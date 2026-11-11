using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SpeakYourMind.Core;
using static SpeakYourMind.Tests.Suite;

namespace SpeakYourMind.Tests;

public static class ProviderErrorTests
{
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"param":"text.format","code":"unsupported_value","message":"private key and colony text"}}""")
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        }
    }
    public static async Task Run()
    {
        string Error(string body) => ProviderClient.FriendlyError(HttpStatusCode.BadRequest, body);
        string format = Error("""{"error":{"param":"text.format.type","code":"unsupported_value","message":"private API key and prompt"}}""");
        Check(format.Contains("output format") && format.Contains("unsupported_value") && format.Contains("HTTP 400"), "rejected format identifies field and code");
        Check(!format.Contains("private") && !format.Contains("prompt") && !format.Contains("Try disabling"), "server message hidden and no unverified reasoning advice");
        Check(Error("""{"error":{"param":"reasoning.effort","code":"unsupported_parameter"}}""").Contains("reasoning level"), "reasoning failures distinguished from format");
        Check(Error("""{"error":{"param":"max_output_tokens"}}""").Contains("output allowance"), "output budget error identifies user setting");
        foreach (string body in new[] { "not JSON private", "[]", "null", "{\"error\":null}", "{\"error\":\"private\"}", "{\"error\":{\"param\":{},\"code\":123}}", new string('x', 20000) })
            Check(Error(body) == ProviderClient.FriendlyError(HttpStatusCode.BadRequest), "malformed or non-object error safely uses HTTP fallback");
        Check(Error("""{"error":{"param":"secret-token","code":"private-account","type":"private","message":"private"}}""") == ProviderClient.FriendlyError(HttpStatusCode.BadRequest), "untrusted metadata cannot leak secrets");
        Check(ProviderClient.FriendlyError(HttpStatusCode.UnprocessableEntity).Contains("HTTP 422"), "generic rejection preserves status");
        using var client = new ProviderClient(new Handler());
        var connection = new Connection { Model = "test-model", BaseUrl = "http://localhost:1234/v1" };
        try { await client.Generate(connection, "", "system", "context", CancellationToken.None); throw new Exception("Expected rejection"); }
        catch (ProviderException e)
        {
            Check(e.Message.Contains("output format") && !e.Message.Contains("private"), "generation exposes safe error metadata");
            Check(e.RetryAfterSeconds == 30, "detailed errors preserve backoff");
        }
        try { await client.ListModels(connection, "", CancellationToken.None); throw new Exception("Expected rejection"); }
        catch (ProviderException e) { Check(e.Message.Contains("unsupported_value") && !e.Message.Contains("private"), "model listing also exposes safe error metadata"); }
    }
}
