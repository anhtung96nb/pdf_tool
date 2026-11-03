using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CountFile2.Services
{
    public sealed class VietOcrOptions
    {
        public string PythonPath { get; set; } = FindPython();
        public string WorkerPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VietOcr", "worker.py");
        public string EngineName { get; set; } = "VietOCR";
        public string OutputFolderName { get; set; } = "viet-ocr";
        public string SetupPath { get; set; } = "VietOcr/setup.ps1";
        public string Device { get; set; } = "cpu";
        public int Dpi { get; set; } = 300;
        public int Rotation { get; set; }
        public bool Recursive { get; set; } = true;
        public bool WriteText { get; set; }
        public bool WriteMarkdown { get; set; } = true;
        public bool WriteJson { get; set; } = true;
        public bool Overwrite { get; set; }
        public int TimeoutSeconds { get; set; } = 300;
        public bool ReadPageNumbers { get; set; }
        public bool ExtractMetadata { get; set; }
        public LlmExtractionOptions LlmOptions { get; set; }
        public int NumberRightPercent { get; set; } = 20;
        public int NumberTopPercent { get; set; } = 10;

        private static string FindPython()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, ".vietocr-venv", "Scripts", "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "python";
        }
    }
    public sealed class VietOcrResult : IDisposable
    {
        public string InputPath { get; set; }
        public string TextPath { get; set; }
        public string MarkdownPath { get; set; }
        public string JsonPath { get; set; }
        public string ExtractionPath { get; set; }
        public AdministrativeDocument Extraction { get; set; }
        public string Status { get; set; }
        public string Details { get; set; }
        public int PageCount { get; set; }
        public int CompletedPages { get; set; }
        public int ReviewPages { get; set; }
        public int FailedPages { get; set; }
        public int PageNumberReviewPages { get; set; }
        public List<VietOcrPageNumber> PageNumbers { get; private set; } = new List<VietOcrPageNumber>();
        internal string ReviewDataPath { get; set; }
        internal long SourceLength { get; set; }
        internal long SourceWriteTicks { get; set; }
        public void Dispose() { if (ReviewDataPath != null && File.Exists(ReviewDataPath)) File.Delete(ReviewDataPath); ReviewDataPath = null; }
    }
    public sealed class VietOcrProgress
    {
        public string InputPath { get; set; }
        public string Status { get; set; }
        public int TotalFiles { get; set; }
        public int CompletedFiles { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public VietOcrResult Result { get; set; }
    }
    public sealed class VietOcrCanceledException : OperationCanceledException
    {
        public IList<VietOcrResult> Results { get; private set; }
        public VietOcrCanceledException(IList<VietOcrResult> results, CancellationToken token) : base("Đã hủy Việt OCR.", token)
        { Results = results; }
    }

    public sealed class VietOcrService
    {
        public Func<LlmExtractionService> LlmServiceFactory { get; set; } = () => new LlmExtractionService();
        internal static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 }; }

        public Task<IList<VietOcrResult>> ProcessAsync(string input, string output, VietOcrOptions options,
            CancellationToken token, IProgress<VietOcrProgress> progress = null)
        { return Task.Run(() => Process(input, output, options, token, progress), token); }

        private static IEnumerable<string> Scan(string path, bool recursive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            foreach (string file in Directory.GetFiles(path))
                if (string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase)) yield return file;
            if (recursive) foreach (string child in Directory.GetDirectories(path))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    foreach (string file in Scan(child, true, token)) yield return file;
        }
        private IList<VietOcrResult> Process(string input, string output, VietOcrOptions options,
            CancellationToken token, IProgress<VietOcrProgress> progress)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (!options.WriteText && !options.WriteMarkdown && !options.WriteJson && !options.ExtractMetadata) throw new ArgumentException("Chọn Markdown hoặc JSON.");
            if (options.Dpi < 72 || options.Dpi > 600) throw new ArgumentException("DPI phải từ 72 đến 600.");
            if (!new[] { 0, 90, 180, 270 }.Contains(options.Rotation)) throw new ArgumentException("Góc xoay không hợp lệ.");
            if (options.TimeoutSeconds < 1) throw new ArgumentException("Thời gian chờ phải lớn hơn 0.");
            ValidateNumberOptions(options);
            if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Chọn một PDF hoặc thư mục.");
            input = Path.GetFullPath(input.Trim());
            bool folder = Directory.Exists(input);
            if (!folder && (!File.Exists(input) || !string.Equals(Path.GetExtension(input), ".pdf", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Đầu vào phải là file PDF hoặc thư mục có PDF.");
            string root = (folder ? input : Path.GetDirectoryName(input)).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            output = string.IsNullOrWhiteSpace(output) ? Path.Combine(root, options.OutputFolderName) : Path.GetFullPath(output.Trim());
            string[] files = folder ? Scan(input, options.Recursive, token).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() : new[] { input };
            if (files.Length == 0) throw new ArgumentException("Không có PDF trong thư mục đã chọn.");
            if (!File.Exists(options.WorkerPath)) throw new FileNotFoundException("Thiếu worker OCR. Hãy build lại ứng dụng.");
            var results = new List<VietOcrResult>();
            Action<string, int, int, string, VietOcrResult> report = (path, page, pages, state, result) => {
                if (progress != null) progress.Report(new VietOcrProgress { InputPath = path, Page = page, TotalPages = pages,
                    TotalFiles = files.Length, CompletedFiles = results.Count, Status = state, Result = result });
            };
            report(input, 0, 0, "Đang nạp " + options.EngineName + " và bộ phát hiện chữ…", null);
            // One worker/model session for the whole batch. All PDF rendering runs on this background thread.
            VietOcrWorker worker;
            try { worker = new VietOcrWorker(options, token); }
            catch (OperationCanceledException) { throw Canceled(results, files, token); }
            using (worker)
            foreach (string file in files)
            {
                if (token.IsCancellationRequested) throw Canceled(results, files, token);
                string relative = folder ? file.Substring(root.Length) : Path.GetFileName(file);
                string destination = Path.Combine(output, relative) + ".ocr";
                var result = new VietOcrResult { InputPath = file, TextPath = options.WriteText ? destination + ".txt" : null,
                    MarkdownPath = options.WriteMarkdown || options.ExtractMetadata ? destination + ".md" : null,
                    JsonPath = options.WriteJson ? destination + ".json" : null, Status = "Đang xử lý" };
                bool ocrWritten = false;
                try
                {
                    report(file, 0, 0, result.Status, null);
                    AdministrativeDocument previousExtraction = null;
                    if (options.ExtractMetadata && !options.Overwrite && File.Exists(destination + ".metadata.json"))
                        throw new IOException("Kết quả bóc tách đã tồn tại. Chọn Ghi đè hoặc thư mục khác.");
                    if (options.ExtractMetadata && File.Exists(destination + ".metadata.json")) {
                        try { previousExtraction = ExtractionExportService.LoadJson(destination + ".metadata.json").FirstOrDefault(d => string.Equals(d.Source, file, StringComparison.OrdinalIgnoreCase)); }
                        catch (Exception ex) { throw new InvalidDataException("Không đọc được bản bóc tách trước để giữ sửa thủ công. Chọn thư mục đích khác. " + ex.Message, ex); }
                    }
                    Write(file, destination, result, options, worker, token, (p, n) => report(file, p, n, "OCR toàn trang", null));
                    ocrWritten = true;
                    UpdateSummary(result);
                    if (options.ExtractMetadata) {
                        report(file, result.PageCount, result.PageCount, "Bóc tách thông tin văn bản", null);
                        try {
                            result.Extraction = PostOcrExtractionService.FromResult(result, token);
                            if (previousExtraction != null && previousExtraction.PrimaryRangeConfirmed && previousExtraction.Fingerprint == result.Extraction.Fingerprint && previousExtraction.OcrFingerprint == result.Extraction.OcrFingerprint)
                                PostOcrExtractionService.Reextract(result.Extraction, previousExtraction.PrimaryStartPage, previousExtraction.PrimaryEndPage);
                            ExtractionReview.Preserve(previousExtraction, result.Extraction);
                            result.ExtractionPath = destination + ".metadata.json";
                            using (var llm = LlmServiceFactory()) llm.ExtractAsync(result.Extraction, options.LlmOptions, token).GetAwaiter().GetResult();
                            ExtractionExportService.SaveJson(result.ExtractionPath, new[] { result.Extraction });
                            result.Details += " LLM: " + result.Extraction.LlmStatus + ". " + result.Extraction.LlmError;
                        } catch (OperationCanceledException) { throw; }
                        catch (Exception ex) {
                            result.Extraction = new AdministrativeDocument { Source = file, RecordId = PostOcrExtractionService.RecordId(file), PageCount = result.PageCount,
                                Error = "Bóc tách sau OCR: " + ex.Message, LinkedOcrResult = result };
                            result.Details += " Bóc tách chưa hoàn tất: " + ex.Message;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    result.Status = ocrWritten ? "OCR hoàn tất; đã hủy LLM" : "Đã hủy";
                    result.Details = ocrWritten ? "Đã giữ các file OCR. Có thể chạy lại LLM từ nội dung OCR đã lưu." : "Chưa xuất file đang xử lý. Giữ kết quả các PDF trước đó.";
                    if (ocrWritten && result.Extraction != null && result.ExtractionPath != null) {
                        try { ExtractionExportService.SaveJson(result.ExtractionPath, new[] { result.Extraction }); }
                        catch (Exception ex) { result.Details += " Không lưu được trạng thái LLM: " + ex.Message; }
                    }
                    results.Add(result); report(file, result.CompletedPages, result.PageCount, result.Status, result);
                    throw Canceled(results, files, token);
                }
                catch (Exception ex) { result.Status = "Lỗi"; result.Details = ex.Message; }
                results.Add(result); report(file, result.PageCount, result.PageCount, result.Status, result);
            }
            return results.AsReadOnly();
        }
        private static VietOcrCanceledException Canceled(List<VietOcrResult> results, string[] files, CancellationToken token)
        {
            foreach (string file in files.Where(f => !results.Any(r => string.Equals(r.InputPath, f, StringComparison.OrdinalIgnoreCase))))
                results.Add(new VietOcrResult { InputPath = file, Status = "Chưa xử lý", Details = "Tác vụ đã hủy trước khi xử lý PDF này." });
            return new VietOcrCanceledException(results.AsReadOnly(), token);
        }

        private static void Write(string input, string destination, VietOcrResult result, VietOcrOptions options,
            VietOcrWorker worker, CancellationToken token, Action<int, int> report)
        {
            var paths = new List<string>();
            if (options.WriteText) paths.Add(destination + ".txt");
            if (options.WriteMarkdown || options.ExtractMetadata) paths.Add(destination + ".md");
            if (options.WriteJson) paths.Add(destination + ".json");
            if (!options.Overwrite && paths.Any(File.Exists)) throw new IOException("Đầu ra đã tồn tại. Chọn Ghi đè hoặc thư mục khác.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string work = Path.Combine(Path.GetDirectoryName(destination), ".vietocr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var json = Serializer();
            var sourceInfo = new FileInfo(input); result.SourceLength = sourceInfo.Length; result.SourceWriteTicks = sourceInfo.LastWriteTimeUtc.Ticks;
            string sourceHash = PostOcrExtractionService.Fingerprint(input);
            string reviewPath = options.ReadPageNumbers || options.ExtractMetadata ? Path.Combine(Path.GetTempPath(), "CountFile2-vietocr-" + Guid.NewGuid().ToString("N") + ".jsonl") : null;
            try
            {
                using (var pdf = PdfiumViewer.PdfDocument.Load(input))
                using (TextWriter txt = options.WriteText ? new StreamWriter(Path.Combine(work, "text"), false, new UTF8Encoding(false)) : TextWriter.Null)
                using (TextWriter markdown = options.WriteMarkdown || options.ExtractMetadata ? new StreamWriter(Path.Combine(work, "markdown"), false, new UTF8Encoding(false)) : TextWriter.Null)
                using (TextWriter data = options.WriteJson ? new StreamWriter(Path.Combine(work, "json"), false, new UTF8Encoding(false)) : TextWriter.Null)
                using (TextWriter review = reviewPath != null ? new StreamWriter(reviewPath, false, new UTF8Encoding(false)) : TextWriter.Null)
                {
                    result.PageCount = pdf.PageCount;
                    if (pdf.PageCount == 0) throw new InvalidDataException("PDF không có trang.");
                    VietOcrMarkdownExport.WriteHeader(markdown, input, pdf.PageCount);
                    string header = json.Serialize(new { schema_version = 1, source = input, engine = options.EngineName, model = worker.Model,
                        device = options.Device, dpi = options.Dpi, rotation_clockwise = options.Rotation, coordinate_origin = "top_left", page_count = pdf.PageCount,
                        source_bytes = result.SourceLength, source_modified_utc = sourceInfo.LastWriteTimeUtc.ToString("o"),
                        source_sha256 = sourceHash,
                        page_number_options = new { enabled = options.ReadPageNumbers, right_percent = options.NumberRightPercent, top_percent = options.NumberTopPercent } });
                    review.WriteLine(header); data.Write(header.Substring(0, header.Length - 1) + ",\"pages\":[");
                    for (int i = 0; i < pdf.PageCount; i++)
                    {
                        token.ThrowIfCancellationRequested(); report(i + 1, pdf.PageCount);
                        SizeF size = pdf.PageSizes[i];
                        var page = new Dictionary<string, object> { { "page", i + 1 }, { "width_points", size.Width }, { "height_points", size.Height } };
                        try
                        {
                            int width = checked((int)Math.Ceiling(size.Width * options.Dpi / 72.0));
                            int height = checked((int)Math.Ceiling(size.Height * options.Dpi / 72.0));
                            if (width <= 0 || height <= 0 || (long)width * height > 40000000)
                                throw new InvalidDataException("Trang vượt giới hạn 40 megapixel. Hãy giảm DPI.");
                            string imagePath = Path.Combine(work, "page.png");
                            using (var rendered = pdf.Render(i, width, height, options.Dpi, options.Dpi, PdfiumViewer.PdfRenderFlags.ForPrinting))
                            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                            {
                                using (var graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.White); graphics.DrawImage(rendered, 0, 0, width, height); }
                                bitmap.Save(imagePath, ImageFormat.Png);
                            }
                            var recognized = worker.Recognize(imagePath, i + 1, size.Width, size.Height, token);
                            foreach (var item in recognized) page[item.Key] = item.Value;
                            if ((string)page["status"] == "failed") result.FailedPages++;
                            else result.CompletedPages++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            result.FailedPages++; page["status"] = "failed"; page["error"] = ex.Message;
                            page["text"] = ""; page["lines"] = new object[0];
                        }
                        page["ocr_status"] = page["status"];
                        if (options.ReadPageNumbers)
                        {
                            if (!page.ContainsKey("document_page_number")) page["document_page_number"] = new Dictionary<string, object> {
                                { "status", "error" }, { "value", null }, { "confirmed", false }, { "warnings", new[] { "Không nhận được kết quả số trang." } } };
                            var number = VietOcrPageNumber.FromPage(page); result.PageNumbers.Add(number);
                            if (number.NeedsReview) { result.PageNumberReviewPages++; if ((string)page["status"] == "completed") page["status"] = "needs_review"; }
                        }
                        if ((string)page["status"] == "needs_review") result.ReviewPages++;
                        if (i > 0) data.Write(",");
                        string serialized = json.Serialize(page); data.Write(serialized); review.WriteLine(serialized);
                        txt.WriteLine("===== Trang " + (i + 1) + " / " + pdf.PageCount + " [" + page["status"] + "] =====");
                        if (page.ContainsKey("error")) txt.WriteLine("[Lỗi OCR: " + page["error"] + "]");
                        txt.WriteLine(Convert.ToString(page["text"])); txt.WriteLine();
                        VietOcrMarkdownExport.WritePage(markdown, page, pdf.PageCount);
                        txt.Flush(); markdown.Flush(); data.Flush();
                    }
                    data.Write("],\"status\":" + json.Serialize(result.FailedPages > 0 ? "completed_with_errors" : result.ReviewPages > 0 ? "needs_review" : "completed") + "}");
                }
                token.ThrowIfCancellationRequested();
                var finalSource = new FileInfo(input);
                if (finalSource.Length != result.SourceLength || finalSource.LastWriteTimeUtc.Ticks != result.SourceWriteTicks || PostOcrExtractionService.Fingerprint(input) != sourceHash)
                    throw new IOException("PDF thay đổi trong khi OCR. Chạy lại để kết quả khớp nguồn.");
                // Commit selected outputs together, rolling back any replacement if the second file fails.
                Publish(paths, work, options.Overwrite);
                result.ReviewDataPath = reviewPath; reviewPath = null;
            }
            finally { if (reviewPath != null && File.Exists(reviewPath)) File.Delete(reviewPath); if (Directory.Exists(work)) Directory.Delete(work, true); }
        }

        internal static void UpdateSummary(VietOcrResult result)
        {
            result.Status = result.FailedPages > 0 ? "Hoàn tất có lỗi trang" : result.ReviewPages > 0 ? "Cần kiểm tra" : "Hoàn tất";
            result.Details = result.CompletedPages + "/" + result.PageCount + " trang đọc được; " + result.ReviewPages + " cần kiểm tra; " + result.FailedPages +
                " lỗi. " + (result.PageNumbers.Count > 0 ? "Số trang ghi tay: " + result.PageNumberReviewPages + " cần kiểm tra. " : "") +
                "Điểm nhận dạng chưa được hiệu chuẩn; cần đối chiếu bản gốc.";
        }
        internal static void ValidateNumberOptions(VietOcrOptions options)
        {
            if (options.NumberRightPercent < 1 || options.NumberRightPercent > 100 || options.NumberTopPercent < 1 || options.NumberTopPercent > 100)
                throw new ArgumentException("Vùng số trang phải từ 1 đến 100 phần trăm.");
        }
        internal static void Publish(IList<string> paths, string work, bool overwrite)
        {
            var published = new List<string>(); var backups = new Dictionary<string, string>();
            var stagedFiles = new List<string>();
            try
            {
                foreach (string target in paths)
                {
                    // Stage beside each target: the private review cache can be on another drive.
                    string staged = Path.Combine(Path.GetDirectoryName(target), ".vietocr-publish-" + Guid.NewGuid().ToString("N") + ".tmp");
                    stagedFiles.Add(staged); File.Copy(Path.Combine(work, TemporaryName(target)), staged);
                    if (File.Exists(target))
                    {
                        if (!overwrite) throw new IOException("Đầu ra đã tồn tại: " + target);
                        string backup = Path.Combine(work, Guid.NewGuid().ToString("N"));
                        File.Copy(target, backup); backups.Add(target, backup);
                        try { File.Replace(staged, target, null, true); }
                        catch (IOException ex) { throw new IOException("Không thay thế được file: " + target + ". " + ex.Message, ex); }
                    }
                    else File.Move(staged, target);
                    published.Add(target);
                }
            }
            catch
            {
                foreach (string target in published)
                    if (backups.ContainsKey(target)) File.Copy(backups[target], target, true); else File.Delete(target);
                throw;
            }
            finally { foreach (string staged in stagedFiles) if (File.Exists(staged)) File.Delete(staged); }
        }
        private static string TemporaryName(string target)
        {
            switch (Path.GetExtension(target).ToLowerInvariant())
            {
                case ".txt": return "text";
                case ".md": return "markdown";
                case ".json": return "json";
                case ".jsonl": return "review";
                default: throw new InvalidOperationException("Định dạng đầu ra không hợp lệ.");
            }
        }
    }

    internal sealed class VietOcrWorker : IDisposable
    {
        private readonly Process process;
        private StreamWriter input;
        private readonly StringBuilder errors = new StringBuilder();
        private readonly int timeout;
        private int requestId;
        private readonly VietOcrOptions options;
        public string Model { get; private set; }
        private static string Quote(string text)
        { return "\"" + text.Replace("\"", "\\\"").TrimEnd('\\') + "\""; }
        public VietOcrWorker(VietOcrOptions options, CancellationToken token)
        {
            this.options = options;
            timeout = options.TimeoutSeconds;
            process = new Process { StartInfo = new ProcessStartInfo {
                FileName = options.PythonPath, Arguments = "-u " + Quote(options.WorkerPath) + " --device " + Quote(options.Device) + " --rotation " + options.Rotation,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
            process.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            process.StartInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 6000) errors.Remove(0, errors.Length - 6000); } };
            try
            {
                process.Start(); process.BeginErrorReadLine();
                input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
                var ready = Read(token);
                if (!ready.ContainsKey("type") || (string)ready["type"] != "ready" || !ready.ContainsKey("protocol") || Convert.ToInt32(ready["protocol"]) != 1)
                    throw new InvalidDataException("Worker chưa sẵn sàng hoặc không đúng phiên bản giao thức.");
                Model = Convert.ToString(ready["model"]);
                object capabilities;
                if (options.ReadPageNumbers && (!ready.TryGetValue("capabilities", out capabilities) ||
                    !((System.Collections.IEnumerable)capabilities).Cast<object>().Any(c => Convert.ToString(c) == "page_number_v1")))
                    throw new InvalidOperationException("Worker này chưa hỗ trợ đọc số trang viết tay. Hãy cập nhật worker VietOCR.");
            }
            catch (OperationCanceledException) { Dispose(); throw; }
            catch (Exception ex) { Dispose(); throw new InvalidOperationException("Không khởi động được " + options.EngineName + ": " + ex.Message + "\nChạy " + options.SetupPath + " bằng Python 3.11 để cài thư viện và model, hoặc chọn đúng python.exe.", ex); }
        }
        private Dictionary<string, object> Read(CancellationToken token)
        {
            var task = process.StandardOutput.ReadLineAsync();
            try
            {
                if (!task.Wait(checked(timeout * 1000), token)) { Stop(); throw new TimeoutException("VietOCR quá thời gian chờ " + timeout + " giây."); }
            }
            catch (OperationCanceledException) { Stop(); throw; }
            string line = task.GetAwaiter().GetResult();
            if (line == null) { string detail; lock (errors) detail = errors.ToString(); throw new IOException("Worker đã dừng. " + detail); }
            var value = VietOcrService.Serializer().Deserialize<Dictionary<string, object>>(line);
            if (value.ContainsKey("type") && (string)value["type"] == "error") throw new InvalidOperationException(Convert.ToString(value["error"]));
            return value;
        }
        public Dictionary<string, object> Recognize(string path, int page, float width, float height, CancellationToken token, bool numberOnly = false)
        {
            token.ThrowIfCancellationRequested();
            int id = ++requestId;
            input.WriteLine(VietOcrService.Serializer().Serialize(new { id = id, image = path, page = page, width_points = width, height_points = height,
                operation = numberOnly ? "page_number" : "page", page_number = new { enabled = options.ReadPageNumbers,
                    right_percent = options.NumberRightPercent, top_percent = options.NumberTopPercent } }));
            var value = Read(token);
            if (!value.ContainsKey("id") || Convert.ToInt32(value["id"]) != id) throw new InvalidDataException("Sai mã phản hồi từ worker.");
            value.Remove("id"); value.Remove("type"); return value;
        }
        private void Stop() { try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } } catch (InvalidOperationException) { } }
        public void Dispose() { Stop(); if (input != null) input.Dispose(); process.Dispose(); }
    }
}
