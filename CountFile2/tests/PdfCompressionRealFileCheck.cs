using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CountFile2.Services;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Path = System.IO.Path;

class PdfCompressionRealFileCheck
{
    sealed class PlacementListener : IRenderListener
    {
        public List<string> Placements = new List<string>();
        public void BeginTextBlock() { } public void EndTextBlock() { } public void RenderText(TextRenderInfo info) { }
        public void RenderImage(ImageRenderInfo info)
        {
            var dictionary = (PdfDictionary)PdfReader.GetPdfObject(info.GetRef());
            string pixelHash;
            using (var hash = SHA256.Create()) pixelHash = Convert.ToBase64String(hash.ComputeHash(PdfReader.GetStreamBytesRaw((PRStream)dictionary)));
            Placements.Add(dictionary.GetAsNumber(PdfName.WIDTH) + "x" + dictionary.GetAsNumber(PdfName.HEIGHT) + ":" + info.GetImageCTM() + ":" + pixelHash);
        }
    }
    static string[] Placements(string file)
    {
        using (var reader = new PdfReader(file))
        {
            var listener = new PlacementListener();
            for (int p = 1; p <= reader.NumberOfPages; p++) new PdfReaderContentParser(reader).ProcessContent(p, listener);
            return listener.Placements.ToArray();
        }
    }
    static string Hash(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return Convert.ToBase64String(hash.ComputeHash(stream)); }
    static void Main(string[] args)
    {
        var sources = Directory.GetFiles(args[0], "*.pdf").ToDictionary(p => Path.GetFullPath(p), Hash, StringComparer.OrdinalIgnoreCase);
        var results = new PdfCompressionService().Process(args[0], args[1], new PdfCompressionOptions { MinDpi = 300, ReductionPercent = 20 });
        foreach (var result in results)
        {
            if (!result.Written || result.Error != null || !result.DpiSatisfied || result.BeforeDpi < 300 || result.AfterDpi < 300
                || result.OutputBytes > result.OriginalBytes || Hash(result.InputPath) != sources[result.InputPath]
                || !Placements(result.InputPath).SequenceEqual(Placements(result.OutputPath)))
                throw new Exception("Real-file verification failed: " + result.InputPath + ": " + result.Details);
            if (!result.Details.Contains("JPEG")) throw new Exception("Missing JPEG diagnostic: " + result.InputPath + ": " + result.Details);
            Console.WriteLine(Path.GetFileName(result.InputPath) + ": " + result.OriginalBytes + " -> " + result.OutputBytes
                + " (" + result.ActualReduction.ToString("F4") + "%), DPI " + result.BeforeDpi + " -> " + result.AfterDpi);
        }
        Directory.CreateDirectory(args[1]);
        PdfCompressionService.ExportCsv(Path.Combine(args[1], "report.csv"), results);
        Console.WriteLine("PASS: " + results.Count + " files; image data/dimensions/placements, minimum 300 DPI and source hashes preserved.");
    }
}
