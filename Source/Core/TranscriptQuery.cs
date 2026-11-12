using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeakYourMind.Core;

// Cache references rather than a formatted string for every line of an unlimited transcript.
public sealed class TranscriptQuery
{
    private SaveState? state;
    private long revision = -1;
    private long reset = -1;
    private int count = -1, retention;
    private string pawn = "", search = "", trigger = "";
    private double expiry = double.PositiveInfinity;
    public IReadOnlyList<TranscriptEntry> Matches { get; private set; } = Array.Empty<TranscriptEntry>();
    public int Rebuilds { get; private set; }
    public bool Appended { get; private set; }
    public IReadOnlyList<TranscriptEntry> Added { get; private set; } = Array.Empty<TranscriptEntry>();
    public bool Update(SaveState current, string pawnId, string term, string category, double day, int days)
    {
        bool same = state == current && reset == current.TranscriptReset && pawn == pawnId && search == term && trigger == category && retention == days && day <= expiry;
        if (same && revision == current.TranscriptRevision && count == current.Transcript.Count) return false;
        Appended = same && count >= 0 && current.Transcript.Count > count;
        int start = Appended ? count : 0;
        state = current; revision = current.TranscriptRevision; reset = current.TranscriptReset; count = current.Transcript.Count; pawn = pawnId; search = term; trigger = category; retention = days;
        var matches = new List<TranscriptEntry>(); if (!Appended) expiry = double.PositiveInfinity;
        for (int i = current.Transcript.Count - 1; i >= start; i--)
        {
            var t = current.Transcript[i];
            if (pawnId.Length > 0 && !t.Participants.Contains(pawnId)) continue;
            if (category.Length > 0 && t.Trigger != category) continue;
            if (!HistoryPolicy.Visible(t.Day, day, days)) continue;
            if (term.Length > 0 && !(t.Text.Contains(term, StringComparison.OrdinalIgnoreCase) || t.SpeakerName.Contains(term, StringComparison.OrdinalIgnoreCase) || t.Trigger.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            matches.Add(t);
            if (days > 0) expiry = Math.Min(expiry, t.Day + days);
        }
        Added = matches.ToArray();
        if (Appended) matches.AddRange(Matches); else Rebuilds++;
        Matches = matches; return true;
    }
    public IEnumerable<TranscriptEntry> Page(int page, int size = 60) => Matches.Skip(Math.Max(0, page) * size).Take(size);
}
