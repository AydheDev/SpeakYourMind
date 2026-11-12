using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SpeakYourMind.Core;

// Store the last sample, rather than waiting for a large change before updating it.
public sealed class NeedWatch
{
    private sealed class Sample { public float Level; public bool Alert; public double LastAlert; }
    private readonly Dictionary<string, Sample> samples = new();
    public static float Satisfaction(float level, bool highBad) => highBad ? 1 - level : level;
    public bool Observe(string key, float level, bool highBad, double now)
    {
        if (!float.IsFinite(level)) return false;
        float value = Satisfaction(level, highBad);
        if (!samples.TryGetValue(key, out var sample)) samples[key] = sample = new Sample { Level = 1, LastAlert = double.NegativeInfinity };
        bool crossed = value < 0.25f && !sample.Alert;
        bool worsened = value < 0.1f && sample.Level >= 0.1f;
        bool remind = value < 0.25f && now - sample.LastAlert >= 180;
        sample.Level = value;
        if (value >= 0.35f) sample.Alert = false;
        bool emit = (crossed || worsened || remind) && now - sample.LastAlert >= 30;
        if (emit) { sample.Alert = true; sample.LastAlert = now; }
        return emit;
    }
    public void Prune(HashSet<string> alive) { foreach (var k in samples.Keys.Where(k => !alive.Contains(k.Split(':')[0])).ToList()) samples.Remove(k); }
}

public static class FailurePolicy
{
    public static bool LocalResponse(Exception e) => e is FormatException or JsonException;
}

public static class MemoryWork
{
    public const int MaxScoreAttempts = 3;
    public static IEnumerable<Memory> Pending(Profile p, double day) => p.Memories
        .Where(m => !m.Scored && m.ScoreAttempts < MaxScoreAttempts && day >= m.NextScoreDay)
        .OrderBy(m => m.ScoreAttempts).ThenBy(m => m.Day).Take(20);
    public static bool RefreshVoice(Profile p, string facts, string signature, double day) => day >= p.NextProfileDay &&
        (p.Voice.Length == 0 || p.FallbackSignature != signature || (p.LastProfileFacts != facts && day - p.UpdatedDay >= 0.25));
    public static void CompleteScores(Profile p, IEnumerable<string> requested, IEnumerable<MemoryScore>? scores, double day)
    {
        var ids = requested.ToHashSet();
        foreach (var score in scores ?? Array.Empty<MemoryScore>())
        {
            if (score == null || !ids.Contains(score.Id) || !float.IsFinite(score.Importance) || score.Importance < 0 || score.Importance > 1) continue;
            var memory = p.Memories.Find(m => m.Id == score.Id);
            if (memory != null) ProfilePolicy.ApplyScore(memory, score.Importance, score.Reason);
        }
        foreach (var m in p.Memories.Where(m => ids.Contains(m.Id) && !m.Scored))
        {
            m.ScoreAttempts++; m.NextScoreDay = day + Math.Min(2, 0.25 * Math.Pow(2, m.ScoreAttempts - 1));
            if (m.ScoreAttempts >= MaxScoreAttempts) m.Reason = "Automatic scoring deferred after three incomplete replies. Kept safely; retry from Memories.";
        }
    }
    public static void Retry(Profile p) { foreach (var m in p.Memories.Where(m => !m.Scored)) { m.ScoreAttempts = 0; m.NextScoreDay = 0; } p.NextProfileDay = 0; }
}
