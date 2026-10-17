using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using System.Configuration;
using System.Diagnostics;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Threading;

namespace CountFile2
{
    public partial class Form1 : Form
    {
        public event EventHandler ConversionRequested;
        public Form1()
        {
            InitializeComponent();
            CountFile2.Services.DocumentJobCoordinator.Shared.Changed += JobChanged;
            Disposed += (s, e) => CountFile2.Services.DocumentJobCoordinator.Shared.Changed -= JobChanged;
            JobChanged(this, EventArgs.Empty);
            var conversionButton = new Button { Text = "Chuyển đổi TIFF ↔ PDF", AutoSize = true,
                Dock = DockStyle.Bottom, Height = 32 };
            conversionButton.Click += (sender, e) => {
                if (ConversionRequested != null) ConversionRequested(this, EventArgs.Empty);
                else using (var form = new frmChuyenDoiFile()) form.ShowDialog(this);
            };
            Controls.Add(conversionButton);
        }
        private List<string> lst_PathFile;
        private DataTable dtData;
        private string IHeight_A0 = ConfigurationSettings.AppSettings["IHeight_A0"];
        private string IHeight_A1 = ConfigurationSettings.AppSettings["IHeight_A1"];
        private string IHeight_A2 = ConfigurationSettings.AppSettings["IHeight_A2"];
        private string IHeight_A3 = ConfigurationSettings.AppSettings["IHeight_A3"];
        private string IHeight_A4 = ConfigurationSettings.AppSettings["IHeight_A4"];
        private string IHeight_A5 = ConfigurationSettings.AppSettings["IHeight_A5"];

        private string PHeight_A0 = ConfigurationSettings.AppSettings["PHeight_A0"];
        private string PHeight_A1 = ConfigurationSettings.AppSettings["PHeight_A1"];
        private string PHeight_A2 = ConfigurationSettings.AppSettings["PHeight_A2"];
        private string PHeight_A3 = ConfigurationSettings.AppSettings["PHeight_A3"];
        private string PHeight_A4 = ConfigurationSettings.AppSettings["PHeight_A4"];
        private string PHeight_A5 = ConfigurationSettings.AppSettings["PHeight_A5"];

