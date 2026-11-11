using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SpeakYourMind.Core;

public sealed class DebugEvent
{
    public long Sequence;
    public string Utc = "", Session = "", Request = "", Stage = "", Source = "", Model = "", Reasoning = "";
    public double Day, ElapsedMs;
    public string Trigger = "", SystemPrompt = "", Context = "", Response = "", Text = "", Detail = "";
}

// Receives strings and numbers only. No game objects, connections, credentials, or HTTP headers are serialized.
public sealed class DebugJournal : IDisposable
{
    private readonly BlockingCollection<DebugEvent> queue = new(2048);
    private readonly HashSet<string> secrets = new(StringComparer.Ordinal);
    private readonly Task writer;
    private readonly long maxBytes;
    private long sequence, dropped;
    private volatile bool closed;
    private volatile bool stopped;
    private string status = "Recording";
    public string Session { get; } = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    public string Folder { get; }
    public string Status => status + (Interlocked.Read(ref dropped) > 0 ? " · omitted records: " + Interlocked.Read(ref dropped) : "");
    public DebugJournal(string folder, int limitMegabytes = 50)
    {
        Folder = folder; maxBytes = Math.Max(1, limitMegabytes) * 1024L * 1024L;
        writer = Task.Run(WriteLoop);
    }
    public void AddSecret(string value) { if (!string.IsNullOrWhiteSpace(value)) lock (secrets) secrets.Add(value.Trim()); }
    private string Redact(string value)
    {
        string result = value ?? "";
        lock (secrets) foreach (var key in secrets.OrderByDescending(s => s.Length)) result = result.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        return Regex.Replace(result, @"(?i)(Bearer\s+)[^\s""<>]+|\b(?:sk-|ghp_|AIza)[A-Za-z0-9_\-]{12,}", "[REDACTED]");
    }
    public void Record(DebugEvent entry)
    {
        if (closed) return;
        if (stopped) { Interlocked.Increment(ref dropped); return; }
        entry.Sequence = Interlocked.Increment(ref sequence); entry.Utc = DateTime.UtcNow.ToString("O"); entry.Session = Session;
        entry.Model = Redact(entry.Model); entry.SystemPrompt = Redact(entry.SystemPrompt); entry.Context = Redact(entry.Context);
        entry.Response = Redact(entry.Response); entry.Text = Redact(entry.Text); entry.Detail = Redact(entry.Detail);
        entry.Reasoning = Redact(entry.Reasoning); entry.Source = Redact(entry.Source); entry.Trigger = Redact(entry.Trigger);
        try { if (!queue.TryAdd(entry)) Interlocked.Increment(ref dropped); } catch (InvalidOperationException) { }
    }
    private void WriteLoop()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            using var json = new StreamWriter(Path.Combine(Folder, Session + ".jsonl"), false, new UTF8Encoding(false));
            using var readable = new StreamWriter(Path.Combine(Folder, Session + ".txt"), false, new UTF8Encoding(false));
            long bytes = 0;
            foreach (var entry in queue.GetConsumingEnumerable())
            {
                string line = SYMJson.Write(entry);
                string report = $"[{entry.Sequence}] {entry.Utc} | day {entry.Day:F3} | {entry.Stage} | {entry.Request}\nSource: {entry.Source}; model: {entry.Model}; reasoning: {entry.Reasoning}; trigger: {entry.Trigger}; elapsed: {entry.ElapsedMs:F0}ms\n" +
                    Sections(entry) + "\n";
                long size = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(report) + 4;
                if (stopped || bytes + size > maxBytes)
                {
                    if (status != "Session size limit reached") { json.WriteLine(SYMJson.Write(new DebugEvent { Stage = "size_limit", Detail = "Recording stopped at the configured session limit; further records are omitted." })); readable.WriteLine("SESSION SIZE LIMIT REACHED — further records omitted."); }
                    status = "Session size limit reached"; stopped = true; Interlocked.Increment(ref dropped); continue;
                }
                json.WriteLine(line); readable.WriteLine(report); bytes += size;
                json.Flush(); readable.Flush();
            }
            if (status == "Recording") status = "Saved";
        }
        catch { stopped = true; status = "Could not write debug files"; while (queue.TryTake(out _)) Interlocked.Increment(ref dropped); }
    }
    private static string Sections(DebugEvent e) => string.Join("\n", new[]
    {
        e.Detail.Length > 0 ? "DETAIL: " + e.Detail : "", e.Text.Length > 0 ? "TEXT: " + e.Text : "",
        e.SystemPrompt.Length > 0 ? "SYSTEM PROMPT:\n" + e.SystemPrompt : "", e.Context.Length > 0 ? "CONTEXT:\n" + e.Context : "",
        e.Response.Length > 0 ? "RAW RESPONSE:\n" + e.Response : ""
    }.Where(s => s.Length > 0));
    // Completion can be awaited by tests/tools; the game's main thread never waits for disk writes.
    public Task Completion => writer;
    public void Dispose() { if (closed) return; closed = true; queue.CompleteAdding(); }
}
