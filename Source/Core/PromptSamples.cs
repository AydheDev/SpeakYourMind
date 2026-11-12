using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeakYourMind.Core;

public sealed class PromptSample
{
    public string Request = "", Context = "", System = "", Model = "", Feedback = "";
    public double Day;
    public List<TranscriptEntry> Displayed = new();
    public GenerationContext Snapshot() => SYMJson.Read<GenerationContext>(Context);
}
// Debug-only session cache. Strings freeze the input before live profiles change; no game objects or keys.
public sealed class PromptSamples
{
    private readonly List<PromptSample> items = new();
    public IReadOnlyList<PromptSample> Items => items;
    public void Capture(string request, GenerationContext context, string system, string model, double day)
    {
        string json = SYMJson.Write(context);
        if (json.Length + system.Length > 128 * 1024) return;
        items.Add(new PromptSample { Request = request, Context = json, System = system, Model = model, Day = day });
        while (items.Count > 24 || items.Sum(x => x.Context.Length + x.System.Length) > 1024 * 1024) items.RemoveAt(0);
    }
    public PromptSample? Find(string request) => items.Find(s => s.Request == request);
    public void Displayed(string request, TranscriptEntry entry) { var sample = Find(request); if (sample != null && sample.Displayed.Count < 4) sample.Displayed.Add(SYMJson.Read<TranscriptEntry>(SYMJson.Write(entry))); }
    public void Clear() => items.Clear();
}
