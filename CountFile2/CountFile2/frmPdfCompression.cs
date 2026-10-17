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
    public sealed class frmPdfCompression : Form
    {
        private readonly TextBox source = new TextBox { Width = 310 };
        private readonly TextBox output = new TextBox { Width = 310 };
        private readonly NumericUpDown minDpi = new NumericUpDown { Minimum = 1, Maximum = 2400, Value = 150, Width = 75 };
        private readonly NumericUpDown reduction = new NumericUpDown { Minimum = .01m, Maximum = 99.99m, DecimalPlaces = 2, Value = 40, Width = 75 };
        private readonly CheckBox recursive = new CheckBox { Text = "Thư mục con", Checked = true, AutoSize = true };
        private readonly CheckBox overwrite = new CheckBox { Text = "Ghi đè đầu ra", AutoSize = true };
        private readonly Button start = new Button { Text = "Bắt đầu", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Hủy", AutoSize = true, Enabled = false };
        private readonly Button export = new Button { Text = "Xuất CSV", AutoSize = true };
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 145, AutoScroll = true, Padding = new Padding(5) };
        private readonly DataGridView files = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true,
            AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        private readonly TextBox details = new TextBox { Dock = DockStyle.Bottom, Height = 100, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 48, AutoEllipsis = true };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Bottom };
        private readonly List<PdfCompressionResult> results = new List<PdfCompressionResult>();
        private CancellationTokenSource cancellation;
        public IList<PdfCompressionResult> Results { get { return results.AsReadOnly(); } }

        public frmPdfCompression()
        {
            Text = "Giảm dung lượng PDF"; Name = "frmPdfCompression"; ClientSize = new Size(1100, 750);
            Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Font;
            AddButton("Chọn PDF", () => { using (var dialog = new OpenFileDialog { Filter = "PDF|*.pdf" })
                if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.FileName; });
            AddButton("Chọn thư mục", () => ChooseFolder(source)); toolbar.Controls.Add(source); toolbar.SetFlowBreak(source, true);
            AddButton("Đầu ra", () => ChooseFolder(output)); toolbar.Controls.Add(output);
            toolbar.Controls.Add(new Label { Text = "Để trống: lưu vào thư mục compressed cạnh nguồn", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            toolbar.SetFlowBreak(toolbar.Controls[toolbar.Controls.Count - 1], true);
            toolbar.Controls.Add(new Label { Text = "DPI tối thiểu:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }); toolbar.Controls.Add(minDpi);
            toolbar.Controls.Add(new Label { Text = "Giảm dung lượng (%):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }); toolbar.Controls.Add(reduction);
            toolbar.Controls.AddRange(new Control[] { recursive, overwrite, start, cancel, export });
            Column("InputPath", "File nguồn", 230); Column("Status", "Trạng thái", 220);
            Column("OriginalBytes", "Byte trước", 100, "N0"); Column("OutputBytes", "Byte sau", 100, "N0");
            Column("RequestedReduction", "Giảm yêu cầu %", 100, "N2"); Column("ActualReduction", "Giảm thực tế %", 100, "N2");
            Column("MinDpi", "DPI cấu hình", 90, "N2"); Column("BeforeDpi", "DPI trước", 90, "N2"); Column("AfterDpi", "DPI sau", 90, "N2");
            Controls.Add(files); Controls.Add(details); Controls.Add(progress); Controls.Add(status); Controls.Add(toolbar);
            status.Text = "Giữ file gốc. Nếu không đạt tỷ lệ giảm, lưu kết quả tốt nhất và ghi chú DPI. Ảnh đầu vào dưới ngưỡng sẽ được báo riêng.";
            start.Click += async (s, e) => await RunCompressionAsync(source.Text, output.Text, new PdfCompressionOptions {
                MinDpi = (double)minDpi.Value, ReductionPercent = (double)reduction.Value, Recursive = recursive.Checked, Overwrite = overwrite.Checked });
            cancel.Click += (s, e) => { if (cancellation != null) cancellation.Cancel(); };
            export.Click += (s, e) => { using (var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "pdf-compression.csv" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try { PdfCompressionService.ExportCsv(dialog.FileName, results); status.Text = "Đã xuất " + dialog.FileName; }
                    catch (Exception ex) { status.Text = "Không lưu được báo cáo: " + ex.Message; } };
            files.SelectionChanged += (s, e) => { var r = files.CurrentRow == null ? null : files.CurrentRow.DataBoundItem as PdfCompressionResult;
                details.Text = r == null ? "" : "Đầu ra: " + r.OutputPath + Environment.NewLine + r.Details; };
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            FormClosing += (s, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); status.Text = "Đang hủy; chờ dọn file tạm."; } };
            JobChanged(this, EventArgs.Empty);
        }
        private void Column(string property, string label, int width, string format = null)
        {
            var column = new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = label, Width = width };
            if (format != null) column.DefaultCellStyle.Format = format;
            column.DefaultCellStyle.NullValue = "N/A"; files.Columns.Add(column);
        }
        private void AddButton(string text, Action action) { var button = new Button { Text = text, AutoSize = true }; button.Click += (s, e) => action(); toolbar.Controls.Add(button); }
        private void ChooseFolder(TextBox box) { using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; }
        private void JobChanged(object sender, EventArgs e)
        { foreach (Control control in toolbar.Controls) control.Enabled = !DocumentJobCoordinator.Shared.IsBusy; cancel.Enabled = cancellation != null; }
        private void RefreshFiles() { files.DataSource = new BindingList<PdfCompressionResult>(results.ToList()); }

        public async Task RunCompressionAsync(string inputPath, string outputPath, PdfCompressionOptions options)
        {
            if (DocumentJobCoordinator.Shared.IsBusy) return;
            cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin("Giảm dung lượng PDF", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            results.Clear(); RefreshFiles();
            bool acceptProgress = true;
            var reports = new Progress<PdfCompressionProgress>(p => {
                if (!acceptProgress) return;
                progress.Maximum = Math.Max(1, p.TotalFiles); progress.Value = Math.Min(progress.Maximum, p.CompletedFiles);
                if (p.Result != null) { results.Add(p.Result); RefreshFiles(); }
                status.Text = p.CompletedFiles + "/" + p.TotalFiles + " file - " + Path.GetFileName(p.InputPath) + " - " + p.Status;
            });
            try
            {
                var done = await new PdfCompressionService().ProcessAsync(inputPath, outputPath, options, cancellation.Token, reports);
                acceptProgress = false; results.Clear(); results.AddRange(done); RefreshFiles();
                progress.Value = progress.Maximum;
                status.Text = "Hoàn tất: " + results.Count + " file; " + results.Count(r => r.Written && !r.TargetReached) + " không đạt tỷ lệ giảm; "
                    + results.Count(r => r.Written && !r.DpiSatisfied) + " dưới ngưỡng DPI; " + results.Count(r => r.Error != null) + " lỗi. " + SaveReport();
            }
            catch (PdfCompressionCanceledException ex)
            { acceptProgress = false; results.Clear(); results.AddRange(ex.Results); RefreshFiles(); status.Text = "Đã hủy. Giữ " + results.Count + " kết quả hoàn tất. " + SaveReport(); }
            catch (OperationCanceledException) { status.Text = "Đã hủy."; }
            catch (Exception ex) { status.Text = "Lỗi: " + ex.Message; }
            finally { acceptProgress = false; var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); }
        }
        private string SaveReport()
        {
            var first = results.FirstOrDefault(r => r.Written);
            if (first == null) return "";
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(first.OutputPath), "pdf-compression-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv");
                PdfCompressionService.ExportCsv(path, results); return "Báo cáo: " + path;
            }
            catch (Exception ex) { return "Không lưu được báo cáo: " + ex.Message; }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) DocumentJobCoordinator.Shared.Changed -= JobChanged; base.Dispose(disposing); }
    }
}
