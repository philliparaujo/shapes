using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

// The predicate vocabulary: one generic `creature_state` op parameterized by target + check,
// rather than a bespoke predicate name per card (self_at_full_health, target_damaged, ...).
public class ConditionEvaluatorTests
{
    private static EffectContext SelfAt(int maxHealth, int health)
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel, maxHealth: maxHealth, health: health))
            .Build();
        return new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
    }

    [Fact]
    public void Full_health_check_holds_at_max_health()
    {
        var ctx = SelfAt(maxHealth: 3, health: 3);
        Assert.True(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "full_health"))));
    }

    [Fact]
    public void Full_health_check_fails_when_damaged()
    {
        var ctx = SelfAt(maxHealth: 3, health: 2);
        Assert.False(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "full_health"))));
    }

    [Fact]
    public void Damaged_check_holds_below_max_health()
    {
        var ctx = SelfAt(maxHealth: 3, health: 2);
        Assert.True(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "damaged"))));
    }

    [Fact]
    public void Damaged_check_can_target_an_opposing_creature()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "foe", TypeMask.Anvil, maxHealth: 5, health: 3))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.True(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "opposing"), ("check", "damaged"))));
    }

    [Fact]
    public void A_check_against_a_target_that_resolves_to_no_creature_is_false()
    {
        // Opposing is empty -- the state being checked cannot hold of a creature that is not
        // there, so this must fail rather than throw.
        var ctx = SelfAt(maxHealth: 3, health: 3);

        Assert.False(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "opposing"), ("check", "damaged"))));
    }

    [Fact]
    public void Health_at_most_holds_when_at_or_below_the_threshold()
    {
        var ctx = SelfAt(maxHealth: 5, health: 3);
        Assert.True(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "health_at_most:3"))));
    }

    [Fact]
    public void Health_at_most_fails_above_the_threshold()
    {
        var ctx = SelfAt(maxHealth: 5, health: 4);
        Assert.False(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "health_at_most:3"))));
    }

    [Fact]
    public void Unopposed_holds_when_the_facing_slot_is_empty()
    {
        var ctx = SelfAt(maxHealth: 3, health: 3);
        Assert.True(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "unopposed"))));
    }

    [Fact]
    public void Unopposed_fails_when_the_facing_slot_is_occupied()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "foe", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.False(ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "unopposed"))));
    }

    [Fact]
    public void Unopposed_rejects_a_target_other_than_self()
    {
        var ctx = SelfAt(maxHealth: 3, health: 3);

        Assert.Throws<ArgumentException>(() => ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "opposing"), ("check", "unopposed"))));
    }

    [Fact]
    public void An_unknown_predicate_op_throws()
    {
        var ctx = SelfAt(maxHealth: 3, health: 3);

        Assert.Throws<ArgumentException>(() => ConditionEvaluator.Evaluate(ctx, Eff.Node("self_at_full_health")));
    }

    [Fact]
    public void An_unknown_check_throws()
    {
        var ctx = SelfAt(maxHealth: 3, health: 3);

        Assert.Throws<ArgumentException>(() => ConditionEvaluator.Evaluate(
            ctx, Eff.Node("creature_state", ("target", "self"), ("check", "invincible"))));
    }
}
