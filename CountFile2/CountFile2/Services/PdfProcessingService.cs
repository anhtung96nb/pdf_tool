using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using iTextSharp.text.pdf;
using Tesseract;

namespace CountFile2.Services
{
    public sealed class PdfProcessingOptions
    {
        public int Dpi { get; set; } = 300;
        public string Language { get; set; } = "vie";
        public string TessDataPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        public string FontPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        public string ColorProfilePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\drivers\color\sRGB Color Space Profile.icm");
        public bool Overwrite { get; set; }
        public bool Recursive { get; set; } = true;
    }

    /// <summary>Rasterizes pages, optionally adds invisible OCR text, and writes PDF or PDF/A-1b.</summary>
    public sealed class PdfProcessingService
    {
        public IList<ConversionResult> ConvertToSearchablePdf(string inputPath, string outputDirectory = null,
            PdfProcessingOptions options = null, CancellationToken cancellationToken = default(CancellationToken),
            IProgress<ConversionProgress> progress = null)
        { return Convert(inputPath, outputDirectory, options, true, false, cancellationToken, progress); }

        public IList<ConversionResult> ConvertToPdfA(string inputPath, string outputDirectory = null,
            PdfProcessingOptions options = null, bool searchable = false,
            CancellationToken cancellationToken = default(CancellationToken), IProgress<ConversionProgress> progress = null)
        { return Convert(inputPath, outputDirectory, options, searchable, true, cancellationToken, progress); }

        public Task<IList<ConversionResult>> ConvertToSearchablePdfAsync(string inputPath, string outputDirectory = null,
            PdfProcessingOptions options = null, CancellationToken cancellationToken = default(CancellationToken),
            IProgress<ConversionProgress> progress = null)
        { return Task.Run(() => ConvertToSearchablePdf(inputPath, outputDirectory, options, cancellationToken, progress), cancellationToken); }

        public Task<IList<ConversionResult>> ConvertToPdfAAsync(string inputPath, string outputDirectory = null,
            PdfProcessingOptions options = null, bool searchable = false,
            CancellationToken cancellationToken = default(CancellationToken), IProgress<ConversionProgress> progress = null)
        { return Task.Run(() => ConvertToPdfA(inputPath, outputDirectory, options, searchable, cancellationToken, progress), cancellationToken); }

        private static IEnumerable<string> Scan(string directory, bool recursive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            foreach (string file in Directory.GetFiles(directory))
                if (string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase)) yield return file;
            if (recursive)
                foreach (string child in Directory.GetDirectories(directory))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        foreach (string file in Scan(child, true, token)) yield return file;
        }

