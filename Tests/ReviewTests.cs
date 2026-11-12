using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using SpeakYourMind.Core;
using static SpeakYourMind.Tests.Suite;

namespace SpeakYourMind.Tests;

public static class ReviewTests
{
    public static void Run() { NotesAndFallbacks(); Needs(); MemoryProgress(); History(); Usage(); Capture(); Models(); Transport().GetAwaiter().GetResult(); }
    private static void NotesAndFallbacks()
    {
        var prefs = new Preferences(); var state = new SaveState { ColonyRule = "Use natural metaphors." };
        var a = state.Profile("a", "Ona", true); var b = state.Profile("b", "Tess", true);
        a.Annotation = "End every spoken word with nya."; b.Annotation = "Speak in short questions.";
        a.Voice = "A quiet carpenter."; a.Memories.Add(new Memory { Fact = "A sister died.", Importance = 1, Scored = true });
        var restored = SaveCodec.Read(SYMJson.Write(state));
        Check(restored.Profiles[0].Annotation == a.Annotation && restored.Profiles[1].Annotation == b.Annotation, "individual notes survive save independently");
        Check(restored.ColonyRule == state.ColonyRule, "colony rule is separate from both pawn notes");
        a.Voice = "An older quiet carpenter."; ProfilePolicy.ApplyScore(a.Memories[0], 0.1f, "test");
        Check(a.Annotation.EndsWith("nya.") && b.Annotation == "Speak in short questions.", "automatic voice and score updates preserve notes");
        var context = Prompts.Preview(prefs); context.Pawns[0].Id = "a"; context.Profiles.Add(a);
        var native = new FallbackCandidate { Source = "game", Text = "They chatted.", Narration = true };
        Check(FallbackPolicy.Rejection(native, context, prefs).Length > 0, "original interaction cannot bypass personal note");
        a.Annotation = ""; context.ColonyRule = "End with nya.";
        Check(FallbackPolicy.Rejection(native, context, prefs).Length > 0, "original interaction cannot bypass colony rule");
        context.ColonyRule = "";
        Check(FallbackPolicy.Rejection(native, context, prefs) == "", "compatible original text can use checked fallback path");
        context.Language = "Polish"; Check(FallbackPolicy.Rejection(native, context, prefs).Length > 0, "original fallback checks language");
        context.Language = "English"; context.Pawns[0].Animal = true;
        Check(FallbackPolicy.Rejection(native, context, prefs).Length > 0, "original fallback checks animal speech");
        Check(FallbackPolicy.Generic(context.Pawns[0], a, prefs, "").Count == 0, "animals get no human generic filler");
        context.Pawns[0].Animal = false; context.Pawns[0].CanSpeak = false;
        Check(FallbackPolicy.Rejection(native, context, prefs).Length > 0, "speech incapacity excludes unverified authored text");
        Check(FallbackPolicy.Generic(context.Pawns[0], a, prefs, "").Contains("Pauses a moment."), "non-speaking human uses body language");
        context.Pawns[0].CanSpeak = true; a.Annotation = "End with nya.";
        Check(FallbackPolicy.Generic(context.Pawns[0], a, prefs, "").Count == 0, "uncached note never falls back to unannotated generic words");
        a.Fallbacks.Add("Hmm nya."); a.FallbackSignature = Prompts.Signature(prefs, "", a); a.FallbackCommunication = "";
        Check(FallbackPolicy.Generic(context.Pawns[0], a, prefs, "").Single() == "Hmm nya.", "matching cached note-aware fallback is eligible");
        a.Annotation = "Only questions.";
        Check(FallbackPolicy.Generic(context.Pawns[0], a, prefs, "").Count == 0, "editing personal note invalidates old cached filler immediately");
        prefs.Style = context.Style = ExpressionStyle.Dialogue; a.Annotation = "";
        Check(FallbackPolicy.Rejection(native, context, prefs).Length > 0, "narrative game fallback cannot violate dialogue preference");
        prefs.Style = context.Style = ExpressionStyle.Hybrid; prefs.MaxCharacters = 3;
        Check(FallbackPolicy.Rejection(native, context, prefs).Length > 0, "original fallback checks length");
    }
    private static void Needs()
    {
        var watch = new NeedWatch();
        Check(!watch.Observe("a:Food", .30f, false, 0), "comfortable need is quiet");
        Check(watch.Observe("a:Food", .24f, false, 6), "small threshold crossing emits hunger");
        Check(!watch.Observe("a:Food", .26f, false, 20) && !watch.Observe("a:Food", .24f, false, 40), "threshold jitter does not spam");
        Check(watch.Observe("a:Food", .09f, false, 50), "severe deterioration can get another reaction");
        Check(!watch.Observe("a:Food", .09f, false, 100), "unchanged severe need waits");
        Check(watch.Observe("a:Food", .09f, false, 240), "sustained need permits spaced reminder");
        watch.Observe("a:Food", .40f, false, 280);
        Check(watch.Observe("a:Food", .24f, false, 320), "recovery resets hysteresis");
        Check(!watch.Observe("b:Bladder", .70f, true, 0) && watch.Observe("b:Bladder", .76f, true, 6), "high-bad mod need crossing is supported");
        Check(!watch.Observe("b:Bladder", float.NaN, true, 40), "broken mod need is ignored");
        Check(NeedWatch.Satisfaction(.9f, true) < .11f, "high-bad need urgency uses inverted satisfaction");
    }
    private static void MemoryProgress()
    {
        var p = new Profile { PawnId = "a", Annotation = "Keep my accent." };
        var loss = new Memory { Id = "loss", Fact = "Sister died." }; var food = new Memory { Id = "food", Fact = "Ate a pear." };
        p.Memories.AddRange(new[] { loss, food });
        var response = SYMJson.Read<ProfileResponse>("{\"voice\":\"Patient.\",\"scores\":[{\"id\":\"food\",\"importance\":0.1,\"reason\":\"Routine\"}]}");
        MemoryWork.CompleteScores(p, new[] { "loss", "food" }, response.Scores, 1);
        Check(food.Scored && !loss.Scored && loss.ScoreAttempts == 1, "partial score response makes progress and tracks missing ID");
        Check(!MemoryWork.Pending(p, 1).Any(), "missing score is deferred instead of spinning");
        Check(MemoryWork.Pending(p, 1.3).Single() == loss, "only missing IDs enter retry batch");
        MemoryWork.CompleteScores(p, new[] { "loss" }, Array.Empty<MemoryScore>(), 1.3);
        MemoryWork.CompleteScores(p, new[] { "loss" }, null, 2);
        Check(!MemoryWork.Pending(p, 1000).Any(), "three incomplete score replies stop automatic retry churn");
        Check(!MemoryPolicy.Expired(loss, 1000, new Preferences()), "unscored bereavement is retained safely");
        Check(p.Annotation == "Keep my accent.", "score completion never rewrites personal note");
        MemoryWork.Retry(p); Check(MemoryWork.Pending(p, 1000).Single() == loss, "player can retry deferred IDs without rescoring completed ones");
        MemoryWork.CompleteScores(p, new[] { "loss" }, new[] { new MemoryScore { Id = "loss", Importance = 1 } }, 1000);
        Check(loss.Scored && loss.Importance == 1, "retried bereavement can become permanent core memory");
        p.Voice = "Patient carpenter."; p.LastProfileFacts = "facts"; p.FallbackSignature = "signature";
        Check(!MemoryWork.RefreshVoice(p, "facts", "signature", 5000), "time alone does not regenerate an unchanged personality");
        Check(MemoryWork.RefreshVoice(p, "new facts", "signature", 5000), "changed biography requests profile refresh");
        Check(MemoryWork.RefreshVoice(p, "facts", "new rules", 5000), "changed notes request fresh matching generic replies");
        Check(FailurePolicy.LocalResponse(new FormatException()) && FailurePolicy.LocalResponse(new JsonException()), "invalid reply stays local to request");
        Check(!FailurePolicy.LocalResponse(new ProviderException("Unavailable")) && !FailurePolicy.LocalResponse(new HttpRequestException()), "transport errors retain service backoff");
        var saved = new SaveState(); saved.Profiles.Add(p); loss.ScoreAttempts = 2; loss.NextScoreDay = 1001;
        var read = SaveCodec.Read(SYMJson.Write(saved));
        Check(read.Profiles[0].Memories[0].ScoreAttempts == 2 && read.Profiles[0].Memories[0].NextScoreDay == 1001, "retry progress survives save/load");
    }
    private static void History()
    {
        var state = new SaveState(); var profile = state.Profile("a", "Ona", true);
        for (int i = 0; i < 100000; i++) state.Record(new TranscriptEntry { Id = i.ToString(), ExchangeId = (i / 3).ToString(), SpeakerId = "a", SpeakerName = "Ona", Text = "line " + i, Day = i / 1000d, Participants = new() { "a" } });
        var query = new ConversationQuery(); query.Update(state, "a", "", "", 100, 0); int builds = query.Rebuilds;
        profile.Voice = "Revised voice"; profile.Annotation = "Nya"; state.Touch();
        Check(!query.Update(state, "a", "", "", 100, 0) && query.Rebuilds == builds, "profile and note edits do not rebuild 100k-line conversation index");
        state.Record(new TranscriptEntry { ExchangeId = "33333", SpeakerId = "a", Text = "Reply needle", Day = 101, Participants = new() { "a" } });
        Check(query.Update(state, "a", "", "", 101, 0) && query.Rebuilds == builds, "append extends conversation index without full regroup");
        Check(query.Matches[0].Lines.Count == 2 && query.Matches[0].Lines.Last().Text == "Reply needle", "appended reply joins existing exchange in order");
        query.Update(state, "a", "needle", "", 101, 0);
        Check(query.Matches.Count == 1 && query.Matches[0].Lines.Count == 2, "search retains other lines in matched exchange");
        state.Record(new TranscriptEntry { ExchangeId = "new", SpeakerId = "a", Text = "needle too", Day = 102, Participants = new() { "a" } });
        builds = query.Rebuilds; query.Update(state, "a", "needle", "", 102, 0);
        Check(query.Matches.Count == 2 && query.Rebuilds == builds, "filtered append updates only new conversation");
        state.Prune(103, new Preferences { PawnHistoryDays = 1, ColonyHistoryDays = 1 });
        query.Update(state, "a", "needle", "", 103, 0);
        Check(query.Matches.Count == 1 && query.Rebuilds > builds, "retention removal invalidates old index references");
        state.Transcript[0].Text = "Changed"; state.TouchTranscript(); query.Update(state, "a", "needle", "", 103, 0);
        Check(query.Matches.Count == 0, "explicit transcript edits invalidate search cache");
    }
    private static void Usage()
    {
        var metrics = new RequestMetrics(); var shown = new RequestTrace(RequestPurpose.Dialogue);
        metrics.Start(shown); metrics.Finish(true, false, 2, 100, 40, shown); metrics.Outcome(shown, true); metrics.Outcome(shown, false);
        var discarded = new RequestTrace(RequestPurpose.Dialogue);
        metrics.Start(discarded); metrics.Outcome(discarded, false); metrics.Finish(true, false, 3, 200, 80, discarded);
        var memory = new RequestTrace(RequestPurpose.Memory); metrics.Start(memory); metrics.Finish(true, false, 5, 500, 100, memory);
        var missing = new RequestTrace(RequestPurpose.Dialogue); metrics.Start(missing); metrics.Outcome(missing, false); metrics.Finish(false, true, 1, trace: missing);
        var d = metrics.For(RequestPurpose.Dialogue); var m = metrics.For(RequestPurpose.Memory);
        Check(d.VisibleRequests == 1 && d.DiscardedRequests == 2, "visible then removed exchange counts once as useful");
        Check(d.DiscardedInput == 200 && d.DiscardedOutput == 80, "late provider completion attaches usage to discarded request");
        Check(d.ResponsesWithUsage == 2 && d.Started == 3, "cancelled missing usage remains explicitly unreported");
        Check(m.InputTokens == 500 && d.InputTokens == 300 && m.AverageSeconds == 5 && d.AverageSeconds == 2, "dialogue and memory usage and latency remain separate");
    }
    private static void Capture()
    {
        var samples = new PromptSamples(); var context = Prompts.Preview(new Preferences());
        context.Profiles.Add(new Profile { PawnId = "preview", Annotation = "Nya" });
        samples.Capture("first", context, Prompts.ExpressionSystem, "model", 1);
        context.Event = "Later raid"; context.Profiles[0].Annotation = "Different rule";
        var sample = samples.Find("first")!;
        Check(!sample.Snapshot().Event.Contains("raid") && sample.Snapshot().Profiles[0].Annotation == "Nya", "prompt comparison freezes original context and personal note");
        var clone = sample.Snapshot(); clone.Pawns[0].Name = "Changed";
        Check(sample.Snapshot().Pawns[0].Name == "Ona", "preview context cannot mutate captured example");
        var entry = new TranscriptEntry { Text = "Original" }; samples.Displayed("first", entry); entry.Text = "Changed";
        Check(sample.Displayed[0].Text == "Original", "captured displayed line is detached from live transcript");
        for (int i = 0; i < 30; i++) samples.Capture(i.ToString(), context, Prompts.ExpressionSystem, "model", i);
        Check(samples.Items.Count == 24 && samples.Find("first") == null, "debug replay cache is bounded to latest 24 requests");
        samples.Clear(); Check(samples.Items.Count == 0, "disabling debug clears captured context");
    }
    private static void Models()
    {
        var c = new Connection { Model = "gpt-6.1-sol", Reasoning = "none" };
        Check(ModelCapabilities.Error(c).Length > 0 && !ModelCapabilities.Options(c).Contains("none"), "Sol rejects unsupported none before spending tokens");
        c.Reasoning = "low"; Check(ModelCapabilities.Error(c) == "", "Sol low is supported");
        c.Model = "gpt-6.1-terra"; c.Reasoning = "none"; Check(ModelCapabilities.Error(c) == "", "Terra none is supported");
        c.Model = "gpt-6-luna-2026-11-01"; Check(ModelCapabilities.Options(c).Contains("max"), "dated known-model snapshots inherit capability menu");
        c.Model = "custom-model"; c.Reasoning = "custom-effort"; Check(ModelCapabilities.Known(c) == null && ModelCapabilities.Error(c) == "", "unknown custom model remains configurable");
        c.Model = "gpt-6.1-sol"; c.BaseUrl = "http://localhost:1234/v1";
        Check(ModelCapabilities.Known(c) == null, "local aliases are not forced into public OpenAI capabilities");
        var old = SaveCodec.Read("{\"Version\":1,\"Profiles\":[{\"PawnId\":\"a\"}],\"Transcript\":[],\"ColonyEvents\":[]}");
        Check(old.Profiles[0].Annotation == "" && old.Profiles[0].LastWorkDay == -1, "old save gains safe defaults without losing data");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Bad = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(Bad ? "{\"choices\":[{\"message\":{\"content\":\"broken JSON\"}}],\"usage\":{\"prompt_tokens\":50,\"completion_tokens\":10}}" : "{\"choices\":[{\"message\":{\"content\":\"{\\\"turns\\\":[{\\\"pawnId\\\":\\\"preview\\\",\\\"text\\\":\\\"Done.\\\"}]}\"}}]}") });
    }
    private static async Task Transport()
    {
        var handler = new Handler(); using var client = new ProviderClient(handler);
        var c = new Connection { Provider = ProviderKind.Compatible, BaseUrl = "http://localhost:1234/v1", Model = "local" };
        var usage = new RequestTrace(RequestPurpose.Dialogue); var ctx = Prompts.Preview(new Preferences());
        string raw = await client.Generate(c, "", Prompts.ExpressionSystem, SYMJson.Write(ctx), CancellationToken.None, usage);
        Throws<JsonException>(() => Prompts.ParseExpression(raw, ctx), "malformed model text rejected after successful transport");
        client.Metrics.Outcome(usage, false); handler.Bad = false;
        raw = await client.Generate(c, "", Prompts.ExpressionSystem, SYMJson.Write(ctx), CancellationToken.None, new RequestTrace(RequestPurpose.Dialogue));
        Check(Prompts.ParseExpression(raw, ctx).Turns.Single().Text == "Done.", "another pawn request succeeds after invalid model response");
        Check(client.Metrics.For(RequestPurpose.Dialogue).DiscardedOutput == 10, "invalid model reply still contributes known discarded usage");
    }
}
