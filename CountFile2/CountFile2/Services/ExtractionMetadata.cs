using System;
using System.Collections.Generic;
using System.Linq;

namespace CountFile2.Services
{
    // Metadata only. Semantic field selection is exclusively performed by LlmExtractionService.
    public static class ExtractionMetadata
    {
        public static readonly string[] Keys = { "pdf_pages", "printed_pages", "handwritten_pages", "document_number_full", "document_number", "document_symbol", "issue_date", "issuer", "parent_organization", "document_type", "title", "subject", "recipients", "greeting", "signer_name", "signer_title", "signing_authority" };
        public static readonly string[] Names = { "Trang PDF", "Trang in / tờ số", "Số trang ghi tay", "Số, ký hiệu", "Số văn bản", "Ký hiệu", "Ngày ban hành", "Cơ quan ban hành", "Cơ quan chủ quản", "Thể loại", "Tiêu đề", "Trích yếu", "Nơi nhận", "Kính gửi", "Người ký", "Chức vụ người ký", "Quyền hạn ký" };
        public static string Name(string key) { int index = Array.IndexOf(Keys, key); return index < 0 ? null : Names[index]; }
        public static void Initialize(AdministrativeDocument doc)
        {
            doc.Fields = Keys.Select((key, i) => new ExtractedField { Key = key, Name = Names[i], RequiresReview = true, Warning = "Chờ LLM phân tích." }).ToList();
            doc.Signers.Clear(); doc.Warnings = doc.SourceWarnings.ToList();
            doc.RuleVersion = "llm-schema-1"; doc.LlmStatus = "pending";
            doc.LlmError = null; doc.DocumentGroups.Clear(); doc.MultipleDocuments = false; doc.AnalyzedPages.Clear();
            doc.ExtractionProvider = doc.ExtractionModel = doc.ExtractionCacheKey = doc.PromptVersion = null;
            doc.LlmRequestCount = 0; doc.InputTokens = doc.OutputTokens = null;
            foreach (var page in doc.Pages) page.PrintedNumber = null;
            if (doc.PrimaryEndPage == 0) doc.PrimaryEndPage = doc.PageCount;
            if (doc.RecordId == null && doc.Source != null) doc.RecordId = PostOcrExtractionService.RecordId(doc.Source);
            if (doc.Pages.Count == 0) for (int p = 1; p <= doc.PageCount; p++) doc.Pages.Add(new OcrPageMetadata { Page = p, NumberStatus = "disabled" });
            foreach (var page in doc.Lines.GroupBy(l => l.Page)) {
                int i = 0; foreach (var line in page) { if (string.IsNullOrEmpty(line.Id)) line.Id = page.Key + ":" + (++i); if (line.RawText == null) line.RawText = line.Text; }
            }
            var count = ExtractionReview.Field(doc, "Trang PDF"); count.Value = count.OriginalValue = doc.PageCount.ToString();
            count.Confirmed = true; count.RequiresReview = false; count.Warning = "Tổng số trang từ nguồn PDF/OCR.";
            UpdatePageNumbers(doc); doc.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        }
        public static void UpdatePageNumbers(AdministrativeDocument doc)
        {
            var field = ExtractionReview.Field(doc, "Số trang ghi tay"); if (field == null) return;
            var pages = doc.Pages.Where(p => !string.IsNullOrEmpty(p.NumberStatus) && p.NumberStatus != "disabled").OrderBy(p => p.Page).ToList();
            field.Value = string.Join("; ", pages.Select(p => "PDF " + p.Page + ": " + (string.IsNullOrWhiteSpace(p.HandwrittenNumber) ? p.NumberConfirmed ? "Không có số" : "?" : p.HandwrittenNumber)));
            field.OriginalValue = string.Join("; ", pages.Select(p => "PDF " + p.Page + ": " + p.RawHandwrittenNumber));
            field.Confirmed = pages.Count == 0 || pages.All(p => p.NumberConfirmed); field.NotApplicable = pages.Count == 0;
            field.RequiresReview = pages.Any(p => !p.NumberConfirmed);
            field.Evidence = pages.SelectMany(p => p.NumberEvidence).ToList(); field.Candidates.Clear();
            field.Warning = pages.Count == 0 ? "Chưa bật nhận dạng số ghi tay." : "Dùng chung nguồn OCR; sửa/xác nhận tại Số trang ghi tay.";
        }
    }
}
