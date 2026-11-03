using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CountFile2.Services
{
    public sealed class FieldCandidate
    {
        public string Id { get; set; }
        public string Value { get; set; }
        public string RawValue { get; set; }
        public string NormalizedValue { get; set; }
        public int Rank { get; set; }
        public string Reason { get; set; }
        public List<EvidenceLine> Evidence { get; set; } = new List<EvidenceLine>();
    }
    public sealed class FieldRevision
    {
        public string Value { get; set; }
        public bool Confirmed { get; set; }
        public bool NotApplicable { get; set; }
        public string AtUtc { get; set; }
        public string User { get; set; }
        public string Reason { get; set; }
    }
    public sealed class OcrPageMetadata
    {
        public int Page { get; set; }
        public string OcrStatus { get; set; }
        public string Error { get; set; }
        public string PrintedNumber { get; set; }
        public string HandwrittenNumber { get; set; }
        public string RawHandwrittenNumber { get; set; }
        public string NumberStatus { get; set; }
        public double? NumberScore { get; set; }
        public bool NumberConfirmed { get; set; }
        public string NumberConfirmedAtUtc { get; set; }
        public string NumberWarning { get; set; }
        public List<EvidenceLine> NumberEvidence { get; set; } = new List<EvidenceLine>();
        public List<FieldCandidate> NumberCandidates { get; set; } = new List<FieldCandidate>();
        public List<FieldRevision> NumberHistory { get; set; } = new List<FieldRevision>();
    }
    public sealed class SignatureBlock
    {
        public int Page { get; set; }
        public string NameField { get; set; }
        public string TitleField { get; set; }
        public string AuthorityField { get; set; }
    }
    public static class ExtractionReview
    {
        public static ExtractedField Field(AdministrativeDocument doc, string name)
        { return doc.Fields.FirstOrDefault(f => f.Name == name); }
        public static void Update(AdministrativeDocument doc, ExtractedField field, string value, bool confirmed,
            bool notApplicable = false, string reason = "Sửa/xác nhận thủ công")
        {
            if (field.Name == "Số trang ghi tay") throw new InvalidOperationException("Sửa nhãn tại tab Số trang ghi tay để cập nhật cùng nguồn OCR.");
            if (field.Name == "Trang PDF") throw new InvalidOperationException("Tổng số trang lấy từ PDF/metadata OCR.");
            field.History.Add(new FieldRevision { Value = field.Value, Confirmed = field.Confirmed, NotApplicable = field.NotApplicable,
                AtUtc = DateTime.UtcNow.ToString("o"), User = Environment.UserName, Reason = reason });
            field.Value = notApplicable ? null : value; field.NotApplicable = notApplicable;
            field.HasManualValue = true;
            field.Confirmed = confirmed; field.ConfirmedAtUtc = confirmed ? DateTime.UtcNow.ToString("o") : null;
            field.ConfirmedBy = confirmed ? Environment.UserName : null;
            field.NormalizedValue = null;
            if (field.Name == "Ngày ban hành" && !string.IsNullOrWhiteSpace(field.Value)) {
                DateTime date;
                if (!TryDate(field.Value, out date)) {
                    field.Confirmed = false; field.RequiresReview = true; field.Warning = "Ngày không hợp lệ. Nhập yyyy-MM-dd hoặc dd/MM/yyyy.";
                    field.ConfirmedAtUtc = field.ConfirmedBy = null;
                    doc.UpdatedAtUtc = DateTime.UtcNow.ToString("o"); return;
                }
                field.NormalizedValue = date.ToString("yyyy-MM-dd");
            }
            if (confirmed && !notApplicable && string.IsNullOrWhiteSpace(field.Value)) {
                field.Confirmed = false; field.Warning = "Trường trống: chọn Không áp dụng nếu thông tin không có.";
                field.ConfirmedAtUtc = field.ConfirmedBy = null;
            } else field.Warning = field.Confirmed ? "Đã xác nhận thủ công." : "Chưa được người dùng xác nhận.";
            doc.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            if (field.Name == "Số, ký hiệu") UpdateNumberParts(doc, field);
        }
        private static void UpdateNumberParts(AdministrativeDocument doc, ExtractedField full)
        {
            var match = System.Text.RegularExpressions.Regex.Match(full.Value ?? "", @"^([0-9]+)\s*/\s*(\S.*)$");
            foreach (string name in new[] { "Số văn bản", "Ký hiệu" }) {
                var part = Field(doc, name); if (part == null) continue;
                string value = match.Success ? match.Groups[name == "Số văn bản" ? 1 : 2].Value.Trim() : null;
                if (part.Value == value) continue;
                if (part.Confirmed || part.History.Any(h => h.Reason != "Cập nhật theo số/ký hiệu đầy đủ")) { part.Confirmed = false; part.RequiresReview = true; part.Warning = "Số/ký hiệu đầy đủ đã đổi; cần đối chiếu thành phần đã sửa."; continue; }
                part.History.Add(new FieldRevision { Value = part.Value, AtUtc = DateTime.UtcNow.ToString("o"), User = Environment.UserName, Reason = "Cập nhật theo số/ký hiệu đầy đủ" });
                part.Value = value; part.Evidence = full.Evidence.ToList(); part.Confirmed = false; part.RequiresReview = !match.Success;
                part.Warning = "Thành phần cập nhật theo số/ký hiệu đầy đủ; chưa xác nhận.";
            }
        }
        public static void ChooseCandidate(AdministrativeDocument doc, ExtractedField field, FieldCandidate candidate)
        {
            field.Evidence = candidate.Evidence.ToList();
            Update(doc, field, candidate.Value, false, false, "Chọn ứng viên " + candidate.Id);
            field.NormalizedValue = candidate.NormalizedValue; field.Evidence = candidate.Evidence.ToList();
        }
        public static bool TryDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value, new[] { "yyyy-MM-dd", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        public static void Preserve(AdministrativeDocument previous, AdministrativeDocument current)
        {
            if (previous == null) return;
            bool same = !string.IsNullOrEmpty(previous.Fingerprint) && previous.Fingerprint == current.Fingerprint && previous.OcrFingerprint == current.OcrFingerprint && previous.MarkdownFingerprint == current.MarkdownFingerprint;
            bool retained = false;
            foreach (var old in previous.Fields.Where(f => f.Name != "Số trang ghi tay" && f.Name != "Trang PDF")) {
                bool manual = old.Confirmed || old.HasManualValue || old.Value != old.OriginalValue;
                var fresh = Field(current, old.Name);
                if (fresh == null && manual) {
                    fresh = new ExtractedField { Name = old.Name, Key = old.Key, RequiresReview = true, Warning = "LLM chưa tìm thấy lại trường đã sửa." };
                    current.Fields.Add(fresh);
                }
                if (fresh == null) continue;
                fresh.History = old.History.ToList();
                if (!manual) continue;
                fresh.History.Add(new FieldRevision { Value = old.Value, Confirmed = old.Confirmed, NotApplicable = old.NotApplicable,
                    AtUtc = DateTime.UtcNow.ToString("o"), User = Environment.UserName, Reason = same ? "Giữ bản sửa khi bóc tách lại" : "Nguồn thay đổi; giữ bản sửa để đối chiếu" });
                if (previous.PrimaryStartPage != current.PrimaryStartPage || previous.PrimaryEndPage != current.PrimaryEndPage) {
                    fresh.History.Last().Reason = "Đổi phạm vi: lưu bản sửa văn bản trước vào lịch sử, không chuyển sang văn bản mới"; continue;
                }
                retained = true; fresh.HasManualValue = true;
                fresh.Value = old.Value; fresh.NotApplicable = old.NotApplicable;
                fresh.Confirmed = same && old.Confirmed; fresh.ConfirmedBy = same ? old.ConfirmedBy : null; fresh.ConfirmedAtUtc = same ? old.ConfirmedAtUtc : null;
                if (same) { fresh.Evidence = old.Evidence.ToList(); fresh.NormalizedValue = old.NormalizedValue; }
                fresh.RequiresReview |= !same;
                fresh.Warning = same ? old.Warning : "Nguồn thay đổi: cần đối chiếu bản sửa với bằng chứng mới.";
            }
            if (previous.PrimaryStartPage == current.PrimaryStartPage && previous.PrimaryEndPage == current.PrimaryEndPage)
                foreach (var signer in previous.Signers.Where(s => !current.Signers.Any(n => n.NameField == s.NameField)))
                    if (new[] { signer.NameField, signer.TitleField, signer.AuthorityField }.Select(n => Field(current, n)).Any(f => f != null && f.HasManualValue))
                        current.Signers.Add(new SignatureBlock { Page = signer.Page, NameField = signer.NameField, TitleField = signer.TitleField, AuthorityField = signer.AuthorityField });
            if (!same && retained) current.Warnings.Add("Nguồn thay đổi: đã giữ giá trị sửa thủ công và bỏ xác nhận để đối chiếu lại.");
        }
    }
}
