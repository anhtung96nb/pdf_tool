using System.Threading;
using System.Threading.Tasks;
using CountFile2.Services;
using System;
using System.Drawing;
using System.Windows.Forms;
using Tesseract;
using PdfiumViewer;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using System.Runtime.InteropServices;
namespace CountFile2
{
    public partial class frmXuLyOCR : Form
    {
        private TesseractEngine ocrEngine;
        private List<Rectangle> selectedRegions = new List<Rectangle>(); // Danh sách các vùng được chọn
        private Point startPoint;
        private Rectangle currentRect;
        private bool isSelecting = false;
        private List<OcrRegion> ocrRegions = new List<OcrRegion>();
        private OcrRegion selectedRegion = null;
        private Point dragStartPoint;
        private bool isDragging = false;
        private bool isResizing = false;
        private ResizeDirection resizeDirection;
        private LinkedList<string> pathFiles = new LinkedList<string>();
        private LinkedListNode<string> currentNode;
        private int currentIndex = 1;
        private int totalFiles = 0;
        private string csvSuccess = "success.csv";
        private string csvError = "error.csv";
        private List<DocumentInfo> documentsInfo = new List<DocumentInfo>();
        private List<OCRError> ocrError = new List<OCRError>();
        private int batch = 1000;
        private ImageList imageListIcons = new ImageList();
        private Dictionary<string, int> iconCache = new Dictionary<string, int>();
        private string currentPath;
        private readonly string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
        private readonly string[] pdfExtensions = { ".pdf" };

        public frmXuLyOCR()
        {
            InitializeComponent();
            btnOCRAll.Text = "OCR thư mục → Excel";
            btnOCRAll.AutoSize = true;
            btnOCRAll.MinimumSize = new Size(165, 28);
            btOCR.Text = "OCR file";
            button1.Text = "Xuất Excel";
            label1.Text = "OCR THÔNG TIN VĂN BẢN";
            string langueTessa = Application.StartupPath + @"\tessdata";
            try { ocrEngine = new TesseractEngine(langueTessa, "vie", EngineMode.Default); }
            catch { Dispose(); throw; }
            DocumentJobCoordinator.Shared.Changed += JobChanged;
            Disposed += (s, e) => {
                DocumentJobCoordinator.Shared.Changed -= JobChanged;
                if (ocrEngine != null) ocrEngine.Dispose();
                if (pictureBox1.Image != null) pictureBox1.Image.Dispose();
                imageListIcons.Dispose();
            };
            JobChanged(this, EventArgs.Empty);
            InitializeFileListView();
            InitializeImageList();

            ocrRegions.Add(new OcrRegion { Bounds = new Rectangle(62, 72, 201, 43), RegionId = 1, RegionName = "CQBH" });
            ocrRegions.Add(new OcrRegion { Bounds = new Rectangle(341, 99, 238, 24), RegionId = 2, RegionName = "Ngày BH" });
            ocrRegions.Add(new OcrRegion { Bounds = new Rectangle(97, 115, 169, 29), RegionId = 3, RegionName = "Số VB" });
            ocrRegions.Add(new OcrRegion { Bounds = new Rectangle(147, 149, 410, 95), RegionId = 4, RegionName = "Trích Yếu" });
            pictureBox1.Invalidate();
            pictureBox1.Visible = false;
            txtOcrMsg.Visible = false;
            HiddenOrShowFormInfoOCR(false);
            LoadDrives();
        }
        private Bitmap ConvertPdfToImage(string pdfPath, int pageNumber)
        {
            using (var pdfDocument = PdfDocument.Load(pdfPath))
            {
                using (var image = pdfDocument.Render(pageNumber - 1, 800, 800, true))
                    return new Bitmap(image);
            }
        }



        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private Rectangle NormalizeRectangle(Rectangle rect)
        {
            int x = rect.Width < 0 ? rect.X + rect.Width : rect.X;
            int y = rect.Height < 0 ? rect.Y + rect.Height : rect.Y;
            int width = Math.Abs(rect.Width);
            int height = Math.Abs(rect.Height);
            return new Rectangle(x, y, width, height);
        }
        private void JobChanged(object sender, EventArgs e)
        { Enabled = !DocumentJobCoordinator.Shared.IsBusy; }
        private void Notify(string message)
        {
            var shell = TopLevelControl as frmMain;
            if (shell == null || !shell.IsShuttingDown) MessageBox.Show(TopLevelControl, message, "OCR thông tin văn bản");
        }

