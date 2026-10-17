using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CountFile2;
using CountFile2.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Rectangle = iTextSharp.text.Rectangle;
using Image = iTextSharp.text.Image;
using Path = System.IO.Path;

class PdfCompressionTests
{
    static int assertions;
    static void Assert(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    sealed class Callback : IProgress<PdfCompressionProgress>
    { public Action<PdfCompressionProgress> Action; public void Report(PdfCompressionProgress p) { Action(p); } }
    static void Text(PdfContentByte cb, string text)
    { cb.BeginText(); cb.SetFontAndSize(BaseFont.CreateFont(), 14); cb.SetTextMatrix(15, 275); cb.ShowText(text); cb.EndText(); }
    static Image TestImage(int pixels = 1200, bool jpeg = false, long jpegQuality = 100)
    {
        using (var bitmap = new Bitmap(pixels, pixels, PixelFormat.Format24bppRgb))
        {
            bitmap.SetResolution(300, 300); // Real scanner JPEG metadata must not affect placement inside the encoder bitmap.
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font("Arial", pixels / 35f))
            {
                graphics.Clear(Color.White);
                for (int y = pixels / 15; y < pixels - pixels / 10; y += pixels / 12)
                    graphics.DrawString("SCAN SAMPLE 12345", font, Brushes.Black, pixels / 20, y);
                graphics.FillRectangle(Brushes.SteelBlue, pixels / 10, pixels * .8f, pixels * .4f, pixels / 20);
            }
            if (jpeg)
            {
                using (var bytes = new MemoryStream()) using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);
                    bitmap.Save(bytes, ImageCodecInfo.GetImageEncoders().Single(c => c.FormatID == ImageFormat.Jpeg.Guid), parameters);
                    return Image.GetInstance(bytes.ToArray());
                }
            }
            var raw = new byte[pixels * pixels * 3];
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, pixels, pixels), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[pixels * 3];
                for (int y = 0; y < pixels; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < pixels; x++)
                    { int offset = (y * pixels + x) * 3; raw[offset] = row[x * 3 + 2]; raw[offset + 1] = row[x * 3 + 1]; raw[offset + 2] = row[x * 3]; }
                }
            }
            finally { bitmap.UnlockBits(data); }
            var image = Image.GetInstance(pixels, pixels, 3, 8, raw); image.CompressionLevel = 0; return image;
        }
    }
    static void Fixture(string path, int pixels = 1200, bool shared = false, bool form = false, bool inline = false, bool masked = false,
        bool shear = false, bool rotate = false, bool annotationImage = false, bool signed = false, bool jpeg = false)
    {
        using (var stream = File.Create(path))
        using (var document = new Document(new Rectangle(300, 300)))
        {
            var writer = PdfWriter.GetInstance(document, stream); document.Open(); var cb = writer.DirectContent;
            Text(cb, "PRESERVED TEXT 12345");
            var image = TestImage(pixels, jpeg);
            if (masked)
            {
                var mask = Image.GetInstance(pixels, pixels, 1, 8, Enumerable.Repeat((byte)200, pixels * pixels).ToArray());
                mask.MakeMask(); image.ImageMask = mask;
            }
            if (form)
            {
                var template = cb.CreateTemplate(240, 240); template.AddImage(image, 240, 0, 0, 240, 0, 0); cb.AddTemplate(template, 1, 0, 0, 1, 30, 20);
            }
            else if (shear) cb.AddImage(image, 180, 0, 90, 180, 10, 20);
            else if (rotate) cb.AddImage(image, 0, 240, -240, 0, 270, 20);
            else cb.AddImage(image, shared ? 120 : 240, 0, 0, shared ? 120 : 240, 20, 20, inline);
            cb.Rectangle(10, 10, 280, 280); cb.Stroke();
            if (shared) { document.NewPage(); Text(cb, "SECOND PAGE"); cb.AddImage(image, 240, 0, 0, 240, 30, 20); }
            var field = new TextField(writer, new Rectangle(15, 2, 120, 16), "sample-field"); field.Text = "FIELD KEPT"; writer.AddAnnotation(field.GetTextField());
            writer.AddAnnotation(PdfAnnotation.CreateLink(writer, new Rectangle(230, 275, 280, 290), PdfAnnotation.HIGHLIGHT_INVERT, new PdfAction("https://example.com/")));
            if (annotationImage)
            {
                var appearance = PdfAppearance.CreateAppearance(writer, 280, 280); appearance.AddImage(image, 280, 0, 0, 280, 0, 0);
                var annotation = new PdfAnnotation(writer, new Rectangle(10, 10, 290, 290)); annotation.SetAppearance(PdfAnnotation.APPEARANCE_NORMAL, appearance);
                writer.AddAnnotation(annotation);
            }
            if (signed)
            {
                var signature = new PdfDictionary(PdfName.SIG);
                signature.Put(PdfName.BYTERANGE, new PdfArray(new[] { 0, 10, 20, 10 }));
                signature.Put(PdfName.CONTENTS, new PdfString(new byte[16]).SetHexWriting(true));
                signature.Put(PdfName.FILTER, PdfName.ADOBE_PPKLITE); signature.Put(PdfName.SUBFILTER, PdfName.ADBE_PKCS7_DETACHED);
                var signatureField = PdfFormField.CreateSignature(writer); signatureField.FieldName = "signature";
                signatureField.SetWidget(new Rectangle(15, 20, 80, 40), PdfAnnotation.HIGHLIGHT_INVERT);
                signatureField.Put(PdfName.V, writer.AddToBody(signature).IndirectReference); writer.AddAnnotation(signatureField);
            }
            document.AddTitle("Compression fixture"); document.Close();
        }
    }
    static void TextOnly(string path)
    {
        using (var stream = File.Create(path)) using (var document = new Document(new Rectangle(300, 300)))
        { var writer = PdfWriter.GetInstance(document, stream); document.Open(); Text(writer.DirectContent, "VECTOR TEXT ONLY"); document.Close(); }
    }
    static void BoundaryFixture(string path, float points = 596.88f, long quality = 100)
    {
        using (var stream = File.Create(path)) using (var document = new Document(new Rectangle(points + 20, points + 20)))
        {
            var writer = PdfWriter.GetInstance(document, stream); document.Open();
            writer.DirectContent.AddImage(TestImage(2487, true, quality), points, 0, 0, points, 10, 10); document.Close();
        }
    }
    sealed class DpiListener : IRenderListener
    {
        public readonly List<double> Dpis = new List<double>();
        public readonly List<int> Widths = new List<int>();
        readonly double unit;
        public DpiListener(double unit) { this.unit = unit; }
        public void BeginTextBlock() { } public void EndTextBlock() { } public void RenderText(TextRenderInfo info) { }
        public void RenderImage(ImageRenderInfo info)
        {
            var image = info.GetImage(); var matrix = info.GetImageCTM();
            int width = image.GetDictionary().GetAsNumber(PdfName.WIDTH).IntValue, height = image.GetDictionary().GetAsNumber(PdfName.HEIGHT).IntValue;
            double x = Math.Sqrt(matrix[Matrix.I11] * matrix[Matrix.I11] + matrix[Matrix.I12] * matrix[Matrix.I12]);
            double y = Math.Sqrt(matrix[Matrix.I21] * matrix[Matrix.I21] + matrix[Matrix.I22] * matrix[Matrix.I22]);
            Widths.Add(width); Dpis.Add(Math.Min(width * 72 / x, height * 72 / y) / unit);
        }
    }
    static DpiListener Inspect(string path)
    {
        using (var reader = new PdfReader(path))
        {
            var unit = reader.GetPageN(1).GetAsNumber(PdfName.USERUNIT); var listener = new DpiListener(unit == null ? 1 : unit.DoubleValue);
            for (int p = 1; p <= reader.NumberOfPages; p++) new PdfReaderContentParser(reader).ProcessContent(p, listener);
            return listener;
        }
    }
    static Bitmap DecodeFirstImage(string path)
    {
        using (var reader = new PdfReader(path))
        {
            var xobjects = reader.GetPageN(1).GetAsDict(PdfName.RESOURCES).GetAsDict(PdfName.XOBJECT);
            var image = (PRStream)PdfReader.GetPdfObject(xobjects.Get(xobjects.Keys.First()));
            using (var stream = new MemoryStream(new PdfImageObject(image).GetImageAsBytes()))
            using (var bitmap = new Bitmap(stream)) return (Bitmap)bitmap.Clone();
        }
    }
    static PdfCompressionResult Process(PdfCompressionService service, string input, string output, double dpi = 150, double reduction = 40)
    { return service.Process(input, output, new PdfCompressionOptions { MinDpi = dpi, ReductionPercent = reduction }).Single(); }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    static void Pump(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); if (watch.Elapsed.TotalSeconds >= 120) throw new Exception("UI timeout"); }
        Application.DoEvents(); task.GetAwaiter().GetResult();
    }
    static void Capture(Form form, string path)
    { form.PerformLayout(); Application.DoEvents(); using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); } }
    [STAThread] static void Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        string input = Path.Combine(root, "scan.pdf"), output = Path.Combine(root, "out"); Fixture(input);
        var source = File.ReadAllBytes(input); var service = new PdfCompressionService();
        var result = Process(service, input, output);
        Assert(result.Error == null && result.Written && result.TargetReached && result.QualitySatisfied, "scan reaches target: " + result.Details);
        Assert(result.BeforeDpi == 360 && result.AfterDpi >= 150, "DPI metrics");
        Assert(result.OutputBytes == new FileInfo(result.OutputPath).Length && result.OutputBytes <= result.TargetBytes, "accurate bytes");
        Assert(File.ReadAllBytes(input).SequenceEqual(source), "source untouched");
        var measured = Inspect(result.OutputPath); Assert(measured.Dpis.Min() >= 150 && measured.Widths.Single() == 1200, "independent DPI: lossless optimization first");
        using (var reader = new PdfReader(result.OutputPath))
        {
            Assert(PdfTextExtractor.GetTextFromPage(reader, 1).Contains("PRESERVED TEXT 12345"), "selectable text retained");
            Assert(reader.AcroFields.GetField("sample-field") == "FIELD KEPT", "interactive field retained");
            Assert(reader.GetPageN(1).GetAsArray(PdfName.ANNOTS).Size == 2, "form and link retained");
            Assert(reader.Info["Title"] == "Compression fixture", "metadata retained");
        }
        using (var pdf = PdfiumViewer.PdfDocument.Load(result.OutputPath))
        using (var rendered = pdf.Render(0, 900, 900, true)) { rendered.Save(Path.Combine(root, "compressed-preview.png")); Assert(pdf.PageCount == 1, "PDFium can render output"); }
        using (var pdf = PdfiumViewer.PdfDocument.Load(input)) using (var rendered = pdf.Render(0, 900, 900, true)) rendered.Save(Path.Combine(root, "original-preview.png"));

        var unreachable = Process(service, input, Path.Combine(root, "unreachable"), 360, 99.99);
        Assert(unreachable.Written && !unreachable.TargetReached && unreachable.QualitySatisfied, "best fallback written");
        Assert(unreachable.Status == "Không đạt tỷ lệ giảm yêu cầu" && unreachable.DpiNote.Contains("360") && unreachable.Details.Contains("thực tế"), "fallback DPI note and status");
        Assert(unreachable.OutputBytes <= unreachable.OriginalBytes && Inspect(unreachable.OutputPath).Widths.Single() == 1200, "floor forbids resampling");

        string boundary = Path.Combine(root, "exact-300-decimal-matrix.pdf"); BoundaryFixture(boundary);
        var boundaryStages = new List<string>();
        var boundaryResult = service.Process(boundary, output, new PdfCompressionOptions { MinDpi = 300, ReductionPercent = 99.99 },
            progress: new Callback { Action = p => boundaryStages.Add(p.Status) }).Single();
        Assert(boundaryResult.Written && boundaryResult.DpiSatisfied && boundaryResult.BeforeDpi == 300 && boundaryResult.AfterDpi == 300,
            "decimal CTM float noise does not classify exact 300 DPI as below floor: " + boundaryResult.Details);
        Assert(boundaryStages.Contains("Thử mức nén 3/3") && boundaryResult.ActualReduction > 20,
            "JPEG recompression attempted and reduces bytes at equal 300 DPI floor");
        Assert(Inspect(boundaryResult.OutputPath).Widths.Single() == 2487, "equal floor preserves original pixel dimensions");
        using (var originalImage = DecodeFirstImage(boundary)) using (var compressedImage = DecodeFirstImage(boundaryResult.OutputPath))
        {
            double error = 0; int count = 0;
            for (int y = 0; y < originalImage.Height; y += 7) for (int x = 0; x < originalImage.Width; x += 7)
            {
                var a = originalImage.GetPixel(x, y); var b = compressedImage.GetPixel(x, y);
                error += (a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B); count += 3;
            }
            Assert(Math.Sqrt(error / count) / 255 < .035, "300-DPI JPEG metadata must not shrink contents or create black padding");
        }

        string trulyLow = Path.Combine(root, "actual-299-99.pdf"); BoundaryFixture(trulyLow, (float)(2487d * 72 / 299.99));
        var belowStages = new List<string>();
        var belowResult = service.Process(trulyLow, output, new PdfCompressionOptions { MinDpi = 300, ReductionPercent = 99.99 },
            progress: new Callback { Action = p => belowStages.Add(p.Status) }).Single();
        Assert(belowResult.Written && !belowResult.DpiSatisfied && belowResult.BeforeDpi < 300, "genuinely low 299.99 DPI remains below floor");
        Assert(!belowStages.Any(s => s.StartsWith("Thử mức nén")), "genuinely low input remains lossless only");

        string alreadyCompressed = Path.Combine(root, "already-compressed-300.pdf"); BoundaryFixture(alreadyCompressed, quality: 70);
        var alreadyResult = Process(service, alreadyCompressed, output, 300, 99.99);
        Assert(alreadyResult.Written && alreadyResult.DpiSatisfied && alreadyResult.Details.Contains("JPEG chất lượng 85")
            && alreadyResult.Details.Contains("không nhỏ hơn ảnh gốc"), "fallback explains already compressed JPEG instead of blaming DPI");

        string low = Path.Combine(root, "low.pdf"); Fixture(low, 240);
        var lowResult = Process(service, low, output, 150, 99.99);
        Assert(lowResult.Written && !lowResult.DpiSatisfied && !lowResult.QualitySatisfied && lowResult.AfterDpi == 72, "low input flagged, never upscaled");
        Assert(Inspect(lowResult.OutputPath).Widths.Single() == 240 && lowResult.Details.Contains("đầu vào đã dưới"), "low DPI kept with reason");
        string text = Path.Combine(root, "text.pdf"); TextOnly(text);
        var textResult = Process(service, text, output, 150, 99.99);
        Assert(textResult.Written && textResult.AfterDpi == null && textResult.DpiNote.Contains("Không áp dụng"), "vector DPI N/A");
        Assert(textResult.OutputBytes <= textResult.OriginalBytes, "never grows output");

        string shared = Path.Combine(root, "shared.pdf"); Fixture(shared, shared: true);
        var sharedResult = Process(service, shared, output, 150, 99.99);
        Assert(sharedResult.Error == null && sharedResult.PageCount == 2 && Inspect(sharedResult.OutputPath).Dpis.Min() >= 150, "shared image largest placement protected");
        string jpegSource = Path.Combine(root, "jpeg.pdf"); Fixture(jpegSource, shared: true, jpeg: true);
        var jpegResult = Process(service, jpegSource, output, 150, 99.99);
        Assert(jpegResult.Written && Inspect(jpegResult.OutputPath).Dpis.Min() >= 150 && Inspect(jpegResult.OutputPath).Widths.Min() < 1200,
            "JPEG resampling uses largest shared placement: " + jpegResult.Details);
        using (var pdf = PdfiumViewer.PdfDocument.Load(jpegSource)) using (var bitmap = pdf.Render(1, 900, 900, true)) bitmap.Save(Path.Combine(root, "jpeg-original-preview.png"));
        using (var pdf = PdfiumViewer.PdfDocument.Load(jpegResult.OutputPath)) using (var bitmap = pdf.Render(1, 900, 900, true)) bitmap.Save(Path.Combine(root, "jpeg-compressed-preview.png"));
        string form = Path.Combine(root, "form.pdf"); Fixture(form, form: true);
        var formResult = Process(service, form, output, 150, 99.99);
        Assert(formResult.Written && formResult.BeforeDpi == 360 && Inspect(formResult.OutputPath).Dpis.Min() >= 150, "nested Form image");
        string inline = Path.Combine(root, "inline.pdf"); Fixture(inline, inline: true);
        var inlineResult = Process(service, inline, output, 150, 99.99);
        Assert(inlineResult.Written && Inspect(inlineResult.OutputPath).Widths.Single() == 1200, "inline preserved");
        string masked = Path.Combine(root, "masked.pdf"); Fixture(masked, masked: true);
        var maskedResult = Process(service, masked, output, 150, 99.99);
        Assert(maskedResult.Written && Inspect(maskedResult.OutputPath).Widths.Single() == 1200, "masked image preserved");
        using (var pdf = PdfiumViewer.PdfDocument.Load(maskedResult.OutputPath)) using (var bitmap = pdf.Render(0, 600, 600, true)) bitmap.Save(Path.Combine(root, "masked-preview.png"));
        string unit = Path.Combine(root, "unit.pdf");
        using (var reader = new PdfReader(input)) using (var stream = File.Create(unit)) using (var stamper = new PdfStamper(reader, stream))
            reader.GetPageN(1).Put(PdfName.USERUNIT, new PdfNumber(2));
        var unitResult = Process(service, unit, output, 150, 99.99);
        Assert(unitResult.Written && Math.Abs(unitResult.BeforeDpi.Value - 180) < .001 && Inspect(unitResult.OutputPath).Dpis.Min() >= 150, "UserUnit respected");
        string sheared = Path.Combine(root, "shear.pdf"); Fixture(sheared, shear: true);
        var shearResult = Process(service, sheared, output, 150, 99.99);
        Assert(shearResult.Written && shearResult.BeforeDpi < 1200 * 72 / Math.Sqrt(180 * 180 + 90 * 90)
            && shearResult.AfterDpi >= 150, "conservative DPI includes shear maximum stretch");
        string rotated = Path.Combine(root, "rotated.pdf"); Fixture(rotated, rotate: true);
        var rotatedResult = Process(service, rotated, output, 150, 99.99);
        Assert(rotatedResult.Written && Math.Abs(rotatedResult.BeforeDpi.Value - 360) < .001 && Inspect(rotatedResult.OutputPath).Dpis.Min() >= 150, "rotated image DPI");
        string annotationSource = Path.Combine(root, "annotation-image.pdf"); Fixture(annotationSource, annotationImage: true);
        var annotationResult = Process(service, annotationSource, output, 150, 99.99);
        Assert(annotationResult.Written && Inspect(annotationResult.OutputPath).Widths.Single() == 1200, "image shared with annotation appearance never resized");
        string signedSource = Path.Combine(root, "signed.pdf"); Fixture(signedSource, signed: true);
        var signedResult = Process(service, signedSource, output);
        Assert(signedResult.Error != null && signedResult.Error.Contains("chữ ký") && !File.Exists(signedResult.OutputPath), "signature dictionary detected and protected: " + signedResult.Details);
        string encrypted = Path.Combine(root, "encrypted.pdf");
        using (var reader = new PdfReader(input)) using (var stream = File.Create(encrypted)) using (var stamper = new PdfStamper(reader, stream))
            stamper.SetEncryption(System.Text.Encoding.UTF8.GetBytes("secret"), System.Text.Encoding.UTF8.GetBytes("owner"), 0, PdfWriter.ENCRYPTION_AES_128);
        Assert(Process(service, encrypted, output).Error != null, "encrypted PDF rejected");
        string batch = Path.Combine(root, "thư mục tiếng Việt"); Directory.CreateDirectory(Path.Combine(batch, "nested"));
        File.Copy(input, Path.Combine(batch, "a.PDF")); File.Copy(text, Path.Combine(batch, "nested", "a.pdf"));
        File.WriteAllText(Path.Combine(batch, "b-broken.pdf"), "broken");
        File.Copy(input, Path.Combine(batch, "previous.compressed.pdf"));
        string nestedOut = Path.Combine(batch, "out"); Directory.CreateDirectory(nestedOut); File.Copy(input, Path.Combine(nestedOut, "previous.pdf"));
        var batchResults = service.Process(batch, nestedOut);
        Assert(batchResults.Count == 3 && batchResults.Count(r => r.Written) == 2 && batchResults.Count(r => r.Error != null) == 1, "recursive mixed batch/exclude generated and output");
        Assert(batchResults.Any(r => r.OutputPath == Path.Combine(nestedOut, "nested", "a.compressed.pdf")), "subfolders preserved and duplicate names separate");
        Assert(service.Process(batch, Path.Combine(root, "nonrecursive"), new PdfCompressionOptions { Recursive = false }).Count == 2, "nonrecursive");
        Assert(Process(service, input, output).Error != null, "existing output not overwritten");
        Assert(service.Process(input, output, new PdfCompressionOptions { Overwrite = true }).Single().Written, "explicit output overwrite");
        Assert(service.Process(text).Single().Written, "default output folder");
        string collision = Path.Combine(root, "collision"); Directory.CreateDirectory(Path.Combine(collision, "scan.compressed.pdf"));
        Assert(Process(service, input, collision).Error != null, "publish collision isolated");
        bool invalid = false; try { Process(service, input, output, double.NaN); } catch (ArgumentException) { invalid = true; } Assert(invalid, "NaN DPI rejected");
        foreach (double percent in new[] { 0d, 100d, -1d, double.NaN, double.PositiveInfinity })
        { invalid = false; try { Process(service, input, output, 150, percent); } catch (ArgumentException) { invalid = true; } Assert(invalid, "bad percent rejected"); }
        invalid = false; try { service.Process(batch, batch); } catch (ArgumentException) { invalid = true; } Assert(invalid, "source/output identical rejected");

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); try { service.Process(input, output, token: cts.Token); throw new Exception("cancel missing"); } catch (OperationCanceledException) { assertions++; }
        }
        using (var cts = new CancellationTokenSource())
        {
            try
            {
                service.Process(batch, Path.Combine(root, "partial"), token: cts.Token, progress: new Callback { Action = p => { if (p.Result != null) cts.Cancel(); } });
                throw new Exception("partial cancel missing");
            }
            catch (PdfCompressionCanceledException ex) { Assert(ex.Results.Count == 1 && ex.Results[0].Written, "partial cancellation retains completed result"); }
        }
        using (var cts = new CancellationTokenSource())
        {
            string canceledOut = Path.Combine(root, "cancel-active");
            try
            {
                service.Process(input, canceledOut, token: cts.Token, progress: new Callback { Action = p => { if (p.Status == "Tối ưu không mất chất lượng") cts.Cancel(); } });
                throw new Exception("active cancel missing");
            }
            catch (PdfCompressionCanceledException ex) { Assert(ex.Results.Count == 0 && !File.Exists(Path.Combine(canceledOut, "scan.compressed.pdf")), "cancel before publish"); }
        }
        Assert(!Directory.GetFiles(root, ".pdf-compression-*", SearchOption.AllDirectories).Any(), "no temporary leaks");
        string csv = Path.Combine(root, "report.csv"); PdfCompressionService.ExportCsv(csv, new[] { result, unreachable, lowResult, textResult }.Concat(batchResults));
        Assert(File.ReadAllText(csv).Contains("Không đạt tỷ lệ giảm yêu cầu") && File.ReadAllText(csv).Contains("DPI thấp nhất") && File.ReadAllText(csv).Contains("b-broken.pdf"), "CSV includes failure status, DPI, per-file errors");

        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using (var main = new frmMain())
        {
            main.Show(); main.Location = new Point(-2500, -2500); Application.DoEvents(); main.Navigate("Giảm dung lượng PDF");
            var screen = main.Navigation.Current as frmPdfCompression; Assert(screen != null, "navigation opens compression screen");
            Field<TextBox>(screen, "source").Text = input; Field<NumericUpDown>(screen, "minDpi").Value = 180; Field<NumericUpDown>(screen, "reduction").Value = 65;
            main.Navigate("TIFF / PDF"); main.Navigate("Giảm dung lượng PDF");
            Assert(ReferenceEquals(screen, main.Navigation.Current) && Field<NumericUpDown>(screen, "minDpi").Value == 180, "cached screen/config state");
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var task = screen.RunCompressionAsync(input, Path.Combine(root, "ui-out"), new PdfCompressionOptions());
            Assert(DocumentJobCoordinator.Shared.IsBusy && !Field<Button>(screen, "start").Enabled && Field<Button>(screen, "cancel").Enabled, "async UI acquires coordinator and disables controls");
            main.Navigate("TIFF / PDF"); Pump(task);
            main.Navigate("Giảm dung lượng PDF");
            Assert(screen.Results.Count == 1 && screen.Results[0].Written && Field<Button>(screen, "start").Enabled, "UI completion survives navigation");
            Assert(Directory.GetFiles(Path.Combine(root, "ui-out"), "*.csv").Length == 1, "automatic CSV report");
            main.ClientSize = new Size(1050, 680); Capture(main, Path.Combine(root, "ui-minimum.png"));
            main.Scale(new SizeF(1.25f, 1.25f)); Capture(main, Path.Combine(root, "ui-125.png"));
            main.Scale(new SizeF(1.2f, 1.2f)); Capture(main, Path.Combine(root, "ui-150.png"));
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            task = screen.RunCompressionAsync(batch, Path.Combine(root, "ui-cancel"), new PdfCompressionOptions());
            DocumentJobCoordinator.Shared.Cancel(); Pump(task);
            Assert(!DocumentJobCoordinator.Shared.IsBusy && Field<Label>(screen, "status").Text.StartsWith("Đã hủy"), "UI cancellation releases coordinator");
            main.Close(); Assert(screen.IsDisposed, "screen disposed by shell");
        }
        Console.WriteLine("PASS: " + assertions + " assertions. Fixtures: " + root);
        Console.WriteLine("Scan: " + result.OriginalBytes + " -> " + result.OutputBytes + ", " + result.DpiNote);
        Console.WriteLine("Fallback: " + unreachable.Status + ", " + unreachable.ActualReduction.ToString("F2") + "%, " + unreachable.DpiNote);
    }
}