        private static IList<ConversionResult> Convert(string inputPath, string outputDirectory, PdfProcessingOptions options,
            bool ocr, bool pdfa, CancellationToken token, IProgress<ConversionProgress> progress)
        {
            options = options ?? new PdfProcessingOptions();
            if (options.Dpi < 72 || options.Dpi > 600) throw new ArgumentOutOfRangeException("Dpi", "Use 72-600 DPI.");
            if (ocr && (string.IsNullOrWhiteSpace(options.Language) || !Directory.Exists(options.TessDataPath)))
                throw new ArgumentException("OCR requires a language and a tessdata directory.");
            if (ocr && !File.Exists(options.FontPath)) throw new FileNotFoundException("OCR font not found.", options.FontPath);
            if (pdfa && !File.Exists(options.ColorProfilePath)) throw new FileNotFoundException("sRGB ICC profile not found.", options.ColorProfilePath);
            string input = Path.GetFullPath(inputPath);
            bool folder = Directory.Exists(input);
            if (!folder && !File.Exists(input)) throw new FileNotFoundException("Input not found.", input);
            if (!folder && !string.Equals(Path.GetExtension(input), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Input must be a PDF or directory.", "inputPath");
            string root = (folder ? input : Path.GetDirectoryName(input)).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string suffix = pdfa ? (ocr ? ".ocr.pdfa.pdf" : ".pdfa.pdf") : ".ocr.pdf";
            string[] files = folder ? Scan(input, options.Recursive, token).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() : new[] { input };
            var results = new List<ConversionResult>();
            var sources = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            string outputRoot = outputDirectory == null ? null : Path.GetFullPath(outputDirectory);
            Func<string, string> destinationFor = file => {
                string relative = folder ? file.Substring(root.Length) : Path.GetFileName(file);
                string target = outputRoot == null ? file : Path.Combine(outputRoot, relative);
                return Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target) + suffix);
            };
            // Publish the scanned snapshot before conversion so canceled/pending files remain visible.
            foreach (string file in files) {
                token.ThrowIfCancellationRequested();
                if (progress != null) progress.Report(new ConversionProgress { InputPath = file,
                    OutputPath = destinationFor(file), TotalFiles = files.Length, Status = "Chờ" });
            }
            foreach (string file in files)
            {
                if (token.IsCancellationRequested) throw new ConversionCanceledException(results, token);
                string target = destinationFor(file);
                var result = new ConversionResult { InputPath = file, OutputPath = target };
                string temporary = null;
                Action<int, int, string> report = (page, pages, status) => {
                    if (progress != null) progress.Report(new ConversionProgress { InputPath = file, OutputPath = target,
                        TotalFiles = files.Length, CompletedFiles = results.Count, Page = page, TotalPages = pages,
                        Status = status, Result = status == "Hoàn tất" || status == "Lỗi" ? result : null });
                };
                try
                {
                    if (sources.Contains(target)) throw new IOException("Output would replace an input PDF.");
                    if (!options.Overwrite && File.Exists(target)) throw new IOException("Output already exists: " + target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    temporary = Path.Combine(Path.GetDirectoryName(target), ".pdf-processing-" + Guid.NewGuid().ToString("N") + ".tmp");
                    result.PageCount = Write(file, temporary, options, ocr, pdfa, token, (p, n) => report(p, n, "Đang xử lý"));
                    token.ThrowIfCancellationRequested();
                    if (options.Overwrite && File.Exists(target)) File.Replace(temporary, target, null);
                    else File.Move(temporary, target);
                }
                catch (OperationCanceledException) { throw new ConversionCanceledException(results, token); }
                catch (Exception ex) { result.Error = ex; result.PageCount = 0; }
                finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
                results.Add(result);
                report(result.PageCount, result.PageCount, result.Success ? "Hoàn tất" : "Lỗi");
            }
            return results.AsReadOnly();
        }

        private static int Write(string source, string target, PdfProcessingOptions options, bool ocr, bool pdfa,
            CancellationToken token, Action<int, int> report)
        {
            using (var pdf = PdfiumViewer.PdfDocument.Load(source))
            using (var engine = ocr ? new TesseractEngine(options.TessDataPath, options.Language, EngineMode.Default) : null)
            using (var stream = File.Create(target))
            using (var document = new iTextSharp.text.Document())
            {
                if (pdf.PageCount == 0) throw new InvalidDataException("PDF has no pages.");
                PdfWriter writer = pdfa ? PdfAWriter.GetInstance(document, stream, PdfAConformanceLevel.PDF_A_1B)
                    : PdfWriter.GetInstance(document, stream);
                document.AddCreator("CountFile2 PdfProcessingService");
                writer.CreateXmpMetadata();
                document.Open();
                if (pdfa) writer.SetOutputIntents("sRGB IEC61966-2.1", "", "http://www.color.org", "sRGB IEC61966-2.1",
                    ICC_Profile.GetInstance(File.ReadAllBytes(options.ColorProfilePath)));
                BaseFont font = ocr ? BaseFont.CreateFont(options.FontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED) : null;
                for (int index = 0; index < pdf.PageCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SizeF size = pdf.PageSizes[index];
                    int width = checked((int)Math.Ceiling(size.Width * options.Dpi / 72f));
                    int height = checked((int)Math.Ceiling(size.Height * options.Dpi / 72f));
                    if ((long)width * height > 100000000) throw new InvalidDataException("Page exceeds 100 megapixels; reduce DPI.");
                    document.SetPageSize(new iTextSharp.text.Rectangle(size.Width, size.Height));
                    document.NewPage();
                    using (var rendered = pdf.Render(index, width, height, options.Dpi, options.Dpi, PdfiumViewer.PdfRenderFlags.ForPrinting))
                    using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                    {
                        using (var graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.White); graphics.DrawImage(rendered, 0, 0, width, height); }
                        using (var bytes = new MemoryStream())
                        {
                            bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
                            var image = iTextSharp.text.Image.GetInstance(bytes.ToArray());
                            image.ScaleAbsolute(size.Width, size.Height); image.SetAbsolutePosition(0, 0);
                            writer.DirectContent.AddImage(image);
                        }
                        if (ocr)
                            using (var pix = PixConverter.ToPix(bitmap))
                            using (var page = engine.Process(pix))
                            using (var iterator = page.GetIterator())
                            {
                                iterator.Begin();
                                do
                                {
                                    token.ThrowIfCancellationRequested();
                                    Rect box;
                                    string word = iterator.GetText(PageIteratorLevel.Word);
                                    if (string.IsNullOrWhiteSpace(word) || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out box)) continue;
                                    word = word.Trim();
                                    float fontSize = Math.Max(1, box.Height * size.Height / height);
                                    float textWidth = font.GetWidthPoint(word, fontSize);
                                    if (textWidth <= 0) continue;
                                    var content = writer.DirectContent;
                                    content.BeginText(); content.SetFontAndSize(font, fontSize);
                                    content.SetTextRenderingMode(PdfContentByte.TEXT_RENDER_MODE_INVISIBLE);
                                    content.SetHorizontalScaling(100 * box.Width * size.Width / width / textWidth);
                                    content.SetTextMatrix(box.X1 * size.Width / width, size.Height - box.Y2 * size.Height / height);
                                    content.ShowText(word + " "); content.EndText();
                                } while (iterator.Next(PageIteratorLevel.Word));
                            }
                    }
                    report(index + 1, pdf.PageCount);
                }
                document.Close();
                return pdf.PageCount;
            }
        }
    }
}
