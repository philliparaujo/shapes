using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class EffectInterpreterTests
{
    [Fact]
    public void Apply_throws_for_an_unregistered_op_name()
    {
        var state = new StateBuilder().P1(p => p.Slot(0, "a", TypeMask.Wheel)).Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Throws<UnknownEffectOpException>(
            () => EffectInterpreter.Apply(Eff.Node("teleport"), ctx));
    }

    [Fact]
    public void Multi_effect_moves_apply_in_declared_order()
    {
        // heal_to_full first restores 3, then heal +1 would be a no-op if order were reversed
        // and heal ran before heal_to_full -- so ending at max health only happens if the
        // declared order (damage-free here) is respected.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel, maxHealth: 5, health: 1))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.ApplyAll(
            [
                Eff.Node("self_damage", ("amount", 0)),
                Eff.Node("heal_to_full", ("target", "self")),
                Eff.Node("self_damage", ("amount", 2)),
            ],
            ctx);

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
    }

    [Fact]
    public void An_effect_that_kills_a_creature_mid_sequence_does_not_corrupt_later_effects()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel, maxHealth: 5).Hand("x").Deck("y"))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.ApplyAll(
            [
                Eff.Node("self_damage", ("amount", 100)), // kills the source creature
                Eff.Node("draw", ("amount", 1)),          // must still run
            ],
            ctx);

        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        Assert.True(creature.IsDead);
        Assert.Equal(2, state[PlayerId.One].Hand.Count); // the draw still happened
    }
}
