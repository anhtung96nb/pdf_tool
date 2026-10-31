using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CountFile2.Services
{
    public static class ExtractionExportService
    {
        public static void SaveJson(string path, IList<AdministrativeDocument> documents)
        {
            documents = PostOcrReportExport.Snapshot(documents);
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
            foreach (var doc in documents) PostOcrExtractionService.ValidateCurrentSource(doc);
            return documents;
        }
        public static void SaveExcel(string path, IList<AdministrativeDocument> documents)
        { PostOcrReportExport.Save(path, documents); }
    }
}
