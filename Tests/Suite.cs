using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpeakYourMind.Core;

namespace SpeakYourMind.Tests;

public static class Suite
{
    private static int assertions;
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); assertions++; }
    private static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { assertions++; return; } throw new Exception("FAILED: expected " + typeof(T).Name + ": " + name); }
    public static string Run()
    {
        assertions = 0;
        Memories(); History(); PromptsAndText(); Providers(); Serialization(); Budget(); ReflectionContracts();
        Transport().GetAwaiter().GetResult();
        return "PASS: " + assertions + " assertions (memory, history, provider requests/responses, mocked transport, budget, serialization, installed 1.7 integration contracts).";
    }
    private static void Memories()
    {
        var s = new Preferences();
        Check(MemoryPolicy.Lifetime(0.1f, s) > 1 && MemoryPolicy.Lifetime(0.1f, s) < 4, "minor memories last a few days");
        double last = 0;
        for (int n = 1; n < 100; n++) { double next = MemoryPolicy.Lifetime(n / 100f, s); Check(next > last, "continuous increasing retention " + n); last = next; }
        Check(double.IsPositiveInfinity(MemoryPolicy.Lifetime(1, s)), "core memory permanent");
        var m = new Memory { Day = 0, Importance = 1, Scored = true };
        Check(!MemoryPolicy.Expired(m, 1000000, s), "core survives long campaign");
        ProfilePolicy.ApplyScore(m, 0, "forgettable"); Check(m.Importance == 1, "LLM cannot downgrade core");
        m = new Memory { Importance = 0.1f, Scored = false };
        Check(!MemoryPolicy.Expired(m, 99, s), "unscored events preserved during provider outage");
        ProfilePolicy.ApplyScore(m, 0, "ordinary"); Check(MemoryPolicy.Expired(m, 1, s), "zero score forgets");
        ProfilePolicy.ApplyScore(m, float.NaN, "bad"); Check(m.Importance == 0, "reject NaN score");
        ProfilePolicy.ApplyScore(m, 2, "bad"); Check(m.Importance == 0, "reject out of range score");
        ProfilePolicy.ApplyScore(m, 0.7f, "later relevance"); Check(m.Importance == 0.7f, "importance may increase");
        ProfilePolicy.ApplyScore(m, 0.2f, "lower"); Check(m.Importance == 0.7f, "revisiting does not arbitrarily erase significance");
        s.MemoryScaleDays *= 2; Check(Math.Abs(MemoryPolicy.Lifetime(0.5f, s) - 60) < 0.01, "configurable retention");
    }
    private static void History()
    {
        var s = new Preferences { PawnHistoryDays = 5, ColonyHistoryDays = 10 };
        var state = new SaveState();
        state.Transcript.Add(new TranscriptEntry { Day = 0, Text = "hello" });
        state.Prune(7, s); Check(state.Transcript.Count == 1, "longer colony retention keeps shared record");
        Check(!HistoryPolicy.Visible(0, 7, 5) && HistoryPolicy.Visible(0, 7, 10), "independent filtered histories");
        state.Prune(11, s); Check(state.Transcript.Count == 0, "delete only after both expire");
        s.PawnHistoryDays = 0; state.Transcript.Add(new TranscriptEntry { Day = 0 });
        state.Prune(10000, s); Check(state.Transcript.Count == 1, "unlimited pawn retention protects transcript");
        s.PawnHistoryDays = 1; s.ColonyHistoryDays = 0; state.Prune(10001, s);
        Check(state.Transcript.Count == 1, "unlimited colony retention protects transcript");
        var p = state.Profile("pawn", "Ona", true);
        p.Memories.Add(new Memory { Importance = 1, Scored = true, Fact = "sister died" });
        s.PawnHistoryDays = 1; s.ColonyHistoryDays = 1; state.Prune(10002, s);
        Check(state.Transcript.Count == 0 && p.Memories.Count == 1, "core independent from transcript expiry");
    }
    private static void PromptsAndText()
    {
        var context = new GenerationContext { MaxCharacters = 30, MaxTurns = 2, Pawns = new() { new PawnSnapshot { Id = "a" }, new PawnSnapshot { Id = "b" } } };
        var r = Prompts.ParseExpression("```json\n{\"turns\":[{\"pawnId\":\"a\",\"text\":\"Steady.\"},{\"pawnId\":\"b\",\"text\":\"I hear you.\"}]}\n```", context);
        Check(r.Turns.Count == 2, "short exchange accepted");
        Throws<FormatException>(() => Prompts.ParseExpression("{\"turns\":[{\"pawnId\":\"z\",\"text\":\"Hi\"}]}", context), "invented speaker rejected");
        Throws<FormatException>(() => Prompts.ParseExpression("{\"turns\":[{\"pawnId\":\"b\",\"text\":\"Hi\"}]}", context), "initiator stays first");
        Throws<FormatException>(() => Prompts.ParseExpression("{\"turns\":[]}", context), "empty response rejected");
        Throws<FormatException>(() => Prompts.ParseExpression("{\"turns\":null}", context), "null turns rejected");
        Throws<FormatException>(() => Prompts.ParseExpression(SYMJson.Write(new ExpressionResponse { Turns = new() { new Turn { PawnId = "a", Text = new string('x', 31) } } }), context), "overlong response rejected rather than chopped");
        Check(TextPolicy.Clean("<color=red>Hello</color>") == "Hello", "rich text stripped");
        var settings = new Preferences(); var profile = new Profile();
        string before = Prompts.Signature(settings, "nya", profile);
        profile.Annotation = "Only gestures";
        Check(before != Prompts.Signature(settings, "nya", profile), "annotation invalidates fallback cache");
        Check(SYMJson.Write(new GenerationContext { Style = ExpressionStyle.Hybrid }).Contains("Hybrid"), "prompt style uses readable enum");
    }
    private static void Providers()
    {
        foreach (var url in new[] { "http://example.com/v1", "https://user:password@example.com", "https://example.com/?key=secret", "file:///tmp/x", "https://example.com/#secret" })
            Throws<ArgumentException>(() => ProviderClient.ValidateEndpoint(url), "unsafe endpoint " + url);
        Check(ProviderClient.ValidateEndpoint("http://localhost:11434/v1").IsLoopback, "localhost allowed");
        Check(ProviderClient.ValidateEndpoint("http://[::1]:1234/v1").IsLoopback, "IPv6 localhost allowed");
        Throws<ArgumentException>(() => ProviderClient.BuildRequest(new Connection { Model = "test" }, "private\nkey", "s", "u"), "invalid credentials rejected without echo");
        foreach (var kind in Enum.GetValues<ProviderKind>())
        {
            var c = new Connection { Provider = kind, BaseUrl = "https://example.com/v1", Model = "test-model" };
            using var request = ProviderClient.BuildRequest(c, "test-secret", "system", "user context");
            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Check(!request.RequestUri!.ToString().Contains("test-secret") && !body.Contains("test-secret"), "key absent from URL/body " + kind);
            Check(body.Contains("system") && body.Contains("user context"), "instructions preserved " + kind);
            Check(kind == ProviderKind.Gemini || body.Contains("test-model"), "model passed " + kind);
            if (kind == ProviderKind.OpenAI) { Check(body.Contains("\"store\":false"), "OpenAI storage disabled"); Check(body.Contains("\"effort\":\"low\""), "Low reasoning default"); }
            if (kind == ProviderKind.Claude) Check(request.Headers.Contains("x-api-key"), "Claude key header");
            if (kind == ProviderKind.Gemini) Check(request.Headers.Contains("x-goog-api-key"), "Gemini key header");
        }
        using (var req = ProviderClient.BuildRequest(new Connection { Provider = ProviderKind.Compatible, Model = "local", BaseUrl = "http://localhost:11434/v1", SendReasoning = false }, "", "s", "u"))
        { Check(req.RequestUri!.AbsolutePath == "/v1/chat/completions", "local route"); Check(!req.Content!.ReadAsStringAsync().Result.Contains("reasoning"), "unsupported reasoning omitted"); }
        Check(ProviderClient.Extract(ProviderKind.OpenAI, "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\"},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"ok\"}]}]}") == "ok", "OpenAI output extraction");
        Throws<FormatException>(() => ProviderClient.Extract(ProviderKind.OpenAI, "{\"status\":\"incomplete\"}"), "incomplete responses rejected");
        Check(ProviderClient.Extract(ProviderKind.Claude, "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"stop_reason\":\"end_turn\"}") == "ok", "Claude extraction");
        Check(ProviderClient.Extract(ProviderKind.Compatible, "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}") == "ok", "compatible extraction");
        Check(ProviderClient.Extract(ProviderKind.Gemini, "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":true,\"text\":\"hidden\"},{\"text\":\"ok\"}]}}]}") == "ok", "Gemini excludes reasoning text");
        Check(!ProviderClient.SafeError(new HttpRequestException("secret in provider error")).Contains("secret"), "network details redacted");
    }
    private static void Serialization()
    {
        var s = new SaveState { ColonyRule = "Every word ends in nya" };
        var p = s.Profile("A", "Ona", true); p.Annotation = "Only gestures"; p.Memories.Add(new Memory { Fact = "Sister died", Importance = 1, Scored = true });
        s.Transcript.Add(new TranscriptEntry { Text = "Steady.", Participants = new() { "A", "B" } });
        var restored = SYMJson.Read<SaveState>(SYMJson.Write(s));
        Check(restored.Profiles[0].Memories[0].Importance == 1, "save roundtrip core");
        Check(restored.Profiles[0].Annotation == "Only gestures" && restored.ColonyRule.Contains("nya"), "save annotations");
        Check(restored.Transcript[0].Participants.Count == 2, "save shared exchange");
        Check(!SYMJson.Write(new Connection()).Contains("key", StringComparison.OrdinalIgnoreCase), "connection serialization has no credential field");
    }
    private static void Budget()
    {
        var budget = new RequestBudget();
        Check(budget.TrySpend(0, 2) && budget.TrySpend(1, 2), "within budget");
        Check(!budget.TrySpend(59, 2), "real minute limit");
        Check(budget.TrySpend(60, 2), "sliding expiration");
        Check(!budget.TrySpend(60, 2), "no burst after expiration");
    }
    private static void ReflectionContracts()
    {
        var game = typeof(Verse.Pawn).Assembly;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        Check(game.GetType("Verse.PlayLog")!.GetMethod("Add", flags, new[] { typeof(Verse.LogEntry) }) != null, "play log hook exists");
        Check(game.GetType("Verse.Pawn_HealthTracker")!.GetMethod("PostApplyDamage", flags)!.GetParameters().Length == 2, "damage hook contract");
        Check(game.GetType("Verse.Pawn_HealthTracker")!.GetMethod("Notify_UsedVerb", flags)!.GetParameters()[1].ParameterType == typeof(Verse.LocalTargetInfo), "combat hook contract");
        Check(game.GetType("Verse.PlayLogEntry_Interaction")!.GetField("initiator", flags)?.FieldType == typeof(Verse.Pawn), "interaction speaker field");
        Check(game.GetType("Verse.PlayLogEntry_Interaction")!.GetField("recipient", flags)?.FieldType == typeof(Verse.Pawn), "interaction listener field");
        Check(game.GetType("Verse.Profile.MemoryUtility")!.GetMethod("ClearAllMapsAndWorld", flags) != null, "world cleanup hook");
        Check(typeof(ExpressionEntry).BaseType == typeof(Verse.LogEntry), "presentation log type");
    }
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Code = HttpStatusCode.OK;
        public bool Wait;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Wait) await Task.Delay(5000, token);
            return new HttpResponseMessage(Code) { Content = new StringContent(Code == HttpStatusCode.OK ? "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}" : "private account details and API key") };
        }
    }
    private static async Task Transport()
    {
        var handler = new StubHandler(); using var client = new ProviderClient(handler);
        var c = new Connection { Provider = ProviderKind.Compatible, BaseUrl = "http://localhost:1234/v1", Model = "local", SendReasoning = false };
        Check(await client.Generate(c, "", "s", "u", CancellationToken.None) == "ok", "mocked end to end transport");
        handler.Code = HttpStatusCode.TooManyRequests;
        try { await client.Generate(c, "", "s", "u", CancellationToken.None); throw new Exception("expected error"); }
        catch (ProviderException e) { Check(e.Message.Contains("limit") && !e.Message.Contains("private"), "rate limit guidance without error-body leak"); }
        handler.Wait = true; using var cancel = new CancellationTokenSource(20);
        try { await client.Generate(c, "", "s", "u", cancel.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { Check(true, "network cancellation"); }
    }
}
