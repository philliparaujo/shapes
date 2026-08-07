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
    public void Reflect_does_not_trigger_against_damage_with_no_creature_source()
    {
        // Reflect gates on HasCreatureSource, not on whether the attack has a type -- a spell
        // can be typed (see ActionExecutorTests.A_spells_attack_type_comes_from_its_own_cost)
        // and still never trigger reflect, because there is no attacking creature to redirect
        // the hit back onto.
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
    public void Ricochet_is_consumed_after_one_trigger()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "neighbor", TypeMask.Anvil, maxHealth: 10)
                      .Slot(1, "ricochet_target", TypeMask.Anvil, maxHealth: 5))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 1)]!.GrantRicochet(RicochetDirection.Left);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3)),
            ctx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1)));
        EffectInterpreter.Apply(
            Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 2)),
            ctx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1)));

        // Second hit is not redirected: the target takes it itself, the neighbor is untouched
        // by it. Without the consume the grant was a once-per-game switch that made every later
        // attack free to deflect too.
        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 1)]!.Health);
        Assert.Equal(7, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Ricochet_stays_armed_when_it_could_not_redirect_for_want_of_a_neighbor()
    {
        // The keyword is spent on a redirect that actually happened, not on any attack that
        // merely arrived -- so a grant made while the target side is empty survives until a
        // neighbor exists to receive the hit.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(1, "ricochet_target", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var target = state.Board[new SlotIndex(PlayerId.Two, 1)]!;
        target.GrantRicochet(RicochetDirection.Left);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3)),
            ctx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1)));

        Assert.Equal(2, target.Health); // took it itself
        Assert.True(target.HasKeyword(KeywordFlags.Ricochet)); // but the charge is still there
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

    [Fact]
    public void Taunt_granted_until_next_turn_expires_on_the_reset()
    {
        // Columns: "+2 health, taunt until next turn".
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "tank", TypeMask.Anvil))
            .Build();
        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("grant_keyword", ("target", "self"), ("keyword", "taunt"), ("until_next_turn", true)),
            ctx);
        Assert.True(creature.HasKeyword(KeywordFlags.Taunt));

        creature.ResetMovesForNewTurn();
        Assert.False(creature.HasKeyword(KeywordFlags.Taunt));
    }

    [Fact]
    public void Taunt_granted_without_expiry_survives_the_turn_reset()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "tank", TypeMask.Anvil))
            .Build();
        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("grant_keyword", ("target", "self"), ("keyword", "taunt")), ctx);
        creature.ResetMovesForNewTurn();

        Assert.True(creature.HasKeyword(KeywordFlags.Taunt));
    }

    [Fact]
    public void On_next_damage_taken_fires_once_when_the_creature_is_hit()
    {
        // Zealot: "next damage this takes, gain 2 anvil".
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "zealot", TypeMask.Anvil, maxHealth: 10))
            .Build();
        var defender = state.Board[new SlotIndex(PlayerId.Two, 0)]!;
        var armCtx = new EffectContext(state, PlayerId.Two, new SlotIndex(PlayerId.Two, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("on_next_damage_taken", ("target", "self"),
                ("effect", Eff.Node("gain_resource", ("type", "anvil"), ("amount", 2)))),
            armCtx);

        var attackCtx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), attackCtx);

        // The DEFENDER's controller (P2) gets the resources, not the attacker (P1).
        Assert.Equal(2, state[PlayerId.Two].Resources.Anvil);
        Assert.Equal(0, state[PlayerId.One].Resources.Anvil);
        Assert.Equal(7, defender.Health);
    }

    [Fact]
    public void On_next_damage_taken_does_not_refire_on_a_second_hit()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "zealot", TypeMask.Anvil, maxHealth: 10))
            .Build();
        var armCtx = new EffectContext(state, PlayerId.Two, new SlotIndex(PlayerId.Two, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("on_next_damage_taken", ("target", "self"),
                ("effect", Eff.Node("gain_resource", ("type", "anvil"), ("amount", 2)))),
            armCtx);

        var attackCtx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), attackCtx);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 1)), attackCtx);

        Assert.Equal(2, state[PlayerId.Two].Resources.Anvil); // only fired once
    }

    [Fact]
    public void On_next_damage_taken_still_fires_on_a_lethal_hit()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "zealot", TypeMask.Anvil, maxHealth: 2))
            .Build();
        var armCtx = new EffectContext(state, PlayerId.Two, new SlotIndex(PlayerId.Two, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("on_next_damage_taken", ("target", "self"),
                ("effect", Eff.Node("gain_resource", ("type", "anvil"), ("amount", 2)))),
            armCtx);

        var attackCtx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 10)), attackCtx);

        // The interpreter itself does not sweep the dead (ActionExecutor does, once per action)
        // -- the creature is still on the board at 0 health, but the trigger already fired.
        Assert.Equal(2, state[PlayerId.Two].Resources.Anvil);
        Assert.True(state.Board[new SlotIndex(PlayerId.Two, 0)]!.IsDead);
    }

    [Fact]
    public void On_next_damage_taken_does_not_fire_when_ricochet_redirects_the_hit_away()
    {
        // The armed creature never actually TOOK the damage -- its neighbor did.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "neighbor", TypeMask.Anvil, maxHealth: 5)
                      .Slot(1, "ricocheter", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var ricocheter = state.Board[new SlotIndex(PlayerId.Two, 1)]!;
        ricocheter.GrantRicochet(RicochetDirection.Left);
        var armCtx = new EffectContext(state, PlayerId.Two, new SlotIndex(PlayerId.Two, 1), null);
        EffectInterpreter.Apply(
            Eff.Node("on_next_damage_taken", ("target", "self"),
                ("effect", Eff.Node("gain_resource", ("type", "anvil"), ("amount", 2)))),
            armCtx);

        var attackCtx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3)),
            attackCtx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1)));

        Assert.Equal(0, state[PlayerId.Two].Resources.Anvil);
    }

    [Fact]
    public void On_next_ricochet_fires_when_this_creatures_own_ricochet_redirects_a_hit()
    {
        // Circle Bender: "when next attack ricochets, gain 3 wheel".
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "neighbor", TypeMask.Anvil, maxHealth: 5)
                      .Slot(1, "bender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var bender = state.Board[new SlotIndex(PlayerId.Two, 1)]!;
        bender.GrantRicochet(RicochetDirection.Left);
        var armCtx = new EffectContext(state, PlayerId.Two, new SlotIndex(PlayerId.Two, 1), null);
        EffectInterpreter.Apply(
            Eff.Node("on_next_ricochet", ("target", "self"),
                ("effect", Eff.Node("gain_resource", ("type", "wheel"), ("amount", 3)))),
            armCtx);

        var attackCtx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3)),
            attackCtx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1)));

        Assert.Equal(3, state[PlayerId.Two].Resources.Wheel);
        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // neighbor took the hit
        Assert.Equal(5, bender.Health); // bender itself took zero
    }

    [Fact]
    public void On_next_ricochet_does_not_fire_when_there_is_no_neighbor_to_redirect_to()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "bender", TypeMask.Anvil, maxHealth: 5)) // slot 0: no left neighbor
            .Build();
        var bender = state.Board[new SlotIndex(PlayerId.Two, 0)]!;
        bender.GrantRicochet(RicochetDirection.Left);
        var armCtx = new EffectContext(state, PlayerId.Two, new SlotIndex(PlayerId.Two, 0), null);
        EffectInterpreter.Apply(
            Eff.Node("on_next_ricochet", ("target", "self"),
                ("effect", Eff.Node("gain_resource", ("type", "wheel"), ("amount", 3)))),
            armCtx);

        var attackCtx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), attackCtx);

        Assert.Equal(0, state[PlayerId.Two].Resources.Wheel);
        Assert.Equal(2, bender.Health); // took the hit normally instead
    }
}
