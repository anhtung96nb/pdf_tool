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
        public string Text { get; set; }
        public int Page { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Confidence { get; set; }
        public string Method { get; set; }
        public int RotationDegrees { get; set; }
    }
    public sealed class ExtractedField
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string OriginalValue { get; set; }
        public string Warning { get; set; }
        public bool Confirmed { get; set; }
        public List<EvidenceLine> Evidence { get; set; } = new List<EvidenceLine>();
    }
    public sealed class AdministrativeDocument
    {
        public string Source { get; set; }
        public string Fingerprint { get; set; }
        public bool UsedForceOcr { get; set; }
        public int PageCount { get; set; }
        public string Error { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<EvidenceLine> Lines { get; set; } = new List<EvidenceLine>();
        public List<ExtractedField> Fields { get; set; } = new List<ExtractedField>();
        public string Status { get { return Error != null ? "Lỗi" : Fields.All(f => f.Confirmed) ? "Đã xác nhận" : "Cần kiểm tra"; } }
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
        public static readonly string[] FieldNames = { "Trang PDF", "Trang in / tờ số", "Số, ký hiệu", "Số văn bản", "Ký hiệu", "Ngày ban hành", "Cơ quan ban hành", "Cơ quan chủ quản", "Thể loại", "Trích yếu", "Nơi nhận", "Kính gửi", "Người ký", "Chức vụ người ký", "Quyền hạn ký" };
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
                    Extract(result);
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
        private static bool Match(string text, string pattern) { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase); }
        public static void Extract(AdministrativeDocument doc)
        {
            doc.Fields.Clear();
            doc.Warnings.Clear();
            foreach (string name in FieldNames) doc.Fields.Add(new ExtractedField { Name = name, Warning = "Không tìm thấy; cần kiểm tra." });
            Action<string, string, IEnumerable<EvidenceLine>> put = (name, value, evidence) => {
                var f = doc.Fields.First(x => x.Name == name); f.Value = f.OriginalValue = value; f.Evidence = evidence.ToList();
                f.Warning = f.Evidence.Any(l => l.Confidence < .8f) ? "OCR độ tin cậy thấp; cần kiểm tra." : "Chưa được người dùng xác nhận.";
            };
            put("Trang PDF", "1–" + doc.PageCount, new EvidenceLine[0]);
            var head = doc.Lines.Where(l => l.Page == 1 && l.Y < .45f).ToList();
            var numbers = doc.Lines.Where(l => l.Y < .35f && Match(l.Text, @"^\s*S[ốo]\s*[:：]")).ToList();
            var number = numbers.FirstOrDefault(l => l.Page == 1);
            if (numbers.Select(l => l.Page).Distinct().Count() > 1) doc.Warnings.Add("Có số văn bản ở nhiều trang: có thể có văn bản đính kèm. Chưa tự tách tài liệu.");
            if (number != null) {
                string v = Regex.Replace(number.Text, @"^\s*S[ốo]\s*[:：]\s*", "", RegexOptions.IgnoreCase).Trim();
                put("Số, ký hiệu", v, new[] { number }); int slash = v.IndexOf('/');
                if (slash > 0) { put("Số văn bản", v.Substring(0, slash).Trim(), new[] { number }); put("Ký hiệu", v.Substring(slash + 1).Trim(), new[] { number }); }
            }
            var date = head.FirstOrDefault(l => Match(l.Text, @"ngày\s*\d{1,2}\s*tháng\s*\d{1,2}\s*năm\s*\d{4}"));
            if (date != null) {
                var m = Regex.Match(date.Text, @"ngày\s*(\d{1,2})\s*tháng\s*(\d{1,2})\s*năm\s*(\d{4})", RegexOptions.IgnoreCase);
                try { put("Ngày ban hành", new DateTime(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)).ToString("yyyy-MM-dd"), new[] { date }); }
                catch (ArgumentOutOfRangeException) { doc.Warnings.Add("Ngày ban hành không hợp lệ: " + date.Text); }
            }
            var agencies = head.Where(l => l.X < .48f && l.Y < (number == null ? .20f : number.Y) && !Match(l.Text, @"CỘNG H[ÒO]A|Độc lập|Hạnh phúc|^Số:")).ToList();
            if (agencies.Count > 0) {
                int start = agencies.FindLastIndex(l => Match(l.Text, @"^(ỦY BAN|UỶ BAN|BỘ\s|SỞ\s|PHÒNG\s|CỤC\s|CHI CỤC|TRƯỜNG\s|VĂN PHÒNG|TỔNG CỤC|CÔNG TY|TRUNG TÂM|BAN\s|VIỆN\s|ĐẢNG ỦY|HUYỆN ỦY|TỈNH ỦY)"));
                if (start < 0) start = agencies.Count - 1;
                put("Cơ quan ban hành", string.Join("\n", agencies.Skip(start).Select(l => l.Text.Trim())), agencies.Skip(start));
                if (start > 0) put("Cơ quan chủ quản", string.Join("\n", agencies.Take(start).Select(l => l.Text)), agencies.Take(start));
                if (agencies.Count > 2) doc.Warnings.Add("Tên cơ quan có nhiều dòng; kiểm tra cơ quan ban hành/chủ quản.");
            }
            const string types = @"^(QUYẾT ĐỊNH|THÔNG BÁO|BÁO CÁO|TỜ TRÌNH|KẾ HOẠCH|NGHỊ QUYẾT|CHỈ THỊ|QUY CHẾ|QUY ĐỊNH|HƯỚNG DẪN|BIÊN BẢN|CÔNG VĂN|CÔNG ĐIỆN|GIẤY MỜI|HỢP ĐỒNG|CHƯƠNG TRÌNH|PHƯƠNG ÁN|ĐỀ ÁN)(\s|$)";
            var type = head.FirstOrDefault(l => Match(l.Text.Trim(), types));
            if (type != null) {
                put("Thể loại", Regex.Match(type.Text.Trim(), types, RegexOptions.IgnoreCase).Groups[1].Value, new[] { type });
                var summary = head.Where(l => l.Y > type.Y + .005f && l.Y < type.Y + .13f).TakeWhile(l => !Match(l.Text, @"^Căn cứ|^Kính gửi|^QUYẾT ĐỊNH:|^Điều\s+1|^CHỦ TỊCH|^GIÁM ĐỐC")).ToList();
                if (summary.Count > 0) put("Trích yếu", string.Join("\n", summary.Select(l => l.Text)), summary);
            } else {
                var subject = head.FirstOrDefault(l => Match(l.Text, @"^V\s*/\s*v\b|^Về việc"));
                if (subject != null) {
                    var summary = head.Where(l => l.Y >= subject.Y && l.Y < subject.Y + .09f && l.X < .55f).TakeWhile(l => !Match(l.Text, @"^Kính gửi|^Căn cứ")).ToList();
                    put("Thể loại", "Công văn", new[] { subject }); put("Trích yếu", string.Join("\n", summary.Select(l => l.Text)), summary);
                }
            }
            var printed = doc.Lines.Where(l => (l.Y < .06f || l.Y > .92f) && Match(l.Text.Trim(), @"^(Trang\s*)?\d{1,4}$|^(Tờ\s*(số)?\s*[:：]?\s*\d+)" )).ToList();
            if (printed.Count > 0) put("Trang in / tờ số", string.Join("; ", printed.Select(l => "PDF " + l.Page + ": " + l.Text)), printed);
            var greeting = doc.Lines.FirstOrDefault(l => Match(l.Text, @"^Kính gửi\s*:"));
            if (greeting != null) {
                var lines = doc.Lines.Where(l => l.Page == greeting.Page && l.Y >= greeting.Y && l.Y < greeting.Y + .10f).TakeWhile(l => l == greeting || Match(l.Text, @"^[-–•]")).ToList();
                put("Kính gửi", string.Join("\n", lines.Select(l => l.Text)), lines);
            }
            var recipient = doc.Lines.LastOrDefault(l => Match(l.Text, @"^Nơi nhận\s*:"));
            if (recipient != null) {
                var lines = doc.Lines.Where(l => l.Page == recipient.Page && l.Y >= recipient.Y && l.X < .55f).TakeWhile(l => l == recipient || Match(l.Text, @"^[-–•]|^Lưu\s*:")).ToList();
                put("Nơi nhận", string.Join("\n", lines.Select(l => l.Text)), lines);
            }
            var authority = doc.Lines.LastOrDefault(l => l.X > .4f && l.Y > .35f && Match(l.Text, @"^(KT\.|TL\.|TUQ\.|TM\.|Q\.)"));
            if (authority != null) put("Quyền hạn ký", authority.Text, new[] { authority });
            var title = doc.Lines.LastOrDefault(l => l.X > .4f && l.Y > .35f && Match(l.Text, @"CHỦ TỊCH|GIÁM ĐỐC|TRƯỞNG|BÍ THƯ|CHÁNH|TỔNG GIÁM|HIỆU TRƯỞNG") && !Match(l.Text, @"^(KT\.|TL\.|TUQ\.|TM\.)|^[-–•]|^Kính gửi"));
            if (title != null) {
                put("Chức vụ người ký", title.Text, new[] { title });
                var signer = doc.Lines.Where(l => l.Page == title.Page && l.X > .4f && l.Y > title.Y + .025f && l.Y < title.Y + .3f)
                    .LastOrDefault(l => Match(l.Text.Trim(), @"^\p{L}+(\s+\p{L}+){1,5}$") && !Match(l.Text, @"CHỦ TỊCH|GIÁM ĐỐC|TRƯỞNG|BÍ THƯ|CHÁNH|Nơi nhận|Ký bởi|Đã ký"));
                if (signer != null) put("Người ký", signer.Text, new[] { signer });
            }
            doc.Warnings.Add("Một PDF được xử lý như một tài liệu. Các trường tự động cần đối chiếu trước khi xác nhận.");
        }
    }
}
