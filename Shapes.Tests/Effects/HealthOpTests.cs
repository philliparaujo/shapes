using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class HealthOpTests
{
    // Gravewarden's reworked Reap: "+1 max health for each creature destroyed this turn".
    // The destroyed_this_turn scale used to live only on draw_scaled; this pins that it now
    // works through the shared DamageScale vocabulary that every *_scaled op reads.
    [Fact]
    public void Buff_max_health_scaled_reads_creatures_destroyed_this_turn()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "reaper", TypeMask.Anvil, maxHealth: 3, health: 3))
            .Build();
        state.RecordTurnEvent(TurnEventKind.CreatureDestroyed, PlayerId.Two, new SlotIndex(PlayerId.Two, 0), "x");
        state.RecordTurnEvent(TurnEventKind.CreatureDestroyed, PlayerId.One, new SlotIndex(PlayerId.One, 2), "y");
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("buff_max_health_scaled", ("target", "self"),
                ("scale", "destroyed_this_turn"), ("multiplier", 1)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 0)]!.MaxHealth); // 3 + 2 kills
    }

    [Fact]
    public void Buff_max_health_scaled_grants_nothing_when_nothing_died()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "reaper", TypeMask.Anvil, maxHealth: 3, health: 3))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("buff_max_health_scaled", ("target", "self"),
                ("scale", "destroyed_this_turn"), ("multiplier", 1)), ctx);

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.One, 0)]!.MaxHealth);
    }

    private static (GameState State, EffectContext Ctx) SelfDamaged(int maxHealth, int health)
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "healer", TypeMask.Wheel, maxHealth: maxHealth, health: health))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        return (state, ctx);
    }

    [Fact]
    public void Heal_caps_at_max_health()
    {
        var (state, ctx) = SelfDamaged(maxHealth: 5, health: 2);

        EffectInterpreter.Apply(Eff.Node("heal", ("target", "self"), ("amount", 10)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
    }

    [Fact]
    public void Heal_to_full_restores_from_arbitrary_damage()
    {
        var (state, ctx) = SelfDamaged(maxHealth: 6, health: 1);

        EffectInterpreter.Apply(Eff.Node("heal_to_full", ("target", "self")), ctx);

        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
    }

    [Fact]
    public void Buff_max_health_raises_both_current_and_max()
    {
        var (state, ctx) = SelfDamaged(maxHealth: 3, health: 3);

        EffectInterpreter.Apply(Eff.Node("buff_max_health", ("target", "self"), ("amount", 2)), ctx);

        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        Assert.Equal(5, creature.Health);
        Assert.Equal(5, creature.MaxHealth);
    }

    [Fact]
    public void Self_damage_can_kill_its_own_creature()
    {
        var (state, ctx) = SelfDamaged(maxHealth: 2, health: 2);

        EffectInterpreter.Apply(Eff.Node("self_damage", ("amount", 5)), ctx);

        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        Assert.Equal(0, creature.Health);
        Assert.True(creature.IsDead);
    }

    [Fact]
    public void Set_health_raises_when_below_target()
    {
        var (state, ctx) = SelfDamaged(maxHealth: 5, health: 1);

        EffectInterpreter.Apply(Eff.Node("set_health", ("target", "self"), ("amount", 4)), ctx);

        Assert.Equal(4, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
    }

    [Fact]
    public void Set_health_lowers_and_can_kill()
    {
        var (state, ctx) = SelfDamaged(maxHealth: 5, health: 5);

        EffectInterpreter.Apply(Eff.Node("set_health", ("target", "self"), ("amount", 0)), ctx);

        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        Assert.Equal(0, creature.Health);
        Assert.True(creature.IsDead);
    }

    [Fact]
    public void Heal_scaled_by_hand_size_targets_a_chosen_friendly()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Anvil)
                      .Slot(1, "hurt", TypeMask.Anvil, maxHealth: 10, health: 2)
                      .Hand("a", "b", "c"))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null)
            .WithChosenTarget(new SlotIndex(PlayerId.One, 1));

        EffectInterpreter.Apply(
            Eff.Node("heal_scaled", ("target", "chosen_friendly"), ("scale", "hand_size"), ("multiplier", 1)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 1)]!.Health);
    }

    [Fact]
    public void Heal_scaled_by_a_third_creatures_health_via_health_source()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "healer", TypeMask.Anvil)
                      .Slot(1, "source", TypeMask.Anvil, maxHealth: 5, health: 4)
                      .Slot(2, "hurt", TypeMask.Anvil, maxHealth: 10, health: 2))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null)
            .WithChosenTarget(new SlotIndex(PlayerId.One, 2));

        EffectInterpreter.Apply(
            Eff.Node("heal_scaled", ("target", "chosen_friendly"), ("scale", "selector_health"),
                ("health_source", "right_friendly")),
            ctx);

        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.One, 2)]!.Health); // 2 + source's health (4)
    }

    [Fact]
    public void Buff_max_health_scaled_by_hand_size_raises_both_current_and_max()
    {
        // Anchor: "+1 max health to a friendly for each card in hand".
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Anvil)
                      .Slot(1, "target", TypeMask.Anvil, maxHealth: 3, health: 3)
                      .Hand("a", "b", "c"))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null)
            .WithChosenTarget(new SlotIndex(PlayerId.One, 1));

        EffectInterpreter.Apply(
            Eff.Node(
                "buff_max_health_scaled", ("target", "chosen_friendly"), ("scale", "hand_size"),
                ("multiplier", 1)),
            ctx);

        var creature = state.Board[new SlotIndex(PlayerId.One, 1)]!;
        Assert.Equal(6, creature.Health);
        Assert.Equal(6, creature.MaxHealth);
    }
}
