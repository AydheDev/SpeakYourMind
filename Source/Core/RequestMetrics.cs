using System;
using System.Text.Json;

namespace SpeakYourMind.Core;

public enum RequestPurpose { Dialogue, Memory, Preview }
public sealed class RequestTrace
{
    public readonly RequestPurpose Purpose;
    internal bool Finished, Visible, Discarded, Counted;
    internal long Input, Output;
    public RequestTrace(RequestPurpose purpose) { Purpose = purpose; }
}
public sealed record PurposeMetrics(int Started, int ResponsesWithUsage, long InputTokens, long OutputTokens,
    double AverageSeconds, int VisibleRequests, int DiscardedRequests, long DiscardedInput, long DiscardedOutput);

public sealed record RequestMetricsSnapshot(int Started, int Completed, int Failed, int Cancelled, int InFlight,
    int ResponsesWithUsage, long InputTokens, long OutputTokens, double LastSeconds, double AverageSeconds);

// The transport finishes on worker threads; UI reads an immutable snapshot.
public sealed class RequestMetrics
{
    private readonly object gate = new();
    private int started, completed, failed, cancelled, inFlight, withUsage, finished;
    private long input, output;
    private double last, total;
    private sealed class Group { public int Started, Finished, Usage, Visible, Discarded; public long Input, Output, DiscardedInput, DiscardedOutput; public double Seconds; }
    private readonly Group[] groups = { new(), new(), new() };
    public void Start(RequestTrace? trace = null) { lock (gate) { started++; inFlight++; if (trace != null) groups[(int)trace.Purpose].Started++; } }
    public void Finish(bool success, bool wasCancelled, double seconds, long? inputTokens = null, long? outputTokens = null, RequestTrace? trace = null)
    {
        lock (gate)
        {
            inFlight--; finished++; last = Math.Max(0, seconds); total += last;
            if (success) completed++; else if (wasCancelled) cancelled++; else failed++;
            if (inputTokens.HasValue || outputTokens.HasValue) withUsage++;
            input += Math.Max(0, inputTokens ?? 0); output += Math.Max(0, outputTokens ?? 0);
            if (trace != null)
            {
                var g = groups[(int)trace.Purpose]; g.Finished++; g.Seconds += Math.Max(0, seconds);
                if (inputTokens.HasValue || outputTokens.HasValue) g.Usage++;
                trace.Input = Math.Max(0, inputTokens ?? 0); trace.Output = Math.Max(0, outputTokens ?? 0);
                g.Input += trace.Input; g.Output += trace.Output; trace.Finished = true; CountOutcome(trace);
            }
        }
    }
    public void Outcome(RequestTrace? trace, bool visible)
    {
        if (trace == null) return;
        lock (gate)
        {
            if (trace.Counted || trace.Visible) return;
            trace.Visible = visible; trace.Discarded = !visible; CountOutcome(trace);
        }
    }
    private void CountOutcome(RequestTrace trace)
    {
        if (!trace.Finished || trace.Counted || (!trace.Visible && !trace.Discarded)) return;
        var g = groups[(int)trace.Purpose]; trace.Counted = true;
        if (trace.Visible) g.Visible++;
        else { g.Discarded++; g.DiscardedInput += trace.Input; g.DiscardedOutput += trace.Output; }
    }
    public PurposeMetrics For(RequestPurpose purpose)
    {
        lock (gate) { var g = groups[(int)purpose]; return new(g.Started, g.Usage, g.Input, g.Output, g.Finished == 0 ? 0 : g.Seconds / g.Finished, g.Visible, g.Discarded, g.DiscardedInput, g.DiscardedOutput); }
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
