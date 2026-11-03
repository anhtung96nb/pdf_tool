using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CountFile2.Services
{
    public interface ILlmExtractionProvider
    {
        Task<LlmProviderReply> GenerateAsync(LlmRequest request, LlmExtractionOptions options, CancellationToken token);
    }
    public sealed class LlmApiException : Exception
    {
        public bool Retryable { get; private set; }
        public TimeSpan? RetryAfter { get; private set; }
        public int? HttpStatus { get; private set; }
        public string ErrorCode { get; private set; }
        public LlmApiException(string message, bool retryable = false, TimeSpan? retryAfter = null, int? httpStatus = null, string errorCode = null) : base(message)
        { Retryable = retryable; RetryAfter = retryAfter; HttpStatus = httpStatus; ErrorCode = errorCode; }
        public static string SafeMessage(string message, LlmExtractionOptions options)
        {
            message = message ?? "";
            foreach (var secret in new[] { options.GeminiApiKey, options.LocalApiKey }.Where(s => !string.IsNullOrWhiteSpace(s))) message = message.Replace(secret, "[đã che]");
            message = Regex.Replace(message, @"(?i)(AQ\.[A-Za-z0-9_-]{12,}|AIza[A-Za-z0-9_-]{15,}|Bearer\s+[^\s,;]+|(?:key|api_key|token)=[^\s&]+)", "[đã che]");
            message = Regex.Replace(message, @"[\x00-\x1f\x7f]+", " ").Trim();
            return message.Length > 700 ? message.Substring(0, 700) + "…" : message;
        }
    }
    public sealed class HttpLlmExtractionProvider : ILlmExtractionProvider, IDisposable
    {
        public const string GeminiProtocolVersion = "interactions-v1beta-2";
        private readonly HttpClient client;
        private readonly HttpClient localClient;
        public HttpLlmExtractionProvider(HttpMessageHandler handler = null)
        {
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
            client.Timeout = Timeout.InfiniteTimeSpan;
            // Internal document traffic must not be sent through a system Internet proxy.
            localClient = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) : client;
            localClient.Timeout = Timeout.InfiniteTimeSpan;
        }
        public async Task<LlmProviderReply> GenerateAsync(LlmRequest request, LlmExtractionOptions options, CancellationToken token)
        {
            string issue = options.ConfigurationIssue(); if (issue != null) throw new LlmApiException(issue);
            object body; string url;
            if (options.Provider == "Gemini") {
                url = "https://generativelanguage.googleapis.com/v1beta/interactions";
                var input = new List<object> { new { type = "text", text = request.Text } };
                foreach (var image in request.Images) {
                    input.Add(new { type = "text", text = "SOURCE_IMAGE_ID=" + image.Id + "; PDF_PAGE=" + image.Page });
                    input.Add(new { type = "image", data = image.Base64, mime_type = "image/png" });
                }
                if (request.PdfBase64 != null) input.Add(new { type = "document", data = request.PdfBase64, mime_type = "application/pdf" });
                body = new { model = options.Model, input = input, system_instruction = request.Instructions, store = false, stream = false,
                    generation_config = new { max_output_tokens = options.OutputTokens },
                    response_format = new { type = "text", mime_type = "application/json", schema = request.Schema } };
            } else if (options.Provider == "Ollama") {
                url = options.LocalBaseUrl.TrimEnd('/') + "/api/chat";
                var user = new Dictionary<string, object> { { "role", "user" }, { "content", request.Text + ImageLabels(request) } };
                if (request.Images.Count > 0) user["images"] = request.Images.Select(i => i.Base64).ToArray();
                body = new { model = options.Model, stream = false, format = request.Schema,
                    options = new { temperature = 0, num_ctx = options.ContextTokens, num_predict = options.OutputTokens },
                    messages = new object[] { new { role = "system", content = request.Instructions }, user } };
                if (options.Model.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase)) {
                    var fields = PostOcrExtractionService.Serializer().Deserialize<Dictionary<string, object>>(PostOcrExtractionService.Serializer().Serialize(body));
                    fields["think"] = options.OllamaThink; body = fields;
                }
            } else {
                string root = options.LocalBaseUrl.TrimEnd('/');
                url = root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root + "/chat/completions" : root + "/v1/chat/completions";
                var content = new List<object> { new { type = "text", text = request.Text } };
                foreach (var image in request.Images) {
                    content.Add(new { type = "text", text = "SOURCE_IMAGE_ID=" + image.Id + "; PDF_PAGE=" + image.Page });
                    content.Add(new { type = "image_url", image_url = new { url = "data:image/png;base64," + image.Base64 } });
                }
                body = new { model = options.Model, stream = false, temperature = 0, max_tokens = options.OutputTokens,
                    messages = new object[] { new { role = "system", content = request.Instructions }, new { role = "user", content = (object)content } },
                    response_format = new { type = "json_schema", json_schema = new { name = "vietnamese_document", strict = true, schema = request.Schema } } };
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var message = new HttpRequestMessage(HttpMethod.Post, url)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                if (options.Provider == "Gemini") message.Headers.TryAddWithoutValidation("x-goog-api-key", options.GeminiApiKey);
                else if (!string.IsNullOrWhiteSpace(options.LocalApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.LocalApiKey);
                message.Content = new StringContent(PostOcrExtractionService.Serializer().Serialize(body), Encoding.UTF8, "application/json");
                try {
                    using (var response = await (options.Provider == "Gemini" ? client : localClient).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false)) {
                        string data = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) {
                            int status = (int)response.StatusCode;
                            bool quota = data.IndexOf("perday", StringComparison.OrdinalIgnoreCase) >= 0 || data.IndexOf("billing", StringComparison.OrdinalIgnoreCase) >= 0 || data.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0;
                            bool memory = data.IndexOf("out of memory", StringComparison.OrdinalIgnoreCase) >= 0 || data.IndexOf("not enough memory", StringComparison.OrdinalIgnoreCase) >= 0;
                            TimeSpan? delay = response.Headers.RetryAfter == null ? (TimeSpan?)null : response.Headers.RetryAfter.Delta ?? (response.Headers.RetryAfter.Date.HasValue ? (TimeSpan?)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow) : null);
                            string advice = status == 401 || status == 403 ? "Kiểm tra API key/quyền truy cập." : status == 404 ? "Kiểm tra endpoint và tên model." : status == 400 ? "Kiểm tra model hỗ trợ JSON Schema/vision và giới hạn context." : status == 429 ? quota ? "Đã hết quota/ngân sách." : "Đang giới hạn tốc độ." : status == 503 ? "Model đang quá tải hoặc dịch vụ tạm thời chưa sẵn sàng. Thử lại sau; không cần đổi key chỉ vì lỗi này." : "Máy chủ chưa xử lý được yêu cầu.";
                            if (memory) advice = "Model không đủ RAM/VRAM; giảm context/ảnh hoặc chọn model phù hợp.";
                            string code = null, detail = null;
                            if (options.Provider == "Gemini") {
                                try {
                                    var root = PostOcrExtractionService.Serializer().Deserialize<Dictionary<string, object>>(data);
                                    var error = G(root, "error") as IDictionary<string, object> ?? root;
                                    code = S(error, "status"); detail = S(error, "message");
                                    if (!Regex.IsMatch(code, @"^[A-Z][A-Z0-9_]{1,63}$")) code = null;
                                    detail = LlmApiException.SafeMessage(detail, options);
                                } catch (Exception) { }
                            } else if (options.Provider == "Ollama") {
                                try { detail = LlmApiException.SafeMessage(S(PostOcrExtractionService.Serializer().Deserialize<Dictionary<string, object>>(data), "error"), options); }
                                catch (Exception) { }
                                if (status == 404) advice = "Không tìm thấy endpoint/model Ollama. Kiểm tra URL gốc và tải đúng model bằng ollama pull " + options.Model + ".";
                            }
                            throw new LlmApiException(options.Provider + " HTTP " + status + (string.IsNullOrEmpty(code) ? "" : " (" + code + ")") + ": " + advice +
                                (string.IsNullOrEmpty(detail) ? "" : " Máy chủ: " + detail), !quota && !memory && (status == 429 || status >= 500), delay, status, code);
                        }
                        return Parse(data, options.Provider);
                    }
                } catch (OperationCanceledException) {
                    token.ThrowIfCancellationRequested(); throw new LlmApiException("LLM hết thời gian chờ. Kiểm tra model, tài nguyên hoặc tăng timeout.", true);
                } catch (HttpRequestException ex) {
                    var cause = ex.InnerException; string advice = "Kiểm tra endpoint, mạng, proxy và dịch vụ đang chạy.";
                    var socket = cause as System.Net.Sockets.SocketException;
                    var web = cause as WebException;
                    if (socket != null && (socket.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound || socket.SocketErrorCode == System.Net.Sockets.SocketError.TryAgain) || web != null && web.Status == WebExceptionStatus.NameResolutionFailure)
                        advice = "Không phân giải được tên miền (DNS). Kiểm tra mạng/DNS/proxy.";
                    else if (cause is System.Security.Authentication.AuthenticationException || web != null && (web.Status == WebExceptionStatus.TrustFailure || web.Status == WebExceptionStatus.SecureChannelFailure))
                        advice = "Không thiết lập được HTTPS/TLS. Kiểm tra chứng chỉ, ngày giờ máy và proxy.";
                    throw new LlmApiException("Không kết nối được " + options.Provider + ". " + advice, true);
                }
            }
        }
        public async Task<string[]> ListOllamaModelsAsync(LlmExtractionOptions selected, CancellationToken token)
        {
            var options = new LlmExtractionOptions { LocalBaseUrl = selected.LocalBaseUrl, LocalApiKey = selected.LocalApiKey, AllowLanHttp = selected.AllowLanHttp };
            string issue = options.ConfigurationIssue(); if (issue != null) throw new LlmApiException(issue);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var message = new HttpRequestMessage(HttpMethod.Get, options.LocalBaseUrl.TrimEnd('/') + "/api/tags")) {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                if (!string.IsNullOrWhiteSpace(options.LocalApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.LocalApiKey);
                try {
                    using (var response = await localClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false)) {
                        string text = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw new LlmApiException("Không lấy được danh sách Ollama (HTTP " + (int)response.StatusCode + "). Kiểm tra URL gốc/quyền truy cập.");
                        var data = PostOcrExtractionService.Serializer().Deserialize<Dictionary<string, object>>(text);
                        if (data == null || !data.ContainsKey("models")) throw new LlmApiException("Endpoint không trả danh sách model Ollama hợp lệ.");
                        return A(G(data, "models")).Where(m => string.IsNullOrEmpty(S(m, "remote_host")))
                            .Select(m => S(m, "name")).Where(n => !string.IsNullOrWhiteSpace(n) && n.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) < 0)
                            .Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                    }
                } catch (OperationCanceledException) {
                    token.ThrowIfCancellationRequested(); throw new LlmApiException("Ollama hết thời gian chờ. Khởi chạy Ollama rồi thử lại.");
                } catch (HttpRequestException) { throw new LlmApiException("Không kết nối được Ollama. Khởi chạy Ollama và kiểm tra địa chỉ localhost:11434."); }
                catch (ArgumentException) { throw new LlmApiException("Danh sách model Ollama không phải JSON hợp lệ."); }
            }
        }
        private static string ImageLabels(LlmRequest request)
        { return request.Images.Count == 0 ? "" : "\nẢnh theo thứ tự: " + string.Join("; ", request.Images.Select(i => i.Id + " (trang " + i.Page + ")")); }
        private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken token)
        {
            const int limit = 8 * 1024 * 1024;
            if (content.Headers.ContentLength > limit) throw new LlmApiException("Phản hồi LLM quá lớn.");
            using (var stream = await content.ReadAsStreamAsync().ConfigureAwait(false)) using (var buffer = new MemoryStream()) {
                var bytes = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false)) > 0) {
                    if (buffer.Length + count > limit) throw new LlmApiException("Phản hồi LLM quá lớn.");
                    buffer.Write(bytes, 0, count);
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }
        private static LlmProviderReply Parse(string data, string provider)
        {
            Dictionary<string, object> root;
            try { root = PostOcrExtractionService.Serializer().Deserialize<Dictionary<string, object>>(data); }
            catch (Exception) { throw new LlmApiException("Máy chủ không trả JSON hợp lệ."); }
            if (root == null || root.ContainsKey("error")) throw new LlmApiException("Máy chủ trả lỗi hoặc phản hồi rỗng.");
            var result = new LlmProviderReply { Model = S(root, "model") };
            if (provider == "Gemini") {
                if (S(root, "status") != "completed") throw new LlmApiException("Gemini chưa hoàn tất hoặc đã chặn yêu cầu; không dùng kết quả một phần.");
                var output = new List<string>();
                foreach (var step in A(G(root, "steps"))) if (S(step, "type") == "model_output") foreach (var part in A(G(step, "content"))) {
                    if (S(part, "type") != "text") throw new LlmApiException("Gemini trả loại nội dung không phải JSON văn bản.");
                    output.Add(S(part, "text"));
                }
                result.Json = string.Join("", output); var usage = G(root, "usage") as IDictionary<string, object>;
                result.InputTokens = Number(usage, "total_input_tokens"); result.OutputTokens = Number(usage, "total_output_tokens");
            } else if (provider == "Ollama") {
                if (!Equals(G(root, "done"), true) || S(root, "done_reason") != "stop") throw new LlmApiException("Ollama chưa hoàn tất hoặc chạm giới hạn đầu ra; không dùng JSON một phần.");
                var message = G(root, "message") as IDictionary<string, object>;
                result.Json = message == null ? null : S(message, "content"); result.InputTokens = Number(root, "prompt_eval_count"); result.OutputTokens = Number(root, "eval_count");
            } else {
                var choice = A(G(root, "choices")).SingleOrDefault();
                if (choice == null || S(choice, "finish_reason") != "stop") throw new LlmApiException("Model chưa hoàn tất hoặc đầu ra bị giới hạn/chặn.");
                var message = G(choice, "message") as IDictionary<string, object>;
                if (message == null || !string.IsNullOrEmpty(S(message, "refusal"))) throw new LlmApiException("Model không trả kết quả trích xuất.");
                result.Json = S(message, "content"); var usage = G(root, "usage") as IDictionary<string, object>;
                result.InputTokens = Number(usage, "prompt_tokens"); result.OutputTokens = Number(usage, "completion_tokens");
            }
            if (string.IsNullOrWhiteSpace(result.Json)) throw new LlmApiException("LLM trả nội dung rỗng.");
            return result;
        }
        private static string S(IDictionary<string, object> d, string key) { return PostOcrExtractionService.Str(d, key); }
        private static object G(IDictionary<string, object> d, string key) { return PostOcrExtractionService.Get(d, key); }
        private static IEnumerable<IDictionary<string, object>> A(object value) { return PostOcrExtractionService.Items(value).OfType<IDictionary<string, object>>(); }
        private static long? Number(IDictionary<string, object> d, string key) { object value = d == null ? null : G(d, key); return value == null ? (long?)null : Convert.ToInt64(value); }
        public void Dispose() { if (!ReferenceEquals(localClient, client)) localClient.Dispose(); client.Dispose(); }
    }
}
