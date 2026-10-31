using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CountFile2.Services
{
    public sealed class LlmExtractionOptions
    {
        public string Provider { get; set; } = "Ollama";
        public string Model { get; set; } = "";
        public string LocalBaseUrl { get; set; } = "http://localhost:11434";
        [ScriptIgnore] public string GeminiApiKey { get; set; }
        [ScriptIgnore] public string LocalApiKey { get; set; }
        public string ImageMode { get; set; } = "TextOnly";
        public bool SupportsVision { get; set; }
        public bool AllowLanHttp { get; set; }
        public int ContextTokens { get; set; } = 16384;
        public int OutputTokens { get; set; } = 4096;
        public int TimeoutSeconds { get; set; } = 180;
        public int MaxRequests { get; set; } = 24;
        public int MaxAttempts { get; set; } = 3;
        public int MaxImages { get; set; } = 8;
        public bool UseCache { get; set; } = true;
        public string CacheDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CountFile2", "llm-cache");
        public string ConfigurationIssue()
        {
            if (!new[] { "Gemini", "Ollama", "CompatibleLocal" }.Contains(Provider)) return "Nhà cung cấp LLM không hợp lệ.";
            if (string.IsNullOrWhiteSpace(Model)) return "Chọn Cấu hình LLM và nhập tên model.";
            if (Provider == "Gemini" && string.IsNullOrWhiteSpace(GeminiApiKey)) return "Chưa có API key Gemini. Nhập trong Cấu hình LLM.";
            if (Provider != "Gemini") {
                Uri uri;
                if (!Uri.TryCreate(LocalBaseUrl, UriKind.Absolute, out uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return "Endpoint local phải là URL không chứa credential/query/fragment.";
                if (uri.Scheme != "https" && !(uri.Scheme == "http" && (uri.IsLoopback || AllowLanHttp))) return "Endpoint cần HTTPS; HTTP chỉ dành cho localhost hoặc mạng nội bộ được bật rõ ràng.";
            }
            if (!new[] { "TextOnly", "ImagesOnDemand", "Pdf" }.Contains(ImageMode)) return "Chế độ đầu vào LLM không hợp lệ.";
            if (ImageMode != "TextOnly" && !SupportsVision) return "Chọn model có vision và bật khả năng đọc ảnh; hoặc dùng Chỉ nội dung OCR.";
            if (ImageMode == "Pdf" && Provider != "Gemini") return "PDF đối chiếu trực tiếp hiện chỉ dành cho Gemini. Với local, chọn OCR + ảnh khi cần.";
            if (ContextTokens < 8192 || OutputTokens < 512 || OutputTokens > ContextTokens - 4000) return "Context cần tối thiểu 8192 token và đủ chỗ cho schema, nội dung, đầu ra.";
            if (TimeoutSeconds < 1 || MaxRequests < 1 || MaxAttempts < 1 || MaxAttempts > 3 || MaxImages < 0) return "Giới hạn request/timeout/ảnh không hợp lệ.";
            if (Model.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0 && Provider != "Gemini") return "Chọn model chạy tại local, không dùng tag cloud trong cấu hình local.";
            return null;
        }
        public LlmExtractionOptions Copy()
        {
            var serializer = PostOcrExtractionService.Serializer();
            var result = serializer.Deserialize<LlmExtractionOptions>(serializer.Serialize(this));
            result.GeminiApiKey = GeminiApiKey; result.LocalApiKey = LocalApiKey; return result;
        }
    }
    public static class LlmSettingsStore
    {
        public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CountFile2", "llm-settings.json"); } }
        private sealed class SavedSettings
        {
            public LlmExtractionOptions Options { get; set; }
            public string GeminiSecret { get; set; }
            public string LocalSecret { get; set; }
        }
        public static LlmExtractionOptions Load(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) return new LlmExtractionOptions();
            var saved = PostOcrExtractionService.Serializer().Deserialize<SavedSettings>(File.ReadAllText(path, Encoding.UTF8));
            if (saved == null || saved.Options == null) throw new InvalidDataException("Cấu hình LLM không hợp lệ.");
            saved.Options.GeminiApiKey = Unprotect(saved.GeminiSecret); saved.Options.LocalApiKey = Unprotect(saved.LocalSecret); return saved.Options;
        }
        public static void Save(LlmExtractionOptions options, string path = null)
        {
            path = path ?? DefaultPath; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var saved = new SavedSettings { Options = options, GeminiSecret = Protect(options.GeminiApiKey), LocalSecret = Protect(options.LocalApiKey) };
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, PostOcrExtractionService.Serializer().Serialize(saved), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static string Protect(string value) { return string.IsNullOrEmpty(value) ? null : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
        private static string Unprotect(string value) { return string.IsNullOrEmpty(value) ? null : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
    }
    public sealed class LlmDocumentGroup
    {
        public string id { get; set; }
        public int start_page { get; set; }
        public int end_page { get; set; }
        public bool is_primary { get; set; }
    }
    public sealed class LlmFieldData
    {
        public string key { get; set; }
        public string value { get; set; }
        public string raw_value { get; set; }
        public string state { get; set; }
        public string group_id { get; set; }
        public string signer_id { get; set; }
        public string[] evidence_ids { get; set; }
        public string issue { get; set; }
    }
    public sealed class LlmResponseData
    {
        public LlmDocumentGroup[] document_groups { get; set; }
        public LlmFieldData[] fields { get; set; }
        public int[] requested_image_pages { get; set; }
        public string[] issues { get; set; }
    }
    public sealed class LlmImageInput
    {
        public string Id { get; set; }
        public int Page { get; set; }
        public string Base64 { get; set; }
    }
    public sealed class LlmRequest
    {
        public string Instructions { get; set; }
        public string Text { get; set; }
        public object Schema { get; set; }
        public List<LlmImageInput> Images { get; set; } = new List<LlmImageInput>();
        public string PdfBase64 { get; set; }
        public string[] AllowedEvidenceIds { get; set; } = new string[0];
    }
    public sealed class LlmProviderReply
    {
        public string Json { get; set; }
        public string Model { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
    }
}
