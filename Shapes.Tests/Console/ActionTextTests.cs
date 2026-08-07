using Shapes.Console;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Console;

// Step 4 of Phase 4: the console's action log should name real moves and effects, not the bare
// "Move #N" GameAction.Describe() emits on its own (it has no CardDatabase/GameState access --
// see its doc comment). ActionText.Describe is the call site that has both.
public class ActionTextTests
{
    private static readonly CardDatabase Cards = TestCards.Database;

    [Fact]
    public void UseMove_names_the_real_move_instead_of_a_bare_index()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains("Strike", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Move #", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UseMove_on_a_merged_creature_resolves_the_move_from_the_correct_source_card()
    {
        // TwoMove's second move ("Brace") lands at concatenated index 1 on an unmerged creature;
        // merging Striker in front shifts it to index 2 -- MoveIndexOffset's whole point. This
        // pins that ActionText walks MergedFrom via CardDatabase.MovesOf rather than assuming a
        // single card's move list.
        var creature = new CreatureInstance(TestCards.Striker, maxHealth: 2, TypeMask.Wheel);
        var second = new CreatureInstance(TestCards.TwoMove, maxHealth: 3, TypeMask.Spike);
        creature.AbsorbMerge(second, Cards.MoveCountOf);

        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, creature))
            .Build();

        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 2);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains("Brace", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UseMove_shows_the_moves_cost_as_icons()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains("◯1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayCard_shows_the_cards_cost_as_icons()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Hand(TestCards.Bolt).Resources(wheel: 1))
            .Build();

        var action = new PlayCardAction(PlayerId.One, TestCards.Bolt);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains("◯1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UseMove_includes_effect_text()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains("damage", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayCard_names_the_card_and_its_effects()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Hand(TestCards.Bolt).Resources(wheel: 1))
            .Build();

        var action = new PlayCardAction(PlayerId.One, TestCards.Bolt);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains(Cards.Get(TestCards.Bolt).Name, text, StringComparison.Ordinal);
        Assert.Contains("draw", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Discard_names_the_card()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Hand(TestCards.Bolt))
            .Build();

        var action = new DiscardAction(PlayerId.One, TestCards.Bolt);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains(Cards.Get(TestCards.Bolt).Name, text, StringComparison.Ordinal);
    }

    [Fact]
    public void EndTurn_falls_back_to_the_base_description()
    {
        var state = new StateBuilder().Build();

        var text = ActionText.Describe(new EndTurnAction(PlayerId.One), state, Cards);

        Assert.Equal("End turn", text);
    }
}
