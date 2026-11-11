using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using SpeakYourMind.Core;
using UnityEngine;
using Verse;

namespace SpeakYourMind;

public sealed class SYMSettings : ModSettings
{
    public Preferences Preferences = new();
    public Connection Connection = new();
    public Connection MemoryConnection = new();
    public bool SeparateMemoryModel;
    public override void ExposeData()
    {
        string prefs = SYMJson.Write(Preferences), connection = SYMJson.Write(Connection), memory = SYMJson.Write(MemoryConnection);
        Scribe_Values.Look(ref prefs, "SYM_preferences", "");
        Scribe_Values.Look(ref connection, "SYM_connection", "");
        Scribe_Values.Look(ref memory, "SYM_memoryConnection", "");
        Scribe_Values.Look(ref SeparateMemoryModel, "SYM_separateMemoryModel", false);
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            try { if (prefs.Length > 0) Preferences = SYMJson.Read<Preferences>(prefs); if (connection.Length > 0) Connection = SYMJson.Read<Connection>(connection); if (memory.Length > 0) MemoryConnection = SYMJson.Read<Connection>(memory); Preferences.Normalize(); }
            catch { Log.Warning("[SpeakYourMind] Could not read settings. Check the setup screen."); }
            Preferences.Normalize(); Connection.Normalize(); MemoryConnection.Normalize();
        }
    }
}

