using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpeakYourMind.Core;

public static class SYMJson
{
    public static readonly JsonSerializerOptions Options = new() { IncludeFields = true, PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(Unfence(value), Options) ?? throw new FormatException("Empty response.");
    private static string Unfence(string value)
    {
        value = value.Trim();
        if (value.StartsWith("```")) { int start = value.IndexOf('\n'); int end = value.LastIndexOf("```"); if (start >= 0 && end > start) value = value[(start + 1)..end].Trim(); }
        return value;
    }
}

public enum ExpressionStyle { Dialogue, Narration, Hybrid }
public enum ExpressionLength { Brief, Standard, Descriptive }
public enum Trigger { Social, Needs, Health, Combat, Colony, Observation }
public enum ProviderKind { OpenAI, Compatible, Claude, Gemini }

public sealed class Preferences
{
    public bool Enabled;
    public ExpressionStyle Style = ExpressionStyle.Hybrid;
    public ExpressionLength Length = ExpressionLength.Standard;
    public int Activity = 3, MaxCharacters = 240, MaxBubbles = 3, MaxTurns = 3, RequestsPerMinute = 12;
    public int PawnHistoryDays, ColonyHistoryDays; // Zero means forever.
    public float MemoryScaleDays = 30, MemoryExponent = 1.4f;
    public bool TrackGuests, Social = true, Needs = true, Health = true, Combat = true, Colony = true, Observation = true;
    public string ColonyRule = "", Language = "English";
    public int EffectiveLimit => MaxCharacters > 0 ? Math.Clamp(MaxCharacters, 8, 2000) : Length switch { ExpressionLength.Brief => 100, ExpressionLength.Descriptive => 420, _ => 240 };
    public bool Allows(Trigger t) => t switch { Trigger.Social => Social, Trigger.Needs => Needs, Trigger.Health => Health, Trigger.Combat => Combat, Trigger.Colony => Colony, _ => Observation };
}

public sealed class Connection
{
    public ProviderKind Provider;
    public string BaseUrl = "https://api.openai.com/v1", Model = "", Reasoning = "low";
    public bool SendReasoning = true;
    public Connection Copy() => SYMJson.Read<Connection>(SYMJson.Write(this));
}

public sealed class PawnSnapshot
{
    public string Id = "", Name = "", Kind = "", Faction = "", Technology = "", Job = "", Background = "", Beliefs = "";
    public bool Animal, Mechanoid;
    public float Age, Pain;
    public List<string> Traits = new(), Skills = new(), Gear = new(), Health = new(), Relationships = new(), Mood = new();
    public Dictionary<string, float> Needs = new();
    public Dictionary<string, float> Stats = new();
    public string Environment = "";
}
public sealed class Memory
{
    public string Id = Guid.NewGuid().ToString("N"), Fact = "", Source = "game", Perspective = "experienced", Reason = "";
    public double Day;
    public float Importance = 0.1f;
    public bool Scored;
}
public sealed class Profile
{
    public string PawnId = "", Name = "", Voice = "", Annotation = "", Fingerprint = "", LastProfileFacts = "";
    public double UpdatedDay;
    public bool Persistent = true;
    public List<Memory> Memories = new();
    public List<string> Fallbacks = new();
    public string FallbackSignature = "";
}
public sealed class TranscriptEntry
{
    public string Id = Guid.NewGuid().ToString("N"), ExchangeId = "", SpeakerId = "", SpeakerName = "", Text = "", Trigger = "", Source = "ai";
    public List<string> Participants = new();
    public double Day;
}
public sealed class SaveState
{
    public int Version = 1;
    public string ColonyRule = "";
    public List<Profile> Profiles = new();
    public List<Memory> ColonyEvents = new();
    public List<TranscriptEntry> Transcript = new();
    public Profile Profile(string id, string name, bool persistent)
    {
        var p = Profiles.Find(x => x.PawnId == id);
        if (p == null) { p = new Profile { PawnId = id }; Profiles.Add(p); }
        p.Name = name; p.Persistent = persistent; return p;
    }
    public void Prune(double day, Preferences settings)
    {
        foreach (var p in Profiles) p.Memories.RemoveAll(m => MemoryPolicy.Expired(m, day, settings));
        ColonyEvents.RemoveAll(m => MemoryPolicy.Expired(m, day, settings));
        Transcript.RemoveAll(t => !HistoryPolicy.Visible(t.Day, day, settings.PawnHistoryDays) && !HistoryPolicy.Visible(t.Day, day, settings.ColonyHistoryDays));
    }
}
public static class MemoryPolicy
{
    public static double Lifetime(float importance, Preferences s)
    {
        if (importance >= 1) return double.PositiveInfinity;
        if (importance <= 0 || !float.IsFinite(importance)) return 0;
        return Math.Max(1, s.MemoryScaleDays) * Math.Pow(importance / (1 - importance), Math.Clamp(s.MemoryExponent, 0.2, 4));
    }
    public static bool Expired(Memory m, double day, Preferences s) => m.Scored && day - m.Day >= Lifetime(m.Importance, s);
    public static IEnumerable<Memory> Relevant(Profile p, double day, Preferences s) => p.Memories.Where(m => !Expired(m, day, s)).OrderByDescending(m => m.Importance).ThenByDescending(m => m.Day).Take(14);
}
public static class HistoryPolicy
{
    public static bool Visible(double entryDay, double now, int days) => days <= 0 || now - entryDay <= days;
}
public static class TextPolicy
{
    public static string Clean(string text) => Regex.Replace(text ?? "", "<[^>]*>", "").Replace('\r', ' ').Trim();
    public static bool Valid(string text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max && text.Length <= 2000;
}
public sealed class Turn { public string PawnId = "", Text = ""; }
public sealed class ExpressionResponse { public List<Turn> Turns = new(); }
public sealed class MemoryScore { public string Id = "", Reason = ""; public float Importance; }
public sealed class ProfileResponse { public string Voice = ""; public List<MemoryScore> Scores = new(); public List<string> Fallbacks = new(); }
public sealed class GenerationContext
{
    public string Event = "", Trigger = "", ColonyRule = "", Language = "English";
    public int MaxCharacters, MaxTurns;
    public ExpressionStyle Style;
    public ExpressionLength Length;
    public List<PawnSnapshot> Pawns = new();
    public List<Profile> Profiles = new();
    public List<Memory> ColonyAwareness = new();
    public List<string> ColonyResources = new();
    public List<TranscriptEntry> RecentExchange = new();
}

public static class ProfilePolicy
{
    public static void ApplyScore(Memory memory, float importance, string reason)
    {
        if (!float.IsFinite(importance) || importance < 0 || importance > 1) return;
        if (!memory.Scored || memory.Importance < 1) memory.Importance = Math.Max(memory.Scored ? memory.Importance : 0, importance);
        memory.Scored = true; memory.Reason = TextPolicy.Clean(reason ?? "");
        if (memory.Reason.Length > 500) memory.Reason = memory.Reason[..500];
    }
}

// Real-time limits are independent of simulation speed and shared by all AI jobs.
public sealed class RequestBudget
{
    private readonly Queue<double> starts = new();
    public bool TrySpend(double now, int limit)
    {
        while (starts.Count > 0 && starts.Peek() <= now - 60) starts.Dequeue();
        if (starts.Count >= Math.Max(1, limit)) return false;
        starts.Enqueue(now); return true;
    }
}
