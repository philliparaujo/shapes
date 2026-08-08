using Shapes.Core.Cards;
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
        Assert.Equal("deal 1 damage to opposing", move.Effects);
    }

    [Fact]
    public void Gated_move_text_includes_the_condition()
    {
        var text = CardText.Of(Cards.Get(TestCards.Gated));

        var move = Assert.Single(text.Moves);
        Assert.Equal("only if self is at full health: draw 1", move.Effects);
    }

    [Fact]
    public void Spell_card_reports_no_health_and_synthesized_effects()
    {
        var text = CardText.Of(Cards.Get(TestCards.TargetedBolt));

        Assert.False(text.IsCreature);
        Assert.Equal(0, text.Health);
        Assert.Empty(text.Moves);
        Assert.Equal("deal 2 damage to chosen enemy", text.SpellEffects);
    }

    [Fact]
    public void Free_move_cost_reads_free_not_a_blank_pool()
    {
        var text = CardText.Of(Cards.Get(TestCards.FreeMove));

        var move = Assert.Single(text.Moves);
        Assert.Equal("free", move.Cost);
    }
}
