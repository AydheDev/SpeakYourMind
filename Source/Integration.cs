using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SpeakYourMind;

public static class Integration
{
    public const string Owner = "aydhe.speakyourmind";
    public static string Status = "Checking…";
    public static bool Ready;
    private static Type? bubble;
    private static FieldInfo? dictionary;
    private static PropertyInfo? entry;
    private static MethodInfo? rebuild, canRender;
    private static ConstructorInfo? constructor;
    public static bool Active => Ready && SYMMod.Instance.Settings.Preferences.Enabled && SYMGame.Current?.StateHealthy == true;

    public static void Initialize()
    {
        try
        {
            var ids = LoadedModManager.RunningModsListForReading.Select(m => m.PackageIdPlayerFacing.ToLowerInvariant()).ToList();
            bool expanded = ids.Contains("vengie.bubblesexpanded"), original = ids.Contains("jaxe.bubbles");
            if (expanded == original) { Status = expanded ? "Enable only one bubble mod, not both." : "Enable Interaction Bubbles Expanded and restart."; return; }
            bubble = AccessTools.TypeByName("Bubbles.Core.Bubble");
            var bubbler = AccessTools.TypeByName("Bubbles.Core.Bubbler");
            if (bubble == null || bubbler == null) throw new MissingMemberException();
            constructor = bubble.GetConstructor(new[] { typeof(Pawn), typeof(LogEntry) });
            dictionary = AccessTools.Field(bubbler, "Dictionary"); entry = AccessTools.Property(bubble, "Entry");
            rebuild = AccessTools.Method(bubble, "Rebuild"); canRender = AccessTools.Method(bubbler, "CanRender");
            var add = AccessTools.Method(bubbler, "Add", new[] { typeof(LogEntry) });
            var fade = AccessTools.Method(bubble, "GetFade");
            var draw = AccessTools.Method(bubble, "Draw", new[] { typeof(Vector2), typeof(bool), typeof(float) });
            if (constructor == null || dictionary == null || entry == null || rebuild == null || canRender == null || add == null || fade == null || draw == null) throw new MissingMemberException();
            var harmony = new Harmony(Owner);
            harmony.Patch(add, prefix: new HarmonyMethod(typeof(Integration), nameof(BubbleAddPrefix)));
            harmony.Patch(fade, prefix: new HarmonyMethod(typeof(Integration), nameof(FadePrefix)));
            harmony.Patch(draw, postfix: new HarmonyMethod(typeof(Integration), nameof(DrawPostfix)));
            // Observe completed game events; never suppress or replace gameplay methods.
            harmony.Patch(AccessTools.Method(typeof(PlayLog), "Add"), postfix: new HarmonyMethod(typeof(Integration), nameof(LogPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "PostApplyDamage"), postfix: new HarmonyMethod(typeof(Integration), nameof(DamagePostfix)));
            harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "Notify_UsedVerb"), postfix: new HarmonyMethod(typeof(Integration), nameof(AttackPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Verse.Profile.MemoryUtility), "ClearAllMapsAndWorld"), prefix: new HarmonyMethod(typeof(Integration), nameof(WorldClearPrefix)));
            harmony.Patch(AccessTools.Method(typeof(Pawn), "Kill"), prefix: new HarmonyMethod(typeof(Integration), nameof(DeathPrefix)), postfix: new HarmonyMethod(typeof(Integration), nameof(DeathPostfix)));
            harmony.Patch(AccessTools.Method(typeof(ResearchManager), "FinishProject"), postfix: new HarmonyMethod(typeof(Integration), nameof(ResearchPostfix)));
            harmony.Patch(AccessTools.Method(typeof(LetterStack), "ReceiveLetter", new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }), postfix: new HarmonyMethod(typeof(Integration), nameof(LetterPostfix)));
            // Expanded's click-through navigation expects a vanilla log entry. Route only our entries to our history.
            var navigation = AccessTools.TypeByName("Bubbles.Access.BubbleNavigation");
            var open = navigation == null ? null : AccessTools.Method(navigation, "Open");
            if (open != null) harmony.Patch(open, prefix: new HarmonyMethod(typeof(Integration), nameof(NavigationPrefix)));
            Ready = true;
            try { OptionalDialogue.Initialize(); } catch { OptionalDialogue.Status = "Optional adapter initialization failed; base integration remains available."; }
            Status = expanded ? "Expanded connected" : "Original interface detected (experimental on 1.7)";
            if (ids.Any(id => id.Contains("rimtalk") || id.Contains("rimdialogue")))
                Status += ". Another dialogue producer is active; disable its bubble generation to prevent competing text.";
        }
        catch
        {
            new Harmony(Owner).UnpatchAll(Owner); Ready = false;
            Status = "Unsupported bubble interface. SpeakYourMind is inactive; existing bubbles are unchanged.";
            Log.Warning("[SpeakYourMind] " + Status);
        }
    }
    public static bool Rendering => Ready && (bool)(canRender?.Invoke(null, null) ?? false);
    public static object Add(Pawn pawn, ExpressionEntry log)
    {
        var dict = (IDictionary)dictionary!.GetValue(null)!;
        if (!dict.Contains(pawn)) dict[pawn] = Activator.CreateInstance(typeof(List<>).MakeGenericType(bubble!));
        var instance = constructor!.Invoke(new object[] { pawn, log });
        ((IList)dict[pawn]!).Add(instance); return instance;
    }
    public static void Refresh(object instance) => rebuild!.Invoke(instance, null);
    public static void Remove(Pawn pawn, object instance)
    {
        if (dictionary?.GetValue(null) is IDictionary dict && dict.Contains(pawn))
        { var list = (IList)dict[pawn]!; list.Remove(instance); if (list.Count == 0) dict.Remove(pawn); }
    }
    private static bool BubbleAddPrefix() => !Active;
    private static bool FadePrefix(object __instance, ref float __result)
    {
        if (entry?.GetValue(__instance) is not ExpressionEntry own) return true;
        __result = own.Alive ? 1f : 0f; return false;
    }
    private static void DrawPostfix(object __instance, bool __result)
    {
        if (__result && Event.current.type == EventType.Repaint && entry?.GetValue(__instance) is ExpressionEntry own)
        {
            try { own.OnRendered?.Invoke(); }
            catch { Log.WarningOnce("[SpeakYourMind] A displayed line could not be recorded.", 17393013); }
        }
    }
    private static bool NavigationPrefix(Pawn __0, LogEntry __1)
    {
        if (__1 is not ExpressionEntry) return true;
        Find.WindowStack.Add(new HistoryWindow(__0.ThingID)); return false;
    }
    private static void Observe(Action<SYMGame> action)
    {
        if (!Active) return;
        try { action(SYMGame.Current!); } catch { Log.WarningOnce("[SpeakYourMind] An event could not be observed. Gameplay was left unchanged.", 17393011); }
    }
    private static void LogPostfix(LogEntry __0)
    {
        try { if (SYMGame.Current is { } game && (Active || SYMMod.Instance.Settings.Preferences.DebugLogging)) game.ObserveSocial(__0); }
        catch { Log.WarningOnce("[SpeakYourMind] A social event could not be captured.", 17393015); }
    }
    private static void DamagePostfix(Pawn_HealthTracker __instance, DamageInfo __0, float __1)
    {
        if (__1 <= 0) return;
        Observe(g => { if (AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").GetValue(__instance) is Pawn pawn) g.ObserveDamage(pawn, __0); });
    }
    private static void AttackPostfix(Pawn_HealthTracker __instance, Verb __0, LocalTargetInfo __1)
    {
        if (!__0.verbProps.violent) return;
        Observe(g => { if (AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").GetValue(__instance) is Pawn pawn) g.ObserveAttack(pawn, __1); });
    }
    private static void WorldClearPrefix()
    {
        try { SYMGame.Current?.Shutdown(); }
        catch { Log.Warning("[SpeakYourMind] Cleanup could not finish; the game will continue unloading normally."); }
    }
    private static void DeathPrefix(Pawn __instance, out List<DeathRelation>? __state)
    {
        __state = null;
        if (!Active || __instance.Dead) return;
        try { __state = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => p != __instance && p.relations != null)
            .Select(p => new DeathRelation(p, string.Join(", ", p.GetRelations(__instance).Select(r => r.label)))).Where(r => r.Relationship.Length > 0).ToList(); } catch { }
    }
    private static void DeathPostfix(Pawn __instance, List<DeathRelation>? __state)
    { if (__instance.Dead && __state != null) Observe(g => g.ObserveDeath(__instance, __state)); }
    private static void ResearchPostfix(ResearchProjectDef __0) => Observe(g => g.ObserveColony("Research completed: " + __0.LabelCap));
    private static void LetterPostfix(Letter __0) => Observe(g => g.ObserveColony(TextPolicySafe(__0.Label.ToString()) + (__0 is ChoiceLetter choice ? ": " + TextPolicySafe(AccessTools.Field(typeof(ChoiceLetter), "text")?.GetValue(choice)?.ToString() ?? "") : "")));
    private static string TextPolicySafe(string value) => Core.TextPolicy.Clean(value).Truncate(1200);
}

// This entry is presentation-only. It is NEVER added to PlayLog or BattleLog.
public sealed class ExpressionEntry : LogEntry
{
    public string Text = "…";
    public bool Alive = true;
    public Action? OnRendered;
    private readonly Pawn pawn;
    public ExpressionEntry(Pawn pawn) : base(null) { this.pawn = pawn; }
    protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog) => Text;
    public override bool Concerns(Thing thing) => thing == pawn;
    public override IEnumerable<Thing> GetConcerns() { yield return pawn; }
    public void SetText(string text) { Text = text; ResetCache(); }
}
public sealed record DeathRelation(Pawn Pawn, string Relationship);
