using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PdfiumViewer;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.codec;
using PdfDocument = PdfiumViewer.PdfDocument;
using DrawingImage = System.Drawing.Image;

namespace CountFile2.Services
{
    public sealed class ConversionProgress
    {
        public string InputPath { get; internal set; }
        public string OutputPath { get; internal set; }
        public int TotalFiles { get; internal set; }
        public int CompletedFiles { get; internal set; }
        public int Page { get; internal set; }
        public int TotalPages { get; internal set; }
        public string Status { get; internal set; }
        public ConversionResult Result { get; internal set; }
    }

    public sealed class ConversionCanceledException : OperationCanceledException
    {
        public IList<ConversionResult> Results { get; private set; }
        public ConversionCanceledException(IList<ConversionResult> results, CancellationToken token)
            : base("Conversion canceled.", token) { Results = new List<ConversionResult>(results).AsReadOnly(); }
    }

    public sealed class ConversionResult
    {
        public string InputPath { get; internal set; }
        public string OutputPath { get; internal set; }
        public int PageCount { get; internal set; }
        public Exception Error { get; internal set; }
        public bool Success { get { return Error == null; } }
    }

    /// <summary>Converts a file or recursively converts a directory. Source files are preserved.</summary>
    public sealed class PdfTiffConversionService
    {
        public IList<ConversionResult> ConvertPdfToTiff(string inputPath,
            string outputDirectory = null, int dpi = 300, bool overwrite = false,
            CancellationToken cancellationToken = default(CancellationToken), IProgress<ConversionProgress> progress = null)
        {
            if (dpi < 1 || dpi > 1200)
                throw new ArgumentOutOfRangeException("dpi", "DPI must be between 1 and 1200.");
            return ConvertFiles(inputPath, outputDirectory, true, dpi, overwrite, cancellationToken, progress);
        }

        public IList<ConversionResult> ConvertTiffToPdf(string inputPath,
            string outputDirectory = null, bool overwrite = false,
            CancellationToken cancellationToken = default(CancellationToken), IProgress<ConversionProgress> progress = null)
        {
            return ConvertFiles(inputPath, outputDirectory, false, 300, overwrite, cancellationToken, progress);
        }

        public Task<IList<ConversionResult>> ConvertPdfToTiffAsync(string inputPath,
            string outputDirectory = null, int dpi = 300, bool overwrite = false,
            CancellationToken cancellationToken = default(CancellationToken), IProgress<ConversionProgress> progress = null)
        {
            return Task.Run(() => ConvertPdfToTiff(inputPath, outputDirectory, dpi,
                overwrite, cancellationToken, progress));
        }

        public Task<IList<ConversionResult>> ConvertTiffToPdfAsync(string inputPath,
            string outputDirectory = null, bool overwrite = false,
            CancellationToken cancellationToken = default(CancellationToken), IProgress<ConversionProgress> progress = null)
        {
            return Task.Run(() => ConvertTiffToPdf(inputPath, outputDirectory,
                overwrite, cancellationToken, progress));
        }

        private static bool Matches(string path, bool pdfToTiff)
        {
            string extension = Path.GetExtension(path);
            return pdfToTiff ? extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                : extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
        }

