using System;
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
    internal sealed class VietOcrPageNumberPanel : UserControl
    {
        private readonly Func<VietOcrOptions> options;
        private readonly DataGridView pages = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        private readonly PictureBox image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SystemColors.ControlDark };
        private readonly TextBox label = new TextBox { Width = 90, AccessibleName = "Số trang ghi tay (giữ số 0 đầu)" };
        private readonly CheckBox absent = new CheckBox { Text = "Không có số", AutoSize = true };
        private readonly CheckBox filter = new CheckBox { Text = "Chỉ trang cần kiểm tra", AutoSize = true };
        private readonly Button confirm = new Button { Text = "Lưu / xác nhận", AutoSize = true };
        private readonly Button reread = new Button { Text = "Đọc lại số trang", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Hủy", AutoSize = true, Enabled = false };
        private readonly Label hint = new Label { Dock = DockStyle.Bottom, Height = 48, AutoEllipsis = true, Padding = new Padding(4) };
        private VietOcrResult document;
        private CancellationTokenSource cancellation;
        private int generation;
        public event EventHandler ResultChanged;

        public VietOcrPageNumberPanel(Func<VietOcrOptions> getOptions)
        {
            options = getOptions; Dock = DockStyle.Fill;
            foreach (var c in new[] { new[] { "PdfPage", "Trang PDF", "75" }, new[] { "Value", "Số ghi tay", "85" },
                new[] { "StateLabel", "Trạng thái", "115" }, new[] { "Score", "Điểm", "60" } })
                pages.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = c[0], HeaderText = c[1], Width = int.Parse(c[2]) });
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            tools.Controls.AddRange(new Control[] { new Label { Text = "Nhãn số:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, label, absent, confirm, reread, cancel, filter });
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 230), SplitterDistance = 360 };
            split.Panel1.Controls.Add(pages); split.Panel2.Controls.Add(image);
            Controls.Add(split); Controls.Add(hint); Controls.Add(tools);
            pages.SelectionChanged += async (s, e) => await ShowSelectedAsync();
            filter.CheckedChanged += (s, e) => RefreshRows();
            absent.CheckedChanged += (s, e) => label.Enabled = !absent.Checked && !DocumentJobCoordinator.Shared.IsBusy;
            confirm.Click += async (s, e) => await ConfirmAsync();
            reread.Click += async (s, e) => await RereadAsync();
            cancel.Click += (s, e) => { if (cancellation != null) cancellation.Cancel(); };
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            hint.Text = "Bật Đọc số trang viết tay trước khi OCR. Chọn trang để đối chiếu vùng số; nhãn ghi tay tách biệt với trang PDF.";
            SetEnabled();
        }
        private VietOcrPageNumber Selected { get { return pages.CurrentRow == null ? null : pages.CurrentRow.DataBoundItem as VietOcrPageNumber; } }
        public void SetDocument(VietOcrResult result)
        {
            if (ReferenceEquals(document, result)) return;
            document = result; generation++; SetImage(null); RefreshRows();
            if (result == null || result.PageNumbers.Count == 0) hint.Text = "Chưa có dữ liệu số trang. Bật tùy chọn Đọc số trang viết tay và OCR tài liệu.";
        }
        private void RefreshRows()
        {
            int selected = Selected == null ? 0 : Selected.PdfPage;
            var rows = document == null ? new VietOcrPageNumber[0] : document.PageNumbers.Where(p => !filter.Checked || p.NeedsReview).ToArray();
            pages.DataSource = new BindingList<VietOcrPageNumber>(rows);
            foreach (DataGridViewRow row in pages.Rows) if (((VietOcrPageNumber)row.DataBoundItem).PdfPage == selected) pages.CurrentCell = row.Cells[0];
            SetEnabled();
        }
        private void SetImage(Image value) { var previous = image.Image; image.Image = value; if (previous != null) previous.Dispose(); }
        private void SetEnabled()
        {
            bool idle = !DocumentJobCoordinator.Shared.IsBusy;
            pages.Enabled = filter.Enabled = idle; confirm.Enabled = reread.Enabled = absent.Enabled = idle && Selected != null;
            label.Enabled = idle && Selected != null && !absent.Checked; cancel.Enabled = cancellation != null;
        }
        private void JobChanged(object sender, EventArgs e) { SetEnabled(); }
        public Task RefreshEvidenceAsync() { return ShowSelectedAsync(); }
        private void EnsureUiContext()
        {
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext) && !InvokeRequired)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }
        private async Task ShowSelectedAsync()
        {
            if (IsDisposed) return; EnsureUiContext();
            var row = Selected; int version = ++generation;
            if (row == null) { label.Clear(); absent.Checked = false; SetImage(null); return; }
            label.Text = row.Value; absent.Checked = row.Confirmed && row.State == "not_found";
            hint.Text = "PDF " + row.PdfPage + " • " + row.StateLabel + " • Máy đọc: " + row.RawText + ". " + row.Warning;
            if (DocumentJobCoordinator.Shared.IsBusy || document == null) return;
            string path = document.InputPath; var configuration = options();
            try
            {
                VietOcrPageNumberReview.ValidateSource(document);
                var bitmap = await Task.Run(() => VietOcrPageNumberReview.RenderRegion(path, row.PdfPage, configuration, row));
                if (IsDisposed || generation != version) bitmap.Dispose(); else SetImage(bitmap);
            }
            catch (Exception ex) { if (!IsDisposed && generation == version) { SetImage(null); hint.Text = "Không xem được vùng số: " + ex.Message; } }
            if (!IsDisposed) SetEnabled();
        }
        public async Task PreviewAsync(string path, VietOcrOptions configuration)
        {
            if (IsDisposed) return; EnsureUiContext();
            if (DocumentJobCoordinator.Shared.IsBusy) return;
            if (!File.Exists(path)) { hint.Text = "Chọn một PDF hoặc một file trong bảng kết quả để xem vùng số."; return; }
            int page = document != null && string.Equals(document.InputPath, path, StringComparison.OrdinalIgnoreCase) && Selected != null ? Selected.PdfPage : 1;
            int version = ++generation;
            try
            {
                var bitmap = await Task.Run(() => VietOcrPageNumberReview.RenderRegion(path, page, configuration));
                if (IsDisposed || version != generation) bitmap.Dispose(); else { SetImage(bitmap); hint.Text = "Vùng sẽ đọc: " + configuration.NumberRightPercent + "% bên phải × " + configuration.NumberTopPercent + "% phía trên, trang PDF " + page + "."; }
            }
            catch (Exception ex) { if (!IsDisposed) hint.Text = ex.Message; }
        }
        private async Task ConfirmAsync()
        {
            var row = Selected; var target = document; if (row == null || target == null) return;
            string value = label.Text; bool noNumber = absent.Checked;
            await RunAsync("Lưu số trang", async token => await VietOcrPageNumberReview.ConfirmAsync(target, row.PdfPage, value, noNumber, token));
        }
        private async Task RereadAsync()
        {
            var row = Selected; var target = document; if (row == null || target == null) return;
            var configuration = options();
            await RunAsync("Đọc lại số trang", async token => {
                var metadata = await VietOcrPageNumberReview.RereadAsync(target, row.PdfPage, configuration, token);
                await VietOcrPageNumberReview.ReplaceAsync(target, row.PdfPage, metadata, token);
            });
        }
        private async Task RunAsync(string name, Func<CancellationToken, Task> operation)
        {
            EnsureUiContext();
            if (DocumentJobCoordinator.Shared.IsBusy) return;
            cancellation = new CancellationTokenSource();
            if (!DocumentJobCoordinator.Shared.TryBegin(name, cancellation)) { cancellation.Dispose(); cancellation = null; return; }
            string message = null;
            try
            {
                hint.Text = name + "…"; await operation(cancellation.Token);
                RefreshRows(); if (ResultChanged != null) ResultChanged(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) { message = "Đã hủy; giữ kết quả cũ."; }
            catch (Exception ex) { message = "Lỗi: " + ex.Message; }
            finally { var done = cancellation; cancellation = null; DocumentJobCoordinator.Shared.End(done); done.Dispose(); SetEnabled(); await ShowSelectedAsync(); if (message != null) hint.Text = message; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { generation++; DocumentJobCoordinator.Shared.Changed -= JobChanged; if (cancellation != null) cancellation.Cancel(); SetImage(null); }
            base.Dispose(disposing);
        }
    }
}
