using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SpeakYourMind.Core;
using UnityEngine;
using Verse;

namespace SpeakYourMind;

// Optional text adapters, with no compile-time references to third-party assemblies.
public static class OptionalDialogue
{
    public static string Status = "No optional adapters checked yet.";
    private static bool speakUpPresent, speakUpGuarded;
    [ThreadStatic] private static CalloutFrame? callout;
    private sealed class CalloutFrame { public Pawn Pawn = null!; public string Definition = ""; }
    public static void Initialize()
    {
        var messages = new List<string>();
        var mods = LoadedModManager.RunningModsListForReading;
        var harmony = new Harmony(Integration.Owner + ".optional");
        speakUpPresent = mods.Any(m => m.PackageIdPlayerFacing.Equals("jpt.speakup", StringComparison.OrdinalIgnoreCase));
        if (speakUpPresent)
        {
            try
            {
                var method = AccessTools.Method(AccessTools.TypeByName("SpeakUp.DialogManager"), "Ensue", new[] { typeof(List<string>) });
                if (method == null) throw new MissingMethodException();
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(OptionalDialogue), nameof(EnsuePrefix)));
                speakUpGuarded = true;
                messages.Add("SpeakUp text adapter ready (includes installed Extension, Child and ChildSFW rules)");
            }
            catch { messages.Add("SpeakUp interface unknown: text capture disabled"); }
        }
        if (mods.Any(m => m.PackageIdPlayerFacing.Equals("VanillaExpanded.VanillaSocialInteractionsExpanded", StringComparison.OrdinalIgnoreCase))) messages.Add("Vanilla Social Interactions Expanded event text available");
        if (mods.Any(m => m.PackageIdPlayerFacing.Contains("rumorhasit", StringComparison.OrdinalIgnoreCase))) messages.Add("Rumor Has It event text available (1.7 playtest required)");
        var tracker = AccessTools.TypeByName("CM_Callouts.CalloutTracker");
        if (tracker != null)
        {
            var calloutHarmony = new Harmony(Integration.Owner + ".callouts");
            try
            {
                var request = AccessTools.Method(tracker, "RequestCallout", new[] { typeof(Pawn), typeof(RulePackDef), typeof(Verse.Grammar.GrammarRequest) });
                var attached = AccessTools.Method(tracker, "createAttachedCalloutText", new[] { typeof(Thing), typeof(string), typeof(Color), typeof(float) });
                var mote = AccessTools.Method(typeof(MoteMaker), "ThrowText", new[] { typeof(Vector3), typeof(Map), typeof(string), typeof(float) });
                if (request == null || attached == null || mote == null) throw new MissingMethodException();
                calloutHarmony.Patch(request, prefix: new HarmonyMethod(typeof(OptionalDialogue), nameof(CalloutPrefix)), finalizer: new HarmonyMethod(typeof(OptionalDialogue), nameof(CalloutFinalizer)));
                calloutHarmony.Patch(attached, prefix: new HarmonyMethod(typeof(OptionalDialogue), nameof(AttachedPrefix)));
                calloutHarmony.Patch(mote, prefix: new HarmonyMethod(typeof(OptionalDialogue), nameof(MotePrefix)));
                messages.Add("Callouts text interface detected (experimental on 1.7)");
            }
            catch { calloutHarmony.UnpatchAll(Integration.Owner + ".callouts"); messages.Add("Callouts interface unknown: native display preserved"); }
        }
        Status = messages.Count == 0 ? "No optional dialogue mods detected." : string.Join(". ", messages) + ".";
    }
    private static bool EnsuePrefix() => !DialogueReadScope.Active;
    public static string ReadText(LogEntry entry, Pawn pawn)
    {
        if (speakUpPresent && !speakUpGuarded) return "A social interaction occurred; its text could not be safely captured.";
        return DialogueReadScope.Read(() => TextPolicy.Clean(entry.ToGameStringFromPOV(pawn, false)));
    }
    public static FallbackCandidate? SocialCandidate(LogEntry entry, string text, Preferences settings)
    {
        var def = AccessTools.Field(entry.GetType(), "intDef")?.GetValue(entry) as InteractionDef;
        if (def == null) return null;
        string owner = def.modContentPack?.PackageIdPlayerFacing ?? "";
        string source = ""; bool narration = true;
        // SpeakUp patches base/other mods' grammar too; tagged rules identify its reply-capable grammar.
        bool authored = speakUpGuarded && (owner.Equals("jpt.speakup", StringComparison.OrdinalIgnoreCase) ||
            def.modContentPack?.Name.StartsWith("SpeakUp", StringComparison.OrdinalIgnoreCase) == true ||
            def.logRulesInitiator != null && (AccessTools.Field(typeof(Verse.Grammar.RulePack), "rulesStrings")?.GetValue(def.logRulesInitiator) as IEnumerable<string>)?.Any(r => r.Contains("tag=")) == true);
        if (authored) { source = "speakup"; narration = false; }
        else if (owner.Equals("VanillaExpanded.VanillaSocialInteractionsExpanded", StringComparison.OrdinalIgnoreCase)) source = "vsie";
        else if (owner.Contains("rumorhasit", StringComparison.OrdinalIgnoreCase) || (AccessTools.Field(typeof(InteractionDef), "workerClass")?.GetValue(def) as Type)?.Namespace == "Rumor_Code") source = "rumor";
        if (source.Length == 0) return null;
        return new FallbackCandidate { Source = source, Text = text, Definition = def.defName, Narration = narration, Language = LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "English" };
    }
    public static string EventLabel(LogEntry entry) => (AccessTools.Field(entry.GetType(), "intDef")?.GetValue(entry) as InteractionDef)?.label ?? "social interaction";
    private static void CalloutPrefix(Pawn __0, RulePackDef __1, out CalloutFrame? __state)
    { __state = callout; callout = new CalloutFrame { Pawn = __0, Definition = __1?.defName ?? "" }; }
    private static Exception? CalloutFinalizer(Exception? __exception, CalloutFrame? __state) { callout = __state; return __exception; }
    private static bool AttachedPrefix(Thing __0, string __1) => __0 is not Pawn pawn || PassThrough(pawn, __1);
    private static bool MotePrefix(string __2) => callout == null || PassThrough(callout.Pawn, __2);
    private static bool PassThrough(Pawn pawn, string text)
    {
        if (callout == null || callout.Pawn != pawn) return true;
        try
        {
            var game = SYMGame.Current;
            if (game == null) return true;
            var candidate = new FallbackCandidate { Source = "callouts", Text = TextPolicy.Clean(text), Definition = callout.Definition,
                Language = LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "English" };
            return !game.ObserveCallout(pawn, candidate);
        }
        catch { return true; } // Adapter failures must never swallow the original display.
    }
}
