using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeakYourMind.Core;

public sealed class ConversationGroup
{
    public string Id = "";
    public List<TranscriptEntry> Lines = new();
    public double Day => Lines.Count == 0 ? 0 : Lines.Max(t => t.Day);
}

public sealed class ConversationQuery
{
    private readonly TranscriptQuery entries = new();
    private string search = "", category = "";
    public IReadOnlyList<ConversationGroup> Matches { get; private set; } = Array.Empty<ConversationGroup>();
    public int Rebuilds { get; private set; }
    public long Revision { get; private set; }
    private readonly Dictionary<string, ConversationGroup> index = new();
    private readonly List<ConversationGroup> ordered = new();
    private static string Key(TranscriptEntry t) => t.ExchangeId.Length > 0 ? "exchange:" + t.ExchangeId : "line:" + t.Id;
    public bool Update(SaveState state, string pawn, string term, string trigger, double day, int retention)
    {
        bool changed = entries.Update(state, pawn, "", "", day, retention);
        if (!changed && search == term && category == trigger) return false;
        bool incremental = changed && entries.Appended && search == term && category == trigger;
        search = term; category = trigger;
        bool Accept(ConversationGroup g) => (trigger.Length == 0 || g.Lines.Any(t => t.Trigger == trigger)) &&
                (term.Length == 0 || g.Lines.Any(t => t.Text.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    t.SpeakerName.Contains(term, StringComparison.OrdinalIgnoreCase) || t.Trigger.Contains(term, StringComparison.OrdinalIgnoreCase)));
        if (!incremental)
        {
            index.Clear(); ordered.Clear();
            foreach (var t in entries.Matches.Reverse())
            {
                string key = Key(t);
                if (!index.TryGetValue(key, out var group)) index[key] = group = new ConversationGroup { Id = key };
                group.Lines.Add(t);
            }
            foreach (var g in index.Values) g.Lines = g.Lines.OrderBy(t => t.Day).ToList();
            ordered.AddRange(index.Values.Where(Accept).OrderByDescending(g => g.Day)); Rebuilds++;
        }
        else
        {
            foreach (var t in entries.Added.Reverse())
            {
                string key = Key(t);
                if (!index.TryGetValue(key, out var group)) index[key] = group = new ConversationGroup { Id = key };
                ordered.Remove(group); group.Lines.Add(t);
                group.Lines = group.Lines.OrderBy(x => x.Day).ToList();
                if (!Accept(group)) continue;
                int at = ordered.FindIndex(g => g.Day <= group.Day);
                ordered.Insert(at < 0 ? ordered.Count : at, group);
            }
        }
        Matches = ordered; Revision++; return true;
    }
    public IEnumerable<ConversationGroup> Page(int page, int size = 60) => Matches.Skip(Math.Max(0, page) * size).Take(size);
}
