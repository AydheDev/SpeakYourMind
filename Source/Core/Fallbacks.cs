using System;
using System.Linq;

namespace SpeakYourMind.Core;

public sealed class FallbackCandidate
{
    public string Source = "", Text = "", Definition = "", Language = "English";
    public bool Narration;
}

public static class FallbackPolicy
{
    public static bool Enabled(string source, Preferences s) => source switch
    {
        "speakup" => s.SpeakUpFallback,
        "vsie" => s.VSIEFallback,
        "rumor" => s.RumorFallback,
        "callouts" => s.CalloutsFallback,
        _ => false
    };
    public static string Rejection(FallbackCandidate? candidate, GenerationContext context, Preferences settings)
    {
        if (candidate == null) return "No matching text from an optional mod.";
        if (!Enabled(candidate.Source, settings)) return "This optional source is disabled.";
        if (!TextPolicy.Valid(candidate.Text, settings.EffectiveLimit)) return "Source text is empty or exceeds the character limit.";
        if (context.ColonyRule.Length > 0 || context.Profiles.Any(p => p.Annotation.Length > 0)) return "Player instructions exclude unvalidated mod text.";
        if (!string.Equals(candidate.Language, context.Language.Trim(), StringComparison.OrdinalIgnoreCase)) return "Source language does not match the expression language.";
        if (context.Pawns.Count == 0 || context.Pawns[0].Animal || context.Pawns[0].Mechanoid) return "Unverified species-specific source text.";
        if (candidate.Narration && context.Style == ExpressionStyle.Dialogue || !candidate.Narration && context.Style == ExpressionStyle.Narration) return "Source text does not match the selected communication style.";
        return "";
    }
    public static string Label(string source) => source switch
    {
        "speakup" => "SpeakUp / installed extensions", "vsie" => "Vanilla Social Interactions Expanded",
        "rumor" => "Rumor Has It", "callouts" => "Callouts", _ => source
    };
}

public static class ConversationPolicy
{
    public static int Turns(Preferences s, bool hasListener, bool continuation) => !hasListener || !s.FollowUps ? 1 : Math.Clamp(s.MaxTurns - (continuation ? 1 : 0), 1, 4);
    public static bool ContinueAfterFallback(bool discarded, bool fallback, bool alreadyContinuation, bool hasListener, Preferences s) =>
        !discarded && fallback && !alreadyContinuation && hasListener && s.FollowUps;
}