public sealed class SYMMod : Mod
{
    public static SYMMod Instance = null!;
    public SYMSettings Settings;
    private Vector2 scroll;
    private float settingsHeight = 1550;
    private string characterBuffer = "", pawnDaysBuffer = "", colonyDaysBuffer = "";
    private string fallbackDelayBuffer = "", debugLimitBuffer = "", debugNote = "";
    public SYMMod(ModContentPack pack) : base(pack)
    {
        Instance = this; Settings = GetSettings<SYMSettings>();
        LongEventHandler.ExecuteWhenFinished(Integration.Initialize);
    }
    public override string SettingsCategory() => "SpeakYourMind";
    public override void DoSettingsWindowContents(Rect rect)
    {
        Widgets.BeginScrollView(rect, ref scroll, new Rect(0, 0, rect.width - 24, Math.Max(settingsHeight, rect.height)));
        var l = new Listing_Standard(); l.Begin(new Rect(0, 0, rect.width - 30, Math.Max(settingsHeight, rect.height)));
        var s = Settings.Preferences;
        l.Label("SpeakYourMind — every pawn has a story.");
        l.Label("Bubble integration: " + Integration.Status);
        if (l.ButtonText("Guided AI setup")) Find.WindowStack.Add(new SetupWindow(Settings.Connection));
        l.CheckboxLabeled("Enable contextual expressions", ref s.Enabled);
        l.Label("Connection: " + Settings.Connection.Provider + " / " + Settings.Connection.Model);
        l.Label("Status: " + (SYMGame.Current?.Status ?? "Load a colony to begin."));
        if (SYMGame.Current is { } game)
        {
            if (Find.TickManager.Paused) l.Label("Paused — completed replies will appear after you resume.");
            if (game.RetryIn > 0) l.Label("Retrying in " + Math.Ceiling(game.RetryIn) + " seconds.");
            l.Label("Memory: " + game.MemoryStatus);
            l.Label("This session: " + game.RequestsStarted + " requests · " + game.ExpressionsShown + " expressions · " + game.ActiveBubbles + " bubbles · " + game.Queued + " queued");
            if (l.ButtonText("Copy diagnostic summary (no keys or conversation text)")) GUIUtility.systemCopyBuffer = Diagnostics.Summary();
            if (l.ButtonText("Retry connection now")) game.RefreshConnections();
        }
        l.GapLine();
        if (l.ButtonText("Communication style: " + s.Style)) s.Style = (ExpressionStyle)(((int)s.Style + 1) % 3);
        if (l.ButtonText("Length: " + s.Length)) s.Length = (ExpressionLength)(((int)s.Length + 1) % 3);
        s.Activity = (int)Math.Round(l.SliderLabeled("Activity: " + new[] { "Quiet", "Balanced", "Lively", "Chatty" }[Math.Clamp(s.Activity - 1, 0, 3)], s.Activity, 1, 4));
        l.TextFieldNumericLabeled("Character limit override (0 = length default)", ref s.MaxCharacters, ref characterBuffer, 0, 2000);
        l.Label("Current limit: " + s.EffectiveLimit + " visible characters, including punctuation and player-rule additions.");
        s.MaxBubbles = (int)l.SliderLabeled("Simultaneous bubbles: " + s.MaxBubbles, s.MaxBubbles, 1, 8);
        s.MaxTurns = (int)l.SliderLabeled("Maximum exchange turns: " + s.MaxTurns, s.MaxTurns, 2, 4);
        l.CheckboxLabeled("Allow follow-up lines (including replies after a quick fallback)", ref s.FollowUps);
        s.RequestsPerMinute = (int)l.SliderLabeled("AI requests per real minute: " + s.RequestsPerMinute, s.RequestsPerMinute, 1, 60);
        s.Language = l.TextEntryLabeled("Expression language", s.Language);
        l.GapLine();
        l.CheckboxLabeled("Social exchanges", ref s.Social);
        l.CheckboxLabeled("Needs and discomfort (including mod-added needs)", ref s.Needs);
        l.CheckboxLabeled("Health reactions", ref s.Health);
        l.CheckboxLabeled("Combat callouts and generic fallbacks", ref s.Combat);
        l.CheckboxLabeled("Colony events", ref s.Colony);
        l.CheckboxLabeled("Ambient observations", ref s.Observation);
        l.CheckboxLabeled("Keep persistent guest profiles", ref s.TrackGuests);
        l.GapLine();
        l.Label("Optional quick replies when AI is slow");
        l.Label(OptionalDialogue.Status);
        l.CheckboxLabeled("Use SpeakUp text, including installed Extension / Child / ChildSFW content", ref s.SpeakUpFallback);
        l.CheckboxLabeled("Use Vanilla Social Interactions Expanded event text", ref s.VSIEFallback);
        l.CheckboxLabeled("Use Rumor Has It event text", ref s.RumorFallback);
        l.CheckboxLabeled("Route supported Callouts text through SpeakYourMind", ref s.CalloutsFallback);
        l.TextFieldNumericLabeled("Quick reply delay (unpaused seconds)", ref s.FallbackDelaySeconds, ref fallbackDelayBuffer, 2, 30);
        l.Label("Only text from the current event is eligible. Combat still falls back after 3 seconds. Player instructions, language, species and length checks take priority. Social event summaries require Hybrid or Narration. These mods are optional and installed separately; their own gameplay features remain active.");
        l.Label("SpeakUp Child and ChildSFW are alternative content packs. Choose the version you want in your mod list; SpeakYourMind uses the final installed rules, not a second copy of their dialogue.");
        l.GapLine();
        l.Label("History retention is in game days. Zero = complete transcript.");
        l.TextFieldNumericLabeled("Pawn history days (0 = forever)", ref s.PawnHistoryDays, ref pawnDaysBuffer, 0, 60000);
        l.TextFieldNumericLabeled("Colony feed days (0 = forever)", ref s.ColonyHistoryDays, ref colonyDaysBuffer, 0, 60000);
        l.Label("An entry is deleted only after both history limits expire. Shortening limits removes old entries at the next daily cleanup.");
        s.MemoryScaleDays = l.SliderLabeled("Memory retention scale: " + s.MemoryScaleDays.ToString("F0") + " days", s.MemoryScaleDays, 5, 120);
        s.MemoryExponent = l.SliderLabeled("Memory importance curve: " + s.MemoryExponent.ToString("F1"), s.MemoryExponent, 0.5f, 3);
        l.Label("Importance 0.1: " + MemoryPolicy.Lifetime(0.1f, s).ToString("F1") + " days; 0.5: " + MemoryPolicy.Lifetime(0.5f, s).ToString("F0") + " days; 1: forever.");
        l.GapLine();
        l.CheckboxLabeled("Advanced: use a separate model for memory", ref Settings.SeparateMemoryModel);
        if (Settings.SeparateMemoryModel && l.ButtonText("Configure memory model")) Find.WindowStack.Add(new SetupWindow(Settings.MemoryConnection, true));
        if (l.ButtonText("Open colony history and annotations") && Current.Game != null) Find.WindowStack.Add(new HistoryWindow());
        l.Label("Player instructions are saved per colony. Select a pawn in the history window to add an individual instruction.");
        l.GapLine();
        l.CheckboxLabeled("Debug: record dialogue and full prompt context to local files", ref s.DebugLogging);
        l.Label("Off by default. Debug files contain pawn names, colony details, player instructions, prompts, model replies, fallback decisions and displayed text. API credentials and request headers are excluded. Nothing is uploaded automatically. Review files before sharing.");
        l.TextFieldNumericLabeled("Debug session size limit (MB; next session)", ref s.DebugSessionMegabytes, ref debugLimitBuffer, 1, 1000);
        if (SYMGame.Current is { } debugGame)
        {
            debugGame.SyncDebug(); l.Label("Debug: " + debugGame.DebugStatus);
            if (l.ButtonText("Copy debug folder path")) GUIUtility.systemCopyBuffer = SYMGame.DebugFolder;
            if (s.DebugLogging)
            {
                debugNote = l.TextEntryLabeled("Playtest note", debugNote);
                if (debugNote.Trim().Length > 0 && l.ButtonText("Add note to debug log")) { debugGame.AddDebugNote(debugNote); debugNote = ""; }
            }
        }
        settingsHeight = l.CurHeight + 20; l.End(); Widgets.EndScrollView();
    }
}

