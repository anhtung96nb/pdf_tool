using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CountFile2.Services
{
    public sealed class VietOcrPageNumber
    {
        public int PdfPage { get; private set; }
        public float WidthPoints { get; private set; }
        public float HeightPoints { get; private set; }
        public string Value { get { return Get("value"); } }
        public string RawText { get { return Get("raw_text"); } }
        public string State { get { return Get("status"); } }
        public bool Confirmed { get { object v; return Metadata.TryGetValue("confirmed", out v) && Convert.ToBoolean(v); } }
        public bool NeedsReview { get { return !Confirmed && State != "recognized" && State != "disabled"; } }
        public string StateLabel { get { return Confirmed ? "Đã xác nhận" : State == "recognized" ? "Máy đọc được" : State == "not_found" ? "Không tìm thấy" : State == "error" ? "Lỗi đọc số" : State == "disabled" ? "Đã tắt" : "Cần kiểm tra"; } }
        public double? Score { get { object value; return Metadata.TryGetValue("confidence", out value) && value != null ? (double?)Convert.ToDouble(value, CultureInfo.InvariantCulture) : null; } }
        public string Warning { get { object value; return Metadata.TryGetValue("warnings", out value) && value is IEnumerable ? string.Join("; ", ((IEnumerable)value).Cast<object>().Select(Convert.ToString)) : ""; } }
        internal Dictionary<string, object> Metadata { get; set; }
        private string Get(string key) { object value; return Metadata.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
        internal static VietOcrPageNumber FromPage(IDictionary<string, object> page)
        {
            return new VietOcrPageNumber { PdfPage = Convert.ToInt32(page["page"]), WidthPoints = Convert.ToSingle(page["width_points"], CultureInfo.InvariantCulture),
                HeightPoints = Convert.ToSingle(page["height_points"], CultureInfo.InvariantCulture), Metadata = (Dictionary<string, object>)page["document_page_number"] };
        }
    }

    public static class VietOcrPageNumberReview
    {
        public static Task ConfirmAsync(VietOcrResult result, int pdfPage, string value, bool absent = false, CancellationToken token = default(CancellationToken))
        {
            if (!absent && (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 32 || value.Trim().Any(c => c < '0' || c > '9')))
                throw new ArgumentException("Số trang chỉ gồm chữ số 0–9, tối đa 32 ký tự. Chọn Không có số nếu trang không đánh số.");
            var row = result.PageNumbers.Single(p => p.PdfPage == pdfPage);
            var metadata = new Dictionary<string, object>(row.Metadata);
            metadata["value"] = absent ? null : value.Trim(); metadata["status"] = absent ? "not_found" : "recognized";
            metadata["confirmed"] = true; metadata["source"] = "manual"; metadata["confirmed_at_utc"] = DateTime.UtcNow.ToString("o");
            return ReplaceAsync(result, pdfPage, metadata, token);
        }

        public static Task ReplaceAsync(VietOcrResult result, int pdfPage, Dictionary<string, object> metadata, CancellationToken token)
        { return Task.Run(() => Rewrite(result, pdfPage, metadata, token), token); }

        private static void Rewrite(VietOcrResult result, int pdfPage, Dictionary<string, object> replacement, CancellationToken token)
        {
            if (result.ReviewDataPath == null || !File.Exists(result.ReviewDataPath)) throw new InvalidOperationException("Không còn dữ liệu kiểm tra của phiên này. Hãy OCR lại file.");
            var row = result.PageNumbers.Single(p => p.PdfPage == pdfPage);
            // Keep machine output and previous confirmations when rereading/revising a label.
            var updated = new Dictionary<string, object>(replacement);
            var history = new List<object>(); object previous;
            if (row.Metadata.TryGetValue("history", out previous) && previous is IEnumerable) history.AddRange(((IEnumerable)previous).Cast<object>());
            var old = new Dictionary<string, object>(row.Metadata); old.Remove("history"); history.Add(old); updated["history"] = history;
            string parent = Path.GetDirectoryName(result.JsonPath ?? result.MarkdownPath ?? result.TextPath);
            string work = Path.Combine(parent, ".vietocr-review-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            var serializer = VietOcrService.Serializer(); int reviewCount = 0, numberReviewCount = 0;
            try
            {
                using (var input = new StreamReader(result.ReviewDataPath, Encoding.UTF8))
                using (var review = new StreamWriter(Path.Combine(work, "review"), false, new UTF8Encoding(false)))
                using (TextWriter data = result.JsonPath != null ? new StreamWriter(Path.Combine(work, "json"), false, new UTF8Encoding(false)) : TextWriter.Null)
                using (TextWriter md = result.MarkdownPath != null ? new StreamWriter(Path.Combine(work, "markdown"), false, new UTF8Encoding(false)) : TextWriter.Null)
                using (TextWriter txt = result.TextPath != null ? new StreamWriter(Path.Combine(work, "text"), false, new UTF8Encoding(false)) : TextWriter.Null)
                {
                    string header = input.ReadLine(); if (header == null) throw new InvalidDataException("Dữ liệu kiểm tra rỗng.");
                    review.WriteLine(header); data.Write(header.Substring(0, header.Length - 1) + ",\"pages\":[");
                    VietOcrMarkdownExport.WriteHeader(md, result.InputPath, result.PageCount);
                    for (int i = 0; i < result.PageCount; i++)
                    {
                        token.ThrowIfCancellationRequested(); string line = input.ReadLine();
                        if (line == null) throw new InvalidDataException("Thiếu trang trong dữ liệu kiểm tra.");
                        var page = serializer.Deserialize<Dictionary<string, object>>(line);
                        if (Convert.ToInt32(page["page"]) == pdfPage) page["document_page_number"] = updated;
                        var number = VietOcrPageNumber.FromPage(page);
                        page["status"] = page["ocr_status"];
                        if (number.NeedsReview) { numberReviewCount++; if ((string)page["status"] == "completed") page["status"] = "needs_review"; }
                        if ((string)page["status"] == "needs_review") reviewCount++;
                        string serialized = serializer.Serialize(page); review.WriteLine(serialized); if (i > 0) data.Write(","); data.Write(serialized);
                        VietOcrMarkdownExport.WritePage(md, page, result.PageCount);
                        txt.WriteLine("===== Trang " + page["page"] + " / " + result.PageCount + " [" + page["status"] + "] =====");
                        if (page.ContainsKey("error")) txt.WriteLine("[Lỗi OCR: " + page["error"] + "]");
                        txt.WriteLine(Convert.ToString(page["text"])); txt.WriteLine();
                    }
                    data.Write("],\"status\":" + serializer.Serialize(result.FailedPages > 0 ? "completed_with_errors" : reviewCount > 0 ? "needs_review" : "completed") + "}");
                }
                token.ThrowIfCancellationRequested();
                var paths = new List<string>(); if (result.TextPath != null) paths.Add(result.TextPath); if (result.MarkdownPath != null) paths.Add(result.MarkdownPath);
                if (result.JsonPath != null) paths.Add(result.JsonPath); paths.Add(result.ReviewDataPath);
                VietOcrService.Publish(paths, work, true);
                row.Metadata = updated; result.ReviewPages = reviewCount; result.PageNumberReviewPages = numberReviewCount; VietOcrService.UpdateSummary(result);
            }
            finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
        }

        public static Task<Dictionary<string, object>> RereadAsync(VietOcrResult result, int pdfPage, VietOcrOptions options, CancellationToken token)
        {
            return Task.Run(() => {
                VietOcrService.ValidateNumberOptions(options); options.ReadPageNumbers = true; ValidateSource(result);
                if (options.Dpi < 72 || options.Dpi > 600) throw new ArgumentException("DPI phải từ 72 đến 600.");
                string imagePath = Path.Combine(Path.GetTempPath(), "CountFile2-number-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    using (var worker = new VietOcrWorker(options, token))
                    using (var pdf = PdfiumViewer.PdfDocument.Load(result.InputPath))
                    {
                        token.ThrowIfCancellationRequested(); var size = pdf.PageSizes[pdfPage - 1];
                        int w = checked((int)Math.Ceiling(size.Width * options.Dpi / 72.0)), h = checked((int)Math.Ceiling(size.Height * options.Dpi / 72.0));
                        if (w <= 0 || h <= 0 || (long)w * h > 40000000) throw new InvalidOperationException("Trang quá lớn; hãy giảm DPI.");
                        using (var image = pdf.Render(pdfPage - 1, w, h, options.Dpi, options.Dpi, PdfiumViewer.PdfRenderFlags.ForPrinting)) image.Save(imagePath, ImageFormat.Png);
                        return (Dictionary<string, object>)worker.Recognize(imagePath, pdfPage, size.Width, size.Height, token, true)["document_page_number"];
                    }
                }
                finally { if (File.Exists(imagePath)) File.Delete(imagePath); }
            }, token);
        }

        internal static void ValidateSource(VietOcrResult result)
        {
            var source = new FileInfo(result.InputPath);
            if (!source.Exists || source.Length != result.SourceLength || source.LastWriteTimeUtc.Ticks != result.SourceWriteTicks)
                throw new InvalidOperationException("PDF nguồn đã thay đổi hoặc bị di chuyển. Hãy OCR lại trước khi đối chiếu số trang.");
        }

        public static Bitmap RenderRegion(string path, int pdfPage, VietOcrOptions options, VietOcrPageNumber number = null)
        {
            using (var pdf = PdfiumViewer.PdfDocument.Load(path))
            {
                var size = pdf.PageSizes[pdfPage - 1];
                int w = Math.Max(1, (int)Math.Ceiling(size.Width * 150 / 72.0)), h = Math.Max(1, (int)Math.Ceiling(size.Height * 150 / 72.0));
                if ((long)w * h > 16000000) throw new InvalidOperationException("Trang quá lớn để xem vùng số.");
                using (var image = pdf.Render(pdfPage - 1, w, h, 150, 150, PdfiumViewer.PdfRenderFlags.ForPrinting))
                using (var bitmap = new Bitmap(image))
                {
                    object region;
                    if (number != null && number.Metadata.TryGetValue("roi_polygon_points", out region) && ((IEnumerable)region).Cast<object>().Any())
                    {
                        var pts = Points(region, w / size.Width, h / size.Height);
                        int x = Math.Max(0, (int)pts.Min(p => p.X)), y = Math.Max(0, (int)pts.Min(p => p.Y));
                        int rw = Math.Min(w - x, (int)Math.Ceiling(pts.Max(p => p.X)) - x), rh = Math.Min(h - y, (int)Math.Ceiling(pts.Max(p => p.Y)) - y);
                        if (rw < 1 || rh < 1) throw new InvalidDataException("Tọa độ vùng số không hợp lệ.");
                        var crop = bitmap.Clone(new Rectangle(x, y, rw, rh), PixelFormat.Format24bppRgb);
                        object polygon;
                        if (number.Metadata.TryGetValue("polygon_points", out polygon) && ((IEnumerable)polygon).Cast<object>().Count() >= 3)
                            using (var graphics = Graphics.FromImage(crop)) using (var pen = new Pen(Color.Red, 2))
                                graphics.DrawPolygon(pen, Points(polygon, w / size.Width, h / size.Height).Select(p => new PointF(p.X - x, p.Y - y)).ToArray());
                        object rotation;
                        if (number.Metadata.TryGetValue("rotation_clockwise", out rotation))
                        {
                            int angle = Convert.ToInt32(rotation);
                            if (angle != 0) crop.RotateFlip(angle == 90 ? RotateFlipType.Rotate90FlipNone : angle == 180 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                        }
                        return crop;
                    }
                    if (options.Rotation != 0) bitmap.RotateFlip(options.Rotation == 90 ? RotateFlipType.Rotate90FlipNone : options.Rotation == 180 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                    int width = Math.Max(1, (int)Math.Ceiling(bitmap.Width * options.NumberRightPercent / 100.0));
                    int height = Math.Max(1, (int)Math.Ceiling(bitmap.Height * options.NumberTopPercent / 100.0));
                    return bitmap.Clone(new Rectangle(bitmap.Width - width, 0, width, height), PixelFormat.Format24bppRgb);
                }
            }
        }
        private static PointF[] Points(object value, float scaleX, float scaleY)
        {
            return ((IEnumerable)value).Cast<object>().Select(v => { var xy = ((IEnumerable)v).Cast<object>().ToArray();
                return new PointF(Convert.ToSingle(xy[0], CultureInfo.InvariantCulture) * scaleX, Convert.ToSingle(xy[1], CultureInfo.InvariantCulture) * scaleY); }).ToArray();
        }
    }
}
