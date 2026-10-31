using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CountFile2.Services;

namespace CountFile2
{
    public class frmVietOCR : Form
    {
        private readonly TextBox source = new TextBox { Width = 430, AccessibleName = "File PDF hoặc thư mục đầu vào" };
        private readonly TextBox output = new TextBox { Width = 430, AccessibleName = "Thư mục kết quả" };
        private readonly TextBox python = new TextBox { Width = 430, Text = new VietOcrOptions().PythonPath, AccessibleName = "Python thực thi VietOCR" };
        private readonly CheckBox recursive = new CheckBox { Text = "Gồm thư mục con", Checked = true, AutoSize = true };
        private readonly CheckBox overwrite = new CheckBox { Text = "Ghi đè kết quả", AutoSize = true };
        private readonly ComboBox format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "Định dạng đầu ra" };
        private readonly ComboBox device = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, AccessibleName = "Thiết bị OCR" };
        private readonly ComboBox rotation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, AccessibleName = "Xoay trang theo chiều kim đồng hồ" };
        private readonly NumericUpDown dpi = new NumericUpDown { Minimum = 72, Maximum = 600, Value = 300, Width = 65, AccessibleName = "DPI render PDF" };
        private readonly NumericUpDown timeout = new NumericUpDown { Minimum = 30, Maximum = 3600, Value = 300, Width = 75, AccessibleName = "Thời gian chờ mỗi trang (giây)" };
        private readonly Button start = new Button { Text = "Bắt đầu OCR", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Hủy", AutoSize = true, Enabled = false };
        private readonly Button openOutput = new Button { Text = "Mở thư mục kết quả", AutoSize = true };
        private readonly TableLayoutPanel settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        private readonly DataGridView files = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        private readonly TextBox preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        private readonly ComboBox previewFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
        private readonly Label details = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(1200, 0) };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(8), AutoEllipsis = true };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 18 };
        private readonly List<VietOcrResult> results = new List<VietOcrResult>();
        private readonly CheckBox readNumbers = new CheckBox { Text = "Đọc số trang viết tay", Checked = true, AutoSize = true };
        private readonly CheckBox extractMetadata = new CheckBox { Text = "Bóc tách thông tin sau OCR", Checked = true, AutoSize = true };
        private readonly frmTrichXuatVanBan metadataForm;
        private readonly TabControl resultTabs;
        private readonly TabPage metadataTab;
        private readonly NumericUpDown numberRight = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 20, Width = 55, AccessibleName = "Chiều rộng vùng số bên phải (%)" };
        private readonly NumericUpDown numberTop = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 10, Width = 55, AccessibleName = "Chiều cao vùng số phía trên (%)" };
        private readonly VietOcrPageNumberPanel numberPanel;
        private CancellationTokenSource cancellation;
        private readonly bool paddle;
        public IList<VietOcrResult> Results { get { return results.AsReadOnly(); } }

        public frmVietOCR() : this(false) { }

        protected frmVietOCR(bool usePaddle)
        {
            paddle = usePaddle;
            Text = "Việt OCR"; Name = "frmVietOCR"; ClientSize = new Size(1100, 750);
            Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Font;
            format.Items.AddRange(new object[] { "Markdown + JSON", "Markdown (.md)", "JSON (.json)" }); format.SelectedIndex = 0;
            device.Items.AddRange(new object[] { "CPU", "CUDA GPU" }); device.SelectedIndex = 0;
            rotation.Items.AddRange(new object[] { "0°", "90°", "180°", "270°" }); rotation.SelectedIndex = 0;
            previewFormat.Items.AddRange(new object[] { "Xem Markdown", "Xem JSON" }); previewFormat.SelectedIndex = 0;
            AddRow(Button("Chọn PDF", ChoosePdf), Button("Chọn thư mục", () => ChooseFolder(source)), source);
            AddRow(Button("Thư mục đích", () => ChooseFolder(output)), output, Label("Trống: thư mục viet-ocr cạnh nguồn"));
            AddRow(Label("Xuất:"), format, recursive, overwrite, start, cancel, openOutput);
            var reviewMetadata = new Button { Text = "Rà soát / Xuất Excel", AutoSize = true };
            AddRow(extractMetadata, Button("Cấu hình LLM", () => metadataForm.ConfigureLlm()), reviewMetadata, Label("Gemini / LLM local • một dòng/PDF"));
            AddRow(Label("Python:"), python, Button("Chọn python.exe", ChoosePython));
            AddRow(Label("DPI:"), dpi, Label("Thiết bị:"), device, Label("Xoay trang:"), rotation, Label("Chờ/trang (giây):"), timeout);
            numberPanel = new VietOcrPageNumberPanel(ReadOptions);
            var numberTab = new TabPage("Số trang ghi tay"); numberTab.Controls.Add(numberPanel);
            if (!paddle)
            {
                var showRegion = new Button { Text = "Xem vùng số", AutoSize = true };
                AddRow(readNumbers, Label("Bên phải (%):"), numberRight, Label("Phía trên (%):"), numberTop, showRegion);
                showRegion.Click += async (s, e) => { if (numberTab.Parent is TabControl) ((TabControl)numberTab.Parent).SelectedTab = numberTab;
                    await numberPanel.PreviewAsync(Selected == null ? source.Text : Selected.InputPath, ReadOptions()); };
            }
            AddRow(Label("OCR mọi trang. Bảng được đọc thành dòng chữ; JSON giữ tọa độ để đối chiếu. Chọn góc xoay nếu bản scan nằm ngang."));
            Column("InputPath", "File PDF", 320); Column("Status", "Trạng thái", 155);
            Column("PageCount", "Tổng trang", 85); Column("CompletedPages", "Đã đọc", 75);
            Column("ReviewPages", "Cần kiểm tra", 100); Column("FailedPages", "Lỗi trang", 80);
            var previewPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var previewTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            previewTools.Controls.Add(previewFormat);
            previewTools.Controls.Add(Button("Mở file kết quả", OpenResult));
            previewPanel.Controls.Add(preview); previewPanel.Controls.Add(details); previewPanel.Controls.Add(previewTools);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                Size = new Size(1000, 460), SplitterDistance = 170, Panel1MinSize = 85, Panel2MinSize = 100 };
            split.Panel1.Controls.Add(files);
            resultTabs = new TabControl { Dock = DockStyle.Fill };
            var ocrTab = new TabPage("Kết quả OCR"); ocrTab.Controls.Add(split); resultTabs.TabPages.Add(ocrTab);
            var previewTabs = new TabControl { Dock = DockStyle.Fill };
            var textTab = new TabPage("Toàn văn"); textTab.Controls.Add(previewPanel); previewTabs.TabPages.Add(textTab);
            if (!paddle)
            {
                previewTabs.TabPages.Add(numberTab);
            }
            else { numberPanel.Dispose(); numberTab.Dispose(); }
            metadataForm = new frmTrichXuatVanBan { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
            metadataTab = new TabPage("Thông tin / Excel"); metadataTab.Controls.Add(metadataForm); resultTabs.TabPages.Add(metadataTab); metadataForm.Show();
            split.Panel2.Controls.Add(previewTabs);
            resultTabs.SelectedIndexChanged += (s, e) => settings.Visible = resultTabs.SelectedTab != metadataTab;
            reviewMetadata.Click += async (s, e) => { resultTabs.SelectedTab = metadataTab; await metadataForm.LoadOcrResultsAsync(results); };
            numberPanel.ResultChanged += (s, e) => {
                foreach (var item in results) if (item.Extraction != null) PostOcrExtractionService.SynchronizePageNumbers(item.Extraction);
                var binding = files.DataSource as BindingList<VietOcrResult>; if (binding != null) binding.ResetBindings(); ShowResult();
                status.Text = "Đã cập nhật số trang dùng chung. Lưu JSON trích xuất / xuất lại Excel để cập nhật báo cáo.";
            };
            Controls.Add(resultTabs); Controls.Add(progress); Controls.Add(status); Controls.Add(settings);
            status.Text = "Chọn một PDF hoặc thư mục rồi bắt đầu. Mỗi PDF có file .pdf.ocr.md / .pdf.ocr.json riêng; giữ cấu trúc thư mục nguồn.";
            start.Click += async (s, e) => await RunOcrAsync(source.Text, output.Text, ReadOptions());
            cancel.Click += (s, e) => { if (cancellation != null) { cancellation.Cancel(); status.Text = "Đang hủy OCR và dọn file tạm…"; } };
            openOutput.Click += (s, e) => OpenFolder();
            files.SelectionChanged += (s, e) => ShowResult();
            previewFormat.SelectedIndexChanged += (s, e) => ShowResult();
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            FormClosing += (s, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
            if (paddle)
            {
                Text = "PaddleOCR — Tiếng Việt"; Name = "frmPaddleOCR";
                python.Text = PaddleOcrDefaults.FindPython(); python.AccessibleName = "Python thực thi PaddleOCR";
                format.SelectedIndex = 2; previewFormat.SelectedIndex = 1;
                foreach (Control row in settings.Controls)
                    foreach (Control c in row.Controls)
                        if (c is Label) c.Text = c.Text.Replace("viet-ocr", "paddle-ocr");
                AddRow(Label("PP-OCRv5 • Tiếng Việt (vi) • latin_PP-OCRv5_mobile_rec. Dòng có điểm < 0,8 cần kiểm tra dấu."));
            }
            JobChanged(this, EventArgs.Empty);
        }

        private static Label Label(string text) { return new Label { Text = text, AutoSize = true, Padding = new Padding(0, 5, 0, 0) }; }
        private VietOcrOptions ReadOptions()
        {
            return new VietOcrOptions { EngineName = paddle ? "PaddleOCR" : "VietOCR", OutputFolderName = paddle ? "paddle-ocr" : "viet-ocr",
                SetupPath = paddle ? "PaddleOcr/setup.ps1" : "VietOcr/setup.ps1",
                WorkerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, paddle ? "PaddleOcr" : "VietOcr", "worker.py"),
                PythonPath = python.Text.Trim(), Dpi = (int)dpi.Value, Rotation = rotation.SelectedIndex * 90,
                Device = device.SelectedIndex == 0 ? "cpu" : "cuda:0", TimeoutSeconds = (int)timeout.Value,
                WriteText = false, WriteMarkdown = format.SelectedIndex != 2, WriteJson = format.SelectedIndex != 1,
                Recursive = recursive.Checked, Overwrite = overwrite.Checked, ReadPageNumbers = !paddle && readNumbers.Checked,
                ExtractMetadata = extractMetadata.Checked,
                LlmOptions = metadataForm.LlmOptions.Copy(),
                NumberRightPercent = (int)numberRight.Value, NumberTopPercent = (int)numberTop.Value };
        }
        private static Button Button(string text, Action action) { var b = new Button { Text = text, AutoSize = true }; b.Click += (s, e) => action(); return b; }
        private void AddRow(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            row.Controls.AddRange(controls); settings.Controls.Add(row, 0, settings.RowCount++);
            settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        private void Column(string property, string title, int width)
        { files.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, Width = width }); }
        private void ChoosePdf() { using (var d = new OpenFileDialog { Filter = "PDF|*.pdf", CheckFileExists = true }) if (d.ShowDialog(this) == DialogResult.OK) source.Text = d.FileName; }
        private void ChoosePython() { using (var d = new OpenFileDialog { Filter = "Python|python.exe|Executable|*.exe", CheckFileExists = true }) if (d.ShowDialog(this) == DialogResult.OK) python.Text = d.FileName; }
        private void ChooseFolder(TextBox box) { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath; }
        private void JobChanged(object sender, EventArgs e)
        {
            foreach (Control row in settings.Controls)
                foreach (Control c in row.Controls) c.Enabled = !DocumentJobCoordinator.Shared.IsBusy;
            cancel.Enabled = cancellation != null;
            if (!paddle) files.Enabled = !DocumentJobCoordinator.Shared.IsBusy;
        }
        private void RefreshFiles() { files.DataSource = new BindingList<VietOcrResult>(results.ToList()); }
        private VietOcrResult Selected { get { return files.CurrentRow == null ? null : files.CurrentRow.DataBoundItem as VietOcrResult; } }
        private string SelectedPath { get { var r = Selected; return r == null || r.Status == "Lỗi" || r.Status == "Đã hủy" ? null : previewFormat.SelectedIndex == 0 ? r.MarkdownPath ?? r.TextPath : r.JsonPath; } }
        private void ShowResult()
        {
            var r = Selected; preview.Clear();
            if (!paddle) numberPanel.SetDocument(r != null && r.Status != "Lỗi" && r.Status != "Đã hủy" ? r : null);
            details.Text = r == null ? "" : r.Details;
            string path = SelectedPath;
            if (path == null) { preview.Text = "Chưa có file ở định dạng đang chọn."; return; }
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8))
                {
                    var buffer = new char[200000]; int length = reader.ReadBlock(buffer, 0, buffer.Length);
                    preview.Text = new string(buffer, 0, length) + (reader.Peek() >= 0 ? "\r\n[Xem trước tối đa 200.000 ký tự. Mở file để đọc đầy đủ.]" : "");
                }
                details.Text += Environment.NewLine + path;
            }
            catch (Exception ex) { preview.Text = "Không xem được kết quả: " + ex.Message; }
        }
        private void OpenResult()
        { try { string path = SelectedPath; if (path != null && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; } }
        private void OpenFolder()
        {
            try
            {
                var r = results.FirstOrDefault(x => x.Status != "Lỗi" && x.Status != "Đã hủy");
                string path = r == null ? output.Text : Path.GetDirectoryName(r.MarkdownPath ?? r.TextPath ?? r.JsonPath);
                if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else status.Text = "Thư mục kết quả chưa tồn tại.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        public async Task RunOcrAsync(string input, string destination, VietOcrOptions options)
        {
            if (DocumentJobCoordinator.Shared.IsBusy) { status.Text = "Đang có tác vụ: " + DocumentJobCoordinator.Shared.ActiveJob; return; }
            cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin(paddle ? "PaddleOCR" : "Việt OCR", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            previewFormat.SelectedIndex = options != null && !options.WriteMarkdown && !options.WriteText ? 1 : 0;
            foreach (var old in results) old.Dispose(); results.Clear(); RefreshFiles(); progress.Value = 0; progress.Style = ProgressBarStyle.Marquee;
            bool accept = true;
            var reports = new Progress<VietOcrProgress>(p => {
                if (!accept || IsDisposed) return;
                progress.Style = p.TotalPages == 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                progress.Maximum = Math.Max(1, p.TotalPages); progress.Value = Math.Min(progress.Maximum, p.Page);
                status.Text = p.CompletedFiles + "/" + p.TotalFiles + " file • " + Path.GetFileName(p.InputPath) + " • " + p.Status +
                    (p.TotalPages > 0 ? " • Trang " + p.Page + "/" + p.TotalPages : "");
                if (p.Result != null) { results.Add(p.Result); RefreshFiles(); }
            });
            try
            {
                var done = await new VietOcrService().ProcessAsync(input, destination, options, cancellation.Token, reports);
                accept = false; results.Clear(); results.AddRange(done); RefreshFiles();
                status.Text = "Đã xử lý " + results.Count + " PDF: " + results.Count(r => r.Status == "Hoàn tất") + " hoàn tất; " +
                    results.Count(r => r.Status == "Cần kiểm tra") + " cần kiểm tra; " + results.Count(r => r.Status == "Lỗi" || r.FailedPages > 0) + " có lỗi. Kết quả giữ toàn văn theo từng trang.";
                progress.Value = progress.Maximum;
            }
            catch (VietOcrCanceledException ex)
            { accept = false; results.Clear(); results.AddRange(ex.Results); RefreshFiles(); status.Text = "Đã hủy. Giữ các file OCR đã xuất; xem trạng thái từng PDF và có thể chạy lại LLM sau."; }
            catch (OperationCanceledException) { status.Text = "Đã hủy Việt OCR."; }
            catch (Exception ex) { status.Text = "Lỗi: " + ex.Message; preview.Text = ex.Message; }
            finally
            {
                accept = false; progress.Style = ProgressBarStyle.Continuous;
                var completed = cancellation; cancellation = null;
                DocumentJobCoordinator.Shared.End(completed); completed.Dispose();
                if (!paddle) await numberPanel.RefreshEvidenceAsync();
                if (options != null && options.ExtractMetadata && !IsDisposed) await metadataForm.LoadOcrResultsAsync(results);
            }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { DocumentJobCoordinator.Shared.Changed -= JobChanged; if (cancellation != null) cancellation.Cancel(); foreach (var result in results) result.Dispose(); } base.Dispose(disposing); }
    }
}
