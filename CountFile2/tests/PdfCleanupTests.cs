using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CountFile2;
using CountFile2.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Path = System.IO.Path;
using Rectangle = iTextSharp.text.Rectangle;
class PdfCleanupTests
{
    static int assertions;
    static void Assert(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    sealed class Callback : IProgress<PdfCleanupProgress>
    { public Action<PdfCleanupProgress> Action; public void Report(PdfCleanupProgress p) { Action(p); } }
    static void Text(PdfContentByte cb, float x, float y, string text, float size = 14)
    { cb.BeginText(); cb.SetFontAndSize(BaseFont.CreateFont(), size); cb.SetTextMatrix(x, y); cb.ShowText(text); cb.EndText(); }
    static void ImagePage(PdfContentByte cb, int width, int height, bool faint = false, bool blurred = false)
    {
        using (var image = new Bitmap(width, height))
        {
            using (var g = Graphics.FromImage(image))
            using (var font = new System.Drawing.Font("Arial", Math.Max(8, width / 45f)))
            using (var brush = new SolidBrush(faint ? Color.FromArgb(225, 225, 225) : Color.Black))
            { g.Clear(Color.White); for (int y = height / 10; y < height * .8; y += height / 22) g.DrawString("DOCUMENT SAMPLE 12345 TEST CONTENT", font, brush, width / 12, y); }
            if (blurred)
            {
                using (var small = new Bitmap(image, 100, 140)) using (var g = Graphics.FromImage(image))
                { g.Clear(Color.White); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(small, 0, 0, width, height); }
            }
            using (var stream = new MemoryStream())
            { image.Save(stream, ImageFormat.Png); var pdfImage = iTextSharp.text.Image.GetInstance(stream.ToArray()); pdfImage.ScaleAbsolute(595, 842); pdfImage.SetAbsolutePosition(0, 0); cb.AddImage(pdfImage); }
        }
    }
    static void Fixture(string path)
    {
        using (var stream = File.Create(path)) using (var doc = new Document(new Rectangle(595, 842)))
        {
            var writer = PdfWriter.GetInstance(doc, stream); doc.Open(); var cb = writer.DirectContent;
            Text(cb, 50, 780, "ORIGINAL TEXT KEPT 12345");
            doc.NewPage(); writer.PageEmpty = false; // Genuine empty content stream.
            doc.NewPage(); cb.SetColorFill(new BaseColor(250, 250, 250)); Text(cb, 50, 780, "VERY FAINT CONTENT MUST STAY"); cb.SetColorFill(BaseColor.BLACK);
            doc.NewPage(); Text(cb, 565, 15, "1", 6); cb.Circle(60, 60, 3); cb.Stroke();
            doc.NewPage(); cb.SaveState(); double a = -3 * Math.PI / 180;
            cb.ConcatCTM(Math.Cos(a), Math.Sin(a), -Math.Sin(a), Math.Cos(a), -20, 20);
            for (int row = 0; row < 24; row++) Text(cb, 50, 760 - row * 25, "ALIGNMENT LINE " + row + " ABCDEFG HIJKLMN 123456", 14);
            cb.RestoreState();
            doc.NewPage(); ImagePage(cb, 414, 585);
            doc.NewPage(); ImagePage(cb, 1240, 1754, true);
            doc.NewPage(); ImagePage(cb, 1240, 1754, false, true);
            doc.NewPage(); writer.AddAnnotation(PdfAnnotation.CreateText(writer, new Rectangle(30, 30, 50, 50), "Note", "KEEP ANNOTATION", false, null)); writer.PageEmpty = false;
        }
    }
    [STAThread] static void Main(string[] args)
    { try { Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true; Run(args); } catch (Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } }
    static void Run(string[] args)
    {
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        string input = Path.Combine(root, "sample.pdf"), output = Path.Combine(root, "output"); Fixture(input);
        byte[] original = File.ReadAllBytes(input);
        var service = new PdfCleanupService();
        var analysis = service.Process(input, output).Single();
        PdfCleanupService.ExportCsv(Path.Combine(root, "analysis.csv"), new[] { analysis });
        using (var pdf = PdfiumViewer.PdfDocument.Load(input)) using (var image = pdf.Render(4, 1240, 1754, 150, 150, PdfiumViewer.PdfRenderFlags.Annotations)) image.Save(Path.Combine(root, "analysis-render.png"));
        Assert(analysis.Error == null, "analysis: " + analysis.Error);
        Assert(!Directory.Exists(output), "analysis must not write PDFs/directories");
        Assert(analysis.Pages.Count == 9 && analysis.Pages.Count(p => p.Remove) == 1, "only real blank removed");
        Assert(analysis.Pages[1].Remove && analysis.Pages[1].OutputPage == 0 && analysis.Pages[2].OutputPage == 2, "original/output page map");
        Assert(!analysis.Pages[2].Remove && !analysis.Pages[3].Remove && !analysis.Pages[8].Remove, "faint/stamp/annotation retained");
        Console.WriteLine("Skew detected: " + analysis.Pages[4].SkewDegrees + " confidence: " + analysis.Pages[4].SkewConfidence + " ink: " + analysis.Pages[4].InkFraction);
        Assert(analysis.Pages[4].ApplyDeskew && Math.Abs(analysis.Pages[4].SkewDegrees - 3) <= .5, "3 degree skew detected");
        Assert(analysis.Pages[5].LowQuality && analysis.Pages[5].ImageDpi < 60, "source image DPI, independent of rendering DPI");
        Assert(analysis.Pages[6].QualityReasons.Any(x => x.Contains("Tương phản")), "faint scan contrast");
        Console.WriteLine("Blur sharpness: " + analysis.Pages[7].Sharpness);
        Assert(analysis.Pages[7].QualityReasons.Any(x => x.Contains("mờ")), "blurred scan flagged");
        Assert(!analysis.Pages[0].LowQuality, "native vector text not marked low resolution");
        var result = service.Process(input, output, new PdfCleanupOptions { AnalyzeOnly = false }).Single();
        Assert(result.Written && result.Error == null, "write: " + result.Error);
        Assert(original.SequenceEqual(File.ReadAllBytes(input)), "source unchanged");
        using (var reader = new PdfReader(result.OutputPath))
        {
            Assert(reader.NumberOfPages == 8, "page count after delete");
            Assert(PdfTextExtractor.GetTextFromPage(reader, 1).Contains("ORIGINAL TEXT KEPT"), "selectable native text preserved");
            Assert(PdfTextExtractor.GetTextFromPage(reader, 4).Contains("ALIGNMENT LINE 23"), "deskew preserves text");
            Assert(reader.GetPageN(8).GetAsArray(PdfName.ANNOTS) != null, "annotation preserved");
        }
        using (var pdf = PdfiumViewer.PdfDocument.Load(result.OutputPath))
        using (var image = pdf.Render(3, 1300, 1850, true))
        using (var bitmap = new Bitmap(image))
        {
            var measured = PdfPageAnalyzer.Analyze(bitmap, new PdfCleanupOptions());
            Console.WriteLine("Residual angle: " + measured.SkewDegrees);
            Assert(Math.Abs(measured.SkewDegrees) <= .5, "output really straight"); bitmap.Save(Path.Combine(root, "after.png"));
        }
        using (var pdf = PdfiumViewer.PdfDocument.Load(input)) using (var image = pdf.Render(4, 1300, 1850, true)) image.Save(Path.Combine(root, "before.png"));
        Assert(service.Process(input, output, new PdfCleanupOptions { AnalyzeOnly = false }).Single().Error != null, "existing output protected");
        Assert(service.Process(input, output, new PdfCleanupOptions { AnalyzeOnly = false, Overwrite = true }).Single().Written, "overwrite output allowed");
        var retained = service.Process(input, Path.Combine(root, "retained"), new PdfCleanupOptions { AnalyzeOnly = false, RemoveBlankPages = false, Deskew = false, CheckQuality = false }).Single();
        Assert(retained.KeptPages == 9 && retained.Pages.All(p => !p.ApplyDeskew && !p.LowQuality), "toggles respected");
        string blank = Path.Combine(root, "blank.pdf");
        using (var stream = File.Create(blank)) using (var doc = new Document()) { var writer = PdfWriter.GetInstance(doc, stream); doc.Open(); writer.PageEmpty = false; }
        var allBlank = service.Process(blank, output, new PdfCleanupOptions { AnalyzeOnly = false }).Single();
        Assert(allBlank.AllBlank && !allBlank.Written && !File.Exists(allBlank.OutputPath), "no zero-page PDF");
        string emptyAnnots = Path.Combine(root, "empty-annots.pdf");
        using (var reader = new PdfReader(blank)) using (var stream = File.Create(emptyAnnots)) using (var stamper = new PdfStamper(reader, stream))
            reader.GetPageN(1).Put(PdfName.ANNOTS, new PdfArray());
        Assert(service.Process(emptyAnnots, output).Single().AllBlank, "empty annotations array does not hide blank page");
        string batch = Path.Combine(root, "batch"); Directory.CreateDirectory(Path.Combine(batch, "nested"));
        File.Copy(blank, Path.Combine(batch, "nested", "EMPTY.PDF")); File.WriteAllText(Path.Combine(batch, "bad.pdf"), "broken");
        string nestedOut = Path.Combine(batch, "out"); Directory.CreateDirectory(nestedOut); File.Copy(input, Path.Combine(nestedOut, "previous.pdf"));
        File.Copy(blank, Path.Combine(batch, "previous.clean.pdf"));
        var batchResults = service.Process(batch, nestedOut);
        Assert(batchResults.Count == 2 && batchResults.Count(r => r.Error != null) == 1, "recursive, exclude output, exclude generated, corrupt file continues");
        Assert(service.Process(batch, nestedOut, new PdfCleanupOptions { Recursive = false }).Count == 1, "nonrecursive");
        bool rejected = false; try { service.Process(batch, batch); } catch (ArgumentException) { rejected = true; } Assert(rejected, "same source/output rejected");
        rejected = false; try { service.Process(input, output, new PdfCleanupOptions { MinContrast = double.NaN }); } catch (ArgumentException) { rejected = true; } Assert(rejected, "invalid thresholds rejected");
        var cts = new CancellationTokenSource(); cts.Cancel();
        try { service.Process(input, output, token: cts.Token); throw new Exception("cancel missing"); } catch (OperationCanceledException) { assertions++; }
        string cancelDir = Path.Combine(root, "cancel"); Directory.CreateDirectory(cancelDir); File.Copy(blank, Path.Combine(cancelDir, "a.pdf")); File.Copy(input, Path.Combine(cancelDir, "b.pdf"));
        cts = new CancellationTokenSource();
        try
        {
            service.Process(cancelDir, Path.Combine(root, "cancel-out"), new PdfCleanupOptions { AnalyzeOnly = false }, cts.Token,
                new Callback { Action = p => { if (p.Result != null) cts.Cancel(); } });
            throw new Exception("partial cancel missing");
        }
        catch (PdfCleanupCanceledException ex) { Assert(ex.Results.Count == 1, "partial results retained"); }
        Assert(!Directory.GetFiles(root, ".pdf-cleanup-*", SearchOption.AllDirectories).Any(), "no temporary leak");
        string collisionOut = Path.Combine(root, "collision-out"); Directory.CreateDirectory(Path.Combine(collisionOut, "sample.clean.pdf"));
        var collision = service.Process(input, collisionOut, new PdfCleanupOptions { AnalyzeOnly = false }).Single();
        Assert(collision.Error != null && !collision.Written && !Directory.GetFiles(collisionOut, ".pdf-cleanup-*").Any(), "failed publish removes temporary PDF");
        using (var white = new Bitmap(300, 400))
        {
            using (var g = Graphics.FromImage(white)) { g.Clear(Color.White); g.FillRectangle(new SolidBrush(Color.FromArgb(254, 254, 254)), 10, 10, 20, 10); }
            Assert(!PdfPageAnalyzer.Analyze(white, new PdfCleanupOptions()).IsBlank, "near white raster mark retained");
            using (var g = Graphics.FromImage(white)) { g.Clear(Color.White); g.FillRectangle(Brushes.Gray, 3, 3, 1, 1); }
            Assert(!PdfPageAnalyzer.Analyze(white, new PdfCleanupOptions()).IsBlank, "noisy white page retained for review");
        }
        string csv = Path.Combine(root, "report.csv"); PdfCleanupService.ExportCsv(csv, new[] { result, batchResults.Single(r => r.Error != null) });
        Assert(File.ReadAllText(csv).Contains("bad.pdf") && File.ReadAllText(csv).Contains("Tương phản"), "CSV includes errors and reasons");
        PdfCleanupService.ExportCsv(Path.Combine(root, "low.csv"), new[] { result }, true);
        Assert(File.ReadAllLines(Path.Combine(root, "low.csv")).Length == result.Pages.Count(p => p.LowQuality) + 1, "filtered report");
        string scanSource = Path.Combine(root, "scan-skew.pdf");
        using (var sourcePdf = PdfiumViewer.PdfDocument.Load(input))
        using (var rendered = sourcePdf.Render(4, 1240, 1754, true))
        using (var bytes = new MemoryStream())
        using (var stream = File.Create(scanSource))
        using (var doc = new Document(new Rectangle(595, 842)))
        {
            rendered.Save(bytes, ImageFormat.Png); var writer = PdfWriter.GetInstance(doc, stream); doc.Open();
            var scanImage = iTextSharp.text.Image.GetInstance(bytes.ToArray()); scanImage.ScaleAbsolute(595, 842); scanImage.SetAbsolutePosition(0, 0); writer.DirectContent.AddImage(scanImage);
        }
        var scanResult = service.Process(scanSource, output, new PdfCleanupOptions { AnalyzeOnly = false }).Single();
        Assert(scanResult.Written && scanResult.Pages[0].ApplyDeskew, "raster scan deskew: " + scanResult.Error);
        using (var pdf = PdfiumViewer.PdfDocument.Load(scanResult.OutputPath)) using (var image = pdf.Render(0, 1240, 1754, true)) using (var bitmap = new Bitmap(image))
            Assert(Math.Abs(PdfPageAnalyzer.Analyze(bitmap, new PdfCleanupOptions()).SkewDegrees) <= .5, "raster scan residual angle");
        string sharedSource = Path.Combine(root, "shared.pdf");
        using (var reader = new PdfReader(input))
        using (var stream = File.Create(sharedSource))
        using (var stamper = new PdfStamper(reader, stream))
        {
            var a = reader.GetPageN(5); var b = reader.GetPageN(1);
            b.Put(PdfName.CONTENTS, a.Get(PdfName.CONTENTS)); b.Put(PdfName.RESOURCES, a.Get(PdfName.RESOURCES));
        }
        var shared = service.Process(sharedSource, output, new PdfCleanupOptions { AnalyzeOnly = false }).Single();
        Assert(shared.Written && shared.Error == null, "shared content streams write: " + shared.Error);
        using (var reader = new PdfReader(shared.OutputPath))
        { Assert(PdfTextExtractor.GetTextFromPage(reader, 1).Contains("ALIGNMENT LINE 23") && PdfTextExtractor.GetTextFromPage(reader, 4).Contains("ALIGNMENT LINE 23"), "shared streams keep both pages intact"); }
        string encrypted = Path.Combine(root, "encrypted.pdf");
        using (var stream = File.Create(encrypted)) using (var doc = new Document())
        { var writer = PdfWriter.GetInstance(doc, stream); writer.SetEncryption(System.Text.Encoding.ASCII.GetBytes("user"), System.Text.Encoding.ASCII.GetBytes("owner"), 0, PdfWriter.ENCRYPTION_AES_128); doc.Open(); Text(writer.DirectContent, 50, 700, "PROTECTED PDF"); }
        Assert(service.Process(encrypted, output, new PdfCleanupOptions { AnalyzeOnly = false }).Single().Error != null, "password PDF reported without overwrite");
        string signature = Path.Combine(root, "signature-marker.pdf");
        using (var stream = File.Create(signature)) using (var doc = new Document())
        {
            var writer = PdfWriter.GetInstance(doc, stream); doc.Open(); Text(writer.DirectContent, 50, 700, "DOCUMENT WITH SIGNATURE MARKER");
            var field = PdfFormField.CreateSignature(writer); field.FieldName = "Signature1";
            field.SetWidget(new Rectangle(30, 30, 150, 60), PdfAnnotation.HIGHLIGHT_INVERT);
            var value = new PdfDictionary(PdfName.SIG); value.Put(PdfName.CONTENTS, new PdfString(new byte[128]).SetHexWriting(true)); value.Put(PdfName.BYTERANGE, new PdfArray(new int[] { 0, 1, 2, 3 }));
            field.Put(PdfName.V, value); writer.AddAnnotation(field);
        }
        var signed = service.Process(signature, output, new PdfCleanupOptions { AnalyzeOnly = false }).Single();
        Assert(signed.Error == null && !signed.Written && signed.Warnings.Any(x => x.Contains("chữ ký")), "signature marker prevents modified output");
        string orientedSource = Path.Combine(root, "orientations.pdf");
        using (var stream = File.Create(orientedSource)) using (var doc = new Document(new Rectangle(595, 842)))
        {
            var writer = PdfWriter.GetInstance(doc, stream); doc.Open();
            for (int p = 0; p < 4; p++) { if (p > 0) doc.NewPage(); for (int row = 0; row < 24; row++) Text(writer.DirectContent, 50, 760 - row * 25, "This is a document with readable text and numbers 12345", 15); }
        }
        string rotated = Path.Combine(root, "rotated.pdf");
        using (var reader = new PdfReader(orientedSource)) using (var stream = File.Create(rotated)) using (var stamper = new PdfStamper(reader, stream))
        { for (int p = 1; p <= 4; p++) reader.GetPageN(p).Put(PdfName.ROTATE, new PdfNumber((p - 1) * 90)); }
        var orientations = service.Process(rotated, Path.Combine(root, "orientation-out"), new PdfCleanupOptions { UseOcr = true, AnalyzeOnly = false }).Single();
        Assert(orientations.Error == null && orientations.Written, "OCR orientation execution: " + orientations.Error);
        using (var reader = new PdfReader(orientations.OutputPath))
        {
            for (int p = 0; p < 4; p++)
            {
                Console.WriteLine("Orientation page " + (p + 1) + " correction " + orientations.Pages[p].ClockwiseRotation + " OCR " + orientations.Pages[p].OcrConfidence);
                Assert(reader.GetPageRotation(p + 1) == 0, "orientation correction page " + (p + 1));
            }
        }
        using (var form = new frmPdfCleanup())
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            form.Show(); Application.DoEvents();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Task task = form.RunCleanupAsync(input, Path.Combine(root, "ui-out"), new PdfCleanupOptions());
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); if (watch.Elapsed.TotalSeconds > 120) throw new TimeoutException("UI run timeout"); }
            task.GetAwaiter().GetResult(); Application.DoEvents();
            Assert(form.Results.Count == 1 && !DocumentJobCoordinator.Shared.IsBusy, "WinForms async/coordinator release");
            Assert(Directory.GetFiles(Path.Combine(root, "ui-out"), "*.csv").Length == 2, "UI auto reports");
            using (var screenshot = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(screenshot, new System.Drawing.Rectangle(0, 0, form.Width, form.Height)); screenshot.Save(Path.Combine(root, "form.png")); }
            form.Close();
        }
        using (var main = new frmMain())
        {
            main.ShowInTaskbar = false; main.StartPosition = FormStartPosition.Manual; main.Location = new Point(-20000, -20000);
            main.Show(); Application.DoEvents(); main.Navigate("Làm sạch PDF");
            Form cleanup = main.Navigation.Current;
            Assert(cleanup is frmPdfCleanup, "main navigation opens cleanup");
            main.Navigate("TIFF / PDF"); main.Navigate("Làm sạch PDF");
            Assert(object.ReferenceEquals(cleanup, main.Navigation.Current), "main navigation preserves cleanup form"); main.Close();
        }
        Console.WriteLine("PASS: " + assertions + " assertions. Artifacts: " + root);
    }
}
