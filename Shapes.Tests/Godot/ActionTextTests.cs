using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// Godot's ActionText duplicates Shapes.Console's ActionText (structurally unreachable from
// Shapes.Godot -- see that class's header); these tests pin the same behavior.
public class ActionTextTests
{
    private static CardDatabase Cards => TestCards.Database;

    [Fact]
    public void PlayCardAction_resolves_the_real_name_and_cost_and_effects()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Bolt).Resources(wheel: 1))
            .Build();
        var action = new PlayCardAction(PlayerId.One, TestCards.Bolt, targetSlot: null);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Equal("Play test_bolt [◯1] (draw 1)", text);
    }

    [Fact]
    public void UseMoveAction_resolves_the_move_name_cost_and_effects()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, health: 2))
            .Build();
        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), moveIndex: 0);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Equal("Strike [◯1] from P1:0 (deal 1 damage to opposing)", text);
    }

    [Fact]
    public void UseMoveAction_with_a_chosen_target_reports_it()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Chooser, TypeMask.Anvil, health: 2))
            .P2(p => p.Slot(1, TestCards.Striker, TypeMask.Wheel, health: 2))
            .Build();
        var target = new SlotIndex(PlayerId.Two, 1);
        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), moveIndex: 0, target);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains("targeting P2:1", text);
    }

    [Fact]
    public void DiscardAction_resolves_the_real_card_name()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Bolt))
            .Build();
        var action = new DiscardAction(PlayerId.One, TestCards.Bolt);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Contains("test_bolt", text);
    }

    [Fact]
    public void EndTurnAction_falls_back_to_Describe()
    {
        var state = new StateBuilder().Build();
        var action = new EndTurnAction(PlayerId.One);

        var text = ActionText.Describe(action, state, Cards);

        Assert.Equal(action.Describe(), text);
    }
}
