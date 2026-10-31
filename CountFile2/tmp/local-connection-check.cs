using System;
using System.Threading;
using System.Web.Script.Serialization;
using CountFile2.Services;
class LocalConnectionCheck {
    static int Main() {
        var options = new LlmExtractionOptions { Provider = "CompatibleLocal", Model = "ggml-org/gemma-3-1b-it-qat-GGUF:Q4_0",
            LocalBaseUrl = "http://127.0.0.1:9931/v1", ContextTokens = 32768, TimeoutSeconds = 30, OutputTokens = 512, UseCache = false };
        var json = new JavaScriptSerializer();
        try {
            using (var provider = new HttpLlmExtractionProvider()) {
                var reply = provider.GenerateAsync(new LlmRequest { Instructions = "Return JSON only: {\"status\":\"ok\"}", Text = "Connection and JSON schema test.",
                    Schema = new { type = "object", properties = new { status = new { type = "string", @enum = new[] { "ok" } } }, required = new[] { "status" }, additionalProperties = false } }, options, CancellationToken.None).GetAwaiter().GetResult();
                var value = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(reply.Json);
                Console.WriteLine(json.Serialize(new { result = value.ContainsKey("status") && (string)value["status"] == "ok" ? "PASS" : "INVALID_JSON", provider = options.Provider, endpoint = options.LocalBaseUrl, model = reply.Model, input_tokens = reply.InputTokens, output_tokens = reply.OutputTokens }));
                return 0;
            }
        } catch (LlmApiException ex) { Console.WriteLine(json.Serialize(new { result = "FAILED", http_status = ex.HttpStatus, message = ex.Message })); return 1; }
        catch (Exception ex) { Console.WriteLine("CHECK FAILED: " + ex.GetType().Name); return 1; }
    }
}
