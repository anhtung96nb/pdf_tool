using System;
using System.Drawing;
using System.Windows.Forms;
using CountFile2.Services;

namespace CountFile2
{
    public sealed class frmMain : Form
    {
        private readonly Panel content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly Label heading = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 8, 0, 0) };
        private readonly Label jobStatus = new Label { AutoSize = true, MaximumSize = new Size(190, 0) };
        private readonly Button stop = new Button { Text = "Hủy tác vụ đang chạy", AutoSize = true, Enabled = false };
        private readonly Timer closeTimer = new Timer { Interval = 100 };
        private bool closing;
        public bool IsShuttingDown { get { return closing; } }
        public FormNavigationController Navigation { get; private set; }
        public frmMain()
        {
            Text = "CountFile2 - Xử lý tài liệu"; Name = "frmMain";
            Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1280, 800); MinimumSize = new Size(1050, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Navigation = new FormNavigationController(content);
            var side = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 210,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10), AutoScroll = true };
            side.Controls.Add(new Label { Text = "CHỨC NĂNG", AutoSize = true, Margin = new Padding(3, 10, 3, 15) });
            foreach (string key in new[] { "Thống kê", "TIFF / PDF", "PDF hai lớp / PDF/A", "Giảm dung lượng PDF", "Làm sạch PDF", "OCR thông tin văn bản", "Trích xuất văn bản PDF" })
            {
                string captured = key;
                var button = new Button { Text = key, Width = 185, Height = 46 };
                button.Click += (s, e) => Navigate(captured); side.Controls.Add(button);
            }
            side.Controls.Add(jobStatus); side.Controls.Add(stop);
            stop.Click += (s, e) => DocumentJobCoordinator.Shared.Cancel();
            Controls.Add(content); Controls.Add(heading); Controls.Add(side);
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            JobChanged(this, EventArgs.Empty);
            FormClosing += OnClosing;
            closeTimer.Tick += (s, e) => { if (!DocumentJobCoordinator.Shared.IsBusy) { closeTimer.Stop(); Close(); } };
            Shown += (s, e) => Navigate("TIFF / PDF");
        }
        public void Navigate(string key)
        {
            if (closing) return;
            try
            {
                Navigation.Navigate(key, () => {
                    switch (key)
                    {
                        case "Thống kê":
                            var statistics = new Form1();
                            statistics.ConversionRequested += (s, e) => Navigate("TIFF / PDF");
                            return statistics;
                        case "TIFF / PDF": return new frmChuyenDoiFile();
                        case "PDF hai lớp / PDF/A": return new frmPdfProcessing();
                        case "Giảm dung lượng PDF": return new frmPdfCompression();
                        case "Làm sạch PDF": return new frmPdfCleanup();
                        case "OCR thông tin văn bản": return new frmXuLyOCR();
                        case "Trích xuất văn bản PDF": return new frmTrichXuatVanBan();
                        default: throw new ArgumentException("Màn hình không tồn tại.");
                    }
                });
                heading.Text = key;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không mở được chức năng", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void JobChanged(object sender, EventArgs e)
        {
            jobStatus.Text = DocumentJobCoordinator.Shared.IsBusy ? "Đang chạy: " + DocumentJobCoordinator.Shared.ActiveJob : "Sẵn sàng";
            stop.Enabled = DocumentJobCoordinator.Shared.IsBusy && !closing;
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!DocumentJobCoordinator.Shared.IsBusy) return;
            e.Cancel = true;
            if (closing) return;
            if (MessageBox.Show(this, "Dừng tác vụ và thoát sau khi dọn file tạm? Việc hủy có thể phải đợi trang hiện tại hoàn tất.",
                "Đang xử lý tài liệu", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            BeginShutdown();
        }
        private void BeginShutdown()
        {
            closing = true; DocumentJobCoordinator.Shared.Cancel();
            stop.Enabled = false; jobStatus.Text = "Đang hủy, vui lòng chờ…"; closeTimer.Start();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DocumentJobCoordinator.Shared.Changed -= JobChanged;
                closeTimer.Dispose(); Navigation.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
