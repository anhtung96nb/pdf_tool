using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CountFile2.Services
{
    // Export OCR as literal text within Markdown: do not interpret source text as markup.
    internal static class VietOcrMarkdownExport
    {
        public static void WriteHeader(TextWriter writer, string input, int pageCount)
        {
            writer.WriteLine("# OCR: " + Escape(Path.GetFileName(input)));
            writer.WriteLine();
            writer.WriteLine("Tổng số trang: " + pageCount);
            writer.WriteLine();
        }

        public static void WritePage(TextWriter writer, IDictionary<string, object> page, int totalPages)
        {
            if (Convert.ToInt32(page["page"]) > 1) { writer.WriteLine("---"); writer.WriteLine(); }
            writer.WriteLine("## Trang " + page["page"] + " / " + totalPages);
            writer.WriteLine();
            string state = Convert.ToString(page["status"]);
            string label = state == "completed" ? "Hoàn tất" : state == "needs_review" ? "Cần kiểm tra" : state == "failed" ? "Lỗi" : state;
            writer.WriteLine("**Trạng thái:** " + Escape(label));
            writer.WriteLine();
            object metadata;
            if (page.TryGetValue("document_page_number", out metadata) && metadata is Dictionary<string, object>)
            {
                var number = (Dictionary<string, object>)metadata;
                string stateNumber = Convert.ToString(number["status"]);
                object value, confirmed;
                bool manual = number.TryGetValue("confirmed", out confirmed) && Convert.ToBoolean(confirmed);
                if (stateNumber != "disabled")
                {
                    string numberLabel = stateNumber == "recognized" && number.TryGetValue("value", out value) && value != null ? Convert.ToString(value) :
                        stateNumber == "not_found" ? (manual ? "Không có số" : "Không tìm thấy trong vùng cấu hình") : "Cần kiểm tra";
                    writer.WriteLine("**Số trang ghi tay:** " + Escape(numberLabel) + (manual ? " (đã xác nhận)" : "")); writer.WriteLine();
                }
            }
            if (page.ContainsKey("error"))
            {
                writer.WriteLine("> **Lỗi OCR:**");
                foreach (string line in Lines(Convert.ToString(page["error"]))) writer.WriteLine("> " + Escape(line));
                writer.WriteLine();
            }
            // Two trailing spaces are a CommonMark hard break; blank lines remain paragraphs.
            foreach (string line in Lines(Convert.ToString(page["text"])))
                writer.WriteLine(string.IsNullOrWhiteSpace(line) ? "" : Escape(line) + "  ");
            writer.WriteLine();
        }

        private static IEnumerable<string> Lines(string value)
        { return (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'); }

        internal static string Escape(string value)
        {
            var result = new StringBuilder();
            bool leading = true;
            foreach (char c in value ?? "")
            {
                // Prevent indented source lines becoming code blocks.
                if (leading && (c == ' ' || c == '\t')) { result.Append(c == ' ' ? "&#32;" : "&#9;"); continue; }
                leading = false;
                if (c == '&') result.Append("&amp;");
                else if (c == '<') result.Append("&lt;");
                else if (c == '>') result.Append("&gt;");
                else { if ("\\`*_{}[]()#+-.!|~".IndexOf(c) >= 0) result.Append('\\'); result.Append(c); }
            }
            return result.ToString();
        }
    }
}
