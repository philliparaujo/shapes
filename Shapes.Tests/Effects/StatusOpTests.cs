using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class StatusOpTests
{
    [Fact]
    public void Grant_keyword_taunt_sets_the_flag()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("grant_keyword", ("target", "self"), ("keyword", "taunt")), ctx);

        Assert.True(state.Board[new SlotIndex(PlayerId.One, 0)]!.HasKeyword(KeywordFlags.Taunt));
    }

    [Fact]
    public void Taunt_restricts_chosen_enemy_targeting_to_taunting_creatures()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "tank", TypeMask.Anvil).Slot(1, "squishy", TypeMask.Anvil))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Taunt);

        // Effect has a creature source (a move), so taunt applies.
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        var candidates = TargetResolver.ChosenCandidates(ctx, TargetSelector.ChosenEnemy);

        Assert.Equal([new SlotIndex(PlayerId.Two, 0)], candidates);
    }

    [Fact]
    public void Taunt_does_not_restrict_spell_targeting()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "tank", TypeMask.Anvil).Slot(1, "squishy", TypeMask.Anvil))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Taunt);

        var ctx = new EffectContext(state, PlayerId.One, sourceSlot: null, chosenTarget: null);

        var candidates = TargetResolver.ChosenCandidates(ctx, TargetSelector.ChosenEnemy);

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void No_taunting_creature_leaves_all_enemies_as_candidates()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "a", TypeMask.Anvil).Slot(1, "b", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        var candidates = TargetResolver.ChosenCandidates(ctx, TargetSelector.ChosenEnemy);

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void Reflect_deals_full_damage_to_the_attacker_and_none_to_the_defender()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel, maxHealth: 5))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Reflect);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // defender untouched
        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health); // attacker took it
    }

    [Fact]
    public void Reflect_is_consumed_after_one_trigger()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel, maxHealth: 10))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Reflect);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 2)), ctx);

        // Second hit is not reflected: defender takes the second hit normally.
        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
        Assert.Equal(7, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
    }

    [Fact]
    public void Reflect_does_not_trigger_against_typeless_spell_damage()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel, maxHealth: 5))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Reflect);

        // No creature source -- a spell.
        var ctx = new EffectContext(state, PlayerId.One, sourceSlot: null,
            chosenTarget: new SlotIndex(PlayerId.Two, 0));
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3)), ctx);

        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // defender takes it
    }

    [Fact]
    public void Ricochet_redirects_damage_to_the_specified_neighbor_taking_zero_itself()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "left_neighbor", TypeMask.Anvil, maxHealth: 5)
                      .Slot(1, "ricochet_target", TypeMask.Anvil, maxHealth: 5))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 1)]!.GrantRicochet(RicochetDirection.Left);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3)),
            ctx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1)));

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.Two, 1)]!.Health); // took zero
        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // took all of it
    }

    [Fact]
    public void Ricochet_does_not_trigger_when_there_is_no_friendly_neighbor_on_that_side()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "ricochet_target", TypeMask.Anvil, maxHealth: 5)) // slot 0: no left neighbor
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantRicochet(RicochetDirection.Left);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);

        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Stun_prevents_moves_and_clears_on_the_next_turn_reset()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "target", TypeMask.Wheel))
            .Build();
        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("stun", ("target", "self")), ctx);
        Assert.True(creature.IsStunned);

        creature.ResetMovesForNewTurn();
        Assert.False(creature.IsStunned);
    }
}
