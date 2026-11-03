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
        private readonly ComboBox sourceMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ComboBox engineFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly ComboBox candidate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310 };
        private readonly CheckBox onlyReview = new CheckBox { Text = "Chỉ file cần kiểm tra", AutoSize = true };
        private readonly NumericUpDown rangeStart = new NumericUpDown { Minimum = 1, Maximum = 1, Width = 52 };
        private readonly NumericUpDown rangeEnd = new NumericUpDown { Minimum = 1, Maximum = 1, Width = 52 };
        private ExtractedField editBefore;
        private readonly Button start = new Button { Text = "Trích xuất", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Hủy", Enabled = false };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 48, AutoEllipsis = true };
        private readonly DataGridView documents = Grid();
        private readonly DataGridView fields = Grid();
        private readonly PictureBox preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.DimGray };
        private readonly NumericUpDown page = new NumericUpDown { Minimum = 1, Maximum = 1, Width = 60 };
        private readonly TextBox raw = new TextBox { Dock = DockStyle.Bottom, Height = 100, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 145, AutoScroll = true, Padding = new Padding(5) };
        private readonly List<AdministrativeDocument> results = new List<AdministrativeDocument>();
        private readonly List<VietOcrResult> ownedOcrResults = new List<VietOcrResult>();
        private CancellationTokenSource cancellation;
        private AdministrativeDocument selected;
        private ExtractedField selectedField;
        private List<EvidenceLine> displayedEvidence = new List<EvidenceLine>();
        private bool changing;
        private string checkpoint;
        public LlmExtractionOptions LlmOptions { get; set; } = LlmExtractionService.LoadSettingsOrDefault();
        public Func<LlmExtractionService> LlmServiceFactory { get; set; } = () => new LlmExtractionService();
        public void ConfigureLlm() {
            using (var dialog = new LlmSettingsForm(LlmOptions)) if (dialog.ShowDialog(this) == DialogResult.OK) { LlmOptions = dialog.Options; status.Text = "Đã lưu cấu hình LLM. Chạy lại LLM từ nội dung OCR đang có."; }
        }
        private static DataGridView Grid() { return new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false }; }
        private static void Column(DataGridView grid, string name, string label, int width, bool readOnly = true) {
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = name, HeaderText = label, Width = width, ReadOnly = readOnly });
        }
        public frmTrichXuatVanBan()
        {
            Text = "Trích xuất văn bản PDF"; Name = "frmTrichXuatVanBan"; ClientSize = new Size(1100, 720); Font = new Font("Segoe UI", 9);
            sourceMode.Items.AddRange(new object[] { "PDF / lớp chữ", "JSON sau OCR" }); sourceMode.SelectedIndex = 1;
            engineFilter.Items.AddRange(new object[] { "VietOCR", "Tất cả" }); engineFilter.SelectedIndex = 0;
            AddButton("Chọn PDF", () => { using (var dialog = new OpenFileDialog { Filter = "PDF|*.pdf" }) if (dialog.ShowDialog(this) == DialogResult.OK) { source.Text = dialog.FileName; sourceMode.SelectedIndex = 0; } });
            AddButton("Chọn JSON OCR", () => { using (var dialog = new OpenFileDialog { Filter = "JSON OCR|*.ocr.json|JSON|*.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) { source.Text = dialog.FileName; sourceMode.SelectedIndex = 1; } });
            AddButton("Nhập MD/TXT", ImportPlainText);
            AddButton("Chọn thư mục", () => { using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.SelectedPath; });
            toolbar.Controls.Add(source); toolbar.Controls.Add(sourceMode); toolbar.Controls.Add(engineFilter); toolbar.Controls.Add(recursive); toolbar.Controls.Add(force); toolbar.Controls.Add(start); toolbar.Controls.Add(cancel);
            AddButton("Mở JSON / tiếp tục", LoadResults); AddButton("Lưu JSON", () => Export(false)); AddButton("Xuất Excel", () => Export(true));
            AddButton("Xóa bảng kết quả", () => { results.Clear(); RefreshDocuments(); status.Text = "Đã xóa bảng kết quả. Các file đã lưu vẫn được giữ."; });
            toolbar.Controls.Add(onlyReview); onlyReview.CheckedChanged += (s, e) => RefreshDocuments();
            toolbar.Controls.Add(new Label { Text = "Văn bản chính: trang", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            toolbar.Controls.Add(rangeStart); toolbar.Controls.Add(rangeEnd); AddButton("Bóc tách lại phạm vi", ReextractRange);
            toolbar.Controls.Add(candidate); AddButton("Dùng ứng viên", UseCandidate); AddButton("Không áp dụng", MarkNotApplicable);
            AddButton("Số trang ghi tay", ReviewPageNumbers);
            AddButton("Cấu hình LLM", ConfigureLlm); AddButton("Chạy lại LLM", async () => await RunSelectedLlmAsync());
            candidate.SelectedIndexChanged += (s, e) => {
                if (changing || selected == null) return;
                var choice = candidate.SelectedItem as CandidateChoice; if (choice == null) return;
                displayedEvidence = choice.Candidate.Evidence;
                if (displayedEvidence.Count > 0) page.Value = Math.Max(1, Math.Min(page.Maximum, displayedEvidence[0].Page));
                RenderPreview();
            };
            sourceMode.SelectedIndexChanged += (s, e) => JobChanged(this, EventArgs.Empty);
            Column(documents, "Source", "File PDF", 250); Column(documents, "PageCount", "Trang", 50); Column(documents, "Status", "Trạng thái", 105);
            Column(fields, "Name", "Trường", 125); Column(fields, "Value", "Giá trị (có thể sửa)", 230, false);
            fields.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Confirmed", HeaderText = "Xác nhận", Width = 65 });
            Column(fields, "Warning", "Kiểm tra", 220);
            Column(fields, "OriginalValue", "LLM đề xuất ban đầu", 220); Column(fields, "Status", "Trạng thái trường", 160);
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
            fields.CellBeginEdit += (s, e) => {
                var f = fields.Rows[e.RowIndex].DataBoundItem as ExtractedField;
                if (f != null && (f.Name == "Số trang ghi tay" || f.Name == "Trang PDF")) { e.Cancel = true; status.Text = "Tổng trang lấy từ nguồn; sửa/xác nhận nhãn tại nút Số trang ghi tay."; return; }
                editBefore = f == null ? null : new ExtractedField { Value = f.Value, Confirmed = f.Confirmed, NotApplicable = f.NotApplicable };
            };
            fields.CellValueChanged += (s, e) => {
                if (changing || e.RowIndex < 0) return;
                var f = fields.Rows[e.RowIndex].DataBoundItem as ExtractedField;
                if (f != null && selected != null && editBefore != null) {
                    string value = f.Value; bool confirmed = e.ColumnIndex == 1 ? false : f.Confirmed;
                    f.Value = editBefore.Value; f.Confirmed = editBefore.Confirmed; f.NotApplicable = editBefore.NotApplicable;
                    ExtractionReview.Update(selected, f, value, confirmed, false); editBefore = null;
                    documents.Refresh(); fields.Refresh(); status.Text = "Đã cập nhật. Lưu JSON để giữ bản sửa; xuất lại Excel để cập nhật báo cáo.";
                }
            };
            fields.DataError += (s, e) => { status.Text = "Giá trị nhập không hợp lệ."; e.ThrowException = false; };
            page.ValueChanged += (s, e) => { if (!changing) RenderPreview(); };
            preview.Paint += PaintEvidence;
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            FormClosing += (s, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); status.Text = "Đang hủy; đóng lại sau khi xử lý dừng."; } };
            JobChanged(this, EventArgs.Empty);
        }
        private void UseCandidate() {
            if (selected == null || selectedField == null || candidate.SelectedItem == null) return;
            var choice = candidate.SelectedItem as CandidateChoice; if (choice == null) return;
            ExtractionReview.ChooseCandidate(selected, selectedField, choice.Candidate); fields.Refresh(); documents.Refresh(); SelectField();
            status.Text = "Đã chọn ứng viên; kiểm tra và xác nhận trường.";
        }
        private void MarkNotApplicable() {
            if (selected == null || selectedField == null) return;
            try { ExtractionReview.Update(selected, selectedField, null, true, true); fields.Refresh(); documents.Refresh(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        private sealed class CandidateChoice {
            public FieldCandidate Candidate; public override string ToString() { return (Candidate.Value ?? Candidate.RawValue ?? "[trống]") + " — " + Candidate.Reason; }
        }
        private async void ReextractRange() {
            if (selected == null || DocumentJobCoordinator.Shared.IsBusy) return;
            try { fields.EndEdit(); PostOcrExtractionService.Reextract(selected, (int)rangeStart.Value, (int)rangeEnd.Value); await RunSelectedLlmAsync(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        private void ReviewPageNumbers() {
            if (selected == null || DocumentJobCoordinator.Shared.IsBusy) return;
            try {
                var doc = selected; bool owned = doc.LinkedOcrResult == null;
                var result = PostOcrExtractionService.OpenNumberReview(doc); if (owned) ownedOcrResults.Add(result);
                using (var dialog = new Form { Text = "Số trang ghi tay — " + Path.GetFileName(doc.Source), ClientSize = new Size(1000, 620), StartPosition = FormStartPosition.CenterParent })
                using (var panel = new VietOcrPageNumberPanel(() => new VietOcrOptions { ReadPageNumbers = true }) { Dock = DockStyle.Fill }) {
                    dialog.Controls.Add(panel); panel.SetDocument(result);
                    panel.ResultChanged += (s, e) => { PostOcrExtractionService.SynchronizePageNumbers(doc); fields.Refresh(); documents.Refresh(); status.Text = "Đã cập nhật nhãn tại nguồn OCR. Lưu JSON trích xuất / xuất lại Excel."; };
                    dialog.ShowDialog(this);
                }
            } catch (Exception ex) { status.Text = ex.Message; }
        }
        private async void ImportPlainText() {
            string textPath, pdfPath;
            using (var dialog = new OpenFileDialog { Filter = "Nội dung OCR|*.md;*.txt", Title = "Chọn nội dung OCR" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return; textPath = dialog.FileName;
            }
            using (var dialog = new OpenFileDialog { Filter = "PDF nguồn|*.pdf", Title = "Chọn PDF tương ứng để đối chiếu" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return; pdfPath = dialog.FileName;
            }
            cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin("Nhập MD/TXT sau OCR", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            try { var doc = await Task.Run(() => PostOcrExtractionService.FromText(textPath, pdfPath, cancellation.Token)); PreparePrevious(doc); using (var llm = LlmServiceFactory()) await llm.ExtractAsync(doc, LlmOptions, cancellation.Token); Upsert(doc); RefreshDocuments(); status.Text = doc.LlmError ?? "Đã nạp và phân tích văn bản bằng LLM; nguồn không có tọa độ."; }
            catch (OperationCanceledException) { status.Text = "Đã hủy nhập văn bản."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); }
        }
        private void AddButton(string text, Action action) { var b = new Button { Text = text, AutoSize = true }; b.Click += (s, e) => action(); toolbar.Controls.Add(b); }
        private void JobChanged(object sender, EventArgs e) {
            bool busy = DocumentJobCoordinator.Shared.IsBusy;
            foreach (Control c in toolbar.Controls) c.Enabled = !busy;
            cancel.Enabled = cancellation != null; fields.ReadOnly = busy;
            force.Enabled = !busy && sourceMode.SelectedIndex == 0;
        }
        private async void StartExtraction(object sender, EventArgs e)
        {
            string input = source.Text; bool recurse = recursive.Checked, forced = force.Checked;
            using (var dialog = new SaveFileDialog { Filter = "Kết quả JSON|*.json", FileName = "trich-xuat-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return; checkpoint = dialog.FileName;
            }
            if (sourceMode.SelectedIndex == 1) await RunOcrImportAsync(input, recurse, checkpoint, engineFilter.Text == "Tất cả" ? null : engineFilter.Text);
            else await RunExtractionAsync(input, recurse, forced, checkpoint);
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
                foreach (string file in files) if (!results.Any(d => string.Equals(d.Source, file, StringComparison.OrdinalIgnoreCase)))
                    results.Add(new AdministrativeDocument { Source = file, RecordId = PostOcrExtractionService.RecordId(file), Pending = true });
                await Task.Run(() => ExtractionExportService.SaveJson(checkpoint, results));
                // Resume only after comparing the source content, never by filename alone.
                var service = new AdministrativeExtractionService { ForceOcr = forced };
                var progress = new Progress<ExtractionProgress>(p => status.Text = Path.GetFileName(p.File) + " — trang " + p.Page + "/" + p.Pages);
                foreach (string file in files) {
                    cancellation.Token.ThrowIfCancellationRequested();
                    string fingerprint = await Task.Run(() => Fingerprint(file));
                    var previous = fingerprint == null ? null : results.FirstOrDefault(d => d.Source == file && !d.Pending && d.Error == null && d.Fingerprint == fingerprint && d.UsedForceOcr == forced);
                    if (previous != null) { using (var llm = LlmServiceFactory()) await llm.ExtractAsync(previous, LlmOptions, cancellation.Token); RefreshDocuments(); continue; }
                    var result = await Task.Run(() => service.Process(file, cancellation.Token, progress));
                    result.Fingerprint = fingerprint; result.SourceVerified = fingerprint != null; result.UsedForceOcr = forced;
                    PreparePrevious(result);
                    using (var llm = LlmServiceFactory()) await llm.ExtractAsync(result, LlmOptions, cancellation.Token);
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
        public async Task RunOcrImportAsync(string input, bool recurse, string resultPath, string engine = null)
        {
            if (DocumentJobCoordinator.Shared.IsBusy) return;
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext)) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            checkpoint = resultPath; cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin("Bóc tách JSON OCR", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            int errors = 0, skipped = 0; var importErrors = new List<string>();
            try {
                fields.EndEdit();
                var files = await Task.Run(() => PostOcrExtractionService.DiscoverJson(input, recurse, cancellation.Token));
                if (files.Count == 0) { status.Text = "Không có file .ocr.json. Chọn thư mục kết quả OCR."; return; }
                var importedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var imported = new List<AdministrativeDocument>();
                foreach (string file in files) {
                    cancellation.Token.ThrowIfCancellationRequested(); status.Text = "Bóc tách " + Path.GetFileName(file);
                    AdministrativeDocument doc;
                    try { doc = await Task.Run(() => PostOcrExtractionService.FromJson(file, cancellation.Token)); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { errors++; importErrors.Add(file + ": " + ex.Message); continue; }
                    if (Directory.Exists(input) && engine != null && !string.Equals(doc.Engine, engine, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    if (!importedSources.Add(doc.Source)) throw new InvalidDataException("Có nhiều nguồn OCR cho " + doc.Source + ". Chọn engine cụ thể hoặc từng file JSON.");
                    imported.Add(doc);
                }
                foreach (var doc in imported) {
                    cancellation.Token.ThrowIfCancellationRequested();
                    PreparePrevious(doc);
                    using (var llm = LlmServiceFactory()) await llm.ExtractAsync(doc, LlmOptions, cancellation.Token);
                    Upsert(doc); RefreshDocuments();
                    if (!string.IsNullOrWhiteSpace(checkpoint)) await Task.Run(() => ExtractionExportService.SaveJson(checkpoint, results));
                }
                status.Text = "Đã nạp " + importedSources.Count + " PDF từ JSON OCR; " + errors + " JSON lỗi; " + skipped + " nguồn khác engine. Một dòng/PDF. " + (errors > 0 ? "Xem chi tiết lỗi ở ô nội dung." : "");
                if (importErrors.Count > 0) raw.AppendText(Environment.NewLine + string.Join(Environment.NewLine, importErrors));
            } catch (OperationCanceledException) { status.Text = "Đã hủy nhập JSON. Giữ các PDF đã bóc tách và checkpoint."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); }
        }
        private void PreparePrevious(AdministrativeDocument doc) {
            var previous = results.FirstOrDefault(d => string.Equals(d.Source, doc.Source, StringComparison.OrdinalIgnoreCase));
            if (previous != null && previous.PrimaryRangeConfirmed && previous.Fingerprint == doc.Fingerprint && previous.OcrFingerprint == doc.OcrFingerprint)
                PostOcrExtractionService.Reextract(doc, previous.PrimaryStartPage, previous.PrimaryEndPage);
            ExtractionReview.Preserve(previous, doc);
        }
        private void Upsert(AdministrativeDocument doc) {
            ExtractionReview.Preserve(results.FirstOrDefault(d => string.Equals(d.Source, doc.Source, StringComparison.OrdinalIgnoreCase)), doc);
            results.RemoveAll(d => string.Equals(d.Source, doc.Source, StringComparison.OrdinalIgnoreCase)); results.Add(doc);
        }
        public async Task RunSelectedLlmAsync() {
            if (selected == null || DocumentJobCoordinator.Shared.IsBusy) return;
            var doc = selected; fields.EndEdit(); cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin("Trích xuất bằng LLM", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            try {
                var progress = new Progress<string>(message => { if (!IsDisposed) status.Text = message; });
                var options = LlmOptions.Copy(); options.UseCache = false;
                using (var llm = LlmServiceFactory()) await llm.ExtractAsync(doc, options, cancellation.Token, progress);
                RefreshDocuments(); status.Text = doc.LlmError ?? "LLM hoàn tất. Rà soát và lưu JSON/xuất lại Excel.";
            } catch (OperationCanceledException) { status.Text = "Đã hủy LLM; giữ bản sửa trước."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); }
        }
        public async Task LoadOcrResultsAsync(IList<VietOcrResult> ocrResults)
        {
            if (DocumentJobCoordinator.Shared.IsBusy) return;
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext)) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin("Mở thông tin sau OCR", cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            try {
                results.RemoveAll(d => !ocrResults.Any(r => string.Equals(r.InputPath, d.Source, StringComparison.OrdinalIgnoreCase)));
                foreach (var result in ocrResults) {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var doc = result.Extraction ?? await Task.Run(() => PostOcrExtractionService.FromResult(result, cancellation.Token));
                    PostOcrExtractionService.SynchronizePageNumbers(doc);
                    if (!results.Contains(doc)) Upsert(doc);
                }
                RefreshDocuments(); status.Text = "Đã mở " + results.Count + " PDF từ phiên OCR. Rà soát trường, lưu JSON hoặc Xuất Excel.";
            } catch (OperationCanceledException) { status.Text = "Đã hủy mở thông tin OCR."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); }
        }
        private static string Fingerprint(string file) {
            try { using (var stream = File.OpenRead(file)) using (var hash = System.Security.Cryptography.SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        private void RefreshDocuments() {
            var old = selected; var shown = results.Where(d => !onlyReview.Checked || d.Status != "Đã xác nhận").ToList();
            documents.DataSource = new BindingList<AdministrativeDocument>(shown);
            if (old != null && shown.Contains(old)) { int i = shown.IndexOf(old); if (documents.Rows.Count > i) documents.CurrentCell = documents.Rows[i].Cells[0]; }
            else if (documents.Rows.Count > 0) documents.CurrentCell = documents.Rows[0].Cells[0];
            SelectDocument();
        }
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
                    var exported = onlyReview.Checked ? results.Where(d => d.Status != "Đã xác nhận").ToList() : results;
                    if (excel) ExtractionExportService.SaveExcel(dialog.FileName, exported); else ExtractionExportService.SaveJson(dialog.FileName, exported);
                    status.Text = "Đã lưu " + dialog.FileName;
                } catch (Exception ex) { status.Text = ex.Message; }
        }
        private void SelectDocument() {
            selected = documents.CurrentRow == null ? null : documents.CurrentRow.DataBoundItem as AdministrativeDocument;
            changing = true; fields.DataSource = selected == null ? null : new BindingList<ExtractedField>(selected.Fields);
            page.Maximum = selected == null ? 1 : Math.Max(1, selected.PageCount); page.Value = 1; changing = false;
            rangeStart.Maximum = rangeEnd.Maximum = page.Maximum;
            rangeStart.Value = selected == null ? 1 : Math.Max(1, Math.Min((int)page.Maximum, selected.PrimaryStartPage));
            rangeEnd.Value = selected == null ? 1 : Math.Max(1, Math.Min((int)page.Maximum, selected.PrimaryEndPage > 0 ? selected.PrimaryEndPage : selected.PageCount));
            selectedField = null; displayedEvidence.Clear(); candidate.Items.Clear(); RenderPreview();
            if (selected != null) status.Text = selected.Error ?? selected.LlmError ?? string.Join(" ", selected.Warnings);
        }
        private void SelectField() {
            if (changing) return;
            selectedField = fields.CurrentRow == null ? null : fields.CurrentRow.DataBoundItem as ExtractedField;
            displayedEvidence = selectedField == null ? new List<EvidenceLine>() : selectedField.Evidence;
            changing = true; candidate.Items.Clear();
            if (selectedField != null && selectedField.Name != "Số trang ghi tay") foreach (var c in selectedField.Candidates) candidate.Items.Add(new CandidateChoice { Candidate = c });
            changing = false;
            if (selectedField != null && selectedField.Evidence.Count > 0) page.Value = Math.Max(1, Math.Min(page.Maximum, selectedField.Evidence[0].Page));
            RenderPreview();
        }
        private void RenderPreview() {
            var old = preview.Image; preview.Image = null; if (old != null) old.Dispose(); raw.Clear();
            if (selected == null || selected.PageCount == 0) return;
            raw.Text = string.Join(Environment.NewLine, selected.Lines.Where(l => l.Page == (int)page.Value).Select(l => l.Text));
            if (displayedEvidence.Count > 0) raw.AppendText(Environment.NewLine + "[Bằng chứng] " + string.Join(" | ", displayedEvidence.Select(l => l.RawText ?? l.Text)));
            if (selected.Engine != null && !selected.SourceVerified) { raw.AppendText(Environment.NewLine + "PDF nguồn chưa được kiểm chứng; chỉ hiển thị bằng chứng OCR đã lưu."); return; }
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
            using (var pen = new Pen(Color.OrangeRed, 2)) foreach (var l in displayedEvidence.Where(l => l.Page == (int)page.Value && l.HasGeometry))
                e.Graphics.DrawRectangle(pen, x + l.X * w, y + l.Y * h, l.Width * w, l.Height * h);
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { DocumentJobCoordinator.Shared.Changed -= JobChanged; if (preview.Image != null) preview.Image.Dispose(); foreach (var result in ownedOcrResults) result.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
