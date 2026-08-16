using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// Remembers which moves have been used since a seat's last turn (PLAN.md D2 item 3), so the board
// can keep them marked through the opponent's turn instead of only during the user's own.
//
// WHY THIS HAS TO EXIST AT ALL. The obvious source is CreatureInstance.HasUsedMove, which the first
// cut read directly -- and it is the right source for LEGALITY, since it is what ActionGenerator
// consults. But it is cleared by ResetMovesForNewTurn at the OWNER'S TURN END (despite the name;
// see that method's own note), because the engine only needs the flag to survive as long as it
// gates actions. So by the time the opponent is acting, the flag is already gone and the marking
// vanished exactly when a spectator most wants it -- "what did they just spend" is a question you
// ask while watching someone else's turn.
//
// Fixing that in the engine would mean changing when a rules-bearing flag clears, which the
// milestone forbids (`Shapes.Core` stays unmodified) and which would be wrong anyway: the engine's
// timing is correct for the job the engine has. This is a VIEW concern -- how long a cue stays on
// screen -- so the memory lives here, in the adapter, where it is testable without a scene.
//
// LIFETIME: a seat's record clears at the start of that seat's OWN next turn, not at the end of
// their turn. That is the "until your next turn" the display wants, and it is deliberately a
// different boundary from the engine's.
public sealed class SpentMoveTracker
{
    // Keyed by slot and move index rather than by creature identity: CreatureInstance is a mutable
    // reference type that a merge folds away entirely, so holding one would either leak or go
    // stale. A slot is a stable address for as long as the marking is meant to last.
    private readonly HashSet<(SlotIndex Slot, int MoveIndex)> _spent = [];

    // Which seat each remembered use belonged to, so one seat's turn start clears only its own.
    private readonly Dictionary<(SlotIndex Slot, int MoveIndex), PlayerId> _owners = [];

    // The seat that was active when the last action was observed, used to detect a handover.
    private PlayerId? _lastActive;

    public bool WasUsed(SlotIndex slot, int moveIndex) => _spent.Contains((slot, moveIndex));

    // Called after every action lands, with the state it produced.
    //
    // Order matters: the handover is processed FIRST, so a move used as the very first action of a
    // turn is not immediately wiped by that same turn's own start. `before` supplies the seat that
    // actually acted, which is not `after.ActivePlayer` once an EndTurn has flipped it.
    public void Observe(GameAction action, GameState before, GameState after)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        _lastActive ??= before.ActivePlayer;

        if (after.ActivePlayer != _lastActive)
        {
            // A seat is beginning its turn: everything it spent last time round stops being shown.
            ClearFor(after.ActivePlayer);
            _lastActive = after.ActivePlayer;
        }

        if (action is UseMoveAction useMove)
        {
            var key = (useMove.SourceSlot, useMove.MoveIndex);
            _spent.Add(key);
            _owners[key] = useMove.Player;
        }
    }

    // A slot that is now empty has nothing to mark. Called on every render rather than tracked
    // through destruction events: a creature can leave a slot by dying, by merging away, or by
    // being replaced, and a board read is the one check that covers all three without this class
    // having to model any of them.
    public void ForgetEmptySlots(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var stale = _spent.Where(key => state.Board[key.Slot] is null).ToList();
        foreach (var key in stale)
        {
            _spent.Remove(key);
            _owners.Remove(key);
        }
    }

    public void Clear()
    {
        _spent.Clear();
        _owners.Clear();
        _lastActive = null;
    }

    private void ClearFor(PlayerId player)
    {
        var theirs = _spent.Where(key => _owners.GetValueOrDefault(key, key.Slot.Owner) == player).ToList();
        foreach (var key in theirs)
        {
            _spent.Remove(key);
            _owners.Remove(key);
        }
    }
}
