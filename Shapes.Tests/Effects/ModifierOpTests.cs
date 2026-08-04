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
}
