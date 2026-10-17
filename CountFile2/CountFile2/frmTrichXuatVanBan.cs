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
    public sealed class frmTrichXuatVanBan : Form
    {
        private readonly TextBox source = new TextBox { Width = 320 };
        private readonly CheckBox recursive = new CheckBox { Text = "Gồm thư mục con", Checked = true, AutoSize = true };
        private readonly CheckBox force = new CheckBox { Text = "Buộc OCR", AutoSize = true };
        private readonly Button start = new Button { Text = "Trích xuất", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Hủy", Enabled = false };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 48, AutoEllipsis = true };
        private readonly DataGridView documents = Grid();
        private readonly DataGridView fields = Grid();
        private readonly PictureBox preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.DimGray };
        private readonly NumericUpDown page = new NumericUpDown { Minimum = 1, Maximum = 1, Width = 60 };
        private readonly TextBox raw = new TextBox { Dock = DockStyle.Bottom, Height = 100, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 85, AutoScroll = true, Padding = new Padding(5) };
        private readonly List<AdministrativeDocument> results = new List<AdministrativeDocument>();
        private CancellationTokenSource cancellation;
        private AdministrativeDocument selected;
        private ExtractedField selectedField;
        private bool changing;
        private string checkpoint;
        private static DataGridView Grid() { return new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false }; }
        private static void Column(DataGridView grid, string name, string label, int width, bool readOnly = true) {
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = name, HeaderText = label, Width = width, ReadOnly = readOnly });
        }
        public frmTrichXuatVanBan()
        {
            Text = "Trích xuất văn bản PDF"; Name = "frmTrichXuatVanBan"; ClientSize = new Size(1100, 720); Font = new Font("Segoe UI", 9);
            AddButton("Chọn PDF", () => { using (var dialog = new OpenFileDialog { Filter = "PDF|*.pdf" }) if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.FileName; });
            AddButton("Chọn thư mục", () => { using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.SelectedPath; });
            toolbar.Controls.Add(source); toolbar.Controls.Add(recursive); toolbar.Controls.Add(force); toolbar.Controls.Add(start); toolbar.Controls.Add(cancel);
            AddButton("Mở JSON / tiếp tục", LoadResults); AddButton("Lưu JSON", () => Export(false)); AddButton("Xuất Excel", () => Export(true));
            AddButton("Xóa bảng kết quả", () => { results.Clear(); RefreshDocuments(); status.Text = "Đã xóa bảng kết quả. Các file đã lưu vẫn được giữ."; });
            Column(documents, "Source", "File PDF", 250); Column(documents, "PageCount", "Trang", 50); Column(documents, "Status", "Trạng thái", 105);
            Column(fields, "Name", "Trường", 125); Column(fields, "Value", "Giá trị (có thể sửa)", 230, false);
            fields.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Confirmed", HeaderText = "Xác nhận", Width = 65 });
            Column(fields, "Warning", "Kiểm tra", 220);
            fields.DefaultCellStyle.WrapMode = DataGridViewTriState.True; fields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(560, 600), SplitterDistance = 180 };
            left.Panel1.Controls.Add(documents); left.Panel2.Controls.Add(fields);
            var right = new Panel { Dock = DockStyle.Fill }; var pages = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
            pages.Controls.Add(new Label { Text = "Trang PDF:", AutoSize = true }); pages.Controls.Add(page);
            right.Controls.Add(preview); right.Controls.Add(raw); right.Controls.Add(pages);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = ClientSize, SplitterDistance = 580 };
            split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right); Controls.Add(split); Controls.Add(status); Controls.Add(toolbar);
            status.Text = "Chọn nguồn. Mỗi PDF là một tài liệu; cần đối chiếu các trường trước khi xác nhận.";
            start.Click += StartExtraction; cancel.Click += (s, e) => { if (cancellation != null) cancellation.Cancel(); };
            documents.SelectionChanged += (s, e) => SelectDocument();
            fields.SelectionChanged += (s, e) => SelectField();
            fields.CurrentCellDirtyStateChanged += (s, e) => { if (fields.IsCurrentCellDirty) fields.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            fields.CellValueChanged += (s, e) => {
                if (changing || e.RowIndex < 0) return;
                var f = fields.Rows[e.RowIndex].DataBoundItem as ExtractedField;
                if (f != null) {
                    if (e.ColumnIndex == 1) f.Confirmed = false;
                    f.Warning = f.Confirmed ? "Đã xác nhận thủ công." : "Chưa được người dùng xác nhận."; documents.Refresh(); fields.Refresh();
                }
            };
            fields.DataError += (s, e) => { status.Text = "Giá trị nhập không hợp lệ."; e.ThrowException = false; };
            page.ValueChanged += (s, e) => { if (!changing) RenderPreview(); };
            preview.Paint += PaintEvidence;
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            FormClosing += (s, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); status.Text = "Đang hủy; đóng lại sau khi xử lý dừng."; } };
            JobChanged(this, EventArgs.Empty);
        }
        private void AddButton(string text, Action action) { var b = new Button { Text = text, AutoSize = true }; b.Click += (s, e) => action(); toolbar.Controls.Add(b); }
        private void JobChanged(object sender, EventArgs e) {
            bool busy = DocumentJobCoordinator.Shared.IsBusy;
            foreach (Control c in toolbar.Controls) c.Enabled = !busy;
            cancel.Enabled = cancellation != null; fields.ReadOnly = busy;
        }
        private async void StartExtraction(object sender, EventArgs e)
        {
            string input = source.Text; bool recurse = recursive.Checked, forced = force.Checked;
            using (var dialog = new SaveFileDialog { Filter = "Kết quả JSON|*.json", FileName = "trich-xuat-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return; checkpoint = dialog.FileName;
            }
            await RunExtractionAsync(input, recurse, forced, checkpoint);
        }
        public async Task RunExtractionAsync(string input, bool recurse, bool forced, string resultPath)
        {
            if (DocumentJobCoordinator.Shared.IsBusy) return;
            if (!Path.GetExtension(resultPath).Equals(".json", StringComparison.OrdinalIgnoreCase)) { status.Text = "Kết quả phải lưu dưới dạng .json."; return; }
            checkpoint = resultPath;
            cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin("Trích xuất văn bản PDF", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            try {
                fields.EndEdit();
                var files = await Task.Run(() => AdministrativeExtractionService.Discover(input, recurse, cancellation.Token));
                if (files.Count == 0) { status.Text = "Không tìm thấy PDF."; return; }
                await Task.Run(() => ExtractionExportService.SaveJson(checkpoint, results));
                // Resume only after comparing the source content, never by filename alone.
                var service = new AdministrativeExtractionService { ForceOcr = forced };
                var progress = new Progress<ExtractionProgress>(p => status.Text = Path.GetFileName(p.File) + " — trang " + p.Page + "/" + p.Pages);
                foreach (string file in files) {
                    cancellation.Token.ThrowIfCancellationRequested();
                    string fingerprint = await Task.Run(() => Fingerprint(file));
                    var previous = fingerprint == null ? null : results.FirstOrDefault(d => d.Source == file && d.Error == null && d.Fingerprint == fingerprint && d.UsedForceOcr == forced);
                    if (previous != null) continue;
                    var result = await Task.Run(() => service.Process(file, cancellation.Token, progress));
                    result.Fingerprint = fingerprint; result.UsedForceOcr = forced;
                    results.RemoveAll(d => d.Source == file); results.Add(result);
                    await Task.Run(() => ExtractionExportService.SaveJson(checkpoint, results));
                    RefreshDocuments();
                }
                await Task.Run(() => ExtractionExportService.SaveJson(checkpoint, results));
                status.Text = "Hoàn tất: " + results.Count + " tài liệu; " + results.Count(d => d.Error != null) + " lỗi. Đã lưu " + checkpoint;
            } catch (OperationCanceledException) { status.Text = "Đã hủy. Các file hoàn tất đã lưu tại " + checkpoint; }
            catch (Exception ex) { status.Text = "Lỗi: " + ex.Message; }
            finally { var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); }
        }
        private static string Fingerprint(string file) {
            try { using (var stream = File.OpenRead(file)) using (var hash = System.Security.Cryptography.SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        private void RefreshDocuments() { documents.DataSource = new BindingList<AdministrativeDocument>(results.ToList()); }
        private void LoadResults() {
            using (var dialog = new OpenFileDialog { Filter = "Kết quả JSON|*.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) {
                try { var loaded = ExtractionExportService.LoadJson(dialog.FileName); results.Clear(); results.AddRange(loaded); checkpoint = dialog.FileName; RefreshDocuments(); status.Text = "Đã mở kết quả. Chọn nguồn rồi Trích xuất để tiếp tục; file thay đổi sẽ được xử lý lại."; }
                catch (Exception ex) { status.Text = ex.Message; }
            }
        }
        private void Export(bool excel) {
            fields.EndEdit(); if (results.Count == 0) { status.Text = "Chưa có kết quả."; return; }
            using (var dialog = new SaveFileDialog { Filter = excel ? "Excel|*.xlsx" : "JSON|*.json", FileName = excel ? "van-ban.xlsx" : "van-ban.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK) try {
                    if (!Path.GetExtension(dialog.FileName).Equals(excel ? ".xlsx" : ".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Đuôi file xuất không đúng định dạng.");
                    if (excel) ExtractionExportService.SaveExcel(dialog.FileName, results); else ExtractionExportService.SaveJson(dialog.FileName, results);
                    status.Text = "Đã lưu " + dialog.FileName;
                } catch (Exception ex) { status.Text = ex.Message; }
        }
        private void SelectDocument() {
            selected = documents.CurrentRow == null ? null : documents.CurrentRow.DataBoundItem as AdministrativeDocument;
            changing = true; fields.DataSource = selected == null ? null : new BindingList<ExtractedField>(selected.Fields);
            page.Maximum = selected == null ? 1 : Math.Max(1, selected.PageCount); page.Value = 1; changing = false;
            selectedField = null; RenderPreview();
            if (selected != null) status.Text = selected.Error ?? string.Join(" ", selected.Warnings);
        }
        private void SelectField() {
            if (changing) return;
            selectedField = fields.CurrentRow == null ? null : fields.CurrentRow.DataBoundItem as ExtractedField;
            if (selectedField != null && selectedField.Evidence.Count > 0) page.Value = Math.Max(1, Math.Min(page.Maximum, selectedField.Evidence[0].Page));
            RenderPreview();
        }
        private void RenderPreview() {
            var old = preview.Image; preview.Image = null; if (old != null) old.Dispose(); raw.Clear();
            if (selected == null || selected.PageCount == 0) return;
            raw.Text = string.Join(Environment.NewLine, selected.Lines.Where(l => l.Page == (int)page.Value).Select(l => l.Text));
            try {
                using (var pdf = PdfiumViewer.PdfDocument.Load(selected.Source)) {
                    var size = pdf.PageSizes[(int)page.Value - 1]; int w = 1000, h = (int)(1000 * size.Height / size.Width);
                    if (h > 5000) throw new InvalidDataException("Trang quá cao để xem trước.");
                    using (var image = pdf.Render((int)page.Value - 1, w, h, true)) {
                        var bitmap = new Bitmap(image);
                        int rotation = selected.Lines.Where(l => l.Page == (int)page.Value).Select(l => l.RotationDegrees).FirstOrDefault();
                        if (rotation != 0) bitmap.RotateFlip(rotation == 90 ? RotateFlipType.Rotate90FlipNone : rotation == 180 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                        preview.Image = bitmap;
                    }
                }
            } catch (Exception ex) { raw.AppendText(Environment.NewLine + "Không xem được PDF: " + ex.Message); }
            preview.Invalidate();
        }
        private void PaintEvidence(object sender, PaintEventArgs e) {
            if (preview.Image == null || selectedField == null) return;
            float scale = Math.Min((float)preview.ClientSize.Width / preview.Image.Width, (float)preview.ClientSize.Height / preview.Image.Height);
            float w = preview.Image.Width * scale, h = preview.Image.Height * scale;
            float x = (preview.ClientSize.Width - w) / 2, y = (preview.ClientSize.Height - h) / 2;
            using (var pen = new Pen(Color.OrangeRed, 2)) foreach (var l in selectedField.Evidence.Where(l => l.Page == (int)page.Value))
                e.Graphics.DrawRectangle(pen, x + l.X * w, y + l.Y * h, l.Width * w, l.Height * h);
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { DocumentJobCoordinator.Shared.Changed -= JobChanged; if (preview.Image != null) preview.Image.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
