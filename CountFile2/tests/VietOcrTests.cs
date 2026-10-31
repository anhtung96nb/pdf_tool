using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CountFile2;
using CountFile2.Services;

class VietOcrTests
{
    static string root, python, fixture, pdf;
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
    static VietOcrOptions Options(string mode = "cpu") { return new VietOcrOptions { PythonPath = python, WorkerPath = fixture, Dpi = 100, Device = mode, WriteText = true, WriteMarkdown = false }; }
    static VietOcrOptions MarkdownOptions(string mode = "cpu") { return new VietOcrOptions { PythonPath = python, WorkerPath = fixture, Dpi = 100, Device = mode }; }
    static VietOcrOptions NumberOptions(string mode = "cpu") { var options = MarkdownOptions(mode); options.ReadPageNumbers = true; return options; }
    static IList<VietOcrResult> Run(string input, string output, VietOcrOptions options)
    { return new VietOcrService().ProcessAsync(input, output, options, CancellationToken.None).GetAwaiter().GetResult(); }
    static Dictionary<string, object> Json(string path)
    { return new JavaScriptSerializer { MaxJsonLength = 32000000 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); }
    static void MakePdf(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var baseFont = iTextSharp.text.pdf.BaseFont.CreateFont(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            iTextSharp.text.pdf.BaseFont.IDENTITY_H, iTextSharp.text.pdf.BaseFont.EMBEDDED);
        var font = new iTextSharp.text.Font(baseFont, 18);
        using (var stream = File.Create(path)) using (var document = new iTextSharp.text.Document())
        {
            iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream); document.Open();
            document.Add(new iTextSharp.text.Paragraph("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", font));
            document.Add(new iTextSharp.text.Paragraph("Độc lập - Tự do - Hạnh phúc", font));
            document.NewPage(); document.Add(new iTextSharp.text.Paragraph("THÔNG BÁO", font));
            document.Add(new iTextSharp.text.Paragraph("Nội dung văn bản trang thứ hai. Số 12345.", font));
        }
    }
    static void Pump(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); Assert(timer.Elapsed.TotalSeconds < 360, "UI timeout"); }
        task.GetAwaiter().GetResult(); Application.DoEvents();
    }
    [STAThread] static void Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); python = args[1]; fixture = args[2];
        Directory.CreateDirectory(root); pdf = Path.Combine(root, "đầu vào", "văn bản.pdf"); MakePdf(pdf);
        if (args.Length > 3 && args[3] == "real")
        {
            var watch = Stopwatch.StartNew(); var options = NumberOptions(); options.WorkerPath = args[2]; options.Dpi = 300;
            var result = Run(pdf, Path.Combine(root, "real-output"), options).Single();
            Assert(result.CompletedPages == 2 && result.FailedPages == 0, "Real OCR pages: " + result.Details);
            string text = File.ReadAllText(result.MarkdownPath);
            Assert(text.Contains("CỘNG HÒA") && text.Contains("THÔNG BÁO"), "Real Vietnamese recognition: " + text);
            Assert(text.Contains("## Trang 1 / 2") && text.Contains("## Trang 2 / 2") && result.TextPath == null, "Real Markdown output");
            var pages = (ArrayList)Json(result.JsonPath)["pages"];
            Assert(pages.Count == 2, "Real JSON pages");
            Assert(result.PageNumbers.Count == 2 && result.PageNumbers.All(p => p.State == "not_found"), "Blank top-right has no hallucinated labels");
            Assert(text.Contains("Số trang ghi tay:"), "Real Markdown number metadata");
            Console.WriteLine("REAL OCR MARKDOWN PASS " + watch.Elapsed + "\n" + result.MarkdownPath + "\n" + text); result.Dispose(); return;
        }
        File.WriteAllText(fixture + ".requests", "");
        using (var numbered = Run(pdf, Path.Combine(root, "numbers"), NumberOptions()).Single())
        {
            Assert(numbered.PageNumbers.Count == 2 && numbered.PageNumbers[0].Value == "003" && numbered.PageNumbers[1].Value == "12", "Separate labels keep leading zeros");
            Assert(File.ReadAllLines(fixture + ".requests").Length == 2, "One integrated worker request per PDF page");
            string content = File.ReadAllText(numbered.MarkdownPath);
            Assert(content.Contains("**Số trang ghi tay:** 003") && content.Contains("## Trang 2 / 2"), "Markdown labels do not replace PDF indices");
            string originalMarkdown = content, originalJson = File.ReadAllText(numbered.JsonPath);
            using (var locked = new FileStream(numbered.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool failed = false; try { VietOcrPageNumberReview.ConfirmAsync(numbered, 1, "0009").GetAwaiter().GetResult(); } catch (IOException) { failed = true; }
                Assert(failed, "Locked JSON rejects review commit");
            }
            Assert(File.ReadAllText(numbered.MarkdownPath) == originalMarkdown && File.ReadAllText(numbered.JsonPath) == originalJson && !numbered.PageNumbers[0].Confirmed, "Review rollback keeps output and memory in sync");
            VietOcrPageNumberReview.ConfirmAsync(numbered, 1, "0009").GetAwaiter().GetResult();
            Assert(numbered.PageNumbers[0].Value == "0009" && numbered.PageNumbers[0].RawText == "003" && numbered.PageNumbers[0].Confirmed, "Manual value preserves machine output");
            Assert(File.ReadAllText(numbered.MarkdownPath).Contains("0009 (đã xác nhận)") && File.ReadAllText(numbered.JsonPath).Contains("\"value\":\"0009\""), "MD/JSON confirmations synchronized");
            bool invalid = false; try { VietOcrPageNumberReview.ConfirmAsync(numbered, 1, "O09"); } catch (ArgumentException) { invalid = true; }
            Assert(invalid, "Manual values must be digits");
            VietOcrPageNumberReview.ConfirmAsync(numbered, 2, null, true).GetAwaiter().GetResult();
            Assert(numbered.PageNumbers[1].Confirmed && numbered.PageNumbers[1].State == "not_found" && !numbered.PageNumbers[1].NeedsReview, "Explicitly confirm absent number");
            var reread = VietOcrPageNumberReview.RereadAsync(numbered, 2, NumberOptions(), CancellationToken.None).GetAwaiter().GetResult();
            VietOcrPageNumberReview.ReplaceAsync(numbered, 2, reread, CancellationToken.None).GetAwaiter().GetResult();
            Assert(numbered.PageNumbers[1].Value == "12" && !numbered.PageNumbers[1].Confirmed, "Reread only the selected page label");
            Assert(Convert.ToString(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllLines(fixture + ".requests").Last())["operation"]) == "page_number", "Reread skips full-text OCR");
            using (var crop = VietOcrPageNumberReview.RenderRegion(pdf, 1, NumberOptions(), numbered.PageNumbers[0])) Assert(crop.Width > 10 && crop.Height > 10, "Evidence crop");
        }
        using (var ambiguous = Run(pdf, Path.Combine(root, "numbers-ambiguous"), NumberOptions("test-number-ambiguous")).Single())
        {
            Assert(ambiguous.CompletedPages == 2 && ambiguous.PageNumberReviewPages == 2 && ambiguous.PageNumbers.All(p => p.Value == ""), "Ambiguity does not erase full text or invent digits");
            Assert(File.ReadAllText(ambiguous.MarkdownPath).Contains("Số trang ghi tay:** Cần kiểm tra"), "Ambiguous candidate not printed as accepted label");
        }
        using (var failedNumber = Run(pdf, Path.Combine(root, "numbers-error"), NumberOptions("test-number-error")).Single())
            Assert(failedNumber.FailedPages == 0 && failedNumber.CompletedPages == 2 && failedNumber.PageNumberReviewPages == 2, "Number failure is independent of full-text OCR");
        var mdNumberOptions = NumberOptions(); mdNumberOptions.WriteJson = false;
        using (var mdNumber = Run(pdf, Path.Combine(root, "numbers-md-only"), mdNumberOptions).Single())
        {
            VietOcrPageNumberReview.ConfirmAsync(mdNumber, 1, "007").GetAwaiter().GetResult();
            Assert(mdNumber.JsonPath == null && File.ReadAllText(mdNumber.MarkdownPath).Contains("007 (đã xác nhận)"), "Markdown-only supports confirmation without extra output JSON");
        }
        var jsonNumberOptions = NumberOptions(); jsonNumberOptions.WriteMarkdown = false;
        using (var jsonNumber = Run(pdf, Path.Combine(root, "numbers-json-only"), jsonNumberOptions).Single())
        {
            VietOcrPageNumberReview.ConfirmAsync(jsonNumber, 2, "002").GetAwaiter().GetResult();
            Assert(jsonNumber.MarkdownPath == null && File.ReadAllText(jsonNumber.JsonPath).Contains("\"value\":\"002\""), "JSON-only confirmation");
            var cache = (string)jsonNumber.GetType().GetProperty("ReviewDataPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(jsonNumber, null);
            Assert(File.Exists(cache), "Private review cache exists during session"); jsonNumber.Dispose(); Assert(!File.Exists(cache), "Private review cache cleaned on disposal");
        }
        var defaults = new VietOcrOptions();
        Assert(defaults.WriteMarkdown && defaults.WriteJson && !defaults.WriteText, "Default Markdown + JSON");
        var mdOnly = MarkdownOptions(); mdOnly.WriteJson = false;
        var mdResult = Run(pdf, Path.Combine(root, "markdown"), mdOnly).Single();
        Assert(mdResult.Status == "Hoàn tất" && File.Exists(mdResult.MarkdownPath) && mdResult.TextPath == null && mdResult.JsonPath == null, "Markdown only");
        string markdown = File.ReadAllText(mdResult.MarkdownPath);
        Assert(markdown.Contains("# OCR: văn bản\\.pdf") && markdown.Contains("## Trang 1 / 2") && markdown.Contains("## Trang 2 / 2"), "Markdown document and page headings");
        Assert(markdown.Contains("Nội dung tiếng Việt trang 1  \r\n") && markdown.Contains("Nội dung tiếng Việt trang 2"), "UTF8 Markdown and hard line breaks");
        Assert(Directory.GetFiles(Path.GetDirectoryName(mdResult.MarkdownPath)).Length == 1, "No extra formats for Markdown only");
        var mdPair = Run(pdf, Path.Combine(root, "markdown-json"), MarkdownOptions()).Single();
        Assert(File.Exists(mdPair.MarkdownPath) && File.Exists(mdPair.JsonPath) && mdPair.TextPath == null, "Markdown + JSON");
        Assert(((ArrayList)Json(mdPair.JsonPath)["pages"]).Count == 2, "Markdown export keeps full JSON");
        string oldJson = File.ReadAllText(mdPair.JsonPath);
        Assert(Run(pdf, Path.Combine(root, "markdown-json"), MarkdownOptions()).Single().Status == "Lỗi", "Markdown no overwrite");
        File.WriteAllText(mdPair.MarkdownPath, "OLD MARKDOWN");
        var mdOverwrite = MarkdownOptions(); mdOverwrite.Overwrite = true;
        using (var lockedJson = new FileStream(mdPair.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert(Run(pdf, Path.Combine(root, "markdown-json"), mdOverwrite).Single().Status == "Lỗi", "Markdown pair transaction fails on locked JSON");
        Assert(File.ReadAllText(mdPair.MarkdownPath) == "OLD MARKDOWN" && File.ReadAllText(mdPair.JsonPath) == oldJson, "Rollback Markdown + JSON");
        Assert(Run(pdf, Path.Combine(root, "markdown-json"), mdOverwrite).Single().Status == "Hoàn tất", "Overwrite Markdown pair");
        var escaped = Run(pdf, Path.Combine(root, "markdown-escape"), MarkdownOptions("test-markdown")).Single();
        string literal = File.ReadAllText(escaped.MarkdownPath);
        Assert(literal.Contains("\\# Giữ \\*nguyên\\* \\[nhãn\\]\\(https://example\\.invalid\\)"), "OCR markup remains literal");
        Assert(literal.Contains("&lt;b&gt;&amp;amp;&lt;/b&gt; \\| 1\\.") && literal.Contains("&#32;&#32;&#32;&#32;Không biến thành code"), "HTML entities, table separators and indentation escaped");
        var mdError = Run(pdf, Path.Combine(root, "markdown-error"), MarkdownOptions("test-page-error")).Single();
        Assert(mdError.FailedPages == 1 && File.ReadAllText(mdError.MarkdownPath).Contains("> **Lỗi OCR:**"), "Markdown keeps page errors");
        string output = Path.Combine(root, "kết quả");
        var first = Run(pdf, output, Options()).Single();
        Assert(first.CompletedPages == 2 && first.Status == "Hoàn tất", "All pages");
        Assert(File.ReadAllText(first.TextPath).Contains("tiếng Việt trang 2"), "UTF8 and page 2");
        Assert(((ArrayList)Json(first.JsonPath)["pages"]).Count == 2, "JSON pages");
        string original = File.ReadAllText(first.JsonPath);
        Assert(Run(pdf, output, Options()).Single().Status == "Lỗi", "No overwrite by default");
        Assert(File.ReadAllText(first.JsonPath) == original, "Existing outputs intact");
        var overwrite = Options(); overwrite.Overwrite = true;
        Assert(Run(pdf, output, overwrite).Single().Status == "Hoàn tất", "Overwrite enabled");
        // Failure publishing the JSON must restore the TXT, even when overwrite is enabled.
        File.WriteAllText(first.TextPath, "OLD TXT");
        using (var locked = new FileStream(first.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert(Run(pdf, output, overwrite).Single().Status == "Lỗi", "Locked JSON fails transaction");
        Assert(File.ReadAllText(first.TextPath) == "OLD TXT", "Rollback TXT");
        Assert(File.ReadAllText(first.JsonPath) == original, "Rollback JSON");
        var txtOnly = Options(); txtOnly.WriteJson = false;
        var txtResult = Run(pdf, Path.Combine(root, "txt"), txtOnly).Single();
        Assert(File.Exists(txtResult.TextPath) && txtResult.JsonPath == null && !File.Exists(txtResult.TextPath.Replace(".txt", ".json")), "TXT only");
        var jsonOnly = Options(); jsonOnly.WriteText = false;
        var jsonResult = Run(pdf, Path.Combine(root, "json"), jsonOnly).Single();
        Assert(File.Exists(jsonResult.JsonPath) && jsonResult.TextPath == null, "JSON only");
        string batch = Path.Combine(root, "batch"); Directory.CreateDirectory(batch);
        File.WriteAllText(Path.Combine(batch, "0-broken.pdf"), "broken");
        File.Copy(pdf, Path.Combine(batch, "same.PDF")); Directory.CreateDirectory(Path.Combine(batch, "nested"));
        File.Copy(pdf, Path.Combine(batch, "nested", "same.pdf"));
        var grouped = Run(batch, Path.Combine(root, "batch-output"), Options());
        Assert(grouped.Count == 3 && grouped.Count(r => r.Status == "Lỗi") == 1, "Continue after invalid PDF");
        Assert(grouped.Where(r => r.Status == "Hoàn tất").Select(r => r.TextPath).Distinct().Count() == 2, "Preserve nested duplicate names");
        var shallow = Options(); shallow.Recursive = false;
        Assert(Run(batch, Path.Combine(root, "shallow"), shallow).Count == 2, "Recursion off");
        var partial = Run(pdf, Path.Combine(root, "page-error"), Options("test-page-error")).Single();
        Assert(partial.CompletedPages == 1 && partial.FailedPages == 1 && partial.Status == "Hoàn tất có lỗi trang", "Page error marked");
        Assert(File.ReadAllText(partial.TextPath).Contains("Test page failed"), "Page error visible in TXT");
        bool fatal = false; try { Run(pdf, Path.Combine(root, "fatal"), Options("test-fatal")); } catch (InvalidOperationException ex) { fatal = ex.Message.Contains("Missing test weights"); }
        Assert(fatal, "Missing model surfaced");
        var timeout = Options("test-timeout"); timeout.TimeoutSeconds = 1;
        var timeoutResult = Run(pdf, Path.Combine(root, "timeout"), timeout).Single();
        Assert(timeoutResult.FailedPages == 2 && timeoutResult.CompletedPages == 0, "Worker timeout is not success");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.CancelAfter(1500); bool canceled = false;
            try { new VietOcrService().ProcessAsync(pdf, Path.Combine(root, "cancel"), Options("test-slow"), cancel.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled, "Cancellation");
        }
        int pid = int.Parse(File.ReadAllText(fixture + ".pid"));
        bool exited = false; try { exited = Process.GetProcessById(pid).HasExited; } catch (ArgumentException) { exited = true; }
        Assert(exited, "Worker stopped on cancellation");
        Assert(!Directory.Exists(Path.Combine(root, "cancel")) || Directory.GetFiles(Path.Combine(root, "cancel"), "*.txt").Length == 0, "No final output on cancel");
        Assert(Directory.GetDirectories(root, ".vietocr-*", SearchOption.AllDirectories).Length == 0, "No temporary directories");
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using (var main = new frmMain())
        {
            main.Show(); main.Location = new Point(-2500, -2500); Application.DoEvents(); main.Navigate("Việt OCR");
            var form = main.Navigation.Current as frmVietOCR;
            Assert(form != null && form.Text == "Việt OCR", "Navigation");
            Assert(Field<CheckBox>(form, "readNumbers").Checked && Field<NumericUpDown>(form, "numberRight").Value == 20 && Field<NumericUpDown>(form, "numberTop").Value == 10, "Integrated number OCR defaults 20% x 10%");
            Assert(Field<ComboBox>(form, "format").Items.Count == 3 && Field<ComboBox>(form, "format").Text == "Markdown + JSON", "New export choices");
            Field<TextBox>(form, "source").Text = pdf;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var task = form.RunOcrAsync(pdf, Path.Combine(root, "ui-output"), MarkdownOptions());
            Assert(DocumentJobCoordinator.Shared.IsBusy && Field<Button>(form, "cancel").Enabled && !Field<Button>(form, "start").Enabled, "UI running state");
            main.Navigate("Thống kê"); main.Navigate("Việt OCR");
            Assert(ReferenceEquals(main.Navigation.Current, form) && Field<TextBox>(form, "source").Text == pdf, "Retained screen");
            Pump(task); Assert(!DocumentJobCoordinator.Shared.IsBusy && form.Results.Count == 1, "UI finish");
            Assert(Field<TextBox>(form, "preview").Text.Contains("## Trang 1 / 2") && Field<ComboBox>(form, "previewFormat").Text == "Xem Markdown", "UI Markdown preview");
            Field<ComboBox>(form, "previewFormat").SelectedIndex = 1;
            Assert(Field<TextBox>(form, "preview").Text.Contains("\"schema_version\":1"), "UI JSON preview");
            Field<ComboBox>(form, "previewFormat").SelectedIndex = 0;
            foreach (int width in new[] { 1050, 1280 })
            {
                main.Size = new Size(width, 800); main.PerformLayout(); Application.DoEvents();
                using (var image = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "vietocr-" + width + ".png")); }
                Assert(Field<Button>(form, "start").Right <= Field<Button>(form, "start").Parent.ClientSize.Width, "Start button layout");
            }
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var jsonUi = MarkdownOptions(); jsonUi.WriteMarkdown = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Pump(form.RunOcrAsync(pdf, Path.Combine(root, "ui-json-only"), jsonUi));
            Assert(Field<ComboBox>(form, "previewFormat").SelectedIndex == 1 && Field<TextBox>(form, "preview").Text.Contains("\"schema_version\":1"), "JSON-only auto preview");
            Assert(form.Results.Single().MarkdownPath == null && form.Results.Single().TextPath == null, "JSON-only from new options");
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var cancelTask = form.RunOcrAsync(pdf, Path.Combine(root, "ui-cancel"), MarkdownOptions("test-slow"));
            DocumentJobCoordinator.Shared.Cancel(); Pump(cancelTask);
            Assert(!DocumentJobCoordinator.Shared.IsBusy && Field<Button>(form, "start").Enabled, "UI cancel unlock");
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Pump(form.RunOcrAsync(pdf, Path.Combine(root, "ui-numbers"), NumberOptions()));
            var numberPanel = Field<Control>(form, "numberPanel");
            ((TabControl)numberPanel.Parent.Parent).SelectedTab = (TabPage)numberPanel.Parent;
            Application.DoEvents();
            Assert(Field<DataGridView>(numberPanel, "pages").Rows.Count == 2, "Per-page review grid");
            Field<TextBox>(numberPanel, "label").Text = "0008";
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Pump((Task)numberPanel.GetType().GetMethod("ConfirmAsync", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(numberPanel, null));
            Assert(form.Results.Single().PageNumbers[0].Value == "0008" && form.Results.Single().PageNumbers[0].Confirmed, "Confirm label through UI flow");
            using (var image = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "vietocr-number-review.png")); }
        }
        Console.WriteLine("VIET OCR INTEGRATION PASS\n" + root);
    }
}