        private DocumentInfo Recognize(Bitmap image, string path, CancellationToken token)
        {
            var info = new DocumentInfo { DuongDanFile = path };
            foreach (var region in ocrRegions)
            {
                token.ThrowIfCancellationRequested();
                using (var cropped = CropRegion(image, region.Bounds))
                using (var pix = PixConverter.ToPix(cropped))
                using (var page = ocrEngine.Process(pix))
                {
                    string text = page.GetText().Trim();
                    switch (region.RegionId) {
                        case 1: info.CoQuanBanHanh = text; break;
                        case 2: info.NgayBanHanh = ExtractIssueDate(text); break;
                        case 3: info.SoVanBan = text; break;
                        case 4: info.TrichYeu = text; break;
                    }
                }
            }
            return info;
        }
        private async void btOCR_Click(object sender, EventArgs e)
        {
            string path = pictureBox1.Tag as string;
            if (pictureBox1.Image == null || string.IsNullOrEmpty(path)) {
                MessageBox.Show(this, "Chọn file ảnh hoặc PDF trước khi OCR."); return;
            }
            using (var cancellation = new CancellationTokenSource())
            {
                if (!DocumentJobCoordinator.Shared.TryBegin("OCR thông tin văn bản", cancellation)) return;
                try {
                    using (var image = new Bitmap(pictureBox1.Image)) {
                        var info = await Task.Run(() => Recognize(image, path, cancellation.Token), cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        documentsInfo.RemoveAll(d => d.DuongDanFile == path); documentsInfo.Add(info);
                    }
                    LoadFormInfo(path); txtOcrMsg.Text = "OCR thành công"; txtOcrMsg.Visible = true;
                }
                catch (OperationCanceledException) { txtOcrMsg.Text = "Đã hủy OCR"; txtOcrMsg.Visible = true; }
                catch (Exception ex) { Notify(ex.Message); }
                finally { DocumentJobCoordinator.Shared.End(cancellation); }
            }
        }
        private async void btPDF2Lop_Click(object sender, EventArgs e)
        {
            string folder = lbCurrentFolder.Text, output = txtFolderOut.Text;
            if (!Directory.Exists(folder) || !Directory.Exists(output)) {
                MessageBox.Show(this, "Chọn thư mục nguồn và thư mục xuất Excel hợp lệ."); return;
            }
            using (var cancellation = new CancellationTokenSource())
            {
                if (!DocumentJobCoordinator.Shared.TryBegin("OCR thư mục → Excel", cancellation)) return;
                documentsInfo.Clear(); ocrError.Clear();
                try {
                    await Task.Run(() => {
                        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                            .Where(f => pdfExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) || imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))) {
                            cancellation.Token.ThrowIfCancellationRequested();
                            try {
                                using (var image = Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                                    ? ConvertPdfToImage(file, 1) : new Bitmap(file))
                                    documentsInfo.Add(Recognize(image, file, cancellation.Token));
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { ocrError.Add(new OCRError { FileName = Path.GetFileName(file), DuongDanFile = file, ErrorMessage = ex.Message }); }
                        }
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (!ExportExcel(output)) throw new IOException("Không xuất được Excel. Kiểm tra template và quyền ghi.");
                    }, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    Notify(string.Format("OCR hoàn tất: {0} thành công, {1} lỗi. Excel: {2}", documentsInfo.Count, ocrError.Count, output));
                }
                catch (OperationCanceledException) { Notify("Đã hủy OCR; thông tin file đã nhận dạng vẫn được giữ."); }
                catch (Exception ex) { Notify(ex.Message); }
                finally { DocumentJobCoordinator.Shared.End(cancellation); }
            }
        }
        // Hàm cắt ảnh
        private Bitmap CropImage(Bitmap source, Rectangle section)
        {
            Bitmap croppedImage = new Bitmap(section.Width, section.Height);
            using (Graphics g = Graphics.FromImage(croppedImage))
            {
                g.DrawImage(source, 0, 0, section, GraphicsUnit.Pixel);
            }
            return croppedImage;
        }
        // Hàm căt  vùng
        private Bitmap CropRegion(Bitmap sourceImage, Rectangle region)
        {
            Bitmap croppedImage = new Bitmap(region.Width, region.Height);
            using (Graphics g = Graphics.FromImage(croppedImage))
            {
                g.DrawImage(sourceImage, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
            }
            return croppedImage;
        }


        private void ExportToCsv(List<string> ocrResults)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(basePath, csvSuccess);

            using (StreamWriter writer = new StreamWriter(filePath, true, new UTF8Encoding(true)))
            {
                for (int i = 0; i < ocrResults.Count; i++)
                {
                    // Escape dấu " nếu có
                    string cellValue = ocrResults[i].Replace("\"", "\"\"");
                    writer.Write($"\"{cellValue}\"");

                    if (i < ocrResults.Count - 1)
                        writer.Write(",");
                }
                writer.WriteLine(); // Kết thúc row
            }


        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Chọn thư mục để lưu file";
                folderDialog.ShowNewFolderButton = true; // Cho phép tạo thư mục mới

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = folderDialog.SelectedPath;
                    txtFolderOut.Text = selectedPath;
                }
            }
        }
        private string[] GetAllFilesInFolder(string folderPath)
        {
            string[] files = Directory.GetFiles(folderPath, "*.pdf");
            string[] pdfFiles = Directory.GetFiles(folderPath, "*.pdf");
            string[] jpgFiles = Directory.GetFiles(folderPath, "*.jpg");
            string[] pngFiles = Directory.GetFiles(folderPath, "*.png");
            string[] allFiles = pdfFiles.Concat(jpgFiles).Concat(pngFiles).ToArray();
            return allFiles;
        }
        private void txtFolderOut_TextChanged(object sender, EventArgs e)
        {

        }

        /// <summary>
        /// 
        /// </summary>

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Color.Blue, 2))
            using (Font font = new Font("Arial", 12))
            using (Brush brush = new SolidBrush(Color.Red))
            {
                foreach (var region in ocrRegions)
                {
                    e.Graphics.DrawRectangle(pen, region.Bounds);
                    e.Graphics.DrawString($"{region.RegionName}", font, brush, region.Bounds.Location);
                }
            }
        }
        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            foreach (var region in ocrRegions)
            {
                if (region.Bounds.Contains(e.Location))
                {
                    selectedRegion = region;
                    dragStartPoint = e.Location;

                    resizeDirection = GetResizeDirection(e.Location, region.Bounds);
                    isResizing = resizeDirection != ResizeDirection.None;
                    isDragging = !isResizing;

                    return;
                }
            }

            selectedRegion = null;
            pictureBox1.Invalidate();
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (selectedRegion != null)
            {
                if (isDragging)
                {
                    int offsetX = e.X - dragStartPoint.X;
                    int offsetY = e.Y - dragStartPoint.Y;

                    selectedRegion.Bounds = new Rectangle(
                        selectedRegion.Bounds.X + offsetX,
                        selectedRegion.Bounds.Y + offsetY,
                        selectedRegion.Bounds.Width,
                        selectedRegion.Bounds.Height
                    );

                    dragStartPoint = e.Location;
                    pictureBox1.Invalidate();
                }
                else if (isResizing)
                {
                    ResizeRegion(e.Location);
                    dragStartPoint = e.Location;
                    pictureBox1.Invalidate();
                }
            }
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
            isResizing = false;
        }
        private ResizeDirection GetResizeDirection(Point mousePoint, Rectangle rect)
        {
            int edgeSize = 8;

            if (IsNear(mousePoint, rect.Left, rect.Top)) return ResizeDirection.TopLeft;
            if (IsNear(mousePoint, rect.Right, rect.Top)) return ResizeDirection.TopRight;
            if (IsNear(mousePoint, rect.Left, rect.Bottom)) return ResizeDirection.BottomLeft;
            if (IsNear(mousePoint, rect.Right, rect.Bottom)) return ResizeDirection.BottomRight;

            if (Math.Abs(mousePoint.X - rect.Left) <= edgeSize) return ResizeDirection.Left;
            if (Math.Abs(mousePoint.X - rect.Right) <= edgeSize) return ResizeDirection.Right;
            if (Math.Abs(mousePoint.Y - rect.Top) <= edgeSize) return ResizeDirection.Top;
            if (Math.Abs(mousePoint.Y - rect.Bottom) <= edgeSize) return ResizeDirection.Bottom;

            return ResizeDirection.None;
        }

        private bool IsNear(Point p, int x, int y, int distance = 8)
        {
            return Math.Abs(p.X - x) <= distance && Math.Abs(p.Y - y) <= distance;
        }

        private void ResizeRegion(Point currentPoint)
        {
            var rect = selectedRegion.Bounds;

            switch (resizeDirection)
            {
                case ResizeDirection.Left:
                    rect.Width += rect.X - currentPoint.X;
                    rect.X = currentPoint.X;
                    break;
                case ResizeDirection.Right:
                    rect.Width = currentPoint.X - rect.X;
                    break;
                case ResizeDirection.Top:
                    rect.Height += rect.Y - currentPoint.Y;
                    rect.Y = currentPoint.Y;
                    break;
                case ResizeDirection.Bottom:
                    rect.Height = currentPoint.Y - rect.Y;
                    break;
                case ResizeDirection.TopLeft:
                    rect.Width += rect.X - currentPoint.X;
                    rect.Height += rect.Y - currentPoint.Y;
                    rect.X = currentPoint.X;
                    rect.Y = currentPoint.Y;
                    break;
                case ResizeDirection.TopRight:
                    rect.Width = currentPoint.X - rect.X;
                    rect.Height += rect.Y - currentPoint.Y;
                    rect.Y = currentPoint.Y;
                    break;
                case ResizeDirection.BottomLeft:
                    rect.Width += rect.X - currentPoint.X;
                    rect.Height = currentPoint.Y - rect.Y;
                    rect.X = currentPoint.X;
                    break;
                case ResizeDirection.BottomRight:
                    rect.Width = currentPoint.X - rect.X;
                    rect.Height = currentPoint.Y - rect.Y;
                    break;
            }

            if (rect.Width < 10) rect.Width = 10;
            if (rect.Height < 10) rect.Height = 10;

            selectedRegion.Bounds = rect;
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && selectedRegion != null)
            {
                ocrRegions.Remove(selectedRegion);
                selectedRegion = null;
                pictureBox1.Invalidate();
            }
        }

        private void btOpen_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Chọn thư mục nguồn";
                folderDialog.ShowNewFolderButton = true;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = folderDialog.SelectedPath;
                    txtFolderIn.Text = selectedPath;

                }
            }
        }
        private async void button1_Click(object sender, EventArgs e)
        {
            string output = txtFolderOut.Text;
            if (!Directory.Exists(output)) { Notify("Chọn thư mục xuất Excel hợp lệ."); return; }
            using (var cancellation = new CancellationTokenSource()) {
                if (!DocumentJobCoordinator.Shared.TryBegin("Xuất Excel OCR", cancellation)) return;
                try {
                    bool success = await Task.Run(() => {
                        cancellation.Token.ThrowIfCancellationRequested();
                        return ExportExcel(output);
                    }, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    Notify(success ? "Xuất Excel thành công." : "Không xuất được Excel. Kiểm tra template và quyền ghi.");
                }
                catch (OperationCanceledException) { Notify("Đã hủy tác vụ xuất Excel; file đã ghi xong được giữ lại."); }
                catch (Exception ex) { Notify(ex.Message); }
                finally { DocumentJobCoordinator.Shared.End(cancellation); }
            }
        }
        private void DisplayFile()
        {
            if (currentNode != null && !string.IsNullOrEmpty(currentNode.Value))
            {
                try
                {
                    string filePath = currentNode.Value;
                    Bitmap image = ConvertPdfToImage(filePath, 1);
                    var previousImage = pictureBox1.Image; pictureBox1.Image = image;
                    if (previousImage != null) previousImage.Dispose();
                    pictureBox1.Visible = true;
                    pictureBox1.Invalidate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi mở file: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("Không có file nào trong thư mục đã chọn.");
            }
        }
        private void DisplayFile(string filePath)
        {
            try
            {
                Bitmap image = ConvertPdfToImage(filePath, 1);
                pictureBox1.Tag = filePath;// Giải phóng ảnh cũ nếu có
                var previousImage = pictureBox1.Image; pictureBox1.Image = image;
                if (previousImage != null) previousImage.Dispose();
                pictureBox1.Visible = true;
                pictureBox1.Invalidate();
                LoadFormInfo(filePath);
                txtOcrMsg.Visible = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi mở file: {ex.Message}");
            }
        }
        private void pictureBox1_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {

        }

        private bool ExportExcel(string outputDirectory = null)
        {
            bool flag = true;
            try
            {
                string selectedOutput = outputDirectory ?? txtFolderOut.Text;
                if (string.IsNullOrEmpty(selectedOutput))
                {
                    MessageBox.Show("Vui lòng chọn thư mục xuất dữ liệu.");
                    return false;
                }
                var fileName = "\\Temp\\OCR_Template.xlsx";
                string PathIn = Application.StartupPath + fileName;
                string PathOut = selectedOutput + "\\OCR_Data.xlsx";
                FileInfo fi = new FileInfo(PathOut);
                //
                int count = 1;
                string fileNameOnly = Path.GetFileNameWithoutExtension(PathOut);
                string extension = Path.GetExtension(PathOut);
                string path = fi.DirectoryName;

                while (fi.Exists)
                {
                    string tempFileName = $"{fileNameOnly}({count}){extension}";
                    fi = new FileInfo(Path.Combine(path, tempFileName));
                    count++;
                }

                //
                FileInfo fi1 = new FileInfo(PathIn);
                using (ExcelPackage excel = new ExcelPackage(fi1))
                {
                var workSheet = excel.Workbook.Worksheets["Sheet1"];// ("");

                int row = 3;
                int i = 1;
                if (documentsInfo != null)
                {
                    foreach (DocumentInfo doc in documentsInfo)
                    {
                        workSheet.Row(row).Height = 22.00D;
                        workSheet.Cells[row, 1].Value = i++;
                        workSheet.Cells[row, 2].Value = doc.SoVanBan;
                        workSheet.Cells[row, 3].Value = doc.NgayBanHanh;
                        workSheet.Cells[row, 4].Value = doc.CoQuanBanHanh;
                        workSheet.Cells[row, 5].Value = doc.TrichYeu;
                        workSheet.Cells[row, 6].Value = doc.LoaiVanBan;
                        workSheet.Cells[row, 7].Value = doc.DuongDanFile;
                        row++;
                    }
                    excel.SaveAs(fi);
                }
                else
                {
                    flag = false;
                }
                }
            }
            catch
            {
                flag = false;
            }
            return flag;
        }

        public static string ExtractIssueDate(string ocrText)
        {
            if (string.IsNullOrEmpty(ocrText))
            {
                return "";
            }
            try
            {
                string pattern = @"ngày\s*([^\s]+)\s*tháng\s*([^\s]+)\s*năm\s*([^\s]+)";

                var match = Regex.Match(ocrText, pattern, RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    string rawDay = match.Groups[1].Value;
                    string rawMonth = match.Groups[2].Value;
                    string rawYear = match.Groups[3].Value;

                    // Làm sạch dữ liệu: giữ lại chỉ chữ số
                    string day = ExtractNumber(rawDay).PadLeft(2, '0');
                    string month = ExtractNumber(rawMonth).PadLeft(2, '0');
                    string year = ExtractNumber(rawYear);

                    if (string.IsNullOrEmpty(day) || string.IsNullOrEmpty(month) || string.IsNullOrEmpty(year))
                    {
                        return "Không thể trích xuất ngày ban hành hợp lệ";
                    }

                    return $"{day}-{month}-{year}";
                }
            }
            catch (Exception ex)
            {
                return "";
            }
            return "";
        }

        // Hàm làm sạch: giữ lại các ký tự số
        private static string ExtractNumber(string input)
        {
            return Regex.Replace(input, @"[^\d]", "");
        }

        private void btnNextFile_Click(object sender, EventArgs e)
        {
            currentNode = currentNode.Next;
            DisplayFile();
            currentIndex++;
            btOCR.Enabled = true;
            txtOcrMsg.Visible = false;
        }

        private void btnSkip_Click(object sender, EventArgs e)
        {
            if (currentNode == null || currentNode.Next == null)
            {
                MessageBox.Show("Đã xử lý tất cả các files trong thư mục.");
                return;
            }
            currentNode = currentNode.Next;
            DisplayFile();
            currentIndex++;
            btOCR.Enabled = true;
            txtOcrMsg.Visible = false;
        }

        private void InitializeFileListView()
        {
            listViewFiles.View = View.Details;
            listViewFiles.FullRowSelect = true;
            listViewFiles.GridLines = true;

            listViewFiles.Columns.Add("Name", 300);
            listViewFiles.Columns.Add("Size", 100);
            listViewFiles.Columns.Add("Modified", 150);

            listViewFiles.SmallImageList = imageListIcons;
            listViewFiles.ItemActivate += listViewFiles_ItemActivate;
        }

        private void InitializeImageList()
        {
            imageListIcons.ColorDepth = ColorDepth.Depth32Bit;
            imageListIcons.ImageSize = new Size(16, 16);
            imageListIcons.TransparentColor = Color.Transparent;
            Icon folderIcon = SystemIconHelper.GetFolderIcon();
            imageListIcons.Images.Add(folderIcon);
            iconCache["folder"] = 0;
        }
        private void LoadFiles(string path)
        {
            try
            {
                listViewFiles.Items.Clear();
                string[] files = Directory.GetFiles(path);

                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file).ToLower();
                    if (!IsImageFile(file) && !IsPdfFile(extension))
                    {
                        continue; // Chỉ hiển thị file ảnh và PDF
                    }
                    FileInfo fileInfo = new FileInfo(file);
                   
                    if (!iconCache.ContainsKey(extension))
                    {
                        Icon fileIcon = SystemIconHelper.GetFileIcon(file);
                        imageListIcons.Images.Add(fileIcon);
                        iconCache[extension] = imageListIcons.Images.Count - 1;
                    }

                    int imageIndex = iconCache[extension];

                    ListViewItem item = new ListViewItem(fileInfo.Name, imageIndex);
                    item.SubItems.Add(FormatFileSize(fileInfo.Length));
                    item.SubItems.Add(fileInfo.LastWriteTime.ToString());
                    item.Tag = file;
                    listViewFiles.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}");
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void listViewFiles_ItemActivate(object sender, EventArgs e)
        {
            if (listViewFiles.SelectedItems.Count > 0)
            {
                var selectedItem = listViewFiles.SelectedItems[0];
                string path = selectedItem.Tag.ToString();
                DisplayFile(path);
               
            }
        }

        private bool IsImageFile(string extension)
        {
            return Array.Exists(imageExtensions, e => e == extension);
        }

        private bool IsPdfFile(string extension)
        {
            return Array.Exists(pdfExtensions, e => e == extension);
        }
        private void listViewFilePdf_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
        private void LoadFormInfo(string path)
        {
            paneForm.Tag = path;
            var documentInfo = documentsInfo.FirstOrDefault(d => d.DuongDanFile == path);
            if (documentInfo != null)
            {
                txbSoVB.Text = documentInfo.SoVanBan ?? "";
                txbNgayBH.Text = documentInfo.NgayBanHanh ?? "";
                txbCoQuanBH.Text = documentInfo.CoQuanBanHanh ?? "";
                txbTrichYeu.Text = documentInfo.TrichYeu ?? "";
                txbLoaiVB.Text = documentInfo.LoaiVanBan ?? "";
                txbTrichYeu.Text = documentInfo.TrichYeu ?? "";
            }
            HiddenOrShowFormInfoOCR(true);
        }
        private void HiddenOrShowFormInfoOCR(bool isShow)
        {
           lbSoVB.Visible = isShow;
           txbSoVB.Visible = isShow;
           lbCoQuanBH.Visible = isShow;
           txbCoQuanBH.Visible = isShow;
           lbNgayBH.Visible = isShow;
           txbNgayBH.Visible = isShow;
           lbLoaiVB.Visible = isShow;
           txbLoaiVB.Visible = isShow;
           lbTrichYeu.Visible = isShow;
           txbTrichYeu.Visible = isShow;
           btnUpdateOCR.Visible = isShow;
        }
        private void txtFolderIn_TextChanged(object sender, EventArgs e)
        {
            lbCurrentFolder.Text = txtFolderIn.Text;
        }
        private void LoadDrives()
        {
            this.treeViewFolders.ImageList = imageListIcons;
            this.treeViewFolders.AfterSelect += TreeViewFolders_AfterSelect;
            treeViewFolders.Nodes.Clear();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    TreeNode rootNode = new TreeNode(drive.Name);
                    rootNode.Tag = drive.RootDirectory.FullName;
                    rootNode.Nodes.Add("Loading...");
                    rootNode.ImageKey = "folder";
                    rootNode.SelectedImageKey = "folder";
                    treeViewFolders.Nodes.Add(rootNode);
                }
            }
        }

        private void treeViewFolders_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes[0].Text == "Loading...")
            {
                e.Node.Nodes.Clear();
                try
                {
                    string path = e.Node.Tag.ToString();
                    string[] dirs = Directory.GetDirectories(path);
                    foreach (string dir in dirs)
                    {
                        DirectoryInfo di = new DirectoryInfo(dir);
                        TreeNode node = new TreeNode(di.Name);
                        node.Tag = di.FullName;
                        node.ImageKey = "folder"; // Hiển thị icon khi chưa chọn
                        node.SelectedImageKey = "folder"; // Icon khi được chọn
                        // Add dummy node if directory has subdirectories
                        if (Directory.GetDirectories(dir).Length > 0)
                        {
                            node.Nodes.Add("Loading...");
                        }

                        e.Node.Nodes.Add(node);
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }

        private void frmXuLyOCR_Load(object sender, EventArgs e)
        {
            treeViewFolders.BeforeExpand += treeViewFolders_BeforeExpand;
        }
        private void TreeViewFolders_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is string path && Directory.Exists(path))
            {
                documentsInfo.Clear();
                LoadFiles(path);
                lbCurrentFolder.Text = path;
            }
        }

        private void btnUpdateOCR_Click(object sender, EventArgs e)
        {
            var path = paneForm.Tag?.ToString();
            var docInfo = new DocumentInfo
            {
                DuongDanFile = path,
                SoVanBan = txbSoVB.Text.Trim(),
                NgayBanHanh = txbNgayBH.Text.Trim(),
                CoQuanBanHanh = txbCoQuanBH.Text.Trim(),
                TrichYeu = txbTrichYeu.Text.Trim(),
                LoaiVanBan = txbLoaiVB.Text.Trim()
            };
            var documentInfo = documentsInfo.FirstOrDefault(d => d.DuongDanFile == path);
            if (documentInfo != null)
            {
                documentsInfo.Remove(documentInfo);
            }
            documentsInfo.Add(docInfo);
            txtOcrMsg.Text = "Cập nhật thông tin thành công";
            txtOcrMsg.Visible = true;
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }
    }

    public class OcrRegion
    {
        public Rectangle Bounds { get; set; }
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
    }

    public enum ResizeDirection
    {
        None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight
    }
    public class DocumentInfo
    {
        public string SoVanBan { get; set; }
        public string NgayBanHanh { get; set; }
        public string CoQuanBanHanh { get; set; }
        public string TrichYeu { get; set; }
        public string LoaiVanBan { get; set; }
        public string DuongDanFile { get; set; }
    }
    public class OCRError
    {
        public string FileName { get; set; }
        public string DuongDanFile { get; set; }
        public string ErrorMessage { get; set; }

    }


    public static class SystemIconHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 220)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        };

        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_SMALLICON = 0x1;

        public static Icon GetFileIcon(string fileName)
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            IntPtr hImgSmall = SHGetFileInfo(fileName, 0, out shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON);

            return Icon.FromHandle(shinfo.hIcon);
        }
        public static Icon GetFolderIcon()
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            IntPtr hImgSmall = SHGetFileInfo(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                0,
                out shinfo,
                (uint)Marshal.SizeOf(shinfo),
                SHGFI_ICON | SHGFI_SMALLICON);

            return Icon.FromHandle(shinfo.hIcon);
        }
    }
}


