using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CountFile2.Services
{
    // This adapter consumes saved recognition data; it never launches an OCR engine.
    public static class PostOcrExtractionService
    {
        internal static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 150 }; }
        internal static string Str(IDictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
        internal static IEnumerable<object> Items(object value)
        { return value is IEnumerable && !(value is string) ? ((IEnumerable)value).Cast<object>() : Enumerable.Empty<object>(); }
        internal static object Get(IDictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) ? value : null; }
        public static string Fingerprint(string path)
        { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream)); }
        private static string HashText(string value)
        { using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        public static string RecordId(string path)
        { return BitConverter.ToString(Convert.FromBase64String(HashText(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "").Substring(0, 16); }
        public static void ValidateCurrentSource(AdministrativeDocument doc)
        {
            if (string.IsNullOrEmpty(doc.Fingerprint)) return;
            bool valid = false;
            try { valid = File.Exists(doc.Source) && Fingerprint(doc.Source) == doc.Fingerprint; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!valid) {
                doc.SourceVerified = false;
                const string warning = "Nguồn thay đổi hoặc không truy cập được: cần đối chiếu lại PDF với kết quả đã lưu.";
                if (!doc.Warnings.Contains(warning)) {
                    doc.Warnings.Add(warning); doc.SourceWarnings.Add(warning);
                    foreach (var f in doc.Fields.Where(f => f.Confirmed && f.Name != "Số trang ghi tay")) {
                        f.History.Add(new FieldRevision { Value = f.Value, Confirmed = f.Confirmed, AtUtc = DateTime.UtcNow.ToString("o"), Reason = warning });
                        f.Confirmed = false; f.RequiresReview = true; f.Warning = warning;
                        f.ConfirmedAtUtc = f.ConfirmedBy = null;
                    }
                    doc.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                }
            }
        }
        public static List<string> DiscoverJson(string input, bool recursive, CancellationToken token)
        {
            if (File.Exists(input)) return new List<string> { Path.GetFullPath(input) };
            if (!Directory.Exists(input)) throw new DirectoryNotFoundException("Không tìm thấy nguồn JSON OCR.");
            var files = new List<string>(); var pending = new Stack<string>(); pending.Push(Path.GetFullPath(input));
            while (pending.Count > 0) {
                token.ThrowIfCancellationRequested(); string dir = pending.Pop();
                files.AddRange(Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".ocr.json", StringComparison.OrdinalIgnoreCase)));
                if (recursive) foreach (string child in Directory.EnumerateDirectories(dir))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
            return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static AdministrativeDocument FromJson(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var root = Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (root == null || !root.ContainsKey("pages")) throw new InvalidDataException("Chọn JSON OCR có pages (không phải JSON trích xuất).");
            var doc = Parse(root, Items(root["pages"]).Select(p => (IDictionary<string, object>)p).ToList(), path, token);
            string markdown = Path.ChangeExtension(path, ".md");
            if (File.Exists(markdown)) LlmMarkdownInput.Attach(doc, markdown, token);
            return doc;
        }
        public static AdministrativeDocument FromText(string path, string pdfPath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string text = File.ReadAllText(path, Encoding.UTF8); pdfPath = Path.GetFullPath(pdfPath);
            int count; using (var pdf = PdfiumViewer.PdfDocument.Load(pdfPath)) count = pdf.PageCount;
            var doc = new AdministrativeDocument { Source = pdfPath, RecordId = RecordId(pdfPath), Engine = "Văn bản thuần", PageCount = count,
                PrimaryEndPage = count, Fingerprint = Fingerprint(pdfPath), OcrFingerprint = HashText(text), OcrSourcePath = Path.GetFullPath(path) };
            doc.SourceWarnings.Add("MD/TXT được ghép với PDF do người dùng chọn; thiếu bằng chứng tọa độ và dấu vân tay nguồn OCR. Cần đối chiếu.");
            if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)) {
                LlmMarkdownInput.Attach(doc, path, token);
                for (int p = 1; p <= count; p++) doc.Pages.Add(new OcrPageMetadata { Page = p, NumberStatus = "disabled", OcrStatus = "text_only" });
                ExtractionMetadata.Initialize(doc); return doc;
            }
            int page = 1, index = 0;
            foreach (string raw in text.Split('\n')) {
                token.ThrowIfCancellationRequested();
                var marker = System.Text.RegularExpressions.Regex.Match(raw, @"^(?:##\s*|=====\s*)Trang\s+(\d+)\s*/", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (marker.Success) { page = int.Parse(marker.Groups[1].Value); if (page < 1 || page > count) throw new InvalidDataException("Trang trong MD/TXT vượt PDF đã chọn."); continue; }
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string value = raw.Trim();
                doc.Lines.Add(new EvidenceLine { Id = page + ":text:" + (++index), Page = page, RawText = raw, Text = Normalize(value), HasGeometry = false, Method = "MD/TXT", Confidence = 0 });
            }
            for (int p = 1; p <= count; p++) doc.Pages.Add(new OcrPageMetadata { Page = p, NumberStatus = "disabled", OcrStatus = "text_only" });
            ExtractionMetadata.Initialize(doc); return doc;
        }
        public static AdministrativeDocument FromResult(VietOcrResult result, CancellationToken token)
        {
            if (result.Status == "Lỗi" || result.Status == "Đã hủy" || result.Status == "Chưa xử lý" || result.ReviewDataPath == null && result.JsonPath == null)
                return new AdministrativeDocument { Source = result.InputPath, RecordId = RecordId(result.InputPath), PageCount = result.PageCount,
                    Error = result.Status == "Lỗi" ? result.Details : null, Pending = result.Status == "Đã hủy" || result.Status == "Chưa xử lý", LinkedOcrResult = result };
            AdministrativeDocument doc;
            if (result.ReviewDataPath != null && File.Exists(result.ReviewDataPath)) {
                using (var reader = new StreamReader(result.ReviewDataPath, Encoding.UTF8)) {
                    var serializer = Serializer(); var header = serializer.Deserialize<Dictionary<string, object>>(reader.ReadLine());
                    var pages = new List<IDictionary<string, object>>(); string line;
                    while ((line = reader.ReadLine()) != null) { token.ThrowIfCancellationRequested(); pages.Add(serializer.Deserialize<Dictionary<string, object>>(line)); }
                    doc = Parse(header, pages, result.JsonPath, token);
                }
            } else doc = FromJson(result.JsonPath, token);
            if (result.MarkdownPath != null) LlmMarkdownInput.Attach(doc, result.MarkdownPath, token);
            doc.LinkedOcrResult = result;
            ExtractionReview.Preserve(result.Extraction, doc); result.Extraction = doc;
            return doc;
        }
        private static AdministrativeDocument Parse(IDictionary<string, object> root, IList<IDictionary<string, object>> pages, string jsonPath, CancellationToken token)
        {
            if (Convert.ToInt32(Get(root, "schema_version")) != 1) throw new InvalidDataException("Phiên bản JSON OCR chưa được hỗ trợ.");
            string source = Str(root, "source");
            if (string.IsNullOrWhiteSpace(source) || !source.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("JSON thiếu PDF nguồn.");
            if (!Path.IsPathRooted(source)) source = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", source);
            source = Path.GetFullPath(source);
            int count = Convert.ToInt32(Get(root, "page_count"));
            if (count < 1 || pages.Count != count) throw new InvalidDataException("Số trang JSON không khớp page_count.");
            if (Str(root, "coordinate_origin") != "top_left") throw new InvalidDataException("Hệ tọa độ OCR chưa được hỗ trợ.");
            int rotation = Convert.ToInt32(Get(root, "rotation_clockwise"));
            if (!new[] { 0, 90, 180, 270 }.Contains(rotation)) throw new InvalidDataException("Góc xoay OCR không hợp lệ.");
            var doc = new AdministrativeDocument { Source = source, RecordId = RecordId(source), Engine = Str(root, "engine"),
                OcrSourcePath = jsonPath, CoordinateFrame = "pdf_original", ReadingRotation = rotation, PageCount = count, PrimaryEndPage = count, Fingerprint = Str(root, "source_sha256") };
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(doc.Fingerprint)) doc.SourceWarnings.Add("JSON cũ chưa có SHA-256: nguồn PDF chưa được kiểm chứng.");
            else if (!File.Exists(source)) doc.SourceWarnings.Add("Không tìm thấy PDF nguồn để đối chiếu.");
            else {
                try { doc.SourceVerified = Fingerprint(source) == doc.Fingerprint; if (!doc.SourceVerified) doc.SourceWarnings.Add("Nguồn thay đổi: PDF không khớp dấu vân tay trong OCR. Hãy OCR lại."); }
                catch (IOException ex) { doc.SourceWarnings.Add("Không kiểm chứng được PDF: " + ex.Message); }
                catch (UnauthorizedAccessException ex) { doc.SourceWarnings.Add("Không kiểm chứng được PDF: " + ex.Message); }
            }
            var canonical = new StringBuilder(Str(root, "engine") + "|" + Str(root, "model") + "|" + rotation + "|");
            var seen = new HashSet<int>();
            foreach (var p in pages) {
                token.ThrowIfCancellationRequested(); int number = Convert.ToInt32(Get(p, "page"));
                if (number < 1 || number > count || !seen.Add(number)) throw new InvalidDataException("Thứ tự trang JSON trùng hoặc không hợp lệ.");
                double w = Convert.ToDouble(Get(p, "width_points")), h = Convert.ToDouble(Get(p, "height_points"));
                if (w <= 0 || h <= 0 || double.IsInfinity(w) || double.IsInfinity(h) || double.IsNaN(w) || double.IsNaN(h)) throw new InvalidDataException("Kích thước trang JSON không hợp lệ.");
                var pm = new OcrPageMetadata { Page = number, OcrStatus = Str(p, "ocr_status") == "" ? Str(p, "status") : Str(p, "ocr_status"), Error = Str(p, "error") };
                if (pm.OcrStatus == "failed") doc.SourceWarnings.Add("Trang PDF " + number + " lỗi OCR: " + pm.Error);
                var metadata = Get(p, "document_page_number") as IDictionary<string, object>;
                if (metadata != null) ReadNumber(pm, metadata, w, h, doc.Engine);
                else pm.NumberStatus = "disabled";
                doc.Pages.Add(pm);
                canonical.Append(number).Append('|').Append(w.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(h.ToString("R", CultureInfo.InvariantCulture))
                    .Append('|').Append(pm.OcrStatus).Append('|').Append(Str(p, "text")).Append('|').Append(Serializer().Serialize(Get(p, "lines")));
                int index = 0;
                foreach (var item in Items(Get(p, "lines"))) {
                    var line = item as IDictionary<string, object>; if (line == null) throw new InvalidDataException("Dòng OCR không hợp lệ.");
                    string raw = Str(line, "raw_text"); if (raw == "") raw = Str(line, "text");
                    var evidence = new EvidenceLine { Id = number + ":" + (++index), RawText = raw, Text = Normalize(raw), Page = number,
                        Method = doc.Engine, Confidence = Get(line, "confidence") == null ? 0 : Convert.ToSingle(Get(line, "confidence"), CultureInfo.InvariantCulture), RotationDegrees = 0 };
                    var points = Points(Get(line, "polygon_points"));
                    if (points.Count >= 3) {
                        var normal = points.Select(v => new PointF((float)(v.X / w), (float)(v.Y / h))).ToList();
                        if (normal.Any(v => v.X < -.002 || v.Y < -.002 || v.X > 1.002 || v.Y > 1.002)) throw new InvalidDataException("Tọa độ dòng vượt trang PDF.");
                        SetBox(evidence, normal, false); SetBox(evidence, normal.Select(v => Upright(v, rotation)).ToList(), true);
                    } else { evidence.HasGeometry = false; doc.SourceWarnings.Add("Trang " + number + " có dòng thiếu tọa độ; cần kiểm tra bố cục."); }
                    if (!string.IsNullOrWhiteSpace(evidence.Text)) doc.Lines.Add(evidence);
                }
                if (!doc.Lines.Any(l => l.Page == number) && !string.IsNullOrWhiteSpace(Str(p, "text"))) {
                    doc.SourceWarnings.Add("Trang " + number + " chỉ có văn bản thuần, không có tọa độ.");
                    foreach (string line in Str(p, "text").Split('\n')) if (!string.IsNullOrWhiteSpace(line)) doc.Lines.Add(new EvidenceLine {
                        Id = number + ":text:" + (++index), RawText = line, Text = Normalize(line), Page = number, Method = doc.Engine + " (văn bản thuần)", HasGeometry = false });
                }
            }
            doc.SourceWarnings = doc.SourceWarnings.Distinct().ToList();
            doc.OcrFingerprint = HashText(canonical.ToString());
            ExtractionMetadata.Initialize(doc);
            return doc;
        }
        private static string Normalize(string value)
        { return System.Text.RegularExpressions.Regex.Replace(value.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim(); }
        private static List<PointF> Points(object value)
        {
            return Items(value).Select(v => Items(v).ToArray()).Where(v => v.Length == 2).Select(v => new PointF(
                Convert.ToSingle(v[0], CultureInfo.InvariantCulture), Convert.ToSingle(v[1], CultureInfo.InvariantCulture))).ToList();
        }
        internal static PointF Upright(PointF p, int rotation)
        { return rotation == 90 ? new PointF(1 - p.Y, p.X) : rotation == 180 ? new PointF(1 - p.X, 1 - p.Y) : rotation == 270 ? new PointF(p.Y, 1 - p.X) : p; }
        private static void SetBox(EvidenceLine line, IList<PointF> points, bool layout)
        {
            float x = points.Min(v => v.X), y = points.Min(v => v.Y), w = points.Max(v => v.X) - x, h = points.Max(v => v.Y) - y;
            if (layout) { line.LayoutX = x; line.LayoutY = y; line.LayoutWidth = w; line.LayoutHeight = h; }
            else { line.X = x; line.Y = y; line.Width = w; line.Height = h; }
        }
        private static void ReadNumber(OcrPageMetadata pm, IDictionary<string, object> metadata, double w, double h, string engine)
        {
            pm.NumberCandidates.Clear(); pm.NumberHistory.Clear();
            pm.HandwrittenNumber = Str(metadata, "value"); pm.RawHandwrittenNumber = Str(metadata, "raw_text");
            // History contains the original machine reading after a manual correction.
            var first = Items(Get(metadata, "history")).OfType<IDictionary<string, object>>().FirstOrDefault();
            if (first != null) pm.RawHandwrittenNumber = Str(first, "raw_text");
            pm.NumberStatus = Str(metadata, "status"); pm.NumberConfirmed = Get(metadata, "confirmed") != null && Convert.ToBoolean(Get(metadata, "confirmed"));
            pm.NumberConfirmedAtUtc = Str(metadata, "confirmed_at_utc");
            pm.NumberScore = Get(metadata, "confidence") == null ? (double?)null : Convert.ToDouble(Get(metadata, "confidence"), CultureInfo.InvariantCulture);
            pm.NumberWarning = string.Join("; ", Items(Get(metadata, "warnings")).Select(Convert.ToString));
            foreach (var old in Items(Get(metadata, "history")).OfType<IDictionary<string, object>>()) pm.NumberHistory.Add(new FieldRevision {
                Value = Str(old, "value"), Confirmed = Get(old, "confirmed") != null && Convert.ToBoolean(Get(old, "confirmed")),
                AtUtc = Str(old, "confirmed_at_utc"), Reason = "Lịch sử nhãn tại nguồn OCR", User = Str(old, "source") });
            int ci = 0;
            foreach (var c in Items(Get(metadata, "candidates")).OfType<IDictionary<string, object>>()) foreach (var reading in Items(Get(c, "readings")).OfType<IDictionary<string, object>>()) {
                var candidate = new FieldCandidate { Id = pm.Page + ":number:" + (++ci), Value = Str(reading, "value"), RawValue = Str(reading, "raw_text"),
                    Reason = "Đọc vùng số: " + Str(reading, "variant") + " " + Str(reading, "error") };
                var pts = Points(Get(c, "polygon_points"));
                if (pts.Count >= 3) {
                    var line = new EvidenceLine { Id = candidate.Id, Page = pm.Page, Text = candidate.Value, RawText = candidate.RawValue,
                        Method = engine + " vùng số", Confidence = Get(reading, "confidence") == null ? 0 : Convert.ToSingle(Get(reading, "confidence"), CultureInfo.InvariantCulture) };
                    SetBox(line, pts.Select(p => new PointF((float)(p.X / w), (float)(p.Y / h))).ToList(), false); candidate.Evidence.Add(line);
                }
                pm.NumberCandidates.Add(candidate);
            }
            if (pm.HandwrittenNumber.Any(c => c < '0' || c > '9')) { pm.NumberWarning += " Nhãn có ký tự ngoài 0–9."; pm.NumberConfirmed = false; }
            var points = Points(Get(metadata, "polygon_points")); if (points.Count < 3) points = Points(Get(metadata, "roi_polygon_points"));
            if (points.Count >= 3) {
                var line = new EvidenceLine { Id = pm.Page + ":handwritten", Page = pm.Page, Text = pm.HandwrittenNumber, RawText = pm.RawHandwrittenNumber,
                    Confidence = (float)(pm.NumberScore ?? 0), Method = engine + " vùng số trang" };
                SetBox(line, points.Select(p => new PointF((float)(p.X / w), (float)(p.Y / h))).ToList(), false); pm.NumberEvidence.Add(line);
            }
        }
        public static void SynchronizePageNumbers(AdministrativeDocument doc)
        {
            var linked = doc.LinkedOcrResult; if (linked == null) return;
            bool changed = false;
            foreach (var number in linked.PageNumbers) {
                var page = doc.Pages.FirstOrDefault(p => p.Page == number.PdfPage); if (page == null) continue;
                if (page.HandwrittenNumber != number.Value || page.NumberConfirmed != number.Confirmed || page.NumberStatus != number.State) changed = true;
                page.NumberEvidence.Clear(); ReadNumber(page, number.Metadata, number.WidthPoints, number.HeightPoints, doc.Engine);
            }
            ExtractionMetadata.UpdatePageNumbers(doc);
            if (changed) doc.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        }
        public static VietOcrResult OpenNumberReview(AdministrativeDocument doc)
        {
            if (doc.LinkedOcrResult != null) return doc.LinkedOcrResult;
            if (string.IsNullOrWhiteSpace(doc.OcrSourcePath) || !File.Exists(doc.OcrSourcePath)) throw new InvalidOperationException("Cần mở file JSON OCR gốc để sửa nhãn số trang dùng chung.");
            if (!doc.SourceVerified || Fingerprint(doc.Source) != doc.Fingerprint) throw new InvalidOperationException("PDF nguồn chưa khớp dữ liệu OCR. Hãy OCR lại trước khi sửa số trang.");
            var serializer = Serializer(); var header = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(doc.OcrSourcePath, Encoding.UTF8));
            var pages = Items(header["pages"]).Cast<Dictionary<string, object>>().OrderBy(p => Convert.ToInt32(p["page"])).ToList();
            header.Remove("pages"); header.Remove("status");
            string cache = Path.Combine(Path.GetTempPath(), "CountFile2-vietocr-" + Guid.NewGuid().ToString("N") + ".jsonl");
            var info = new FileInfo(doc.Source);
            var result = new VietOcrResult { InputPath = doc.Source, JsonPath = doc.OcrSourcePath, PageCount = doc.PageCount,
                SourceLength = info.Length, SourceWriteTicks = info.LastWriteTimeUtc.Ticks, ReviewDataPath = cache, Extraction = doc };
            string md = Path.ChangeExtension(doc.OcrSourcePath, ".md"); if (File.Exists(md)) result.MarkdownPath = md;
            try {
                using (var writer = new StreamWriter(cache, false, new UTF8Encoding(false))) {
                    writer.WriteLine(serializer.Serialize(header));
                    foreach (var p in pages) {
                        if (!p.ContainsKey("ocr_status")) p["ocr_status"] = Get(p, "status");
                        if (!p.ContainsKey("document_page_number")) p["document_page_number"] = new Dictionary<string, object> { { "status", "disabled" }, { "confirmed", false }, { "value", null } };
                        result.PageNumbers.Add(VietOcrPageNumber.FromPage(p));
                        if (Str(p, "ocr_status") == "failed") result.FailedPages++; else result.CompletedPages++;
                        writer.WriteLine(serializer.Serialize(p));
                    }
                }
                doc.LinkedOcrResult = result; VietOcrService.UpdateSummary(result); return result;
            } catch { result.Dispose(); throw; }
        }
        public static void Reextract(AdministrativeDocument doc, int start, int end)
        {
            if (start < 1 || end < start || end > doc.PageCount) throw new ArgumentException("Phạm vi trang không hợp lệ.");
            var previous = Serializer().Deserialize<AdministrativeDocument>(Serializer().Serialize(doc));
            doc.PrimaryStartPage = start; doc.PrimaryEndPage = end; doc.PrimaryRangeConfirmed = true;
            ExtractionMetadata.Initialize(doc);
            // A different primary document requires fresh confirmations; preserve edits for comparison.
            if (previous.PrimaryStartPage != start || previous.PrimaryEndPage != end) previous.OcrFingerprint += ":range-changed";
            ExtractionReview.Preserve(previous, doc);
        }
    }
}
