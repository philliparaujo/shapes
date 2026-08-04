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

    [Fact]
    public void Gain_resource_scaled_by_source_health()
    {
        // T Flare: "gain 1 spike per health".
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel, maxHealth: 5, health: 3))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("gain_resource_scaled", ("type", "spike"), ("scale", "health"), ("multiplier", 1)), ctx);

        Assert.Equal(3, state[PlayerId.One].Resources.Spike);
    }

    [Fact]
    public void Gain_resource_scaled_by_hand_composition_counts_only_matching_type_cards()
    {
        // Rally: "gain 2 spike for each SPIKE card in hand". hand_composition reads
        // HandComposition[type] using the SAME type this op gains -- see
        // ActionExecutorTests.Playing_a_spell_computes_hand_composition_from_the_remaining_hand
        // for the end-to-end version computed from real hand cards.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Spike).Hand("a", "b", "c"))
            .Build();

        // HandComposition is normally precomputed by ActionExecutor from the real hand; a test
        // constructs it directly: 3 spike-cost cards, 0 anvil, 0 wheel.
        var ctx = new EffectContext(
            state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null,
            handComposition: new ResourcePool(3, 0, 0));

        EffectInterpreter.Apply(
            Eff.Node("gain_resource_scaled", ("type", "spike"), ("scale", "hand_composition"), ("multiplier", 2)),
            ctx);

        Assert.Equal(6, state[PlayerId.One].Resources.Spike);
    }

    [Fact]
    public void Gain_resource_scaled_rejects_selector_health_scale()
    {
        // gain_resource_scaled has no "target" of its own to distinguish from a health source.
        var (state, ctx) = Setup();

        Assert.Throws<ArgumentException>(() => EffectInterpreter.Apply(
            Eff.Node("gain_resource_scaled", ("type", "spike"), ("scale", "selector_health")), ctx));
    }
}
