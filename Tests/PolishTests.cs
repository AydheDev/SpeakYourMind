using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SpeakYourMind.Core;
using static SpeakYourMind.Tests.Suite;

namespace SpeakYourMind.Tests;

public static class PolishTests
{
    public static void Run()
    {
        Delivery(); Drafts(); SaveValidation(); TextAndSettings(); LargeHistory(); SchedulingAndBackoff();
        RoundTrips().GetAwaiter().GetResult();
    }
    private static Turn A(string text = "Steady.") => new() { PawnId = "a", Text = text };
    private static void Delivery()
    {
        var d = new ExchangeDelivery();
        Check(d.Advance(0, false, true, true) == DeliveryAction.Waiting, "dots await provider");
        Check(d.MarkRendered(0) == null, "pending dots never become transcript");
        Check(d.Accept(new[] { A(), new Turn { PawnId = "b", Text = "Together." } }), "accept exchange");
        Check(d.Advance(0, true, true, true) == DeliveryAction.Waiting && d.Current == null, "completed reply held during pause");
        Check(d.Advance(0, false, true, false) == DeliveryAction.Waiting, "resume release stagger gate");
        Check(d.Advance(0, false, true, true) == DeliveryAction.Show && d.Current!.PawnId == "a", "first turn on resume");
        Check(d.MarkRendered(0)?.Text == "Steady.", "record actual repaint");
        Check(d.MarkRendered(0) == null && d.MarkRendered(1) == null, "repeat render cannot duplicate transcript");
        Check(d.Advance(1, true, true, true) == DeliveryAction.Waiting, "display lifetime held in pause");
        Check(d.Advance(2, false, true, true) == DeliveryAction.Waiting, "minimum reading time");
        Check(d.Advance(4, false, true, false) == DeliveryAction.Waiting, "reply turns also respect global release gate");
        Check(d.Advance(4, false, true, true) == DeliveryAction.Show && d.Current!.PawnId == "b", "second turn after gate");
        Check(d.MarkRendered(4)?.Text == "Together.", "second line recorded");
        Check(d.Advance(8, false, true, true) == DeliveryAction.Finished, "exchange ends after reading time");
        Check(d.MarkRendered(8) == null && !d.Accept(new[] { A() }), "finished exchange sealed");

        var fallback = new ExchangeDelivery(); fallback.Accept(new[] { A("Not now…") }, true);
        Check(!fallback.Accept(new[] { A("Late AI answer") }), "late answer cannot replace preset");
        Check(fallback.IsFallback, "fallback provenance preserved");
        fallback.Advance(0, false, true, true);
        Check(fallback.Current!.Text == "Not now…", "preset remains actual text");
        var neverDrawn = new ExchangeDelivery(); neverDrawn.Accept(new[] { A() }); neverDrawn.Advance(0, false, true, true);
        Check(neverDrawn.Advance(9, false, true, true) == DeliveryAction.Finished && neverDrawn.MarkRendered(9) == null, "undrawn response expires without history");
        var stale = new ExchangeDelivery(); stale.Accept(new[] { A() });
        Check(stale.Advance(0, false, false, true) == DeliveryAction.Finished && stale.Current == null, "stale response never shown");
        var shown = new ExchangeDelivery(); shown.Accept(new[] { A() }); shown.Advance(0, false, true, true);
        var transcript = new List<Turn>(); var rendered = shown.MarkRendered(0); if (rendered != null) transcript.Add(rendered);
        shown.Advance(0.1, false, false, true);
        Check(transcript.Count == 1, "context change cannot erase a line already rendered");
        var cancelled = new ExchangeDelivery(); cancelled.Cancel();
        Check(!cancelled.Accept(new[] { A() }) && cancelled.Advance(10, false, true, true) == DeliveryAction.Finished, "cancelled session cannot deliver late response");
    }
    private static void Drafts()
    {
        var original = new Connection { Model = "working", BaseUrl = "http://localhost:1234/v1" };
        var draft = new ConnectionDraft(original) { Key = "sample-key" };
        draft.Value.Model = "new";
        Check(original.Model == "working", "setup edits leave active connection intact");
        Throws<InvalidOperationException>(() => draft.Apply(original), "cannot apply untested draft");
        string stamp = draft.Stamp;
        Check(!stamp.Contains("sample-key"), "test identity contains no key text");
        draft.Value.Model = "other";
        Check(!draft.ValidateTest(stamp), "old model test cannot validate changed settings");
        Check(draft.ValidateTest(draft.Stamp) && draft.Tested, "current test validates");
        draft.Key = "different-key";
        Check(!draft.Tested, "credential change invalidates test");
        draft.ValidateTest(draft.Stamp); draft.Value.OutputTokens += 10;
        Check(!draft.Tested, "output allowance change invalidates test");
        draft.ValidateTest(draft.Stamp); draft.Apply(original);
        Check(original.Model == "other" && original.OutputTokens == draft.Value.OutputTokens, "explicit apply saves tested draft");
    }
    private static void SaveValidation()
    {
        Throws<FormatException>(() => SaveCodec.Read("{\"Version\":2}"), "future schema refused without silently rewriting");
        Throws<FormatException>(() => SaveCodec.Read("{\"Profiles\":null}"), "null profile list refused");
        Throws<FormatException>(() => SaveCodec.Read("{\"Profiles\":[null]}"), "null profile entry refused");
        Throws<FormatException>(() => SaveCodec.Read("{\"Profiles\":[{\"PawnId\":\"a\"},{\"PawnId\":\"a\"}]}"), "duplicate pawn identities refused");
        Throws<FormatException>(() => SaveCodec.Read("{\"ColonyEvents\":[{\"Importance\":2}]}"), "invalid saved memory importance refused");
        Throws<FormatException>(() => SaveCodec.Read("{\"Transcript\":[{\"Text\":\"hi\",\"SpeakerId\":\"a\",\"Participants\":null}]}"), "invalid transcript refused");
        var older = SaveCodec.Read("{\"Profiles\":[{\"PawnId\":\"a\",\"Voice\":null}]}" );
        Check(older.Profiles[0].Voice == "" && older.Profiles[0].ObservedFacts == "", "old saves gain optional defaults");
        var state = new SaveState(); state.Profile("a", "Ona", true).Memories.Add(new Memory { Importance = 1, Scored = true, Fact = "A defining loss" });
        state.Record(new TranscriptEntry { SpeakerId = "a", Text = "I remember.", Participants = new() { "b" } });
        var restored = SaveCodec.Read(SYMJson.Write(state));
        Check(restored.Transcript[0].Participants.Contains("a") && restored.Profiles[0].Memories[0].Importance == 1, "roundtrip preserves core and repairs speaker membership");
    }
    private static void TextAndSettings()
    {
        Check(TextPolicy.Valid("😺", 1), "emoji counts as one visible character");
        Check(TextPolicy.Valid("e\u0301", 1), "combining mark counts with letter");
        Check(TextPolicy.Valid(TextPolicy.Clean("👨‍👩‍👧"), 1), "joined emoji remains one visible character after cleaning");
        Check(!TextPolicy.Valid("😺!", 1), "hard visible length cap");
        Check(TextPolicy.Clean("Hi\n\tthere\0") == "Hi there", "control characters flattened");
        Check(TextPolicy.Repeated("Steady…", new[] { "steady!" }), "punctuation cannot bypass repetition control");
        Check(!TextPolicy.Repeated("Keep moving.", new[] { "Steady." }), "distinct expression allowed");
        var settings = new Preferences { Activity = 99, MaxBubbles = -8, MemoryScaleDays = float.NaN, MemoryExponent = float.PositiveInfinity, Language = null!, MaxCharacters = 1 };
        settings.Normalize();
        Check(settings.Activity == 4 && settings.MaxBubbles == 1 && settings.MemoryScaleDays == 30 && settings.MemoryExponent == 1.4f && settings.Language == "English", "invalid preferences normalized");
        Check(settings.EffectiveLimit == 1, "small explicit character override respected exactly");
        var presets = new Preferences { Length = ExpressionLength.Brief };
        Check(presets.EffectiveLimit == 100, "fresh settings let the selected length preset control the cap");
        presets.Length = ExpressionLength.Descriptive;
        Check(presets.EffectiveLimit == 420, "changing a length preset updates the default cap");
        var config = new Connection { Model = null!, TimeoutSeconds = -1, OutputTokens = 999999 }; config.Normalize();
        Check(config.Model == "" && config.TimeoutSeconds == 3 && config.OutputTokens == 16000, "invalid connection settings normalized");
        var context = Prompts.Preview(new Preferences());
        Throws<FormatException>(() => Prompts.ParseExpression("{\"turns\":[null]}", context), "null first turn reported cleanly");
        Check(ProviderClient.ExtractModels("{\"data\":[{\"id\":\"b\"},{\"id\":\"a\"},{\"id\":\"a\"},{\"id\":null}]}" ).SequenceEqual(new[] { "a", "b" }), "model list normalized");
        var state = new SaveState();
        Check(!state.Profile("visitor", "visitor", false).Persistent, "new visitors remain lightweight");
        state.Profile("visitor", "resident", true); state.Profile("visitor", "departed", false);
        Check(state.Profiles[0].Persistent, "former residents retain identity after leaving");
    }
    private static void LargeHistory()
    {
        var state = new SaveState();
        for (int i = 0; i < 100000; i++) state.Transcript.Add(new TranscriptEntry { Id = i.ToString(), SpeakerId = "a", SpeakerName = "Ona", Participants = new() { "a", "b" }, Trigger = i % 2 == 0 ? "Social" : "Needs", Text = "line " + i, Day = i / 1000.0 });
        var query = new TranscriptQuery();
        Check(query.Update(state, "b", "", "Social", 100, 0) && query.Matches.Count == 50000, "100k transcript filtered for listener and category");
        var references = query.Matches;
        for (int i = 0; i < 120; i++) query.Update(state, "b", "", "Social", 100, 0);
        Check(query.Rebuilds == 1 && ReferenceEquals(references, query.Matches), "GUI redraws reuse cached references");
        Check(query.Page(0).Count() == 60 && query.Page(0).First().Id == "99998", "pagination newest first without formatting entire archive");
        query.Update(state, "b", "line 99998", "Social", 100, 0);
        Check(query.Matches.Count == 1, "search finds exact historical entry");
        var expiring = new SaveState(); expiring.Record(new TranscriptEntry { Day = 0, SpeakerId = "a", Text = "old" });
        query.Update(expiring, "", "", "", 1, 1);
        Check(query.Matches.Count == 1, "history boundary inclusive");
        query.Update(expiring, "", "", "", 1.01, 1);
        Check(query.Matches.Count == 0, "cache invalidates when retention expires without new messages");
        expiring.Record(new TranscriptEntry { Day = 1.01, SpeakerId = "a", Text = "new" });
        query.Update(expiring, "", "", "", 1.01, 1);
        Check(query.Matches.Count == 1 && query.Matches[0].Text == "new", "append refreshes cached history");
    }
    private static void SchedulingAndBackoff()
    {
        Check(Scheduling.Overlaps(new[] { "a", "b" }, new[] { "c", "b" }), "shared listener locks both exchanges");
        Check(!Scheduling.Overlaps(new[] { "a", "b" }, new[] { "c", "d" }), "independent exchanges may coexist");
        Check(Scheduling.Priority(Trigger.Combat, false) > Scheduling.Priority(Trigger.Observation, true), "combat outranks selected idle observation");
        Check(Scheduling.Priority(Trigger.Social, true) > Scheduling.Priority(Trigger.Social, false), "observed selection prioritized within same urgency");
        var speech = new FailureBackoff(); var memory = new FailureBackoff();
        memory.Fail(0, 120);
        Check(speech.Ready(1) && !memory.Ready(119), "memory outage does not silence dialogue");
        Check(memory.Ready(120), "server retry-after honoured");
        speech.Fail(0); speech.Fail(0);
        Check(speech.Until == 8, "exponential retry pacing");
        speech.Success(); Check(speech.Ready(0) && speech.Failures == 0, "successful connection resets retry state");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public ProviderKind Kind;
        public bool RateLimit;
        public HttpMethod? LastMethod;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastMethod = request.Method;
            if (RateLimit)
            {
                var error = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("private service details") };
                error.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
                return Task.FromResult(error);
            }
            if (request.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"local-model\"}]}") });
            string text = SYMJson.Write(new ExpressionResponse { Turns = new() { new Turn { PawnId = "preview", Text = "That should hold." } } });
            string json = Kind switch
            {
                ProviderKind.OpenAI => SYMJson.Write(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text } } } } }),
                ProviderKind.Claude => SYMJson.Write(new { stop_reason = "end_turn", content = new[] { new { type = "text", text } } }),
                ProviderKind.Gemini => SYMJson.Write(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text } } } } } }),
                _ => SYMJson.Write(new { choices = new[] { new { finish_reason = "stop", message = new { content = text } } } })
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private static async Task RoundTrips()
    {
        foreach (var kind in Enum.GetValues<ProviderKind>())
        {
            var handler = new Handler { Kind = kind }; using var client = new ProviderClient(handler);
            var config = new Connection { Provider = kind, Model = "test-model", BaseUrl = "http://localhost:1234/v1" };
            var context = Prompts.Preview(new Preferences());
            var response = await client.Generate(config, "", Prompts.ExpressionSystem, SYMJson.Write(context), CancellationToken.None);
            var turns = Prompts.ParseExpression(response, context).Turns;
            var delivery = new ExchangeDelivery(); delivery.Accept(turns);
            delivery.Advance(0, true, true, true); Check(delivery.Current == null, kind + " roundtrip held in pause");
            delivery.Advance(0, false, true, true); var turn = delivery.MarkRendered(0)!;
            var state = new SaveState(); state.Record(new TranscriptEntry { SpeakerId = turn.PawnId, Text = turn.Text, Participants = new() { turn.PawnId } });
            Check(SaveCodec.Read(SYMJson.Write(state)).Transcript[0].Text == "That should hold.", kind + " provider-to-history-to-save roundtrip");
            if (kind is ProviderKind.OpenAI or ProviderKind.Compatible)
            {
                config.Model = "";
                Check((await client.ListModels(config, "", CancellationToken.None)).Single() == "local-model" && handler.LastMethod == HttpMethod.Get, "model discovery before selection " + kind);
            }
            handler.RateLimit = true; config.Model = "test";
            try { await client.Generate(config, "", "s", "u", CancellationToken.None); throw new Exception("Expected rate limit"); }
            catch (ProviderException e) { Check(e.RetryAfterSeconds == 90 && !e.Message.Contains("private"), "retry metadata without provider error leak " + kind); }
        }
    }
}
