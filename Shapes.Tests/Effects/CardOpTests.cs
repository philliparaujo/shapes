using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class CardOpTests
{
    private static (GameState State, EffectContext Ctx) Setup(
        string[]? hand = null, string[]? deck = null)
    {
        var state = new StateBuilder()
            .P1(p =>
            {
                p.Slot(0, "caster", TypeMask.Wheel);
                if (hand is not null) p.Hand(hand);
                if (deck is not null) p.Deck(deck);
            })
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        return (state, ctx);
    }

    [Fact]
    public void Draw_moves_cards_from_deck_to_hand()
    {
        var (state, ctx) = Setup(deck: ["a", "b", "c"]);

        EffectInterpreter.Apply(Eff.Node("draw", ("amount", 2)), ctx);

        Assert.Equal(2, state[PlayerId.One].Hand.Count);
        Assert.Single(state[PlayerId.One].Deck);
    }

    [Fact]
    public void Draw_on_an_empty_deck_does_not_throw_and_draws_nothing_more()
    {
        var (state, ctx) = Setup(deck: []);

        EffectInterpreter.Apply(Eff.Node("draw", ("amount", 3)), ctx);

        Assert.Empty(state[PlayerId.One].Hand);
    }

    [Fact]
    public void Discard_from_an_empty_hand_is_a_no_op()
    {
        var (state, ctx) = Setup(hand: []);

        EffectInterpreter.Apply(Eff.Node("discard", ("amount", 2)), ctx);

        Assert.Empty(state[PlayerId.One].Discard);
    }

    [Fact]
    public void Discard_moves_cards_from_hand_to_discard()
    {
        var (state, ctx) = Setup(hand: ["a", "b", "c"]);

        EffectInterpreter.Apply(Eff.Node("discard", ("amount", 2)), ctx);

        Assert.Single(state[PlayerId.One].Hand);
        Assert.Equal(2, state[PlayerId.One].Discard.Count);
    }

    [Fact]
    public void Draw_up_to_fills_the_hand_to_the_target()
    {
        var (state, ctx) = Setup(hand: ["a"], deck: ["b", "c", "d"]);

        EffectInterpreter.Apply(Eff.Node("draw_up_to", ("amount", 3)), ctx);

        Assert.Equal(3, state[PlayerId.One].Hand.Count);
    }

    [Fact]
    public void Draw_up_to_is_a_no_op_when_already_at_or_above_target()
    {
        var (state, ctx) = Setup(hand: ["a", "b", "c"], deck: ["d"]);

        EffectInterpreter.Apply(Eff.Node("draw_up_to", ("amount", 2)), ctx);

        Assert.Equal(3, state[PlayerId.One].Hand.Count);
        Assert.Single(state[PlayerId.One].Deck); // untouched, not negative draw
    }
}
