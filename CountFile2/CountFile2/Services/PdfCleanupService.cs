using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Tesseract;
using Path = System.IO.Path;

namespace CountFile2.Services
{
    public sealed class PdfCleanupOptions
    {
        public bool Recursive { get; set; } = true;
        public bool AnalyzeOnly { get; set; } = true;
        public bool RemoveBlankPages { get; set; } = true;
        public bool Deskew { get; set; } = true;
        public bool CheckQuality { get; set; } = true;
        public bool UseOcr { get; set; }
        public bool Overwrite { get; set; }
        public int AnalysisDpi { get; set; } = 150;
        public double MaxSkewDegrees { get; set; } = 7;
        public double MinSkewDegrees { get; set; } = .3;
        public double MinSkewConfidence { get; set; } = .12;
        public double MinImageDpi { get; set; } = 150;
        public double MinContrast { get; set; } = 45;
        public double MinSharpness { get; set; } = 150;
        public double MinOcrConfidence { get; set; } = .45;
        public string Language { get; set; } = "vie";
        public string TessDataPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
    }
    public sealed class PdfPageAnalysis
    {
        public int OriginalPage { get; set; }
        public int OutputPage { get; set; }
        public bool IsBlank { get; set; }
        public bool Remove { get; set; }
        public double InkFraction { get; set; }
        // Clockwise slope measured in the displayed image; PDF correction is counterclockwise.
        public double SkewDegrees { get; set; }
        public double SkewConfidence { get; set; }
        public bool ApplyDeskew { get; set; }
        public int ClockwiseRotation { get; set; }
        public double? ImageDpi { get; set; }
        public double Contrast { get; set; }
        public double Sharpness { get; set; }
        public double? OcrConfidence { get; set; }
        public List<string> QualityReasons { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public bool LowQuality { get { return QualityReasons.Count > 0; } }
        public string Action { get { return Remove ? "Xóa trang trắng" : ApplyDeskew || ClockwiseRotation != 0 ? "Căn thẳng / xoay" : "Giữ"; } }
        public string Details { get { return string.Join("; ", QualityReasons.Concat(Warnings)); } }
    }
    public sealed class PdfCleanupResult
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string Error { get; set; }
        public bool Written { get; set; }
        public bool AllBlank { get; set; }
        public List<PdfPageAnalysis> Pages { get; set; } = new List<PdfPageAnalysis>();
        public List<string> Warnings { get; set; } = new List<string>();
        public int OriginalPages { get; set; }
        public int KeptPages { get { return Pages.Count(p => !p.Remove); } }
        public bool LowQuality { get { return Pages.Any(p => p.LowQuality); } }
        public string Status { get { return Error != null ? "Lỗi" : AllBlank ? "Toàn bộ trang trắng" : Written ? "Đã xuất" : "Đã phân tích"; } }
        public string Details { get { return Error ?? string.Join("; ", Warnings.Concat(Pages.Where(p => p.LowQuality).Select(p => "Trang " + p.OriginalPage + ": " + string.Join(", ", p.QualityReasons)))); } }
    }
    public sealed class PdfCleanupProgress
    {
        public string InputPath { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public int CompletedFiles { get; set; }
        public int TotalFiles { get; set; }
        public PdfCleanupResult Result { get; set; }
    }
    public sealed class PdfCleanupCanceledException : OperationCanceledException
    {
        public IList<PdfCleanupResult> Results { get; private set; }
        public PdfCleanupCanceledException(IList<PdfCleanupResult> results, CancellationToken token) : base(token)
        { Results = new List<PdfCleanupResult>(results).AsReadOnly(); }
    }

    public sealed class PdfCleanupService
    {
        public Task<IList<PdfCleanupResult>> ProcessAsync(string inputPath, string outputDirectory, PdfCleanupOptions options = null,
            CancellationToken token = default(CancellationToken), IProgress<PdfCleanupProgress> progress = null)
        { return Task.Run(() => Process(inputPath, outputDirectory, options, token, progress), token); }

