using Shapes.Console;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Console;

// Step 4 of Phase 4: a merged creature should show every card folded into it, not just the
// primary one -- previously DescribeSlot looked up only CreatureInstance.CardId, so a merge
// silently dropped the absorbed card's name from the board render.
public class BoardViewMergeTests
{
    private static readonly CardDatabase Cards = TestCards.Database;

    [Fact]
    public void Unmerged_creature_shows_only_its_own_name()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var text = BoardView.DescribeSlot(state, Cards, new SlotIndex(PlayerId.One, 0));

        Assert.Contains(Cards.Get(TestCards.Striker).Name, text, StringComparison.Ordinal);
        Assert.DoesNotContain("+", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Merged_creature_shows_both_source_card_names()
    {
        var creature = new CreatureInstance(TestCards.Striker, maxHealth: 2, TypeMask.Wheel);
        var second = new CreatureInstance(TestCards.TwoMove, maxHealth: 3, TypeMask.Spike);
        creature.AbsorbMerge(second, Cards.MoveCountOf);

        var state = new StateBuilder()
            .P1(p => p.Slot(0, creature))
            .Build();

        var text = BoardView.DescribeSlot(state, Cards, new SlotIndex(PlayerId.One, 0));

        Assert.Contains(Cards.Get(TestCards.Striker).Name, text, StringComparison.Ordinal);
        Assert.Contains(Cards.Get(TestCards.TwoMove).Name, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Creature_types_render_as_icons_not_words()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var text = BoardView.DescribeSlot(state, Cards, new SlotIndex(PlayerId.One, 0));

        Assert.Contains("◯", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Wheel", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_slot_shows_a_placeholder_not_a_crash()
    {
        var state = new StateBuilder().Build();

        var text = BoardView.DescribeSlot(state, Cards, new SlotIndex(PlayerId.One, 0));

        Assert.Contains("--", text, StringComparison.Ordinal);
    }
}
