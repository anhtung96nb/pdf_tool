using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using OfficeOpenXml;

namespace CountFile2.Services
{
    public static class ExtractionExportService
    {
        public static void SaveJson(string path, IList<AdministrativeDocument> documents)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                File.WriteAllText(temporary, serializer.Serialize(documents), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static List<AdministrativeDocument> LoadJson(string path)
        {
            var documents = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<AdministrativeDocument>>(File.ReadAllText(path));
            if (documents == null || documents.Any(d => d == null || d.Fields == null || d.Lines == null || d.Warnings == null))
                throw new InvalidDataException("Dữ liệu kết quả không hợp lệ.");
            return documents;
        }
        public static void SaveExcel(string path, IList<AdministrativeDocument> documents)
        {
            using (var package = new ExcelPackage()) {
                var sheet = package.Workbook.Worksheets.Add("Thông tin văn bản");
                var headers = new[] { "File nguồn", "Số trang", "Trạng thái", "Cảnh báo / lỗi" }.Concat(AdministrativeExtractionService.FieldNames).ToArray();
                for (int c = 0; c < headers.Length; c++) sheet.Cells[1, c + 1].Value = headers[c];
                for (int r = 0; r < documents.Count; r++) {
                    var d = documents[r]; sheet.Cells[r + 2, 1].Value = d.Source; sheet.Cells[r + 2, 2].Value = d.PageCount;
                    sheet.Cells[r + 2, 3].Value = d.Status; sheet.Cells[r + 2, 4].Value = d.Error ?? string.Join("\n", d.Warnings);
                    for (int c = 0; c < AdministrativeExtractionService.FieldNames.Length; c++) {
                        var f = d.Fields.FirstOrDefault(x => x.Name == AdministrativeExtractionService.FieldNames[c]);
                        sheet.Cells[r + 2, c + 5].Value = f == null ? null : f.Value;
                    }
                }
                var evidence = package.Workbook.Worksheets.Add("Bằng chứng");
                string[] eh = { "File", "Trường", "Giá trị", "Giá trị tự động", "Đã xác nhận", "Cảnh báo", "Trang PDF", "X", "Y", "Rộng", "Cao", "Chữ gốc", "Phương pháp", "Tin cậy OCR", "Xoay ảnh nguồn (độ)" };
                for (int c = 0; c < eh.Length; c++) evidence.Cells[1, c + 1].Value = eh[c];
                int row = 2;
                foreach (var d in documents) foreach (var f in d.Fields) {
                    foreach (var l in f.Evidence.Count == 0 ? new EvidenceLine[] { null } : f.Evidence.ToArray()) {
                        object[] values = { d.Source, f.Name, f.Value, f.OriginalValue, f.Confirmed, f.Warning,
                            l == null ? (object)null : l.Page, l == null ? (object)null : l.X, l == null ? (object)null : l.Y,
                            l == null ? (object)null : l.Width, l == null ? (object)null : l.Height,
                            l == null ? null : l.Text, l == null ? null : l.Method, l == null ? (object)null : l.Confidence, l == null ? (object)null : l.RotationDegrees };
                        for (int c = 0; c < values.Length; c++) evidence.Cells[row, c + 1].Value = values[c];
                        row++;
                    }
                }
                foreach (var ws in package.Workbook.Worksheets) {
                    ws.Row(1).Style.Font.Bold = true; ws.View.FreezePanes(2, 1);
                    ws.Cells[ws.Dimension.Address].AutoFilter = true;
                    ws.Cells[ws.Dimension.Address].Style.WrapText = true;
                    for (int c = 1; c <= ws.Dimension.End.Column; c++) ws.Column(c).Width = c == 1 ? 45 : 26;
                }
                package.SaveAs(new FileInfo(path));
            }
        }
    }
}