        public IList<PdfCleanupResult> Process(string inputPath, string outputDirectory, PdfCleanupOptions options = null,
            CancellationToken token = default(CancellationToken), IProgress<PdfCleanupProgress> progress = null)
        {
            options = options ?? new PdfCleanupOptions(); Validate(options);
            if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("Chọn file hoặc thư mục nguồn.");
            string input = Path.GetFullPath(inputPath);
            bool folder = Directory.Exists(input);
            if (!folder && !File.Exists(input)) throw new FileNotFoundException("Không tìm thấy nguồn.", input);
            if (!folder && !IsPdf(input)) throw new ArgumentException("Nguồn phải là PDF hoặc thư mục.");
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Chọn thư mục đầu ra.");
            string output = Path.GetFullPath(outputDirectory);
            if (folder && Same(input, output)) throw new ArgumentException("Thư mục đầu ra phải khác thư mục nguồn.");
            string root = folder ? input : Path.GetDirectoryName(input);
            string[] files = (folder ? Scan(input, output, options.Recursive, token) : new[] { input }).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var results = new List<PdfCleanupResult>();
            var sources = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (token.IsCancellationRequested) throw new PdfCleanupCanceledException(results, token);
                string relative = file.Substring(root.TrimEnd('\\', '/').Length + 1);
                string target = Path.Combine(output, Path.GetDirectoryName(relative), Path.GetFileNameWithoutExtension(relative) + ".clean.pdf");
                var result = new PdfCleanupResult { InputPath = file, OutputPath = target };
                string temporary = null;
                try
                {
                    if (sources.Contains(target) || Same(target, file)) throw new IOException("Đầu ra trùng file nguồn.");
                    if (!options.AnalyzeOnly && !options.Overwrite && File.Exists(target)) throw new IOException("File đầu ra đã tồn tại.");
                    using (var reader = new PdfReader(file))
                    using (var pdf = PdfiumViewer.PdfDocument.Load(file))
                    using (var engine = options.UseOcr ? new TesseractEngine(options.TessDataPath, options.Language, EngineMode.Default) : null)
                    {
                        bool signed = reader.AcroFields.GetSignatureNames().Count > 0;
                        if (signed) result.Warnings.Add("PDF có chữ ký số: chỉ phân tích, không xuất bản chỉnh sửa.");
                        if (reader.IsEncrypted() && !reader.IsOpenedWithFullPermissions) throw new InvalidOperationException("PDF không cho phép chỉnh sửa.");
                        if (pdf.PageCount == 0) throw new InvalidDataException("PDF không có trang.");
                        result.OriginalPages = pdf.PageCount;
                        for (int index = 0; index < pdf.PageCount; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            var content = new ContentMetrics(reader.GetPageSize(index + 1).Width * reader.GetPageSize(index + 1).Height);
                            PdfReaderContentParser parser = new PdfReaderContentParser(reader);
                            parser.ProcessContent(index + 1, content);
                            var size = pdf.PageSizes[index];
                            int w = checked((int)Math.Ceiling(size.Width * options.AnalysisDpi / 72));
                            int h = checked((int)Math.Ceiling(size.Height * options.AnalysisDpi / 72));
                            if (w < 3 || h < 3 || (long)w * h > 20000000) throw new InvalidDataException("Trang vượt giới hạn 20 megapixel hoặc kích thước không hợp lệ.");
                            var annots = reader.GetPageN(index + 1).GetAsArray(PdfName.ANNOTS);
                            bool annotations = annots != null && annots.Size > 0;
                            using (var image = pdf.Render(index, w, h, options.AnalysisDpi, options.AnalysisDpi, PdfiumViewer.PdfRenderFlags.Annotations))
                            using (var bitmap = WhiteBitmap(image))
                            {
                                PdfPageAnalysis page = PdfPageAnalyzer.Analyze(bitmap, options, token);
                                page.OriginalPage = index + 1;
                                page.ImageDpi = content.MinDpi;
                                if (content.HasText || annotations) page.IsBlank = false;
                                page.Remove = page.IsBlank && options.RemoveBlankPages;
                                if (annotations) page.Warnings.Add("Có chú thích/form: giữ nguyên hình học trang để bảo toàn tương tác.");
                                if (!page.IsBlank && engine != null) AnalyzeOcr(bitmap, page, engine, options, token);
                                else if (!page.IsBlank && options.Deskew && content.MinDpi.HasValue)
                                    page.Warnings.Add("Chưa kiểm tra hướng 90/180/270 độ; bật OCR để kiểm tra.");
                                if (page.ClockwiseRotation != 0)
                                {
                                    using (var oriented = new Bitmap(bitmap))
                                    {
                                        oriented.RotateFlip(page.ClockwiseRotation == 90 ? RotateFlipType.Rotate90FlipNone : page.ClockwiseRotation == 180 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                                        var aligned = PdfPageAnalyzer.Analyze(oriented, options, token);
                                        page.SkewDegrees = aligned.SkewDegrees; page.SkewConfidence = aligned.SkewConfidence; page.ApplyDeskew = aligned.ApplyDeskew;
                                    }
                                }
                                if (annotations) { page.ApplyDeskew = false; page.ClockwiseRotation = 0; }
                                if (options.CheckQuality && !page.IsBlank && content.MinDpi.HasValue)
                                {
                                    if (content.MinDpi.Value < options.MinImageDpi * .98) page.QualityReasons.Add("Độ phân giải ảnh thấp: " + content.MinDpi.Value.ToString("F0") + " DPI");
                                    if (page.Contrast < options.MinContrast) page.QualityReasons.Add("Tương phản thấp / chữ mờ");
                                    if (page.Sharpness < options.MinSharpness) page.QualityReasons.Add("Ảnh có dấu hiệu mờ (cần đối chiếu)");
                                }
                                if (content.HasText && content.MinDpi == null) page.QualityReasons.Clear();
                                result.Pages.Add(page);
                            }
                            if (progress != null) progress.Report(new PdfCleanupProgress { InputPath = file, Page = index + 1, TotalPages = pdf.PageCount, TotalFiles = files.Length, CompletedFiles = results.Count });
                        }
                        int number = 0;
                        foreach (var page in result.Pages) page.OutputPage = page.Remove ? 0 : ++number;
                        result.AllBlank = number == 0;
                        if (!options.AnalyzeOnly && !result.AllBlank && !signed)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            temporary = Path.Combine(Path.GetDirectoryName(target), ".pdf-cleanup-" + Guid.NewGuid().ToString("N") + ".tmp");
                            Write(reader, temporary, result.Pages, token);
                            using (var check = new PdfReader(temporary))
                                if (check.NumberOfPages != number) throw new InvalidDataException("Số trang đầu ra không đúng.");
                            using (var check = PdfiumViewer.PdfDocument.Load(temporary))
                            {
                                if (check.PageCount != number) throw new InvalidDataException("Không mở được PDF đầu ra.");
                                for (int p = 0; p < number; p++) { token.ThrowIfCancellationRequested(); using (var checkImage = check.Render(p, 100, 100, true)) { } }
                            }
                            token.ThrowIfCancellationRequested();
                            if (File.Exists(target) && options.Overwrite) File.Replace(temporary, target, null);
                            else File.Move(temporary, target);
                            result.Written = true;
                        }
                    }
                }
                catch (OperationCanceledException) { throw new PdfCleanupCanceledException(results, token); }
                catch (Exception ex) { result.Error = ex.Message; }
                finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
                results.Add(result);
                if (progress != null) progress.Report(new PdfCleanupProgress { InputPath = file, CompletedFiles = results.Count, TotalFiles = files.Length, Result = result });
            }
            return results.AsReadOnly();
        }
        private static void Validate(PdfCleanupOptions o)
        {
            if (o.AnalysisDpi < 72 || o.AnalysisDpi > 300 || !Finite(o.MaxSkewDegrees) || o.MaxSkewDegrees < 1 || o.MaxSkewDegrees > 15
                || !Finite(o.MinSkewDegrees) || o.MinSkewDegrees < .1 || o.MinSkewDegrees > o.MaxSkewDegrees
                || !Finite(o.MinSkewConfidence) || o.MinSkewConfidence < 0 || o.MinSkewConfidence > 1
                || !Finite(o.MinImageDpi) || o.MinImageDpi <= 0 || !Finite(o.MinContrast) || o.MinContrast < 0 || o.MinContrast > 255
                || !Finite(o.MinSharpness) || o.MinSharpness < 0 || !Finite(o.MinOcrConfidence) || o.MinOcrConfidence < 0 || o.MinOcrConfidence > 1)
                throw new ArgumentException("Ngưỡng phân tích không hợp lệ.");
            if (o.UseOcr && (string.IsNullOrWhiteSpace(o.Language) || !Directory.Exists(o.TessDataPath))) throw new ArgumentException("OCR cần ngôn ngữ và thư mục tessdata hợp lệ.");
        }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        private static bool IsPdf(string p) { return Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase); }
        private static bool Same(string a, string b) { return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        private static IEnumerable<string> Scan(string dir, string excluded, bool recursive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Same(dir, excluded)) yield break;
            foreach (string file in Directory.GetFiles(dir))
                if (IsPdf(file) && !file.EndsWith(".clean.pdf", StringComparison.OrdinalIgnoreCase)) yield return file;
            if (recursive) foreach (string child in Directory.GetDirectories(dir))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    foreach (string file in Scan(child, excluded, true, token)) yield return file;
        }
        internal static Bitmap WhiteBitmap(Image image)
        {
            var result = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(result)) { g.Clear(Color.White); g.DrawImageUnscaled(image, 0, 0); }
            return result;
        }
        private static void AnalyzeOcr(Bitmap bitmap, PdfPageAnalysis result, TesseractEngine engine, PdfCleanupOptions options, CancellationToken token)
        {
            var scores = new double[4]; var letters = new int[4];
            for (int turn = 0; turn < (options.Deskew ? 4 : 1); turn++)
            {
                token.ThrowIfCancellationRequested();
                using (var copy = new Bitmap(bitmap))
                {
                    if (turn > 0) copy.RotateFlip(turn == 1 ? RotateFlipType.Rotate90FlipNone : turn == 2 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                    // Auto can read vertical blocks without changing the page orientation, making
                    // upright and sideways confidence identical. Probe a horizontal block instead.
                    using (var pix = PixConverter.ToPix(copy)) using (var page = engine.Process(pix, options.Deskew ? PageSegMode.SingleBlock : PageSegMode.Auto))
                    { scores[turn] = page.GetMeanConfidence(); letters[turn] = (page.GetText() ?? "").Count(char.IsLetter); }
                }
            }
            int best = Enumerable.Range(0, options.Deskew ? 4 : 1).OrderByDescending(i => scores[i]).First();
            double second = Enumerable.Range(0, 4).Where(i => i != best).Max(i => scores[i]);
            if (best > 0 && scores[best] >= .80 && scores[best] - second >= .15 && letters[best] >= 60) result.ClockwiseRotation = best * 90;
            else if (options.Deskew && (best > 0 || scores[best] < .65)) result.Warnings.Add("Hướng trang chưa đủ tin cậy: giữ nguyên hướng.");
            result.OcrConfidence = scores[result.ClockwiseRotation / 90];
            if (options.CheckQuality && result.OcrConfidence < options.MinOcrConfidence) result.QualityReasons.Add("OCR đọc kém; cần kiểm tra chữ hoặc loại nội dung");
        }
        private static void Write(PdfReader reader, string target, IList<PdfPageAnalysis> pages, CancellationToken token)
        {
            reader.SelectPages(pages.Where(p => !p.Remove).Select(p => p.OriginalPage).ToList());
            using (var stream = File.Create(target))
            using (var stamper = new PdfStamper(reader, stream))
            {
                stamper.RotateContents = false;
                foreach (var page in pages.Where(p => !p.Remove))
                {
                    token.ThrowIfCancellationRequested();
                    int n = page.OutputPage;
                    var dictionary = reader.GetPageN(n);
                    if (page.ApplyDeskew)
                    {
                        var box = reader.GetCropBox(n);
                        double angle = page.SkewDegrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
                        double[] xs = { box.Left, box.Right, box.Right, box.Left }, ys = { box.Bottom, box.Bottom, box.Top, box.Top };
                        var rx = xs.Select((x, i) => c * x - s * ys[i]).ToArray();
                        var ry = xs.Select((x, i) => s * x + c * ys[i]).ToArray();
                        float width = (float)(rx.Max() - rx.Min()), height = (float)(ry.Max() - ry.Min());
                        var bounds = new PdfArray(new float[] { 0, 0, width, height });
                        dictionary.Put(PdfName.MEDIABOX, bounds); dictionary.Put(PdfName.CROPBOX, bounds);
                        dictionary.Remove(PdfName.TRIMBOX); dictionary.Remove(PdfName.BLEEDBOX); dictionary.Remove(PdfName.ARTBOX);
                        byte[] prefix = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, "q\n{0:0.############} {1:0.############} {2:0.############} {3:0.############} {4:0.############} {5:0.############} cm\n{6:0.############} {7:0.############} {8:0.############} {9:0.############} re W n\n", c, s, -s, c, -rx.Min(), -ry.Min(), box.Left, box.Bottom, box.Width, box.Height));
                        byte[] original = reader.GetPageContent(n), suffix = Encoding.ASCII.GetBytes("\nQ\n");
                        using (var content = new MemoryStream())
                        {
                            content.Write(prefix, 0, prefix.Length); content.Write(original, 0, original.Length); content.Write(suffix, 0, suffix.Length);
                            // Do not kill the old stream: another page may share its reference.
                            var transformed = new PdfStream(content.ToArray()); transformed.FlateCompress();
                            dictionary.Put(PdfName.CONTENTS, stamper.Writer.AddToBody(transformed).IndirectReference);
                        }
                    }
                    if (page.ClockwiseRotation != 0) dictionary.Put(PdfName.ROTATE, new PdfNumber((reader.GetPageRotation(n) + page.ClockwiseRotation) % 360));
                }
            }
        }
        public static void ExportCsv(string path, IEnumerable<PdfCleanupResult> results, bool lowQualityOnly = false)
        {
            string target = Path.GetFullPath(path);
            if (!Path.GetExtension(target).Equals(".csv", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Báo cáo phải là CSV.");
            var snapshot = results.ToArray();
            if (snapshot.Any(r => Same(target, r.InputPath) || Same(target, r.OutputPath))) throw new IOException("Báo cáo trùng PDF.");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new StreamWriter(temp, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("FileNguon,FileDich,SoTrangGoc,SoTrangGiu,TrangGoc,TrangDich,HanhDong,GocNghieng,DoTinCay,DPIAnh,TuongPhan,DoSacNet,OCR,ChatLuongThap,TrangThai,LyDo");
                    foreach (var result in snapshot.Where(r => !lowQualityOnly || r.LowQuality))
                    {
                        foreach (var page in result.Pages.Where(p => !lowQualityOnly || p.LowQuality))
                            writer.WriteLine(string.Join(",", new object[] { result.InputPath, result.Written ? result.OutputPath : "", result.OriginalPages, result.KeptPages, page.OriginalPage,
                                page.OutputPage, page.Action, page.SkewDegrees, page.SkewConfidence, page.ImageDpi, page.Contrast, page.Sharpness,
                                page.OcrConfidence, page.LowQuality, result.Status, string.Join("; ", result.Warnings.Concat(new[] { page.Details, result.Error ?? "" })) }.Select(Csv)));
                        if (result.Pages.Count == 0 || result.Error != null)
                            writer.WriteLine(string.Join(",", new object[] { result.InputPath, "", result.OriginalPages, result.KeptPages, "", "", "", "", "", "", "", "", "", "", result.Status, result.Error }.Select(Csv)));
                    }
                }
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static string Csv(object value)
        {
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            // Prevent CSV formula execution when users open reports in Excel.
            if (s.Length > 0 && "=+@\t\r".Contains(s[0]) || s.StartsWith("-", StringComparison.Ordinal) && !(value is double)) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        private sealed class ContentMetrics : IRenderListener
        {
            private readonly double area;
            public bool HasText;
            public double? MinDpi;
            public ContentMetrics(double pageArea) { area = pageArea; }
            public void BeginTextBlock() { }
            public void EndTextBlock() { }
            public void RenderText(TextRenderInfo info) { if (!string.IsNullOrWhiteSpace(info.GetText())) HasText = true; }
            public void RenderImage(ImageRenderInfo info)
            {
                var m = info.GetImageCTM();
                double w = Math.Sqrt(m[Matrix.I11] * m[Matrix.I11] + m[Matrix.I12] * m[Matrix.I12]);
                double h = Math.Sqrt(m[Matrix.I21] * m[Matrix.I21] + m[Matrix.I22] * m[Matrix.I22]);
                double imageArea = Math.Abs(m[Matrix.I11] * m[Matrix.I22] - m[Matrix.I12] * m[Matrix.I21]);
                if (w <= 0 || h <= 0 || imageArea < area * .25) return; // Ignore logos and small stamps.
                var image = info.GetImage();
                double dpi = Math.Min(image.GetDictionary().GetAsNumber(PdfName.WIDTH).DoubleValue * 72 / w, image.GetDictionary().GetAsNumber(PdfName.HEIGHT).DoubleValue * 72 / h);
                MinDpi = MinDpi.HasValue ? Math.Min(MinDpi.Value, dpi) : dpi;
            }
        }
    }

    public static class PdfPageAnalyzer
    {
        public static PdfPageAnalysis Analyze(Bitmap bitmap, PdfCleanupOptions options, CancellationToken token = default(CancellationToken))
        {
            // Whole page, including margins: stamps/page numbers must never be discarded by a crop.
            int w = bitmap.Width, h = bitmap.Height;
            byte[] gray = Gray(bitmap); int[] histogram = new int[256];
            foreach (byte v in gray) histogram[v]++;
            int minimum = Array.FindIndex(histogram, n => n > 0), maximum = Array.FindLastIndex(histogram, n => n > 0);
            int background = Percentile(histogram, gray.Length, .8), threshold = Math.Min(245, background - 10);
            var points = new List<Point>(); int ink = 0;
            for (int y = 0; y < h; y++)
            {
                if (y % 64 == 0) token.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++) if (gray[y * w + x] < threshold) { ink++; if (x % 2 == 0) points.Add(new Point(x, y)); }
            }
            var result = new PdfPageAnalysis { InkFraction = (double)ink / gray.Length,
                IsBlank = minimum >= 250 && maximum == minimum };
            if (!result.IsBlank && result.InkFraction < .0005) result.Warnings.Add("Trang ít nội dung hoặc nền nhiễu: giữ lại để kiểm tra.");
            int[] foreground = new int[256]; int foregroundCount = 0;
            for (int i = 0; i < threshold; i++) { foreground[i] = histogram[i]; foregroundCount += histogram[i]; }
            result.Contrast = foregroundCount > 0 ? background - Percentile(foreground, foregroundCount, .2) : maximum - minimum;
            double lap = 0; int samples = 0;
            for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                if (gray[i] >= threshold && gray[i - 1] >= threshold && gray[i + 1] >= threshold && gray[i - w] >= threshold && gray[i + w] >= threshold) continue;
                double l = 4 * gray[i] - gray[i - 1] - gray[i + 1] - gray[i - w] - gray[i + w]; lap += l * l; samples++;
            }
            result.Sharpness = samples == 0 ? 0 : lap / samples;
            if (!options.Deskew || result.IsBlank || points.Count < 200 || result.InkFraction > .3) return result;
            // Bound projection cost for dense scans while retaining points across the whole page.
            if (points.Count > 40000) { int stride = (int)Math.Ceiling(points.Count / 40000d); points = points.Where((p, i) => i % stride == 0).ToList(); }
            double baseline = Score(points, w, h, 0), bestScore = baseline, bestAngle = 0;
            for (double angle = -options.MaxSkewDegrees; angle <= options.MaxSkewDegrees + .001; angle += .25)
            {
                token.ThrowIfCancellationRequested(); double score = Score(points, w, h, angle);
                if (score > bestScore) { bestScore = score; bestAngle = angle; }
            }
            result.SkewDegrees = bestAngle;
            result.SkewConfidence = bestScore == 0 ? 0 : (bestScore - baseline) / bestScore;
            double offPeak = Math.Max(Score(points, w, h, bestAngle - 1), Score(points, w, h, bestAngle + 1));
            double prominence = bestScore == 0 ? 0 : (bestScore - offPeak) / bestScore;
            result.ApplyDeskew = Math.Abs(bestAngle) >= options.MinSkewDegrees && Math.Abs(bestAngle) < options.MaxSkewDegrees - .2
                && result.SkewConfidence >= options.MinSkewConfidence && prominence >= .08;
            if (!result.ApplyDeskew && Math.Abs(bestAngle) >= options.MinSkewDegrees) result.Warnings.Add("Góc nghiêng chưa đủ tin cậy hoặc vượt phạm vi: giữ nguyên.");
            return result;
        }
        private static int Percentile(int[] histogram, int count, double p)
        { int n = 0; for (int i = 0; i < 256; i++) { n += histogram[i]; if (n >= Math.Max(1, count * p)) return i; } return 255; }
        private static double Score(IList<Point> points, int w, int h, double angle)
        {
            double slope = Math.Tan(angle * Math.PI / 180); int offset = w / 2; var bins = new int[h + w + 4];
            foreach (var point in points) { int row = (int)Math.Round(point.Y - slope * (point.X - w / 2d)) + offset; if (row >= 0 && row < bins.Length) bins[row]++; }
            double score = 0; foreach (int count in bins) score += (double)count * count; return score;
        }
        private static byte[] Gray(Bitmap bitmap)
        {
            using (var copy = PdfCleanupService.WhiteBitmap(bitmap))
            {
                var data = copy.LockBits(new System.Drawing.Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var bytes = new byte[Math.Abs(data.Stride) * copy.Height]; Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    var gray = new byte[copy.Width * copy.Height];
                    for (int y = 0; y < copy.Height; y++) for (int x = 0; x < copy.Width; x++)
                    { int i = y * data.Stride + x * 3; gray[y * copy.Width + x] = (byte)((bytes[i] * 114 + bytes[i + 1] * 587 + bytes[i + 2] * 299) / 1000); }
                    return gray;
                }
                finally { copy.UnlockBits(data); }
            }
        }
    }
}
