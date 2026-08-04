using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class ResourceOpTests
{
    private static (GameState State, EffectContext Ctx) Setup()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        return (state, ctx);
    }

    [Fact]
    public void Gain_resource_lands_immediately()
    {
        var (state, ctx) = Setup();

        EffectInterpreter.Apply(Eff.Node("gain_resource", ("type", "spike"), ("amount", 3)), ctx);

        Assert.Equal(3, state[PlayerId.One].Resources.Spike);
    }

    [Fact]
    public void Gain_next_turn_does_not_land_this_turn()
    {
        var (state, ctx) = Setup();

        EffectInterpreter.Apply(Eff.Node("gain_next_turn", ("type", "anvil"), ("amount", 2)), ctx);

        Assert.Equal(0, state[PlayerId.One].Resources.Anvil);
        Assert.Equal(2, state[PlayerId.One].PendingNextTurnResources.Anvil);
    }

    [Fact]
    public void Gain_next_turn_lands_on_the_following_income_phase_and_then_clears()
    {
        var (state, ctx) = Setup();
        EffectInterpreter.Apply(Eff.Node("gain_next_turn", ("type", "wheel"), ("amount", 2)), ctx);

        state.SetPhase(TurnPhase.Income);
        state.ApplyIncome();

        Assert.True(state[PlayerId.One].Resources.Wheel >= 2);
        Assert.Equal(0, state[PlayerId.One].PendingNextTurnResources.Wheel);

        var wheelAfterFirstIncome = state[PlayerId.One].Resources.Wheel;
        var regularIncome = state.PendingIncome(PlayerId.One).Wheel;
        state.SetPhase(TurnPhase.Income);
        state.ApplyIncome();

        // Second income phase must not re-grant the already-consumed pending amount -- only
        // the regular per-turn income should land.
        Assert.Equal(wheelAfterFirstIncome + regularIncome, state[PlayerId.One].Resources.Wheel);
    }
}
