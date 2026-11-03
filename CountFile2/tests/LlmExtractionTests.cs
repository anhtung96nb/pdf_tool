using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CountFile2;
using CountFile2.Services;
using OfficeOpenXml;

class LlmExtractionTests
{
    static int checks;
    static string root;
    static JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("OK " + checks + " " + message); }
    static ExtractedField F(AdministrativeDocument d, string name) { return d.Fields.First(f => f.Name == name); }
    static T Clone<T>(T value) { return json.Deserialize<T>(json.Serialize(value)); }
    static AdministrativeDocument Doc(string name, int pages) {
        string dir = Path.Combine(root, name); Directory.CreateDirectory(dir); string path = Path.Combine(dir, "văn bản.pdf");
        using (var stream = File.Create(path)) using (var pdf = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4)) {
            var writer = iTextSharp.text.pdf.PdfWriter.GetInstance(pdf, stream); pdf.Open();
            for (int p = 1; p <= pages; p++) { pdf.NewPage(); writer.DirectContent.Rectangle(40, 40, 500, 700); writer.DirectContent.Stroke(); }
        }
        var d = new AdministrativeDocument { Source = path, Fingerprint = PostOcrExtractionService.Fingerprint(path), OcrFingerprint = "fixture-1", Engine = "VietOCR", SourceVerified = true, PageCount = pages, PrimaryEndPage = pages, CoordinateFrame = "pdf_original" };
        string[] text = { "SỞ NỘI VỤ", "Số: 003/QĐ-SNV", "Hà Nội, ngày 12 tháng 10 năm 2026", "QUYẾT ĐỊNH", "Về việc phê duyệt kế hoạch", "KT. GIÁM ĐỐC", "PHÓ GIÁM ĐỐC", "Nguyễn Văn An" };
        for (int i = 0; i < text.Length; i++) d.Lines.Add(new EvidenceLine { Id = "l" + i, Text = text[i], RawText = text[i], Page = i < 5 ? 1 : pages, X = i < 5 ? .1f : .6f, Y = .08f + i * .08f, Width = .3f, Height = .02f, Confidence = .95f, Method = "fixture" });
        d.Pages.Add(new OcrPageMetadata { Page = 1, NumberStatus = "recognized", HandwrittenNumber = "003", RawHandwrittenNumber = "003", NumberConfirmed = true });
        for (int i = 2; i <= pages; i++) d.Pages.Add(new OcrPageMetadata { Page = i, NumberStatus = "disabled" });
        ExtractionMetadata.Initialize(d); return d;
    }
    static LlmExtractionOptions Options() { return new LlmExtractionOptions { Provider = "Ollama", Model = "test-local", UseCache = false, CacheDirectory = Path.Combine(root, "cache"), TimeoutSeconds = 5, MaxAttempts = 2 }; }
    static LlmFieldData Field(string key, string value, string raw, string id, string signer) {
        return new LlmFieldData { key = key, value = value, raw_value = raw, group_id = "doc-1", signer_id = signer, state = value == null ? "missing" : "found", evidence_ids = id == null ? new string[0] : new[] { id }, issue = null };
    }
    static LlmResponseData Reply(int pages) {
        return new LlmResponseData { document_groups = new[] { new LlmDocumentGroup { id = "doc-1", start_page = 1, end_page = pages, is_primary = true } },
            fields = new[] { Field("issuer", "SỞ NỘI VỤ", "SỞ NỘI VỤ", "l0", null), Field("document_number_full", "003/QĐ-SNV", "Số: 003/QĐ-SNV", "l1", null),
                Field("document_number", "003", "Số: 003/QĐ-SNV", "l1", null), Field("issue_date", "2026-10-12", "ngày 12 tháng 10 năm 2026", "l2", null),
                Field("subject", "Về việc phê duyệt kế hoạch", "Về việc phê duyệt kế hoạch", "l4", null), Field("signer_name", "Nguyễn Văn An", "Nguyễn Văn An", "l7", "signer-1"),
                Field("signer_title", "PHÓ GIÁM ĐỐC", "PHÓ GIÁM ĐỐC", "l6", "signer-1"), Field("signing_authority", "KT. GIÁM ĐỐC", "KT. GIÁM ĐỐC", "l5", "signer-1") }, requested_image_pages = new int[0], issues = new string[0] };
    }
    class Fake : ILlmExtractionProvider {
        public int Calls; public Func<LlmRequest, int, string> Respond; public bool Delay;
        public async Task<LlmProviderReply> GenerateAsync(LlmRequest request, LlmExtractionOptions options, CancellationToken token) {
            Calls++; if (Delay) await Task.Delay(30000, token);
            return new LlmProviderReply { Json = Respond(request, Calls), Model = "test-model-version", InputTokens = 100, OutputTokens = 50 };
        }
    }
    class Handler : HttpMessageHandler {
        public int Calls; public string Url, Body; public Dictionary<string, string> Headers; public Func<string, HttpResponseMessage> Response;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Calls++;
            Url = request.RequestUri.AbsoluteUri; Headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)); Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            return Response(Body);
        }
    }
    static HttpResponseMessage HttpJson(object value) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.Serialize(value), Encoding.UTF8, "application/json") }; }
    static string Json(LlmResponseData data) { return json.Serialize(data); }
    static void Extract(AdministrativeDocument d, LlmExtractionOptions o, Fake fake) { using (var service = new LlmExtractionService(fake)) service.ExtractAsync(d, o, CancellationToken.None).GetAwaiter().GetResult(); }
    static void Pump(Task task) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); if (watch.Elapsed.TotalSeconds > 60) throw new Exception("UI timeout"); } task.GetAwaiter().GetResult(); Application.DoEvents(); }
    [STAThread] static void Main(string[] args) { try { Run(args); } catch (Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } }
    static void Run(string[] args) {
        root = Path.Combine(args[0], "llm-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var d = Doc("simple", 2); var o = Options(); var fake = new Fake { Respond = (q, n) => Json(Reply(2)) };
        Check(F(d, "Cơ quan ban hành").Value == null && F(d, "Ngày ban hành").Value == null, "loading OCR never executes old rule extraction");
        Extract(d, o, fake);
        Check(d.LlmStatus == "completed" && fake.Calls == 1, "LLM extraction completed");
        Check(F(d, "Cơ quan ban hành").Value == "SỞ NỘI VỤ" && F(d, "Số văn bản").Value == "003", "mapped metadata / leading zeros");
        Check(F(d, "Ngày ban hành").Value == "2026-10-12" && F(d, "Ngày ban hành").Evidence[0].Id == "l2", "date normalization and evidence");
        Check(d.Signers.Count == 1 && F(d, "Người ký").Value == "Nguyễn Văn An" && F(d, "Chức vụ người ký").Value == "PHÓ GIÁM ĐỐC", "signer pairing");
        Check(!F(d, "Người ký").Confirmed && d.Pages[0].HandwrittenNumber == "003" && d.Pages[0].NumberConfirmed, "LLM does not confirm or change locked numbers");
        Check(d.InputTokens == 100 && d.OutputTokens == 50 && d.AnalyzedPages.SequenceEqual(new[] { 1, 2 }), "usage/coverage");
        var noConfig = Doc("no-config", 2); var missingOptions = Options(); missingOptions.Model = ""; int before = fake.Calls; Extract(noConfig, missingOptions, fake);
        Check(noConfig.LlmStatus == "not_configured" && fake.Calls == before && F(noConfig, "Số, ký hiệu").Value == null, "unconfigured is not silently replaced by rules");
        var badSource = Reply(2); badSource.fields[0].evidence_ids = new[] { "invented-line" }; var bad = Doc("bad-evidence", 2);
        var badFake = new Fake { Respond = (q, n) => Json(badSource) }; Extract(bad, o, badFake);
        Check(bad.LlmStatus == "failed" && badFake.Calls == 2 && F(bad, "Cơ quan ban hành").Value == null, "fabricated evidence rejected, one repair only");
        var badSchema = new Fake { Respond = (q, n) => "{\"fields\":[]}" }; Extract(bad, o, badSchema);
        Check(bad.LlmStatus == "failed" && badSchema.Calls == 2, "schema mismatch rejected");
        var inventedValue = Reply(2); inventedValue.fields[0].value = "CƠ QUAN KHÔNG CÓ TRONG NGUỒN";
        Extract(bad, o, new Fake { Respond = (q, n) => Json(inventedValue) });
        Check(F(bad, "Cơ quan ban hành").Value == null && F(bad, "Cơ quan ban hành").RequiresReview, "hallucinated value not accepted from valid line ID");
        var protectedNumber = Reply(2); protectedNumber.fields[0].key = "handwritten_pages";
        Extract(bad, o, new Fake { Respond = (q, n) => Json(protectedNumber) }); Check(bad.LlmStatus == "failed" && bad.Pages[0].HandwrittenNumber == "003", "protected field cannot be assigned by model");
        var invalidDate = Reply(2); invalidDate.fields[3].value = "2026-02-31";
        Extract(bad, o, new Fake { Respond = (q, n) => Json(invalidDate) }); Check(F(bad, "Ngày ban hành").Value == null, "invalid calendar date rejected");
        ExtractionReview.Update(d, F(d, "Người ký"), "Nguyễn Văn Anh", true);
        Extract(d, o, fake); Check(F(d, "Người ký").Value == "Nguyễn Văn Anh" && F(d, "Người ký").Confirmed && F(d, "Người ký").OriginalValue == "Nguyễn Văn An", "confirmed human edit survives rerun");
        var extraSigner = Clone(d);
        extraSigner.Fields.Add(new ExtractedField { Name = "Người ký #2", Key = "signer_name" });
        extraSigner.Signers.Add(new SignatureBlock { Page = 2, NameField = "Người ký #2", TitleField = "Chức vụ người ký #2", AuthorityField = "Quyền hạn ký #2" });
        ExtractionReview.Update(extraSigner, F(extraSigner, "Người ký #2"), "Trần Thị Bình", true);
        Extract(extraSigner, o, fake);
        Check(F(extraSigner, "Người ký #2").Value == "Trần Thị Bình" && extraSigner.Signers.Count == 2, "manual second signer survives model omission");
        d.OcrFingerprint = "changed"; var prior = Clone(d); prior.OcrFingerprint = "old"; ExtractionReview.Preserve(prior, d);
        Check(!F(d, "Người ký").Confirmed && F(d, "Người ký").History.Count > 0, "changed source invalidates confirmation");
        var multi = Doc("multi", 2); var multiData = Reply(2);
        multiData.document_groups = new[] { new LlmDocumentGroup { id = "doc-1", start_page = 1, end_page = 1, is_primary = true }, new LlmDocumentGroup { id = "doc-2", start_page = 2, end_page = 2 } };
        foreach (var f in multiData.fields.Where(f => f.signer_id != null)) f.group_id = "doc-2";
        Extract(multi, o, new Fake { Respond = (q, n) => Json(multiData) });
        Check(multi.MultipleDocuments && F(multi, "Người ký").Value == null && F(multi, "Cơ quan ban hành").Value == "SỞ NỘI VỤ", "never mix primary issuer with attached document signer");
        foreach (var g in multiData.document_groups) g.is_primary = false;
        Extract(multi, o, new Fake { Respond = (q, n) => Json(multiData) }); Check(multi.LlmStatus == "needs_review", "ambiguous primary stays reviewable");
        var scoped = Doc("scope", 2); ExtractionReview.Update(scoped, F(scoped, "Người ký"), "Tên văn bản cũ", true);
        PostOcrExtractionService.Reextract(scoped, 2, 2);
        Check(F(scoped, "Người ký").Value == null && F(scoped, "Người ký").History.Count > 0, "changing primary range archives old manual data");
        var cacheDoc = Doc("cache", 2); var cacheOptions = Options(); cacheOptions.UseCache = true;
        var cachedFake = new Fake { Respond = (q, n) => Json(Reply(2)) }; Extract(cacheDoc, cacheOptions, cachedFake); Extract(cacheDoc, cacheOptions, cachedFake);
        Check(cachedFake.Calls == 1 && cacheDoc.LlmRequestCount == 0 && F(cacheDoc, "Số văn bản").Value == "003", "same source and configuration uses validated cache");
        cacheOptions.Model = "different-version"; Extract(cacheDoc, cacheOptions, cachedFake); Check(cachedFake.Calls == 2, "model change invalidates cache");
        var imageDoc = Doc("images", 2); var imageOptions = Options(); imageOptions.ImageMode = "ImagesOnDemand"; imageOptions.SupportsVision = true;
        var imageFake = new Fake { Respond = (q, n) => { var r = Reply(2); if (n == 1) r.requested_image_pages = new[] { 1 }; else { Check(q.Images.Count == 1 && q.Images[0].Base64.StartsWith("iVBOR"), "vision request contains rendered source image"); r.fields[0].value = r.fields[0].raw_value = "SỞ NỘI VỤ ĐỌC TỪ ẢNH"; r.fields[0].evidence_ids = new[] { "image:p1" }; } return Json(r); } };
        Extract(imageDoc, imageOptions, imageFake);
        Check(imageDoc.LlmStatus == "completed" && F(imageDoc, "Cơ quan ban hành").Evidence[0].SourceKind == "image" && F(imageDoc, "Cơ quan ban hành").RequiresReview, "vision reading separate from OCR and unconfirmed");
        var imageBudget = Options(); imageBudget.ImageMode = "ImagesOnDemand"; imageBudget.SupportsVision = true; imageBudget.MaxImages = 0;
        var imageLimited = Doc("image-budget", 2);
        Extract(imageLimited, imageBudget, new Fake { Respond = (q, n) => { var r = Reply(2); r.requested_image_pages = new[] { 1 }; return Json(r); } });
        Check(imageLimited.LlmStatus == "failed" && imageLimited.LlmRequestCount == 1, "image budget cannot silently skip requested source");
        var retryDoc = Doc("retry", 2); var retry = new Fake { Respond = (q, n) => { if (n == 1) throw new LlmApiException("temporary", true, TimeSpan.Zero); return Json(Reply(2)); } };
        Extract(retryDoc, o, retry); Check(retryDoc.LlmStatus == "completed" && retry.Calls == 2 && retryDoc.InputTokens == null, "bounded transient retry / unknown failed-call usage");
        var noRetry = new Fake { Respond = (q, n) => { throw new LlmApiException("invalid model/schema", false); } };
        Extract(retryDoc, o, noRetry); Check(retryDoc.LlmStatus == "failed" && noRetry.Calls == 1, "configuration error is not retried");
        var longDoc = Doc("long", 20); longDoc.Lines.Clear();
        for (int p = 1; p <= 20; p++) for (int l = 0; l < 3; l++) longDoc.Lines.Add(new EvidenceLine { Id = p + ":" + l, Page = p, Text = "UNIT " + new string('x', 300), RawText = "UNIT " + new string('x', 300), HasGeometry = false });
        var longOptions = Options(); longOptions.ContextTokens = 8192; longOptions.OutputTokens = 1024; longOptions.MaxRequests = 100;
        var longFake = new Fake { Respond = (q, n) => { string id = q.AllowedEvidenceIds.First(); var r = Reply(20); r.fields = new[] { Field("issuer", "UNIT", "UNIT", id, null) }; return Json(r); } };
        Extract(longDoc, longOptions, longFake);
        Check(longDoc.LlmStatus == "completed" && longFake.Calls > 2 && longDoc.AnalyzedPages.Count == 20, "chunk/reduce covers every page within context");
        longOptions.MaxRequests = 1; var limited = Clone(longDoc); limited.Fields.Clear(); ExtractionMetadata.Initialize(limited); Extract(limited, longOptions, longFake);
        Check(limited.LlmStatus == "failed" && limited.LlmRequestCount == 1, "request budget cannot report partial document success");
        var cancelled = Doc("cancel", 2); ExtractionReview.Update(cancelled, F(cancelled, "Người ký"), "Giữ bản sửa", true);
        using (var cts = new CancellationTokenSource()) using (var service = new LlmExtractionService(new Fake { Delay = true, Respond = (q, n) => Json(Reply(2)) })) {
            cts.CancelAfter(50); bool caught = false; try { service.ExtractAsync(cancelled, o, cts.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { caught = true; }
            Check(caught && cancelled.LlmStatus == "cancelled" && F(cancelled, "Người ký").Value == "Giữ bản sửa", "cancel retains manual values");
        }
        var settings = Options(); settings.GeminiApiKey = "FAKE-GEMINI-SECRET"; settings.LocalApiKey = "FAKE-LOCAL-SECRET";
        string settingsPath = Path.Combine(root, "settings.json"); LlmSettingsStore.Save(settings, settingsPath); var restored = LlmSettingsStore.Load(settingsPath);
        Check(!File.ReadAllText(settingsPath).Contains("FAKE-") && restored.GeminiApiKey == settings.GeminiApiKey && restored.LocalApiKey == settings.LocalApiKey, "DPAPI settings preserve keys without plaintext");
        Check(!json.Serialize(settings).Contains("FAKE-"), "keys excluded from options JSON");
        TransportTests(settings);
        MarkdownTests();
        OllamaModelTests();
        string report = Path.Combine(root, "report.xlsx"); ExtractionExportService.SaveExcel(report, new[] { d, noConfig, multi });
        using (var workbook = new ExcelPackage(new FileInfo(report))) {
            var sheet = workbook.Workbook.Worksheets["Báo cáo"];
            Check(workbook.Workbook.Worksheets.Count == 5 && sheet.Dimension.End.Row == 4, "five sheets / one row per PDF including pending LLM");
            Check(sheet.Cells[2, 12].Value as string == "003" && sheet.Cells[2, 10].Style.Numberformat.Format == "dd/mm/yyyy", "Excel text number and typed date");
            Check(sheet.Cells[2, 30].Value as string == "Ollama" && sheet.Cells[2, 31].Value as string == "test-model-version", "Excel separates provider and actual model");
            Check(workbook.Workbook.Worksheets["Bằng chứng"].Dimension.End.Row > 20, "Excel evidence and history");
        }
        byte[] originalReport = File.ReadAllBytes(report); bool locked = false;
        using (var file = new FileStream(report, FileMode.Open, FileAccess.Read, FileShare.None)) {
            try { ExtractionExportService.SaveExcel(report, new[] { d }); } catch (IOException) { locked = true; }
        }
        Check(locked && originalReport.SequenceEqual(File.ReadAllBytes(report)), "locked Excel keeps old file");
        var literal = Clone(d); ExtractionReview.Update(literal, F(literal, "Trích yếu"), "=HYPERLINK(\"invalid\")", false);
        string literalPath = Path.Combine(root, "literal.xlsx"); ExtractionExportService.SaveExcel(literalPath, new[] { literal });
        using (var workbook = new ExcelPackage(new FileInfo(literalPath))) Check(workbook.Workbook.Worksheets["Báo cáo"].Cells[2,16].Formula == "" && workbook.Workbook.Worksheets["Báo cáo"].Cells[2,16].Value as string == "=HYPERLINK(\"invalid\")", "OCR formula text stays literal");
        ExtractionReview.Update(literal, F(literal, "Trích yếu"), new string('x', 32768), false); bool tooLong = false;
        try { ExtractionExportService.SaveExcel(report, new[] { literal }); } catch (InvalidDataException) { tooLong = true; }
        Check(tooLong && originalReport.SequenceEqual(File.ReadAllBytes(report)), "oversize Excel cell fails without losing old report");
        string saved = Path.Combine(root, "result.json"); ExtractionExportService.SaveJson(saved, new[] { d }); var loaded = ExtractionExportService.LoadJson(saved)[0];
        Check(loaded.ExtractionProvider == "Ollama" && F(loaded, "Người ký").HasManualValue, "metadata reload retains provenance/manual state");
        var request = new LlmRequest { Text = "test", Instructions = "return JSON", Schema = LlmExtractionService.Schema() };
        var noFallback = Options(); noFallback.LocalBaseUrl = "http://not-local.example";
        Check(noFallback.ConfigurationIssue() != null, "non-loopback HTTP requires explicit configuration");
        var jsonInput = WriteOcrInput(Doc("ui", 2));
        var parsedInput = PostOcrExtractionService.FromJson(jsonInput, CancellationToken.None);
        Check(parsedInput.SourceVerified && parsedInput.Lines.All(l => l.HasGeometry) && parsedInput.Lines.Count == 8 && parsedInput.LlmStatus == "pending", "JSON adapter preserves lines and verified source without semantics");
        string plainPath = Path.Combine(root, "input.md"); File.WriteAllText(plainPath, "Số: 003/QĐ-SNV\nNguyễn Văn An");
        var plainInput = PostOcrExtractionService.FromText(plainPath, d.Source, CancellationToken.None);
        Check(!plainInput.SourceVerified && plainInput.Lines.All(l => !l.HasGeometry) && F(plainInput, "Số văn bản").Value == null, "text adapter never fabricates geometry or rule-selected data");
        using (var form = new frmTrichXuatVanBan()) {
            form.LlmOptions = new LlmExtractionOptions { Model = "" }; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.Show(); SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Pump(form.RunOcrImportAsync(jsonInput, false, Path.Combine(root, "ui-results.json")));
            var records = (List<AdministrativeDocument>)typeof(frmTrichXuatVanBan).GetField("results", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            Check(records.Count == 1 && records[0].LlmStatus == "not_configured" && F(records[0], "Cơ quan ban hành").Value == null, "UI imports OCR, waits for model, no legacy fallback");
            string uiReply = Json(Reply(2)).Replace("l0", "1:1").Replace("l1", "1:2").Replace("l2", "1:3").Replace("l3", "1:4").Replace("l4", "1:5").Replace("l5", "2:1").Replace("l6", "2:2").Replace("l7", "2:3");
            var uiHandler = new Handler { Response = s => HttpJson(new { model = "ui-mock", done = true, done_reason = "stop", message = new { content = uiReply } }) };
            using (var uiProvider = new HttpLlmExtractionProvider(uiHandler)) {
                form.LlmOptions = Options(); form.LlmServiceFactory = () => new LlmExtractionService(uiProvider);
                Pump(form.RunSelectedLlmAsync());
                Check(records[0].LlmStatus == "completed" && F(records[0], "Số văn bản").Value == "003" && !DocumentJobCoordinator.Shared.IsBusy && uiHandler.Body.Contains("1:1"), "UI end-to-end through injected HTTP mock");
                records[0].PrimaryRangeConfirmed = true;
                Pump(form.RunOcrImportAsync(jsonInput, false, Path.Combine(root, "ui-results.json")));
                Check(records.Count == 1 && records[0].LlmStatus == "completed" && F(records[0], "Số văn bản").Value == "003", "repeat import preserves scope without resetting fresh LLM result");
            }
            using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "llm-review.png")); }
        }
        using (var form = new LlmSettingsForm(new LlmExtractionOptions())) { form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.Show(); Application.DoEvents(); using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "llm-settings.png")); } }
        ConnectionUiTests();
        SynchronizationContext.SetSynchronizationContext(null);
        OcrIntegration(args);
        Check(!DocumentJobCoordinator.Shared.IsBusy, "all UI jobs release coordinator");
        File.WriteAllText(Path.Combine(root, "verification.txt"), checks + " mock/integration assertions passed. No real LLM installed/called. No accuracy benchmark."); Console.WriteLine("PASS " + checks + " assertions; " + root);
    }
    class CancelLlmStage : IProgress<VietOcrProgress> {
        public CancellationTokenSource Cancellation;
        public void Report(VietOcrProgress p) { if (p.Status == "Bóc tách thông tin văn bản") Cancellation.Cancel(); }
    }
    static void OcrIntegration(string[] args) {
        var input = Doc("ocr-input", 2);
        var options = new VietOcrOptions { PythonPath = args[1], WorkerPath = args[2], WriteMarkdown = true, WriteJson = false, ExtractMetadata = true, LlmOptions = new LlmExtractionOptions { Model = "" } };
        var result = new VietOcrService().ProcessAsync(input.Source, Path.Combine(root, "ocr-output"), options, CancellationToken.None).GetAwaiter().GetResult()[0];
        Check(result.CompletedPages == 2 && File.Exists(result.MarkdownPath) && File.Exists(result.ExtractionPath) && result.Extraction.LlmStatus == "not_configured", "automatic after-OCR persists content when model unconfigured");
        var stored = ExtractionExportService.LoadJson(result.ExtractionPath); ExtractionReview.Update(stored[0], F(stored[0], "Người ký"), "Bản sửa giữ lại", true);
        ExtractionExportService.SaveJson(result.ExtractionPath, stored); result.Dispose(); options.Overwrite = true;
        var again = new VietOcrService().ProcessAsync(input.Source, Path.Combine(root, "ocr-output"), options, CancellationToken.None).GetAwaiter().GetResult()[0];
        Check(F(again.Extraction, "Người ký").Value == "Bản sửa giữ lại" && F(again.Extraction, "Người ký").Confirmed, "OCR overwrite retains saved manual metadata"); again.Dispose();
        using (var cancellation = new CancellationTokenSource()) {
            bool kept = false;
            try { new VietOcrService().ProcessAsync(input.Source, Path.Combine(root, "cancel-after-ocr"), options, cancellation.Token, new CancelLlmStage { Cancellation = cancellation }).GetAwaiter().GetResult(); }
            catch (VietOcrCanceledException ex) { kept = ex.Results.Count == 1 && ex.Results[0].Status == "OCR hoàn tất; đã hủy LLM" && File.Exists(ex.Results[0].MarkdownPath); foreach (var r in ex.Results) r.Dispose(); }
            Check(kept, "cancel extraction stage keeps completed OCR output");
        }
        File.AppendAllText(input.Source, "\n%changed"); PostOcrExtractionService.ValidateCurrentSource(input);
        Check(!input.SourceVerified && input.Warnings.Any(w => w.StartsWith("Nguồn thay đổi")), "changed PDF invalidates source before sending vision");
        var pipelineSource = Doc("ollama-pipeline", 2); options.Overwrite = false; options.WriteMarkdown = false; options.WriteJson = true; options.LlmOptions = Options();
        var handler = new Handler { Response = body => {
            string path = Path.Combine(root, "ollama-output", Path.GetFileName(pipelineSource.Source) + ".ocr.md");
            Check(File.Exists(path) && File.ReadAllText(path).Contains("Nội dung tiếng Việt trang 2"), "Markdown committed before Ollama request");
            var requestBody = json.Deserialize<Dictionary<string, object>>(body);
            var messages = ((System.Collections.IEnumerable)requestBody["messages"]).Cast<Dictionary<string, object>>().ToArray();
            string text = (string)messages[1]["content"];
            Check(text.Contains("# Nội dung OCR") && text.Contains("[1:1] Nội dung tiếng Việt trang 1") && !text.Contains("\"text\":"), "real OCR pipeline sends annotated Markdown to native Ollama");
            var reply = Reply(2); reply.fields = new[] { Field("subject", "Nội dung tiếng Việt trang 1", "Nội dung tiếng Việt trang 1", "1:1", null) };
            return HttpJson(new { model = "qwen3.5:4b", done = true, done_reason = "stop", message = new { content = Json(reply) } });
        } };
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            var service = new VietOcrService { LlmServiceFactory = () => new LlmExtractionService(provider) };
            var completed = service.ProcessAsync(pipelineSource.Source, Path.Combine(root, "ollama-output"), options, CancellationToken.None).GetAwaiter().GetResult()[0];
            Check(completed.Extraction.LlmStatus == "completed" && completed.MarkdownPath != null && completed.Extraction.MarkdownPath == completed.MarkdownPath && F(completed.Extraction, "Trích yếu").Value == "Nội dung tiếng Việt trang 1", "PDF to saved Markdown to Ollama to validated metadata");
            string excel = Path.Combine(root, "ollama-pipeline.xlsx"); ExtractionExportService.SaveExcel(excel, new[] { completed.Extraction });
            using (var workbook = new ExcelPackage(new FileInfo(excel))) Check(workbook.Workbook.Worksheets["Báo cáo"].Dimension.End.Row == 2 && workbook.Workbook.Worksheets.Count == 5, "pipeline exports five sheets and one PDF row");
            completed.Dispose();
        }
    }
    static void MarkdownTests() {
        var doc = Doc("markdown-source", 2); string path = doc.Source + ".ocr.md";
        var content = new StringBuilder("# OCR: Scan2026-03-10.pdf\n\nTổng số trang: 2\n\n");
        foreach (var page in doc.Lines.GroupBy(l => l.Page)) {
            content.Append("## Trang " + page.Key + " / 2\n\n**Trạng thái:** Cần kiểm tra\n\n**Số trang ghi tay:** 003\n\n");
            foreach (var line in page) content.AppendLine(line.RawText.Replace("-", "\\-") + "  ");
        }
        File.WriteAllText(path, content.ToString(), new UTF8Encoding(false)); doc.MarkdownPath = path;
        var options = Options(); options.UseCache = true;
        var fake = new Fake { Respond = (q, n) => {
            Check(q.Text.Contains("[l1] Số: 003/QĐ\\-SNV") && !q.Text.Contains("Scan2026-03-10") && !q.Text.Contains("Cần kiểm tra"), "Markdown content carries IDs and omits export metadata/filename");
            Check(!q.Instructions.Contains("Nguyễn Văn An") && q.Instructions.Contains("người xác nhận"), "prompt has form-role guidance without sample names or dates");
            return Json(Reply(2));
        } };
        Extract(doc, options, fake);
        Check(doc.LlmStatus == "completed" && doc.Lines.Count == 8 && doc.Lines.All(l => l.HasGeometry) && F(doc, "Số, ký hiệu").Value == "003/QĐ-SNV", "escaped Markdown maps back to original geometry and literal values");
        Extract(doc, options, fake); Check(fake.Calls == 1, "unchanged saved Markdown reuses validated cache");
        ExtractionReview.Update(doc, F(doc, "Cơ quan ban hành"), "SỞ NỘI VỤ", true);
        File.WriteAllText(path, content.ToString().Replace("SỞ NỘI VỤ", "CƠ QUAN A &amp; B\\_1"), new UTF8Encoding(false));
        var changed = new Fake { Respond = (q, n) => {
            var response = Reply(2); response.fields = new[] { Field("issuer", "CƠ QUAN A & B_1", "CƠ QUAN A & B_1", q.AllowedEvidenceIds.First(id => id.Contains(":md:")), null) }; return Json(response);
        } };
        Extract(doc, options, changed);
        Check(changed.Calls == 1 && doc.LlmStatus == "completed" && !F(doc, "Cơ quan ban hành").Confirmed && doc.Lines.Any(l => l.RawText == "CƠ QUAN A & B_1" && !l.HasGeometry), "changed Markdown invalidates cache/confirmation and never reuses a different text box");
        options.UseCache = false;
        var modifiedDuringCall = new Fake { Respond = (q, n) => { File.AppendAllText(path, "\nThêm nội dung\n"); var reply = Reply(2); reply.fields = new LlmFieldData[0]; return Json(reply); } };
        Extract(doc, options, modifiedDuringCall);
        Check(doc.LlmStatus == "failed" && doc.LlmError.Contains("Markdown thay đổi"), "Markdown changed during inference cannot produce a completed result");
        File.Delete(path); var unavailable = new Fake { Respond = (q, n) => Json(Reply(2)) }; Extract(doc, options, unavailable);
        Check(doc.LlmStatus == "failed" && unavailable.Calls == 0, "missing intermediate Markdown cannot silently use stale OCR text");
        string invalid = Path.Combine(root, "invalid-page.md"); File.WriteAllText(invalid, "## Trang 3 / 2\nSai trang\n");
        bool rejected = false; try { PostOcrExtractionService.FromText(invalid, doc.Source, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Markdown page markers must agree with the source PDF");
    }
    static void OllamaModelTests() {
        var options = new LlmExtractionOptions();
        Check(options.Provider == "Ollama" && options.Model == "qwen3.5:4b" && options.ImageMode == "TextOnly" && !options.OllamaThink, "default workflow uses local qwen3.5:4b Markdown without thinking");
        var handler = new Handler { Response = s => HttpJson(new { models = new[] { new { name = "qwen3.5:4b", remote_host = "" }, new { name = "remote:cloud", remote_host = "" }, new { name = "remote-model", remote_host = "https://example.invalid" } } }) };
        options.GeminiApiKey = "FAKE-CLOUD-KEY";
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            string[] models = provider.ListOllamaModelsAsync(options, CancellationToken.None).GetAwaiter().GetResult();
            Check(handler.Url == "http://localhost:11434/api/tags" && models.SequenceEqual(new[] { "qwen3.5:4b" }) && !handler.Headers.ContainsKey("x-goog-api-key"), "Ollama discovery uses native local API and excludes cloud models");
        }
        handler = new Handler { Response = s => HttpJson(new { model = "qwen3.5:4b", done = true, done_reason = "stop", message = new { content = "{}" } }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            provider.GenerateAsync(new LlmRequest { Text = "# Markdown", Instructions = "JSON", Schema = LlmExtractionService.Schema() }, options, CancellationToken.None).GetAwaiter().GetResult();
            var body = json.Deserialize<Dictionary<string, object>>(handler.Body);
            Check(Equals(body["think"], false) && Equals(body["stream"], false) && body["format"] is Dictionary<string, object>, "Qwen Ollama call disables thinking and requires the JSON schema");
        }
        using (var form = new LlmSettingsForm(options)) {
            var modelHandler = new Handler { Response = s => HttpJson(new { models = new[] { new { name = "qwen3.5:4b" } } }) };
            form.ConnectionFactory = () => new HttpLlmExtractionProvider(modelHandler); Pump(form.LoadOllamaModelsAsync());
            var combo = (ComboBox)typeof(LlmSettingsForm).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            Check(combo.Items.Count == 1 && combo.Text == "qwen3.5:4b", "settings loads installed Ollama tags and retains the selected model");
        }
        options.LocalBaseUrl = "http://localhost:11434/v1";
        Check(options.ConfigurationIssue() != null, "native Ollama settings reject OpenAI-style endpoint suffix");
    }
    static void TransportTests(LlmExtractionOptions keys) {
        string output = Json(Reply(2)); var request = new LlmRequest { Text = "fixture", Instructions = "schema", Schema = LlmExtractionService.Schema() };
        request.Images.Add(new LlmImageInput { Id = "image:p1", Page = 1, Base64 = "AA==" });
        var gemini = keys.Copy(); gemini.Provider = "Gemini"; gemini.Model = "test-gemini";
        var handler = new Handler { Response = s => HttpJson(new { model = "gemini-pinned", status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = output } } } }, usage = new { total_input_tokens = 11, total_output_tokens = 22 } }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            var reply = provider.GenerateAsync(request, gemini, CancellationToken.None).GetAwaiter().GetResult();
            Check(handler.Url == "https://generativelanguage.googleapis.com/v1beta/interactions" && handler.Headers["x-goog-api-key"] == keys.GeminiApiKey && !handler.Url.Contains(keys.GeminiApiKey), "Gemini endpoint/header auth");
            var body = json.Deserialize<Dictionary<string, object>>(handler.Body);
            Check(Equals(body["store"], false) && body.ContainsKey("response_format") && handler.Body.Contains("application/json") && !handler.Body.Contains("tools"), "Gemini stateless schema / no external tools");
            var generation = (Dictionary<string, object>)body["generation_config"];
            Check(!generation.ContainsKey("temperature") && generation.ContainsKey("max_output_tokens"), "Gemini Interactions generation config uses documented parameters");
            Check(reply.Json == output && reply.InputTokens == 11 && reply.OutputTokens == 22 && handler.Body.Contains("image:p1"), "Gemini output, usage, image association");
        }
        var local = keys.Copy(); local.Provider = "Ollama";
        handler = new Handler { Response = s => HttpJson(new { model = "local-model", done = true, done_reason = "stop", message = new { content = output }, prompt_eval_count = 3, eval_count = 4 }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            var reply = provider.GenerateAsync(request, local, CancellationToken.None).GetAwaiter().GetResult();
            Check(handler.Url.StartsWith("http://localhost:11434/api/chat") && !handler.Headers.ContainsKey("x-goog-api-key") && handler.Headers["Authorization"] == "Bearer " + keys.LocalApiKey, "local never receives Gemini key");
            Check(handler.Body.Contains("\"format\"") && handler.Body.Contains("num_ctx") && handler.Body.Contains("\"images\"") && reply.InputTokens == 3, "Ollama schema/options/images/usage");
        }
        local.Provider = "CompatibleLocal"; local.LocalBaseUrl = "http://localhost:1234/v1";
        handler = new Handler { Response = s => HttpJson(new { model = "compat-model", choices = new[] { new { finish_reason = "stop", message = new { content = output } } }, usage = new { prompt_tokens = 5, completion_tokens = 6 } }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            var reply = provider.GenerateAsync(request, local, CancellationToken.None).GetAwaiter().GetResult();
            Check(handler.Url == "http://localhost:1234/v1/chat/completions" && handler.Body.Contains("json_schema") && handler.Body.Contains("image_url") && reply.OutputTokens == 6, "compatible local protocol tested independently");
        }
        handler = new Handler { Response = s => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("unsupported schema") } };
        using (var provider = new HttpLlmExtractionProvider(handler)) { bool bad = false; try { provider.GenerateAsync(request, local, CancellationToken.None).GetAwaiter().GetResult(); } catch (LlmApiException ex) { bad = !ex.Retryable; } Check(bad, "unsupported schema is a visible error, no silent JSON downgrade"); }
        handler = new Handler { Response = s => HttpJson(new { model = "local", done = true, done_reason = "length", message = new { content = "{\"truncated\":" } }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) { bool cut = false; local.Provider = "Ollama"; try { provider.GenerateAsync(request, local, CancellationToken.None).GetAwaiter().GetResult(); } catch (LlmApiException) { cut = true; } Check(cut, "partial model output rejected"); }
        handler = new Handler { Response = s => HttpError(503, new { error = new { status = "UNAVAILABLE", message = "gemini-3.8-flash is currently experiencing high demand" } }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            bool explained = false; try { provider.GenerateAsync(request, gemini, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (LlmApiException ex) { explained = ex.HttpStatus == 503 && ex.ErrorCode == "UNAVAILABLE" && ex.Retryable && ex.Message.Contains("quá tải") && ex.Message.Contains("high demand"); }
            Check(explained, "Gemini 503 identifies model overload and preserves safe server reason");
        }
        handler = new Handler { Response = s => HttpError(403, new { error = new { status = "PERMISSION_DENIED", message = "Rejected key " + keys.GeminiApiKey + " and token " + keys.LocalApiKey } }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) {
            bool hidden = false; try { provider.GenerateAsync(request, gemini, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (LlmApiException ex) { hidden = ex.HttpStatus == 403 && !ex.Retryable && ex.Message.Contains("PERMISSION_DENIED") && !ex.Message.Contains(keys.GeminiApiKey) && !ex.Message.Contains(keys.LocalApiKey); }
            Check(hidden, "Gemini authorization reason visible without echoing either key");
        }
        handler = new Handler { Response = s => HttpError(503, new { error = new { code = 503, message = "flat high demand" } }) };
        using (var provider = new HttpLlmExtractionProvider(handler)) { bool explained = false; try { provider.GenerateAsync(request, gemini, CancellationToken.None).GetAwaiter().GetResult(); } catch (LlmApiException ex) { explained = ex.Message.Contains("flat high demand"); } Check(explained, "Google error without status remains diagnostic"); }
        handler = new Handler { Response = s => { throw new HttpRequestException("request failed", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound)); } };
        using (var provider = new HttpLlmExtractionProvider(handler)) { bool dns = false; try { provider.GenerateAsync(request, gemini, CancellationToken.None).GetAwaiter().GetResult(); } catch (LlmApiException ex) { dns = ex.Message.Contains("DNS") && ex.Retryable; } Check(dns, "DNS failure distinguished from API key failure"); }
        Check(!LlmApiException.SafeMessage("AQ." + new string('x', 48) + " key=" + new string('y', 25), keys).Contains(new string('x', 48)), "authorization-style keys masked in diagnostic text");
    }
    static HttpResponseMessage HttpError(int code, object value) { var response = HttpJson(value); response.StatusCode = (HttpStatusCode)code; response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero); return response; }
    static void ConnectionUiTests() {
        var options = Options(); options.Provider = "Gemini"; options.Model = "gemini-3.8-flash"; options.GeminiApiKey = "FAKE-CONNECTION-KEY"; options.MaxAttempts = 3;
        var handler = new Handler { Response = s => HttpError(503, new { error = new { status = "UNAVAILABLE", message = "high demand" } }) };
        using (var form = new LlmSettingsForm(options)) {
            form.ConnectionFactory = () => new HttpLlmExtractionProvider(handler); form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.Show();
            Pump(form.CheckConnectionAsync());
            var statusBox = (TextBox)typeof(LlmSettingsForm).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            var testButton = (Button)typeof(LlmSettingsForm).GetField("test", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            Check(handler.Calls == 3 && statusBox.Text.Contains("503") && statusBox.Text.Contains("quá tải") && testButton.Enabled, "API test bounded retry preserves actionable 503 in UI");
            using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "gemini-overload.png")); }
        }
        handler = new Handler(); handler.Response = s => handler.Calls == 1 ? HttpError(503, new { error = new { message = "high demand" } }) : HttpJson(new { model = "gemini-3.8-flash", status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = "{\"status\":\"ok\"}" } } } } });
        using (var form = new LlmSettingsForm(options)) {
            form.ConnectionFactory = () => new HttpLlmExtractionProvider(handler); Pump(form.CheckConnectionAsync());
            var statusBox = (TextBox)typeof(LlmSettingsForm).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            Check(handler.Calls == 2 && statusBox.Text.Contains("thành công") && statusBox.Text.Contains("gemini-3.8-flash"), "API test recovers from overload using same key/model");
        }
        handler = new Handler { Response = s => HttpError(401, new { error = new { status = "UNAUTHENTICATED", message = "Invalid credentials" } }) };
        using (var form = new LlmSettingsForm(options)) { form.ConnectionFactory = () => new HttpLlmExtractionProvider(handler); Pump(form.CheckConnectionAsync()); Check(handler.Calls == 1, "API test never retries invalid credentials"); }
    }
    static string WriteOcrInput(AdministrativeDocument d) {
        string path = d.Source + ".ocr.json";
        var pages = Enumerable.Range(1, d.PageCount).Select(p => new { page = p, width_points = 595, height_points = 842, status = "completed", text = string.Join("\n", d.Lines.Where(l => l.Page == p).Select(l => l.Text)),
            lines = d.Lines.Where(l => l.Page == p).Select(l => new { text = l.Text, raw_text = l.RawText, confidence = .95, polygon_points = new[] { new[] { l.X * 595, l.Y * 842 }, new[] { (l.X + l.Width) * 595, l.Y * 842 }, new[] { (l.X + l.Width) * 595, (l.Y + l.Height) * 842 }, new[] { l.X * 595, (l.Y + l.Height) * 842 } } }) }).ToArray();
        File.WriteAllText(path, json.Serialize(new { schema_version = 1, source = d.Source, source_sha256 = d.Fingerprint, engine = "VietOCR", coordinate_origin = "top_left", rotation_clockwise = 0, page_count = d.PageCount, pages = pages }));
        return path;
    }
}
