using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using CountFile2.Services;
class Check {
 static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
 static void Main(string[] args) {
 string root=args[0]; var service=new PdfTiffConversionService();
 var result=service.ConvertPdfToTiff(Path.Combine(root,"input"),Path.Combine(root,"tiff"),72);
 Assert(result.Count==1 && result[0].Success,"recursive conversion: "+(result.Count==1?Convert.ToString(result[0].Error):"count"));
 int pages=result[0].PageCount;
 using(var image=Image.FromFile(result[0].OutputPath)) Assert(image.GetFrameCount(FrameDimension.Page)==pages,"TIFF page count");
 var reverse=service.ConvertTiffToPdf(Path.Combine(root,"tiff"),Path.Combine(root,"pdf"));
 Assert(reverse.Count==1 && reverse[0].Success,"reverse conversion: "+Convert.ToString(reverse[0].Error));
 using(var pdf=PdfiumViewer.PdfDocument.Load(reverse[0].OutputPath)) Assert(pdf.PageCount==pages,"PDF page count");
 Assert(!service.ConvertPdfToTiff(Path.Combine(root,"input"),Path.Combine(root,"tiff"),72)[0].Success,"overwrite protection");
 Assert(service.ConvertPdfToTiff(result[0].InputPath,Path.Combine(root,"single"),72)[0].Success,"single file");
 Assert(service.ConvertPdfToTiff(result[0].InputPath,Path.Combine(root,"single"),72,true)[0].Success,"overwrite");
 File.WriteAllText(Path.Combine(root,"input","bad.pdf"),"invalid");
 var mixed=service.ConvertPdfToTiff(Path.Combine(root,"input"),Path.Combine(root,"mixed"),72);
 Assert(mixed.Count==2 && mixed.Count(x=>x.Success)==1,"continue after corrupt file");
 var token=new CancellationToken(true); bool cancelled=false;
 try { service.ConvertTiffToPdf(Path.Combine(root,"tiff"),null,false,token); } catch(OperationCanceledException) { cancelled=true; }
 Assert(cancelled,"cancellation");
 Console.WriteLine("PASS: recursive, single file, TIFF/PDF page counts ("+pages+"), overwrite protection, overwrite, corrupt input isolation, cancellation.");
 }
}