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
    public override Vector2 RequestedTabSize => new(Math.Min(950, UI.screenWidth - 40), Math.Min(700, UI.screenHeight - 110));
    public override void DoWindowContents(Rect rect) => view.Draw(rect);
}
public sealed class HistoryWindow : Window
{
    private readonly HistoryView view;
    public HistoryWindow(string? pawnId = null) { view = new HistoryView { PawnId = pawnId ?? "" }; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
    public override Vector2 InitialSize => new(Math.Min(950, UI.screenWidth - 40), Math.Min(720, UI.screenHeight - 60));
    public override void DoWindowContents(Rect rect) => view.Draw(rect);
}
public sealed class HistoryView
{
    public string PawnId = "";
    private string filter = "", mode = "Conversations", pawnFilter = "", category = "", cacheKey = "";
    private int page;
    private Vector2 scroll;
    private readonly ConversationQuery query = new();
    private List<string> memoryLines = new(), rows = new();
    private float[] heights = Array.Empty<float>();
    private string memoryKey = "";
    private SaveState? cachedState;
    private static string SourceLabel(TranscriptEntry t) => t.Source == "preset" ? "generic fallback" :
        t.Source.StartsWith("fallback:") ? FallbackPolicy.Label(t.Source[9..]) : "AI";
    public void Draw(Rect rect)
    {
        var game = SYMGame.Current;
        if (game == null) { Widgets.Label(rect, "Load a colony to view SpeakYourMind history."); return; }
        if (!game.StateHealthy) { Widgets.Label(rect, "SpeakYourMind could not read this colony's data. The original data is preserved. Check the game log before continuing with this save."); return; }
        var state = game.State; var prefs = SYMMod.Instance.Settings.Preferences;
        var profile = state.Profiles.Find(p => p.PawnId == PawnId);
        float w = rect.width, column = (rect.width - 30) / 3;
        Widgets.Label(new Rect(0, 0, w - 40, 30), "SpeakYourMind — " + (PawnId.Length == 0 ? "Colony feed" : profile?.Name ?? "Pawn history"));
        if (Widgets.ButtonText(new Rect(0, 36, column, 30), "Pawn: " + (PawnId.Length == 0 ? "Everyone" : profile?.Name ?? PawnId)))
        {
            var options = new List<FloatMenuOption> { new("Everyone (colony feed)", () => { PawnId = ""; page = 0; scroll = Vector2.zero; }) };
            foreach (var p in state.Profiles.Where(p => pawnFilter.Length == 0 || p.Name.Contains(pawnFilter, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name)) { string id = p.PawnId; options.Add(new FloatMenuOption(p.Name, () => { PawnId = id; page = 0; scroll = Vector2.zero; })); }
            // Historical visitors can outlive their lightweight profile.
            var known = state.Profiles.Select(p => p.PawnId).ToHashSet();
            foreach (var t in state.Transcript.Where(t => !known.Contains(t.SpeakerId) && (pawnFilter.Length == 0 || t.SpeakerName.Contains(pawnFilter, StringComparison.OrdinalIgnoreCase))).DistinctBy(t => t.SpeakerId))
            { string id = t.SpeakerId; options.Add(new FloatMenuOption(t.SpeakerName + " (history)", () => { PawnId = id; page = 0; scroll = Vector2.zero; })); }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        if (Widgets.ButtonText(new Rect(2 * column + 20, 36, column, 30), "Use selected pawn") && Find.Selector.SingleSelectedThing is Pawn selected)
        { profile = game.ProfileFor(selected); PawnId = profile.PawnId; page = 0; scroll = Vector2.zero; }
        Widgets.Label(new Rect(column + 10, 38, 85, 25), "Find pawn:");
        pawnFilter = Widgets.TextField(new Rect(column + 95, 36, Math.Max(30, column - 85), 30), pawnFilter);
        var tabs = new[] { "Conversations", "Memories", "Instructions" };
        for (int i = 0; i < tabs.Length; i++)
        {
            string tab = tabs[i];
            if (Widgets.ButtonText(new Rect(i * (column + 10), 76, column, 30), mode == tab ? "[ " + tab + " ]" : tab))
            { mode = tab; page = 0; scroll = Vector2.zero; }
        }
        if (mode == "Instructions")
        {
            Widgets.Label(new Rect(0, 116, w, 65), PawnId.Length == 0
                ? "Colony-wide creative instructions. These guide every expression; an individual pawn's instruction takes precedence. Example: Append 'nya' to every spoken word."
                : "Individual creative instructions. Personality and history stay automatically managed. These instructions persist when the profile updates.");
            var box = new Rect(0, 186, w - 25, Math.Max(80, rect.height - 246));
            if (PawnId.Length == 0)
            { string rule = Widgets.TextArea(box, state.ColonyRule); if (rule != state.ColonyRule) { state.ColonyRule = rule; state.Touch(); } }
            else if (profile != null)
            { string rule = Widgets.TextArea(box, profile.Annotation); if (rule != profile.Annotation) { profile.Annotation = rule; state.Touch(); } }
            else Widgets.Label(new Rect(0, 186, w, 50), "This pawn is no longer present. Select a current pawn to add an instruction.");
            Widgets.Label(new Rect(0, rect.height - 40, w - 25, 30), "Instructions are stored with this colony when you save the game.");
            return;
        }
        Widgets.Label(new Rect(0, 118, 65, 25), "Search:");
        string updated = Widgets.TextField(new Rect(70, 116, w - 280, 30), filter);
        if (updated != filter) { filter = updated; page = 0; scroll = Vector2.zero; }
        if (Widgets.ButtonText(new Rect(w - 195, 116, 170, 30), category.Length == 0 ? "All expression types" : category))
        {
            var options = new List<FloatMenuOption> { new("All expression types", () => { category = ""; page = 0; }) };
            options.AddRange(Enum.GetNames<Trigger>().Select(t => new FloatMenuOption(t, () => { category = t; page = 0; })));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        bool changed = query.Update(state, PawnId, filter, category, SYMGame.Day, PawnId.Length == 0 ? prefs.ColonyHistoryDays : prefs.PawnHistoryDays);
        string mk = PawnId + ":" + filter + ":" + state.Revision + ":" + prefs.MemoryScaleDays + ":" + prefs.MemoryExponent;
        if (mode == "Memories")
        {
            if (memoryKey != mk || cachedState != state)
            {
            memoryKey = mk; cachedState = state;
            if (profile == null) memoryLines = new() { "Select a pawn to inspect its automatically managed personality and memories." };
            else memoryLines = new[] { "Personality: " + (profile.Voice.Length == 0 ? "Awaiting background generation." : profile.Voice) }
                .Concat(profile.Memories.OrderByDescending(m => m.Importance).ThenByDescending(m => m.Day).Select(m =>
                    "Day " + m.Day.ToString("F1") + " · " + (m.Scored ? m.Importance.ToString("F2") : "awaiting score") + " · " + m.Source + " · " + m.Perspective + "\n" + m.Fact + "\n" + m.Reason +
                    (m.Importance >= 1 ? " · permanent" : m.Scored ? " · retained for " + MemoryPolicy.Lifetime(m.Importance, prefs).ToString("F1") + " days" : "")))
                .Where(t => filter.Length == 0 || t.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }
        int count = mode == "Memories" ? memoryLines.Count : query.Matches.Count;
        int pages = Math.Max(1, (count + 59) / 60);
        page = Math.Clamp(page, 0, pages - 1);
        if (Widgets.ButtonText(new Rect(0, 155, 100, 28), "Previous")) { page = Math.Max(0, page - 1); scroll = Vector2.zero; }
        Widgets.Label(new Rect(110, 155, 300, 28), "Page " + (page + 1) + "/" + pages + " · " + count + (mode == "Memories" ? " entries" : " conversations"));
        if (Widgets.ButtonText(new Rect(w - 125, 155, 100, 28), "Next")) { page = Math.Min(pages - 1, page + 1); scroll = Vector2.zero; }
        float contentWidth = w - 46;
        string key = mode + ":" + page + ":" + mk + ":" + query.Rebuilds + ":" + contentWidth;
        if (cacheKey != key || changed)
        {
            cacheKey = key;
            rows = mode == "Memories" ? memoryLines.Skip(page * 60).Take(60).ToList() : query.Page(page).Select(g =>
                "Day " + g.Lines[0].Day.ToString("F2") + " · " + string.Join(" / ", g.Lines.Select(t => t.SpeakerName).Distinct()) + " · " + g.Lines.Count + " line(s)\n" +
                string.Join("\n", g.Lines.Select(t => t.SpeakerName + " [" + t.Trigger + "; " + SourceLabel(t) + "]: " + t.Text))).ToList();
            if (rows.Count == 0) rows.Add("No matching expressions yet. Enable generation and observe some pawns, or adjust the filters.");
            heights = rows.Select(t => Text.CalcHeight(t, contentWidth) + 18).ToArray();
        }
        var viewport = new Rect(0, 193, w - 20, rect.height - 210);
        Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, contentWidth, Math.Max(viewport.height, heights.Sum())));
        float y = 0;
        for (int i = 0; i < rows.Count; i++) { if (y + heights[i] >= scroll.y && y <= scroll.y + viewport.height) Widgets.Label(new Rect(0, y, contentWidth, heights[i]), rows[i]); y += heights[i]; }
        Widgets.EndScrollView();
    }
}
