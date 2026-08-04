using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class DamageOpTests
{
    private static GameState BasicState(int p1Health = 5, int p2Health = 5) =>
        new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel, maxHealth: p1Health))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: p2Health))
            .Build();

    private static EffectContext SourceContext(GameState state, ResourceType? moveType = null) =>
        new(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null, moveType);

    [Fact]
    public void Damage_reduces_target_health_by_the_exact_amount()
    {
        var state = BasicState();
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 2)), ctx);

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Lethal_damage_reduces_health_to_zero_and_marks_dead()
    {
        var state = BasicState(p2Health: 2);
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 10)), ctx);

        var defender = state.Board[new SlotIndex(PlayerId.Two, 0)]!;
        Assert.Equal(0, defender.Health);
        Assert.True(defender.IsDead);
    }

    [Fact]
    public void Damage_with_no_target_present_is_a_no_op()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .Build();
        var ctx = SourceContext(state);

        // Opposing slot is empty; should not throw.
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);
    }

    [Fact]
    public void Attack_type_doubles_damage_against_a_weak_target()
    {
        // Wheel beats Anvil.
        var state = BasicState();
        var ctx = SourceContext(state, ResourceType.Wheel);

        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 2)), ctx);

        Assert.Equal(1, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Typeless_damage_is_never_doubled()
    {
        var state = BasicState();
        var ctx = SourceContext(state, moveType: null);

        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 2)), ctx);

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Next_attack_bonus_combines_with_base_before_the_type_multiplier()
    {
        // Pinned ordering: (base + bonus) * multiplier, not base * multiplier + bonus.
        // (2 + 1) * 2 = 6, versus the wrong ordering's 2*2 + 1 = 5 -- distinct enough that a
        // large defender health tells the two orderings apart instead of both clamping to 0.
        var state = BasicState(p2Health: 20);
        var attacker = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        attacker.SetNextAttackBonus(1);

        var ctx = SourceContext(state, ResourceType.Wheel);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 2)), ctx);

        Assert.Equal(14, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Next_attack_bonus_is_consumed_after_one_use()
    {
        var state = BasicState();
        var attacker = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        attacker.SetNextAttackBonus(2);

        var ctx = SourceContext(state);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), ctx);
        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // 5 - (1+2)

        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), ctx);
        Assert.Equal(1, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // bonus gone, -1 only
    }

    [Fact]
    public void Next_damage_taken_bonus_is_consumed_after_one_hit()
    {
        var state = BasicState();
        var defender = state.Board[new SlotIndex(PlayerId.Two, 0)]!;
        defender.SetNextDamageTakenBonus(3);

        var ctx = SourceContext(state);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), ctx);
        Assert.Equal(1, defender.Health); // 5 - (1+3)

        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), ctx);
        Assert.Equal(0, defender.Health);
    }

    [Fact]
    public void A_spell_effect_with_no_move_type_deals_typeless_damage()
    {
        // Only a genuinely free spell has no MoveType (see CardDefinition.AttackType) -- a
        // spell has no creature source, but "no creature source" and "no attack type" are
        // independent. This test is about the interpreter's own handling of a null MoveType,
        // not a claim that every spell is typeless; see DamageOpTypingTests for a spell whose
        // cost DOES give it an attack type.
        var state = BasicState();
        var ctx = new EffectContext(state, PlayerId.One, sourceSlot: null, chosenTarget: null);

        EffectInterpreter.Apply(
            Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 2)),
            ctx.WithChosenTarget(new SlotIndex(PlayerId.Two, 0)));

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Damage_scaled_by_source_health()
    {
        var state = BasicState(p1Health: 4);
        var attacker = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        attacker.TakeDamage(1); // health now 3
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "health"), ("multiplier", 1)), ctx);

        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // 5 - 3
    }

    [Fact]
    public void Damage_scaled_by_friendly_creature_count()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Wheel))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "count"), ("multiplier", 1)), ctx);

        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // 5 - 2
    }

    [Fact]
    public void Damage_scaled_by_hand_size()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Hand("x", "y", "z"))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "hand_size"), ("multiplier", 1)), ctx);

        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // 5 - 3
    }

    [Fact]
    public void Damage_scaled_at_zero_deals_no_damage()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "hand_size"), ("multiplier", 1)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Damage_scaled_by_a_third_creatures_health_via_health_source()
    {
        // Worshipper: hit an ENEMY for an amount equal to the LEFT FRIENDLY's health -- three
        // independent slots (attacker, health source, victim). "health_source" is what lets
        // scale:selector_health read a creature that is neither the source nor the target.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "left", TypeMask.Anvil, maxHealth: 5, health: 3)
                      .Slot(1, "caster", TypeMask.Anvil, maxHealth: 9))
            .P2(p => p.Slot(1, "defender", TypeMask.Spike, maxHealth: 10))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 1), null);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "selector_health"),
                ("health_source", "left_friendly"), ("multiplier", 1)),
            ctx);

        // The victim (P2 slot 1) takes 3 -- the LEFT FRIENDLY's health, untouched itself.
        Assert.Equal(7, state.Board[new SlotIndex(PlayerId.Two, 1)]!.Health);
        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
    }

    [Fact]
    public void Damage_scaled_selector_health_with_no_resolved_health_source_is_zero()
    {
        // e.g. left_friendly from slot 0 has no neighbor -- the health source resolves to
        // nothing, so the amount is 0 rather than throwing.
        var state = BasicState();
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "selector_health"),
                ("health_source", "left_friendly"), ("multiplier", 1)),
            ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Damage_scaled_by_the_controllers_current_resource_count()
    {
        // Champion T: "deal 1 for each spike [resource]".
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Spike).Resources(spike: 4))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 10))
            .Build();
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "resource"),
                ("resource_type", "spike"), ("multiplier", 1)),
            ctx);

        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // 10 - 4
    }

    [Fact]
    public void Damage_scaled_applies_a_divisor_by_integer_division()
    {
        // T Swarm: "1 damage for each 2 health this has" -- floor, not a fraction.
        var state = BasicState(p1Health: 8, p2Health: 20);
        var attacker = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        attacker.TakeDamage(3); // health now 5
        var ctx = SourceContext(state);

        EffectInterpreter.Apply(
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "health"),
                ("multiplier", 1), ("divisor", 2)),
            ctx);

        Assert.Equal(18, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // floor(5/2) = 2
    }
}
