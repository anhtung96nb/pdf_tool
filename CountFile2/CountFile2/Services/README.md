# PDF / TIFF conversion service

PDF compression by percentage with a minimum effective DPI: see [PdfCompression.md](PdfCompression.md) for configuration, fallback results with DPI notes, API and verification commands.

PDF cleanup and quality checking: see [PdfCleanup.md](PdfCleanup.md) for the WinForms interface, API, conservative blank-page policy, deskew/orientation thresholds and verification commands.

Use CountFile2.Services.PdfTiffConversionService in the existing .NET Framework 4.8 application.

```csharp
var service = new CountFile2.Services.PdfTiffConversionService();
var toTiff = await service.ConvertPdfToTiffAsync(@"D:\Input", @"D:\OutputTiff", dpi: 300);
var toPdf = await service.ConvertTiffToPdfAsync(@"D:\OutputTiff", @"D:\OutputPdf");
var oneFile = service.ConvertPdfToTiff(@"D:\Input\document.pdf");
foreach (var result in toTiff)
{
    if (!result.Success)
        System.Diagnostics.Debug.WriteLine(result.InputPath + ": " + result.Error.Message);
}
```

- Input accepts one file or a directory, recursively including nested directories. Directory junctions and symbolic links are skipped.
- PDF produces one multi-page .tiff per source, with lossless LZW compression. Default rendering resolution: 300 DPI (1-1200).
- Both .tif and .tiff are accepted, case-insensitively. Each TIFF frame becomes a PDF page; frame DPI determines physical page size (300 DPI fallback).
- Omit outputDirectory to write next to the source. Otherwise the source directory structure is preserved under the output directory.
- Existing outputs are reported as errors unless overwrite: true. Temporary files are published only after conversion completes. Source files are preserved.
- Corrupt files are returned with Success=false and Error; other files continue. Invalid input paths/options and directory enumeration errors throw. CancellationToken cancels between files/pages and throws OperationCanceledException.
- Two inputs mapping to the same destination (such as name.tif and name.tiff) are reported as a collision for the second input, even with overwrite enabled.
- PDF-to-TIFF rasterizes content. A PDF recreated from TIFF contains images; original selectable text, vectors, signatures and form fields are not restored.
- Reuses existing PdfiumViewer and iTextSharp packages. Deploy the matching x86/x64 native pdfium.dll along with the application.

Synchronous methods are also available. Prefer Async methods in WinForms event handlers to keep the UI responsive.

## WinForms interface

Open **Chuyển đổi TIFF ↔ PDF** from the main screen. Select direction, one source file or a recursive source folder, and an output folder. DPI is enabled only for PDF-to-TIFF. The result table shows each file, page count, status and error; failures do not stop the remaining files. Cancel preserves completed files and removes the current temporary file. Closing during a run requests cancellation; close again after cleanup completes.

All conversion methods accept an optional trailing `IProgress<ConversionProgress> progress`. Create `Progress<ConversionProgress>` on the WinForms UI thread. Reports include the scanned pending files, file/page progress and completed results. The scan is performed once before output is written. `ConversionCanceledException` derives from `OperationCanceledException` and exposes a read-only `Results` snapshot of completed files; cancellation during scanning can still throw a regular `OperationCanceledException` with no completed results.

Verification: `tests/ConversionIntegrationTests.cs` generates its own single/multi-page fixtures and checks roundtrip page counts, DPI/physical size, nested folders, overwrite, invalid files, destination collisions, cancellation cleanup/partial results, and the form's asynchronous result/state handling. Compile as a console executable referencing the built application, PdfiumViewer, iTextSharp, System.Drawing and System.Windows.Forms; place it next to the application libraries and run with an artifact directory argument. Both x86 and x64 native PDFium libraries are copied by the project.

From the repository root, run:

```powershell
./tests/Run-ConversionTests.ps1 -Configuration Debug -Architecture x86
./tests/Run-ConversionTests.ps1 -Configuration Release -Architecture x64
```

Both commands passed on 2026-10-12. The form tests run offscreen with a WinForms synchronization context and message pumping; native file/folder dialogs and Explorer opening are not exercised. Existing warnings in the statistics/OCR code and the iTextSharp binding redirect remain. Generated test fixtures and layout images are placed in a unique `tmp/conversion-tests/run-*` directory for each run.
