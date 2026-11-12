using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SpeakYourMind.Core;
using RimWorld;
using Verse;
using HarmonyLib;

namespace SpeakYourMind;

public static class Context
{
    public static bool Tracked(Pawn p, Preferences s) => p.IsColonist || p.IsPrisonerOfColony || p.IsSlaveOfColony || (p.Faction?.IsPlayer ?? false) || (s.TrackGuests && p.HostFaction == Faction.OfPlayer);
    public static bool Visible(Pawn p) => p.Spawned && !p.Dead && p.Map == Find.CurrentMap && !p.Position.Fogged(p.Map) && Find.CameraDriver.CurrentViewRect.Contains(p.Position);
    public static bool CanExpress(Pawn p) => Visible(p) && p.Awake() && p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) > 0.1f;
    public static bool ColonyAware(Pawn p) => p.IsColonist || p.IsPrisonerOfColony || p.IsSlaveOfColony || p.HostFaction == Faction.OfPlayer;
    public static PawnSnapshot Capture(Pawn p)
    {
        var result = new PawnSnapshot
        {
            Id = p.ThingID, Name = p.LabelShort, Kind = p.kindDef.label,
            Animal = p.RaceProps.Animal, Mechanoid = p.RaceProps.IsMechanoid,
            Faction = p.Faction?.Name ?? "Unaffiliated", Technology = p.Faction?.def.techLevel.ToString() ?? "Unknown",
            Age = p.ageTracker?.AgeBiologicalYearsFloat ?? 0, Job = p.CurJob?.def.reportString ?? "Idle",
            Pain = p.health.hediffSet.PainTotal,
            Background = string.Join("; ", new[] { Background(p.story?.Childhood), Background(p.story?.Adulthood) }.Where(x => x.Length > 0)),
            Beliefs = p.Ideo == null ? "" : p.Ideo.name + ": " + string.Join(", ", p.Ideo.memes.Select(m => m.label)),
            Awake = p.Awake(), Communication = Communication(p), CanSpeak = CanSpeak(p),
        };
        if (p.story != null) result.Traits = p.story.traits.allTraits.Select(t => t.LabelCap.ToString()).ToList();
        if (p.skills != null) result.Skills = p.skills.skills.Select(s => s.def.label + ": " + s.Level).ToList();
        foreach (var name in new[] { "MoveSpeed", "SocialImpact", "GlobalLearningFactor", "MeleeHitChance", "ShootingAccuracyPawn" })
        {
            var stat = DefDatabase<StatDef>.GetNamedSilentFail(name);
            if (stat == null) continue;
            try { result.Stats[stat.label] = p.GetStatValue(stat); } catch { /* A third-party stat worker must not block all context. */ }
        }
        if (p.needs != null) foreach (var need in p.needs.AllNeeds) result.Needs[need.def.defName + " (" + need.LabelCap + ")"] = need.CurLevelPercentage;
        if (p.apparel != null) result.Gear.AddRange(p.apparel.WornApparel.Select(a => a.LabelCap + " (condition " + a.HitPoints + "/" + a.MaxHitPoints + ")"));
        if (p.equipment != null) result.Gear.AddRange(p.equipment.AllEquipmentListForReading.Select(e => e.LabelCap.ToString()));
        result.Health = p.health.hediffSet.hediffs.Select(h => h.LabelCap + (h.Part == null ? "" : " on " + h.Part.Label)).Take(24).ToList();
        if (p.relations != null)
        {
            var related = p.relations.RelatedPawns.Where(other => other != null).Take(20);
            foreach (var other in related)
            {
                string fact = other.LabelShort + " [" + other.ThingID + "]: " + string.Join(", ", p.GetRelations(other).Select(r => r.label)) + (other.Dead ? " (dead)" : "");
                result.RelationshipFacts.Add(fact); result.Relationships.Add(fact + "; opinion " + p.relations.OpinionOf(other));
            }
        }
        if (p.needs?.mood?.thoughts?.memories != null) result.Mood = p.needs.mood.thoughts.memories.Memories.Select(m => m.LabelCap.ToString()).Distinct().Take(12).ToList();
        if (p.Spawned)
        {
            result.Environment = "Weather: " + p.Map.weatherManager.curWeather.label + "; temperature " + p.Position.GetTemperature(p.Map).ToString("F0") + " C; " + (p.Position.Roofed(p.Map) ? "roofed" : "unroofed") + "; terrain " + p.Position.GetTerrain(p.Map).label;
            var nearby = GenRadial.RadialDistinctThingsAround(p.Position, p.Map, 5, true)
                .Where(t => t is Building && GenSight.LineOfSight(p.Position, t.Position, p.Map)).Select(t => t.LabelShort).Distinct().Take(8);
            result.Environment += "; nearby visible structures: " + string.Join(", ", nearby);
            result.Facilities.Add("Nearby visible structures only (not a complete inventory): " + string.Join(", ", nearby));
            result.Facilities.Add("Reachability, permissions, power, plumbing and sanitation availability are unverified. Do not infer missing facilities from this list.");
        }
        return result;
    }
    private static string Background(object? backstory)
    {
        if (backstory == null) return "";
        var type = backstory.GetType();
        string title = AccessTools.Field(type, "title")?.GetValue(backstory)?.ToString() ?? "";
        string description = AccessTools.Field(type, "baseDesc")?.GetValue(backstory)?.ToString() ?? "";
        return TextPolicy.Clean(title + ": " + description);
    }
    public static bool CanSpeak(Pawn pawn)
    {
        var talking = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("Talking");
        return pawn.RaceProps.Humanlike && (talking == null || pawn.health.capacities.GetLevel(talking) > 0.1f);
    }
    public static string Communication(Pawn pawn)
    {
        if (pawn.RaceProps.Animal) return "Animal: species-appropriate sounds and body language; no verified human language.";
        if (pawn.RaceProps.IsMechanoid) return "Mechanoid: mechanical signals; no verified human voice.";
        var talking = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("Talking");
        var hearing = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("Hearing");
        return "Speech capacity: " + (talking == null ? "unknown" : pawn.health.capacities.GetLevel(talking).ToString("P0")) +
            "; hearing capacity: " + (hearing == null ? "unknown" : pawn.health.capacities.GetLevel(hearing).ToString("P0")) + ". Capacity is not vocabulary or intelligence.";
    }
    public static string StableFacts(PawnSnapshot s) => SYMJson.Write(new { s.Kind, s.Background, s.Communication, Traits = s.Traits.OrderBy(t => t), s.Beliefs, Relationships = s.RelationshipFacts.OrderBy(t => t), Health = s.Health.OrderBy(t => t) });
    public static string HealthSignature(Pawn p) => string.Join("|", p.health.hediffSet.hediffs.Select(h => h.def.defName + ":" + h.Part?.Label + ":" + (int)(h.Severity * 4)).OrderBy(s => s)) + ";pain:" + (int)(p.health.hediffSet.PainTotal * 5);
    public static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static List<string> Resources(Map map)
    {
        var result = new List<string>();
        result.Add("Partial inventory: visible stored items and selected colony structures. Omission never proves absence or accessibility.");
        result.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(t => !t.Position.Fogged(map) && t.Position.GetSlotGroup(map) != null).GroupBy(t => t.def.label).OrderByDescending(g => g.Sum(t => t.stackCount)).Take(20).Select(g => g.Key + ": " + g.Sum(t => t.stackCount)));
        result.AddRange(map.listerBuildings.allBuildingsColonist.Where(b => !b.Position.Fogged(map)).GroupBy(b => b.def.label).Take(25).Select(g => g.Key + ": " + g.Count()));
        result.Add("Current research: " + (Find.ResearchManager.GetProject()?.label ?? "none"));
        result.Add("Completed research: " + string.Join(", ", DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(r => r.IsFinished).Select(r => r.label).TakeLast(20)));
        return result;
    }
}
