using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CountFile2.Services
{
    // Markdown is the LLM's text source; geometry stays local and is reused only for exact text matches.
    internal static class LlmMarkdownInput
    {
        public static void Attach(AdministrativeDocument doc, string path, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            token.ThrowIfCancellationRequested();
            byte[] bytes = File.ReadAllBytes(path);
            string markdown = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var available = doc.Lines.GroupBy(l => l.Page + "|" + Normalize(l.RawText ?? l.Text))
                .ToDictionary(g => g.Key, g => new Queue<EvidenceLine>(g));
            var lines = new List<EvidenceLine>(); var used = new HashSet<string>();
            int page = 1, index = 0;
            bool exported = markdown.TrimStart('\uFEFF').StartsWith("# OCR: ", StringComparison.Ordinal);
            bool pageHeader = exported, seenPage = !exported;
            foreach (string raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
                token.ThrowIfCancellationRequested();
                var marker = Regex.Match(raw, @"^##\s+Trang\s+(\d+)\s*/\s*(\d+)\s*$", RegexOptions.IgnoreCase);
                if (marker.Success) {
                    page = int.Parse(marker.Groups[1].Value);
                    if (page < 1 || page > doc.PageCount || int.Parse(marker.Groups[2].Value) != doc.PageCount)
                        throw new InvalidDataException("Trang trong Markdown không khớp PDF nguồn.");
                    pageHeader = true; seenPage = true; continue;
                }
                if (!seenPage || string.IsNullOrWhiteSpace(raw) || exported && raw.Trim() == "---") continue;
                if (exported && pageHeader && (raw.StartsWith("**Trạng thái:**") || raw.StartsWith("**Số trang ghi tay:**") || raw.StartsWith("> "))) continue;
                pageHeader = false;
                string value = Decode(raw.TrimEnd());
                if (string.IsNullOrWhiteSpace(value)) continue;
                Queue<EvidenceLine> matches; EvidenceLine line = null;
                if (available.TryGetValue(page + "|" + Normalize(value), out matches) && matches.Count > 0) line = matches.Dequeue();
                if (line == null) {
                    string id;
                    do { id = page + ":md:" + (++index); } while (used.Contains(id) || doc.Lines.Any(l => l.Id == id));
                    line = new EvidenceLine { Id = id, Page = page, HasGeometry = false, Method = "Markdown", Confidence = 0 };
                }
                if (!used.Add(line.Id)) throw new InvalidDataException("Mã dòng Markdown bị trùng.");
                line.RawText = value; line.Text = Normalize(value); line.MarkdownText = raw.TrimEnd(); lines.Add(line);
            }
            doc.MarkdownPath = Path.GetFullPath(path);
            using (var hash = SHA256.Create()) doc.MarkdownFingerprint = Convert.ToBase64String(hash.ComputeHash(bytes));
            doc.Lines = lines;
        }

        public static string Render(IEnumerable<EvidenceLine> source, int pageCount)
        {
            var result = new StringBuilder("# Nội dung OCR\n\n");
            foreach (var page in source.GroupBy(l => l.Page).OrderBy(g => g.Key)) {
                result.Append("## Trang ").Append(page.Key).Append(" / ").Append(pageCount).Append("\n\n");
                foreach (var line in page)
                    result.Append('[').Append(line.Id).Append("] ").Append(line.MarkdownText ?? VietOcrMarkdownExport.Escape(line.RawText ?? line.Text)).Append("  \n");
                result.AppendLine();
            }
            return result.ToString();
        }

        private static string Decode(string text)
        { return WebUtility.HtmlDecode(Regex.Replace(text, @"\\([\p{P}\p{S}])", "$1")); }
        private static string Normalize(string text)
        { return Regex.Replace((text ?? "").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim(); }
    }
}