public sealed class SetupWindow : Window
{
    private readonly Connection target;
    private readonly ConnectionDraft draft;
    private Connection connection => draft.Value;
    private readonly bool memory;
    private string notice = "", keyNotice = "", testSample = "", testedConnection = "", tokenBuffer = "", timeoutBuffer = "";
    private Vector2 scroll;
    private float contentHeight = 600;
    private int step;
    private Task<string>? test;
    private Task<List<string>>? modelTask;
    private string modelStamp = "";
    private readonly ProviderClient client = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly System.Diagnostics.Stopwatch timer = new();
    public override Vector2 InitialSize => new(Math.Min(780, UI.screenWidth - 40), Math.Min(720, UI.screenHeight - 60));
    public SetupWindow(Connection c, bool memory = false)
    {
        target = c; draft = new ConnectionDraft(c) { Key = Credentials.Read(c) }; this.memory = memory;
        RefreshNumberFields();
        doCloseX = true; forcePause = true; absorbInputAroundWindow = true;
    }
    private void RefreshNumberFields()
    {
        timeoutBuffer = connection.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        tokenBuffer = connection.OutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private void Preset(string name, ProviderKind provider, string url)
    {
        connection.Provider = provider; connection.BaseUrl = url; connection.Model = "";
        connection.SendReasoning = provider == ProviderKind.OpenAI; connection.Reasoning = "low";
        connection.TimeoutSeconds = url.Contains("localhost") ? 90 : 45;
        RefreshNumberFields();
        draft.Key = Credentials.Read(connection); notice = ""; keyNotice = ""; testSample = ""; step = 1; scroll = Vector2.zero;
    }
    public override void DoWindowContents(Rect rect)
    {
        if (modelTask?.IsCompleted == true)
        {
            try
            {
                var models = modelTask.GetAwaiter().GetResult();
                if (modelStamp != draft.Stamp) notice = "Settings changed while loading models. Load the list again.";
                else if (models.Count == 0) notice = "The server returned no models. Install a local model or enter the identifier from your provider.";
                else
                {
                    Find.WindowStack.Add(new FloatMenu(models.Select(id => new FloatMenuOption(TextPolicy.Clean(id), () => connection.Model = id)).ToList()));
                    notice = "Choose a text model, then run the preview test. Listing a model does not guarantee it supports every setting.";
                }
            }
            catch (Exception e) { notice = ProviderClient.SafeError(e) + " You can still enter a model name manually."; }
            modelTask = null;
        }
        if (test?.IsCompleted == true)
        {
            try { var result = test.GetAwaiter().GetResult(); if (draft.ValidateTest(testedConnection)) { testSample = result; notice = "Valid response in " + timer.Elapsed.TotalSeconds.ToString("F1") + " seconds."; } else notice = "Settings changed during the test. Test the new connection again."; }
            catch (Exception e) { notice = ProviderClient.SafeError(e); }
            test = null;
        }
        var viewport = new Rect(0, 0, rect.width, rect.height - 45);
        Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, rect.width - 24, Math.Max(contentHeight, viewport.height)));
        var l = new Listing_Standard(); l.Begin(new Rect(0, 0, rect.width - 30, Math.Max(contentHeight, viewport.height)));
        l.Label("SpeakYourMind " + (memory ? "memory " : "") + "setup — " + (step + 1) + "/4"); l.GapLine();
        if (step == 0)
        {
            l.Label("Choose where the AI runs. Cloud services receive selected pawn and colony context and may charge for API usage. Local models run through a server you start on this computer.");
            if (connection.Model.Length > 0 && l.ButtonText("Edit current connection: " + connection.Provider + " / " + connection.Model)) { step = 1; scroll = Vector2.zero; }
            if (l.ButtonText("OpenAI")) Preset("OpenAI", ProviderKind.OpenAI, "https://api.openai.com/v1");
            if (l.ButtonText("Claude")) Preset("Claude", ProviderKind.Claude, "https://api.anthropic.com/v1");
            if (l.ButtonText("Gemini")) Preset("Gemini", ProviderKind.Gemini, "https://generativelanguage.googleapis.com/v1beta");
            if (l.ButtonText("Local — Ollama")) Preset("Ollama", ProviderKind.Compatible, "http://localhost:11434/v1");
            if (l.ButtonText("Local — LM Studio")) Preset("LM Studio", ProviderKind.Compatible, "http://localhost:1234/v1");
            if (l.ButtonText("OpenRouter")) Preset("OpenRouter", ProviderKind.Compatible, "https://openrouter.ai/api/v1");
            if (l.ButtonText("Other OpenAI-compatible server")) Preset("Custom", ProviderKind.Compatible, "http://localhost:1234/v1");
        }
        else if (step == 1)
        {
            l.Label("Connect " + connection.Provider + ". Use an API credential from the provider's developer dashboard. A chat-app account alone does not establish API access. This build uses API keys; account login is not implemented.");
            string address = l.TextEntryLabeled("Server address", connection.BaseUrl);
            if (address != connection.BaseUrl) { connection.BaseUrl = address; draft.Key = ""; keyNotice = ""; }
            l.Label("API key (stored encrypted for this Windows account; never included in saves)");
            string enteredKey = GUI.PasswordField(l.GetRect(32), draft.Key, '*');
            if (enteredKey != draft.Key) { draft.Key = enteredKey; keyNotice = ""; }
            l.Label("First-time setup: paste your API key above, then choose Continue to model selection. Your key is saved only after a successful test and the final Save step.");
            if (l.ButtonText("Use a previously saved key (optional)"))
            {
                string savedKey = Credentials.Read(connection);
                if (string.IsNullOrWhiteSpace(savedKey)) keyNotice = "No saved key could be loaded for this server. Your current entry has been kept. Paste your key if the box is empty, then continue below.";
                else { draft.Key = savedKey; keyNotice = "Previously saved key loaded. Continue to model selection to test the connection."; }
            }
            if (keyNotice.Length > 0) l.Label(keyNotice);
            l.Label("For a local model: install Ollama or LM Studio, download a model suitable for your hardware, start its local API server, then copy its exact model name below. A localhost server may not require a key.");
            if (l.ButtonText("Open official setup instructions")) Application.OpenURL(connection.Provider switch
            {
                ProviderKind.OpenAI => "https://developers.openai.com/api/docs/quickstart",
                ProviderKind.Claude => "https://platform.claude.com/docs/en/get-started",
                ProviderKind.Gemini => "https://aistudio.google.com/apikey",
                _ => connection.BaseUrl.Contains("11434") ? "https://docs.ollama.com/api/openai-compatibility" : connection.BaseUrl.Contains("openrouter") ? "https://openrouter.ai/docs/quickstart" : "https://lmstudio.ai/docs/developer"
            });
            if (l.ButtonText("Continue to model selection"))
            {
                try { ProviderClient.ValidateEndpoint(connection.BaseUrl); step = 2; notice = ""; scroll = Vector2.zero; }
                catch (Exception e) { notice = ProviderClient.SafeError(e); }
            }
        }
        else if (step == 2)
        {
            l.Label("Choose a fast text model available to your account or local server. Smaller models usually suit short bubbles; the connection test shows actual response time.");
            connection.Model = l.TextEntryLabeled("Exact model name", connection.Model);
            if (connection.Provider is ProviderKind.OpenAI or ProviderKind.Compatible)
            {
                if (modelTask == null && l.ButtonText("Choose from server's model list"))
                { modelStamp = draft.Stamp; notice = "Loading models…"; modelTask = client.ListModels(connection.Copy(), draft.Key, cancellation.Token); }
            }
            if (connection.Provider == ProviderKind.OpenAI || connection.Provider == ProviderKind.Compatible)
            {
                l.CheckboxLabeled("Send a reasoning-level setting (only for supported models)", ref connection.SendReasoning);
                if (connection.SendReasoning && l.ButtonText("Reasoning: " + connection.Reasoning))
                {
                    var options = new List<FloatMenuOption>();
                    foreach (var value in new[] { "low", "medium", "high", "none", "provider default" }) { var v = value; options.Add(new FloatMenuOption(v, () => connection.Reasoning = v)); }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            else l.Label("This connection currently uses the provider's default reasoning behaviour. Model-specific reasoning controls are not yet available here.");
            l.TextFieldNumericLabeled("Request timeout (seconds)", ref connection.TimeoutSeconds, ref timeoutBuffer, 3, 180);
            l.TextFieldNumericLabeled("Output allowance (tokens)", ref connection.OutputTokens, ref tokenBuffer, 256, 16000);
            l.Label("Use a longer timeout for slower local models. Increase the output allowance if a reasoning model returns an incomplete answer; the bubble character limit stays separate.");
            if (test == null && l.ButtonText("Test connection (makes one small AI request)"))
            {
                timer.Restart(); notice = "Testing…";
                testSample = ""; testedConnection = draft.Stamp;
                var preview = Prompts.Preview(SYMMod.Instance.Settings.Preferences);
                test = TestPreview(connection.Copy(), draft.Key, preview);
            }
            if (draft.Tested && testSample.Length > 0)
            {
                l.GapLine(); l.Label("Preview (fictional pawn, not your colony history):"); l.Label(testSample);
                if (l.ButtonText("Continue to finish")) { step = 3; scroll = Vector2.zero; }
            }
        }
        else
        {
            var s = SYMMod.Instance.Settings.Preferences;
            l.Label("Ready. Current style: " + s.Style + "; length: " + s.Length + "; activity: " + s.Activity + "/4. All pawn groups are eligible, including species-appropriate animal expressions.");
            l.Label("Requests already running finish during pause, but replies wait until you resume. Generated text never changes relationships or jobs. History is complete by default; you can change retention in settings.");
            l.Label("Use the Voices tab for transcripts, memories, and colony or pawn instructions. Advanced settings are available at any time.");
            if (l.ButtonText(memory ? "Save memory connection" : "Save and enable SpeakYourMind"))
            {
                try
                {
                    if (!draft.Tested) throw new InvalidOperationException();
                    Credentials.Save(connection, draft.Key); draft.Apply(target);
                    if (!memory) s.Enabled = true;
                    SYMMod.Instance.WriteSettings(); SYMGame.Current?.RefreshConnections(); Close();
                }
                catch { notice = "Could not save this connection. Check that the test succeeded and the game can write its configuration folder."; }
            }
        }
        l.GapLine(); l.Label(notice);
        contentHeight = l.CurHeight + 20; l.End(); Widgets.EndScrollView();
        if (step > 0 && test == null && Widgets.ButtonText(new Rect(0, rect.height - 35, 100, 30), "Back")) { step--; scroll = Vector2.zero; }
        if (Widgets.ButtonText(new Rect(rect.width - 160, rect.height - 35, 150, 30), "Cancel")) Close();
    }
    private async Task<string> TestPreview(Connection config, string key, GenerationContext context)
    {
        if (memory)
        {
            string response = await client.Generate(config, key, Prompts.ProfileSystem, SYMJson.Write(new { pawn = context.Pawns[0], memories = Array.Empty<Memory>(), maxCharacters = context.MaxCharacters, style = context.Style.ToString(), language = context.Language }), cancellation.Token).ConfigureAwait(false);
            var profile = SYMJson.Read<ProfileResponse>(response);
            if (string.IsNullOrWhiteSpace(profile.Voice) || profile.Voice.Length > 3000 || profile.Fallbacks == null) throw new FormatException("The model returned an invalid memory response.");
            return TextPolicy.Clean(profile.Voice);
        }
        string json = await client.Generate(config, key, Prompts.ExpressionSystem, SYMJson.Write(context), cancellation.Token).ConfigureAwait(false);
        return Prompts.ParseExpression(json, context).Turns[0].Text;
    }
    public override void PostClose()
    {
        cancellation.Cancel();
        if (test != null) _ = test.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        if (modelTask != null) _ = modelTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        client.Dispose(); cancellation.Dispose(); base.PostClose();
    }
}

public static class Diagnostics
{
    public static string Summary()
    {
        var c = SYMMod.Instance.Settings.Connection;
        var g = SYMGame.Current;
        // Deliberately exclude keys, endpoint addresses, prompts, annotations, names and transcript text.
        return "SpeakYourMind 0.3.3\nRuntime: " + Environment.Version + "\nBubble integration: " + Integration.Status + "\nOptional adapters: " + OptionalDialogue.Status +
            "\nProvider: " + c.Provider + "\nGeneration enabled: " + SYMMod.Instance.Settings.Preferences.Enabled +
            "\nRequests: " + (g?.RequestsStarted ?? 0) + "\nDisplayed: " + (g?.ExpressionsShown ?? 0) + "\nFallbacks: " + (g?.FallbacksShown ?? 0) +
            "\nDiscarded: " + (g?.Discarded ?? 0) + "\nQueued: " + (g?.Queued ?? 0) + "\nActive bubbles: " + (g?.ActiveBubbles ?? 0) +
            "\nExpression status: " + (g?.Status ?? "No colony loaded") + "\nMemory status: " + (g?.MemoryDiagnostic ?? "No colony loaded") + "\nDebug: " + (g?.DebugStatus ?? "No colony loaded");
    }
}
