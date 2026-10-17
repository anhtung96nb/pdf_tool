using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CountFile2;
using CountFile2.Services;

class ConversionIntegrationTests
{
    class Reporter : IProgress<ConversionProgress> {
        public Action<ConversionProgress> Callback;
        public void Report(ConversionProgress p) { Callback(p); }
    }
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Pdf(string path, int pages) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var stream = File.Create(path)) using (var doc = new iTextSharp.text.Document(new iTextSharp.text.Rectangle(144, 72))) {
            iTextSharp.text.pdf.PdfWriter.GetInstance(doc, stream); doc.Open();
            for (int i = 0; i < pages; i++) { doc.NewPage(); doc.Add(new iTextSharp.text.Paragraph("Page " + (i + 1))); }
        }
    }
    static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
    static void RunForm(frmChuyenDoiFile form, string input, string output, int direction, bool cancel = false) {
        Field<ComboBox>(form, "direction").SelectedIndex = direction;
        Field<TextBox>(form, "source").Text = input; Field<TextBox>(form, "output").Text = output;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        form.GetType().GetMethod("StartConversion", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { null, EventArgs.Empty });
        if (cancel) form.GetType().GetMethod("RequestCancel", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (Field<CancellationTokenSource>(form, "cancellation") != null) {
            Application.DoEvents(); Thread.Sleep(5); Assert(watch.Elapsed.TotalSeconds < 30, "UI timeout");
        }
        Application.DoEvents();
    }
    [STAThread] static void Main(string[] args) {
        string root = Path.Combine(args[0], "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string input = Path.Combine(root, "input"), output = Path.Combine(root, "tiff");
        Pdf(Path.Combine(input, "a.pdf"), 1); Pdf(Path.Combine(input, "nested", "b.PDF"), 3);
        var service = new PdfTiffConversionService(); var events = new List<ConversionProgress>();
        var results = service.ConvertPdfToTiff(input, output, 72, false, default(CancellationToken), new Reporter { Callback = events.Add });
        Assert(results.Count == 2 && results.All(r => r.Success), "PDF conversion");
        Assert(events.Count(p => p.Status == "Chờ") == 2 && events.Last().CompletedFiles == 2, "file progress snapshot");
        Assert(events.Any(p => p.Page == 3 && p.TotalPages == 3), "page progress");
        foreach (var r in results) using (var image = Image.FromFile(r.OutputPath)) {
            Assert(image.GetFrameCount(FrameDimension.Page) == r.PageCount, "TIFF frames");
            Assert(image.Width == 144 && image.Height == 72, "render DPI");
        }
        var reverse = service.ConvertTiffToPdf(output, Path.Combine(root, "pdf"));
        Assert(reverse.All(r => r.Success), "reverse conversion");
        foreach (var r in reverse) using (var pdf = PdfiumViewer.PdfDocument.Load(r.OutputPath)) {
            Assert(pdf.PageCount == r.PageCount, "PDF pages"); Assert(Math.Abs(pdf.PageSizes[0].Width - 144) < 1, "physical size");
        }
        Assert(service.ConvertPdfToTiff(input, output, 72).All(r => !r.Success), "overwrite protection");
        Assert(service.ConvertPdfToTiff(input, output, 72, true).All(r => r.Success), "overwrite");
        File.WriteAllText(Path.Combine(input, "bad.pdf"), "invalid");
        Assert(service.ConvertPdfToTiff(input, Path.Combine(root, "mixed"), 72).Count(r => r.Success) == 2, "corrupt isolation");
        string collisions = Path.Combine(root, "collisions"); Directory.CreateDirectory(collisions);
        File.Copy(results[0].OutputPath, Path.Combine(collisions, "same.tif"));
        File.Copy(results[0].OutputPath, Path.Combine(collisions, "same.tiff"));
        Assert(service.ConvertTiffToPdf(collisions, Path.Combine(root, "collisions-out"), true).Count(r => r.Success) == 1, "collision detection");
        string cancelInput = Path.Combine(root, "cancel-input"), cancelOutput = Path.Combine(root, "cancel-output");
        Pdf(Path.Combine(cancelInput, "a.pdf"), 1); Pdf(Path.Combine(cancelInput, "b.pdf"), 3);
        Directory.CreateDirectory(cancelOutput);
        string old = Path.Combine(cancelOutput, "b.tiff"); File.WriteAllText(old, "original destination");
        using (var token = new CancellationTokenSource()) {
            bool canceled = false;
            try { service.ConvertPdfToTiff(cancelInput, cancelOutput, 72, true, token.Token,
                new Reporter { Callback = p => { if (Path.GetFileName(p.InputPath) == "b.pdf" && p.Page == 1) token.Cancel(); } }); }
            catch (ConversionCanceledException ex) { canceled = true; Assert(ex.Results.Count == 1 && ex.Results[0].Success, "retained results"); }
            Assert(canceled && File.ReadAllText(old) == "original destination", "cancel preserves destination");
            Assert(!Directory.GetFiles(cancelOutput, ".conversion-*.tmp", SearchOption.AllDirectories).Any(), "temporary cleanup");
        }
        using (var token = new CancellationTokenSource()) {
            bool canceled = false;
            string reverseCancel = Path.Combine(root, "reverse-cancel");
            try { service.ConvertTiffToPdf(results[1].OutputPath, reverseCancel, false, token.Token,
                new Reporter { Callback = p => { if (p.Page == 1) token.Cancel(); } }); }
            catch (ConversionCanceledException ex) { canceled = true; Assert(ex.Results.Count == 0, "reverse canceled snapshot"); }
            Assert(canceled && !Directory.GetFiles(reverseCancel).Any(), "reverse cancel cleanup");
        }
        Assert(service.ConvertPdfToTiff(Path.Combine(cancelInput, "a.pdf"), Path.Combine(root, "default-dpi"))[0].Success, "default DPI conversion");
        using (var image = Image.FromFile(Path.Combine(root, "default-dpi", "a.tiff"))) Assert(image.Width == 600 && image.Height == 300 && image.HorizontalResolution == 300, "default 300 DPI");
        bool invalid = false; try { service.ConvertPdfToTiff(input, output, 0); } catch (ArgumentOutOfRangeException) { invalid = true; } Assert(invalid, "invalid DPI");
        Application.EnableVisualStyles();
        using (var form = new frmChuyenDoiFile()) {
            var handle = form.Handle;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Assert(!Field<NumericUpDown>(form, "dpi").Enabled, "TIFF mode DPI disabled");
            RunForm(form, input, Path.Combine(root, "ui-tiff"), 1);
            Assert(Field<DataGridView>(form, "grid").Rows.Count == 3, "UI rows");
            Assert(Field<Label>(form, "status").Text.Contains("2 thành công, 1 lỗi"), "UI summary");
            Assert(Field<Button>(form, "start").Enabled && !Field<Button>(form, "cancel").Enabled, "UI restored");
            var content = form.Controls[0];
            form.Controls.Remove(content);
            content.PerformLayout();
            using (var bitmap = new Bitmap(content.Width, content.Height)) { content.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(root, "form-results.png")); }
            form.Controls.Add(content);
            RunForm(form, output, Path.Combine(root, "ui-pdf"), 0);
            Assert(Field<Label>(form, "status").Text.Contains("2 thành công, 0 lỗi"), "UI reverse summary");
            string empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
            RunForm(form, empty, Path.Combine(root, "empty-out"), 0);
            Assert(Field<Label>(form, "status").Text.Contains("Không có file"), "UI empty folder");
            RunForm(form, cancelInput, Path.Combine(root, "ui-cancel"), 1, true);
            Assert(Field<Label>(form, "status").Text.StartsWith("Đã hủy"), "UI cancellation summary");
            Assert(Field<Button>(form, "start").Enabled && !Field<Button>(form, "cancel").Enabled, "UI cancel restored");
        }
        Console.WriteLine("PASS: multipage roundtrip, nested paths, DPI/size, progress, overwrite, corrupt input, collisions, mid-page cancellation, retained results, cleanup, UI async conversions/summary/empty/state.");
        Console.WriteLine("Artifacts: " + root);
    }
}
