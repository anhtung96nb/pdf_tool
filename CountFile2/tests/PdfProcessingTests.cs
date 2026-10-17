using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CountFile2.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
class PdfProcessingTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main(string[] args)
    {
        string root = System.IO.Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        string source = System.IO.Path.Combine(root, "input.pdf");
        using (var stream = File.Create(source))
        using (var doc = new Document())
        {
            PdfWriter.GetInstance(doc, stream); doc.Open();
            doc.Add(new Paragraph("HELLO WORLD 12345", FontFactory.GetFont("Helvetica", 28)));
            doc.NewPage(); doc.Add(new Paragraph("SECOND PAGE 67890", FontFactory.GetFont("Helvetica", 28)));
        }
        var service = new PdfProcessingService();
        string output = System.IO.Path.Combine(root, "output");
        var result = service.ConvertToSearchablePdf(source, output).Single();
        Assert(result.Success, "OCR: " + result.Error);
        using (var reader = new PdfReader(result.OutputPath))
        {
            Assert(reader.NumberOfPages == 2, "page count");
            Assert(PdfTextExtractor.GetTextFromPage(reader, 1).Contains("12345"), "searchable text");
            Assert(Encoding.ASCII.GetString(reader.GetPageContent(1)).Contains("3 Tr"), "invisible text");
        }
        var archive = service.ConvertToPdfA(source, output, searchable: true).Single();
        Assert(archive.Success, "PDF/A OCR: " + archive.Error);
        using (var reader = new PdfReader(archive.OutputPath))
        {
            Assert(reader.NumberOfPages == 2, "PDF/A page count");
            Assert(reader.Catalog.GetAsArray(PdfName.OUTPUTINTENTS) != null, "ICC output intent");
            Assert(Encoding.UTF8.GetString(reader.Metadata).Contains("pdfaid:conformance=\"B\""), "PDF/A metadata");
        }
        Assert(!service.ConvertToSearchablePdf(source, output).Single().Success, "overwrite denied");
        Assert(service.ConvertToSearchablePdf(source, output, new PdfProcessingOptions { Overwrite = true }).Single().Success, "overwrite");
        string batch = System.IO.Path.Combine(root, "batch");
        Directory.CreateDirectory(System.IO.Path.Combine(batch, "nested"));
        File.Copy(source, System.IO.Path.Combine(batch, "nested", "OK.PDF"));
        File.WriteAllText(System.IO.Path.Combine(batch, "bad.pdf"), "broken");
        var results = service.ConvertToPdfA(batch, System.IO.Path.Combine(root, "batch-output"));
        Assert(results.Count == 2 && results.Count(x => x.Success) == 1, "batch continues");
        var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { service.ConvertToPdfA(source, output, cancellationToken: cancellation.Token); throw new Exception("cancellation missing"); }
        catch (OperationCanceledException) { }
        using (var pdf = PdfiumViewer.PdfDocument.Load(archive.OutputPath))
        using (var image = pdf.Render(0, 800, 1100, true)) image.Save(System.IO.Path.Combine(root, "preview.png"));
        Assert(!Directory.GetFiles(root, ".pdf-processing-*", SearchOption.AllDirectories).Any(), "temporary cleanup");
        Console.WriteLine("PASS: OCR, PDF/A metadata/ICC, pages, overwrite, recursive batch, corrupt input, cancellation, cleanup.");
    }
}
