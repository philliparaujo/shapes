using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// A4 (card rendering via EffectText): CardText.Of must actually synthesize text from the op
// vocabulary, not read a hand-authored string -- these pin real strings so a silent fallback
// to e.g. the raw op name would be caught.
public class CardTextTests
{
    private static CardDatabase Cards => TestCards.Database;

    [Fact]
    public void Creature_card_reports_health_and_types_and_no_spell_effects()
    {
        var text = CardText.Of(Cards.Get(TestCards.Striker));

        Assert.True(text.IsCreature);
        Assert.Equal(2, text.Health);
        Assert.Equal("◯", text.TypeIcons);
        Assert.Equal(string.Empty, text.SpellEffects);
    }

    [Fact]
    public void Creature_moves_are_described_via_EffectText()
    {
        var text = CardText.Of(Cards.Get(TestCards.Striker));

        var move = Assert.Single(text.Moves);
        Assert.Equal("Strike", move.Name);
        Assert.Equal("◯1", move.Cost);
        Assert.Equal("Deal 1.", move.Effects);
    }

    [Fact]
    public void Gated_move_text_includes_the_condition()
    {
        var text = CardText.Of(Cards.Get(TestCards.Gated));

        var move = Assert.Single(text.Moves);
        Assert.Equal("Draw 1 if this is at full health.", move.Effects);
    }

    [Fact]
    public void Spell_card_reports_no_health_and_synthesized_effects()
    {
        var text = CardText.Of(Cards.Get(TestCards.TargetedBolt));

        Assert.False(text.IsCreature);
        Assert.Equal(0, text.Health);
        Assert.Empty(text.Moves);
        Assert.Equal("Deal 2 to an enemy.", text.SpellEffects);
    }

    [Fact]
    public void Free_move_cost_reads_free_not_a_blank_pool()
    {
        var text = CardText.Of(Cards.Get(TestCards.FreeMove));

        var move = Assert.Single(text.Moves);
        Assert.Equal("free", move.Cost);
    }

    [Fact]
    public void A_discounted_move_reports_a_zero_badge_flagged_as_discounted()
    {
        // Striker's move costs 1 wheel; a free-moves spell has zeroed wheel moves this turn.
        var move = Cards.Get(TestCards.Striker).Moves[0];

        var text = MoveText.Of(move, ResourcePool.Empty);

        Assert.Equal(0, text.CostAmount);
        Assert.True(text.IsDiscounted);

        // The SHAPE still says which resource the move is paid in -- only the amount owed
        // changed, not what it is owed in.
        Assert.Equal(ResourceType.Wheel, text.PrimaryType);
    }

    [Fact]
    public void An_undiscounted_move_is_not_flagged()
    {
        var move = Cards.Get(TestCards.Striker).Moves[0];

        var text = MoveText.Of(move, move.Cost);

        Assert.Equal(1, text.CostAmount);
        Assert.False(text.IsDiscounted);
    }

    [Fact]
    public void A_printed_free_move_is_not_flagged_as_discounted()
    {
        // The distinction the flag exists for: a move that always costs nothing must NOT tint,
        // or "free right now" and "free always" would look identical on the badge.
        var move = Cards.Get(TestCards.FreeMove).Moves[0];

        var text = MoveText.Of(move, move.Cost);

        Assert.False(text.IsDiscounted);
    }
}
