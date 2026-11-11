using System;
using System.Linq;
using System.Collections.Generic;
using SpeakYourMind.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace SpeakYourMind;

public sealed class HistoryTab : MainTabWindow
{
    private readonly HistoryView view = new();
    public override Vector2 RequestedTabSize => new(950, 700);
    public override void DoWindowContents(Rect rect) => view.Draw(rect);
}
public sealed class HistoryWindow : Window
{
    private readonly HistoryView view;
    public HistoryWindow(string? pawnId = null) { view = new HistoryView { PawnId = pawnId ?? "" }; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
    public override Vector2 InitialSize => new(950, 720);
    public override void DoWindowContents(Rect rect) => view.Draw(rect);
}
public sealed class HistoryView
{
    public string PawnId = "";
    private string filter = "", mode = "Transcript";
    private int page;
    private Vector2 scroll;
    public void Draw(Rect rect)
    {
        var game = SYMGame.Current;
        if (game == null) { Widgets.Label(rect, "Load a colony to view SpeakYourMind history."); return; }
        var state = game.State; var prefs = SYMMod.Instance.Settings.Preferences;
        var profile = state.Profiles.Find(p => p.PawnId == PawnId);
        float w = rect.width;
        Widgets.Label(new Rect(0, 0, w - 40, 30), "SpeakYourMind — " + (PawnId.Length == 0 ? "Colony feed" : profile?.Name ?? "Pawn history"));
        if (Widgets.ButtonText(new Rect(0, 36, 210, 30), "Pawn: " + (PawnId.Length == 0 ? "Everyone" : profile?.Name ?? PawnId)))
        {
            var options = new List<FloatMenuOption> { new("Everyone (colony feed)", () => { PawnId = ""; page = 0; }) };
            foreach (var p in state.Profiles.OrderBy(p => p.Name)) { string id = p.PawnId; options.Add(new FloatMenuOption(p.Name, () => { PawnId = id; page = 0; })); }
            // Historical visitors can outlive their lightweight profile.
            foreach (var t in state.Transcript.Where(t => !state.Profiles.Any(p => p.PawnId == t.SpeakerId)).DistinctBy(t => t.SpeakerId))
            { string id = t.SpeakerId; options.Add(new FloatMenuOption(t.SpeakerName + " (history)", () => { PawnId = id; page = 0; })); }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        if (Widgets.ButtonText(new Rect(220, 36, 180, 30), "Use selected pawn") && Find.Selector.SingleSelectedThing is Pawn selected)
        { profile = game.ProfileFor(selected); PawnId = profile.PawnId; page = 0; }
        if (Widgets.ButtonText(new Rect(410, 36, 150, 30), mode)) { mode = mode == "Transcript" ? "Memories" : mode == "Memories" ? "Instructions" : "Transcript"; page = 0; scroll = Vector2.zero; }
        if (mode == "Instructions")
        {
            Widgets.Label(new Rect(0, 80, w, 65), PawnId.Length == 0
                ? "Colony-wide creative instructions. These guide every expression; an individual pawn's instruction takes precedence. Example: Append 'nya' to every spoken word."
                : "Individual creative instructions. Personality and history stay automatically managed. These instructions persist when the profile updates.");
            if (PawnId.Length == 0) state.ColonyRule = Widgets.TextArea(new Rect(0, 150, w - 25, 250), state.ColonyRule);
            else if (profile != null) profile.Annotation = Widgets.TextArea(new Rect(0, 150, w - 25, 250), profile.Annotation);
            else Widgets.Label(new Rect(0, 150, w, 50), "This pawn is no longer present. Select a current pawn to add an instruction.");
            return;
        }
        Widgets.Label(new Rect(0, 78, 120, 25), "Search:");
        string updated = Widgets.TextField(new Rect(120, 76, w - 150, 30), filter);
        if (updated != filter) { filter = updated; page = 0; }
        IEnumerable<string> lines;
        if (mode == "Memories")
        {
            if (profile == null) lines = new[] { "Select a pawn to inspect its automatically managed personality and memories." };
            else lines = new[] { "Personality: " + (profile.Voice.Length == 0 ? "Awaiting background generation." : profile.Voice) }
                .Concat(profile.Memories.OrderByDescending(m => m.Importance).ThenByDescending(m => m.Day).Select(m =>
                    "Day " + m.Day.ToString("F1") + " · " + (m.Scored ? m.Importance.ToString("F2") : "awaiting score") + " · " + m.Source + " · " + m.Perspective + "\n" + m.Fact + "\n" + m.Reason +
                    (m.Importance >= 1 ? " · permanent" : m.Scored ? " · expires after " + MemoryPolicy.Lifetime(m.Importance, prefs).ToString("F1") + " days" : "")));
        }
        else
        {
            lines = state.Transcript.Where(t => (PawnId.Length == 0 || t.Participants.Contains(PawnId)) && HistoryPolicy.Visible(t.Day, SYMGame.Day, PawnId.Length == 0 ? prefs.ColonyHistoryDays : prefs.PawnHistoryDays))
                .Reverse()
                .Select(t => "Day " + t.Day.ToString("F2") + " · " + t.SpeakerName + " · " + t.Trigger + (t.Source == "preset" ? " · fallback" : "") + " · exchange " + t.ExchangeId[..Math.Min(6, t.ExchangeId.Length)] + "\n" + t.Text);
        }
        var all = lines.Where(t => filter.Length == 0 || t.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        page = Math.Clamp(page, 0, Math.Max(0, (all.Count - 1) / 60));
        if (Widgets.ButtonText(new Rect(0, 115, 100, 28), "Previous")) page = Math.Max(0, page - 1);
        Widgets.Label(new Rect(110, 115, 240, 28), "Page " + (page + 1) + " · " + all.Count + " entries");
        if (Widgets.ButtonText(new Rect(350, 115, 100, 28), "Next")) page = Math.Min(Math.Max(0, (all.Count - 1) / 60), page + 1);
        var rows = all.Skip(page * 60).Take(60).ToList();
        float contentWidth = w - 46;
        var heights = rows.Select(t => Text.CalcHeight(t, contentWidth) + 18).ToArray();
        var viewport = new Rect(0, 153, w - 20, rect.height - 170);
        Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, contentWidth, Math.Max(viewport.height, heights.Sum())));
        float y = 0;
        for (int i = 0; i < rows.Count; i++) { Widgets.Label(new Rect(0, y, contentWidth, heights[i]), rows[i]); y += heights[i]; }
        Widgets.EndScrollView();
    }
}
