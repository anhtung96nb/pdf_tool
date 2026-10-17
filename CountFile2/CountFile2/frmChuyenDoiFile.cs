using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CountFile2.Services;

namespace CountFile2
{
    public class frmChuyenDoiFile : Form
    {
        private readonly ComboBox direction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        private readonly TextBox source = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox output = new TextBox { Dock = DockStyle.Fill };
        private readonly NumericUpDown dpi = new NumericUpDown { Minimum = 1, Maximum = 1200, Value = 300, Width = 80 };
        private readonly CheckBox overwrite = new CheckBox { Text = "Ghi đè file đã tồn tại", AutoSize = true };
        private readonly Button start = new Button { Text = "Bắt đầu", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Hủy", Enabled = false, AutoSize = true };
        private readonly Button open = new Button { Text = "Mở thư mục kết quả", AutoSize = true, Enabled = false };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill };
        private readonly Label status = new Label { Text = "Chọn nguồn và thư mục đầu ra.", AutoSize = true };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        private readonly TableLayoutPanel options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        private readonly Dictionary<string, DataGridViewRow> rows = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource cancellation;
        private int runId;
        private string resultDirectory;
        private readonly bool pdfProcessing;
        private readonly CheckBox recursive = new CheckBox { Text = "Quét thư mục con", Checked = true, AutoSize = true };
        private readonly TextBox language = new TextBox { Text = "vie", Width = 100 };
        private readonly TextBox tessdata = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox fontPath = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox iccPath = new TextBox { Dock = DockStyle.Fill };
        private readonly TableLayoutPanel advanced = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Visible = false };
        private readonly Button advancedButton = new Button { Text = "Cấu hình nâng cao", AutoSize = true };

