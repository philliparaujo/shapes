using Shapes.Core.Actions;

namespace Shapes.Godot.Adapter;

// Turns one action into the sounds it should make (PLAN.md D4).
//
// The audio counterpart of AnimationScript, and deliberately a separate translation rather than a
// filter over that one -- see SoundCue's header for why the two vocabularies differ at both ends.
// Two rules shape this file:
//
//   AT MOST ONE OF EACH CUE PER ACTION. AnimationScript emits per slot, because three damaged
//   creatures genuinely want three floating numbers. Audio does not work that way: three impact
//   sounds triggered on the same frame do not read as "three creatures were hit", they read as
//   one distorted impact, because identical samples starting together sum into constructive
//   interference rather than into a discernible sequence. So each cue here is a yes/no test, not
//   a count -- "did anything merge this action", not "how many merged".
//
//   THE ACTION DECIDES WHAT THE PLAYER DID; THE DIFF DECIDES WHAT FOLLOWED. Play/move/merge are
//   read off the action, because they are what was chosen. Resource gain and scoring are read off
//   the diff, because they are consequences that arrive at turn start without an action of their
//   own. Mixing those up is how a diff-only version would both miss a targetless spell (no slot
//   changed) and mistake a damaging move for a damaging spell.
//
// Pure list-to-list translation with no Godot type in it, so the mapping is testable outside the
// editor -- same reason AnimationScript and MusicPlaylist live here.
public static class SoundScript
{
    // Ordered so the sound of the CAUSE precedes the sound of its EFFECT, matching
    // AnimationScript.Rank's own reasoning: what the player did, then what it did to the board.
    // Only matters when one action produces several cues (a play that also scores), and the list
    // is short enough that an explicit order is cheaper than sorting.
    //
    // `action` is the GameAction that produced `diff`. Null is accepted and means "state advanced
    // without a chosen action", which is how a caller can ask for only the consequence cues.
    public static IReadOnlyList<SoundCue> From(StateDiff diff, GameAction? action)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var cues = new List<SoundCue>();

        // The three "what the player did" cues, read off the action per this class's second rule.
        // Note CardPlay fires for EVERY card played, including a targetless spell that changes
        // nothing on the board -- which a diff-derived version would have missed entirely.
        if (action is PlayCardAction)
        {
            cues.Add(SoundCue.CardPlay);
        }

        if (action is UseMoveAction)
        {
            cues.Add(SoundCue.UseMove);
        }

        if (action is MergeAction)
        {
            cues.Add(SoundCue.Merge);
        }

        // THE TURN-START COLLISION, and why scoring WINS rather than both playing.
        //
        // Income and scoring both resolve in the same AdvanceToActions step, so at every turn start
        // where a point was scored the two cues fire on the identical frame. Two samples starting
        // together do not read as two events -- the louder, longer one simply masks the other, and
        // which one you hear is an accident of their waveforms rather than a decision. Reported
        // from real play as "I only hear the resource sound".
        //
        // Offsetting them was the alternative and was rejected: a delay only moves the collision,
        // because the player can act again before the delay elapses, so the deferred cue would then
        // land on top of the NEXT action's sounds -- trading a predictable overlap for an
        // unpredictable one. Suppression is also the honest reading of what a turn start means:
        // income arrives every single turn and is the least informative sound in the set, while a
        // score is the only event that moves the win condition. When exactly one can be heard, it
        // should be the one that changed the game.
        //
        // Income still sounds on every turn that does NOT score, which is most of them -- this
        // suppresses the cue only in the frame where something more important is competing with it.
        var scored = Scored(diff);

        if (GainedResources(diff) && !scored)
        {
            cues.Add(SoundCue.GainResource);
        }

        // Score last: it resolves at turn start, after everything the previous turn did.
        if (scored)
        {
            cues.Add(SoundCue.HeroDamage);
        }

        return cues;
    }

    // Any resource pool grew. This is turn-start income in practice; a card effect that grants
    // resources sounds the same, which is correct -- both are "you gained resources."
    private static bool GainedResources(StateDiff diff)
    {
        foreach (var player in diff.PlayerChanges)
        {
            if (player.ResourcesAfter.Total > player.ResourcesBefore.Total)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Scored(StateDiff diff)
    {
        foreach (var player in diff.PlayerChanges)
        {
            if (player.ScoreAfter > player.ScoreBefore)
            {
                return true;
            }
        }

        return false;
    }
}
