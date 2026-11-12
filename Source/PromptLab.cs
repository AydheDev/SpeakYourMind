using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeakYourMind.Core;
using UnityEngine;
using Verse;

namespace SpeakYourMind;

public sealed class PromptLabWindow : Window
{
    private readonly SYMGame game;
    private readonly TranscriptEntry line;
    private readonly PromptSample? sample;
    private readonly CancellationTokenSource cancellation = new();
    private Task<string>? test;
    private string directions = "", baseline = "", variant = "", notice = "";
    private string testedDirections = "", testedModel = "";
    private bool variantTest, showContext;
    private Vector2 scroll;
    private float height = 900;
    public PromptLabWindow(SYMGame game, TranscriptEntry line)
    {
        this.game = game; this.line = line; sample = game.Samples.Find(line.RequestId);
        doCloseX = true; forcePause = true; absorbInputAroundWindow = true;
    }
    public override Vector2 InitialSize => new(Math.Min(900, UI.screenWidth - 40), Math.Min(760, UI.screenHeight - 60));
    public override void DoWindowContents(Rect rect)
    {
        if (test?.IsCompleted == true)
        {
            try { string text = test.GetAwaiter().GetResult(); if (variantTest) variant = text; else baseline = text; game.RecordPreview(sample!, testedDirections, testedModel, text); notice = "Preview only — colony history and memories were not changed. Results were added to the debug log."; }
            catch (Exception e) { notice = ProviderClient.SafeError(e); }
            test = null;
        }
        Widgets.BeginScrollView(rect, ref scroll, new Rect(0, 0, rect.width - 24, Math.Max(height, rect.height)));
        var l = new Listing_Standard(); l.Begin(new Rect(0, 0, rect.width - 30, Math.Max(height, rect.height)));
        l.Label("Prompt lab — " + line.SpeakerName);
        l.Label("Displayed: " + line.Text);
        l.Label("Trigger: " + line.Trigger + " · source: " + line.Source + " · day " + line.Day.ToString("F2"));
        if (sample == null)
        {
            l.Label("This line has no captured input in this session. Enable debug capture before generating expressions, then inspect a new line. The latest 24 request snapshots are kept while this colony is open; full logs remain in the debug folder.");
            if (l.ButtonText("Enable debug capture for future expressions")) { SYMMod.Instance.Settings.Preferences.DebugLogging = true; SYMMod.Instance.WriteSettings(); game.SyncDebug(); }
        }
        else
        {
            l.Label("Why this request: " + sample.Snapshot().Event);
            l.Label("Original model: " + sample.Model + ". Comparisons use the current dialogue model and the exact captured context. They make extra AI requests and may incur normal usage charges, including while paused.");
            foreach (var tag in new[] { "Repetitive", "Wrong personality", "Invented fact", "Good example" })
                if (l.ButtonText((sample.Feedback == tag ? "[marked] " : "Mark: ") + tag)) game.Feedback(sample, tag);
            l.CheckboxLabeled("Show captured context", ref showContext);
            if (showContext) l.Label(sample.Context);
            l.Label("Experimental directions (this preview only):");
            directions = Widgets.TextArea(l.GetRect(110), directions);
            if (test == null && l.ButtonText("Generate baseline — one AI request")) { variantTest = false; testedDirections = ""; testedModel = SYMMod.Instance.Settings.Connection.Model; test = game.PreviewSample(sample, testedDirections, cancellation.Token); notice = "Generating baseline…"; }
            if (test == null && directions.Trim().Length > 0 && l.ButtonText("Generate variant — one AI request")) { variantTest = true; testedDirections = directions; testedModel = SYMMod.Instance.Settings.Connection.Model; test = game.PreviewSample(sample, testedDirections, cancellation.Token); notice = "Generating variant…"; }
            if (baseline.Length > 0) l.Label("Baseline:\n" + baseline);
            if (variant.Length > 0) l.Label("Variant:\n" + variant);
        }
        if (notice.Length > 0) l.Label(notice);
        height = l.CurHeight + 20; l.End(); Widgets.EndScrollView();
    }
    public override void PostClose()
    {
        cancellation.Cancel(); if (test != null) _ = test.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        cancellation.Dispose(); base.PostClose();
    }
}

public sealed class NeedSettingsWindow : Window
{
    private string filter = "";
    private Vector2 scroll;
    private float height = 700;
    public NeedSettingsWindow() { doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
    public override Vector2 InitialSize => new(700, Math.Min(700, UI.screenHeight - 60));
    public override void DoWindowContents(Rect rect)
    {
        Widgets.BeginScrollView(rect, ref scroll, new Rect(0, 0, rect.width - 24, Math.Max(height, rect.height)));
        var l = new Listing_Standard(); l.Begin(new Rect(0, 0, rect.width - 30, Math.Max(height, rect.height)));
        l.Label("Need direction — tells SpeakYourMind which end means discomfort. Standard needs usually use low; some mod needs use high. This changes expression triggers only.");
        filter = l.TextEntryLabeled("Find a need", filter);
        var prefs = SYMMod.Instance.Settings.Preferences;
        foreach (var need in DefDatabase<RimWorld.NeedDef>.AllDefsListForReading.Where(n => (n.defName + n.label).Contains(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n.label))
            if (l.ButtonText(need.label + " (" + need.defName + "): " + (prefs.HighBadNeeds.Contains(need.defName) ? "high means discomfort" : "low means discomfort")))
            { if (!prefs.HighBadNeeds.Remove(need.defName)) prefs.HighBadNeeds.Add(need.defName); }
        height = l.CurHeight + 20; l.End(); Widgets.EndScrollView();
    }
    public override void PostClose() { SYMMod.Instance.WriteSettings(); base.PostClose(); }
}