        public frmChuyenDoiFile() : this(false) { }
        protected frmChuyenDoiFile(bool pdfMode)
        {
            pdfProcessing = pdfMode;
            Text = pdfMode ? "PDF hai lớp / PDF/A" : "Chuyển đổi TIFF ↔ PDF";
            Name = pdfMode ? "frmPdfProcessing" : "frmChuyenDoiFile";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 600);
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterParent;
            direction.Width = 260;
            direction.Items.AddRange(pdfMode ? new object[] { "PDF hai lớp (OCR)", "PDF/A-1b", "PDF/A-1b có OCR" }
                : new object[] { "TIFF → PDF", "PDF → TIFF" });
            direction.SelectedIndex = 0;
            dpi.Enabled = pdfMode;
            if (pdfMode) { dpi.Minimum = 72; dpi.Maximum = 600; }
            direction.SelectedIndexChanged += (s, e) => {
                dpi.Enabled = pdfMode || direction.SelectedIndex == 1;
                if (!pdfMode) source.Clear();
                UpdateAdvanced();
            };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddOption(pdfMode ? "Chế độ" : "Chiều chuyển đổi", direction, 0);
            AddOption("Nguồn", source, 1);
            var file = new Button { Text = "Chọn file", AutoSize = true };
            var folder = new Button { Text = "Chọn thư mục", AutoSize = true };
            file.Click += (s, e) => {
                using (var dialog = new OpenFileDialog { Filter = pdfMode || direction.SelectedIndex == 1
                    ? "PDF (*.pdf)|*.pdf" : "TIFF (*.tif;*.tiff)|*.tif;*.tiff" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.FileName;
            };
            folder.Click += (s, e) => ChooseFolder(source);
            options.Controls.Add(file, 2, 1); options.Controls.Add(folder, 3, 1);
            AddOption("Thư mục đầu ra", output, 2);
            var destination = new Button { Text = "Chọn thư mục", AutoSize = true };
            destination.Click += (s, e) => ChooseFolder(output);
            options.Controls.Add(destination, 2, 2); options.SetColumnSpan(destination, 2);
            var settings = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            settings.Controls.Add(new Label { Text = pdfMode ? "DPI:" : "DPI (PDF → TIFF):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            settings.Controls.Add(dpi); settings.Controls.Add(overwrite);
            if (pdfMode) settings.Controls.Add(recursive);
            AddOption("Tùy chọn", settings, 3); options.SetColumnSpan(settings, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            actions.Controls.AddRange(new Control[] { start, cancel, open });
            if (pdfMode) actions.Controls.Add(advancedButton);
            advancedButton.Click += (s, e) => advanced.Visible = !advanced.Visible;
            start.Click += StartConversion;
            cancel.Click += (s, e) => RequestCancel();
            open.Click += (s, e) => {
                try { Process.Start(new ProcessStartInfo(resultDirectory) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không mở được thư mục"); }
            };
            foreach (string heading in new[] { "File nguồn", "File đích", "Số trang", "Trạng thái", "Thông báo lỗi" })
                grid.Columns.Add(heading, heading);
            grid.Columns[2].FillWeight = 35; grid.Columns[3].FillWeight = 65;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 7, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(i == 6 ? SizeType.Percent : SizeType.AutoSize, i == 6 ? 100 : 0));
            layout.Controls.Add(options, 0, 0);
            var notice = new Label { AutoSize = true, MaximumSize = new Size(850, 0), Text = pdfMode
                ? "Trang được dựng lại bằng ảnh; không giữ vector, form và chữ ký số. Đầu ra trống: xuất cạnh nguồn. Nên chọn thư mục đầu ra ngoài cây nguồn."
                : "Giữ cấu trúc thư mục con. PDF từ TIFF chứa ảnh; văn bản có thể chọn không được khôi phục.", Margin = new Padding(3, 8, 3, 8) };
            layout.Controls.Add(notice, 0, 1);
            layout.SizeChanged += (s, e) => notice.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 35), 0);
            layout.Controls.Add(actions, 0, 2); layout.Controls.Add(progress, 0, 3);
            layout.Controls.Add(status, 0, 4); layout.Controls.Add(advanced, 0, 5); layout.Controls.Add(grid, 0, 6);
            var defaults = new PdfProcessingOptions();
            tessdata.Text = defaults.TessDataPath; fontPath.Text = defaults.FontPath; iccPath.Text = defaults.ColorProfilePath;
            advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Control[] fields = { language, tessdata, fontPath, iccPath };
            string[] labels = { "Ngôn ngữ OCR", "Thư mục tessdata", "Font TrueType", "ICC sRGB" };
            for (int i = 0; i < fields.Length; i++) {
                advanced.Controls.Add(new Label { Text = labels[i], AutoSize = true }, 0, i);
                advanced.Controls.Add(fields[i], 1, i);
            }
            UpdateAdvanced();
            Controls.Add(layout);
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            JobChanged(this, EventArgs.Empty);
            FormClosing += (s, e) => { if (cancellation != null) { e.Cancel = true; RequestCancel(); } };
        }

        private void UpdateAdvanced()
        {
            bool ocr = direction.SelectedIndex != 1;
            language.Enabled = tessdata.Enabled = fontPath.Enabled = ocr;
            iccPath.Enabled = direction.SelectedIndex != 0;
        }
        private void JobChanged(object sender, EventArgs e)
        { start.Enabled = !DocumentJobCoordinator.Shared.IsBusy; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) DocumentJobCoordinator.Shared.Changed -= JobChanged;
            base.Dispose(disposing);
        }

        private void AddOption(string label, Control control, int row)
        {
            options.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            options.Controls.Add(control, 1, row);
        }

        private void ChooseFolder(TextBox target)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Chọn thư mục", SelectedPath = target.Text })
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
        }

        private void RequestCancel()
        {
            if (cancellation == null) return;
            cancellation.Cancel(); cancel.Enabled = false;
            status.Text = "Đang hủy và dọn file tạm. Vui lòng chờ trước khi đóng cửa sổ.";
        }

        private void ShowProgress(ConversionProgress item)
        {
            DataGridViewRow row;
            if (!rows.TryGetValue(item.InputPath, out row)) {
                row = grid.Rows[grid.Rows.Add(item.InputPath, item.OutputPath, "", "Chờ", "")];
                rows.Add(item.InputPath, row);
            }
            row.Cells[3].Value = item.Status;
            if (item.Result != null) {
                row.Cells[2].Value = item.Result.PageCount;
                row.Cells[4].Value = item.Result.Error == null ? "" : item.Result.Error.Message;
                row.DefaultCellStyle.ForeColor = item.Result.Success ? Color.DarkGreen : Color.Firebrick;
            }
            progress.Maximum = Math.Max(1, item.TotalFiles);
            progress.Value = Math.Min(item.CompletedFiles, progress.Maximum);
            status.Text = string.Format("{0}/{1} file hoàn tất — {2}{3}", item.CompletedFiles, item.TotalFiles,
                item.Status, item.TotalPages > 0 ? string.Format(" — trang {0}/{1}", item.Page, item.TotalPages) : "");
        }

        private async void StartConversion(object sender, EventArgs e)
        {
            if (DocumentJobCoordinator.Shared.IsBusy) {
                status.Text = "Tác vụ đang chạy: " + DocumentJobCoordinator.Shared.ActiveJob; return;
            }
            string inputPath = source.Text.Trim(), outputPath = output.Text.Trim();
            bool toTiff = direction.SelectedIndex == 1;
            int resolution = (int)dpi.Value;
            bool replace = overwrite.Checked;
            int mode = direction.SelectedIndex;
            var pdfOptions = new PdfProcessingOptions { Dpi = resolution, Overwrite = replace, Recursive = recursive.Checked,
                Language = language.Text.Trim(), TessDataPath = tessdata.Text.Trim(), FontPath = fontPath.Text.Trim(), ColorProfilePath = iccPath.Text.Trim() };
            try {
                if (!File.Exists(inputPath) && !Directory.Exists(inputPath)) throw new IOException("Nguồn không tồn tại.");
                if (File.Exists(inputPath)) {
                    string ext = Path.GetExtension(inputPath).ToLowerInvariant();
                    if (pdfProcessing || toTiff ? ext != ".pdf" : ext != ".tif" && ext != ".tiff") throw new IOException("Định dạng nguồn không phù hợp chiều chuyển đổi.");
                }
                if (string.IsNullOrWhiteSpace(outputPath)) {
                    if (!pdfProcessing) throw new IOException("Vui lòng chọn thư mục đầu ra.");
                    outputPath = null;
                } else {
                    outputPath = Path.GetFullPath(outputPath);
                    if (File.Exists(outputPath)) throw new IOException("Đầu ra phải là thư mục.");
                }
                if (pdfProcessing) {
                    if (mode != 1) {
                        if (string.IsNullOrWhiteSpace(pdfOptions.Language)) throw new IOException("Nhập ngôn ngữ OCR.");
                        foreach (string lang in pdfOptions.Language.Split('+'))
                            if (lang.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || string.IsNullOrWhiteSpace(lang)
                                || !File.Exists(Path.Combine(pdfOptions.TessDataPath, lang + ".traineddata")))
                                throw new IOException("Thiếu traineddata cho ngôn ngữ: " + lang);
                        if (!File.Exists(pdfOptions.FontPath)) throw new IOException("Không tìm thấy font OCR.");
                    }
                    if (mode != 0 && !File.Exists(pdfOptions.ColorProfilePath)) throw new IOException("Không tìm thấy ICC sRGB.");
                    string prefix = Path.GetFullPath(inputPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    if (Directory.Exists(inputPath) && outputPath != null && (outputPath + Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && MessageBox.Show(this, "Thư mục kết quả nằm trong cây nguồn. Lần chạy sau có thể xử lý lại file kết quả. Tiếp tục?", "Thư mục đầu ra", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                }
            } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Kiểm tra dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            cancellation = new CancellationTokenSource();
            DocumentJobCoordinator.Shared.TryBegin(Text, cancellation);
            options.Enabled = start.Enabled = open.Enabled = false; cancel.Enabled = true;
            advanced.Enabled = advancedButton.Enabled = false;
            grid.Rows.Clear(); rows.Clear(); progress.Value = 0;
            progress.Style = ProgressBarStyle.Marquee; status.Text = "Đang quét danh sách file…";
            int currentRun = ++runId;
            var watch = Stopwatch.StartNew();
            IList<ConversionResult> results = new List<ConversionResult>();
            bool canceled = false;
            string failure = null;
            var reporter = new Progress<ConversionProgress>(item => {
                if (runId != currentRun || cancellation == null) return;
                progress.Style = ProgressBarStyle.Blocks; ShowProgress(item);
            });
            try {
                if (pdfProcessing) {
                    var service = new PdfProcessingService();
                    results = mode == 0 ? await service.ConvertToSearchablePdfAsync(inputPath, outputPath, pdfOptions, cancellation.Token, reporter)
                        : await service.ConvertToPdfAAsync(inputPath, outputPath, pdfOptions, mode == 2, cancellation.Token, reporter);
                } else {
                    var service = new PdfTiffConversionService();
                    results = toTiff ? await service.ConvertPdfToTiffAsync(inputPath, outputPath, resolution, replace, cancellation.Token, reporter)
                        : await service.ConvertTiffToPdfAsync(inputPath, outputPath, replace, cancellation.Token, reporter);
                }
            } catch (ConversionCanceledException ex) { canceled = true; results = ex.Results; }
            catch (OperationCanceledException) { canceled = true; }
            catch (Exception ex) { failure = ex.Message; }
            finally {
                ++runId; watch.Stop(); DocumentJobCoordinator.Shared.End(cancellation); cancellation.Dispose(); cancellation = null;
                options.Enabled = start.Enabled = true; cancel.Enabled = false;
                advanced.Enabled = advancedButton.Enabled = true;
                progress.Style = ProgressBarStyle.Blocks;
            }
            // Apply the returned snapshot even if posted progress callbacks are still queued.
            foreach (var result in results) ShowProgress(new ConversionProgress {
                InputPath = result.InputPath, OutputPath = result.OutputPath, Result = result,
                Status = result.Success ? "Thành công" : "Lỗi", CompletedFiles = results.Count,
                TotalFiles = Math.Max(rows.Count, results.Count) });
            foreach (DataGridViewRow row in grid.Rows)
                if ((string)row.Cells[3].Value == "Chờ" || (string)row.Cells[3].Value == "Đang xử lý")
                    row.Cells[3].Value = canceled ? "Đã hủy" : "Chưa xử lý";
            resultDirectory = outputPath ?? (Directory.Exists(inputPath) ? inputPath : Path.GetDirectoryName(Path.GetFullPath(inputPath)));
            open.Enabled = Directory.Exists(resultDirectory);
            status.Text = failure != null ? "Lỗi: " + failure : results.Count == 0 && !canceled ? "Không có file phù hợp để chuyển đổi."
                : string.Format("{0}: {1} thành công, {2} lỗi — {3:0.0} giây.", canceled ? "Đã hủy" : "Hoàn tất",
                    results.Count(r => r.Success), results.Count(r => !r.Success), watch.Elapsed.TotalSeconds);
        }
    }
}
