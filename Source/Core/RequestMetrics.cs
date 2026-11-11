using System;
using System.Text.Json;

namespace SpeakYourMind.Core;

public sealed record RequestMetricsSnapshot(int Started, int Completed, int Failed, int Cancelled, int InFlight,
    int ResponsesWithUsage, long InputTokens, long OutputTokens, double LastSeconds, double AverageSeconds);

// The transport finishes on worker threads; UI reads an immutable snapshot.
public sealed class RequestMetrics
{
    private readonly object gate = new();
    private int started, completed, failed, cancelled, inFlight, withUsage, finished;
    private long input, output;
    private double last, total;
    public void Start() { lock (gate) { started++; inFlight++; } }
    public void Finish(bool success, bool wasCancelled, double seconds, long? inputTokens = null, long? outputTokens = null)
    {
        lock (gate)
        {
            inFlight--; finished++; last = Math.Max(0, seconds); total += last;
            if (success) completed++; else if (wasCancelled) cancelled++; else failed++;
            if (inputTokens.HasValue || outputTokens.HasValue) withUsage++;
            input += Math.Max(0, inputTokens ?? 0); output += Math.Max(0, outputTokens ?? 0);
        }
    }
    public RequestMetricsSnapshot Snapshot()
    {
        lock (gate) return new(started, completed, failed, cancelled, inFlight, withUsage, input, output, last, finished == 0 ? 0 : total / finished);
    }
    public static (long? input, long? output) Usage(ProviderKind provider, string json)
    {
        // Usage is optional. Missing or unfamiliar metadata must never reject valid dialogue.
        try
        {
            using var doc = JsonDocument.Parse(json);
            string field = provider == ProviderKind.Gemini ? "usageMetadata" : "usage";
            if (!doc.RootElement.TryGetProperty(field, out var usage) || usage.ValueKind != JsonValueKind.Object) return (null, null);
            long? Read(string name) => usage.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) && n >= 0 ? n : null;
            return provider switch
            {
                ProviderKind.Compatible => (Read("prompt_tokens"), Read("completion_tokens")),
                ProviderKind.Gemini => (Read("promptTokenCount"), Sum(Read("candidatesTokenCount"), Read("thoughtsTokenCount"))),
                _ => (Read("input_tokens"), Read("output_tokens"))
            };
        }
        catch (JsonException) { return (null, null); }
        catch (InvalidOperationException) { return (null, null); }
    }
    private static long? Sum(long? a, long? b) => a.HasValue || b.HasValue ? (a ?? 0) + (b ?? 0) : null;
}
