using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class ModifierOpTests
{
    [Fact]
    public void Next_attack_bonus_sets_the_creatures_pending_bonus()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "buffed", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("next_attack_bonus", ("target", "self"), ("amount", 3)), ctx);

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.One, 0)]!.NextAttackBonus);
    }

    [Fact]
    public void Next_damage_taken_bonus_sets_the_targets_pending_bonus()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "cursed", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("next_damage_taken_bonus", ("target", "opposing"), ("amount", 2)), ctx);

        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.NextDamageTakenBonus);
    }

    [Fact]
    public void Two_stacked_next_attack_bonus_calls_overwrite_rather_than_stack()
    {
        // Defined behaviour for stacking: the later call replaces the earlier one, since both
        // are "next attack" -- there is only one "next attack" to modify.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "buffed", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("next_attack_bonus", ("target", "self"), ("amount", 1)), ctx);
        EffectInterpreter.Apply(Eff.Node("next_attack_bonus", ("target", "self"), ("amount", 5)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 0)]!.NextAttackBonus);
    }

    [Fact]
    public void Attack_buff_sets_the_creatures_persistent_bonus()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "buffed", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("attack_buff", ("target", "self"), ("amount", 2)), ctx);

        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.One, 0)]!.AttackBuff);
    }

    [Fact]
    public void Repeated_attack_buffs_stack_rather_than_overwrite()
    {
        // Unlike next_attack_bonus, this is a persistent buff -- "increase ALL damage" implies
        // repeated grants add up, not replace each other.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "buffed", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("attack_buff", ("target", "self"), ("amount", 2)), ctx);
        EffectInterpreter.Apply(Eff.Node("attack_buff", ("target", "self"), ("amount", 3)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 0)]!.AttackBuff);
    }

    [Fact]
    public void Attack_buff_applies_to_every_future_hit_without_being_consumed()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 20))
            .Build();
        var attacker = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        attacker.AddAttackBuff(2);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), ctx);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), ctx);

        // Both hits got +2, unlike next_attack_bonus which would only apply once.
        Assert.Equal(14, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // 20 - 3 - 3
    }

    [Fact]
    public void Attack_buff_scaled_reads_the_sources_missing_health()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "hurt", TypeMask.Spike, maxHealth: 6, health: 2))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("attack_buff_scaled", ("target", "self"), ("scale", "missing_health")), ctx);

        Assert.Equal(4, state.Board[new SlotIndex(PlayerId.One, 0)]!.AttackBuff); // 6 - 2
    }

    [Fact]
    public void Attack_buff_scaled_grants_nothing_at_full_health()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "healthy", TypeMask.Spike, maxHealth: 6))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("attack_buff_scaled", ("target", "self"), ("scale", "missing_health")), ctx);

        Assert.Equal(0, state.Board[new SlotIndex(PlayerId.One, 0)]!.AttackBuff);
    }

    // Rally is a SPELL, so ctx.SourceSlot is null and scale "missing_health" -- which reads
    // SourceCreature -- would silently compute 0 for it. selector_missing_health names the
    // creature instead. This is the case that made the second scale necessary; without it Rally
    // would load, run, and quietly do nothing.
    [Fact]
    public void Attack_buff_scaled_reads_a_named_selector_when_there_is_no_creature_source()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(1, "hurt", TypeMask.Spike, maxHealth: 7, health: 3))
            .Build();
        var target = new SlotIndex(PlayerId.One, 1);
        var ctx = new EffectContext(state, PlayerId.One, sourceSlot: null, chosenTarget: target);

        EffectInterpreter.Apply(
            Eff.Node("attack_buff_scaled",
                ("target", "chosen_friendly"),
                ("scale", "selector_missing_health"),
                ("health_source", "chosen_friendly")),
            ctx);

        Assert.Equal(4, state.Board[target]!.AttackBuff); // 7 - 3, despite no source creature
    }

    [Fact]
    public void Attack_buff_scaled_with_missing_health_is_zero_for_a_spell()
    {
        // The negative control for the test above: the source-reading scale really does yield
        // nothing without a creature source, which is why the selector form has to exist.
        var state = new StateBuilder()
            .P1(p => p.Slot(1, "hurt", TypeMask.Spike, maxHealth: 7, health: 3))
            .Build();
        var target = new SlotIndex(PlayerId.One, 1);
        var ctx = new EffectContext(state, PlayerId.One, sourceSlot: null, chosenTarget: target);

        EffectInterpreter.Apply(
            Eff.Node("attack_buff_scaled", ("target", "chosen_friendly"), ("scale", "missing_health")),
            ctx);

        Assert.Equal(0, state.Board[target]!.AttackBuff);
    }
}
