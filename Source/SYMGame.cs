using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public static double Day => (Find.TickManager?.TicksGame ?? 0) / 60000.0;
    private static double RealTime => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private Preferences Settings => SYMMod.Instance.Settings.Preferences;
    private readonly Game game;
    private readonly ProviderClient provider = new();
    private readonly RequestBudget budget = new();
    private readonly Random expressionRandom = new(); // Never consume the game's simulation RNG.
    private readonly List<Intent> queue = new();
    private readonly List<Running> running = new();
    private readonly Dictionary<string, double> cooldowns = new();
    private readonly Dictionary<string, float> previousNeeds = new();
    private readonly Dictionary<int, (double time, List<string> items)> resources = new();
    private readonly Dictionary<string, int> fallbackIndices = new();
    private Task<string>? memoryTask;
    private Profile? memoryProfile;
    private string memoryFingerprint = "", memoryFacts = "", memorySignature = "";
    private HashSet<string> memoryIds = new();
    private double lastUpdate = RealTime, playTime, nextAmbient, nextProfiles, nextMemory, backoffUntil, lastRelease, lastMemoryStart;
    private int lastCleanupDay = -1, failures;
    private bool wasEnabled;
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
        public int Tick;
        public double Created;
    }
    private sealed class Running
    {
        public Intent Intent = null!;
        public Task<string> Task = null!;
        public GenerationContext Context = null!;
        public CancellationTokenSource Cancellation = null!;
        public double Started, ShownAt;
        public ExpressionEntry Entry = null!;
        public object Bubble = null!;
        public Pawn BubblePawn = null!;
        public List<Turn>? Turns;
        public int Index;
        public bool Recorded, Fallback;
        public string Exchange = Guid.NewGuid().ToString("N");
    }

    public SYMGame(Game game) { this.game = game; }
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
            try { State = SYMJson.Read<SaveState>(json); }
            catch { unreadableState = json; Log.Error("[SpeakYourMind] Saved history could not be read. Its original data will be preserved; generation is disabled for this colony."); }
        }
    }
    public Profile ProfileFor(Pawn pawn) => State.Profile(pawn.ThingID, pawn.LabelShort, Context.Tracked(pawn, Settings));
    private void Remember(Pawn pawn, string fact, string source = "game", string perspective = "experienced")
    {
        var p = ProfileFor(pawn);
        if (p.Memories.Any(m => m.Fact == fact && Day - m.Day < 0.1)) return;
        p.Memories.Add(new Memory { Fact = fact, Source = source, Perspective = perspective, Day = Day });
    }
    public void ObserveSocial(LogEntry entry)
    {
        if (entry is ExpressionEntry) return;
        var type = entry.GetType();
        if (entry is not PlayLogEntry_Interaction && type.Name != "PlayLogEntry_InteractionSinglePawn") return;
        var pawn = AccessTools.Field(type, "initiator")?.GetValue(entry) as Pawn;
        var other = AccessTools.Field(type, "recipient")?.GetValue(entry) as Pawn;
        if (pawn == null) return;
        string fact = TextPolicy.Clean(entry.ToGameStringFromPOV(pawn, true));
        if (Context.Tracked(pawn, Settings)) Remember(pawn, fact);
        if (other != null && Context.Tracked(other, Settings)) Remember(other, fact);
        Enqueue(pawn, Trigger.Social, fact, other);
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
    public void ObserveDeath(Pawn dead, List<Pawn> relatives)
    {
        foreach (var pawn in relatives.Where(p => !p.Dead))
        {
            string relationship = string.Join(", ", pawn.GetRelations(dead).Select(r => r.label));
            string fact = dead.LabelShort + " died. Relationship to this pawn: " + relationship + ".";
            Remember(pawn, fact, "game", "personal relationship; not necessarily witnessed");
            Enqueue(pawn, Trigger.Colony, fact);
        }
        if (dead.Faction == Faction.OfPlayer || dead.IsPrisonerOfColony) ObserveColony(dead.LabelShort + " died.");
    }
    public void ObserveColony(string fact)
    {
        if (string.IsNullOrWhiteSpace(fact) || State.ColonyEvents.Any(e => e.Fact == fact && Day - e.Day < 0.1)) return;
        State.ColonyEvents.Add(new Memory { Fact = fact, Day = Day, Importance = 0.5f, Scored = true, Perspective = "colony awareness" });
        foreach (var pawn in Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => Context.Tracked(p, Settings) && Context.ColonyAware(p)))
            Remember(pawn, fact, "game", "colony awareness; not necessarily witnessed");
        var observed = Find.CurrentMap?.mapPawns.AllPawnsSpawned.Where(p => Context.CanExpress(p) && Context.ColonyAware(p)).OrderByDescending(p => Find.Selector.IsSelected(p)).FirstOrDefault();
        if (observed != null) Enqueue(observed, Trigger.Colony, fact);
    }
    private void Enqueue(Pawn pawn, Trigger trigger, string fact, Pawn? other = null, string need = "", float level = 0)
    {
        if (!Settings.Enabled || !Settings.Allows(trigger) || !Context.CanExpress(pawn) || queue.Count >= 24) return;
        string key = pawn.ThingID + ":" + trigger;
        if (cooldowns.TryGetValue(key, out double until) && until > RealTime) return;
        if (queue.Any(i => i.Pawn == pawn) || running.Any(r => r.Intent.Pawn == pawn || r.Intent.Other == pawn)) return;
        if (other != null && (!Context.CanExpress(other) || other.Position.DistanceTo(pawn.Position) > 12)) other = null;
        queue.Add(new Intent { Pawn = pawn, Other = other, Trigger = trigger, Fact = fact, Need = need, NeedLevel = level, Tick = Find.TickManager.TicksGame, Created = RealTime });
        cooldowns[key] = RealTime + (trigger == Trigger.Combat ? 10 : 65 - Math.Clamp(Settings.Activity, 1, 4) * 10);
    }
    private bool Relevant(Intent intent)
    {
        if (!Context.CanExpress(intent.Pawn) || !Settings.Allows(intent.Trigger)) return false;
        if (Find.TickManager.TicksGame - intent.Tick > (intent.Trigger == Trigger.Combat ? 900 : 5000)) return false;
        if (intent.Trigger == Trigger.Health && intent.Pawn.health.hediffSet.PainTotal <= 0.01f && !intent.Pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any()) return false;
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
            Language = s.Language, MaxCharacters = s.EffectiveLimit, MaxTurns = intent.Other == null ? 1 : Math.Clamp(s.MaxTurns, 2, 4), Style = s.Style, Length = s.Length };
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
        context.RecentExchange = State.Transcript.Where(t => t.Participants.Contains(intent.Pawn.ThingID)).TakeLast(6).ToList();
        return context;
    }
    public override void GameComponentUpdate()
    {
        double now = RealTime, delta = Math.Clamp(now - lastUpdate, 0, 0.25); lastUpdate = now;
        if (Verse.Current.Game != game) { StopAll(); return; }
        if (!Settings.Enabled || !Integration.Ready || !StateHealthy)
        { if (wasEnabled) StopAll(); wasEnabled = false; return; }
        wasEnabled = true;
        if (Find.TickManager.Paused) return; // Finished requests are held, with no UI/history changes.
        playTime += delta;
        try
        {
            for (int i = running.Count - 1; i >= 0; i--) UpdateRun(running[i], now);
            FinishMemory();
            if (now >= nextProfiles) { ScanProfiles(); nextProfiles = now + 30; }
            if (now >= nextAmbient) { Ambient(); nextAmbient = now + new[] { 25, 12, 6, 3 }[Math.Clamp(Settings.Activity - 1, 0, 3)]; }
            if ((int)Day != lastCleanupDay) { State.Prune(Day, Settings); lastCleanupDay = (int)Day; }
            foreach (var k in cooldowns.Where(x => x.Value < now).Select(x => x.Key).ToList()) cooldowns.Remove(k);
            queue.RemoveAll(i => !Relevant(i) || now - i.Created > 30);
            if (now < backoffUntil || !Integration.Rendering) return;
            // Reserve occasional capacity for memory even when the visible-expression queue stays busy.
            if (memoryTask == null && now >= nextMemory && (queue.Count == 0 || now - lastMemoryStart > 60)) StartMemory(now);
            if (running.Count < Math.Clamp(Settings.MaxBubbles, 1, 8) && running.Count(r => !r.Task.IsCompleted && r.Turns == null) < 2 && queue.Count > 0)
            {
                var next = queue.OrderByDescending(i => (Find.Selector.IsSelected(i.Pawn) ? 100 : 0) + (i.Trigger == Trigger.Combat ? 60 : i.Trigger == Trigger.Observation ? 0 : 30)).ThenBy(i => i.Created).First();
                if (budget.TrySpend(now, Settings.RequestsPerMinute)) { queue.Remove(next); Start(next, now); }
            }
        }
        catch (Exception e) { Failed(e); }
    }
    private void ScanProfiles()
    {
        var pawns = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => !p.Dead && Context.Tracked(p, Settings)).ToList();
        foreach (var pawn in pawns)
        {
            var profile = ProfileFor(pawn);
            var snapshot = Context.Capture(pawn);
            string facts = Context.StableFacts(snapshot), fingerprint = Context.Fingerprint(facts);
            if (profile.Fingerprint.Length == 0 && profile.Memories.Count == 0)
                Remember(pawn, "Known biography and relationships on first observation: " + facts, "game", "established facts");
            else if (profile.LastProfileFacts.Length > 0 && profile.LastProfileFacts != facts && !profile.Memories.Any(m => m.Fact.EndsWith(facts)))
                Remember(pawn, "Personal facts changed. Previous: " + profile.LastProfileFacts + "; now: " + facts, "game", "experienced");
        }
        var aliveIds = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Select(p => p.ThingID).ToHashSet();
        State.Profiles.RemoveAll(p => !p.Persistent && p.Annotation.Length == 0 && !aliveIds.Contains(p.PawnId));
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
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        var entry = new ExpressionEntry(intent.Pawn);
        var run = new Running { Intent = intent, Context = context, Started = now, Entry = entry, BubblePawn = intent.Pawn, Cancellation = cancellation };
        run.Bubble = Integration.Add(intent.Pawn, entry);
        run.Task = provider.Generate(connection, Credentials.Read(connection), Prompts.ExpressionSystem, SYMJson.Write(context), cancellation.Token);
        running.Add(run); Status = "Generating expressions…";
    }
    private void UpdateRun(Running run, double now)
    {
        if (!Relevant(run.Intent) || !Context.Visible(run.BubblePawn) || !Integration.Rendering) { Remove(run); return; }
        if (run.Context.ColonyRule != State.ColonyRule || run.Context.Style != Settings.Style || run.Context.Language != Settings.Language ||
            run.Context.Profiles.Any(p => p.Annotation != State.Profiles.Find(x => x.PawnId == p.PawnId)?.Annotation)) { Remove(run); return; }
        if (run.Turns == null)
        {
            string dots = new[] { ".  ", ".. ", "...", "   " }[(int)(playTime * 3) % 4];
            if (run.Entry.Text != dots) { run.Entry.SetText(dots); Integration.Refresh(run.Bubble); }
            if (run.Task.IsCompleted)
            {
                try { run.Turns = Prompts.ParseExpression(run.Task.GetAwaiter().GetResult(), run.Context).Turns; failures = 0; }
                catch (Exception e) { Failed(e); if (!Fallback(run)) { Remove(run); return; } }
            }
            else if (run.Intent.Trigger == Trigger.Combat && now - run.Started > 3)
            { if (!Fallback(run)) { Remove(run); return; } }
            else if (now - run.Started > 45) { Remove(run); return; }
            if (run.Turns != null && playTime - lastRelease >= 0.6) ShowTurn(run);
            return;
        }
        // A completed request may be waiting for staggered release after a pause.
        if (run.ShownAt == 0) { if (playTime - lastRelease >= 0.6) ShowTurn(run); return; }
        if (run.Entry.Rendered && !run.Recorded)
        {
            var turn = run.Turns[run.Index];
            var record = new TranscriptEntry { ExchangeId = run.Exchange, SpeakerId = run.BubblePawn.ThingID, SpeakerName = run.BubblePawn.LabelShort,
                Participants = run.Context.Pawns.Select(p => p.Id).ToList(), Text = turn.Text, Day = Day, Trigger = run.Intent.Trigger.ToString(), Source = run.Fallback ? "preset" : "ai" };
            State.Transcript.Add(record); run.Recorded = true;
            foreach (var pawn in new[] { run.Intent.Pawn, run.Intent.Other }.Where(p => p != null).Cast<Pawn>())
                if (Context.Tracked(pawn, Settings)) Remember(pawn, record.SpeakerName + " expressed: " + record.Text, "generated expression", "heard or expressed");
        }
        if (playTime - run.ShownAt > Math.Clamp(run.Entry.Text.Length / 18.0, 3, 12))
        {
            if (!run.Recorded || ++run.Index >= run.Turns.Count) { Remove(run); return; }
            ShowTurn(run);
        }
    }
    private void ShowTurn(Running run)
    {
        var turn = run.Turns![run.Index];
        if (!TextPolicy.Valid(turn.Text, Settings.EffectiveLimit)) { Remove(run); return; }
        var pawn = turn.PawnId == run.Intent.Pawn.ThingID ? run.Intent.Pawn : run.Intent.Other;
        if (pawn == null || !Context.CanExpress(pawn) || (run.Intent.Other != null && run.Intent.Pawn.Position.DistanceTo(run.Intent.Other.Position) > 12)) { Remove(run); return; }
        if (pawn != run.BubblePawn) { Integration.Remove(run.BubblePawn, run.Bubble); run.BubblePawn = pawn; run.Entry = new ExpressionEntry(pawn); run.Bubble = Integration.Add(pawn, run.Entry); }
        run.Entry.SetText(turn.Text); Integration.Refresh(run.Bubble);
        run.ShownAt = playTime; run.Recorded = false; lastRelease = playTime; Status = "Connected.";
    }
    private bool Fallback(Running run)
    {
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
        run.Turns = new() { new Turn { PawnId = profile.PawnId, Text = options[index % options.Count] } };
        run.Fallback = true; run.Cancellation.Cancel(); ObserveTask(run.Task); return true;
    }
    private void Remove(Running run)
    {
        run.Entry.Alive = false; Integration.Remove(run.BubblePawn, run.Bubble);
        run.Cancellation.Cancel(); run.Cancellation.Dispose(); ObserveTask(run.Task); running.Remove(run);
    }
    private static void ObserveTask(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    private void StopAll()
    {
        foreach (var run in running.ToArray()) Remove(run);
        queue.Clear(); session.Cancel(); session.Dispose(); session = new();
        if (memoryTask != null) ObserveTask(memoryTask); memoryTask = null;
    }
    public void Shutdown() { StopAll(); provider.Dispose(); }
    private void Failed(Exception e)
    {
        failures = Math.Min(failures + 1, 6); backoffUntil = RealTime + Math.Min(120, Math.Pow(2, failures) * 2);
        Status = ProviderClient.SafeError(e);
    }
    private void StartMemory(double now)
    {
        nextMemory = now + 15;
        var candidates = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => !p.Dead && (Context.Tracked(p, Settings) || Context.Visible(p))).ToList();
        var pawn = candidates.OrderByDescending(p => ProfileFor(p).Memories.Any(m => !m.Scored)).ThenBy(p => ProfileFor(p).UpdatedDay)
            .FirstOrDefault(p => { var profile = ProfileFor(p); return profile.Voice.Length == 0 || profile.Memories.Any(m => !m.Scored) || profile.FallbackSignature != Prompts.Signature(Settings, State.ColonyRule, profile) || Day - profile.UpdatedDay >= 1; });
        if (pawn == null) return;
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
        var config = SYMMod.Instance.Settings;
        var connection = (config.SeparateMemoryModel ? config.MemoryConnection : config.Connection).Copy();
        memoryTask = provider.Generate(connection, Credentials.Read(connection), Prompts.ProfileSystem, input, session.Token);
    }
    private void FinishMemory()
    {
        if (memoryTask?.IsCompleted != true) return;
        try
        {
            var response = SYMJson.Read<ProfileResponse>(memoryTask.GetAwaiter().GetResult());
            var profile = memoryProfile!;
            if (memorySignature != Prompts.Signature(Settings, State.ColonyRule, profile)) return;
            if (string.IsNullOrWhiteSpace(response.Voice) || response.Voice.Length > 3000) throw new FormatException("Invalid personality response.");
            profile.Voice = TextPolicy.Clean(response.Voice); profile.Fingerprint = memoryFingerprint; profile.LastProfileFacts = memoryFacts; profile.UpdatedDay = Day;
            foreach (var score in response.Scores ?? new())
            {
                if (!memoryIds.Contains(score.Id) || !float.IsFinite(score.Importance) || score.Importance < 0 || score.Importance > 1) continue;
                var memory = profile.Memories.Find(m => m.Id == score.Id);
                if (memory == null) continue;
                ProfilePolicy.ApplyScore(memory, score.Importance, score.Reason);
            }
            profile.Fallbacks = (response.Fallbacks ?? new()).Where(f => f != null).Select(TextPolicy.Clean).Where(f => TextPolicy.Valid(f, Settings.EffectiveLimit)).Distinct().Take(8).ToList();
            profile.FallbackSignature = memorySignature;
        }
        catch (Exception e) { Failed(e); }
        finally { memoryTask = null; memoryProfile = null; }
    }
}
