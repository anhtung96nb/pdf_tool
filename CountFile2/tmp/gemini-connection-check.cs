using System;
using System.Threading;
using System.Web.Script.Serialization;
using CountFile2.Services;
class GeminiConnectionCheck {
    static int Main(string[] args) {
        var options = new LlmExtractionOptions { Provider = "Gemini", Model = "gemini-3.8-flash", GeminiApiKey = Console.ReadLine(), TimeoutSeconds = 35, OutputTokens = 1024, UseCache = false };
        var json = new JavaScriptSerializer();
        try {
            using (var provider = new HttpLlmExtractionProvider()) {
                var reply = provider.GenerateAsync(new LlmRequest { Instructions = "Return JSON only: {\"status\":\"ok\"}", Text = "Connection and JSON schema test.",
                    Schema = new { type = "object", properties = new { status = new { type = "string", @enum = new[] { "ok" } } }, required = new[] { "status" }, additionalProperties = false } }, options, CancellationToken.None).GetAwaiter().GetResult();
                var value = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(reply.Json);
                Console.WriteLine(json.Serialize(new { result = value.ContainsKey("status") && (string)value["status"] == "ok" ? "PASS" : "INVALID_JSON", model = reply.Model, input_tokens = reply.InputTokens, output_tokens = reply.OutputTokens }));
                return 0;
            }
        } catch (LlmApiException ex) { Console.WriteLine(json.Serialize(new { result = "FAILED", http_status = ex.HttpStatus, code = ex.ErrorCode, message = LlmApiException.SafeMessage(ex.Message, options) })); return 1; }
        catch (Exception ex) { Console.WriteLine("CHECK FAILED: " + ex.GetType().Name); return 1; }
        finally { options.GeminiApiKey = null; }
    }
}
