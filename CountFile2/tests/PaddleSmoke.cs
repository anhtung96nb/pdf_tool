using System;
using System.IO;
using System.Linq;
using System.Threading;
using CountFile2.Services;
class PaddleSmoke {
 static void Main(string[] args) {
 var options = new VietOcrOptions { EngineName="PaddleOCR", OutputFolderName="paddle-ocr", PythonPath=args[0], WorkerPath=args[1], Dpi=200, TimeoutSeconds=600 };
 var result = new VietOcrService().ProcessAsync(args[2], args[3], options, CancellationToken.None).GetAwaiter().GetResult().Single();
 if(result.FailedPages != 0 || result.CompletedPages != 2) throw new Exception(result.Details);
 string json=File.ReadAllText(result.JsonPath);
 if(!json.Contains("PaddleOCR") || !json.Contains("THÔNG BÁO")) throw new Exception("Missing engine or Vietnamese recognition: " + json);
 Console.WriteLine("PADDLE REAL PDF PASS: " + result.JsonPath);
 }
}
