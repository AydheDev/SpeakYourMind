using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
            try { if (prefs.Length > 0) Preferences = SYMJson.Read<Preferences>(prefs); if (connection.Length > 0) Connection = SYMJson.Read<Connection>(connection); if (memory.Length > 0) MemoryConnection = SYMJson.Read<Connection>(memory); }
            catch { Log.Warning("[SpeakYourMind] Could not read settings. Check the setup screen."); }
        }
    }
}

public sealed class SYMMod : Mod
{
    public static SYMMod Instance = null!;
    public SYMSettings Settings;
    private Vector2 scroll;
    public SYMMod(ModContentPack pack) : base(pack)
    {
        Instance = this; Settings = GetSettings<SYMSettings>();
        LongEventHandler.ExecuteWhenFinished(Integration.Initialize);
    }
    public override string SettingsCategory() => "SpeakYourMind";
    public override void DoSettingsWindowContents(Rect rect)
    {
        Widgets.BeginScrollView(rect, ref scroll, new Rect(0, 0, rect.width - 24, 1550));
        var l = new Listing_Standard(); l.Begin(new Rect(0, 0, rect.width - 30, 1550));
        var s = Settings.Preferences;
        l.Label("SpeakYourMind — every pawn has a story.");
        l.Label("Bubble integration: " + Integration.Status);
        if (l.ButtonText("Guided AI setup")) Find.WindowStack.Add(new SetupWindow(Settings.Connection));
        l.CheckboxLabeled("Enable contextual expressions", ref s.Enabled);
        l.Label("Connection: " + Settings.Connection.Provider + " / " + Settings.Connection.Model);
        l.Label("Status: " + (SYMGame.Current?.Status ?? "Load a colony to begin."));
        l.GapLine();
        if (l.ButtonText("Communication style: " + s.Style)) s.Style = (ExpressionStyle)(((int)s.Style + 1) % 3);
        if (l.ButtonText("Length: " + s.Length)) s.Length = (ExpressionLength)(((int)s.Length + 1) % 3);
        s.Activity = (int)Math.Round(l.SliderLabeled("Activity: " + new[] { "Quiet", "Balanced", "Lively", "Chatty" }[Math.Clamp(s.Activity - 1, 0, 3)], s.Activity, 1, 4));
        s.MaxCharacters = (int)l.SliderLabeled("Maximum characters: " + (s.MaxCharacters == 0 ? "length default" : s.MaxCharacters), s.MaxCharacters, 0, 1000);
        s.MaxBubbles = (int)l.SliderLabeled("Simultaneous bubbles: " + s.MaxBubbles, s.MaxBubbles, 1, 8);
        s.MaxTurns = (int)l.SliderLabeled("Maximum exchange turns: " + s.MaxTurns, s.MaxTurns, 2, 4);
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
        l.Label("History retention is in game days. Zero = complete transcript.");
        s.PawnHistoryDays = (int)l.SliderLabeled("Pawn history days: " + s.PawnHistoryDays, s.PawnHistoryDays, 0, 600);
        s.ColonyHistoryDays = (int)l.SliderLabeled("Colony feed days: " + s.ColonyHistoryDays, s.ColonyHistoryDays, 0, 600);
        l.Label("An entry is deleted only after both history limits expire. Shortening limits removes old entries at the next daily cleanup.");
        s.MemoryScaleDays = l.SliderLabeled("Memory retention scale: " + s.MemoryScaleDays.ToString("F0") + " days", s.MemoryScaleDays, 5, 120);
        s.MemoryExponent = l.SliderLabeled("Memory importance curve: " + s.MemoryExponent.ToString("F1"), s.MemoryExponent, 0.5f, 3);
        l.Label("Importance 0.1: " + MemoryPolicy.Lifetime(0.1f, s).ToString("F1") + " days; 0.5: " + MemoryPolicy.Lifetime(0.5f, s).ToString("F0") + " days; 1: forever.");
        l.GapLine();
        l.CheckboxLabeled("Advanced: use a separate model for memory", ref Settings.SeparateMemoryModel);
        if (Settings.SeparateMemoryModel && l.ButtonText("Configure memory model")) Find.WindowStack.Add(new SetupWindow(Settings.MemoryConnection));
        if (l.ButtonText("Open colony history and annotations") && Current.Game != null) Find.WindowStack.Add(new HistoryWindow());
        l.Label("Player instructions are saved per colony. Select a pawn in the history window to add an individual instruction.");
        l.End(); Widgets.EndScrollView();
    }
}

