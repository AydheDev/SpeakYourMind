using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpeakYourMind.Core;

public sealed class ProviderClient : IDisposable
{
    private readonly HttpClient http;
    public RequestMetrics Metrics { get; } = new();
    public ProviderClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.MaxResponseContentBufferSize = 256 * 1024;
    }
    public static Uri ValidateEndpoint(string url)
    {
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Use an HTTPS server address, or HTTP localhost for a local model. Do not put credentials in the address.");
        return uri;
    }
    public static HttpRequestMessage BuildRequest(Connection c, string key, string system, string context)
    {
        var root = ValidateConfiguration(c, key);
        int outputTokens = Math.Clamp(c.OutputTokens, 256, 16000);
        var body = new Dictionary<string, object>();
        string path;
        switch (c.Provider)
        {
            case ProviderKind.OpenAI:
                path = "responses";
                body["model"] = c.Model.Trim(); body["instructions"] = system;
                // JSON-mode validation must see an explicit JSON instruction in input,
                // even when our separate instructions already specify the response shape.
                body["input"] = "Return only a JSON object matching the supplied instructions.\nThe following is context data:\n" + context;
                body["store"] = false; body["max_output_tokens"] = outputTokens;
                if (c.SendReasoning && c.Reasoning != "provider default") body["reasoning"] = new { effort = c.Reasoning };
                body["text"] = new { format = new { type = "json_object" } };
                break;
            case ProviderKind.Claude:
                path = "messages";
                body["model"] = c.Model.Trim(); body["system"] = system; body["max_tokens"] = outputTokens;
                body["messages"] = new[] { new { role = "user", content = context } };
                break;
            case ProviderKind.Gemini:
                path = "models/" + Uri.EscapeDataString(c.Model.Trim()) + ":generateContent";
                body["systemInstruction"] = new { parts = new[] { new { text = system } } };
                body["contents"] = new[] { new { role = "user", parts = new[] { new { text = context } } } };
                body["generationConfig"] = new { maxOutputTokens = outputTokens, responseMimeType = "application/json" };
                break;
            default:
                path = "chat/completions";
                body["model"] = c.Model.Trim();
                body["messages"] = new[] { new { role = "system", content = system }, new { role = "user", content = context } };
                body["max_tokens"] = outputTokens; body["stream"] = false;
                if (c.SendReasoning && c.Reasoning != "provider default") body["reasoning_effort"] = c.Reasoning;
                break;
        }
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, path))
        { Content = new StringContent(SYMJson.Write(body), Encoding.UTF8, "application/json") };
        if (c.Provider == ProviderKind.Claude)
        {
            request.Headers.Add("x-api-key", key.Trim());
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else if (c.Provider == ProviderKind.Gemini) request.Headers.Add("x-goog-api-key", key.Trim());
        else if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        return request;
    }
    public static Uri ValidateConfiguration(Connection c, string key)
    {
        if (!Enum.IsDefined(c.Provider)) throw new ArgumentException("Choose a supported provider type.");
        if (string.IsNullOrWhiteSpace(c.BaseUrl)) throw new ArgumentException("Enter the AI server address first.");
        var root = ValidateEndpoint(c.BaseUrl);
        if (key.Any(char.IsControl)) throw new ArgumentException("The API key contains a line break or control character. Paste only the key itself.");
        if (string.IsNullOrWhiteSpace(c.Model)) throw new ArgumentException("Choose a model first.");
        if (string.IsNullOrWhiteSpace(key) && !root.IsLoopback) throw new ArgumentException("Add the API key for this server first.");
        return root;
    }
    public async Task<string> Generate(Connection c, string key, string system, string context, CancellationToken token)
    {
        var timer = Stopwatch.StartNew(); Metrics.Start();
        bool success = false, cancelled = false; long? inputTokens = null, outputTokens = null;
        try
        {
            using var request = BuildRequest(c, key, system, context);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(c.TimeoutSeconds, 3, 180)));
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                double retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 0;
                string error = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                throw new ProviderException(FriendlyError(response.StatusCode, error), Math.Clamp(retryAfter, 0, 600));
            }
            var content = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            (inputTokens, outputTokens) = RequestMetrics.Usage(c.Provider, content);
            string result = Extract(c.Provider, content); success = true; return result;
        }
        catch (OperationCanceledException) { cancelled = token.IsCancellationRequested; throw; }
        finally { Metrics.Finish(success, cancelled, timer.Elapsed.TotalSeconds, inputTokens, outputTokens); }
    }

    public static string Extract(ProviderKind provider, string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        try
        {
            switch (provider)
            {
                case ProviderKind.OpenAI:
                    if (root.TryGetProperty("status", out var status) && status.GetString() != "completed") throw new FormatException("Incomplete response. Increase the output allowance or lower reasoning.");
                    return string.Join("", root.GetProperty("output").EnumerateArray()
                        .Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "message")
                        .SelectMany(x => x.GetProperty("content").EnumerateArray())
                        .Where(x => x.GetProperty("type").GetString() == "output_text").Select(x => x.GetProperty("text").GetString()));
                case ProviderKind.Claude:
                    if (root.TryGetProperty("stop_reason", out var stop) && stop.GetString() == "max_tokens") throw new FormatException("Response exceeded the output allowance.");
                    return string.Join("", root.GetProperty("content").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "text").Select(x => x.GetProperty("text").GetString()));
                case ProviderKind.Gemini:
                    var candidate = root.GetProperty("candidates")[0];
                    if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() != "STOP") throw new FormatException("The provider did not return a complete response.");
                    return string.Join("", candidate.GetProperty("content").GetProperty("parts").EnumerateArray().Where(x => !x.TryGetProperty("thought", out var thought) || !thought.GetBoolean()).Select(x => x.TryGetProperty("text", out var text) ? text.GetString() : ""));
                default:
                    var choice = root.GetProperty("choices")[0];
                    if (choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() == "length") throw new FormatException("Response exceeded the output allowance.");
                    return choice.GetProperty("message").GetProperty("content").GetString() ?? throw new FormatException("Empty response.");
            }
        }
        catch (Exception e) when (e is KeyNotFoundException || e is InvalidOperationException || e is IndexOutOfRangeException)
        { throw new FormatException("The service returned an unsupported response. Check the provider type and model."); }
    }
    public async Task<List<string>> ListModels(Connection connection, string key, CancellationToken token)
    {
        if (connection.Provider is not (ProviderKind.OpenAI or ProviderKind.Compatible)) throw new ArgumentException("Enter the model name from this provider's dashboard.");
        var check = connection.Copy(); check.Model = "model-list";
        var root = ValidateConfiguration(check, key);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "models"));
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ProviderException(FriendlyError(response.StatusCode, await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)));
        return ExtractModels(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
    }
    public static List<string> ExtractModels(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) throw new FormatException("This server does not return a compatible model list.");
        return data.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(m => m.GetProperty("id").GetString()!).Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 200)
            .Distinct().OrderBy(id => id, StringComparer.OrdinalIgnoreCase).Take(500).ToList();
    }
    public static string FriendlyError(HttpStatusCode code, string? body = null)
    {
        string summary = ErrorSummary(code);
        // Providers may echo credentials, account details or prompts in any field. Only
        // return recognized protocol identifiers; never display or log the raw body.
        string parameter = "", errorCode = "";
        if (!string.IsNullOrEmpty(body) && body.Length <= 16384)
        {
            try
            {
                using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    string Read(string name) => error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
                    parameter = Read("param") switch
                    {
                        "reasoning" or "reasoning.effort" or "reasoning_effort" => "reasoning level",
                        "text" or "text.format" or "text.format.type" or "response_format" or "response_format.type" => "output format",
                        "max_output_tokens" or "max_tokens" or "max_completion_tokens" => "output allowance",
                        "model" => "model name",
                        "store" => "response storage",
                        "input" => "prompt content (input)",
                        "instructions" => "prompt content (instructions)",
                        "messages" => "prompt content (messages)",
                        _ => ""
                    };
                    errorCode = Read("code") switch
                    {
                        "unsupported_parameter" => "unsupported_parameter",
                        "unsupported_value" => "unsupported_value",
                        "invalid_value" => "invalid_value",
                        "invalid_json_schema" => "invalid_json_schema",
                        "invalid_request_error" => "invalid_request_error",
                        "model_not_found" => "model_not_found",
                        "insufficient_quota" => "insufficient_quota",
                        "rate_limit_exceeded" => "rate_limit_exceeded",
                        "context_length_exceeded" => "context_length_exceeded",
                        _ => ""
                    };
                }
            }
            catch (JsonException) { }
        }
        if (parameter.Length > 0) summary += " Reported field: " + parameter + ".";
        if (errorCode.Length > 0) summary += " Server code: " + errorCode + ".";
        return summary + " (HTTP " + (int)code + ")";
    }
    private static string ErrorSummary(HttpStatusCode code) => (int)code switch
    {
        401 or 403 => "The service rejected the credentials or model access. Check your API key and account permissions.",
        404 => "The model or address was not found. Check the server address and exact model name.",
        429 => "The service reached a usage or rate limit. Check your account allowance; generation will back off.",
        400 or 422 => "The server rejected the request. Check the reported field and provider type.",
        >= 500 => "The AI service is temporarily unavailable. Generation will back off.",
        _ => "The service could not complete the request."
    };
    public static string SafeError(Exception e) => e is ProviderException || e is ArgumentException || e is FormatException ? e.Message :
        e is JsonException ? "The model returned malformed JSON. Try another model or increase its output allowance." :
        e is OperationCanceledException ? "The request timed out or was cancelled." : "Could not connect. Check the network or start your local model server.";
    public void Dispose() => http.Dispose();
}
public sealed class ProviderException : Exception
{
    public double RetryAfterSeconds { get; }
    public ProviderException(string message, double retryAfterSeconds = 0) : base(message) { RetryAfterSeconds = retryAfterSeconds; }
}
