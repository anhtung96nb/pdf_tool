using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CountFile2;
using CountFile2.Services;

class WinFormsNavigationTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static T Field<T>(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType) {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) return (T)field.GetValue(target);
        }
        throw new Exception("Missing field: " + name);
    }
    static void SetField(object target, string name, object value)
    { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    static void Pump(Func<bool> pending)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (pending()) { Application.DoEvents(); Thread.Sleep(10); Assert(watch.Elapsed.TotalSeconds < 120, "UI timeout"); }
        Application.DoEvents();
    }
    static void Start(Form form)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        typeof(frmChuyenDoiFile).GetMethod("StartConversion", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, EventArgs.Empty });
    }
    static void Capture(Form form, string path)
    {
        form.PerformLayout(); Application.DoEvents();
        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path); }
    }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Control.CheckForIllegalCrossThreadCalls = true;
        string root = Path.Combine(Path.GetFullPath(args[0]), "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string input = Path.Combine(root, "input.pdf");
        using (var stream = File.Create(input)) using (var doc = new iTextSharp.text.Document()) {
            iTextSharp.text.pdf.PdfWriter.GetInstance(doc, stream); doc.Open();
            doc.Add(new iTextSharp.text.Paragraph("TEST DOCUMENT 12345", iTextSharp.text.FontFactory.GetFont("Helvetica", 28)));
            doc.NewPage(); doc.Add(new iTextSharp.text.Paragraph("SECOND PAGE 67890"));
        }
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using (var main = new frmMain()) {
            main.Show(); main.Location = new Point(-2500, -2500); Application.DoEvents();
            Assert(main.Navigation.Current is frmChuyenDoiFile, "startup");
            Form tiff = main.Navigation.Current;
            main.Navigate("PDF hai lớp / PDF/A"); Form pdf = main.Navigation.Current;
            Assert(pdf is frmPdfProcessing, "PDF screen");
            Field<TextBox>(pdf, "source").Text = input;
            Field<TextBox>(pdf, "output").Text = Path.Combine(root, "output");
            Field<NumericUpDown>(pdf, "dpi").Value = 150;
            for (int i = 0; i < 3; i++) {
                main.Navigate("Thống kê"); main.Navigate("TIFF / PDF"); Assert(ReferenceEquals(main.Navigation.Current, tiff), "cached TIFF");
                main.Navigate("PDF hai lớp / PDF/A"); Assert(ReferenceEquals(main.Navigation.Current, pdf), "cached PDF");
            }
            Assert(Field<TextBox>(pdf, "source").Text == input, "preserved input");
            main.Navigate("OCR thông tin văn bản"); Assert(main.Navigation.Current is frmXuLyOCR, "legacy OCR opens");
            Form ocr = main.Navigation.Current;
            Field<PictureBox>(ocr, "pictureBox1").Image = new Bitmap(800, 800);
            using (var graphics = Graphics.FromImage(Field<PictureBox>(ocr, "pictureBox1").Image)) graphics.Clear(Color.White);
            Field<PictureBox>(ocr, "pictureBox1").Tag = input;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            typeof(frmXuLyOCR).GetMethod("btOCR_Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ocr, new object[] { null, EventArgs.Empty });
            Assert(DocumentJobCoordinator.Shared.IsBusy, "legacy OCR async acquired");
            main.Navigate("PDF hai lớp / PDF/A");
            Pump(() => DocumentJobCoordinator.Shared.IsBusy);
            Assert(Field<Label>(ocr, "txtOcrMsg").Text == "OCR thành công", "legacy OCR UI completion");
            main.Navigate("Thống kê"); Form statistics = main.Navigation.Current;
            string statsInput = Path.Combine(root, "statistics-input"); Directory.CreateDirectory(Path.Combine(statsInput, "nested"));
            File.Copy(input, Path.Combine(statsInput, "nested", "file.pdf"));
            Field<TextBox>(statistics, "txtFolder").Text = statsInput;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            typeof(Form1).GetMethod("btThongKe_Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(statistics, new object[] { null, EventArgs.Empty });
            DocumentJobCoordinator.Shared.Cancel(); main.Navigate("PDF hai lớp / PDF/A");
            Pump(() => DocumentJobCoordinator.Shared.IsBusy);
            Assert(Field<Label>(statistics, "lblStatus").Text == "Đã hủy thống kê.", "legacy statistics cancellation");
            SetField(statistics, "statisticsToken", CancellationToken.None);
            SetField(statistics, "statisticsStructure", false);
            using (var statsToken = new CancellationTokenSource()) {
                Assert(DocumentJobCoordinator.Shared.TryBegin("Kiểm thử thống kê", statsToken), "stats coordinator");
                var work = Task.Run(() => typeof(Form1).GetMethod("lstHoSo", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(statistics, new object[] { statsInput }));
                Pump(() => !work.IsCompleted); work.GetAwaiter().GetResult();
                Assert(Field<TextBox>(statistics, "txtThanhCong").Text == "1", "stats background UI count");
                DocumentJobCoordinator.Shared.End(statsToken);
            }
            string excelOutput = Path.Combine(root, "ocr-excel"); Directory.CreateDirectory(excelOutput);
            var export = Task.Run(() => (bool)typeof(frmXuLyOCR).GetMethod("ExportExcel", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(ocr, new object[] { excelOutput }));
            Pump(() => !export.IsCompleted); Assert(export.Result && File.Exists(Path.Combine(excelOutput, "OCR_Data.xlsx")), "legacy OCR worker export");
            main.Navigate("PDF hai lớp / PDF/A");
            for (int mode = 0; mode < 3; mode++) {
                Field<ComboBox>(pdf, "direction").SelectedIndex = mode;
                Assert(Field<TextBox>(pdf, "language").Enabled == (mode != 1), "language availability");
                Assert(Field<TextBox>(pdf, "iccPath").Enabled == (mode != 0), "ICC availability");
                Start(pdf); Assert(DocumentJobCoordinator.Shared.IsBusy, "job acquired");
                main.Navigate("TIFF / PDF"); Assert(!Field<Button>(tiff, "start").Enabled, "other job blocked");
                Assert(!ocr.Enabled, "legacy OCR protected");
                Start(tiff); Assert(DocumentJobCoordinator.Shared.ActiveJob == pdf.Text, "second start blocked");
                main.Navigate("PDF hai lớp / PDF/A");
                Pump(() => DocumentJobCoordinator.Shared.IsBusy);
                Assert(Field<Label>(pdf, "status").Text.Contains("1 thành công, 0 lỗi"), "PDF mode " + mode + ": " + Field<Label>(pdf, "status").Text);
                Assert(Field<DataGridView>(pdf, "grid").Rows.Count == 1, "snapshot row");
                Assert(Field<Button>(pdf, "start").Enabled && ocr.Enabled, "UI restored");
            }
            Capture(main, Path.Combine(root, "shell-pdf.png"));
            // A per-file collision is reported in the grid, then overwrite succeeds.
            Start(pdf); Pump(() => DocumentJobCoordinator.Shared.IsBusy);
            Assert(Field<Label>(pdf, "status").Text.Contains("0 thành công, 1 lỗi"), "UI overwrite protection");
            Field<CheckBox>(pdf, "overwrite").Checked = true;
            Start(pdf); Pump(() => DocumentJobCoordinator.Shared.IsBusy);
            Assert(Field<Label>(pdf, "status").Text.Contains("1 thành công, 0 lỗi"), "UI overwrite");
            Field<Button>(pdf, "advancedButton").PerformClick();
            main.ClientSize = new Size(1050, 680); Capture(main, Path.Combine(root, "shell-small-advanced.png"));
            main.Navigate("Thống kê"); Capture(main, Path.Combine(root, "shell-statistics.png"));
            main.Navigate("OCR thông tin văn bản"); Capture(main, Path.Combine(root, "shell-ocr.png"));
            main.Navigate("PDF hai lớp / PDF/A");
            Field<ComboBox>(pdf, "direction").SelectedIndex = 1;
            string batchInput = Path.Combine(root, "batch-input"); Directory.CreateDirectory(Path.Combine(batchInput, "nested"));
            File.Copy(input, Path.Combine(batchInput, "nested", "good.pdf")); File.WriteAllText(Path.Combine(batchInput, "bad.pdf"), "bad");
            Field<TextBox>(pdf, "source").Text = batchInput;
            Field<TextBox>(pdf, "output").Text = Path.Combine(root, "batch-output");
            Start(pdf); Pump(() => DocumentJobCoordinator.Shared.IsBusy);
            Assert(Field<Label>(pdf, "status").Text.Contains("1 thành công, 1 lỗi"), "UI recursive mixed batch");
            Field<TextBox>(pdf, "source").Text = input; Field<TextBox>(pdf, "output").Clear();
            Start(pdf); Pump(() => DocumentJobCoordinator.Shared.IsBusy);
            Assert(File.Exists(Path.Combine(root, "input.pdfa.pdf")), "UI output beside source");
            float previousScale = 1;
            foreach (float scale in new[] { 1.25f, 1.5f }) {
                main.Scale(new SizeF(scale / previousScale, scale / previousScale));
                main.ClientSize = new Size((int)(1050 * scale), (int)(680 * scale));
                Capture(main, Path.Combine(root, "shell-layout-" + (int)(scale * 100) + ".png"));
                foreach (string name in new[] { "start", "cancel", "open", "advancedButton" }) {
                    var button = Field<Button>(pdf, name);
                    Assert(button.Right <= button.Parent.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height, "scaled actions " + name);
                }
                Assert(Field<DataGridView>(pdf, "grid").Height > 100, "scaled result grid");
                previousScale = scale;
            }
            Field<ComboBox>(pdf, "direction").SelectedIndex = 0;
            Field<TextBox>(pdf, "output").Text = Path.Combine(root, "cancel-output");
            Start(pdf); DocumentJobCoordinator.Shared.Cancel(); Pump(() => DocumentJobCoordinator.Shared.IsBusy);
            Assert(Field<Label>(pdf, "status").Text.StartsWith("Đã hủy"), "cancellation summary");
            Assert(!Directory.GetFiles(root, ".pdf-processing-*", SearchOption.AllDirectories).Any(), "cleanup");
            Field<TextBox>(pdf, "output").Text = Path.Combine(root, "close-output");
            Start(pdf);
            typeof(frmMain).GetMethod("BeginShutdown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(main, null);
            Pump(() => !main.IsDisposed);
            Assert(!DocumentJobCoordinator.Shared.IsBusy && pdf.IsDisposed && tiff.IsDisposed && ocr.IsDisposed, "close waits and disposes");
        }
        Console.WriteLine("PASS: cached navigation, preserved state, three PDF modes, option availability, single-job guard, switching while running, cancellation, safe shutdown/disposal.");
        Console.WriteLine("Artifacts: " + root);
    }
}
