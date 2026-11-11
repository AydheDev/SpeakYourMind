using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpeakYourMind.Core;

public static class EventLifetime
{
    public static bool Expired(Trigger trigger, int createdTick, int currentTick, bool hasDisplayed) =>
        !hasDisplayed && currentTick - createdTick > (trigger == Trigger.Combat ? 900 : 5000);
}

// Topics are game facts/definition names, not model-created classifications.
public sealed class DialogueSelection
{
    private readonly Dictionary<string, double> recent = new();
    private static string Key(string pawn, Trigger trigger, string topic) => pawn + "\0" + trigger + "\0" + topic;
    public static bool Urgent(Trigger trigger) => trigger is Trigger.Combat or Trigger.Health or Trigger.Colony;
    public bool Allow(string pawn, Trigger trigger, string topic, double now, int activity)
    {
        if (Urgent(trigger)) return true;
        return !recent.TryGetValue(Key(pawn, trigger, topic), out var last) ||
            now - last >= 150 - Math.Clamp(activity, 1, 4) * 25;
    }
    public int Novelty(string pawn, Trigger trigger, string topic) =>
        !recent.ContainsKey(Key(pawn, trigger, topic)) ? 20 : 0;
    public void Displayed(string pawn, Trigger trigger, string topic, double now) => recent[Key(pawn, trigger, topic)] = now;
    public void Prune(double now)
    {
        foreach (var key in recent.Where(p => now - p.Value > 300).Select(p => p.Key).ToList()) recent.Remove(key);
    }
}

public static class TopicWords
{
    private static readonly HashSet<string> stop = new("the a an and or to of in on is it this that for with from as at be was were has have had you your i my we our they their he she his her current recently event social pawn expressed".Split(' '));
    public static HashSet<string> Read(string text) => Regex.Matches((text ?? "").ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
        .Select(m => m.Value).Where(w => !stop.Contains(w)).ToHashSet();
    public static double Similarity(string a, string b)
    {
        var first = Read(a); var second = Read(b);
        if (first.Count < 3 || second.Count < 3) return 0;
        return first.Intersect(second).Count() / (double)first.Union(second).Count();
    }
}

public static class MemorySelection
{
    public static IReadOnlyList<Memory> Select(Profile profile, double day, Preferences settings, string topic, IEnumerable<string> otherParticipants)
    {
        var available = profile.Memories.Where(m => !MemoryPolicy.Expired(m, day, settings)).ToList();
        var people = otherParticipants.ToHashSet(); var words = TopicWords.Read(topic);
        double Relevance(Memory m) => (m.SubjectIds.Any(people.Contains) ? 4 : 0) +
            TopicWords.Read(m.Topic + " " + m.Fact).Count(words.Contains) / (double)Math.Max(1, words.Count);
        var result = new List<Memory>(); var seen = new HashSet<string>();
        void Add(IEnumerable<Memory> candidates, int limit)
        {
            int added = 0;
            foreach (var memory in candidates)
                if (seen.Add(memory.Id)) { result.Add(memory); if (++added >= limit) break; }
        }
        // Reserve independent slots: old defining experiences cannot crowd out the listener or recent life.
        Add(available.Where(m => Relevance(m) > 0).OrderByDescending(Relevance).ThenByDescending(m => m.Day), 6);
        Add(available.OrderByDescending(m => m.Day), 4);
        Add(available.OrderByDescending(m => m.Importance).ThenByDescending(m => m.Day), 4);
        if (result.Count < 14) Add(available.OrderByDescending(m => m.Importance).ThenByDescending(m => m.Day), 14 - result.Count);
        return result;
    }
}
