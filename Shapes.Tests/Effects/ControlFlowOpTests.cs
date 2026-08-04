using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class ControlFlowOpTests
{
    private static (GameState State, EffectContext Ctx) SelfAt(int maxHealth, int health)
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel, maxHealth: maxHealth, health: health)
                      .Hand("h").Deck("d1", "d2", "d3", "d4", "d5"))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        return (state, ctx);
    }

    [Fact]
    public void Conditional_runs_then_branch_when_predicate_holds()
    {
        var (state, ctx) = SelfAt(maxHealth: 3, health: 3);

        EffectInterpreter.Apply(
            Eff.Node("conditional",
                ("condition", Eff.Node("self_at_full_health")),
                ("then", new[] { Eff.Node("draw", ("amount", 1)) })),
            ctx);

        Assert.Equal(2, state[PlayerId.One].Hand.Count);
    }

    [Fact]
    public void Conditional_runs_else_branch_when_predicate_fails()
    {
        var (state, ctx) = SelfAt(maxHealth: 3, health: 1);

        EffectInterpreter.Apply(
            Eff.Node("conditional",
                ("condition", Eff.Node("self_at_full_health")),
                ("then", new[] { Eff.Node("draw", ("amount", 5)) }),
                ("else", new[] { Eff.Node("draw", ("amount", 1)) })),
            ctx);

        Assert.Equal(2, state[PlayerId.One].Hand.Count);
    }

    [Fact]
    public void Conditional_with_no_else_and_a_failed_predicate_is_a_no_op()
    {
        var (state, ctx) = SelfAt(maxHealth: 3, health: 1);

        EffectInterpreter.Apply(
            Eff.Node("conditional",
                ("condition", Eff.Node("self_at_full_health")),
                ("then", new[] { Eff.Node("draw", ("amount", 5)) })),
            ctx);

        Assert.Single(state[PlayerId.One].Hand); // unchanged
    }

    [Fact]
    public void For_each_over_an_empty_collection_is_a_no_op()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("for_each", ("collection", "enemy_creatures"),
                ("effects", new[] { Eff.Node("damage", ("target", "self"), ("amount", 1)) })),
            ctx);

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health); // untouched
    }

    [Fact]
    public void For_each_friendly_creature_counts_match_the_board()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel, maxHealth: 5).Slot(1, "b", TypeMask.Wheel, maxHealth: 5))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("for_each", ("collection", "friendly_creatures"),
                ("effects", new[] { Eff.Node("heal", ("target", "self"), ("amount", 0)) })),
            ctx);

        // Sanity: both creatures reachable via "self" rebinding without throwing.
        EffectInterpreter.Apply(
            Eff.Node("for_each", ("collection", "friendly_creatures"),
                ("effects", new[] { Eff.Node("self_damage", ("amount", 1)) })),
            ctx);

        Assert.Equal(4, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
        Assert.Equal(4, state.Board[new SlotIndex(PlayerId.One, 1)]!.Health);
    }

    [Fact]
    public void For_each_damaged_filter_only_matches_damaged_creatures()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel, maxHealth: 5, health: 5)
                      .Slot(1, "b", TypeMask.Wheel, maxHealth: 5, health: 2))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("for_each", ("collection", "friendly_creatures"), ("filter", "damaged"),
                ("effects", new[] { Eff.Node("heal", ("target", "self"), ("amount", 100)) })),
            ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health); // untouched (was full)
        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 1)]!.Health); // healed to full
    }
}
