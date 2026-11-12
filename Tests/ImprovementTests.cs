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

public static class ImprovementTests
{
    public static void Run() { Timing(); Selection(); Memories(); Conversations(); Metrics().GetAwaiter().GetResult(); }
    private static void Timing()
    {
        var delivery = new ExchangeDelivery();
        delivery.Accept(new[] { new Turn { PawnId = "a", Text = "First." }, new Turn { PawnId = "b", Text = "Second." } });
        Check(EventLifetime.Expired(Trigger.Social, 0, 5001, delivery.HasDisplayedAny), "unspoken social events still expire");
        delivery.Advance(0, false, true, true); delivery.MarkRendered(0);
        Check(!EventLifetime.Expired(Trigger.Social, 0, 99999, delivery.HasDisplayedAny), "game speed cannot expire a displayed conversation");
        Check(delivery.Advance(2, false, true, true) == DeliveryAction.Waiting, "fast ticks do not shorten reading time");
        Check(delivery.Advance(4, true, true, true) == DeliveryAction.Waiting, "fast exchange still respects pause");
        Check(delivery.Advance(4, false, true, true) == DeliveryAction.Show, "second turn delivered after reading time");
        Check(delivery.HasDisplayedAny && !delivery.CurrentWasDisplayed, "prior displayed turn protects undrawn next turn from tick expiry");
        Check(EventLifetime.Expired(Trigger.Combat, 0, 901, false), "unspoken combat retains stricter freshness");
        Check(!EventLifetime.Expired(Trigger.Combat, 0, 901, true), "displayed combat retains reading time");
    }
    private static void Selection()
    {
        var selection = new DialogueSelection();
        Check(selection.Allow("a", Trigger.Observation, "repair", 0, 3), "new topic eligible");
        selection.Displayed("a", Trigger.Observation, "repair", 0);
        Check(!selection.Allow("a", Trigger.Observation, "repair", 40, 3), "recent displayed topic suppressed");
        Check(selection.Allow("a", Trigger.Observation, "cook", 40, 3), "new activity can speak");
        selection.Displayed("a", Trigger.Observation, "cook", 41);
        Check(!selection.Allow("a", Trigger.Observation, "repair", 42, 3), "alternating topics cannot reset suppression");
        Check(selection.Allow("b", Trigger.Observation, "repair", 40, 3), "another pawn not silenced");
        Check(selection.Allow("a", Trigger.Observation, "repair", 76, 3), "topic recovers after interval");
        selection.Displayed("a", Trigger.Combat, "attack", 0);
        Check(selection.Allow("a", Trigger.Combat, "attack", 1, 3), "urgent events bypass topic suppression");
        Check(selection.Novelty("a", Trigger.Observation, "mine") > selection.Novelty("a", Trigger.Observation, "repair"), "new topic receives priority bonus");
        Check(TopicWords.Similarity("We should repair the broken wooden chair", "Repair that broken wooden chair") >= 0.8, "small wording changes retain topic similarity");
        Check(TopicWords.Similarity("Stop!", "Go!") == 0, "short reactions are not semantically overfiltered");
        var budget = new RequestBudget();
        Check(budget.Available(0, 1) && budget.Available(0, 1), "availability inspection does not spend requests");
        budget.TrySpend(0, 1);
        Check(!budget.Available(59, 1) && budget.Available(60, 1), "native fallback sees exhausted and replenished budget");
        var preferences = SYMJson.Read<Preferences>("{}");
        Check(preferences.NativeSocialWhenUnavailable && !preferences.ClickBubblesOpenHistory, "old preferences get native fallback and opt-in clicks");
    }
    private static void Memories()
    {
        var p = new Profile(); var settings = new Preferences();
        for (int i = 0; i < 30; i++) p.Memories.Add(new Memory { Id = "core" + i, Fact = "Old defining experience", Importance = 1, Scored = true, Day = 0 });
        p.Memories.Add(new Memory { Id = "listener", Fact = "Shared a meal", SubjectIds = new() { "b" }, Day = 1 });
        p.Memories.Add(new Memory { Id = "topic", Fact = "Repaired a wooden chair", Topic = "repair", Day = 2 });
        p.Memories.Add(new Memory { Id = "new", Fact = "Fresh experience", Day = 100 });
        p.Memories.Add(new Memory { Id = "expired", Fact = "repair", Scored = true, Importance = 0.01f, Day = 0 });
        var result = MemorySelection.Select(p, 100, settings, "repair chair", new[] { "b" });
        Check(result.Count == 14 && result.Select(m => m.Id).Distinct().Count() == 14, "memory selection bounded and unique");
        Check(result.Any(m => m.Id == "listener") && result.Any(m => m.Id == "topic"), "listener and current task survive many permanent memories");
        Check(result.Any(m => m.Id == "new") && result.Any(m => m.Importance == 1), "recent life and defining experiences both retained");
        Check(!result.Any(m => m.Id == "expired"), "expired topic memory excluded");
        Check(p.Memories.Count == 34, "retrieval never deletes archive memories");
        var saved = new SaveState(); saved.Profiles.Add(p); p.PawnId = "a";
        var restored = SaveCodec.Read(SYMJson.Write(saved));
        Check(restored.Profiles[0].Memories.Find(m => m.Id == "listener")!.SubjectIds.Contains("b"), "participant metadata survives save");
        var old = SaveCodec.Read("{\"Profiles\":[{\"PawnId\":\"a\",\"Memories\":[{\"Fact\":\"old\",\"Topic\":null,\"SubjectIds\":null}]}]}");
        Check(old.Profiles[0].Memories[0].SubjectIds.Count == 0, "legacy memories migrate without schema break");
    }
    private static void Conversations()
    {
        var state = new SaveState();
        state.Record(new TranscriptEntry { Id = "one", ExchangeId = "e", SpeakerId = "a", SpeakerName = "Ona", Text = "Question", Day = 1, Participants = new() { "a", "b" }, Trigger = "Social" });
        state.Record(new TranscriptEntry { Id = "two", ExchangeId = "e", SpeakerId = "b", SpeakerName = "Bo", Text = "Answer", Day = 1, Participants = new() { "a", "b" }, Trigger = "Social" });
        state.Record(new TranscriptEntry { Id = "three", SpeakerId = "c", Text = "Elsewhere", Day = 2, Participants = new() { "c" } });
        var query = new ConversationQuery(); query.Update(state, "b", "Answer", "Social", 3, 0);
        Check(query.Matches.Count == 1 && query.Matches[0].Lines.Count == 2, "search retains surrounding conversation");
        Check(query.Matches[0].Lines[0].Text == "Question", "same-tick lines preserve insertion order");
        Check(!query.Update(state, "b", "Answer", "Social", 3, 0) && query.Rebuilds == 1, "conversation GUI redraw uses cache");
        query.Update(state, "", "", "", 3, 0);
        Check(query.Matches.Count == 2 && query.Page(0, 1).Single().Lines[0].Id == "three", "ungrouped legacy lines and newest-first pagination");
        query.Update(state, "b", "", "", 3, 1);
        Check(query.Matches.Count == 0, "conversation view respects participant retention");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(Fail ? "private" : "{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":5}}") });
    }
    private static async Task Metrics()
    {
        Check(RequestMetrics.Usage(ProviderKind.OpenAI, "{\"usage\":{\"input_tokens\":7,\"output_tokens\":3}}") == (7L, 3L), "OpenAI usage parsed");
        Check(RequestMetrics.Usage(ProviderKind.Claude, "{\"usage\":{\"input_tokens\":8,\"output_tokens\":4}}") == (8L, 4L), "Claude usage parsed");
        Check(RequestMetrics.Usage(ProviderKind.Gemini, "{\"usageMetadata\":{\"promptTokenCount\":2,\"candidatesTokenCount\":3,\"thoughtsTokenCount\":4}}") == (2L, 7L), "Gemini generated and thought tokens reported");
        Check(RequestMetrics.Usage(ProviderKind.OpenAI, "{}").input == null, "missing usage remains unknown");
        Check(RequestMetrics.Usage(ProviderKind.OpenAI, "{\"usage\":{\"input_tokens\":-1,\"output_tokens\":\"bad\"}}").output == null, "invalid usage does not break response");
        var handler = new Handler(); using var client = new ProviderClient(handler);
        var connection = new Connection { Provider = ProviderKind.Compatible, BaseUrl = "http://localhost:1234/v1", Model = "test" };
        Check(await client.Generate(connection, "", "s", "u", CancellationToken.None) == "ok", "usage collection preserves returned text");
        handler.Fail = true;
        try { await client.Generate(connection, "", "s", "u", CancellationToken.None); } catch (ProviderException) { }
        var metrics = client.Metrics.Snapshot();
        Check(metrics.Started == 2 && metrics.Completed == 1 && metrics.Failed == 1 && metrics.InFlight == 0, "transport success and failure counted");
        Check(metrics.InputTokens == 12 && metrics.OutputTokens == 5 && metrics.ResponsesWithUsage == 1, "reported totals distinguish incomplete coverage");
        var parallel = new RequestMetrics();
        Parallel.For(0, 1000, _ => { parallel.Start(); parallel.Finish(true, false, 2, 1, 1); });
        Check(parallel.Snapshot().Completed == 1000 && parallel.Snapshot().AverageSeconds == 2, "worker metrics are thread safe");
    }
}
