using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// DESIGN.md D2 items 2 and 4: WHAT the recap panel shows for a given action. The decision lives in
// the adapter precisely so it can be tested here; the panel in Shapes.Godot owns only the hold and
// the fade, which are timings and belong to a windowed playtest rather than to a test suite.
public class ActionRecapTests
{
    private static CardDatabase Cards => TestCards.Database;

    [Fact]
    public void A_played_card_recaps_with_the_card_face()
    {
        var state = new StateBuilder().P1(p => p.Hand(TestCards.Bolt).Resources(wheel: 1)).Build();
        var action = new PlayCardAction(PlayerId.One, TestCards.Bolt, targetSlot: null);

        var recap = ActionRecap.For(action, state, Cards);

        Assert.NotNull(recap);
        Assert.Equal(ActionRecapKind.Card, recap!.Kind);
        Assert.NotNull(recap.Card);
        Assert.Equal(TestCards.Bolt, recap.Card!.CardId);
    }

    // Item 4, and the reason item 2's panel is worth building at all: a move firing leaves no trace
    // on the board except a health number changing somewhere, so the recap has to name both the
    // move and the creature that used it.
    [Fact]
    public void A_used_move_recaps_with_the_move_name_and_its_creature()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, health: 2))
            .Build();
        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), moveIndex: 0);

        var recap = ActionRecap.For(action, state, Cards);

        Assert.NotNull(recap);
        Assert.Equal("Strike", recap!.Title);
        Assert.Equal("test_striker", recap.Subtitle);

        // The compact strip, not a card face -- a move has no card of its own, and rendering the
        // whole creature to say "it used one of these" buries the answer and costs the height the
        // played-card case needs.
        Assert.Equal(ActionRecapKind.Move, recap.Kind);

        // The creature's CardText still travels, so the strip can pull its art.
        Assert.NotNull(recap.Card);
    }

    // Both seats raise recaps -- the decision recorded in ActionRecap's header. An AI-vs-AI
    // spectator is the case that makes a self/opponent split wrong: neither seat is "yours", so a
    // split rule would show nothing at all in the one mode built entirely for watching.
    [Fact]
    public void Both_seats_raise_a_recap()
    {
        var state = new StateBuilder()
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, health: 2))
            .Build();
        var action = new UseMoveAction(PlayerId.Two, new SlotIndex(PlayerId.Two, 0), moveIndex: 0);

        var recap = ActionRecap.For(action, state, Cards);

        Assert.NotNull(recap);
        Assert.Equal(PlayerId.Two, recap!.Player);
    }

    // EndTurn is already unmistakable on the board (the rail button changes, the turn number ticks)
    // so it does not evict a card the player may still be reading.
    [Fact]
    public void Ending_a_turn_raises_no_recap()
    {
        var state = new StateBuilder().Build();

        Assert.Null(ActionRecap.For(new EndTurnAction(PlayerId.One), state, Cards));
    }
}
