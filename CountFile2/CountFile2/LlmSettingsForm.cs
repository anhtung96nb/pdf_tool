using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CountFile2.Services;

namespace CountFile2
{
    public sealed class LlmSettingsForm : Form
    {
        private readonly ComboBox provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly ComboBox model = new ComboBox { Width = 360, DropDownStyle = ComboBoxStyle.DropDown, AccessibleName = "Tên model LLM" };
        private readonly TextBox endpoint = new TextBox { Width = 360, AccessibleName = "Endpoint LLM local" };
        private readonly TextBox geminiKey = new TextBox { Width = 360, UseSystemPasswordChar = true, AccessibleName = "API key Gemini" };
        private readonly TextBox localKey = new TextBox { Width = 360, UseSystemPasswordChar = true, AccessibleName = "Khóa máy chủ local nếu có" };
        private readonly ComboBox inputMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly CheckBox vision = new CheckBox { Text = "Model có hỗ trợ đọc ảnh", AutoSize = true };
        private readonly CheckBox lan = new CheckBox { Text = "Cho phép HTTP mạng nội bộ (không mã hóa)", AutoSize = true };
        private readonly CheckBox cache = new CheckBox { Text = "Dùng cache cùng nguồn/model/prompt", AutoSize = true };
        private readonly CheckBox think = new CheckBox { Text = "Bật thinking cho Qwen3 trên Ollama (chậm hơn)", AutoSize = true };
        private readonly Button loadModels = new Button { Text = "Lấy danh sách model Ollama", AutoSize = true };
        private readonly NumericUpDown context = Number(8192, 1048576, 16384);
        private readonly NumericUpDown output = Number(512, 65536, 2048);
        private readonly NumericUpDown timeout = Number(1, 3600, 180);
        private readonly NumericUpDown calls = Number(1, 200, 24);
        private readonly NumericUpDown images = Number(0, 100, 8);
        private readonly TextBox status = new TextBox { Dock = DockStyle.Bottom, Height = 96, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control };
        private readonly Button test = new Button { Text = "Kiểm tra API / JSON", AutoSize = true };
        private readonly Button save = new Button { Text = "Lưu cấu hình", AutoSize = true };
        private CancellationTokenSource cancellation;
        private readonly LlmExtractionOptions original;
        public LlmExtractionOptions Options { get; private set; }
        public Func<HttpLlmExtractionProvider> ConnectionFactory { get; set; } = () => new HttpLlmExtractionProvider();
        private static NumericUpDown Number(int min, int max, int value) { return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 120 }; }
        public LlmSettingsForm(LlmExtractionOptions options)
        {
            original = (options ?? new LlmExtractionOptions()).Copy();
            Text = "Cấu hình trích xuất bằng LLM"; ClientSize = new Size(740, 760); Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterParent;
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(10) };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            provider.Items.AddRange(new object[] { "Gemini API (Google)", "Ollama local", "API local tương thích OpenAI" });
            inputMode.Items.AddRange(new object[] { "Markdown OCR (văn bản)", "Markdown OCR + ảnh khi cần", "Markdown OCR + PDF (Gemini)" });
            Add(rows, "Nhà cung cấp", provider); Add(rows, "Model (nhập tên/tag chính xác)", model); Add(rows, "Địa chỉ local / nội bộ", endpoint);
            var localTools = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(470, 0) };
            var ollamaPreset = new Button { Text = "Dùng Ollama · qwen3.5:4b", AutoSize = true };
            localTools.Controls.AddRange(new Control[] { ollamaPreset, loadModels }); Add(rows, "Ollama", localTools);
            Add(rows, "API key Gemini", geminiKey); Add(rows, "Token local (nếu server yêu cầu)", localKey); Add(rows, "Dữ liệu đầu vào", inputMode);
            Add(rows, "Khả năng model", vision); Add(rows, "Kết nối nội bộ", lan); Add(rows, "Context thực tế (token)", context);
            Add(rows, "Giới hạn đầu ra (token)", output); Add(rows, "Chờ mỗi request (giây)", timeout); Add(rows, "Tổng request tối đa / PDF", calls);
            Add(rows, "Ảnh tối đa / PDF", images); Add(rows, "Chạy lại", cache);
            Add(rows, "Suy luận Ollama", think);
            var buttons = new FlowLayoutPanel { AutoSize = true }; var close = new Button { Text = "Đóng", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { test, save, close }); Add(rows, "", buttons);
            Controls.Add(rows); Controls.Add(status); CancelButton = close;
            provider.SelectedIndex = original.Provider == "Gemini" ? 0 : original.Provider == "CompatibleLocal" ? 2 : 1;
            model.Text = original.Model; endpoint.Text = original.LocalBaseUrl; geminiKey.Text = original.GeminiApiKey; localKey.Text = original.LocalApiKey;
            inputMode.SelectedIndex = original.ImageMode == "ImagesOnDemand" ? 1 : original.ImageMode == "Pdf" ? 2 : 0;
            vision.Checked = original.SupportsVision; lan.Checked = original.AllowLanHttp; cache.Checked = original.UseCache;
            think.Checked = original.OllamaThink;
            Set(context, original.ContextTokens); Set(output, original.OutputTokens); Set(timeout, original.TimeoutSeconds); Set(calls, original.MaxRequests); Set(images, original.MaxImages);
            provider.SelectedIndexChanged += (s, e) => EnabledFields(); EnabledFields();
            endpoint.TextChanged += (s, e) => { if (endpoint.Text != original.LocalBaseUrl && localKey.Text.Length > 0) { localKey.Clear(); status.Text = "Endpoint local đã đổi: nhập lại token dành cho server này nếu cần."; } };
            save.Click += (s, e) => {
                try { Options = Read(); LlmSettingsStore.Save(Options); DialogResult = DialogResult.OK; Close(); }
                catch (Exception) { status.Text = "Không lưu được cấu hình/khóa theo tài khoản Windows. Kiểm tra quyền ghi thư mục ứng dụng."; }
            };
            test.Click += async (s, e) => await CheckConnectionAsync();
            loadModels.Click += async (s, e) => await LoadOllamaModelsAsync();
            ollamaPreset.Click += (s, e) => {
                provider.SelectedIndex = 1; endpoint.Text = "http://localhost:11434"; model.Text = "qwen3.5:4b";
                localKey.Clear(); inputMode.SelectedIndex = 0; vision.Checked = think.Checked = lan.Checked = false;
                context.Value = 16384; output.Value = 4096; timeout.Value = 300;
                status.Text = "Đã chọn OCR → Markdown → Ollama qwen3.5:4b. Lấy danh sách model, kiểm tra API rồi lưu cấu hình.";
            };
            FormClosing += (s, e) => { if (cancellation != null) cancellation.Cancel(); };
            status.Text = "Luồng: PDF → OCR → Markdown → LLM → JSON / Excel. Ollama mặc định: qwen3.5:4b, localhost:11434, không cần key. Model phải có sẵn trên Ollama. Cấu hình Gemini gửi nội dung đã chọn lên Google.";
        }
        public async Task LoadOllamaModelsAsync()
        {
            if (cancellation != null) return;
            if (provider.SelectedIndex != 1) { status.Text = "Chọn Ollama local để lấy danh sách model."; return; }
            var selected = Read(); cancellation = new CancellationTokenSource(); test.Enabled = save.Enabled = loadModels.Enabled = false;
            status.Text = "Đang đọc các model local đã cài trên Ollama…";
            try {
                using (var connection = ConnectionFactory()) {
                    string[] names = await connection.ListOllamaModelsAsync(selected, cancellation.Token);
                    if (IsDisposed) return;
                    string typed = model.Text; model.Items.Clear(); model.Items.AddRange(names); model.Text = typed;
                    status.Text = names.Length == 0 ? "Chưa có model local. Chạy: ollama pull qwen3.5:4b, rồi lấy lại danh sách."
                        : "Tìm thấy " + names.Length + " model local. Chọn tên/tag trong danh sách rồi kiểm tra API / JSON.";
                }
            } catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Đã hủy lấy danh sách model."; }
            catch (LlmApiException ex) { if (!IsDisposed) status.Text = LlmApiException.SafeMessage(ex.Message, selected); }
            catch (Exception) { if (!IsDisposed) status.Text = "Không đọc được danh sách Ollama. Kiểm tra URL gốc và dịch vụ đang chạy."; }
            finally { cancellation.Dispose(); cancellation = null; if (!IsDisposed) { test.Enabled = save.Enabled = true; EnabledFields(); } }
        }
        public async Task CheckConnectionAsync()
        {
            if (cancellation != null) return;
            var selected = Read(); string issue = selected.ConfigurationIssue(); if (issue != null) { status.Text = issue; return; }
            cancellation = new CancellationTokenSource(); test.Enabled = save.Enabled = loadModels.Enabled = false;
            status.Text = "Đang kiểm tra bằng JSON mẫu, không gửi tài liệu của bạn…";
            try {
                using (var connection = ConnectionFactory()) {
                    LlmProviderReply result = null;
                    for (int attempt = 1; attempt <= selected.MaxAttempts; attempt++) {
                        try {
                            result = await connection.GenerateAsync(new LlmRequest { Instructions = "Return JSON only: {\"status\":\"ok\"}", Text = "Connection and JSON schema test.",
                                Schema = new { type = "object", properties = new { status = new { type = "string", @enum = new[] { "ok" } } }, required = new[] { "status" }, additionalProperties = false } }, selected, cancellation.Token);
                            break;
                        } catch (LlmApiException ex) {
                            if (!ex.Retryable || attempt >= selected.MaxAttempts) throw;
                            var delay = ex.RetryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                            if (delay > TimeSpan.FromSeconds(selected.TimeoutSeconds)) throw;
                            if (!IsDisposed) status.Text = LlmApiException.SafeMessage(ex.Message, selected) + Environment.NewLine + "Đang chờ thử lại " + (attempt + 1) + "/" + selected.MaxAttempts + ".";
                            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, cancellation.Token);
                        }
                    }
                    var value = PostOcrExtractionService.Serializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(result.Json);
                    if (!IsDisposed) status.Text = value != null && value.Count == 1 && PostOcrExtractionService.Str(value, "status") == "ok"
                        ? "API/JSON mẫu thành công: " + (result.Model ?? selected.Model) + ". Khả năng đọc ảnh và độ chính xác cần kiểm tra với tài liệu mẫu."
                        : "API có phản hồi nhưng JSON chưa đúng mẫu; chưa xác nhận kết nối/schema thành công.";
                }
            } catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Đã hủy kiểm tra."; }
            catch (LlmApiException ex) { if (!IsDisposed) status.Text = LlmApiException.SafeMessage(ex.Message, selected); }
            catch (Exception) { if (!IsDisposed) status.Text = "API có phản hồi nhưng không đọc được JSON mẫu. Kiểm tra giao thức/model đã chọn."; }
            finally { cancellation.Dispose(); cancellation = null; if (!IsDisposed) { test.Enabled = save.Enabled = true; EnabledFields(); } }
        }
        private static void Set(NumericUpDown control, int value) { control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, value)); }
        private static void Add(TableLayoutPanel panel, string text, Control control) {
            int r = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = text, AutoSize = true, Padding = new Padding(0, 6, 0, 8) }, 0, r); panel.Controls.Add(control, 1, r);
        }
        private void EnabledFields() { bool cloud = provider.SelectedIndex == 0; endpoint.Enabled = localKey.Enabled = lan.Enabled = !cloud; geminiKey.Enabled = cloud; loadModels.Enabled = cancellation == null && provider.SelectedIndex == 1; think.Enabled = provider.SelectedIndex == 1; }
        private LlmExtractionOptions Read() {
            var result = original.Copy(); result.Provider = provider.SelectedIndex == 0 ? "Gemini" : provider.SelectedIndex == 2 ? "CompatibleLocal" : "Ollama";
            result.Model = model.Text.Trim(); result.LocalBaseUrl = endpoint.Text.Trim(); result.GeminiApiKey = geminiKey.Text.Trim(); result.LocalApiKey = localKey.Text.Trim();
            result.ImageMode = inputMode.SelectedIndex == 1 ? "ImagesOnDemand" : inputMode.SelectedIndex == 2 ? "Pdf" : "TextOnly";
            result.SupportsVision = vision.Checked; result.AllowLanHttp = lan.Checked; result.UseCache = cache.Checked; result.ContextTokens = (int)context.Value;
            result.OllamaThink = think.Checked;
            result.OutputTokens = (int)output.Value; result.TimeoutSeconds = (int)timeout.Value; result.MaxRequests = (int)calls.Value; result.MaxImages = (int)images.Value;
            return result;
        }
    }
}