        // Do not follow directory junctions/symbolic links, which can create recursive loops.
        private static IEnumerable<string> EnumerateSources(string directory, bool pdfToTiff,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            foreach (string file in Directory.EnumerateFiles(directory))
                if (Matches(file, pdfToTiff)) yield return file;
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string file in EnumerateSources(child, pdfToTiff, token)) yield return file;
            }
        }

        private static IList<ConversionResult> ConvertFiles(string inputPath, string outputDirectory,
            bool pdfToTiff, int dpi, bool overwrite, CancellationToken token, IProgress<ConversionProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("Input path is required.", "inputPath");
            string input = Path.GetFullPath(inputPath);
            bool isDirectory = Directory.Exists(input);
            if (!isDirectory && !File.Exists(input)) throw new FileNotFoundException("Input path does not exist.", input);
            if (!isDirectory && !Matches(input, pdfToTiff))
                throw new ArgumentException(pdfToTiff ? "Expected a PDF file." : "Expected a TIF or TIFF file.", "inputPath");
            string sourceRoot = isDirectory ? input : Path.GetDirectoryName(input);
            string rootPrefix = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string outputRoot = outputDirectory == null ? null : Path.GetFullPath(outputDirectory);
            token.ThrowIfCancellationRequested();
            // Snapshot before writing so outputs cannot become new inputs during this run.
            string[] files = isDirectory ? EnumerateSources(input, pdfToTiff, token)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray() : new[] { input };
            var results = new List<ConversionResult>();
            Func<string, string> destinationFor = file => Path.ChangeExtension(outputRoot == null ? file
                : Path.Combine(outputRoot, isDirectory ? file.Substring(rootPrefix.Length) : Path.GetFileName(file)),
                pdfToTiff ? ".tiff" : ".pdf");
            Action<string, string, int, int, ConversionResult> report = (file, status, page, pages, result) =>
            {
                if (progress != null) progress.Report(new ConversionProgress {
                    InputPath = file, OutputPath = destinationFor(file), TotalFiles = files.Length,
                    CompletedFiles = results.Count, Page = page, TotalPages = pages, Status = status, Result = result });
            };
            foreach (string file in files) report(file, "Chờ", 0, 0, null);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (token.IsCancellationRequested) throw new ConversionCanceledException(results, token);
                string relative = isDirectory ? file.Substring(rootPrefix.Length) : Path.GetFileName(file);
                string destination = Path.ChangeExtension(outputRoot == null ? file
                    : Path.Combine(outputRoot, relative), pdfToTiff ? ".tiff" : ".pdf");
                var result = new ConversionResult { InputPath = file, OutputPath = destination };
                string temporary = null;
                report(file, "Đang xử lý", 0, 0, null);
                try
                {
                    if (!destinations.Add(destination))
                        throw new IOException("Multiple source files map to the same output: " + destination);
                    if (!overwrite && File.Exists(destination))
                        throw new IOException("Output already exists: " + destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    temporary = Path.Combine(Path.GetDirectoryName(destination), ".conversion-" + Guid.NewGuid().ToString("N") + ".tmp");
                    Action<int, int> pageProgress = (page, pages) => report(file, "Đang xử lý", page, pages, null);
                    result.PageCount = pdfToTiff ? WriteTiff(file, temporary, dpi, token, pageProgress)
                        : WritePdf(file, temporary, token, pageProgress);
                    token.ThrowIfCancellationRequested();
                    // Publish only a complete file. Failed conversions never truncate an existing output.
                    if (overwrite && File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                }
                catch (OperationCanceledException) { report(file, "Đã hủy", 0, 0, null); throw new ConversionCanceledException(results, token); }
                catch (Exception ex) { result.Error = ex; result.PageCount = 0; }
                finally
                {
                    if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
                }
                results.Add(result);
                report(file, result.Success ? "Thành công" : "Lỗi", result.PageCount, result.PageCount, result);
            }
            return results;
        }

        private static Bitmap RenderPage(PdfDocument pdf, int page, int dpi)
        {
            SizeF size = pdf.PageSizes[page];
            int width = checked((int)Math.Ceiling(size.Width * dpi / 72.0));
            int height = checked((int)Math.Ceiling(size.Height * dpi / 72.0));
            using (DrawingImage rendered = pdf.Render(page, width, height, dpi, dpi,
                PdfRenderFlags.ForPrinting | PdfRenderFlags.Annotations))
            {
                var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                try
                {
                    bitmap.SetResolution(dpi, dpi);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.White);
                        graphics.DrawImageUnscaled(rendered, 0, 0);
                    }
                    return bitmap;
                }
                catch { bitmap.Dispose(); throw; }
            }
        }

        private static EncoderParameters TiffParameters(EncoderValue flag)
        {
            var parameters = new EncoderParameters(2);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)flag);
            parameters.Param[1] = new EncoderParameter(System.Drawing.Imaging.Encoder.Compression,
                (long)EncoderValue.CompressionLZW);
            return parameters;
        }

        private static int WriteTiff(string input, string output, int dpi, CancellationToken token, Action<int, int> progress)
        {
            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Tiff.Guid);
            using (PdfDocument pdf = PdfDocument.Load(input))
            {
                if (pdf.PageCount == 0) throw new InvalidDataException("PDF contains no pages.");
                token.ThrowIfCancellationRequested();
                using (Bitmap first = RenderPage(pdf, 0, dpi))
                {
                    if (pdf.PageCount == 1)
                    {
                        using (var parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Compression,
                                (long)EncoderValue.CompressionLZW);
                            first.Save(output, codec, parameters);
                            progress(1, pdf.PageCount);
                        }
                    }
                    else
                    {
                        using (var parameters = TiffParameters(EncoderValue.MultiFrame)) first.Save(output, codec, parameters);
                        progress(1, pdf.PageCount);
                        for (int page = 1; page < pdf.PageCount; page++)
                        {
                            token.ThrowIfCancellationRequested();
                            using (Bitmap frame = RenderPage(pdf, page, dpi))
                            using (var parameters = TiffParameters(EncoderValue.FrameDimensionPage))
                                first.SaveAdd(frame, parameters);
                            progress(page + 1, pdf.PageCount);
                        }
                        using (var parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag,
                                (long)EncoderValue.Flush);
                            first.SaveAdd(parameters);
                        }
                    }
                }
                return pdf.PageCount;
            }
        }

        private static int WritePdf(string input, string output, CancellationToken token, Action<int, int> progress)
        {
            var source = new RandomAccessFileOrArray(input);
            try
            {
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                using (var document = new iTextSharp.text.Document())
                {
                    PdfWriter writer = PdfWriter.GetInstance(document, stream);
                    int count = TiffImage.GetNumberOfPages(source);
                    if (count == 0) throw new InvalidDataException("TIFF contains no pages.");
                    document.Open();
                    for (int page = 1; page <= count; page++)
                    {
                        token.ThrowIfCancellationRequested();
                        iTextSharp.text.Image image = TiffImage.GetTiffImage(source, page);
                        // PDF units are points. Use each frame's DPI; fall back to 300 when absent.
                        float width = image.Width * 72f / (image.DpiX > 0 ? image.DpiX : 300);
                        float height = image.Height * 72f / (image.DpiY > 0 ? image.DpiY : 300);
                        image.ScaleAbsolute(width, height);
                        document.SetPageSize(new iTextSharp.text.Rectangle(width, height));
                        document.SetMargins(0, 0, 0, 0);
                        document.NewPage();
                        image.SetAbsolutePosition(0, 0);
                        writer.DirectContent.AddImage(image);
                        progress(page, count);
                    }
                    document.Close();
                    return count;
                }
            }
            finally { source.Close(); }
        }
    }
}
