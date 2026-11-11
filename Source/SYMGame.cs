using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using SpeakYourMind.Core;
using Verse;

namespace SpeakYourMind;

public sealed class SYMGame : GameComponent
{
    public static SYMGame? Current => Verse.Current.Game?.GetComponent<SYMGame>();
    public SaveState State = new();
    public string Status = "Ready for setup.";
    public string MemoryStatus = "Waiting for background work.";
    public int RequestsStarted { get; private set; }
    public int ExpressionsShown { get; private set; }
    public int FallbacksShown { get; private set; }
    public int Discarded { get; private set; }
    public int Queued => queue.Count;
    public int ActiveBubbles => running.Count;
    public double RetryIn => expressionBackoff.Remaining(RealTime);
    public string MemoryDiagnostic => memoryTask != null ? "Request in progress" : memoryBackoff.Ready(RealTime) ? "Ready" : "Retry pending";
    public static double Day => (Find.TickManager?.TicksGame ?? 0) / 60000.0;
    private static double RealTime => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private Preferences Settings => SYMMod.Instance.Settings.Preferences;
    private readonly Game game;
    private readonly ProviderClient provider = new();
    private readonly RequestBudget budget = new();
    private readonly FailureBackoff expressionBackoff = new(), memoryBackoff = new();
    private readonly Random expressionRandom = new(); // Never consume the game's simulation RNG.
    private readonly List<Intent> queue = new();
    private readonly List<Running> running = new();
    private readonly Dictionary<string, double> cooldowns = new();
    private readonly Dictionary<string, float> previousNeeds = new();
    private readonly Dictionary<int, (double time, List<string> items)> resources = new();
    private readonly Dictionary<string, int> fallbackIndices = new();
    private readonly Queue<Pawn> profileScan = new();
    private DebugJournal? journal;
    public static string DebugFolder => Path.Combine(GenFilePaths.ConfigFolderPath, "SpeakYourMind", "Debug");
    public string DebugStatus => journal?.Status ?? "Off";
    private string memoryRequestId = "";
    private Task<string>? memoryTask;
    private Profile? memoryProfile;
    private string memoryFingerprint = "", memoryFacts = "", memorySignature = "";
    private HashSet<string> memoryIds = new();
    private double lastUpdate = RealTime, playTime, nextAmbient, nextProfiles, nextMemory, lastRelease, lastMemoryStart;
    private int lastCleanupDay = -1;
    private bool wasEnabled, disposed;
    private string unreadableState = "";
    public bool StateHealthy => unreadableState.Length == 0;
    private CancellationTokenSource session = new();

    private sealed class Intent
    {
        public Pawn Pawn = null!;
        public Pawn? Other;
        public Trigger Trigger;
        public string Fact = "", Need = "";
        public float NeedLevel;
        public string HealthSignature = "";
        public int Tick;
        public double Created;
        public string Id = Guid.NewGuid().ToString("N"), Exchange = Guid.NewGuid().ToString("N");
        public FallbackCandidate? Fallback;
        public bool FollowUp;
    }
    private sealed class Running
    {
        public Intent Intent = null!;
        public Task<string> Task = null!;
        public GenerationContext Context = null!;
        public CancellationTokenSource Cancellation = null!;
        public double Started;
        public int TimeoutSeconds;
        public ExpressionEntry Entry = null!;
        public object Bubble = null!;
        public Pawn BubblePawn = null!;
        public readonly ExchangeDelivery Delivery = new();
        public string Exchange = "", Source = "ai";
    }