        private string PWidth_A0 = ConfigurationSettings.AppSettings["PWidth_A0"];
        private string PWidth_A1 = ConfigurationSettings.AppSettings["PWidth_A1"];
        private string PWidth_A2 = ConfigurationSettings.AppSettings["PWidth_A2"];
        private string PWidth_A3 = ConfigurationSettings.AppSettings["PWidth_A3"];
        private string PWidth_A4 = ConfigurationSettings.AppSettings["PWidth_A4"];
        private string PWidth_A5 = ConfigurationSettings.AppSettings["PWidth_A5"];
        private void btOpen_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dlgFolder = new FolderBrowserDialog();
            dlgFolder.Description = "Chọn thư mục cần duyệt :";
            if (dlgFolder.ShowDialog() == DialogResult.OK)
            {
                this.txtFolder.Text = dlgFolder.SelectedPath;
            }
        }
        private bool statisticsStructure;
        private CancellationToken statisticsToken;
        private async void btThongKe_Click(object sender, EventArgs e)
        {
            string input = txtFolder.Text.Trim();
            if (!Directory.Exists(input)) { MessageBox.Show(this, "Chọn thư mục nguồn hợp lệ."); return; }
            using (var cancellation = new CancellationTokenSource())
            {
                if (!CountFile2.Services.DocumentJobCoordinator.Shared.TryBegin("Thống kê", cancellation)) return;
                statisticsStructure = chkIsStructure.Checked; statisticsToken = cancellation.Token;
                try {
                    await Task.Run(() => lstHoSo(input), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    Notify(dtData.Rows.Count > 0 ? "Đã đọc tất cả các file, có file lỗi cần kiểm tra lại!" : "Đã đọc tất cả các file, không có file lỗi");
                }
                catch (OperationCanceledException) { lblStatus.Text = "Đã hủy thống kê."; }
                catch (Exception ex) { Notify(ex.Message); }
                finally { CountFile2.Services.DocumentJobCoordinator.Shared.End(cancellation); }
            }
        }
        private void JobChanged(object sender, EventArgs e)
        { Enabled = !CountFile2.Services.DocumentJobCoordinator.Shared.IsBusy; }
        private void Notify(string message)
        {
            var shell = TopLevelControl as frmMain;
            if (shell == null || !shell.IsShuttingDown) MessageBox.Show(TopLevelControl, message, "Thống kê");
        }
        private void lstHoSo(string path)
        {
            if (Directory.Exists(path))
            {
                this.lst_PathFile = new List<string>();
                this.WalkDirectory(new DirectoryInfo(path));
                this.DataStatistic(this.lst_PathFile);
            }
            else
            {
                throw new DirectoryNotFoundException("Không tồn tại thư mục nguồn.");
            }
        }
        private void WalkDirectory(DirectoryInfo directory)
        {
            DirectoryInfo[] subDirectories = directory.GetDirectories();
            foreach (DirectoryInfo subDirectory in subDirectories)
            {
                statisticsToken.ThrowIfCancellationRequested();
                if (statisticsStructure)
                {
                    int i;
                    int j;
                    if (subDirectory.Name.ToLower().Contains("csdl_sohoa_daqh"))
                    {
                        DirectoryInfo[] subDir_LoaiHSQH = subDirectory.GetDirectories();
                        i = 0;
                        while (i < subDir_LoaiHSQH.Length)
                        {
                            DirectoryInfo[] subDir_CapHSQH = subDir_LoaiHSQH[i].GetDirectories();
                            j = 0;
                            while (j < subDir_CapHSQH.Length)
                            {
                                DirectoryInfo[] subDir_BoHSQH = subDir_CapHSQH[j].GetDirectories();
                                for (int k = 0; k < subDir_BoHSQH.Length; k++)
                                {
                                    this.lst_PathFile.Add(subDir_BoHSQH[k].FullName);
                                }
                                j++;
                            }
                            i++;
                        }
                    }
                    else if ((subDirectory.Name.ToLower().Contains("csdl_sohoa_tthc") || subDirectory.Name.ToLower().Contains("csdl_sohoa_hscv")) || subDirectory.Name.ToLower().Contains("csdl_sohoa_cvlu"))
                    {
                        DirectoryInfo[] subDir_CaptthcVacv = subDirectory.GetDirectories();
                        for (i = 0; i < subDir_CaptthcVacv.Length; i++)
                        {
                            DirectoryInfo[] subDir_BotthcVacv = subDir_CaptthcVacv[i].GetDirectories();
                            for (j = 0; j < subDir_BotthcVacv.Length; j++)
                            {
                                this.lst_PathFile.Add(subDir_BotthcVacv[j].FullName);
                            }
                        }
                    }
                }
                else
                {
                    this.lst_PathFile.Add(subDirectory.Parent.FullName);
                    break;
                }
            }
        }
        private void DataStatistic(List<string> directory_hoSo)
        {
            dtData = this.createDataTable();
            for (int i = 0; i < directory_hoSo.Count; i++)
            {
                if (Directory.Exists(directory_hoSo[i]))
                {
                    DirectoryInfo directory_HS = new DirectoryInfo(directory_hoSo[i]);
                    DirectoryInfo[] subDirectory_HS = directory_HS.GetDirectories();
                    string tenHoSo = directory_HS.Name;
                    string hoSoXemXetLai = "";
                    string capHoSo = "";
                    List<string> lst_FilePathsPDF = new List<string>();
                    if (statisticsStructure)
                    {
                        capHoSo = directory_HS.Parent.Name;
                        lst_FilePathsPDF = Directory.GetFiles(directory_hoSo[i], "*.*").ToList<string>();
                        for (int j = 0; j < subDirectory_HS.Count<DirectoryInfo>(); j++)
                        {
                            if (!subDirectory_HS[j].Name.ToLower().Contains("tulieuso"))
                            {
                                lst_FilePathsPDF.AddRange(Directory.GetFiles(subDirectory_HS[j].FullName, "*.*", SearchOption.AllDirectories).ToList<string>());
                            }
                            if (subDirectory_HS[j].Name.ToLower().Contains("xemxetlai"))
                            {
                                hoSoXemXetLai = (Directory.GetFiles(subDirectory_HS[j].FullName, "*.*").Length > 0) ? "X" : "";
                            }
                        }
                    }
                    else
                    {
                        List<string> temp_FilePathsPDF = Directory.GetFiles(directory_hoSo[i], "*.*", SearchOption.AllDirectories).ToList<string>();
                        for (int n = 0; n < temp_FilePathsPDF.Count; n++)
                        {
                            if (!temp_FilePathsPDF[n].ToLower().Contains("tulieuso"))
                            {
                                lst_FilePathsPDF.Add(temp_FilePathsPDF[n]);
                            }
                        }
                    }
                    this.countFormatPDF(lst_FilePathsPDF, capHoSo, tenHoSo, hoSoXemXetLai, dtData);
                }

            }
            statisticsToken.ThrowIfCancellationRequested();
            if (dtData.Rows.Count > 0) ExportExcel(dtData);
        }
        private void countFormatPDF(List<string> lst_FilePathsPDF, string capHoSo, string tenHoSo, string hoSoXemXetLai, DataTable dtData)
        {
            PdfReader reader = null;
            int countA0 = 0, fcountA0 = 0;
            int countA1 = 0, fcountA1 = 0;
            int countA2 = 0, fcountA2 = 0;
            int countA3 = 0, fcountA3 = 0;
            int countA4 = 0, fcountA4 = 0;
            int CountLoi = 0, fCountLoi = 0;
            int countKhac = 0, fcountKhac = 0;
            int countResuft = 0, fcountResuft = 0;
            int countError = 0, fcountError = 0;
            string strTrangLoi = "";
            try
            {
                var fileName = "\\ThongKeSoLuong.xlsx";
                string PathIn = Application.StartupPath + fileName;
                string PathOut = Application.StartupPath + "\\Temp" + fileName;
                FileInfo fi = new FileInfo(PathOut);
                FileInfo fi1 = new FileInfo(PathIn);
                using (ExcelPackage excel = new ExcelPackage(fi1))
                {
                var workSheet = excel.Workbook.Worksheets["Sheet1"];// ("");
                int intRows=0;
                foreach (string file in lst_FilePathsPDF)
                {
                    statisticsToken.ThrowIfCancellationRequested();
                    Invoke(new Action(() => lblStatus.Text = file));
                    try
                    {

                        FileInfo info_File = new FileInfo(file);
                        fcountA0 = 0;
                        fcountA1 = 0;
                        fcountA2 = 0;
                        fcountA3 = 0;
                        fcountA4 = 0;
                        fcountKhac = 0;
                        fcountResuft = 0;
                        fcountError = 0;
                        fCountLoi = 0;
                        strTrangLoi = "";
                        switch (info_File.Extension.ToUpper())
                        {
                            case ".PDF":
                                try
                                {
                                    reader = new PdfReader(file);
                                }
                                catch (Exception ex)
                                {
                                    countError += 1;
                                    fcountError += 1;
                                    dtData.Rows.Add(new object[] { countError, file, ex.Message });
                                    continue;
                                }
                               
                                for (int page = 1; page <= reader.NumberOfPages; page++)
                                {
                                    statisticsToken.ThrowIfCancellationRequested();
                                    iTextSharp.text.Rectangle mediabox = reader.GetPageSize(page);
                                    float value_Compare = Math.Max(mediabox.Height, mediabox.Width); 
                                    if ((value_Compare > Convert.ToInt64(this.PHeight_A0)) || (mediabox.Height >= this.SubRange((float)Convert.ToInt64(this.PHeight_A0), (float)Convert.ToInt64(this.PHeight_A1))))
                                    {
                                        countA0++;
                                        fcountA0++;
                                        if (mediabox.Width> Convert.ToInt64(this.PWidth_A0) * 1.3)
                                        {
                                            CountLoi++;
                                            fCountLoi++;
                                            if (strTrangLoi=="")
                                                strTrangLoi ="Trang "+ page.ToString(); 
                                            else
                                                strTrangLoi +=", " + page.ToString();
                                        }    
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.PHeight_A1), (float)Convert.ToInt64(this.PHeight_A2)) <= value_Compare) && (value_Compare < this.AddRange((float)Convert.ToInt64(this.PHeight_A0), (float)Convert.ToInt64(this.PHeight_A1))))
                                    {
                                       
                                        countA1++;
                                        fcountA1++;
                                        if (mediabox.Width > Convert.ToInt64(this.PWidth_A1) * 1.3)
                                        {
                                            CountLoi++;
                                            fCountLoi++;
                                            if (strTrangLoi == "")
                                                strTrangLoi = "Trang " + page.ToString();
                                            else
                                                strTrangLoi += ", " + page.ToString();
                                        }
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.PHeight_A2), (float)Convert.ToInt64(this.PHeight_A3)) <= value_Compare) && (value_Compare < this.AddRange((float)Convert.ToInt64(this.PHeight_A1), (float)Convert.ToInt64(this.PHeight_A2))))
                                    {
                                        countA2++;
                                        fcountA2++;
                                        if (mediabox.Width > Convert.ToInt64(this.PWidth_A2) * 1.3)
                                        {
                                            CountLoi++;
                                            fCountLoi++;
                                            if (strTrangLoi == "")
                                                strTrangLoi = "Trang " + page.ToString();
                                            else
                                                strTrangLoi += ", " + page.ToString();
                                        }
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.PHeight_A3), (float)Convert.ToInt64(this.PHeight_A4)) <= value_Compare) && (value_Compare < this.AddRange((float)Convert.ToInt64(this.PHeight_A2), (float)Convert.ToInt64(this.PHeight_A3))))
                                    {
                                        countA3++;
                                        fcountA3++;
                                        if (mediabox.Width > Convert.ToInt64(this.PWidth_A3) * 1.3)
                                        {
                                            CountLoi++;
                                            fCountLoi++;
                                            if (strTrangLoi == "")
                                                strTrangLoi = "Trang " + page.ToString();
                                            else
                                                strTrangLoi += ", " + page.ToString();
                                        }
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.PHeight_A4), (float)Convert.ToInt64(this.PHeight_A5)) <= value_Compare) && (value_Compare < this.AddRange((float)Convert.ToInt64(this.PHeight_A3), (float)Convert.ToInt64(this.PHeight_A4))))
                                    {
                                        var ffd = this.SubRange((float)Convert.ToInt64(this.PHeight_A1), (float)Convert.ToInt64(this.PHeight_A2));
                                        countA4++;
                                        fcountA4++;
                                        if (mediabox.Width > Convert.ToInt64(this.PWidth_A4)*1.3)
                                        {
                                            CountLoi++;
                                            fCountLoi++;
                                            if (strTrangLoi == "")
                                                strTrangLoi = "Trang " + page.ToString();
                                            else
                                                strTrangLoi += ", " + page.ToString();
                                        }
                                    }
                                    else
                                    {
                                        countKhac++;
                                        fcountKhac++;
                                        
                                    }
                                }
                                break;

                            case ".JPG":
                            case ".PNG":
                            case ".JPEG":
                                {
                                    iTextSharp.text.Image image = null;
                                    try
                                    {
                                        image = iTextSharp.text.Image.GetInstance(file);
                                    }
                                    catch
                                    {
                                        countKhac++;
                                        fcountKhac++;
                                        continue;
                                    }
                                    float value_ImageCompare = Math.Max(image.Height, image.Width);
                                    if ((value_ImageCompare > Convert.ToInt64(this.IHeight_A0)) || (value_ImageCompare >= this.SubRange((float)Convert.ToInt64(this.IHeight_A0), (float)Convert.ToInt64(this.IHeight_A1))))
                                    {
                                        countA0++;
                                        fcountA0++;
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.IHeight_A1), (float)Convert.ToInt64(this.IHeight_A2)) <= value_ImageCompare) && (value_ImageCompare < this.AddRange((float)Convert.ToInt64(this.IHeight_A0), (float)Convert.ToInt64(this.IHeight_A1))))
                                    {
                                        countA1++;
                                        fcountA1++;
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.IHeight_A2), (float)Convert.ToInt64(this.IHeight_A3)) <= value_ImageCompare) && (value_ImageCompare < this.AddRange((float)Convert.ToInt64(this.IHeight_A1), (float)Convert.ToInt64(this.IHeight_A2))))
                                    {
                                        countA2++;
                                        fcountA2++;
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.IHeight_A3), (float)Convert.ToInt64(this.IHeight_A4)) <= value_ImageCompare) && (value_ImageCompare < this.AddRange((float)Convert.ToInt64(this.IHeight_A2), (float)Convert.ToInt64(this.IHeight_A3))))
                                    {
                                        countA3++;
                                        fcountA3++;
                                    }
                                    else if ((this.SubRange((float)Convert.ToInt64(this.IHeight_A4), (float)Convert.ToInt64(this.IHeight_A5)) <= value_ImageCompare) && (value_ImageCompare < this.AddRange((float)Convert.ToInt64(this.IHeight_A3), (float)Convert.ToInt64(this.IHeight_A4))))
                                    {
                                        countA4++;
                                        fcountA4++;
                                    }
                                    else
                                    {
                                        countKhac++;
                                        fcountKhac++;
                                    }
                                    break;
                                }
                               
                        }

                        countResuft += 1;
                        fcountResuft += 1;
                        intRows = countResuft + 3;
                        workSheet.Row(intRows).Height = 22.00D;
                        workSheet.Cells[intRows, 1].Value = countResuft.ToString();
                        workSheet.Cells[intRows, 2].Value = file;
                        workSheet.Cells[intRows, 3].Value = fcountA0;
                        workSheet.Cells[intRows, 4].Value = fcountA1;
                        workSheet.Cells[intRows, 5].Value = fcountA2;
                        workSheet.Cells[intRows, 6].Value = fcountA3;
                        workSheet.Cells[intRows, 7].Value = fcountA4;
                        workSheet.Cells[intRows, 8].Value = fcountKhac;
                        workSheet.Cells[intRows, 11].Value = fCountLoi;
                        workSheet.Cells[intRows, 12].Value = strTrangLoi;
                        //workSheet.Cells[intRows, 10].Style.Border.Top.Style = workSheet.Row(intRows).Style.Border.Left.Style = workSheet.Row(intRows).Style.Border.Right.Style = workSheet.Row(intRows).Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {

                        // Xử lý thêm thông tin file bị lỗi gồm stt(countError), đường dẫ(file), thông tin lỗi (ex)
                        countError += 1;
                        dtData.Rows.Add(new object[] { countError, file, ex.Message });
                        workSheet.Cells[intRows, 2].Value = file;
                        workSheet.Cells[intRows, 13].Value = ex.Message;
                        continue;
                    }
                    finally { if (reader != null) { reader.Close(); reader = null; } }

                }
                excel.SaveAs(fi);

                }
                Invoke(new Action(() => {
                txtA0.Text = countA0.ToString("N0");
                txtA1.Text = countA1.ToString("N0");
                txtA2.Text = countA2.ToString("N0");
                txtA3.Text = countA3.ToString("N0");
                txtA4.Text = countA4.ToString("N0");
                txtKhac.Text = countKhac.ToString("N0");
                txtThanhCong.Text = countResuft.ToString("N0");
                txtLoi.Text = countError.ToString("N0");
                }));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                dtData.Rows.Add(new object[] { "" });
            }
        }
      
        // Hơn quá nửa mới tính khổ giấy lơn hơn
        private float SubRange(float val_Max, float val_Min)
        {
            try
            {
                return (val_Max - ((val_Max - val_Min) / 2f));
            }
            catch
            {
                return 0;
            }

        }
        private float AddRange(float val_Max, float val_Min)
        {
            try
            {
                return (val_Min + ((val_Max - val_Min) / 2f));
            }
            catch
            {
                return 0;
            }
           
        }
        private DataTable createDataTable()
        {
            DataTable dt = new DataTable();
            dt.Columns.AddRange(new DataColumn[] { new DataColumn("STT", typeof(string)), new DataColumn("DuongDan", typeof(string)), new DataColumn("MaLoi", typeof(string)) });
            return dt;
        }

        private void btExportExcel_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "Excel (2003)(.xls)|*.xls|Excel (2007-2010) (.xlsx)|*.xlsx";
                if (saveDialog.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                string exportFilePath = saveDialog.FileName;
                string strFileExt = new FileInfo(exportFilePath).Extension;
                if (strFileExt != null)
                {
                    if ((strFileExt != ".xls") && (strFileExt != ".xlsx"))
                    {
                        MessageBox.Show("Không đúng định dạng file exel", "Thông báo");
                        return;
                    }
                }
                string PathIn = Application.StartupPath + "\\Temp\\ThongKeLoi.xlsx";
                //FileInfo fi = new FileInfo(exportFilePath);
                FileInfo fi1 = new FileInfo(PathIn);
                fi1.MoveTo(exportFilePath);
                Process.Start(exportFilePath);  // mở file
            }
        }

        private bool ExportExcel(DataTable tb)
        {
            bool flag = true;
            try
            {
                var fileName = "\\ThongKeLoi.xlsx";
                string PathIn = Application.StartupPath + fileName;
                string PathOut=Application.StartupPath+ "\\Temp" + fileName;
                FileInfo fi = new FileInfo(PathOut);
                FileInfo fi1 = new FileInfo(PathIn);
                using (ExcelPackage excel = new ExcelPackage(fi1))
                {
                var workSheet = excel.Workbook.Worksheets["Sheet1"];// ("");

                int recordIndex = 3;
                if (tb != null) 
                {
                    foreach (DataRow item in tb.Rows)
                    {
                        workSheet.Row(recordIndex).Height = 22.00D;
                        workSheet.Cells[recordIndex, 1].Value = item[0].ToString();
                        workSheet.Cells[recordIndex, 2].Value = item[1].ToString();
                        workSheet.Cells[recordIndex, 3].Value = item[2].ToString();
                        workSheet.Cells[recordIndex, 3].Style.Border.Top.Style = workSheet.Row(recordIndex).Style.Border.Left.Style = workSheet.Row(recordIndex).Style.Border.Right.Style = workSheet.Row(recordIndex).Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                        recordIndex++;
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

        private void Form1_Load(object sender, EventArgs e)
        {
           
        }

        private void button1_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "Excel (2003)(.xls)|*.xls|Excel (2007-2010) (.xlsx)|*.xlsx";
                if (saveDialog.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                string exportFilePath = saveDialog.FileName;
                string strFileExt = new FileInfo(exportFilePath).Extension;
                if (strFileExt != null)
                {
                    if ((strFileExt != ".xls") && (strFileExt != ".xlsx"))
                    {
                        MessageBox.Show("Không đúng định dạng file exel", "Thông báo");
                        return;
                    }
                }
                string PathIn = Application.StartupPath + "\\Temp\\ThongKeSoLuong.xlsx";
                //FileInfo fi = new FileInfo(exportFilePath);
                FileInfo fi1 = new FileInfo(PathIn);
                fi1.MoveTo(exportFilePath);
                Process.Start(exportFilePath);  // mở file
            }
        }
    }
}
