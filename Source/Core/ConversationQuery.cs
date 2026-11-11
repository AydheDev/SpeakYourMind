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
    public bool Update(SaveState state, string pawn, string term, string trigger, double day, int retention)
    {
        bool changed = entries.Update(state, pawn, "", "", day, retention);
        if (!changed && search == term && category == trigger) return false;
        search = term; category = trigger;
        Matches = entries.Matches.Reverse().GroupBy(t => t.ExchangeId.Length > 0 ? "exchange:" + t.ExchangeId : "line:" + t.Id)
            .Select(g => new ConversationGroup { Id = g.Key, Lines = g.OrderBy(t => t.Day).ToList() })
            .Where(g => (trigger.Length == 0 || g.Lines.Any(t => t.Trigger == trigger)) &&
                (term.Length == 0 || g.Lines.Any(t => t.Text.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    t.SpeakerName.Contains(term, StringComparison.OrdinalIgnoreCase) || t.Trigger.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(g => g.Day).ToList();
        Rebuilds++; return true;
    }
    public IEnumerable<ConversationGroup> Page(int page, int size = 60) => Matches.Skip(Math.Max(0, page) * size).Take(size);
}
