using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

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
    public bool ClickBubblesOpenHistory = false;
    public bool NativeSocialWhenUnavailable = true;
    public ExpressionStyle Style = ExpressionStyle.Hybrid;
    public ExpressionLength Length = ExpressionLength.Standard;
    public int Activity = 3, MaxCharacters = 0, MaxBubbles = 3, MaxTurns = 3, RequestsPerMinute = 12;
    public int PawnHistoryDays, ColonyHistoryDays; // Zero means forever.
    public float MemoryScaleDays = 30, MemoryExponent = 1.4f;
    public bool TrackGuests, Social = true, Needs = true, Health = true, Combat = true, Colony = true, Observation = true;
    public bool SpeakUpFallback, VSIEFallback, RumorFallback, CalloutsFallback, DebugLogging;
    public bool FollowUps = true;
    public int FallbackDelaySeconds = 6, DebugSessionMegabytes = 50;
    public string ColonyRule = "", Language = "English";
    public int EffectiveLimit => MaxCharacters > 0 ? Math.Clamp(MaxCharacters, 1, 2000) : Length switch { ExpressionLength.Brief => 100, ExpressionLength.Descriptive => 420, _ => 240 };
    public bool Allows(Trigger t) => t switch { Trigger.Social => Social, Trigger.Needs => Needs, Trigger.Health => Health, Trigger.Combat => Combat, Trigger.Colony => Colony, _ => Observation };
    public void Normalize()
    {
        if (!Enum.IsDefined(Style)) Style = ExpressionStyle.Hybrid;
        if (!Enum.IsDefined(Length)) Length = ExpressionLength.Standard;
        Activity = Math.Clamp(Activity, 1, 4); MaxCharacters = Math.Clamp(MaxCharacters, 0, 2000);
        MaxBubbles = Math.Clamp(MaxBubbles, 1, 8); MaxTurns = Math.Clamp(MaxTurns, 2, 4); RequestsPerMinute = Math.Clamp(RequestsPerMinute, 1, 60);
        PawnHistoryDays = Math.Clamp(PawnHistoryDays, 0, 60000); ColonyHistoryDays = Math.Clamp(ColonyHistoryDays, 0, 60000);
        MemoryScaleDays = float.IsFinite(MemoryScaleDays) ? Math.Clamp(MemoryScaleDays, 1, 6000) : 30;
        MemoryExponent = float.IsFinite(MemoryExponent) ? Math.Clamp(MemoryExponent, 0.2f, 4) : 1.4f;
        Language = string.IsNullOrWhiteSpace(Language) ? "English" : TextPolicy.Clean(Language);
        ColonyRule ??= "";
        FallbackDelaySeconds = Math.Clamp(FallbackDelaySeconds, 2, 30); DebugSessionMegabytes = Math.Clamp(DebugSessionMegabytes, 1, 1000);
    }
}

public sealed class Connection
{
    public ProviderKind Provider;
    public string BaseUrl = "https://api.openai.com/v1", Model = "", Reasoning = "low";
    public bool SendReasoning = true;
    public int TimeoutSeconds = 45, OutputTokens = 2200;
    public Connection Copy() => SYMJson.Read<Connection>(SYMJson.Write(this));
    public void CopyFrom(Connection source)
    {
        Provider = source.Provider; BaseUrl = source.BaseUrl; Model = source.Model; Reasoning = source.Reasoning;
        SendReasoning = source.SendReasoning; TimeoutSeconds = source.TimeoutSeconds; OutputTokens = source.OutputTokens;
    }
    public void Normalize()
    {
        if (!Enum.IsDefined(Provider)) Provider = ProviderKind.OpenAI;
        BaseUrl ??= "https://api.openai.com/v1"; Model ??= ""; Reasoning ??= "low";
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 3, 180); OutputTokens = Math.Clamp(OutputTokens, 256, 16000);
    }
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
    public string Topic = "";
    public List<string> SubjectIds = new();
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
    public string ObservedFacts = "", ObservedHealth = "";
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
    [System.Text.Json.Serialization.JsonIgnore] public long Revision { get; private set; }
    public void Touch() => Revision++;
    public void Record(TranscriptEntry entry) { Transcript.Add(entry); Touch(); }
    public Profile Profile(string id, string name, bool persistent)
    {
        var p = Profiles.Find(x => x.PawnId == id);
        if (p == null) { p = new Profile { PawnId = id, Persistent = persistent }; Profiles.Add(p); Touch(); }
        if (p.Name != name) { p.Name = name; Touch(); }
        p.Persistent |= persistent; return p; // A former resident keeps their identity if they later return.
    }
    public void Prune(double day, Preferences settings)
    {
        int count = 0;
        foreach (var p in Profiles) count += p.Memories.RemoveAll(m => MemoryPolicy.Expired(m, day, settings));
        count += ColonyEvents.RemoveAll(m => MemoryPolicy.Expired(m, day, settings));
        count += Transcript.RemoveAll(t => !HistoryPolicy.Visible(t.Day, day, settings.PawnHistoryDays) && !HistoryPolicy.Visible(t.Day, day, settings.ColonyHistoryDays));
        if (count > 0) Touch();
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
    public static string Clean(string text) => Regex.Replace(Regex.Replace(text ?? "", "<[^>]*>", ""), @"[\s\p{Cc}\u200B\u202A-\u202E\u2066-\u2069\uFEFF]+", " ").Trim();
    public static int VisibleLength(string text) => StringInfo.ParseCombiningCharacters(text ?? "").Length;
    public static bool Valid(string text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= 8000 && VisibleLength(text) <= Math.Min(max, 2000);
    public static bool Repeated(string text, IEnumerable<string> recent)
    {
        string Normalize(string s) => Regex.Replace(Clean(s).ToLowerInvariant(), @"[\p{P}\s]+", "");
        string key = Normalize(text);
        return key.Length > 0 && recent.Any(s => Normalize(s) == key);
    }
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
    public bool Available(double now, int limit)
    {
        while (starts.Count > 0 && starts.Peek() <= now - 60) starts.Dequeue();
        return starts.Count < Math.Max(1, limit);
    }
    public bool TrySpend(double now, int limit)
    {
        if (!Available(now, limit)) return false;
        starts.Enqueue(now); return true;
    }
}
