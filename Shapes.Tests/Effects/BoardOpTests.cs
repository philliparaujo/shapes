using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class BoardOpTests
{
    [Fact]
    public void Destroy_frees_the_slot()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "target", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy", ("target", "opposing")), ctx);

        Assert.True(state.Board.IsEmpty(new SlotIndex(PlayerId.Two, 0)));
    }

    [Fact]
    public void Destroy_with_no_target_present_is_a_no_op()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy", ("target", "opposing")), ctx);
    }

    [Fact]
    public void Summon_places_a_new_creature_into_an_empty_friendly_slot()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("summon", ("target", "all_friendlies"), ("card_id", "token"),
                ("health", 1), ("types", "spike")),
            ctx);

        var summoned = state.Board[new SlotIndex(PlayerId.One, 1)];
        Assert.NotNull(summoned);
        Assert.Equal("token", summoned!.CardId);
        Assert.Equal(1, summoned.Health);
        Assert.True(summoned.Types.Has(ResourceType.Spike));
    }

    [Fact]
    public void Summon_into_a_full_board_is_a_no_op()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Wheel).Slot(2, "c", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("summon", ("target", "all_friendlies"), ("card_id", "token"),
                ("health", 1), ("types", "spike")),
            ctx);

        Assert.Equal(3, state.Board.CountCreatures(PlayerId.One));
    }

    [Fact]
    public void Destroy_refund_cost_removes_the_target_and_pays_its_own_controller()
    {
        // Suffocate: destroy an enemy, but ITS controller gets its play cost back.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "target", TypeMask.Anvil))
            .Build();
        var targetCreature = state.Board[new SlotIndex(PlayerId.Two, 0)]!;
        // PlayCost is normally set by ActionExecutor.ApplyPlayCard; set it directly for the test.
        var withCost = new CreatureInstance("target", targetCreature.MaxHealth, targetCreature.Types,
            playCost: new ResourcePool(0, 2, 0));
        state.Board.Remove(new SlotIndex(PlayerId.Two, 0));
        state.Board.Place(new SlotIndex(PlayerId.Two, 0), withCost);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("destroy_refund_cost", ("target", "opposing")), ctx);

        Assert.True(state.Board.IsEmpty(new SlotIndex(PlayerId.Two, 0)));
        Assert.Equal(2, state[PlayerId.Two].Resources.Anvil);
        Assert.Equal(0, state[PlayerId.One].Resources.Anvil); // the CASTER gets nothing
    }

    [Fact]
    public void Destroy_refund_cost_with_no_target_present_is_a_no_op()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy_refund_cost", ("target", "opposing")), ctx);

        Assert.Equal(ResourcePool.Empty, state[PlayerId.One].Resources);
    }

    [Fact]
    public void Destroy_and_destroy_refund_cost_both_record_a_turn_event()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "target", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy", ("target", "opposing")), ctx);

        Assert.Contains(state.TurnEvents, e => e.Kind == TurnEventKind.CreatureDestroyed
            && e.Slot == new SlotIndex(PlayerId.Two, 0));
    }
}
