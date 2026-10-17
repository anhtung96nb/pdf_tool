# PDF OCR and PDF/A service

```csharp
var service = new CountFile2.Services.PdfProcessingService();
var ocr = await service.ConvertToSearchablePdfAsync(@"D:\Input\scan.pdf", @"D:\Output");
var archive = await service.ConvertToPdfAAsync(@"D:\Input", @"D:\Archive");
var searchableArchive = await service.ConvertToPdfAAsync(@"D:\Input", @"D:\Archive", searchable: true);
foreach (var result in searchableArchive)
    if (!result.Success) System.Diagnostics.Debug.WriteLine(result.InputPath + ": " + result.Error);
```

Accepts a PDF file or folder (recursive by default; skips directory reparse points). Returns one `ConversionResult` per file. Async variants run on a worker thread. Optional `IProgress<ConversionProgress>` reports page/file progress. Cancellation throws `ConversionCanceledException` with completed results; cancellation during scanning may throw `OperationCanceledException`.

Output suffixes: `.ocr.pdf`, `.pdfa.pdf`, `.ocr.pdfa.pdf`. Output defaults to the source directory; explicit output folders preserve subfolders. Inputs are snapshotted before conversion. All PDF files in a folder are eligible, including previous generated outputs: use a separate output folder outside the input tree for repeated batch runs. Sources are preserved; existing destinations require `Overwrite = true`. Only completed outputs are atomically published; per-file errors do not stop the batch.

`PdfProcessingOptions` controls DPI (72-600, default 300), recursive scan, overwrite, OCR language (default `vie`), tessdata, embedded TrueType font and sRGB ICC profile paths. Defaults use application `tessdata`, Windows Arial and the Windows sRGB profile. Deploy traineddata matching the chosen language(s), Tesseract native libraries, PDFium native libraries, iTextSharp and its matching PDF/A assembly. No Python or external OCR process is required.

Both operations rasterize pages to RGB images at the selected DPI. Searchable output adds invisible Unicode OCR words positioned on the image. PDF/A writes PDF/A-1b with XMP and an sRGB output intent; `searchable: true` also embeds the OCR font. Plain PDF/A does not retain selectable source text. Original vectors, forms, links, signatures, attachments and accessibility structure are not retained. OCR accuracy depends on scan quality. Pages above 100 megapixels fail with an instruction to reduce DPI. Cancellation is checked between pages/words, not during native rendering or recognition.

PDF/A writer conformance checks and structural tests are used; independent veraPDF validation is recommended for archival acceptance. See https://kb.itextpdf.com/it5kb/pdf-a-1 for the writer configuration.

## WinForms

Run CountFile2.exe. The main window opens TIFF/PDF by default. Its navigation switches between Statistics, TIFF/PDF, PDF OCR/PDF-A, and document metadata OCR. Each screen is created once and retains its inputs and results when hidden.

Select **PDF hai lớp / PDF/A**, choose one of the three modes, select a file or folder, choose the destination (or leave it blank), then click **Bắt đầu**. **Cấu hình nâng cao** exposes language, tessdata, font and ICC paths. OCR and ICC fields are enabled only for the modes that need them. The table displays pending files, per-page progress and final results/errors.

Switching screens keeps the current job running. One document job runs at a time; the shell displays its name and offers cancellation from any screen. Metadata OCR and statistics run on worker threads; their controls are disabled during any document job to protect state. Closing the shell during a job asks whether to stop, requests cancellation, waits for cleanup and then disposes all screens. Cancellation cannot interrupt native recognition/rendering or an Excel save already in progress.

Run `tests/Run-WinFormsNavigationTests.ps1 -Configuration Debug` or `-Configuration Release` from the repository root. Tests use a WinForms message pump with cross-thread access checks enabled. They cover cached screens/state, legacy OCR, statistics background updates/cancellation, OCR Excel export, all three PDF modes, option enablement, job exclusion, mixed recursive batches, overwrite, default output, cancellation, shutdown and resource disposal. Layout snapshots cover the minimum window and simulated 125%/150% control scaling. Native file/folder dialogs, Explorer launch and changing the actual Windows display DPI are not automated.
