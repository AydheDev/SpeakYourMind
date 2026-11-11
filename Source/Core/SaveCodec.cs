using System;
using System.Linq;

namespace SpeakYourMind.Core;

public static class SaveCodec
{
    // Reject unsupported/corrupt graphs before assigning them to live state. The caller preserves the raw save.
    public static SaveState Read(string json)
    {
        var state = SYMJson.Read<SaveState>(json);
        if (state.Version != 1) throw new FormatException("This save uses an unsupported SpeakYourMind data version.");
        if (state.Profiles == null || state.ColonyEvents == null || state.Transcript == null) throw new FormatException("The SpeakYourMind save is incomplete.");
        state.ColonyRule ??= "";
        if (state.Profiles.Any(p => p == null || string.IsNullOrEmpty(p.PawnId)) || state.Profiles.Select(p => p.PawnId).Distinct().Count() != state.Profiles.Count)
            throw new FormatException("The SpeakYourMind pawn index is invalid.");
        foreach (var p in state.Profiles)
        {
            p.Name ??= p.PawnId; p.Voice ??= ""; p.Annotation ??= ""; p.Fingerprint ??= ""; p.LastProfileFacts ??= "";
            p.FallbackSignature ??= ""; p.ObservedFacts ??= ""; p.ObservedHealth ??= "";
            if (p.Memories == null || p.Fallbacks == null || !double.IsFinite(p.UpdatedDay)) throw new FormatException("The SpeakYourMind profile is invalid.");
            foreach (var m in p.Memories) Validate(m);
            p.Fallbacks.RemoveAll(f => string.IsNullOrWhiteSpace(f));
        }
        foreach (var m in state.ColonyEvents) Validate(m);
        foreach (var t in state.Transcript)
        {
            if (t == null || string.IsNullOrEmpty(t.Id) || t.Participants == null || !double.IsFinite(t.Day) || string.IsNullOrEmpty(t.SpeakerId) || string.IsNullOrWhiteSpace(t.Text))
                throw new FormatException("The SpeakYourMind transcript is invalid.");
            t.SpeakerName ??= t.SpeakerId; t.ExchangeId ??= ""; t.Trigger ??= ""; t.Source ??= "ai";
            if (!t.Participants.Contains(t.SpeakerId)) t.Participants.Add(t.SpeakerId);
        }
        state.Touch(); return state;
    }
    private static void Validate(Memory m)
    {
        if (m == null || string.IsNullOrEmpty(m.Id) || m.Fact == null || !double.IsFinite(m.Day) || !float.IsFinite(m.Importance) || m.Importance < 0 || m.Importance > 1)
            throw new FormatException("The SpeakYourMind memory data is invalid.");
        m.Topic ??= ""; m.SubjectIds ??= new(); m.SubjectIds.RemoveAll(string.IsNullOrEmpty);
        m.Source ??= "game"; m.Perspective ??= "experienced"; m.Reason ??= "";
    }
}
