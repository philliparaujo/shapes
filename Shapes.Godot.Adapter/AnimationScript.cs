using Shapes.Core.Primitives;

namespace Shapes.Godot.Adapter;

// What one action should look like (PLAN.md B1d). The five cues the step names, and nothing
// else -- a cue is "something happened here worth drawing attention to," not a tween.
public enum AnimationCue
{
    Play,     // a creature appeared in a slot that was empty
    Move,     // the same creature left one slot and arrived in another
    Merge,    // a creature's MergeDepth grew -- it absorbed another
    Damage,   // health went down
    Heal,     // health went up (or MaxHealth grew, which reads the same on the board)
    Destroy,  // a creature left a slot and did not arrive anywhere
    Score,    // a player's score went up
    Scoring,  // this particular creature is one of the ones that earned that score
}

// One thing to draw, at one place. Slot is null for a cue that isn't about a board position
// (Score, which belongs to a player's status readout rather than a slot).
public sealed record AnimationStep(AnimationCue Cue, SlotIndex? Slot, PlayerId Player, int Amount = 0);

// Turns a StateDiff into an ordered list of cues (PLAN.md B1d).
//
// StateDiff is an unordered SET of slot/player changes -- it says what differs, not what
// happened or in what order. One action routinely produces several: a merge is a destroy plus a
// stat change, a play can damage a target, a killing blow is damage plus a destroy. Something
// has to impose an order, and the diff cannot; that derivation is this class.
//
// Deliberately in the Adapter, not in Shapes.Godot: it is pure list-to-list translation with no
// node/tween/scene concept in it, which makes the ordering rules unit-testable without an editor
// -- the same reason StateDiff itself lives here (PLAN.md A2) and the same limitation that makes
// everything in Shapes.Godot editor-only.
public static class AnimationScript
{
    // Ordering rule: causes before effects, and departures before arrivals.
    //
    // 1. Move/Play  -- the thing the player did, and where the eye should go first.
    // 2. Merge      -- reads as a consequence of the play/move that caused it.
    // 3. Damage     -- effects of the action landing.
    // 4. Heal
    // 5. Destroy    -- last among slot cues: a creature must be seen taking lethal damage
    //                  before it disappears, or the damage number animates on an empty slot.
    // 6. Score      -- the outcome, after everything on the board has resolved.
    private static int Rank(AnimationCue cue) => cue switch
    {
        AnimationCue.Move => 0,
        AnimationCue.Play => 0,
        AnimationCue.Merge => 1,
        AnimationCue.Damage => 2,
        AnimationCue.Heal => 3,
        AnimationCue.Destroy => 4,

        // The creatures light up FIRST, then the total lands on the avatar -- the eye should see
        // where the points came from before it sees the number arrive.
        AnimationCue.Scoring => 5,
        AnimationCue.Score => 6,
        _ => 7,
    };

    public static IReadOnlyList<AnimationStep> From(StateDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var steps = new List<AnimationStep>();

        // A creature that vanished from one slot and appeared in another is a MOVE, not a
        // destroy plus a play -- the diff has no notion of identity across slots, so it reports
        // those as two independent slot changes and this is where they are rejoined. Matching on
        // CardId + Health: a move preserves both (ActionExecutor relocates the instance), so a
        // same-card arrival at the same health is the moved creature rather than a coincidence.
        var departures = diff.SlotChanges
            .Where(c => c.Before is not null && c.After is null)
            .ToList();
        var arrivals = diff.SlotChanges
            .Where(c => c.Before is null && c.After is not null)
            .ToList();

        var matchedDepartures = new HashSet<SlotIndex>();
        var matchedArrivals = new HashSet<SlotIndex>();

        foreach (var departure in departures)
        {
            var match = arrivals.FirstOrDefault(a =>
                !matchedArrivals.Contains(a.Slot)
                && a.After!.CardId == departure.Before!.CardId
                && a.After.Health == departure.Before.Health);

            if (match is null)
            {
                continue;
            }

            matchedDepartures.Add(departure.Slot);
            matchedArrivals.Add(match.Slot);
            steps.Add(new AnimationStep(AnimationCue.Move, match.Slot, match.Slot.Owner));
        }

        foreach (var change in diff.SlotChanges)
        {
            var before = change.Before;
            var after = change.After;

            // Arrived: a play, unless it was already claimed as the far end of a move.
            if (before is null && after is not null)
            {
                if (!matchedArrivals.Contains(change.Slot))
                {
                    steps.Add(new AnimationStep(AnimationCue.Play, change.Slot, change.Slot.Owner));
                }

                continue;
            }

            // Left: a destroy, unless it was the near end of a move.
            if (before is not null && after is null)
            {
                if (!matchedDepartures.Contains(change.Slot))
                {
                    steps.Add(new AnimationStep(AnimationCue.Destroy, change.Slot, change.Slot.Owner));
                }

                continue;
            }

            if (before is null || after is null)
            {
                continue;
            }

            // Still there, but different. A merge absorbs another creature into this slot, which
            // shows up as MergeDepth growing -- checked before health so a merge that also
            // changes health reads as "merged" first, which is what actually happened.
            if (after.MergeDepth > before.MergeDepth)
            {
                steps.Add(new AnimationStep(AnimationCue.Merge, change.Slot, change.Slot.Owner));
            }

            if (after.Health < before.Health)
            {
                steps.Add(new AnimationStep(
                    AnimationCue.Damage, change.Slot, change.Slot.Owner, before.Health - after.Health));
            }
            else if (after.Health > before.Health)
            {
                steps.Add(new AnimationStep(
                    AnimationCue.Heal, change.Slot, change.Slot.Owner, after.Health - before.Health));
            }
        }

        // One cue per creature that earned a point, before the totals below -- see Rank.
        foreach (var slot in diff.ScoringSlots)
        {
            steps.Add(new AnimationStep(AnimationCue.Scoring, slot, slot.Owner));
        }

        foreach (var player in diff.PlayerChanges)
        {
            if (player.ScoreAfter > player.ScoreBefore)
            {
                steps.Add(new AnimationStep(
                    AnimationCue.Score, null, player.Player, player.ScoreAfter - player.ScoreBefore));
            }
        }

        // OrderBy is a stable sort, so cues of equal rank keep the order they were discovered in
        // (board order) rather than an arbitrary one -- two creatures damaged by one spell
        // animate left-to-right consistently instead of differently each time.
        return [.. steps.OrderBy(s => Rank(s.Cue))];
    }
}
