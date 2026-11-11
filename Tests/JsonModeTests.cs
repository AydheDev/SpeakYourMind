using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpeakYourMind.Core;
using static SpeakYourMind.Tests.Suite;

namespace SpeakYourMind.Tests;

public static class JsonModeTests
{
    // Simulate the JSON-mode guard on input, independently of the instructions field.
    private sealed class JsonModeServer : HttpMessageHandler
    {
        public string Result = "", Input = "", Instructions = "";
        public int Rejected, Accepted;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = document.RootElement;
            Input = body.GetProperty("input").GetString()!;
            Instructions = body.GetProperty("instructions").GetString()!;
            if (body.GetProperty("text").GetProperty("format").GetProperty("type").GetString() == "json_object" && !Input.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                Rejected++;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":{"param":"input","code":null,"message":"Input messages must contain the word JSON to use json_object format."}}""")
                };
            }
            Accepted++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SYMJson.Write(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = Result } } } } }))
            };
        }
    }
    public static async Task Run()
    {
        var preview = Prompts.Preview(new Preferences());
        string context = SYMJson.Write(preview);
        Check(!context.Contains("json", StringComparison.OrdinalIgnoreCase), "actual preview context does not accidentally satisfy JSON-mode guard");
        var handler = new JsonModeServer();
        using var transport = new HttpClient(handler, false);
        using var oldRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:1234/v1/responses")
        {
            Content = new StringContent(SYMJson.Write(new { instructions = Prompts.ExpressionSystem, input = context, text = new { format = new { type = "json_object" } } }), Encoding.UTF8, "application/json")
        };
        using var rejected = await transport.SendAsync(oldRequest);
        Check(rejected.StatusCode == HttpStatusCode.BadRequest && handler.Rejected == 1, "previous preview shape reproduces input rejection in mocked guard");
        string error = ProviderClient.FriendlyError(rejected.StatusCode, await rejected.Content.ReadAsStringAsync());
        Check(error.Contains("prompt content (input)") && error.Contains("HTTP 400"), "input rejection retains exact known field");

        var config = new Connection { Model = "gpt-6.1-sol", BaseUrl = "http://localhost:1234/v1", Reasoning = "low" };
        using var client = new ProviderClient(handler);
        handler.Result = """{"turns":[{"pawnId":"preview","text":"That should hold."}]}""";
        string result = await client.Generate(config, "", Prompts.ExpressionSystem, context, CancellationToken.None);
        Check(Prompts.ParseExpression(result, preview).Turns[0].Text == "That should hold.", "fixed preview passes JSON-mode guard and dialogue parsing");
        Check(handler.Input.EndsWith(context, StringComparison.Ordinal) && handler.Instructions == Prompts.ExpressionSystem, "context preserved and trusted instructions remain separate");

        string profileContext = SYMJson.Write(new { pawn = preview.Pawns[0], memories = Array.Empty<Memory>(), maxCharacters = preview.MaxCharacters });
        handler.Result = """{"voice":"A patient carpenter.","scores":[],"fallbacks":["Hmm."]}""";
        result = await client.Generate(config, "", Prompts.ProfileSystem, profileContext, CancellationToken.None);
        Check(SYMJson.Read<ProfileResponse>(result).Voice == "A patient carpenter." && handler.Accepted == 2, "memory requests also pass guard and parse profile");
        Check(handler.Input.EndsWith(profileContext, StringComparison.Ordinal) && handler.Instructions == Prompts.ProfileSystem, "memory data and instructions preserved");
        using var payload = ProviderClient.BuildRequest(config, "", Prompts.ExpressionSystem, context);
        using var parsed = JsonDocument.Parse(await payload.Content!.ReadAsStringAsync());
        Check(parsed.RootElement.GetProperty("reasoning").GetProperty("effort").GetString() == "low", "JSON fix preserves selected low reasoning");
        Check(!parsed.RootElement.GetProperty("store").GetBoolean() && parsed.RootElement.GetProperty("max_output_tokens").GetInt32() == config.OutputTokens, "JSON fix preserves privacy and token allowance");
    }
}
