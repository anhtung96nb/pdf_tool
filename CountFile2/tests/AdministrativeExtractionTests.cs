using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CountFile2;
using CountFile2.Services;
using iTextSharp.text.pdf;

class AdministrativeExtractionTests
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static string Value(AdministrativeDocument d, string name) { return d.Fields.First(f => f.Name == name).Value; }
    static void Text(PdfContentByte c, BaseFont font, float x, float y, string text) {
        c.BeginText(); c.SetFontAndSize(font, 13); c.SetTextMatrix(x, y); c.ShowText(text); c.EndText();
    }
    static void CreatePdf(string file) {
        using (var stream = File.Create(file)) using (var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4)) {
            var writer = PdfWriter.GetInstance(doc, stream); doc.Open();
            var font = BaseFont.CreateFont(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "times.ttf"), BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
            var c = writer.DirectContent;
            Text(c, font, 40, 795, "ỦY BAN NHÂN DÂN"); Text(c, font, 40, 775, "HUYỆN MẪU");
            Text(c, font, 315, 795, "CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM");
            Text(c, font, 315, 775, "Độc lập - Tự do - Hạnh phúc");
            Text(c, font, 40, 735, "Số: 123/QĐ-UBND");
            Text(c, font, 315, 735, "Mẫu, ngày 12 tháng 10 năm 2026");
            Text(c, font, 220, 680, "QUYẾT ĐỊNH");
            Text(c, font, 130, 655, "Về việc phê duyệt kế hoạch công tác");
            Text(c, font, 40, 610, "Căn cứ văn bản số 999 ngày 01 tháng 01 năm 2020;");
            Text(c, font, 40, 550, "Điều 1. Phê duyệt kế hoạch công tác của đơn vị.");
            doc.NewPage(); c = writer.DirectContent;
            Text(c, font, 40, 750, "Điều 2. Các đơn vị có trách nhiệm thi hành quyết định này.");
            Text(c, font, 40, 340, "Nơi nhận:"); Text(c, font, 40, 315, "- Phòng Nội vụ;"); Text(c, font, 40, 290, "- Lưu: VT.");
            Text(c, font, 360, 340, "KT. CHỦ TỊCH"); Text(c, font, 360, 315, "PHÓ CHỦ TỊCH");
            Text(c, font, 360, 210, "Nguyễn Văn An"); Text(c, font, 290, 20, "2");
        }
    }
    static void RasterPdf(string source, string target, bool mixed) {
        using (var reader = new PdfReader(source)) using (var pdf = PdfiumViewer.PdfDocument.Load(source))
        using (var stream = File.Create(target)) using (var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4)) {
            var writer = PdfWriter.GetInstance(doc, stream); doc.Open();
            for (int p = 0; p < pdf.PageCount; p++) {
                doc.NewPage();
                if (mixed && p == 0) writer.DirectContent.AddTemplate(writer.GetImportedPage(reader, 1), 0, 0);
                else using (var image = pdf.Render(p, 1654, 2339, 200, 200, PdfiumViewer.PdfRenderFlags.ForPrinting))
                using (var ms = new MemoryStream()) {
                    image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    var img = iTextSharp.text.Image.GetInstance(ms.ToArray()); img.ScaleAbsolute(595, 842); img.SetAbsolutePosition(0, 0); writer.DirectContent.AddImage(img);
                }
            }
        }
    }
    class Reporter : IProgress<ExtractionProgress> { public Action<ExtractionProgress> Callback; public void Report(ExtractionProgress p) { Callback(p); } }
    [STAThread] static void Main(string[] args) {
        string root = Path.Combine(args[0], "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string pdf = Path.Combine(root, "document.pdf"); CreatePdf(pdf);
        var service = new AdministrativeExtractionService(); var progress = new List<ExtractionProgress>();
        var d = service.Process(pdf, CancellationToken.None, new Reporter { Callback = progress.Add });
        Check(d.Error == null, "text PDF error: " + d.Error); Check(d.PageCount == 2 && progress.Count == 2, "all pages/progress");
        Check(Value(d, "Số, ký hiệu") == "123/QĐ-UBND", "number"); Check(Value(d, "Số văn bản") == "123", "split number");
        Check(Value(d, "Ngày ban hành") == "2026-10-12", "issue date vs cited date");
        Check(Value(d, "Cơ quan ban hành") == "ỦY BAN NHÂN DÂN\nHUYỆN MẪU", "multiline agency"); Check(Value(d, "Thể loại") == "QUYẾT ĐỊNH", "type");
        Check(Value(d, "Trích yếu") == "Về việc phê duyệt kế hoạch công tác", "summary excludes body");
        Check(Value(d, "Nơi nhận").Contains("Phòng Nội vụ") && !Value(d, "Nơi nhận").Contains("CHỦ TỊCH"), "recipient column");
        Check(Value(d, "Người ký") == "Nguyễn Văn An", "last page signer"); Check(Value(d, "Chức vụ người ký") == "PHÓ CHỦ TỊCH", "title");
        Check(Value(d, "Quyền hạn ký") == "KT. CHỦ TỊCH", "authority");
        Check(d.Fields.First(f => f.Name == "Người ký").Evidence[0].Page == 2, "evidence page");
        Check(d.Lines.All(l => l.X >= 0 && l.Y >= 0 && l.Width > 0 && l.Height > 0), "evidence geometry");
        string scan = Path.Combine(root, "scan.PDF"), mixed = Path.Combine(root, "mixed.pdf"); RasterPdf(pdf, scan, false); RasterPdf(pdf, mixed, true);
        var sd = service.Process(scan, CancellationToken.None); Check(sd.Error == null, "scan error " + sd.Error);
        ExtractionExportService.SaveJson(Path.Combine(root, "scan-diagnostic.json"), new[] { sd });
        Check(sd.Lines.All(l => l.Method.Contains("OCR")), "scan routes to OCR");
        Check(Value(sd, "Số, ký hiệu") != null && Value(sd, "Số, ký hiệu").Contains("123"), "scan number OCR");
        Check(Value(sd, "Người ký") != null, "scan signer OCR");
        Check(Value(sd, "Số, ký hiệu") == "123/QĐ-UBND" && !Value(sd, "Nơi nhận").Contains("CHỦ TỊCH"), "OCR separates header/footer columns");
        using (var original = PdfiumViewer.PdfDocument.Load(pdf))
        using (var rendered = original.Render(0, 1654, 2339, 200, 200, PdfiumViewer.PdfRenderFlags.ForPrinting))
        using (var rotated = new Bitmap(rendered))
        using (var engine = new VietnameseOcrEngine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"))) {
            rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
            var lines = engine.Recognize(rotated, 1, CancellationToken.None);
            File.WriteAllLines(Path.Combine(root, "rotation.txt"), lines.Select(l => l.RotationDegrees + " " + l.Confidence + " " + l.Text));
            Check(lines.Any(l => l.Text.Contains("123/QĐ-UBND")) && lines.All(l => l.RotationDegrees == 270), "rotated scan correction");
        }
        var md = service.Process(mixed, CancellationToken.None); Check(md.Error == null, "mixed error");
        Check(md.Lines.Any(l => l.Page == 1 && l.Method == "Lớp chữ PDF") && md.Lines.Any(l => l.Page == 2 && l.Method.Contains("OCR")), "per-page mixed routing");
        var forced = new AdministrativeExtractionService { ForceOcr = true }.Process(pdf, CancellationToken.None);
        Check(forced.Error == null && forced.Lines.All(l => l.Method.Contains("OCR")), "force OCR");
        File.WriteAllText(Path.Combine(root, "bad.pdf"), "bad"); Check(service.Process(Path.Combine(root, "bad.pdf"), CancellationToken.None).Error != null, "corrupt PDF");
        Directory.CreateDirectory(Path.Combine(root, "nested")); File.Copy(pdf, Path.Combine(root, "nested", "child.PDF"));
        Check(AdministrativeExtractionService.Discover(root, false).Count == 4 && AdministrativeExtractionService.Discover(root, true).Count == 5, "recursive + uppercase");
        using (var token = new CancellationTokenSource()) {
            bool canceled = false;
            try { service.Process(pdf, token.Token, new Reporter { Callback = p => token.Cancel() }); } catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "cancel between pages");
        }
        var dateLine = d.Lines.First(l => l.Text.Contains("ngày 12")); dateLine.Text = "ngày 31 tháng 02 năm 2026";
        AdministrativeExtractionService.Extract(d); Check(Value(d, "Ngày ban hành") == null && d.Warnings.Any(w => w.Contains("không hợp lệ")), "invalid date");
        var field = d.Fields.First(f => f.Name == "Người ký"); field.Value = "Đã sửa"; field.Confirmed = true;
        string json = Path.Combine(root, "results.json"), excel = Path.Combine(root, "results.xlsx");
        ExtractionExportService.SaveJson(json, new[] { d, sd }); ExtractionExportService.SaveJson(json, new[] { d, sd });
        var loaded = ExtractionExportService.LoadJson(json); Check(Value(loaded[0], "Người ký") == "Đã sửa" && loaded[0].Fields.First(f => f.Name == "Người ký").OriginalValue == "Nguyễn Văn An", "JSON retains correction/original");
        ExtractionExportService.SaveExcel(excel, loaded);
        using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(excel))) {
            Check(package.Workbook.Worksheets.Count == 2 && package.Workbook.Worksheets["Bằng chứng"].Dimension.End.Row > 10, "Excel evidence");
        }
        using (var main = new frmMain()) {
            main.Navigate("Trích xuất văn bản PDF"); var form = main.Navigation.Current;
            Check(form is frmTrichXuatVanBan, "navigation"); main.Navigate("TIFF / PDF"); main.Navigate("Trích xuất văn bản PDF"); Check(main.Navigation.Current == form, "navigation reuses form");
        }
        using (var form = new frmTrichXuatVanBan()) {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.Show();
            var storage = (List<AdministrativeDocument>)typeof(frmTrichXuatVanBan).GetField("results", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); storage.Add(loaded[0]);
            typeof(frmTrichXuatVanBan).GetMethod("RefreshDocuments", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null); Application.DoEvents();
            using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "form.png")); }
        }
        string batch = Path.Combine(root, "batch"); Directory.CreateDirectory(batch); File.Copy(pdf, Path.Combine(batch, "a.pdf")); File.WriteAllText(Path.Combine(batch, "bad.pdf"), "invalid");
        using (var form = new frmTrichXuatVanBan()) {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.Show();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            string saved = Path.Combine(root, "batch.json"); Pump(form.RunExtractionAsync(batch, true, false, saved));
            var storage = (List<AdministrativeDocument>)typeof(frmTrichXuatVanBan).GetField("results", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            Check(storage.Count == 2 && storage.Count(x => x.Error != null) == 1 && !DocumentJobCoordinator.Shared.IsBusy, "UI batch isolates error/releases job");
            Check(ExtractionExportService.LoadJson(saved).Count == 2, "batch checkpoint");
            var first = storage.First(x => x.Error == null); first.Fields.First(f => f.Name == "Người ký").Value = "Đã sửa thủ công";
            Pump(form.RunExtractionAsync(batch, true, false, saved));
            Check(object.ReferenceEquals(first, storage.First(x => x.Error == null)) && Value(first, "Người ký") == "Đã sửa thủ công", "resume preserves corrected unchanged file");
            using (var writer = new StreamWriter(Path.Combine(batch, "a.pdf"), true)) writer.Write("\n% modified");
            Pump(form.RunExtractionAsync(batch, true, false, saved));
            Check(!object.ReferenceEquals(first, storage.First(x => x.Error == null)), "changed content reprocessed");
            var task = form.RunExtractionAsync(batch, true, true, saved); DocumentJobCoordinator.Shared.Cancel(); Pump(task);
            Check(!DocumentJobCoordinator.Shared.IsBusy && ExtractionExportService.LoadJson(saved).Count == 2, "UI cancel keeps checkpoint");
            Check(storage.All(x => x.Source.EndsWith(".pdf")), "source retained");
        }
        File.WriteAllText(Path.Combine(root, "report.txt"), checks + " assertions passed. Synthetic PDFs only; not a real-world accuracy benchmark.");
        Console.WriteLine("PASS " + checks + " assertions; artifacts: " + root);
    }
    static void Pump(System.Threading.Tasks.Task task) {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); if (watch.Elapsed.TotalSeconds > 120) throw new Exception("UI task timeout"); }
        task.GetAwaiter().GetResult(); Application.DoEvents();
    }
}
