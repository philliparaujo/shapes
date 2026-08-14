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
    public void Taunt_redirects_an_opposing_attack_away_from_the_facing_creature()
    {
        // "All enemy creature attacks target this creature." The attacker faces slot 1, but slot 0
        // is taunting, so the hit must land on slot 0.
        //
        // The regression this pins: taunt used to be enforced ONLY in ChosenEnemyCandidates, which
        // an `opposing` attack never consults -- and 32 of the 48 cards attack via `opposing`
        // against 3 via `chosen_enemy`, so taunt did nothing on nearly every attack in the game.
        var state = new StateBuilder()
            .P1(p => p.Slot(1, "attacker", TypeMask.Spike))
            .P2(p => p.Slot(0, "taunter", TypeMask.Anvil, maxHealth: 9)
                      .Slot(1, "facing", TypeMask.Anvil, maxHealth: 9))
            .ActivePlayer(PlayerId.One)
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Taunt);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 1), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);

        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // taunter took it
        Assert.Equal(9, state.Board[new SlotIndex(PlayerId.Two, 1)]!.Health); // facing untouched
    }

    [Fact]
    public void Taunt_pulls_an_opposing_attack_even_when_the_facing_slot_is_empty()
    {
        // The attacker faces nothing, so without taunt the attack would fizzle. Taunt is a
        // redirect, not a filter, so it still pulls the hit onto the taunter.
        var state = new StateBuilder()
            .P1(p => p.Slot(1, "attacker", TypeMask.Spike))
            .P2(p => p.Slot(0, "taunter", TypeMask.Anvil, maxHealth: 9))
            .ActivePlayer(PlayerId.One)
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Taunt);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 1), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);

        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Taunt_does_not_redirect_an_opposing_attack_from_a_spell()
    {
        // Taunt answers being attacked by a CREATURE, the same gate reflect and ricochet use.
        var state = new StateBuilder()
            .P1(p => p.Slot(1, "caster", TypeMask.Spike))
            .P2(p => p.Slot(0, "taunter", TypeMask.Anvil, maxHealth: 9)
                      .Slot(1, "centre", TypeMask.Anvil, maxHealth: 9))
            .ActivePlayer(PlayerId.One)
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Taunt);

        var ctx = new EffectContext(state, PlayerId.One, sourceSlot: null,
            chosenTarget: new SlotIndex(PlayerId.Two, 1));
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3)), ctx);

        Assert.Equal(9, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 1)]!.Health);
    }

    [Fact]
    public void Taunt_does_not_shield_other_creatures_from_an_aoe()
    {
        // all_enemies names every enemy explicitly; taunt redirects a single-target attack, it
        // does not absorb board-wide damage.
        var state = new StateBuilder()
            .P1(p => p.Slot(1, "attacker", TypeMask.Spike))
            .P2(p => p.Slot(0, "taunter", TypeMask.Anvil, maxHealth: 9)
                      .Slot(1, "centre", TypeMask.Anvil, maxHealth: 9))
            .ActivePlayer(PlayerId.One)
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Taunt);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 1), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "all_enemies"), ("amount", 3)), ctx);

        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 1)]!.Health);
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
    public void Reflect_ignores_the_hit_entirely_damaging_neither_side()
    {
        // Reflect is pure negation, not a counter-attack: the hit is ignored, and the attacker
        // takes nothing back for having thrown it.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel, maxHealth: 5))
            .P2(p => p.Slot(0, "defender", TypeMask.Anvil, maxHealth: 5))
            .Build();
        state.Board[new SlotIndex(PlayerId.Two, 0)]!.GrantKeyword(KeywordFlags.Reflect);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // defender untouched
        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health); // attacker untouched
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

        // First hit is ignored; the second is not reflected, so the defender takes it normally.
        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
        Assert.Equal(10, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health); // attacker never hurt
    }

    [Fact]
    public void Reflect_does_not_trigger_against_damage_with_no_creature_source()
    {
        // Reflect gates on HasCreatureSource, not on whether the attack has a type -- a spell
        // can be typed (see ActionExecutorTests.A_spells_attack_type_comes_from_its_own_cost)
        // and still never trigger reflect, because a spell is not a creature's attack. Reflect
        // answers being attacked by a creature; damage from elsewhere lands normally.
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
    public void Ricochet_granted_both_sides_falls_through_to_the_other_side()
    {
        // Snowball's Carom arms both sides, as two separate grants (the way the card
        // authors it: "Gain ricochet left. Gain ricochet right."). With no LEFT neighbor the hit
        // must still redirect right rather than landing on the ricochet holder -- the
        // single-direction model this replaced had the second grant overwrite the first.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "ricochet_target", TypeMask.Anvil, maxHealth: 5) // slot 0: no left neighbor
                      .Slot(1, "right_neighbor", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var target = state.Board[new SlotIndex(PlayerId.Two, 0)]!;
        target.GrantRicochet(RicochetDirection.Left);
        target.GrantRicochet(RicochetDirection.Right);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        EffectInterpreter.Apply(Eff.Node("damage", ("target", "opposing"), ("amount", 3)), ctx);

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health); // took zero
        Assert.Equal(2, state.Board[new SlotIndex(PlayerId.Two, 1)]!.Health); // right took it
    }

    [Fact]
    public void Ricochet_granted_both_sides_deflects_twice_spending_one_side_per_hit()
    {
        // Each armed side is its own charge (Snowball's Carom): the first hit spends
        // left and the second spends right, so granting both is worth more than granting either
        // alone. The holder is in the MIDDLE slot so it has a neighbor on each side.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "left_neighbor", TypeMask.Anvil, maxHealth: 9)
                      .Slot(1, "ricochet_target", TypeMask.Anvil, maxHealth: 5)
                      .Slot(2, "right_neighbor", TypeMask.Anvil, maxHealth: 9))
            .Build();
        var holder = state.Board[new SlotIndex(PlayerId.Two, 1)]!;
        holder.GrantRicochet(RicochetDirection.Left);
        holder.GrantRicochet(RicochetDirection.Right);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        var hit = Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3));
        var atHolder = ctx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1));

        // First hit: left is tried first, so the LEFT neighbor takes it and only left is spent.
        EffectInterpreter.Apply(hit, atHolder);
        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
        Assert.Equal(5, holder.Health);
        Assert.True(holder.HasKeyword(KeywordFlags.Ricochet));
        Assert.Equal(RicochetDirection.Right, holder.RicochetDirection);

        // Second hit: right is all that is left, so the RIGHT neighbor takes it.
        EffectInterpreter.Apply(hit, atHolder);
        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 2)]!.Health);
        Assert.Equal(5, holder.Health);
        Assert.False(holder.HasKeyword(KeywordFlags.Ricochet));
        Assert.Equal(RicochetDirection.None, holder.RicochetDirection);

        // Third hit: nothing armed, so the holder finally takes it itself.
        EffectInterpreter.Apply(hit, atHolder);
        Assert.Equal(2, holder.Health);
    }

    [Fact]
    public void One_armed_side_is_still_spent_by_a_single_redirect()
    {
        // The single-side case must not regress into two free deflections: one grant, one
        // redirect, then the keyword is gone.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Wheel))
            .P2(p => p.Slot(0, "left_neighbor", TypeMask.Anvil, maxHealth: 9)
                      .Slot(1, "ricochet_target", TypeMask.Anvil, maxHealth: 5))
            .Build();
        var holder = state.Board[new SlotIndex(PlayerId.Two, 1)]!;
        holder.GrantRicochet(RicochetDirection.Left);

        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);
        var hit = Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 3));
        var atHolder = ctx.WithChosenTarget(new SlotIndex(PlayerId.Two, 1));

        EffectInterpreter.Apply(hit, atHolder);
        Assert.Equal(6, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
        Assert.False(holder.HasKeyword(KeywordFlags.Ricochet));

        EffectInterpreter.Apply(hit, atHolder);
        Assert.Equal(2, holder.Health);
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
    public void Taunt_granted_until_next_turn_survives_the_opponents_turn()
    {
        // Columns' Renovate, Circle Thorn's Hunker, Shieldbearer's Shield Bash: "taunt until your
        // next turn" exists to soak the OPPONENT's attacks, so the one turn it must be live is
        // theirs. This previously expired in ResetMovesForNewTurn, which despite the name fires as
        // the GRANTING player's turn ends -- so the taunt vanished before the opponent ever acted
        // and every card granting it was a no-op. Driven through real turn transitions rather than
        // by poking the reset directly, which is what let that go unnoticed.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "tank", TypeMask.Anvil))
            .ActivePlayer(PlayerId.One)
            .Build();
        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("grant_keyword", ("target", "self"), ("keyword", "taunt"), ("until_next_turn", true)),
            ctx);
        Assert.True(creature.HasKeyword(KeywordFlags.Taunt));

        // The opponent's whole turn: still taunting, which is the entire point of the keyword.
        state.EndTurn();
        state.AdvanceToActions();
        Assert.True(creature.HasKeyword(KeywordFlags.Taunt));

        // Back to the granting player: now it lapses.
        state.EndTurn();
        state.AdvanceToActions();
        Assert.False(creature.HasKeyword(KeywordFlags.Taunt));
    }

    [Fact]
    public void Stun_still_costs_the_victim_their_next_turn()
    {
        // The mirror of the taunt case, pinned because the fix moved taunt's expiry OFF the hook
        // stun shares. Stun is applied to the opponent's creature, so end-of-turn clearing is
        // correct for it: it must survive the stunner's end-of-turn, hold through the victim's
        // whole turn, and clear as that turn ends.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Spike))
            .P2(p => p.Slot(0, "victim", TypeMask.Anvil))
            .ActivePlayer(PlayerId.One)
            .Build();
        var victim = state.Board[new SlotIndex(PlayerId.Two, 0)]!;

        victim.Stun();

        state.EndTurn();
        state.AdvanceToActions();
        Assert.True(victim.IsStunned); // the victim's turn: they lose their moves

        state.EndTurn();
        state.AdvanceToActions();
        Assert.False(victim.IsStunned);
    }

    [Fact]
    public void Taunt_granted_without_expiry_survives_a_full_turn_cycle()
    {
        // Permanent taunt: only the `until_next_turn` form is on a clock, so a full round trip
        // back to the granting player must leave it standing.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "tank", TypeMask.Anvil))
            .ActivePlayer(PlayerId.One)
            .Build();
        var creature = state.Board[new SlotIndex(PlayerId.One, 0)]!;
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("grant_keyword", ("target", "self"), ("keyword", "taunt")), ctx);

        state.EndTurn();
        state.AdvanceToActions();
        state.EndTurn();
        state.AdvanceToActions();

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
