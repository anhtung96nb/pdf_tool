using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CountFile2.Services;

namespace CountFile2
{
    public sealed class frmPdfCleanup : Form
    {
        private readonly TextBox source = new TextBox { Width = 310 };
        private readonly TextBox output = new TextBox { Width = 310 };
        private readonly CheckBox analyze = new CheckBox { Text = "Chỉ phân tích", Checked = true, AutoSize = true };
        private readonly CheckBox recursive = new CheckBox { Text = "Thư mục con", Checked = true, AutoSize = true };
        private readonly CheckBox blanks = new CheckBox { Text = "Xóa trang trắng", Checked = true, AutoSize = true };
        private readonly CheckBox deskew = new CheckBox { Text = "Căn thẳng", Checked = true, AutoSize = true };
        private readonly CheckBox quality = new CheckBox { Text = "Kiểm tra chất lượng", Checked = true, AutoSize = true };
        private readonly CheckBox ocr = new CheckBox { Text = "OCR hướng / chất lượng", AutoSize = true };
        private readonly CheckBox overwrite = new CheckBox { Text = "Ghi đè đầu ra", AutoSize = true };
        private readonly CheckBox lowOnly = new CheckBox { Text = "Chỉ file chất lượng thấp", AutoSize = true };
        private readonly TextBox language = new TextBox { Text = "vie", Width = 60 };
        private readonly TextBox tessdata = new TextBox { Width = 280, Text = new PdfCleanupOptions().TessDataPath };
        private readonly NumericUpDown maxAngle = new NumericUpDown { Minimum = 1, Maximum = 15, Value = 7, Width = 50 };
        private readonly NumericUpDown minDpi = new NumericUpDown { Minimum = 50, Maximum = 600, Value = 150, Width = 60 };
        private readonly Button start = new Button { Text = "Bắt đầu", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Hủy", AutoSize = true, Enabled = false };
        private readonly Button export = new Button { Text = "Xuất CSV", AutoSize = true };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Bottom };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 44, AutoEllipsis = true };
        private readonly DataGridView files = Grid(), pages = Grid();
        private readonly PictureBox before = Preview(), after = Preview();
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 160, AutoScroll = true, Padding = new Padding(5) };
        private readonly List<PdfCleanupResult> results = new List<PdfCleanupResult>();
        private CancellationTokenSource cancellation;
        public IList<PdfCleanupResult> Results { get { return results.AsReadOnly(); } }
        private static DataGridView Grid() { return new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false,
            ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect }; }
        private static PictureBox Preview() { return new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.DimGray }; }
        private static void Column(DataGridView grid, string property, string label, int width)
        { grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = label, Width = width }); }
        public frmPdfCleanup()
        {
            Text = "Làm sạch PDF"; Name = "frmPdfCleanup"; ClientSize = new Size(1100, 750); Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Font;
            AddButton("Chọn PDF", () => { using (var d = new OpenFileDialog { Filter = "PDF|*.pdf" }) if (d.ShowDialog(this) == DialogResult.OK) source.Text = d.FileName; });
            AddButton("Chọn thư mục", () => ChooseFolder(source)); toolbar.Controls.Add(source);
            AddButton("Đầu ra", () => ChooseFolder(output)); toolbar.Controls.Add(output); toolbar.SetFlowBreak(output, true);
            toolbar.Controls.AddRange(new Control[] { analyze, recursive, blanks, deskew, quality, ocr, overwrite }); toolbar.SetFlowBreak(overwrite, true);
            toolbar.Controls.Add(new Label { Text = "Góc tối đa:", AutoSize = true }); toolbar.Controls.Add(maxAngle);
            toolbar.Controls.Add(new Label { Text = "DPI ảnh tối thiểu:", AutoSize = true }); toolbar.Controls.Add(minDpi);
            toolbar.Controls.Add(new Label { Text = "Ngôn ngữ:", AutoSize = true }); toolbar.Controls.Add(language);
            toolbar.Controls.Add(new Label { Text = "tessdata:", AutoSize = true }); toolbar.Controls.Add(tessdata); toolbar.SetFlowBreak(tessdata, true);
            toolbar.Controls.AddRange(new Control[] { start, cancel, export, lowOnly });
            Column(files, "InputPath", "File nguồn", 260); Column(files, "OriginalPages", "Trang gốc", 65);
            Column(files, "KeptPages", "Trang giữ", 65); Column(files, "LowQuality", "Chất lượng thấp", 100);
            Column(files, "Status", "Trạng thái", 140); Column(files, "Details", "Lý do / lỗi", 330);
            Column(pages, "OriginalPage", "Trang gốc", 65); Column(pages, "OutputPage", "Trang đích", 65);
            Column(pages, "Action", "Dự kiến / hành động", 145); Column(pages, "SkewDegrees", "Góc nghiêng", 75);
            Column(pages, "ClockwiseRotation", "Xoay", 50); Column(pages, "Details", "Chất lượng / cần kiểm tra", 370);
            var list = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(560, 550), SplitterDistance = 210 };
            list.Panel1.Controls.Add(files); list.Panel2.Controls.Add(pages);
            var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            previews.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previews.Controls.Add(new Label { Text = "Trang gốc", Dock = DockStyle.Fill }, 0, 0);
            previews.Controls.Add(new Label { Text = "Sau xử lý / dự kiến", Dock = DockStyle.Fill }, 1, 0);
            previews.Controls.Add(before, 0, 1); previews.Controls.Add(after, 1, 1);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = ClientSize, SplitterDistance = 610 };
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(previews);
            Controls.Add(split); Controls.Add(progress); Controls.Add(status); Controls.Add(toolbar);
            status.Text = "Giữ file gốc. Trang trắng có nhiễu/chữ mờ được giữ để kiểm tra. Căn thẳng có thể tăng kích thước trang.";
            start.Click += async (s, e) => await RunCleanupAsync(source.Text, output.Text, new PdfCleanupOptions {
                AnalyzeOnly = analyze.Checked, Recursive = recursive.Checked, RemoveBlankPages = blanks.Checked, Deskew = deskew.Checked,
                CheckQuality = quality.Checked, UseOcr = ocr.Checked, Overwrite = overwrite.Checked,
                MaxSkewDegrees = (double)maxAngle.Value, MinImageDpi = (double)minDpi.Value, Language = language.Text, TessDataPath = tessdata.Text });
            cancel.Click += (s, e) => { if (cancellation != null) cancellation.Cancel(); };
            export.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "pdf-cleanup.csv" }) if (d.ShowDialog(this) == DialogResult.OK)
                try { PdfCleanupService.ExportCsv(d.FileName, results, lowOnly.Checked); status.Text = "Đã xuất " + d.FileName; } catch (Exception ex) { status.Text = ex.Message; } };
            lowOnly.CheckedChanged += (s, e) => RefreshFiles();
            files.SelectionChanged += (s, e) => { var r = SelectedFile(); pages.DataSource = r == null ? null : new BindingList<PdfPageAnalysis>(r.Pages); RenderPreview(); };
            pages.SelectionChanged += (s, e) => RenderPreview();
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            FormClosing += (s, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); status.Text = "Đang hủy; chờ dọn file tạm trước khi đóng."; } };
            JobChanged(this, EventArgs.Empty);
        }
        private void AddButton(string text, Action action) { var b = new Button { Text = text, AutoSize = true }; b.Click += (s, e) => action(); toolbar.Controls.Add(b); }
        private void ChooseFolder(TextBox box) { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath; }
        private void JobChanged(object sender, EventArgs e)
        { foreach (Control c in toolbar.Controls) c.Enabled = !DocumentJobCoordinator.Shared.IsBusy; cancel.Enabled = cancellation != null; }
        public async Task RunCleanupAsync(string inputPath, string outputPath, PdfCleanupOptions options)
        {
            if (DocumentJobCoordinator.Shared.IsBusy) return;
            cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin("Làm sạch PDF", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            results.Clear(); RefreshFiles();
            bool acceptProgress = true;
            var reports = new Progress<PdfCleanupProgress>(p => {
                if (!acceptProgress) return;
                progress.Maximum = Math.Max(1, p.TotalFiles); progress.Value = Math.Min(progress.Maximum, p.CompletedFiles);
                if (p.Result != null) { results.Add(p.Result); RefreshFiles(); }
                status.Text = string.Format("{0}/{1} file - {2} - trang {3}/{4}", p.CompletedFiles, p.TotalFiles, Path.GetFileName(p.InputPath), p.Page, p.TotalPages);
            });
            try
            {
                var done = await new PdfCleanupService().ProcessAsync(inputPath, outputPath, options, cancellation.Token, reports);
                acceptProgress = false;
                results.Clear(); results.AddRange(done); RefreshFiles();
                string report = SaveReports(outputPath);
                status.Text = "Hoàn tất: " + results.Count + " file, " + results.Count(r => r.LowQuality) + " chất lượng thấp, " + results.Count(r => r.Error != null) + " lỗi. " + report;
            }
            catch (PdfCleanupCanceledException ex) { acceptProgress = false; results.Clear(); results.AddRange(ex.Results); RefreshFiles(); status.Text = "Đã hủy. Giữ kết quả " + results.Count + " file hoàn tất. " + SaveReports(outputPath); }
            catch (OperationCanceledException) { status.Text = "Đã hủy."; }
            catch (Exception ex) { status.Text = "Lỗi: " + ex.Message; }
            finally { acceptProgress = false; var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); }
        }
        private string SaveReports(string directory)
        {
            if (results.Count == 0) return "Không có kết quả để xuất.";
            try
            {
                string name = "pdf-cleanup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                PdfCleanupService.ExportCsv(Path.Combine(directory, name + ".csv"), results);
                PdfCleanupService.ExportCsv(Path.Combine(directory, name + "-low-quality.csv"), results, true);
                return "Báo cáo: " + Path.Combine(directory, name + ".csv");
            }
            catch (Exception ex) { return "Không lưu được báo cáo: " + ex.Message; }
        }
        private void RefreshFiles() { files.DataSource = new BindingList<PdfCleanupResult>(results.Where(r => !lowOnly.Checked || r.LowQuality).ToList()); }
        private PdfCleanupResult SelectedFile() { return files.CurrentRow == null ? null : files.CurrentRow.DataBoundItem as PdfCleanupResult; }
        private void RenderPreview()
        {
            SetImage(before, null); SetImage(after, null);
            var r = SelectedFile(); var p = pages.CurrentRow == null ? null : pages.CurrentRow.DataBoundItem as PdfPageAnalysis;
            if (r == null || p == null) return;
            try
            {
                SetImage(before, Render(r.InputPath, p.OriginalPage));
                if (p.Remove) return;
                if (r.Written) SetImage(after, Render(r.OutputPath, p.OutputPage));
                else
                {
                    using (var image = new Bitmap(before.Image))
                    {
                        if (p.ClockwiseRotation != 0) image.RotateFlip(p.ClockwiseRotation == 90 ? RotateFlipType.Rotate90FlipNone : p.ClockwiseRotation == 180 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                        double rad = p.ApplyDeskew ? -p.SkewDegrees * Math.PI / 180 : 0;
                        int w = (int)Math.Ceiling(Math.Abs(image.Width * Math.Cos(rad)) + Math.Abs(image.Height * Math.Sin(rad)));
                        int h = (int)Math.Ceiling(Math.Abs(image.Width * Math.Sin(rad)) + Math.Abs(image.Height * Math.Cos(rad)));
                        var corrected = new Bitmap(w, h);
                        using (var g = Graphics.FromImage(corrected)) { g.Clear(Color.White); g.TranslateTransform(w / 2f, h / 2f); g.RotateTransform((float)(rad * 180 / Math.PI)); g.DrawImage(image, -image.Width / 2f, -image.Height / 2f); }
                        SetImage(after, corrected);
                    }
                }
            }
            catch (Exception ex) { status.Text = "Không xem được trang: " + ex.Message; }
        }
        private static Bitmap Render(string path, int page)
        {
            using (var pdf = PdfiumViewer.PdfDocument.Load(path))
            {
                var size = pdf.PageSizes[page - 1]; double scale = 900 / Math.Max(size.Width, size.Height);
                using (var image = pdf.Render(page - 1, Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale)), true)) return new Bitmap(image);
            }
        }
        private static void SetImage(PictureBox box, Image image) { var old = box.Image; box.Image = image; if (old != null) old.Dispose(); }
        protected override void Dispose(bool disposing)
        { if (disposing) { DocumentJobCoordinator.Shared.Changed -= JobChanged; SetImage(before, null); SetImage(after, null); } base.Dispose(disposing); }
    }
}
