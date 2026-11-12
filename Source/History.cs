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
    public HistoryWindow(string? pawnId = null, string mode = "Conversations") { view = new HistoryView { PawnId = pawnId ?? "", Mode = mode }; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
    public override Vector2 InitialSize => new(Math.Min(950, UI.screenWidth - 40), Math.Min(720, UI.screenHeight - 60));
    public override void DoWindowContents(Rect rect) => view.Draw(rect);
}
public sealed class HistoryView
{
    public string PawnId = "";
    public string Mode = "Conversations";
    private string mode { get => Mode; set => Mode = value; }
    private string filter = "", pawnFilter = "", category = "", cacheKey = "";
    private int page;
    private Vector2 scroll;
    private readonly ConversationQuery query = new();
    private List<string> memoryLines = new(), rows = new();
    private List<ConversationGroup> rowGroups = new();
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
            // Include current characters before their first AI request, without creating profiles for the whole map.
            var currentPawns = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => !p.Dead).ToList();
            var currentIds = currentPawns.Select(p => p.ThingID).ToHashSet();
            foreach (var pawn in currentPawns.Where(p => pawnFilter.Length == 0 || p.LabelShort.Contains(pawnFilter, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.LabelShort))
            { var chosen = pawn; options.Add(new FloatMenuOption(pawn.LabelShort + " (" + pawn.kindDef.label + ")", () => { PawnId = game.ProfileFor(chosen).PawnId; page = 0; scroll = Vector2.zero; })); }
            foreach (var p in state.Profiles.Where(p => !currentIds.Contains(p.PawnId) && (pawnFilter.Length == 0 || p.Name.Contains(pawnFilter, StringComparison.OrdinalIgnoreCase))).OrderBy(p => p.Name)) { string id = p.PawnId; options.Add(new FloatMenuOption(p.Name + " (history)", () => { PawnId = id; page = 0; scroll = Vector2.zero; })); }
            // Historical visitors can outlive their lightweight profile.
            var known = state.Profiles.Select(p => p.PawnId).ToHashSet();
            known.UnionWith(currentIds);
            foreach (var t in state.Transcript.Where(t => !known.Contains(t.SpeakerId) && (pawnFilter.Length == 0 || t.SpeakerName.Contains(pawnFilter, StringComparison.OrdinalIgnoreCase))).DistinctBy(t => t.SpeakerId))
            { string id = t.SpeakerId; options.Add(new FloatMenuOption(t.SpeakerName + " (history)", () => { PawnId = id; page = 0; scroll = Vector2.zero; })); }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        if (Widgets.ButtonText(new Rect(2 * column + 20, 36, column, 30), "Use selected pawn") && Find.Selector.SingleSelectedThing is Pawn selected)
        { profile = game.ProfileFor(selected); PawnId = profile.PawnId; page = 0; scroll = Vector2.zero; }
        Widgets.Label(new Rect(column + 10, 38, 85, 25), "Find pawn:");
        pawnFilter = Widgets.TextField(new Rect(column + 95, 36, Math.Max(30, column - 85), 30), pawnFilter);
        var tabs = new[] { "Conversations", "Memories", "Pawn notes", "Colony rules" };
        float tabWidth = (w - 30) / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            string tab = tabs[i];
            if (Widgets.ButtonText(new Rect(i * (tabWidth + 5), 76, tabWidth, 30), mode == tab ? "[ " + tab + " ]" : tab))
            { mode = tab; page = 0; scroll = Vector2.zero; }
        }
        if (mode is "Pawn notes" or "Colony rules")
        {
            bool colony = mode == "Colony rules";
            Widgets.Label(new Rect(0, 116, w - 25, 75), colony
                ? "Rules for everyone in this colony. A pawn's personal note takes precedence. Example: Append 'nya' to every spoken word."
                : "Personal note for " + (profile?.Name ?? "a pawn — choose one above") + ". This is a major expression rule, preserved through automatic personality updates. Example: Speak tersely, avoid jokes, and end every spoken word with 'nya'.");
            if (!colony && profile != null && state.ColonyRule.Length > 0)
                Widgets.Label(new Rect(0, 193, w - 25, 42), "Colony rules also apply; this personal note takes precedence if they conflict.");
            var box = new Rect(0, 242, w - 25, Math.Max(80, rect.height - 302));
            if (colony)
            { string rule = Widgets.TextArea(box, state.ColonyRule); if (rule != state.ColonyRule) { state.ColonyRule = rule; state.Touch(); } }
            else if (profile != null)
            { string rule = Widgets.TextArea(box, profile.Annotation); if (rule != profile.Annotation) { profile.Annotation = rule; state.Touch(); } }
            else Widgets.Label(box, "Choose a character from the Pawn menu or click Use selected pawn. Current colonists, prisoners, slaves, guests, animals and visitors are available.");
            Widgets.Label(new Rect(0, rect.height - 40, w - 25, 30), "Changes apply immediately to future expressions and are stored when you save the colony.");
            return;
        }
        Widgets.Label(new Rect(0, 118, 65, 25), "Search:");
        string updated = Widgets.TextField(new Rect(70, 116, w - 280, 30), filter);
        if (updated != filter) { filter = updated; page = 0; scroll = Vector2.zero; }
        if (mode == "Conversations" && Widgets.ButtonText(new Rect(w - 195, 116, 170, 30), category.Length == 0 ? "All expression types" : category))
        {
            var options = new List<FloatMenuOption> { new("All expression types", () => { category = ""; page = 0; }) };
            options.AddRange(Enum.GetNames<Trigger>().Select(t => new FloatMenuOption(t, () => { category = t; page = 0; })));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        bool changed = mode == "Conversations" && query.Update(state, PawnId, filter, category, SYMGame.Day, PawnId.Length == 0 ? prefs.ColonyHistoryDays : prefs.PawnHistoryDays);
        if (mode == "Memories" && profile != null && Widgets.ButtonText(new Rect(w - 195, 116, 170, 30), "Retry missing scores"))
        { MemoryWork.Retry(profile); state.Touch(); }
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
        string key = mode + ":" + page + ":" + (mode == "Memories" ? mk : PawnId + ":" + query.Revision) + ":" + contentWidth;
        if (cacheKey != key || changed)
        {
            cacheKey = key;
            rowGroups = mode == "Conversations" ? query.Page(page).ToList() : new();
            rows = mode == "Memories" ? memoryLines.Skip(page * 60).Take(60).ToList() : rowGroups.Select(g =>
                "Day " + g.Lines[0].Day.ToString("F2") + " · " + string.Join(" / ", g.Lines.Select(t => t.SpeakerName).Distinct()) + " · " + g.Lines.Count + " line(s)\n" +
                string.Join("\n", g.Lines.Select(t => t.SpeakerName + " [" + t.Trigger + "; " + SourceLabel(t) + "]: " + t.Text))).ToList();
            if (rows.Count == 0) rows.Add("No matching expressions yet. Enable generation and observe some pawns, or adjust the filters.");
            heights = rows.Select(t => Text.CalcHeight(t, contentWidth) + (mode == "Conversations" ? 54 : 18)).ToArray();
        }
        var viewport = new Rect(0, 193, w - 20, rect.height - 210);
        Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, contentWidth, Math.Max(viewport.height, heights.Sum())));
        float y = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (y + heights[i] >= scroll.y && y <= scroll.y + viewport.height)
            {
                Widgets.Label(new Rect(0, y, contentWidth, heights[i]), rows[i]);
                if (mode == "Conversations" && i < rowGroups.Count && Widgets.ButtonText(new Rect(0, y + heights[i] - 38, 210, 28), "Inspect / tune a line"))
                {
                    var options = rowGroups[i].Lines.Select(t => new FloatMenuOption(t.SpeakerName + ": " + (t.Text.Length > 60 ? t.Text[..60] + "…" : t.Text), () => Find.WindowStack.Add(new PromptLabWindow(game, t)))).ToList();
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            y += heights[i];
        }
        Widgets.EndScrollView();
    }
}
