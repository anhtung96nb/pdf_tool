using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Path = System.IO.Path;
using Matrix = iTextSharp.text.pdf.parser.Matrix;

namespace CountFile2.Services
{
    public sealed class PdfCompressionOptions
    {
        public double MinDpi { get; set; } = 150;
        public double ReductionPercent { get; set; } = 40;
        public int MinJpegQuality { get; set; } = 85;
        public bool Recursive { get; set; } = true;
        public bool Overwrite { get; set; }
    }

    public sealed class PdfCompressionResult
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public long OriginalBytes { get; set; }
        public long OutputBytes { get; set; }
        public long TargetBytes { get; set; }
        public double RequestedReduction { get; set; }
        public double ActualReduction { get { return !Written || OriginalBytes == 0 ? 0 : 100d * (1 - (double)OutputBytes / OriginalBytes); } }
        public double MinDpi { get; set; }
        public double? BeforeDpi { get; set; }
        public double? AfterDpi { get; set; }
        public int PageCount { get; set; }
        public bool Written { get; set; }
        public bool DpiSatisfied { get { return !AfterDpi.HasValue || AfterDpi.Value + 1e-6 >= MinDpi; } }
        public bool TargetReached { get { return Written && OutputBytes <= TargetBytes; } }
        public bool QualitySatisfied { get { return Written && DpiSatisfied; } }
        public string Error { get; set; }
        public List<string> Notes { get; private set; } = new List<string>();
        public string Status { get { return Error != null ? "Lỗi" : !Written ? "Chờ" : !TargetReached ? "Không đạt tỷ lệ giảm yêu cầu" : !DpiSatisfied ? "DPI đầu vào dưới ngưỡng" : "Đạt yêu cầu"; } }
        public string DpiNote { get { return string.Format(CultureInfo.InvariantCulture,
            "DPI tối thiểu cấu hình: {0:0.##}; DPI thấp nhất trước: {1}; sau: {2}", MinDpi, FormatDpi(BeforeDpi), FormatDpi(AfterDpi)); } }
        public string Details { get { return Error ?? DpiNote + "; " + string.Join("; ", Notes); } }
        internal static string FormatDpi(double? value) { return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "Không áp dụng - không có ảnh raster trong nội dung trang"; }
    }

    public sealed class PdfCompressionProgress
    {
        public string InputPath { get; set; }
        public string Status { get; set; }
        public int TotalFiles { get; set; }
        public int CompletedFiles { get; set; }
        public PdfCompressionResult Result { get; set; }
    }

    public sealed class PdfCompressionCanceledException : OperationCanceledException
    {
        public IList<PdfCompressionResult> Results { get; private set; }
        public PdfCompressionCanceledException(IList<PdfCompressionResult> results, CancellationToken token) : base(token)
        { Results = new List<PdfCompressionResult>(results).AsReadOnly(); }
    }

    /// <summary>Edits image XObjects, never rasterizes pages or upscales low-resolution input.</summary>
    public sealed class PdfCompressionService
    {
        public Task<IList<PdfCompressionResult>> ProcessAsync(string inputPath, string outputDirectory = null,
            PdfCompressionOptions options = null, CancellationToken token = default(CancellationToken), IProgress<PdfCompressionProgress> progress = null)
        { return Task.Run(() => Process(inputPath, outputDirectory, options, token, progress), token); }

        public IList<PdfCompressionResult> Process(string inputPath, string outputDirectory = null,
            PdfCompressionOptions options = null, CancellationToken token = default(CancellationToken), IProgress<PdfCompressionProgress> progress = null)
        {
            options = options ?? new PdfCompressionOptions();
            if (double.IsNaN(options.MinDpi) || double.IsInfinity(options.MinDpi) || options.MinDpi < 1 || options.MinDpi > 2400)
                throw new ArgumentOutOfRangeException("MinDpi", "DPI tối thiểu phải từ 1 đến 2400.");
            if (double.IsNaN(options.ReductionPercent) || double.IsInfinity(options.ReductionPercent) || options.ReductionPercent <= 0 || options.ReductionPercent >= 100)
                throw new ArgumentOutOfRangeException("ReductionPercent", "Tỷ lệ giảm phải lớn hơn 0 và nhỏ hơn 100%.");
            if (options.MinJpegQuality < 85 || options.MinJpegQuality > 100)
                throw new ArgumentOutOfRangeException("MinJpegQuality", "Chất lượng JPEG tối thiểu phải từ 85 đến 100.");
            if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("Chọn file PDF hoặc thư mục nguồn.");
            token.ThrowIfCancellationRequested();
            string input = Path.GetFullPath(inputPath);
            bool folder = Directory.Exists(input);
            if (!folder && !File.Exists(input)) throw new FileNotFoundException("Không tìm thấy nguồn.", input);
            if (!folder && !IsPdf(input)) throw new ArgumentException("Nguồn phải là file PDF hoặc thư mục.");
            string root = folder ? input : Path.GetDirectoryName(input);
            string output = string.IsNullOrWhiteSpace(outputDirectory) ? Path.Combine(root, "compressed") : Path.GetFullPath(outputDirectory);
            if (folder && Same(input, output)) throw new ArgumentException("Thư mục đầu ra phải khác thư mục nguồn.");
            string[] files = (folder ? Scan(input, output, options.Recursive, token) : new[] { input }).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var sources = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var results = new List<PdfCompressionResult>();
            foreach (string file in files)
            {
                if (token.IsCancellationRequested) throw new PdfCompressionCanceledException(results, token);
                string relative = file.Substring(root.TrimEnd('\\', '/').Length + 1);
                string target = Path.Combine(output, Path.GetDirectoryName(relative), Path.GetFileNameWithoutExtension(relative) + ".compressed.pdf");
                var result = new PdfCompressionResult { InputPath = file, OutputPath = target, MinDpi = options.MinDpi, RequestedReduction = options.ReductionPercent };
                Action<string> report = status => { if (progress != null) progress.Report(new PdfCompressionProgress {
                    InputPath = file, TotalFiles = files.Length, CompletedFiles = results.Count, Status = status,
                    Result = result.Written || result.Error != null ? result : null }); };
                try
                {
                    if (sources.Contains(target) || Same(file, target)) throw new IOException("Đầu ra trùng file nguồn.");
                    if (!options.Overwrite && File.Exists(target)) throw new IOException("File đầu ra đã tồn tại: " + target);
                    report("Phân tích DPI");
                    Compress(file, target, options, result, token, report);
                }
                catch (OperationCanceledException) { throw new PdfCompressionCanceledException(results, token); }
                catch (Exception ex) { result.Error = ex.Message; }
                results.Add(result); report(result.Status);
            }
            return results.AsReadOnly();
        }

        private static void Compress(string source, string target, PdfCompressionOptions options, PdfCompressionResult result,
            CancellationToken token, Action<string> report)
        {
            Analysis original;
            using (var reader = Open(source))
            {
                if (reader.NumberOfPages == 0) throw new InvalidDataException("PDF không có trang.");
                if (HasSignatures(reader, token)) throw new InvalidOperationException("PDF có chữ ký số: không nén để tránh làm mất hiệu lực chữ ký.");
                original = Analyze(reader, token);
            }
            result.PageCount = original.PageCount;
            result.OriginalBytes = new FileInfo(source).Length;
            result.TargetBytes = (long)Math.Floor(result.OriginalBytes * (1 - options.ReductionPercent / 100));
            result.BeforeDpi = original.MinDpi;
            result.AfterDpi = original.MinDpi;
            bool lowDpi = original.MinDpi.HasValue && original.MinDpi.Value + 1e-6 < options.MinDpi;
            if (lowDpi) result.Notes.Add("Ảnh đầu vào đã dưới ngưỡng DPI; chỉ tối ưu không mất chất lượng, không nội suy tăng DPI. Không thể xác nhận đạt ngưỡng DPI cấu hình.");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string prefix = Path.Combine(Path.GetDirectoryName(target), ".pdf-compression-" + Guid.NewGuid().ToString("N"));
            string best = prefix + "-best.tmp", candidate = prefix + "-candidate.tmp";
            var retainedNotes = new List<string>();
            var lastAttemptNotes = new List<string>();
            try
            {
                File.Copy(source, best);
                long bestSize = result.OriginalBytes;
                // Every attempt starts from the source; JPEG artifacts never accumulate across attempts.
                for (int level = 0; level <= (lowDpi ? 0 : 3); level++)
                {
                    token.ThrowIfCancellationRequested();
                    report(level == 0 ? "Tối ưu không mất chất lượng" : "Thử mức nén " + level + "/3");
                    var notes = new List<string>();
                    lastAttemptNotes = notes;
                    try
                    {
                        WriteCandidate(source, candidate, options, level, token, notes);
                        Analysis check;
                        using (var reader = Open(candidate)) { check = Analyze(reader, token); Validate(original, check, options.MinDpi, lowDpi); }
                        long size = new FileInfo(candidate).Length;
                        if (size < bestSize)
                        {
                            File.Replace(candidate, best, null); bestSize = size;
                            result.AfterDpi = check.MinDpi; retainedNotes = notes;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result.Notes.Add("Bỏ mức nén " + level + ": " + ex.Message); }
                    if (bestSize <= result.TargetBytes) break;
                }
                token.ThrowIfCancellationRequested();
                if (options.Overwrite && File.Exists(target)) File.Replace(best, target, null);
                else File.Move(best, target);
                result.OutputBytes = bestSize; result.Written = true;
                result.Notes.AddRange(retainedNotes.Distinct());
                if (!result.TargetReached) result.Notes.AddRange(lastAttemptNotes.Except(retainedNotes).Distinct());
                if (bestSize == result.OriginalBytes) result.Notes.Add("Giữ bản sao nguyên gốc vì không có phương án nhỏ hơn đáp ứng kiểm tra.");
                if (!result.TargetReached) result.Notes.Add("Đã thử các mức nén cho phép; giảm thêm có thể vi phạm DPI hoặc giới hạn chất lượng ảnh. Giữ kết quả nhỏ nhất đã kiểm tra.");
                result.Notes.Add(string.Format(CultureInfo.InvariantCulture, "Giảm yêu cầu: {0:0.##}%; thực tế: {1:0.##}%", options.ReductionPercent, result.ActualReduction));
            }
            finally { if (File.Exists(best)) File.Delete(best); if (File.Exists(candidate)) File.Delete(candidate); }
        }

        private static PdfReader Open(string path)
        {
            var reader = new PdfReader(path);
            if (reader.IsEncrypted()) { reader.Close(); throw new InvalidOperationException("PDF có mật khẩu/mã hóa: cần bản PDF không mã hóa để nén."); }
            return reader;
        }

        private static bool HasSignatures(PdfReader reader, CancellationToken token)
        {
            if (reader.AcroFields.GetSignatureNames().Count > 0) return true;
            // Also protect non-widget/detached signature dictionaries not listed by AcroFields.
            for (int index = 1; index < reader.XrefSize; index++)
            {
                token.ThrowIfCancellationRequested();
                var dictionary = reader.GetPdfObject(index) as PdfDictionary;
                if (dictionary != null && PdfName.SIG.Equals(dictionary.GetAsName(PdfName.TYPE)) && dictionary.Contains(PdfName.CONTENTS)) return true;
            }
            return false;
        }

        private static void WriteCandidate(string source, string target, PdfCompressionOptions options, int level, CancellationToken token, List<string> notes)
        {
            using (var reader = Open(source))
            using (var stream = File.Create(target))
            using (var stamper = new PdfStamper(reader, stream))
            {
                stamper.SetFullCompression(); stamper.Writer.CompressionLevel = PdfStream.BEST_COMPRESSION;
                for (int index = 1; index < reader.XrefSize; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var image = reader.GetPdfObject(index) as PRStream;
                    if (image == null || !PdfName.IMAGE.Equals(image.GetAsName(PdfName.SUBTYPE))) continue;
                    int width = image.GetAsNumber(PdfName.WIDTH)?.IntValue ?? 0, height = image.GetAsNumber(PdfName.HEIGHT)?.IntValue ?? 0;
                    if (width > 0 && height > 0 && (long)width * height <= 40000000) Reflate(image);
                }
                if (level > 0)
                {
                    var analysis = Analyze(reader, token);
                    var protectedImages = ProtectedImages(reader);
                    foreach (var item in analysis.Images.Values)
                    {
                        token.ThrowIfCancellationRequested();
                        if (item.Reference == null) { notes.Add("Ảnh inline được giữ nguyên."); continue; }
                        var image = PdfReader.GetPdfObject(item.Reference) as PRStream;
                        if (image == null || protectedImages.Contains(item.Reference.Number) || !CanEdit(image))
                        { notes.Add("Giữ ảnh có mask, màu đặc biệt, bộ lọc đặc biệt hoặc được dùng trong chú thích/form."); continue; }
                        if ((long)item.Width * item.Height > 40000000) { notes.Add("Ảnh trên 40 megapixel: giữ nguyên để giới hạn bộ nhớ."); continue; }
                        try { notes.Add(OptimizeImage(image, item, options, level)); }
                        catch (Exception ex) { notes.Add("Giữ ảnh không giải mã được: " + ex.Message); }
                    }
                }
                token.ThrowIfCancellationRequested();
                // Only Flate content streams are recompressed; do not touch image/font/attachment streams here.
                for (int page = 1; page <= reader.NumberOfPages; page++)
                {
                    token.ThrowIfCancellationRequested();
                    var contents = PdfReader.GetPdfObject(reader.GetPageN(page).Get(PdfName.CONTENTS));
                    var single = contents as PRStream;
                    if (single != null) Reflate(single);
                    var array = contents as PdfArray;
                    if (array != null) for (int i = 0; i < array.Size; i++) { var s = PdfReader.GetPdfObject(array[i]) as PRStream; if (s != null) Reflate(s); }
                }
            }
        }

        private static void Reflate(PRStream stream)
        {
            var filter = stream.Get(PdfName.FILTER);
            if (filter != null && !PdfName.FLATEDECODE.Equals(filter)) return;
            byte[] data = PdfReader.GetStreamBytes(stream);
            stream.Remove(PdfName.DECODEPARMS); stream.SetData(data, true, PdfStream.BEST_COMPRESSION);
        }

        private static bool CanEdit(PRStream stream)
        {
            var filter = stream.GetAsName(PdfName.FILTER);
            return !stream.Contains(PdfName.SMASK) && !stream.Contains(PdfName.MASK) && !stream.Contains(PdfName.IMAGEMASK)
                && !stream.Contains(PdfName.DECODE) && stream.GetAsNumber(PdfName.BITSPERCOMPONENT)?.IntValue == 8
                && (PdfName.DEVICERGB.Equals(stream.GetAsName(PdfName.COLORSPACE)) || PdfName.DEVICEGRAY.Equals(stream.GetAsName(PdfName.COLORSPACE)))
                && !(PdfName.DCTDECODE.Equals(filter) && stream.Contains(PdfName.DECODEPARMS))
                && (PdfName.DCTDECODE.Equals(filter) || PdfName.FLATEDECODE.Equals(filter) || !stream.Contains(PdfName.FILTER));
        }

        private static string OptimizeImage(PRStream stream, ImageUsage usage, PdfCompressionOptions options, int level)
        {
            int width = usage.Width, height = usage.Height;
            if ((long)width * height > 40000000) return "Ảnh trên 40 megapixel: giữ nguyên.";
            // The largest use of a shared image determines the floor, including nested Forms and UserUnit.
            double dpi = Math.Max(options.MinDpi, usage.MinDpi * (level == 1 ? .8 : level == 2 ? .6 : 0));
            double scale = Math.Min(1, dpi / usage.MinDpi);
            int w = Math.Min(width, Math.Max(1, (int)Math.Ceiling(width * scale)));
            int h = Math.Min(height, Math.Max(1, (int)Math.Ceiling(height * scale)));
            int quality = level == 1 ? Math.Max(95, options.MinJpegQuality) : level == 2 ? Math.Max(90, options.MinJpegQuality) : options.MinJpegQuality;
            byte[] sourceBytes = new PdfImageObject(stream).GetImageAsBytes();
            if (sourceBytes == null) return "Không giải mã được dữ liệu ảnh: giữ nguyên.";
            using (var bytes = new MemoryStream(sourceBytes))
            using (var decoded = System.Drawing.Image.FromStream(bytes))
            using (var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                using (var attributes = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    // Explicit pixel source bounds ignore embedded JPEG DPI (often 300 instead
                    // of the new bitmap's 96). DrawImageUnscaled uses physical size in GDI+.
                    g.DrawImage(decoded, new System.Drawing.Rectangle(0, 0, w, h), 0, 0, decoded.Width, decoded.Height, GraphicsUnit.Pixel, attributes);
                }
                byte[] encoded;
                using (var output = new MemoryStream())
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                    bitmap.Save(output, ImageCodecInfo.GetImageEncoders().Single(c => c.FormatID == ImageFormat.Jpeg.Guid), parameters);
                    encoded = output.ToArray();
                }
                int originalSize = PdfReader.GetStreamBytesRaw(stream).Length;
                if (encoded.Length >= originalSize) return string.Format(CultureInfo.InvariantCulture,
                    "Ảnh {0}: JPEG chất lượng {1} tạo {2:N0} byte, không nhỏ hơn ảnh gốc {3:N0} byte; giữ ảnh gốc và DPI {4:0.##}",
                    usage.Reference.Number, quality, encoded.Length, originalSize, usage.MinDpi);
                // Bound encoding distortion against the resized reference, in addition to the quality floor.
                using (var jpeg = new MemoryStream(encoded))
                using (var restored = new Bitmap(jpeg))
                    if (EncodingError(w == decoded.Width && h == decoded.Height ? (Bitmap)decoded : bitmap, restored) > .035)
                        return "Ảnh " + usage.Reference.Number + ": sai số mã hóa JPEG vượt giới hạn chất lượng; giữ nguyên.";
                stream.SetData(encoded, false);
                stream.Put(PdfName.FILTER, PdfName.DCTDECODE); stream.Remove(PdfName.DECODEPARMS);
                stream.Put(PdfName.WIDTH, new PdfNumber(w)); stream.Put(PdfName.HEIGHT, new PdfNumber(h));
                stream.Put(PdfName.COLORSPACE, PdfName.DEVICERGB);
                return string.Format(CultureInfo.InvariantCulture, "Ảnh {0}: JPEG chất lượng {1}, {2} x {3} pixel", usage.Reference.Number, quality, w, h);
            }
        }

        private static double EncodingError(Bitmap reference, Bitmap encoded)
        {
            double error = 0; int count = 0;
            int step = Math.Max(1, (int)Math.Sqrt((double)reference.Width * reference.Height / 100000));
            for (int y = 0; y < reference.Height; y += step) for (int x = 0; x < reference.Width; x += step)
            {
                var a = reference.GetPixel(x, y); var b = encoded.GetPixel(x, y);
                error += (a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B); count += 3;
            }
            return Math.Sqrt(error / count) / 255;
        }

        private sealed class ImageUsage
        {
            public PdfIndirectReference Reference;
            public int Width, Height;
            public double MinDpi;
        }
        private sealed class Analysis
        {
            public int PageCount;
            public double? MinDpi;
            public readonly Dictionary<string, ImageUsage> Images = new Dictionary<string, ImageUsage>();
            public readonly List<string> Text = new List<string>();
            public readonly List<string> Geometry = new List<string>();
        }
        private sealed class Listener : IRenderListener
        {
            private readonly Analysis analysis;
            private readonly double unit;
            private readonly CancellationToken token;
            public Listener(Analysis analysis, double unit, CancellationToken token) { this.analysis = analysis; this.unit = unit; this.token = token; }
            public void BeginTextBlock() { }
            public void EndTextBlock() { }
            public void RenderText(TextRenderInfo info) { token.ThrowIfCancellationRequested(); }
            public void RenderImage(ImageRenderInfo info)
            {
                token.ThrowIfCancellationRequested();
                var reference = info.GetRef();
                var dictionary = reference == null ? info.GetImage().GetDictionary() : (PdfDictionary)PdfReader.GetPdfObject(reference);
                int w = dictionary.GetAsNumber(PdfName.WIDTH).IntValue, h = dictionary.GetAsNumber(PdfName.HEIGHT).IntValue;
                if (w <= 0 || h <= 0) throw new InvalidDataException("Kích thước ảnh PDF không hợp lệ.");
                var m = info.GetImageCTM();
                // Largest singular value of pixel-to-page matrix: conservative under skew/shear as well as rotation.
                double a = m[Matrix.I11] * unit / w, b = m[Matrix.I12] * unit / w;
                double c = m[Matrix.I21] * unit / h, d = m[Matrix.I22] * unit / h;
                double sum = a * a + b * b + c * c + d * d, det = a * d - b * c;
                double stretch = Math.Sqrt((sum + Math.Sqrt(Math.Max(0, sum * sum - 4 * det * det))) / 2);
                if (stretch <= 0) return; // Invisible zero-size placement.
                double dpi = NormalizeMeasuredDpi(72 / stretch);
                if (!analysis.MinDpi.HasValue || dpi < analysis.MinDpi.Value) analysis.MinDpi = dpi;
                string key = reference == null ? "inline-" + analysis.Images.Count : reference.Number + ":" + reference.Generation;
                ImageUsage existing;
                if (analysis.Images.TryGetValue(key, out existing)) existing.MinDpi = Math.Min(existing.MinDpi, dpi);
                else analysis.Images.Add(key, new ImageUsage { Reference = reference, Width = w, Height = h, MinDpi = dpi });
            }
        }

        private static double NormalizeMeasuredDpi(double dpi)
        {
            // iText's CTM stores floats. Decimal page lengths such as 596.88 lose a few
            // millionths of a point, turning a mathematically exact 300 into 299.99999.
            // Snap only integer DPI within two float epsilons; do not round genuinely low DPI.
            double integer = Math.Round(dpi);
            return integer >= 1 && Math.Abs(dpi - integer) <= dpi * 2e-7 ? integer : dpi;
        }

        private static Analysis Analyze(PdfReader reader, CancellationToken token)
        {
            var analysis = new Analysis { PageCount = reader.NumberOfPages };
            var parser = new PdfReaderContentParser(reader);
            for (int page = 1; page <= reader.NumberOfPages; page++)
            {
                token.ThrowIfCancellationRequested();
                var unitNumber = reader.GetPageN(page).GetAsNumber(PdfName.USERUNIT);
                double unit = unitNumber == null ? 1 : unitNumber.DoubleValue;
                if (unit <= 0 || double.IsNaN(unit) || double.IsInfinity(unit)) throw new InvalidDataException("UserUnit không hợp lệ.");
                parser.ProcessContent(page, new Listener(analysis, unit, token));
                analysis.Text.Add(PdfTextExtractor.GetTextFromPage(reader, page));
                var box = reader.GetPageSize(page); var crop = reader.GetCropBox(page);
                analysis.Geometry.Add(box.ToString() + "|" + crop.ToString() + "|" + reader.GetPageRotation(page) + "|" + unit.ToString("R", CultureInfo.InvariantCulture));
            }
            return analysis;
        }

        private static void Validate(Analysis original, Analysis candidate, double floor, bool lowInput)
        {
            if (candidate.PageCount != original.PageCount || !candidate.Text.SequenceEqual(original.Text) || !candidate.Geometry.SequenceEqual(original.Geometry))
                throw new InvalidDataException("Kiểm tra không đạt: số trang, văn bản hoặc hình học trang thay đổi.");
            if (original.MinDpi.HasValue != candidate.MinDpi.HasValue || original.Images.Count != candidate.Images.Count)
                throw new InvalidDataException("Kiểm tra không đạt: số ảnh thay đổi.");
            foreach (var pair in original.Images)
            {
                ImageUsage other;
                // Object numbers may change after full compression; compare all placements through min DPI below.
                if (candidate.Images.TryGetValue(pair.Key, out other) && lowInput && other.MinDpi + 1e-6 < pair.Value.MinDpi)
                    throw new InvalidDataException("DPI ảnh đầu vào thấp bị giảm thêm.");
            }
            double threshold = lowInput ? original.MinDpi.Value : floor;
            if (candidate.MinDpi.HasValue && candidate.MinDpi.Value + 1e-6 < threshold)
                throw new InvalidDataException("DPI đầu ra dưới ngưỡng cho phép.");
        }

        private static HashSet<int> ProtectedImages(PdfReader reader)
        {
            var images = new HashSet<int>(); var visited = new HashSet<int>();
            Action<PdfObject> visit = null;
            visit = obj => {
                var reference = obj as PRIndirectReference;
                if (reference != null && !visited.Add(reference.Number)) return;
                var resolved = PdfReader.GetPdfObject(obj);
                var dictionary = resolved as PdfDictionary;
                if (dictionary != null)
                {
                    if (PdfName.IMAGE.Equals(dictionary.GetAsName(PdfName.SUBTYPE)) && reference != null) images.Add(reference.Number);
                    foreach (var key in dictionary.Keys) if (!PdfName.P.Equals(key) && !PdfName.PARENT.Equals(key)) visit(dictionary.Get(key));
                }
                var array = resolved as PdfArray;
                if (array != null) foreach (PdfObject item in array) visit(item);
            };
            for (int page = 1; page <= reader.NumberOfPages; page++) visit(reader.GetPageN(page).Get(PdfName.ANNOTS));
            visit(reader.Catalog.Get(PdfName.ACROFORM));
            return images;
        }

        private static bool IsPdf(string path) { return string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase); }
        private static bool Same(string a, string b) { return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        private static IEnumerable<string> Scan(string directory, string output, bool recursive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            foreach (string file in Directory.GetFiles(directory))
                if (IsPdf(file) && !file.EndsWith(".compressed.pdf", StringComparison.OrdinalIgnoreCase)) yield return file;
            if (recursive) foreach (string child in Directory.GetDirectories(directory))
                if (!Same(child, output) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    foreach (string file in Scan(child, output, true, token)) yield return file;
        }

        public static void ExportCsv(string path, IEnumerable<PdfCompressionResult> results)
        {
            Func<object, string> cell = value => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Nguồn,Đầu ra,Trạng thái,Byte trước,Byte sau,Byte mục tiêu,Giảm yêu cầu (%),Giảm thực tế (%),DPI cấu hình,DPI trước,DPI sau,Đạt DPI,Ghi chú");
                foreach (var r in results) writer.WriteLine(string.Join(",", new object[] { r.InputPath, r.OutputPath, r.Status, r.OriginalBytes,
                    r.OutputBytes, r.TargetBytes, r.RequestedReduction, r.ActualReduction, r.MinDpi,
                    PdfCompressionResult.FormatDpi(r.BeforeDpi), PdfCompressionResult.FormatDpi(r.AfterDpi), r.Written ? r.DpiSatisfied.ToString() : "", r.Details }.Select(cell)));
            }
        }
    }
}
