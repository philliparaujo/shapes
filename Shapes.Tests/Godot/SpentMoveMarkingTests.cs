using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// DESIGN.md D2 item 3. The board marks a move that has already been used this turn, reading the flag
// straight off CreatureInstance.HasUsedMove -- the same flag ActionGenerator consults for legality,
// so the marking cannot disagree with the rule it depicts.
//
// WHAT THESE PIN, and why it is worth pinning. The decision to highlight BOTH seats rests entirely
// on when the flags clear: ResetMovesForNewTurn runs at the OWNER's turn END, not at the next
// turn's start, so at most one seat can have flags set at any moment. That is what makes "highlight
// both" unambiguous rather than a two-colour mess -- and it is a non-obvious timing detail sitting
// under an unrelated-looking method name, exactly the kind of thing a later refactor breaks
// silently. Nothing in the UI could fail loudly if it changed; the board would just start marking
// the wrong creature's moves.
public class SpentMoveMarkingTests
{
    [Fact]
    public void A_used_move_is_flagged_on_the_creature_that_used_it()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Wheel, health: 3))
            .Build();
        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;

        Assert.False(creature.HasUsedMove(0));

        creature.MarkMoveUsed(0);

        Assert.True(creature.HasUsedMove(0));
    }

    // Different moves on one creature are independent -- the case that matters most on a merged
    // creature fielding up to four, which is where "what have I already spent" is hardest to hold
    // in your head and where this marking earns the most.
    [Fact]
    public void Moves_on_one_creature_are_flagged_independently()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Wheel, health: 3))
            .Build();
        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;

        creature.MarkMoveUsed(1);

        Assert.False(creature.HasUsedMove(0));
        Assert.True(creature.HasUsedMove(1));
    }

    // WHY THE TRACKER EXISTS. The engine clears its flag as the owner's own turn ENDS, because it
    // only needs the flag to gate actions. Correct for legality, useless for display: it means that
    // by the time the opponent is acting, nothing records what was just spent -- which is exactly
    // when someone watching wants to see it. This pins the engine behaviour the view has to work
    // around, so a change to it shows up here rather than as a silently-empty marking.
    [Fact]
    public void The_engine_flag_clears_at_the_owners_turn_end()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Wheel, health: 3))
            .Build();

        var mine = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        mine.MarkMoveUsed(0);
        Assert.True(mine.HasUsedMove(0));

        state.EndTurn();

        Assert.False(mine.HasUsedMove(0));
    }

    // THE LOAD-BEARING ONE, and the behaviour the engine cannot provide: a move stays marked
    // through the opponent's whole turn, and only stops being marked when its own seat comes back
    // round. "Until your next turn", not "until you end this one".
    [Fact]
    public void A_used_move_stays_marked_through_the_opponents_turn()
    {
        var slot = new SlotIndex(PlayerId.One, 0);
        var tracker = new SpentMoveTracker();

        var before = StateOnTurnOf(PlayerId.One);
        var afterMove = StateOnTurnOf(PlayerId.One);
        tracker.Observe(new UseMoveAction(PlayerId.One, slot, moveIndex: 0), before, afterMove);

        Assert.True(tracker.WasUsed(slot, 0));

        // P1 hands over to P2. The engine would have cleared its flag here; the marking must not.
        var afterHandover = StateOnTurnOf(PlayerId.Two);
        tracker.Observe(new EndTurnAction(PlayerId.One), afterMove, afterHandover);

        Assert.True(tracker.WasUsed(slot, 0));

        // P2 acts during their own turn -- still P1's marking, still shown.
        var afterTheirMove = StateOnTurnOf(PlayerId.Two);
        tracker.Observe(
            new UseMoveAction(PlayerId.Two, new SlotIndex(PlayerId.Two, 0), moveIndex: 0),
            afterHandover, afterTheirMove);

        Assert.True(tracker.WasUsed(slot, 0));

        // P2 hands back: P1's turn begins, and only now does P1's marking clear.
        var backToOne = StateOnTurnOf(PlayerId.One);
        tracker.Observe(new EndTurnAction(PlayerId.Two), afterTheirMove, backToOne);

        Assert.False(tracker.WasUsed(slot, 0));
    }

    // One seat's turn start clears only its OWN markings -- the opponent's stay up, since their
    // next turn has not come round yet.
    [Fact]
    public void A_turn_start_clears_only_that_seats_markings()
    {
        var mine = new SlotIndex(PlayerId.One, 0);
        var theirs = new SlotIndex(PlayerId.Two, 0);
        var tracker = new SpentMoveTracker();

        var onOne = StateOnTurnOf(PlayerId.One);
        tracker.Observe(new UseMoveAction(PlayerId.One, mine, moveIndex: 0), onOne, onOne);

        var onTwo = StateOnTurnOf(PlayerId.Two);
        tracker.Observe(new EndTurnAction(PlayerId.One), onOne, onTwo);
        tracker.Observe(new UseMoveAction(PlayerId.Two, theirs, moveIndex: 0), onTwo, onTwo);

        Assert.True(tracker.WasUsed(mine, 0));
        Assert.True(tracker.WasUsed(theirs, 0));

        // Back to P1: their marking clears, P2's survives until P2's own next turn.
        var backToOne = StateOnTurnOf(PlayerId.One);
        tracker.Observe(new EndTurnAction(PlayerId.Two), onTwo, backToOne);

        Assert.False(tracker.WasUsed(mine, 0));
        Assert.True(tracker.WasUsed(theirs, 0));
    }

    // A move used as the very FIRST action of a turn must not be wiped by that same turn's own
    // start -- the tracker processes the handover before recording the use for exactly this case.
    [Fact]
    public void A_move_used_immediately_after_a_handover_survives()
    {
        var slot = new SlotIndex(PlayerId.Two, 0);
        var tracker = new SpentMoveTracker();

        var onOne = StateOnTurnOf(PlayerId.One);
        var onTwo = StateOnTurnOf(PlayerId.Two);
        tracker.Observe(new EndTurnAction(PlayerId.One), onOne, onTwo);
        tracker.Observe(new UseMoveAction(PlayerId.Two, slot, moveIndex: 0), onTwo, onTwo);

        Assert.True(tracker.WasUsed(slot, 0));
    }

    // A creature that dies, merges away, or is replaced takes its markings with it -- otherwise the
    // next creature to occupy that slot would inherit them.
    [Fact]
    public void Markings_are_dropped_when_the_slot_empties()
    {
        var slot = new SlotIndex(PlayerId.One, 0);
        var tracker = new SpentMoveTracker();

        var occupied = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Wheel, health: 3))
            .Build();
        tracker.Observe(new UseMoveAction(PlayerId.One, slot, moveIndex: 0), occupied, occupied);
        Assert.True(tracker.WasUsed(slot, 0));

        tracker.ForgetEmptySlots(new StateBuilder().Build());

        Assert.False(tracker.WasUsed(slot, 0));
    }

    private static Shapes.Core.State.GameState StateOnTurnOf(PlayerId player)
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Wheel, health: 3))
            .P2(p => p.Slot(0, TestCards.TwoMove, TypeMask.Wheel, health: 3))
            .Build();
        state.SetActivePlayer(player);
        return state;
    }
}
