using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Tesseract;
using Path = System.IO.Path;

namespace CountFile2.Services
{
    public sealed class EvidenceLine
    {
        public string Id { get; set; }
        public string RawText { get; set; }
        public string MarkdownText { get; set; }
        public bool HasGeometry { get; set; } = true;
        public float? LayoutX { get; set; }
        public float? LayoutY { get; set; }
        public float? LayoutWidth { get; set; }
        public float? LayoutHeight { get; set; }
        public string Text { get; set; }
        public int Page { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Confidence { get; set; }
        public string Method { get; set; }
        public string SourceKind { get; set; } = "ocr_line";
        public int RotationDegrees { get; set; }
    }
    public sealed class ExtractedField
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public bool HasManualValue { get; set; }
        public string OriginalValue { get; set; }
        public string Warning { get; set; }
        public bool Confirmed { get; set; }
        public bool NotApplicable { get; set; }
        public bool RequiresReview { get; set; }
        public string NormalizedValue { get; set; }
        public string ConfirmedAtUtc { get; set; }
        public string ConfirmedBy { get; set; }
        public List<FieldCandidate> Candidates { get; set; } = new List<FieldCandidate>();
        public List<FieldRevision> History { get; set; } = new List<FieldRevision>();
        public string Status { get { return Confirmed ? (NotApplicable ? "Không áp dụng" : "Đã xác nhận") :
            string.IsNullOrWhiteSpace(Value) ? "Không tìm thấy" : RequiresReview ? "Cần kiểm tra" : "Tìm thấy, chưa xác nhận"; } }
        public List<EvidenceLine> Evidence { get; set; } = new List<EvidenceLine>();
    }
    public sealed class AdministrativeDocument
    {
        public string RecordId { get; set; }
        public string Engine { get; set; }
        public string OcrSourcePath { get; set; }
        public string OcrFingerprint { get; set; }
        public string MarkdownPath { get; set; }
        public string MarkdownFingerprint { get; set; }
        public string RuleVersion { get; set; }
        public string ExtractionProvider { get; set; }
        public string ExtractionModel { get; set; }
        public string LlmStatus { get; set; }
        public string LlmError { get; set; }
        public string ExtractionCacheKey { get; set; }
        public string PromptVersion { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public int LlmRequestCount { get; set; }
        public List<int> AnalyzedPages { get; set; } = new List<int>();
        public List<LlmDocumentGroup> DocumentGroups { get; set; } = new List<LlmDocumentGroup>();
        public string UpdatedAtUtc { get; set; }
        public bool SourceVerified { get; set; }
        public string CoordinateFrame { get; set; }
        public int ReadingRotation { get; set; }
        public int PrimaryStartPage { get; set; } = 1;
        public int PrimaryEndPage { get; set; }
        public bool PrimaryRangeConfirmed { get; set; }
        public bool MultipleDocuments { get; set; }
        public bool Pending { get; set; }
        public List<string> SourceWarnings { get; set; } = new List<string>();
        public List<OcrPageMetadata> Pages { get; set; } = new List<OcrPageMetadata>();
        public List<SignatureBlock> Signers { get; set; } = new List<SignatureBlock>();
        [System.Web.Script.Serialization.ScriptIgnore]
        public VietOcrResult LinkedOcrResult { get; set; }
        public string Source { get; set; }
        public string Fingerprint { get; set; }
        public bool UsedForceOcr { get; set; }
        public int PageCount { get; set; }
        public string Error { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<EvidenceLine> Lines { get; set; } = new List<EvidenceLine>();
        public List<ExtractedField> Fields { get; set; } = new List<ExtractedField>();
        public string Status { get { return Pending ? "Chưa xử lý" : Error != null ? "Lỗi" :
            LlmStatus == "not_configured" ? "LLM chưa cấu hình" : LlmStatus == "failed" ? "Lỗi LLM" : LlmStatus == "cancelled" ? "Đã hủy LLM" :
            LlmStatus == "running" ? "LLM đang phân tích" : LlmStatus == "pending" ? "Chờ LLM" :
            LlmStatus == "needs_review" ? "Cần kiểm tra" :
            (!SourceVerified && Engine != null || MultipleDocuments && !PrimaryRangeConfirmed || Warnings.Any(w => w.StartsWith("Nguồn thay đổi"))) ? "Cần kiểm tra" :
            Fields.Count > 0 && Fields.All(f => f.Confirmed) ? "Đã xác nhận" :
            Fields.Any(f => !f.Confirmed && (f.RequiresReview || string.IsNullOrWhiteSpace(f.Value))) ? "Cần kiểm tra" : "Trích xuất xong, chưa xác nhận"; } }
    }
    public sealed class ExtractionProgress
    {
        public string File { get; set; }
        public int Page { get; set; }
        public int Pages { get; set; }
    }
    public interface IOcrEngine : IDisposable
    {
        List<EvidenceLine> Recognize(Bitmap image, int page, CancellationToken token);
    }
    public sealed class VietnameseOcrEngine : IOcrEngine
    {
        private readonly TesseractEngine engine;
        public VietnameseOcrEngine(string dataPath) { engine = new TesseractEngine(dataPath, "vie", EngineMode.Default); }
        public List<EvidenceLine> Recognize(Bitmap image, int pageNumber, CancellationToken token)
        {
            var best = RecognizeUpright(image, pageNumber, token);
            double bestScore = Score(best);
            if (best.Count > 0 && best.Average(l => l.Confidence) >= .80 && best.Count(l => l.Text.Contains(" ")) > best.Count * .65 && best.Sum(l => l.Text.Count(char.IsLetter)) >= 35) return best;
            for (int turn = 1; turn <= 3; turn++) {
                token.ThrowIfCancellationRequested();
                using (var rotated = new Bitmap(image)) {
                    rotated.RotateFlip(turn == 1 ? RotateFlipType.Rotate90FlipNone : turn == 2 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                    var candidate = RecognizeUpright(rotated, pageNumber, token); double score = Score(candidate);
                    if (score > bestScore * 1.05) { best = candidate; bestScore = score; foreach (var line in best) line.RotationDegrees = turn * 90; }
                }
            }
            return best;
        }
        private static double Score(List<EvidenceLine> lines) {
            return lines.Count == 0 ? 0 : lines.Average(l => l.Confidence) * Math.Log(1 + lines.Sum(l => l.Text.Count(char.IsLetter)))
                * (.5 + .5 * Math.Min(1, lines.Average(l => l.Text.Split(' ').Length) / 3));
        }
        private List<EvidenceLine> RecognizeUpright(Bitmap image, int pageNumber, CancellationToken token)
        {
            var result = new List<EvidenceLine>();
            using (var pix = PixConverter.ToPix(image))
            using (var page = engine.Process(pix, PageSegMode.Auto))
            using (var iterator = page.GetIterator())
            {
                iterator.Begin();
                do {
                    token.ThrowIfCancellationRequested();
                    Rect box; string text = iterator.GetText(PageIteratorLevel.Word);
                    if (string.IsNullOrWhiteSpace(text) || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out box)) continue;
                    result.Add(new EvidenceLine { Text = text.Trim(), Page = pageNumber, X = (float)box.X1 / image.Width,
                        Y = (float)box.Y1 / image.Height, Width = (float)box.Width / image.Width, Height = (float)box.Height / image.Height,
                        Confidence = iterator.GetConfidence(PageIteratorLevel.Word) / 100f, Method = "OCR tiếng Việt" });
                } while (iterator.Next(PageIteratorLevel.Word));
            }
            return AdministrativeExtractionService.MergeLines(result);
        }
        public void Dispose() { engine.Dispose(); }
    }
    public sealed class AdministrativeExtractionService
    {
        internal static List<EvidenceLine> MergeLines(List<EvidenceLine> chunks)
        {
            // Cluster by vertical centre, then split wide horizontal gaps into separate columns.
            var bands = new List<List<EvidenceLine>>();
            foreach (var c in chunks.OrderBy(c => c.Y + c.Height / 2)) {
                var band = bands.LastOrDefault(b => Math.Abs(b[0].Y + b[0].Height / 2 - c.Y - c.Height / 2) < Math.Max(.006f, c.Height * .6f));
                if (band == null) bands.Add(new List<EvidenceLine> { c }); else band.Add(c);
            }
            var rows = new List<EvidenceLine>();
            foreach (var band in bands) {
                EvidenceLine row = null;
                foreach (var c in band.OrderBy(c => c.X)) {
                    if (row == null || c.X - (row.X + row.Width) > .035f) {
                        row = new EvidenceLine { Text = c.Text, Page = c.Page, X = c.X, Y = c.Y, Width = c.Width, Height = c.Height, Method = c.Method, Confidence = c.Confidence }; rows.Add(row);
                    } else {
                        row.Text += " " + c.Text; row.Width = Math.Max(row.Width, c.X + c.Width - row.X);
                        float top = Math.Min(row.Y, c.Y), bottom = Math.Max(row.Y + row.Height, c.Y + c.Height); row.Y = top; row.Height = bottom - top;
                        row.Confidence = Math.Min(row.Confidence, c.Confidence);
                    }
                }
            }
            return rows;
        }
        public static readonly string[] FieldNames = { "Trang PDF", "Trang in / tờ số", "Số trang ghi tay", "Số, ký hiệu", "Số văn bản", "Ký hiệu", "Ngày ban hành", "Cơ quan ban hành", "Cơ quan chủ quản", "Thể loại", "Tiêu đề", "Trích yếu", "Nơi nhận", "Kính gửi", "Người ký", "Chức vụ người ký", "Quyền hạn ký" };
        public string TessDataPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        public bool ForceOcr { get; set; }
        public static List<string> Discover(string input, bool recursive, CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(input)) {
                if (!Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Chỉ nhận file PDF.");
                return new List<string> { Path.GetFullPath(input) };
            }
            if (!Directory.Exists(input)) throw new DirectoryNotFoundException("Đường dẫn nguồn không tồn tại.");
            var files = new List<string>(); var pending = new Stack<string>(); pending.Push(Path.GetFullPath(input));
            while (pending.Count > 0) {
                token.ThrowIfCancellationRequested();
                string dir = pending.Pop();
                foreach (string file in Directory.EnumerateFiles(dir)) {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) files.Add(file);
                }
                if (recursive) foreach (string child in Directory.EnumerateDirectories(dir))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
            files.Sort(StringComparer.OrdinalIgnoreCase); return files;
        }
        public AdministrativeDocument Process(string file, CancellationToken token, IProgress<ExtractionProgress> progress = null)
        {
            var result = new AdministrativeDocument { Source = file };
            IOcrEngine ocr = null;
            try {
                using (var reader = new PdfReader(file))
                using (var pdf = PdfiumViewer.PdfDocument.Load(file)) {
                    result.PageCount = reader.NumberOfPages;
                    for (int p = 1; p <= reader.NumberOfPages; p++) {
                        token.ThrowIfCancellationRequested();
                        var lines = new List<EvidenceLine>();
                        if (!ForceOcr && reader.GetPageRotation(p) == 0) {
                            var bounds = reader.GetCropBox(p);
                            var listener = new TextListener(p, bounds.Left, bounds.Bottom, bounds.Width, bounds.Height);
                            PdfTextExtractor.GetTextFromPage(reader, p, listener); lines = listener.GetLines();
                        }
                        // Sparse, broken or rotated text layers use OCR. This remains a heuristic, reviewed in the UI.
                        string joined = string.Join(" ", lines.Select(l => l.Text));
                        if (ForceOcr || joined.Count(char.IsLetter) < 35 || joined.Contains("\ufffd") || joined.Any(c => char.IsControl(c))) {
                            if (ocr == null) ocr = new VietnameseOcrEngine(TessDataPath);
                            var size = pdf.PageSizes[p - 1]; int w = (int)Math.Ceiling(size.Width * 300 / 72), h = (int)Math.Ceiling(size.Height * 300 / 72);
                            if ((long)w * h > 40000000) throw new InvalidDataException("Trang vượt 40 megapixel.");
                            using (var image = pdf.Render(p - 1, w, h, 300, 300, PdfiumViewer.PdfRenderFlags.ForPrinting))
                            using (var bitmap = new Bitmap(image)) lines = ocr.Recognize(bitmap, p, token);
                        }
                        result.Lines.AddRange(lines.OrderBy(l => l.Y).ThenBy(l => l.X));
                        if (progress != null) progress.Report(new ExtractionProgress { File = file, Page = p, Pages = result.PageCount });
                    }
                    ExtractionMetadata.Initialize(result);
                }
            } catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Error = ex.Message; }
            finally { if (ocr != null) ocr.Dispose(); }
            return result;
        }
        private sealed class TextListener : ITextExtractionStrategy
        {
            private readonly int page; private readonly float left, bottom, width, height;
            private readonly List<EvidenceLine> chunks = new List<EvidenceLine>();
            public TextListener(int p, float l, float b, float w, float h) { page = p; left = l; bottom = b; width = w; height = h; }
            public void BeginTextBlock() { } public void EndTextBlock() { } public void RenderImage(ImageRenderInfo info) { }
            public void RenderText(TextRenderInfo info) {
                string text = info.GetText(); if (string.IsNullOrWhiteSpace(text)) return;
                var a = info.GetAscentLine(); var d = info.GetDescentLine();
                float x = Math.Min(a.GetStartPoint()[0], d.GetStartPoint()[0]), y = Math.Max(a.GetStartPoint()[1], a.GetEndPoint()[1]);
                chunks.Add(new EvidenceLine { Text = text, Page = page, X = (x - left) / width, Y = 1 - (y - bottom) / height,
                    Width = Math.Abs(a.GetEndPoint()[0] - x) / width, Height = Math.Abs(y - d.GetStartPoint()[1]) / height,
                    Confidence = 1, Method = "Lớp chữ PDF" });
            }
            public string GetResultantText() { return string.Join("\n", GetLines().Select(l => l.Text)); }
            public List<EvidenceLine> GetLines() {
                var rows = new List<EvidenceLine>();
                foreach (var c in chunks.OrderBy(c => c.Y).ThenBy(c => c.X)) {
                    var row = rows.LastOrDefault(r => Math.Abs(r.Y - c.Y) < Math.Max(.004f, c.Height * .35f) && c.X >= r.X && c.X - (r.X + r.Width) < .035f);
                    if (row == null) rows.Add(c);
                    else { row.Text += " " + c.Text; row.Width = Math.Max(row.Width, c.X + c.Width - row.X); row.Height = Math.Max(row.Height, c.Height); }
                }
                return rows;
            }
        }
    }
}