public sealed class SetupWindow : Window
{
    private readonly Connection connection;
    private string key = "", notice = "", testSample = "", testedConnection = "";
    private int step;
    private Task<string>? test;
    private readonly ProviderClient client = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly System.Diagnostics.Stopwatch timer = new();
    public override Vector2 InitialSize => new(760, 670);
    public SetupWindow(Connection c) { connection = c; key = Credentials.Read(c); doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
    private void Preset(string name, ProviderKind provider, string url)
    {
        connection.Provider = provider; connection.BaseUrl = url; connection.Model = "";
        connection.SendReasoning = provider == ProviderKind.OpenAI; connection.Reasoning = "low";
        key = Credentials.Read(connection); notice = ""; step = 1;
    }
    public override void DoWindowContents(Rect rect)
    {
        if (test?.IsCompleted == true)
        {
            try { var result = test.GetAwaiter().GetResult(); if (testedConnection == SYMJson.Write(connection)) { testSample = result; notice = "Connected in " + timer.Elapsed.TotalSeconds.ToString("F1") + " seconds. Sample: " + testSample; } else notice = "Settings changed during the test. Test the new connection again."; }
            catch (Exception e) { notice = ProviderClient.SafeError(e); }
            test = null;
        }
        var l = new Listing_Standard(); l.Begin(rect);
        l.Label("SpeakYourMind setup — " + (step + 1) + "/4"); l.GapLine();
        if (step == 0)
        {
            l.Label("Choose where the AI runs. Cloud services receive selected pawn and colony context and may charge for API usage. Local models run through a server you start on this computer.");
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
            connection.BaseUrl = l.TextEntryLabeled("Server address", connection.BaseUrl);
            l.Label("API key (stored encrypted for this Windows account; never included in saves)");
            key = GUI.PasswordField(l.GetRect(32), key, '*');
            l.Label("For a local model: install Ollama or LM Studio, download a model suitable for your hardware, start its local API server, then copy its exact model name below. A localhost server may not require a key.");
            if (l.ButtonText("Open official setup instructions")) Application.OpenURL(connection.Provider switch
            {
                ProviderKind.OpenAI => "https://developers.openai.com/api/docs/quickstart",
                ProviderKind.Claude => "https://platform.claude.com/docs/en/get-started",
                ProviderKind.Gemini => "https://aistudio.google.com/apikey",
                _ => connection.BaseUrl.Contains("11434") ? "https://docs.ollama.com/api/openai-compatibility" : connection.BaseUrl.Contains("openrouter") ? "https://openrouter.ai/docs/quickstart" : "https://lmstudio.ai/docs/developer"
            });
            if (l.ButtonText("Save connection and continue"))
            {
                try { ProviderClient.ValidateEndpoint(connection.BaseUrl); Credentials.Save(connection, key); step = 2; notice = ""; }
                catch (Exception e) { notice = e is ArgumentException ? e.Message : "Could not save the encrypted credential on this computer."; }
            }
        }
        else if (step == 2)
        {
            l.Label("Choose a fast text model available to your account or local server. Smaller models usually suit short bubbles; the connection test shows actual response time.");
            connection.Model = l.TextEntryLabeled("Exact model name", connection.Model);
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
            if (test == null && l.ButtonText("Test connection (makes one small AI request)"))
            {
                timer.Restart(); notice = "Testing…";
                testSample = ""; testedConnection = SYMJson.Write(connection);
                test = client.Generate(connection.Copy(), Credentials.Read(connection), "Return only JSON: {\"text\":\"a short greeting from a fictional colonist\"}.", "Generate the test greeting.", cancellation.Token);
            }
            if (testSample.Length > 0 && testedConnection == SYMJson.Write(connection) && l.ButtonText("Continue to preferences")) step = 3;
        }
        else
        {
            var s = SYMMod.Instance.Settings.Preferences;
            l.Label("Ready. Current style: " + s.Style + "; length: " + s.Length + "; activity: " + s.Activity + "/4. All pawn groups are eligible, including species-appropriate animal expressions.");
            l.Label("Requests already running finish during pause, but replies wait until you resume. Generated text never changes relationships or jobs. History is complete by default; you can change retention in settings.");
            l.Label("Use the Voices tab for transcripts, memories, and colony or pawn instructions. Advanced settings are available at any time.");
            if (l.ButtonText("Enable SpeakYourMind")) { s.Enabled = true; SYMMod.Instance.WriteSettings(); Close(); }
        }
        l.GapLine(); l.Label(notice);
        if (step > 0 && test == null && l.ButtonText("Back")) step--;
        l.End();
    }
    public override void PostClose() { cancellation.Cancel(); client.Dispose(); cancellation.Dispose(); SYMMod.Instance.WriteSettings(); base.PostClose(); }
}
