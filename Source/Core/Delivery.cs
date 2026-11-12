using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeakYourMind.Core;

public enum DeliveryAction { Waiting, Show, Finished }

// Owns the exchange lifecycle independently of rendering, networking, or simulation ticks.
// The caller supplies a clock that advances ONLY while the game is unpaused.
public sealed class ExchangeDelivery
{
    private List<Turn>? turns;
    private int index = -1;
    private double shownAt, renderedAt = -1;
    private bool recorded, ended;
    public bool HasResult => turns != null;
    public bool IsFallback { get; private set; }
    public bool CurrentWasDisplayed => recorded;
    public bool HasDisplayedAny { get; private set; }
    public Turn? Current => index >= 0 && turns != null ? turns[index] : null;
    public bool Accept(IEnumerable<Turn> result, bool fallback = false)
    {
        if (HasResult || ended) return false; // A late provider completion cannot replace a fallback.
        var copy = result.Select(t => new Turn { PawnId = t.PawnId, Text = t.Text }).ToList();
        if (copy.Count == 0) return false;
        turns = copy; IsFallback = fallback; return true;
    }
    public DeliveryAction Advance(double now, bool paused, bool contextValid, bool releaseAllowed)
    {
        if (ended) return DeliveryAction.Finished;
        if (paused) return DeliveryAction.Waiting;
        if (!contextValid) { ended = true; return DeliveryAction.Finished; }
        if (turns == null) return DeliveryAction.Waiting;
        if (Current != null)
        {
            if (!recorded)
            {
                if (now - shownAt >= 8) { ended = true; return DeliveryAction.Finished; }
                return DeliveryAction.Waiting;
            }
            if (now - renderedAt < Math.Clamp(TextPolicy.VisibleLength(Current.Text) / 18.0, 3, 12)) return DeliveryAction.Waiting;
            if (index + 1 >= turns.Count) { ended = true; return DeliveryAction.Finished; }
        }
        if (!releaseAllowed) return DeliveryAction.Waiting;
        index++; shownAt = now; recorded = false; renderedAt = -1;
        return DeliveryAction.Show;
    }
    // Called directly from a successful Repaint, before the next update can invalidate context.
    public Turn? MarkRendered(double now)
    {
        if (ended || recorded || Current == null) return null;
        recorded = true; HasDisplayedAny = true; renderedAt = now; return Current;
    }
    public void Cancel() => ended = true;
}

public sealed class FailureBackoff
{
    public int Failures { get; private set; }
    public double Until { get; private set; }
    public bool Ready(double now) => now >= Until;
    public double Remaining(double now) => Math.Max(0, Until - now);
    public void Fail(double now, double retryAfter = 0)
    {
        Failures = Math.Min(6, Failures + 1);
        Until = Math.Max(Until, now + Math.Max(Math.Min(120, Math.Pow(2, Failures) * 2), Math.Clamp(retryAfter, 0, 600)));
    }
    public void Success() { Failures = 0; Until = 0; }
}

public static class Scheduling
{
    public static bool Overlaps(IEnumerable<string> first, IEnumerable<string> second) => first.Intersect(second).Any();
    public static int Priority(Trigger trigger, bool selected) => (trigger switch { Trigger.Combat => 300, Trigger.Health or Trigger.Colony => 200, Trigger.Social => 100, Trigger.Needs => 80, _ => 0 }) + (selected ? 40 : 0);
}
