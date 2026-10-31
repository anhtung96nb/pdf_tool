using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace CountFile2.Services
{
    public static class PostOcrReportExport
    {
        public static List<AdministrativeDocument> Snapshot(IList<AdministrativeDocument> documents)
        {
            foreach (var d in documents) { PostOcrExtractionService.SynchronizePageNumbers(d); PostOcrExtractionService.ValidateCurrentSource(d); }
            var json = PostOcrExtractionService.Serializer();
            var copy = json.Deserialize<List<AdministrativeDocument>>(json.Serialize(documents));
            if (copy == null || copy.Any(d => d == null || string.IsNullOrWhiteSpace(d.Source) || d.Fields == null || d.Lines == null)) throw new InvalidDataException("Dữ liệu kết quả không hợp lệ.");
            if (copy.GroupBy(d => Path.GetFullPath(d.Source), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Có nhiều bản ghi cho cùng PDF. Chọn một nguồn OCR trước khi xuất.");
            return copy;
        }
        private static string V(AdministrativeDocument d, string name) { var f = ExtractionReview.Field(d, name); return f == null ? null : f.Value; }
        private static string S(AdministrativeDocument d, Func<SignatureBlock, string> select, string fallback)
        { return d.Signers.Count == 0 ? V(d, fallback) : string.Join("\n", d.Signers.Select(s => V(d, select(s)) ?? "[chưa rõ]")); }
        public static void Save(string path, IList<AdministrativeDocument> documents)
        {
            var snapshot = Snapshot(documents); string exported = DateTime.UtcNow.ToString("o");
            path = Path.GetFullPath(path); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var package = new ExcelPackage()) {
                    package.Workbook.Properties.Title = "Báo cáo thông tin sau OCR";
                    package.Workbook.Properties.Subject = "Một dòng/PDF; phiên bản " + LlmExtractionService.SchemaVersion;
                    package.Workbook.Properties.Comments = "Xuất lúc UTC: " + exported + ". Sửa/xác nhận tại ứng dụng; Excel không tự đồng bộ ngược.";
                    var report = new Sheet(package, "Báo cáo", new[] { "STT", "Mã bản ghi", "Tên file", "Đường dẫn PDF", "Tổng trang PDF", "Phạm vi văn bản chính", "Số trang ghi tay", "Cơ quan ban hành", "Cơ quan chủ quản", "Ngày ban hành", "Số/ký hiệu đầy đủ", "Số văn bản", "Ký hiệu", "Loại văn bản", "Tiêu đề", "Trích yếu", "Chức vụ người ký", "Tên người ký", "Quyền hạn ký", "Trạng thái", "Số trường chưa xác nhận", "Cảnh báo / lỗi", "Người xác nhận", "Xác nhận gần nhất (UTC)", "Engine OCR", "Nguồn đã kiểm chứng", "Phiên bản schema", "Cập nhật dữ liệu (UTC)", "Xuất báo cáo (UTC)", "Nhà cung cấp LLM", "Model LLM", "Trạng thái LLM", "Lỗi / cấu hình LLM", "Prompt version", "Request lần chạy", "Input token được báo", "Output token được báo", "Trang đã phân tích" });
                    var pages = new Sheet(package, "Trang số", new[] { "Mã bản ghi", "File PDF", "Trang PDF", "Số in sẵn", "Số ghi tay hiện tại", "Số máy đọc gốc", "Điểm OCR số", "Trạng thái số", "Đã xác nhận", "Xác nhận lúc (UTC)", "Cảnh báo số", "Trạng thái OCR trang", "Lỗi trang" });
                    var signers = new Sheet(package, "Người ký", new[] { "Mã bản ghi", "File PDF", "Thứ tự", "Tên người ký", "Chức vụ", "Quyền hạn ký", "Trang PDF", "Đã xác nhận", "Mã bằng chứng" });
                    var issues = new Sheet(package, "Cần kiểm tra", new[] { "Mã bản ghi", "File PDF", "Trường / loại vấn đề", "Giá trị hiện tại", "Lý do", "Nguyên văn nguồn", "Trang PDF" });
                    var evidence = new Sheet(package, "Bằng chứng", new[] { "Mã bản ghi", "File PDF", "Trường", "Loại", "Mã ứng viên", "Giá trị hiện tại", "Đề xuất LLM / chữ nguồn ứng viên", "Giá trị chuẩn hóa", "Đã xác nhận", "Người xác nhận", "Thời điểm (UTC)", "Lý do / cảnh báo", "Loại nguồn", "Trang PDF", "Mã dòng", "X", "Y", "Rộng", "Cao", "Có tọa độ", "Chữ gốc", "Engine / phương pháp", "Điểm OCR", "Xoay ảnh nguồn (độ)" });
                    int index = 0;
                    foreach (var d in snapshot) {
                        DateTime date; object dateValue = ExtractionReview.TryDate(V(d, "Ngày ban hành"), out date) ? (object)date : null;
                        int row = report.Add(++index, d.RecordId, Path.GetFileName(d.Source), d.Source, d.PageCount,
                            d.PrimaryEndPage > 0 ? d.PrimaryStartPage + "–" + d.PrimaryEndPage + (d.PrimaryRangeConfirmed ? " (đã chọn)" : "") : "",
                            V(d, "Số trang ghi tay"), V(d, "Cơ quan ban hành"), V(d, "Cơ quan chủ quản"), dateValue,
                            V(d, "Số, ký hiệu"), V(d, "Số văn bản"), V(d, "Ký hiệu"), V(d, "Thể loại"), V(d, "Tiêu đề"), V(d, "Trích yếu"),
                            S(d, s => s.TitleField, "Chức vụ người ký"), S(d, s => s.NameField, "Người ký"), S(d, s => s.AuthorityField, "Quyền hạn ký"),
                            d.Status, d.Fields.Count(f => !f.Confirmed), string.Join("\n", d.Warnings.Concat(d.Error == null ? Enumerable.Empty<string>() : new[] { d.Error })),
                            string.Join("; ", d.Fields.Where(f => f.Confirmed).Select(f => f.ConfirmedBy).Where(x => !string.IsNullOrEmpty(x)).Distinct()),
                            d.Fields.Select(f => f.ConfirmedAtUtc).Where(x => x != null).OrderByDescending(x => x).FirstOrDefault(), d.Engine, d.SourceVerified, d.RuleVersion, d.UpdatedAtUtc, exported,
                            d.ExtractionProvider, d.ExtractionModel, d.LlmStatus, d.LlmError, d.PromptVersion, d.LlmRequestCount, d.InputTokens, d.OutputTokens, string.Join(", ", d.AnalyzedPages));
                        if (dateValue != null) report.Current.Cells[row, 10].Style.Numberformat.Format = "dd/mm/yyyy";
                        Uri link; if (Uri.TryCreate(d.Source, UriKind.Absolute, out link) && link.IsFile) report.Current.Cells[row, 4].Hyperlink = link;
                        report.Current.Cells[row, 20].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        report.Current.Cells[row, 20].Style.Fill.BackgroundColor.SetColor(d.Status == "Đã xác nhận" ? Color.Honeydew : d.Error != null ? Color.MistyRose : Color.LemonChiffon);
                        IEnumerable<OcrPageMetadata> pageRows = d.Pages.Count > 0 ? d.Pages : Enumerable.Range(1, d.PageCount).Select(p => new OcrPageMetadata { Page = p, NumberStatus = "disabled" });
                        foreach (var p in pageRows.OrderBy(p => p.Page)) {
                            pages.Add(d.RecordId, d.Source, p.Page, p.PrintedNumber, p.HandwrittenNumber, p.RawHandwrittenNumber, p.NumberScore, p.NumberStatus, p.NumberConfirmed, p.NumberConfirmedAtUtc, p.NumberWarning, p.OcrStatus, p.Error);
                            if (p.NumberStatus != "disabled" && !p.NumberConfirmed) issues.Add(d.RecordId, d.Source, "Số trang ghi tay", p.HandwrittenNumber, "Trang số chưa xác nhận: " + p.NumberStatus + ". " + p.NumberWarning, p.RawHandwrittenNumber, p.Page);
                            var numberField = ExtractionReview.Field(d, "Số trang ghi tay");
                            if (numberField != null) {
                                foreach (var c in p.NumberCandidates) WriteEvidence(evidence, d, numberField, "Ứng viên số trang", c.Id, c.RawValue, c.Value, c.Reason, null, c.Evidence);
                                foreach (var h in p.NumberHistory) evidence.Add(d.RecordId, d.Source, "Số trang ghi tay", "Lịch sử nhãn", null, h.Value, null, null, h.Confirmed, h.User, h.AtUtc, h.Reason, null, p.Page);
                            }
                        }
                        int sn = 0;
                        foreach (var s in d.Signers) {
                            var fs = new[] { s.NameField, s.TitleField, s.AuthorityField }.Select(n => ExtractionReview.Field(d, n)).Where(f => f != null).ToList();
                            signers.Add(d.RecordId, d.Source, ++sn, V(d, s.NameField), V(d, s.TitleField), V(d, s.AuthorityField), s.Page, fs.Count == 3 && fs.All(f => f.Confirmed), string.Join("; ", fs.SelectMany(f => f.Evidence).Select(l => l.Id).Distinct()));
                        }
                        foreach (string warning in d.Warnings) issues.Add(d.RecordId, d.Source, "Cảnh báo tài liệu", null, warning, null, null);
                        if (d.Error != null || d.Pending) issues.Add(d.RecordId, d.Source, d.Pending ? "Chưa xử lý / đã hủy" : "Lỗi xử lý", null, d.Error, null, null);
                        if (!string.IsNullOrEmpty(d.LlmError)) issues.Add(d.RecordId, d.Source, "LLM: " + d.LlmStatus, null, d.LlmError, null, null);
                        foreach (var f in d.Fields) {
                            if (!f.Confirmed) issues.Add(d.RecordId, d.Source, f.Name, f.Value, f.Warning ?? f.Status, string.Join("\n", f.Evidence.Select(l => l.RawText ?? l.Text)), string.Join(", ", f.Evidence.Select(l => l.Page).Distinct()));
                            WriteEvidence(evidence, d, f, "Kết quả", null, f.OriginalValue, f.NormalizedValue, f.Warning, null, f.Evidence);
                            foreach (var c in f.Candidates) WriteEvidence(evidence, d, f, "Ứng viên", c.Id, c.RawValue, c.NormalizedValue, c.Reason, c.Rank, c.Evidence);
                            foreach (var h in f.History) evidence.Add(d.RecordId, d.Source, f.Name, "Lịch sử", null, h.Value, null, null, h.Confirmed, h.User, h.AtUtc, h.Reason);
                        }
                    }
                    foreach (var ws in package.Workbook.Worksheets) {
                        ws.Row(1).Height = 36; ws.Row(1).Style.Font.Bold = true; ws.Row(1).Style.Font.Color.SetColor(Color.White);
                        ws.Row(1).Style.Fill.PatternType = ExcelFillStyle.Solid; ws.Row(1).Style.Fill.BackgroundColor.SetColor(Color.FromArgb(32, 76, 110));
                        ws.View.FreezePanes(2, ws.Name == "Báo cáo" ? 4 : 3); ws.Cells[ws.Dimension.Address].AutoFilter = true;
                        ws.Cells[ws.Dimension.Address].Style.WrapText = true; ws.Cells[ws.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
                        for (int c = 1; c <= ws.Dimension.End.Column; c++) ws.Column(c).Width = c == 2 ? 40 : 25;
                    }
                    package.SaveAs(new FileInfo(temporary));
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void WriteEvidence(Sheet sheet, AdministrativeDocument doc, ExtractedField f, string kind, string id, string raw, string normalized, string reason, int? rank, List<EvidenceLine> lines)
        {
            foreach (var l in lines.Count == 0 ? new EvidenceLine[] { null } : lines.ToArray())
                sheet.Add(doc.RecordId, doc.Source, f.Name, kind, id, f.Value, raw, normalized, f.Confirmed, f.ConfirmedBy, f.ConfirmedAtUtc, reason, l == null ? null : l.SourceKind,
                    l == null ? (object)null : l.Page, l == null ? null : l.Id, l == null || !l.HasGeometry ? (object)null : l.X, l == null || !l.HasGeometry ? (object)null : l.Y,
                    l == null || !l.HasGeometry ? (object)null : l.Width, l == null || !l.HasGeometry ? (object)null : l.Height, l != null && l.HasGeometry,
                    l == null ? null : l.RawText ?? l.Text, l == null ? null : l.Method, l == null ? (object)null : l.Confidence, l == null ? (object)null : l.RotationDegrees);
        }
        private sealed class Sheet
        {
            private readonly ExcelPackage package; private readonly string name; private readonly string[] headers; private int row, part;
            public ExcelWorksheet Current { get; private set; }
            public Sheet(ExcelPackage p, string n, string[] h) { package = p; name = n; headers = h; Next(); }
            private void Next() { Current = package.Workbook.Worksheets.Add(name + (++part == 1 ? "" : " " + part)); row = 1; Add(headers.Cast<object>().ToArray()); }
            public int Add(params object[] values) {
                if (row > 1048576) Next(); int current = row++;
                for (int c = 0; c < values.Length; c++) {
                    string text = values[c] as string;
                    if (text != null && text.Length > 32767) throw new InvalidDataException("Nội dung vượt giới hạn 32.767 ký tự/ô Excel tại sheet " + name + ", dòng " + current + ". Lưu JSON đầy đủ hoặc giảm phạm vi xuất.");
                    if (text != null) Current.Cells[current, c + 1].Style.Numberformat.Format = "@";
                    Current.Cells[current, c + 1].Value = values[c];
                }
                return current;
            }
        }
    }
}
