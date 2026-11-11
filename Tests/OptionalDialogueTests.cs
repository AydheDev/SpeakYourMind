using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using SpeakYourMind.Core;
using Verse;
using static SpeakYourMind.Tests.Suite;

namespace SpeakYourMind.Tests;

public static class OptionalDialogueTests
{
    public static void Run() { Eligibility(); Continuations(); ReadScope(); SocialAdapter(); Journal(); }
    private static void Eligibility()
    {
        var prefs = new Preferences(); var ctx = Prompts.Preview(prefs);
        Check(!prefs.DebugLogging && !prefs.SpeakUpFallback && !prefs.VSIEFallback && !prefs.RumorFallback && !prefs.CalloutsFallback, "debug and optional integrations are opt-in");
        var line = new FallbackCandidate { Source = "speakup", Text = "Pass the flint." };
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("disabled"), "installed text is not automatically enabled");
        prefs.SpeakUpFallback = prefs.VSIEFallback = prefs.RumorFallback = prefs.CalloutsFallback = true;
        foreach (string source in new[] { "speakup", "vsie", "rumor", "callouts" })
        { line.Source = source; Check(FallbackPolicy.Rejection(line, ctx, prefs) == "", "enabled source accepted: " + source); }
        ctx.ColonyRule = "Every word ends in nya";
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("instructions"), "colony annotation cannot be bypassed by optional text");
        ctx.ColonyRule = ""; ctx.Profiles.Add(new Profile { PawnId = "listener", Annotation = "Use gestures" });
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("instructions"), "listener annotation also protects shared exchange");
        ctx.Profiles.Clear(); ctx.Pawns[0].Animal = true;
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("species"), "human mod dialogue not assigned to animals");
        ctx.Pawns[0].Animal = false; ctx.Pawns[0].Mechanoid = true;
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("species"), "human mod dialogue not assigned to mechanoids");
        ctx.Pawns[0].Mechanoid = false; ctx.Language = "French";
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("language"), "different language source declined");
        ctx.Language = "English"; prefs.MaxCharacters = 3;
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("character"), "optional text must satisfy current cap");
        prefs.MaxCharacters = 0; ctx.Style = ExpressionStyle.Dialogue; line.Narration = true;
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("style"), "event summaries excluded in dialogue-only mode");
        ctx.Style = ExpressionStyle.Narration; line.Narration = false;
        Check(FallbackPolicy.Rejection(line, ctx, prefs).Contains("style"), "spoken lines excluded in narration-only mode");
        var saved = SYMJson.Read<Preferences>(SYMJson.Write(prefs));
        Check(saved.SpeakUpFallback && saved.CalloutsFallback && saved.VSIEFallback && saved.RumorFallback, "adapter preferences roundtrip");
    }
    private static void Continuations()
    {
        var prefs = new Preferences();
        for (int limit = 2; limit <= 4; limit++)
        {
            prefs.MaxTurns = limit;
            Check(ConversationPolicy.Turns(prefs, true, false) == limit, "normal turn allowance " + limit);
            Check(ConversationPolicy.Turns(prefs, true, true) + 1 == limit, "fallback consumes one total exchange turn " + limit);
        }
        Check(ConversationPolicy.ContinueAfterFallback(false, true, false, true, prefs), "shown fallback may receive fresh reply");
        Check(!ConversationPolicy.ContinueAfterFallback(true, true, false, true, prefs), "discarded fallback never starts another request");
        Check(!ConversationPolicy.ContinueAfterFallback(false, true, true, true, prefs), "continuation cannot form unbounded loop");
        Check(!ConversationPolicy.ContinueAfterFallback(false, true, false, false, prefs), "no invented listener");
        prefs.FollowUps = false;
        Check(ConversationPolicy.Turns(prefs, true, false) == 1 && !ConversationPolicy.ContinueAfterFallback(false, true, false, true, prefs), "follow-up toggle disables both paths");
        var delivery = new ExchangeDelivery(); delivery.Accept(new[] { new Turn { PawnId = "a", Text = "Careful!" } }, true);
        delivery.Advance(1, false, true, true); delivery.MarkRendered(1);
        Check(!delivery.Accept(new[] { new Turn { PawnId = "a", Text = "Late conflicting answer" } }), "original completion cannot replace fallback before fresh continuation");
        Check(delivery.CurrentWasDisplayed, "shown fallback can be distinguished from an unrendered selection");
        var unseen = new ExchangeDelivery(); unseen.Accept(new[] { new Turn { PawnId = "a", Text = "Unseen" } }, true);
        unseen.Advance(0, false, true, true); unseen.Advance(10, false, true, true);
        Check(!unseen.CurrentWasDisplayed && !ConversationPolicy.ContinueAfterFallback(!unseen.CurrentWasDisplayed, true, false, true, new Preferences()), "unrendered fallback expiry cannot trigger a follow-up");
    }
    private static void ReadScope()
    {
        var prefix = AccessTools.Method(typeof(OptionalDialogue), "EnsuePrefix");
        Check((bool)prefix.Invoke(null, null)!, "native SpeakUp continuation permitted outside our read");
        DialogueReadScope.Read(() =>
        {
            Check(!(bool)prefix.Invoke(null, null)!, "reading text cannot schedule SpeakUp gameplay interaction");
            DialogueReadScope.Read(() => { Check(DialogueReadScope.Active, "nested read guarded"); return 0; });
            Check(DialogueReadScope.Active, "inner scope leaves outer guard intact"); return 0;
        });
        Throws<InvalidOperationException>(() => DialogueReadScope.Read<int>(() => throw new InvalidOperationException()), "failed text read propagates");
        Check(!DialogueReadScope.Active && (bool)prefix.Invoke(null, null)!, "read failure restores native continuation permission");
    }
    private static void SocialAdapter()
    {
        var guarded = AccessTools.Field(typeof(OptionalDialogue), "speakUpGuarded");
        object old = guarded.GetValue(null)!;
        try
        {
            guarded.SetValue(null, true);
            var def = new InteractionDef { defName = "Chitchat", label = "chat", logRulesInitiator = new Verse.Grammar.RulePack() };
            AccessTools.Field(typeof(Verse.Grammar.RulePack), "rulesStrings").SetValue(def.logRulesInitiator, new System.Collections.Generic.List<string> { "r_logentry(tag=ChildReply)->Hi!" });
            var entry = (PlayLogEntry_Interaction)RuntimeHelpers.GetUninitializedObject(typeof(PlayLogEntry_Interaction));
            AccessTools.Field(typeof(PlayLogEntry_Interaction), "intDef").SetValue(entry, def);
            var candidate = OptionalDialogue.SocialCandidate(entry, "Hi!", new Preferences());
            Check(candidate?.Source == "speakup" && candidate.Text == "Hi!" && !candidate.Narration, "patched child/extension grammar captured without rerolling or invoking worker");
            guarded.SetValue(null, false);
            Check(OptionalDialogue.SocialCandidate(entry, "Hi!", new Preferences()) == null, "unguarded unknown SpeakUp interface not accepted");
        }
        finally { guarded.SetValue(null, old); }
    }
    private static void Journal()
    {
        string root = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "debug-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var journal = new DebugJournal(root); journal.AddSecret("fake-private-test-token");
            journal.Record(new DebugEvent { Stage = "request", Request = "r1", SystemPrompt = "Write a response", Context = "colony fake-private-test-token", Model = "test-model" });
            journal.Record(new DebugEvent { Stage = "response_rejected", Request = "r1", Response = "Bearer secretvalue", Detail = "too long" });
            journal.Record(new DebugEvent { Stage = "displayed", Request = "r1", Text = "Good morning.", Source = "fallback:speakup" });
            journal.Record(new DebugEvent { Stage = "player_note", Text = "Wrong tone" });
            journal.Dispose(); journal.Completion.GetAwaiter().GetResult();
            string json = File.ReadAllText(Path.Combine(root, journal.Session + ".jsonl"));
            string readable = File.ReadAllText(Path.Combine(root, journal.Session + ".txt"));
            var events = json.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(SYMJson.Read<DebugEvent>).ToList();
            Check(events.Count == 4 && events.Select(e => e.Sequence).SequenceEqual(new long[] { 1, 2, 3, 4 }), "journal drains complete ordered records on close");
            Check(events[2].Text == "Good morning." && events[2].Source == "fallback:speakup", "debug distinguishes source from AI");
            Check(!json.Contains("fake-private-test-token") && !readable.Contains("fake-private-test-token") && !json.Contains("secretvalue"), "secrets scrubbed in machine and human logs");
            Check(readable.Contains("SYSTEM PROMPT:") && readable.Contains("RAW RESPONSE:") && readable.Contains("too long") && readable.Contains("Wrong tone"), "readable report includes tuning evidence and notes");
            journal.Record(new DebugEvent { Text = "after closing" });
            Check(!File.ReadAllText(Path.Combine(root, journal.Session + ".jsonl")).Contains("after closing"), "disabled journal does not accept late callbacks");
            var limited = new DebugJournal(root, 1);
            limited.Record(new DebugEvent { Response = new string('a', 1024 * 1024) }); limited.Record(new DebugEvent { Text = "small line after oversized record" }); limited.Dispose(); limited.Completion.GetAwaiter().GetResult();
            Check(limited.Status.Contains("limit") && File.ReadAllText(Path.Combine(root, limited.Session + ".jsonl")).Contains("size_limit"), "disk limit is explicit rather than silent truncation");
            Check(!File.ReadAllText(Path.Combine(root, limited.Session + ".jsonl")).Contains("small line after"), "size limit never resumes silently for smaller records");
            string occupied = Path.Combine(root, "occupied"); File.WriteAllText(occupied, "test");
            var denied = new DebugJournal(occupied); denied.Record(new DebugEvent { Text = "test" }); denied.Dispose(); denied.Completion.GetAwaiter().GetResult();
            Check(denied.Status.Contains("Could not write"), "file failure is isolated from game");
        }
        finally { foreach (var file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