    public SYMGame(Game game) { this.game = game; }
    public void SyncDebug()
    {
        if (disposed) return;
        if (!Settings.DebugLogging) { journal?.Dispose(); journal = null; return; }
        if (journal != null) return;
        journal = new DebugJournal(DebugFolder, Settings.DebugSessionMegabytes);
        journal.AddSecret(Credentials.Read(SYMMod.Instance.Settings.Connection));
        journal.AddSecret(Credentials.Read(SYMMod.Instance.Settings.MemoryConnection));
        journal.Record(new DebugEvent { Stage = "session_start", Day = Day, Detail = "SpeakYourMind 0.3.2; prompt revision 2. Loaded mods: " + string.Join(", ", LoadedModManager.RunningModsListForReading.Select(m => m.Name)) });
    }
    private void Trace(string stage, Intent? intent = null, string detail = "", string text = "", string response = "", string system = "", string context = "", Connection? connection = null, double elapsed = 0, string request = "", string source = "")
    {
        SyncDebug();
        journal?.Record(new DebugEvent { Stage = stage, Request = intent?.Id ?? request, Day = Day, Trigger = intent?.Trigger.ToString() ?? "",
            Source = source, Detail = (intent == null ? "" : "Exchange " + intent.Exchange + ". ") + detail + (connection == null ? "" : "; provider " + connection.Provider + "; output tokens " + connection.OutputTokens + "; timeout seconds " + connection.TimeoutSeconds), Text = text, Response = response,
            SystemPrompt = system, Context = context, Model = connection?.Model ?? "", Reasoning = connection == null ? "" : connection.SendReasoning ? connection.Reasoning : "provider default", ElapsedMs = elapsed });
    }
    public void AddDebugNote(string note) => Trace("player_note", detail: note);
    private void TraceCompletion(Task<string> task, string id, string source)
    {
        if (journal is not { } captured) return;
        double started = RealTime, day = Day;
        _ = task.ContinueWith(t => captured.Record(new DebugEvent { Request = id, Day = day, Stage = "provider_completed", Source = source,
            ElapsedMs = (RealTime - started) * 1000, Response = t.IsCompletedSuccessfully ? t.Result : "",
            Detail = t.IsCanceled ? "Cancelled" : t.IsFaulted ? ProviderClient.SafeError(t.Exception!.GetBaseException()) : "Received; validation and display are separate events." }), TaskScheduler.Default);
    }
    public override void ExposeData()
    {
        string json = "";
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            var saved = new SaveState { Version = State.Version, ColonyRule = State.ColonyRule, ColonyEvents = State.ColonyEvents, Transcript = State.Transcript,
                Profiles = State.Profiles.Where(p => p.Persistent || p.Annotation.Length > 0).ToList() };
            json = unreadableState.Length > 0 ? unreadableState : SYMJson.Write(saved);
        }
        Scribe_Values.Look(ref json, "SYM_state_v1", "");
        if (Scribe.mode == LoadSaveMode.LoadingVars && !string.IsNullOrEmpty(json))
        {
            try { State = SaveCodec.Read(json); }
            catch { unreadableState = json; Status = "Saved history could not be read. Original data preserved; generation disabled for this colony."; Log.Error("[SpeakYourMind] " + Status); }
        }
    }
    public Profile ProfileFor(Pawn pawn) => State.Profile(pawn.ThingID, pawn.LabelShort, Context.Tracked(pawn, Settings));
    private void Remember(Pawn pawn, string fact, string source = "game", string perspective = "experienced")
    {
        var p = ProfileFor(pawn);
        if (p.Memories.Any(m => m.Fact == fact && Day - m.Day < 0.1)) return;
        p.Memories.Add(new Memory { Fact = fact, Source = source, Perspective = perspective, Day = Day });
        State.Touch();
    }
    public void ObserveSocial(LogEntry entry)
    {
        if (entry is ExpressionEntry) return;
        var type = entry.GetType();
        if (entry is not PlayLogEntry_Interaction && type.Name != "PlayLogEntry_InteractionSinglePawn") return;
        var pawn = AccessTools.Field(type, "initiator")?.GetValue(entry) as Pawn;
        var other = AccessTools.Field(type, "recipient")?.GetValue(entry) as Pawn;
        if (pawn == null) return;
        string text = OptionalDialogue.ReadText(entry, pawn);
        var candidate = OptionalDialogue.SocialCandidate(entry, text, Settings);
        string fact = "Social event: " + OptionalDialogue.EventLabel(entry) + ". Authored interaction text (claims are not independently verified): " + text;
        Trace("social_observed", text: text, source: candidate?.Source ?? "game", detail: pawn.ThingID + " -> " + other?.ThingID);
        if (!Integration.Active) return;
        if (Context.Tracked(pawn, Settings)) Remember(pawn, fact);
        if (other != null && Context.Tracked(other, Settings)) Remember(other, fact);
        Enqueue(pawn, Trigger.Social, fact, other, fallback: candidate);
    }
    public bool ObserveCallout(Pawn pawn, FallbackCandidate candidate)
    {
        Trace("callout_emitted", text: candidate.Text, source: "callouts", detail: pawn.ThingID + "; " + candidate.Definition + "; emission is not proof of an on-screen draw");
        if (!Integration.Active || !Settings.CalloutsFallback || !Context.CanExpress(pawn)) return false;
        string def = candidate.Definition.ToLowerInvariant();
        var trigger = def.Contains("ranged") || def.Contains("melee") || def.Contains("combat") ? Trigger.Combat : def.Contains("hediff") || def.Contains("wound") ? Trigger.Health : Trigger.Observation;
        // Keep this exact event separate: never reuse a hit/kill callout for a later attack.
        var probe = new Intent { Pawn = pawn, Trigger = trigger, Fact = "Callouts emitted text for " + candidate.Definition + ". Treat the wording as expression, not independent evidence: " + candidate.Text, Fallback = candidate };
        if (FallbackPolicy.Rejection(candidate, Capture(probe), Settings).Length > 0) return false;
        // A damage/attack observer may have queued this same tick's reaction before Callouts resolves.
        // Attach only to an unspoken reaction from this pawn, category and tick, never an older event.
        if (trigger is Trigger.Combat or Trigger.Health)
        {
            int tick = Find.TickManager.TicksGame;
            var pending = running.FirstOrDefault(r => r.Intent.Pawn == pawn && r.Intent.Trigger == trigger && r.Intent.Tick == tick && !r.Delivery.HasResult && r.Intent.Fallback == null)?.Intent
                ?? queue.FirstOrDefault(i => i.Pawn == pawn && i.Trigger == trigger && i.Tick == tick && i.Fallback == null);
            if (pending != null)
            {
                pending.Fallback = candidate;
                Trace("fallback_attached", pending, candidate.Definition + "; same-tick observed reaction", text: candidate.Text, source: "callouts");
                return true;
            }
        }
        return Enqueue(pawn, trigger, probe.Fact, fallback: candidate);
    }
    public void ObserveDamage(Pawn pawn, DamageInfo damage)
    {
        bool combat = damage.Instigator is Pawn || damage.Instigator is Building_Turret;
        string fact = "Recently took " + damage.Def.label + " damage" + (damage.Instigator == null ? "." : " from " + damage.Instigator.LabelShort + ".");
        if (Context.Tracked(pawn, Settings) && !cooldowns.ContainsKey(pawn.ThingID + ":injury"))
        { Remember(pawn, fact); cooldowns[pawn.ThingID + ":injury"] = RealTime + 45; }
        Enqueue(pawn, combat ? Trigger.Combat : Trigger.Health, fact);
    }
    public void ObserveAttack(Pawn pawn, LocalTargetInfo target)
    {
        if (target.Thing is not Pawn other || !pawn.Spawned || !other.Spawned || pawn.Map != other.Map) return;
        Enqueue(pawn, Trigger.Combat, "Attacking " + other.LabelShort + ". Do not claim a hit or outcome.");
        if (GenSight.LineOfSight(other.Position, pawn.Position, other.Map))
            Enqueue(other, Trigger.Combat, "Being attacked by visible " + pawn.LabelShort + ". Do not claim injury or a hit.");
    }
    public void ObserveDeath(Pawn dead, List<DeathRelation> relatives)
    {
        foreach (var relation in relatives.Where(p => !p.Pawn.Dead))
        {
            var pawn = relation.Pawn;
            string fact = dead.LabelShort + " died. Relationship to this pawn before death: " + relation.Relationship + ".";
            Remember(pawn, fact, "game", "personal relationship; not necessarily witnessed");
            Enqueue(pawn, Trigger.Colony, fact);
        }
        if (dead.Faction == Faction.OfPlayer || dead.IsPrisonerOfColony) ObserveColony(dead.LabelShort + " died.");
    }
    public void ObserveColony(string fact)
    {
        if (string.IsNullOrWhiteSpace(fact) || State.ColonyEvents.Any(e => e.Fact == fact && Day - e.Day < 0.1)) return;
        State.ColonyEvents.Add(new Memory { Fact = fact, Day = Day, Importance = 0.5f, Scored = true, Perspective = "colony awareness" });
        State.Touch();
        foreach (var pawn in Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => Context.Tracked(p, Settings) && Context.ColonyAware(p)))
            Remember(pawn, fact, "game", "colony awareness; not necessarily witnessed");
        var observed = Find.CurrentMap?.mapPawns.AllPawnsSpawned.Where(p => Context.CanExpress(p) && Context.ColonyAware(p)).OrderByDescending(p => Find.Selector.IsSelected(p)).FirstOrDefault();
        if (observed != null) Enqueue(observed, Trigger.Colony, fact);
    }
    private bool Enqueue(Pawn pawn, Trigger trigger, string fact, Pawn? other = null, string need = "", float level = 0, FallbackCandidate? fallback = null, bool followUp = false, string exchange = "")
    {
        if (!Settings.Enabled || !Settings.Allows(trigger) || !Context.CanExpress(pawn)) return false;
        string key = pawn.ThingID + ":" + trigger;
        if (!followUp && cooldowns.TryGetValue(key, out double until) && until > RealTime) return false;
        if (other != null && (!Context.CanExpress(other) || other.Position.DistanceTo(pawn.Position) > 12)) other = null;
        var intent = new Intent { Pawn = pawn, Other = other, Trigger = trigger, Fact = fact, Need = need, NeedLevel = level,
            HealthSignature = trigger == Trigger.Health ? Context.HealthSignature(pawn) : "", Tick = Find.TickManager.TicksGame, Created = playTime, Fallback = fallback, FollowUp = followUp };
        if (exchange.Length > 0) intent.Exchange = exchange;
        int priority = Priority(intent);
        foreach (var conflict in running.Where(r => Overlaps(r.Intent, intent)).ToList())
        {
            // Urgent events can displace an unspoken ambient request, never an already displayed line.
            if (priority <= Priority(conflict.Intent) || conflict.Delivery.Current != null) return false;
            Remove(conflict, reason: "Replaced by a higher-priority event.");
        }
        var queued = queue.Where(i => Overlaps(i, intent)).ToList();
        if (queued.Any(i => Priority(i) >= priority)) return false;
        foreach (var item in queued) { Trace("discarded", item, "Replaced by a higher-priority queued event."); queue.Remove(item); }
        if (queue.Count >= 24)
        {
            var lowest = queue.OrderBy(Priority).First();
            if (Priority(lowest) >= priority) return false;
            queue.Remove(lowest);
            Trace("discarded", lowest, "Queue full; displaced by a higher-priority event.");
        }
        queue.Add(intent);
        Trace("queued", intent, fact, source: fallback?.Source ?? "");
        cooldowns[key] = RealTime + (trigger == Trigger.Combat ? 10 : 65 - Math.Clamp(Settings.Activity, 1, 4) * 10);
        return true;
    }
    private static IEnumerable<string> Participants(Intent i) => new[] { i.Pawn.ThingID, i.Other?.ThingID }.OfType<string>();
    private static bool Overlaps(Intent a, Intent b) => Scheduling.Overlaps(Participants(a), Participants(b));
    private static int Priority(Intent i) => Scheduling.Priority(i.Trigger, Find.Selector.IsSelected(i.Pawn)) + (i.Fallback?.Source == "callouts" ? 1 : 0);
    private bool Relevant(Intent intent)
    {
        if (!Context.CanExpress(intent.Pawn) || !Settings.Allows(intent.Trigger)) return false;
        if (Find.TickManager.TicksGame - intent.Tick > (intent.Trigger == Trigger.Combat ? 900 : 5000)) return false;
        if (intent.Trigger == Trigger.Health && Context.HealthSignature(intent.Pawn) != intent.HealthSignature) return false;
        if (intent.Need.Length > 0)
        {
            var need = intent.Pawn.needs?.AllNeeds.FirstOrDefault(n => n.def.defName == intent.Need);
            if (need == null || need.CurLevelPercentage > intent.NeedLevel + 0.15f) return false;
        }
        return true;
    }
    private GenerationContext Capture(Intent intent)
    {
        var s = Settings;
        var context = new GenerationContext { Event = intent.Fact, Trigger = intent.Trigger.ToString(), ColonyRule = State.ColonyRule,
            Language = s.Language, MaxCharacters = s.EffectiveLimit, MaxTurns = ConversationPolicy.Turns(s, intent.Other != null, intent.FollowUp), Style = s.Style, Length = s.Length };
        foreach (var pawn in new[] { intent.Pawn, intent.Other }.Where(p => p != null).Cast<Pawn>())
        {
            context.Pawns.Add(Context.Capture(pawn));
            var profile = ProfileFor(pawn);
            context.Profiles.Add(new Profile { PawnId = profile.PawnId, Name = profile.Name, Voice = profile.Voice, Annotation = profile.Annotation, Memories = MemoryPolicy.Relevant(profile, Day, s).ToList() });
        }
        if (context.Pawns.All(p => new[] { intent.Pawn, intent.Other }.Where(x => x != null).Any(x => x!.ThingID == p.Id && Context.ColonyAware(x))))
        {
            context.ColonyAwareness = State.ColonyEvents.TakeLast(8).ToList();
            int map = intent.Pawn.Map.uniqueID;
            if (!resources.TryGetValue(map, out var cached) || RealTime - cached.time > 30)
                resources[map] = cached = (RealTime, Context.Resources(intent.Pawn.Map));
            context.ColonyResources = cached.items;
        }
        context.RecentExchange = State.Transcript.AsEnumerable().Reverse().Where(t => t.Participants.Contains(intent.Pawn.ThingID)).Take(6).Reverse().ToList();
        return context;
    }
    public override void GameComponentUpdate()
    {
        if (disposed) return;
        SyncDebug();
        double now = RealTime, delta = Math.Clamp(now - lastUpdate, 0, 0.25); lastUpdate = now;
        if (Verse.Current.Game != game) { StopAll(); return; }
        if (!Settings.Enabled || !Integration.Ready || !StateHealthy)
        { if (wasEnabled) StopAll(); wasEnabled = false; return; }
        wasEnabled = true;
        if (Find.TickManager.Paused) return; // Finished requests are held, with no UI/history changes.
        playTime += delta;
        try
        {
            for (int i = running.Count - 1; i >= 0; i--)
            {
                var run = running[i];
                try { UpdateRun(run, now); }
                catch (Exception e) { Remove(run); Failed(e); }
            }
            FinishMemory();
            if (now >= nextProfiles && profileScan.Count == 0) { BeginProfileScan(); nextProfiles = now + 30; }
            if (profileScan.Count > 0) ScanProfile(profileScan.Dequeue());
            if (now >= nextAmbient) { Ambient(); nextAmbient = now + new[] { 25, 12, 6, 3 }[Math.Clamp(Settings.Activity - 1, 0, 3)]; }
            if ((int)Day != lastCleanupDay) { State.Prune(Day, Settings); lastCleanupDay = (int)Day; }
            foreach (var k in cooldowns.Where(x => x.Value < now).Select(x => x.Key).ToList()) cooldowns.Remove(k);
            foreach (var stale in queue.Where(i => !Relevant(i) || playTime - i.Created > 30).ToList()) { Trace("discarded", stale, "Queued event became stale before dispatch."); queue.Remove(stale); }
            if (!Integration.Rendering) return;
            // Optional source text can still appear while the service is busy or backing off.
            foreach (var queued in queue.Where(i => i.Fallback != null && playTime - i.Created >= (i.Trigger == Trigger.Combat ? 3 : Settings.FallbackDelaySeconds)).OrderByDescending(Priority).ToList())
            {
                if (running.Count >= Math.Clamp(Settings.MaxBubbles, 1, 8)) break;
                if (running.Any(r => Overlaps(r.Intent, queued))) continue;
                if (TryQueuedFallback(queued)) queue.Remove(queued);
            }
            // Reserve occasional capacity for memory even when the visible-expression queue stays busy.
            if (memoryTask == null && memoryBackoff.Ready(now) && now >= nextMemory && (queue.Count == 0 || now - lastMemoryStart > 60))
            { try { StartMemory(now); } catch (Exception e) { MemoryFailed(e); } }
            if (expressionBackoff.Ready(now) && running.Count < Math.Clamp(Settings.MaxBubbles, 1, 8) && running.Count(r => !r.Task.IsCompleted && !r.Delivery.HasResult) < 2 && queue.Count > 0)
            {
                var next = queue.Where(i => !running.Any(r => Overlaps(i, r.Intent))).OrderByDescending(Priority).ThenBy(i => i.Created).FirstOrDefault();
                if (next != null && budget.TrySpend(now, Settings.RequestsPerMinute)) { queue.Remove(next); Start(next, now); }
            }
        }
        catch (Exception e) { Failed(e); }
    }
    private void BeginProfileScan()
    {
        foreach (var pawn in Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => !p.Dead && Context.Tracked(p, Settings))) profileScan.Enqueue(pawn);
        var aliveIds = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Select(p => p.ThingID).ToHashSet();
        if (State.Profiles.RemoveAll(p => !p.Persistent && p.Annotation.Length == 0 && !aliveIds.Contains(p.PawnId)) > 0) State.Touch();
        foreach (var id in fallbackIndices.Keys.Where(id => !aliveIds.Contains(id)).ToList()) fallbackIndices.Remove(id);
        foreach (var key in previousNeeds.Keys.Where(k => !aliveIds.Contains(k.Split(':')[0])).ToList()) previousNeeds.Remove(key);
    }
    private void ScanProfile(Pawn pawn)
    {
        if (!pawn.Spawned || pawn.Dead) return;
        try
        {
            var profile = ProfileFor(pawn);
            var snapshot = Context.Capture(pawn);
            string facts = Context.StableFacts(snapshot), health = Context.HealthSignature(pawn);
            if (profile.ObservedFacts.Length == 0 && profile.Memories.Count == 0)
                Remember(pawn, "Known biography and relationships on first observation: " + facts, "game", "established facts");
            else if (profile.ObservedFacts.Length > 0 && profile.ObservedFacts != facts)
                Remember(pawn, "Personal facts changed. Previous: " + profile.ObservedFacts + "; now: " + facts, "game", "experienced");
            if (profile.ObservedHealth.Length > 0 && profile.ObservedHealth != health)
                Enqueue(pawn, Trigger.Health, "Health status changed. Current verified condition: " + string.Join("; ", snapshot.Health) + "; pain " + snapshot.Pain.ToString("P0") + ". Express the current state; do not invent the cause.");
            profile.ObservedFacts = facts; profile.ObservedHealth = health;
        }
        catch { Log.WarningOnce("[SpeakYourMind] A pawn context could not be read. Other pawns will continue normally.", 17393012); }
    }
    private void Ambient()
    {
        if (Find.CurrentMap == null || !Integration.Rendering) return;
        var pawns = Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(Context.CanExpress).OrderByDescending(p => Find.Selector.IsSelected(p)).ThenBy(_ => expressionRandom.NextDouble()).ToList();
        foreach (var pawn in pawns)
        {
            bool selected = Find.Selector.IsSelected(pawn);
            if (pawn.needs != null)
            {
                foreach (var need in pawn.needs.AllNeeds)
                {
                    string key = pawn.ThingID + ":" + need.def.defName;
                    float level = need.CurLevelPercentage;
                    bool changed = !previousNeeds.TryGetValue(key, out var old) || Math.Abs(level - old) >= 0.15f;
                    if (changed) previousNeeds[key] = level;
                    if (level < 0.25f && changed)
                    {
                        string fact = "Current need " + need.LabelCap + " is low (" + level.ToString("P0") + "). Interpret this need according to its actual meaning; do not invent missing facilities.";
                        Enqueue(pawn, Trigger.Needs, fact, need: need.def.defName, level: level);
                        if (Context.Tracked(pawn, Settings)) Remember(pawn, fact);
                    }
                }
            }
            // Occasional ambient expressions; cooldowns and the queue prevent selected-pawn spam.
            if (selected || expressionRandom.NextDouble() < 0.15) Enqueue(pawn, Trigger.Observation, "Express something relevant to current work, surroundings or a meaningful memory. Avoid repeating recent remarks.");
            if (queue.Count >= Settings.MaxBubbles * 2) break;
        }
    }
    private void Start(Intent intent, double now)
    {
        var context = Capture(intent);
        var connection = SYMMod.Instance.Settings.Connection.Copy();
        string key = Credentials.Read(connection);
        journal?.AddSecret(key);
        ProviderClient.ValidateConfiguration(connection, key);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        var entry = new ExpressionEntry(intent.Pawn);
        var run = new Running { Intent = intent, Context = context, Started = playTime, TimeoutSeconds = connection.TimeoutSeconds, Entry = entry, BubblePawn = intent.Pawn, Cancellation = cancellation, Exchange = intent.Exchange };
        try
        {
            run.Bubble = Integration.Add(intent.Pawn, entry);
            string input = SYMJson.Write(context);
            Trace("request", intent, system: Prompts.ExpressionSystem, context: input, connection: connection, source: "ai");
            run.Task = provider.Generate(connection, key, Prompts.ExpressionSystem, input, cancellation.Token);
            TraceCompletion(run.Task, intent.Id, "ai");
            running.Add(run); RequestsStarted++; Status = "Generating expressions…";
        }
        catch { cancellation.Cancel(); cancellation.Dispose(); if (run.Bubble != null) Integration.Remove(intent.Pawn, run.Bubble); throw; }
    }
    private bool TryQueuedFallback(Intent intent)
    {
        var context = Capture(intent);
        string rejection = FallbackPolicy.Rejection(intent.Fallback, context, Settings);
        if (rejection.Length > 0) { Trace("fallback_rejected", intent, rejection, source: intent.Fallback?.Source ?? ""); intent.Fallback = null; return false; }
        var run = new Running { Intent = intent, Context = context, Started = playTime, Entry = new ExpressionEntry(intent.Pawn), BubblePawn = intent.Pawn,
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token), Task = Task.FromResult(""), Exchange = intent.Exchange };
        try
        {
            run.Bubble = Integration.Add(intent.Pawn, run.Entry); running.Add(run);
            Fallback(run); return true;
        }
        catch { if (running.Contains(run)) Remove(run); else run.Cancellation.Dispose(); throw; }
    }
    private void UpdateRun(Running run, double now)
    {
        if (!Relevant(run.Intent)) { Remove(run, reason: "Event expired, need/health changed, trigger disabled, or initiating pawn no longer observable."); return; }
        if (!Context.Visible(run.BubblePawn)) { Remove(run, reason: "Current speaker is no longer visible."); return; }
        if (!Integration.Rendering) { Remove(run, reason: "Bubble renderer is disabled."); return; }
        if (run.Context.ColonyRule != State.ColonyRule || run.Context.Style != Settings.Style || run.Context.Length != Settings.Length || run.Context.Language != Settings.Language ||
            run.Context.MaxTurns != ConversationPolicy.Turns(Settings, run.Intent.Other != null, run.Intent.FollowUp) ||
            run.Context.Profiles.Any(p => p.Annotation != State.Profiles.Find(x => x.PawnId == p.PawnId)?.Annotation)) { Remove(run, reason: "Player instructions or expression settings changed while waiting."); return; }
        if (!run.Delivery.HasResult)
        {
            string dots = new[] { ".  ", ".. ", "...", "   " }[(int)(playTime * 3) % 4];
            if (run.Entry.Text != dots) { run.Entry.SetText(dots); Integration.Refresh(run.Bubble); }
            if (run.Task.IsCompleted)
            {
                try
                {
                    string response = run.Task.GetAwaiter().GetResult();
                    Trace("response", run.Intent, response: response, elapsed: (playTime - run.Started) * 1000, source: "ai");
                    run.Delivery.Accept(Prompts.ParseExpression(response, run.Context).Turns); expressionBackoff.Success();
                }
                catch (Exception e) { Trace("response_rejected", run.Intent, ProviderClient.SafeError(e)); Failed(e); if (!Fallback(run)) { Remove(run); return; } }
            }
            else if (run.Intent.Trigger == Trigger.Combat && playTime - run.Started > 3)
            { if (!Fallback(run)) { Remove(run); return; } }
            else if (run.Intent.Fallback != null && playTime - run.Intent.Created >= Settings.FallbackDelaySeconds)
            { Fallback(run); }
            else if (playTime - run.Started > Math.Clamp(run.TimeoutSeconds, 3, 180)) { Remove(run, reason: "Request exceeded the unpaused waiting limit."); return; }
        }
        switch (run.Delivery.Advance(playTime, false, true, playTime - lastRelease >= 0.6))
        {
            case DeliveryAction.Show: ShowTurn(run); break;
            case DeliveryAction.Finished: Remove(run, !run.Delivery.CurrentWasDisplayed, run.Delivery.CurrentWasDisplayed ? "All accepted turns completed." : "Bubble expired without a successful draw."); break;
        }
    }
    private void ShowTurn(Running run)
    {
        var turn = run.Delivery.Current!;
        if (!TextPolicy.Valid(turn.Text, Settings.EffectiveLimit)) { Trace("line_rejected", run.Intent, "Current character limit", text: turn.Text); Remove(run); return; }
        var pawn = turn.PawnId == run.Intent.Pawn.ThingID ? run.Intent.Pawn : run.Intent.Other;
        if (pawn == null || !Context.CanExpress(pawn) || (run.Intent.Other != null && run.Intent.Pawn.Position.DistanceTo(run.Intent.Other.Position) > 12)) { Remove(run); return; }
        var recent = State.Transcript.AsEnumerable().Reverse().Where(t => t.SpeakerId == pawn.ThingID && Day - t.Day < 0.5).Take(4).Select(t => t.Text);
        if (TextPolicy.Repeated(turn.Text, recent)) { Trace("line_rejected", run.Intent, "Repeated recent line", text: turn.Text); Remove(run); return; }
        if (pawn != run.BubblePawn) { Integration.Remove(run.BubblePawn, run.Bubble); run.BubblePawn = pawn; run.Entry = new ExpressionEntry(pawn); run.Bubble = Integration.Add(pawn, run.Entry); }
        run.Entry.SetText(turn.Text); run.Entry.OnRendered = () => RecordRendered(run); Integration.Refresh(run.Bubble);
        lastRelease = playTime; Status = "Connected.";
    }
    private void RecordRendered(Running run)
    {
        if (Verse.Current.Game != game || disposed) return;
        var turn = run.Delivery.MarkRendered(playTime);
        if (turn == null) return;
        var record = new TranscriptEntry { ExchangeId = run.Exchange, SpeakerId = run.BubblePawn.ThingID, SpeakerName = run.BubblePawn.LabelShort,
            Participants = run.Context.Pawns.Select(p => p.Id).ToList(), Text = turn.Text, Day = Day, Trigger = run.Intent.Trigger.ToString(), Source = run.Source };
        State.Record(record); ExpressionsShown++; if (run.Delivery.IsFallback) FallbacksShown++;
        Trace("displayed", run.Intent, "Speaker " + record.SpeakerId + "; successfully drawn", text: record.Text, source: run.Source, elapsed: (playTime - run.Started) * 1000);
        // Generic filler is readable history, not a lasting personality event.
        if (run.Delivery.IsFallback) return;
        foreach (var pawn in new[] { run.Intent.Pawn, run.Intent.Other }.OfType<Pawn>())
            if (Context.Tracked(pawn, Settings)) Remember(pawn, record.SpeakerName + " expressed: " + record.Text, "generated expression", "heard or expressed");
    }
    private bool Fallback(Running run)
    {
        if (run.Intent.Fallback is { } candidate)
        {
            string reason = FallbackPolicy.Rejection(candidate, run.Context, Settings);
            if (reason.Length == 0)
            {
                bool sourceAccepted = run.Delivery.Accept(new[] { new Turn { PawnId = run.Intent.Pawn.ThingID, Text = candidate.Text } }, true);
                if (sourceAccepted)
                {
                    run.Source = "fallback:" + candidate.Source;
                    Trace("fallback_selected", run.Intent, candidate.Definition, text: candidate.Text, source: candidate.Source, context: journal == null ? "" : SYMJson.Write(run.Context));
                    run.Cancellation.Cancel(); ObserveTask(run.Task);
                }
                return sourceAccepted;
            }
            Trace("fallback_rejected", run.Intent, reason, text: candidate.Text, source: candidate.Source);
            run.Intent.Fallback = null;
        }
        if (run.Intent.Trigger != Trigger.Combat) return false;
        var profile = ProfileFor(run.Intent.Pawn);
        var options = profile.FallbackSignature == Prompts.Signature(Settings, State.ColonyRule, profile) ? profile.Fallbacks.Where(f => TextPolicy.Valid(f, Settings.EffectiveLimit)).ToList() : new List<string>();
        if (options.Count == 0 && State.ColonyRule.Length == 0 && profile.Annotation.Length == 0 && Settings.Language == "English")
        {
            bool human = run.Intent.Pawn.RaceProps.Humanlike;
            options = Settings.Style == ExpressionStyle.Dialogue && human ? new() { "Steady…", "Not now…", "Keep it together." } : new() { "Tenses.", "Shifts restlessly.", "Pauses, alert." };
        }
        options.RemoveAll(t => !TextPolicy.Valid(t, Settings.EffectiveLimit));
        if (options.Count == 0) return false;
        fallbackIndices.TryGetValue(profile.PawnId, out int index); fallbackIndices[profile.PawnId] = index + 1;
        bool accepted = run.Delivery.Accept(new[] { new Turn { PawnId = profile.PawnId, Text = options[index % options.Count] } }, true);
        if (accepted) { run.Source = "preset"; Trace("fallback_selected", run.Intent, "Generic combat fallback", text: options[index % options.Count], source: "preset"); }
        run.Cancellation.Cancel(); ObserveTask(run.Task); return accepted;
    }
    private void Remove(Running run, bool discarded = true, string reason = "Cancelled before completion.")
    {
        if (!running.Remove(run)) return;
        if (discarded) Discarded++;
        Trace(discarded ? "discarded" : "exchange_finished", run.Intent, reason, source: run.Source);
        bool continueConversation = ConversationPolicy.ContinueAfterFallback(discarded, run.Delivery.IsFallback, run.Intent.FollowUp, run.Intent.Other != null, Settings);
        string previousText = run.Delivery.Current?.Text ?? "";
        run.Delivery.Cancel(); run.Entry.OnRendered = null; run.Entry.Alive = false;
        try { Integration.Remove(run.BubblePawn, run.Bubble); }
        catch { Log.WarningOnce("[SpeakYourMind] A bubble could not be removed. Its request was cancelled and its display was expired.", 17393014); }
        finally { run.Cancellation.Cancel(); run.Cancellation.Dispose(); ObserveTask(run.Task); }
        if (continueConversation && Context.CanExpress(run.Intent.Other!) && Context.CanExpress(run.Intent.Pawn) && run.Intent.Other!.Position.DistanceTo(run.Intent.Pawn.Position) <= 12)
            Enqueue(run.Intent.Other!, Trigger.Social, "Continue this conversation after the already displayed line from " + run.Intent.Pawn.LabelShort + ": " + previousText +
                ". Respond naturally; do not repeat it or invent events. Original event: " + run.Intent.Fact, run.Intent.Pawn, followUp: true, exchange: run.Exchange);
    }
    private static void ObserveTask(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    private void StopAll()
    {
        foreach (var run in running.ToArray()) Remove(run);
        foreach (var intent in queue) Trace("discarded", intent, "Session stopped or connection reset before dispatch.");
        queue.Clear(); profileScan.Clear(); session.Cancel(); session.Dispose(); session = new();
        if (memoryTask != null) ObserveTask(memoryTask); memoryTask = null;
    }
    public void Shutdown() { if (disposed) return; StopAll(); Trace("session_end"); journal?.Dispose(); journal = null; session.Dispose(); provider.Dispose(); disposed = true; }
    public void RefreshConnections()
    {
        StopAll(); expressionBackoff.Success(); memoryBackoff.Success(); nextMemory = 0;
        Status = "Connection saved. Ready to resume."; MemoryStatus = "Waiting for background work.";
    }
    private void Failed(Exception e)
    {
        expressionBackoff.Fail(RealTime, e is ProviderException p ? p.RetryAfterSeconds : 0);
        Status = ProviderClient.SafeError(e);
        Trace("error", detail: Status);
    }
    private void StartMemory(double now)
    {
        nextMemory = now + 15;
        var established = State.Profiles.Where(p => p.Persistent).Select(p => p.PawnId).ToHashSet();
        var candidates = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => !p.Dead && (Context.Tracked(p, Settings) || established.Contains(p.ThingID))).ToList();
        var pawn = candidates.OrderByDescending(p => ProfileFor(p).Memories.Any(m => !m.Scored)).ThenBy(p => ProfileFor(p).UpdatedDay)
            .FirstOrDefault(p => { var profile = ProfileFor(p); return profile.Voice.Length == 0 || profile.Memories.Any(m => !m.Scored) || profile.FallbackSignature != Prompts.Signature(Settings, State.ColonyRule, profile) || Day - profile.UpdatedDay >= 1; });
        if (pawn == null) return;
        var config = SYMMod.Instance.Settings;
        var connection = (config.SeparateMemoryModel ? config.MemoryConnection : config.Connection).Copy();
        string key = Credentials.Read(connection);
        journal?.AddSecret(key);
        try { ProviderClient.ValidateConfiguration(connection, key); }
        catch (Exception e) { MemoryFailed(e); return; }
        if (!budget.TrySpend(now, Settings.RequestsPerMinute)) return;
        lastMemoryStart = now;
        memoryProfile = ProfileFor(pawn);
        var snapshot = Context.Capture(pawn);
        memoryFacts = Context.StableFacts(snapshot); memoryFingerprint = Context.Fingerprint(memoryFacts);
        memorySignature = Prompts.Signature(Settings, State.ColonyRule, memoryProfile);
        var memories = memoryProfile.Memories.Where(m => !m.Scored).Take(20).Concat(MemoryPolicy.Relevant(memoryProfile, Day, Settings).Take(8)).Distinct().ToList();
        memoryIds = memories.Select(m => m.Id).ToHashSet();
        var input = SYMJson.Write(new { pawn = snapshot, previousVoice = memoryProfile.Voice, memories, colonyRule = State.ColonyRule,
            annotation = memoryProfile.Annotation, style = Settings.Style.ToString(), language = Settings.Language, maxCharacters = Settings.EffectiveLimit });
        memoryRequestId = Guid.NewGuid().ToString("N");
        Trace("memory_request", request: memoryRequestId, system: Prompts.ProfileSystem, context: input, connection: connection, source: "ai-memory");
        memoryTask = provider.Generate(connection, key, Prompts.ProfileSystem, input, session.Token);
        TraceCompletion(memoryTask, memoryRequestId, "ai-memory");
        RequestsStarted++; MemoryStatus = "Updating " + memoryProfile.Name + "…";
    }
    private void FinishMemory()
    {
        if (memoryTask?.IsCompleted != true) return;
        try
        {
            string raw = memoryTask.GetAwaiter().GetResult();
            Trace("memory_response", request: memoryRequestId, response: raw, source: "ai-memory");
            var response = SYMJson.Read<ProfileResponse>(raw);
            var profile = memoryProfile!;
            if (memorySignature != Prompts.Signature(Settings, State.ColonyRule, profile)) { Trace("memory_rejected", request: memoryRequestId, detail: "Player instructions or settings changed while waiting."); return; }
            if (string.IsNullOrWhiteSpace(response.Voice) || response.Voice.Length > 3000) throw new FormatException("Invalid personality response.");
            profile.Voice = TextPolicy.Clean(response.Voice); profile.Fingerprint = memoryFingerprint; profile.LastProfileFacts = memoryFacts; profile.UpdatedDay = Day;
            foreach (var score in response.Scores ?? new())
            {
                if (score == null || !memoryIds.Contains(score.Id) || !float.IsFinite(score.Importance) || score.Importance < 0 || score.Importance > 1) continue;
                var memory = profile.Memories.Find(m => m.Id == score.Id);
                if (memory == null) continue;
                ProfilePolicy.ApplyScore(memory, score.Importance, score.Reason);
            }
            profile.Fallbacks = (response.Fallbacks ?? new()).Where(f => f != null).Select(TextPolicy.Clean).Where(f => TextPolicy.Valid(f, Settings.EffectiveLimit)).Distinct().Take(8).ToList();
            profile.FallbackSignature = memorySignature;
            memoryBackoff.Success(); MemoryStatus = "Updated " + profile.Name + "."; State.Touch();
            Trace("memory_applied", request: memoryRequestId, detail: "Profile " + profile.PawnId + " updated.", source: "ai-memory");
        }
        catch (Exception e) { MemoryFailed(e); }
        finally { memoryTask = null; memoryProfile = null; }
    }
    private void MemoryFailed(Exception e)
    {
        Trace("memory_error", request: memoryRequestId, detail: ProviderClient.SafeError(e), source: "ai-memory");
        memoryBackoff.Fail(RealTime, e is ProviderException p ? p.RetryAfterSeconds : 0);
        MemoryStatus = ProviderClient.SafeError(e);
    }
}
