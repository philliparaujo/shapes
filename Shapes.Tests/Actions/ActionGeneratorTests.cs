using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Actions;

// Legality rules for the generated action list.
//
// These are the rules every consumer inherits -- console, AI, and UI all read this one list --
// so a gap here is a rule the AI is free to break in a way no other test would catch.
public class ActionGeneratorTests
{
    private static IReadOnlyList<GameAction> Generate(GameState state) =>
        ActionGenerator.Generate(state, TestCards.Database);

    private static GameState WithHand(params string[] hand) =>
        new StateBuilder()
            .P1(p => p.Hand(hand).Resources(spike: 5, anvil: 5, wheel: 5))
            .Build();

    // -- End turn --------------------------------------------------------------------------

    [Fact]
    public void End_turn_is_always_legal_during_the_actions_phase()
    {
        // Non-emptiness is a load-bearing invariant, not a convenience: a player with no
        // affordable action must still have a way to proceed, or random-play fuzzing (step
        // 1.13) deadlocks instead of terminating.
        var state = new StateBuilder().Build();

        Assert.Contains(Generate(state), a => a.Kind == ActionKind.EndTurn);
    }

    [Fact]
    public void A_player_with_nothing_to_do_still_has_exactly_one_action()
    {
        var state = new StateBuilder().Build();

        var actions = Generate(state);

        Assert.Single(actions);
        Assert.Equal(ActionKind.EndTurn, actions[0].Kind);
    }

    [Fact]
    public void A_finished_game_offers_no_actions_at_all()
    {
        // Not even EndTurn. A won game is over; continuing to act would let a sim record
        // actions taken after the result was already decided.
        var state = new StateBuilder()
            .P1(p => p.Score(RuleSet.Default.ScoreToWin).Hand(TestCards.Striker).Resources(wheel: 5))
            .Build();

        Assert.Empty(Generate(state));
    }

    [Theory]
    [InlineData(TurnPhase.Scoring)]
    [InlineData(TurnPhase.Income)]
    [InlineData(TurnPhase.Ended)]
    public void No_actions_are_offered_outside_the_actions_phase(TurnPhase phase)
    {
        var state = new StateBuilder()
            .Phase(phase)
            .P1(p => p.Hand(TestCards.Striker).Resources(wheel: 5))
            .Build();

        Assert.Empty(Generate(state));
    }

    // -- Playing cards ---------------------------------------------------------------------

    [Fact]
    public void A_creature_in_hand_generates_one_play_per_empty_slot()
    {
        var state = WithHand(TestCards.Striker);

        var plays = Generate(state).OfType<PlayCardAction>().ToList();

        Assert.Equal(SlotIndex.SlotsPerPlayer, plays.Count);
        Assert.Equal(
            SlotIndex.AllFor(PlayerId.One).ToHashSet(),
            plays.Select(p => p.TargetSlot!.Value).ToHashSet());
    }

    [Fact]
    public void A_creature_cannot_be_played_into_an_occupied_slot()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Hand(TestCards.Striker)
                .Resources(wheel: 5))
            .Build();

        var plays = Generate(state).OfType<PlayCardAction>().ToList();

        Assert.Equal(2, plays.Count);
        Assert.DoesNotContain(plays, p => p.TargetSlot!.Value.Slot == 0);
    }

    [Fact]
    public void A_creature_is_not_playable_onto_a_full_board()
    {
        // Board caps at three per side -- the cap is expressed as "no empty slots", so it holds
        // without a separate count check.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel)
                .Slot(2, TestCards.Striker, TypeMask.Wheel)
                .Hand(TestCards.Striker)
                .Resources(wheel: 5))
            .Build();

        Assert.DoesNotContain(Generate(state), a => a.Kind == ActionKind.PlayCard);
    }

    [Fact]
    public void An_unaffordable_card_is_not_in_the_legal_list()
    {
        // The soundness invariant in its most direct form: the generator never offers something
        // the executor would throw on, because PlayerState.Pay throws rather than clamping.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Costly).Resources(anvil: 1))
            .Build();

        Assert.DoesNotContain(Generate(state), a => a.Kind == ActionKind.PlayCard);
    }

    [Fact]
    public void A_card_affordable_to_the_exact_pip_is_legal()
    {
        // The boundary: Covers must be >=, not >. An off-by-one here would make every
        // exact-cost play illegal, which is a whole class of turns the AI would never consider.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Costly).Resources(anvil: 9))
            .Build();

        Assert.Contains(Generate(state), a => a.Kind == ActionKind.PlayCard);
    }

    [Fact]
    public void Duplicate_copies_in_hand_collapse_to_one_action_per_slot()
    {
        // Two copies of a card are the same choice: cards are static data with no per-copy
        // identity. Leaving both in would give MCTS two identical edges and split its statistics
        // across them.
        var state = WithHand(TestCards.Striker, TestCards.Striker, TestCards.Striker);

        var plays = Generate(state).OfType<PlayCardAction>().ToList();

        Assert.Equal(SlotIndex.SlotsPerPlayer, plays.Count);
        Assert.Equal(plays.Count, plays.Distinct().Count());
    }

    [Fact]
    public void Distinct_cards_in_hand_each_generate_their_own_plays()
    {
        var state = WithHand(TestCards.Striker, TestCards.TwoMove);

        var plays = Generate(state).OfType<PlayCardAction>().ToList();

        Assert.Equal(SlotIndex.SlotsPerPlayer * 2, plays.Count);
    }

    // -- Spells ----------------------------------------------------------------------------

    [Fact]
    public void An_untargeted_spell_generates_exactly_one_action_with_no_slot()
    {
        // A spell never enters the board, so it has no target slot -- unlike a creature, which
        // must choose one.
        var state = WithHand(TestCards.Bolt);

        var play = Assert.Single(Generate(state).OfType<PlayCardAction>());

        Assert.Null(play.TargetSlot);
        Assert.Null(play.ChosenTarget);
    }

    [Fact]
    public void A_targeted_spell_expands_into_one_action_per_enemy()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.TargetedBolt).Resources(wheel: 5))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(2, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var plays = Generate(state).OfType<PlayCardAction>().ToList();

        Assert.Equal(2, plays.Count);
        Assert.Equal(
            new HashSet<SlotIndex> { new(PlayerId.Two, 0), new(PlayerId.Two, 2) },
            plays.Select(p => p.ChosenTarget!.Value).ToHashSet());
    }

    [Fact]
    public void A_targeted_spell_with_no_valid_target_is_not_playable()
    {
        // Zero candidates means zero actions. Offering it anyway would let a player burn the
        // card and its cost for nothing, and would give the search an edge whose only effect is
        // losing tempo.
        var state = WithHand(TestCards.TargetedBolt);

        Assert.DoesNotContain(Generate(state), a => a.Kind == ActionKind.PlayCard);
    }

    // -- Using moves -----------------------------------------------------------------------

    [Fact]
    public void A_creature_on_the_board_can_use_its_move()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 5))
            .Build();

        var move = Assert.Single(Generate(state).OfType<UseMoveAction>());

        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
        Assert.Equal(0, move.MoveIndex);
    }

    [Fact]
    public void A_creature_played_this_turn_can_act_immediately()
    {
        // No summoning sickness. Asserted through the real action path rather than by
        // inspecting a flag, since "can act" is the actual rule.
        var state = WithHand(TestCards.Striker);
        var play = Generate(state).OfType<PlayCardAction>().First();

        ActionExecutor.Apply(state, TestCards.Database, play);

        Assert.Contains(Generate(state), a => a.Kind == ActionKind.UseMove);
    }

    [Fact]
    public void Each_move_is_offered_once_per_turn()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Spike).Resources(spike: 5))
            .Build();

        var first = Generate(state).OfType<UseMoveAction>().First(m => m.MoveIndex == 0);
        ActionExecutor.Apply(state, TestCards.Database, first);

        var remaining = Generate(state).OfType<UseMoveAction>().ToList();

        Assert.DoesNotContain(remaining, m => m.MoveIndex == 0);
    }

    [Fact]
    public void A_creatures_other_moves_stay_legal_after_one_is_used()
    {
        // Different moves are independent -- only the used one is locked out.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Spike).Resources(spike: 5))
            .Build();

        var first = Generate(state).OfType<UseMoveAction>().First(m => m.MoveIndex == 0);
        ActionExecutor.Apply(state, TestCards.Database, first);

        Assert.Contains(Generate(state).OfType<UseMoveAction>(), m => m.MoveIndex == 1);
    }

    [Fact]
    public void Move_usage_resets_at_the_turn_boundary()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Spike).Resources(spike: 5))
            .Build();

        ActionExecutor.Apply(
            state, TestCards.Database, Generate(state).OfType<UseMoveAction>().First());
        ActionExecutor.Apply(state, TestCards.Database, new EndTurnAction(PlayerId.One));

        // Back round to player one. Score/income sequencing belongs to the turn loop (step
        // 1.9), so the phase is set directly here.
        state.SetActivePlayer(PlayerId.One);
        state.SetPhase(TurnPhase.Actions);

        Assert.Equal(2, Generate(state).OfType<UseMoveAction>().Count());
    }

    [Fact]
    public void An_unaffordable_move_is_not_offered()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(spike: 5))
            .Build();

        Assert.DoesNotContain(Generate(state), a => a.Kind == ActionKind.UseMove);
    }

    [Fact]
    public void A_free_move_is_offered_to_a_player_with_no_resources()
    {
        // Zero-cost must not be mistaken for unaffordable -- an easy off-by-one in a Covers
        // check, and one that would silently delete a whole card archetype.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.FreeMove, TypeMask.Spike))
            .Build();

        Assert.Single(Generate(state).OfType<UseMoveAction>());
    }

    [Fact]
    public void A_stunned_creature_offers_no_moves()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Spike).Resources(spike: 5))
            .Build();

        state.Board[new SlotIndex(PlayerId.One, 0)]!.Stun();

        Assert.DoesNotContain(Generate(state), a => a.Kind == ActionKind.UseMove);
    }

    [Fact]
    public void Only_the_active_players_creatures_may_act()
    {
        var state = new StateBuilder()
            .P1(p => p.Resources(wheel: 5))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 5))
            .ActivePlayer(PlayerId.One)
            .Build();

        Assert.DoesNotContain(Generate(state), a => a.Kind == ActionKind.UseMove);
    }

    // -- Move conditions -------------------------------------------------------------------

    [Fact]
    public void A_move_whose_condition_is_met_is_legal()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Gated, TypeMask.Wheel, maxHealth: 2).Resources(wheel: 5))
            .Build();

        Assert.Single(Generate(state).OfType<UseMoveAction>());
    }

    [Fact]
    public void A_move_whose_condition_is_unmet_is_not_legal_at_all()
    {
        // Not "legal but resolves to nothing" -- see ConditionEvaluator. A move that cannot do
        // anything must not appear, or the search wastes iterations on edges that change
        // nothing and the console offers a visibly inert choice.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Gated, TypeMask.Wheel, maxHealth: 2, health: 1)
                .Resources(wheel: 5))
            .Build();

        Assert.Empty(Generate(state).OfType<UseMoveAction>());
    }

    // -- Chosen targets --------------------------------------------------------------------

    [Fact]
    public void A_targeted_move_expands_into_one_action_per_enemy()
    {
        // The single-target rule's payoff: N actions, not N x M. This is the expansion MCTS
        // budgets for.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Chooser, TypeMask.Anvil).Resources(anvil: 5))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel)
                .Slot(2, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var moves = Generate(state).OfType<UseMoveAction>().ToList();

        Assert.Equal(3, moves.Count);
        Assert.Equal(3, moves.Select(m => m.ChosenTarget).Distinct().Count());
    }

    [Fact]
    public void A_targeted_move_with_no_valid_target_generates_nothing()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Chooser, TypeMask.Anvil).Resources(anvil: 5))
            .Build();

        Assert.Empty(Generate(state).OfType<UseMoveAction>());
    }

    [Fact]
    public void Taunt_restricts_a_moves_chosen_enemy_targets()
    {
        // Taunt applies to creature-sourced effects. Enforcing it in generation rather than at
        // resolution is what makes it real for the AI: an illegal target is one it never sees.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Chooser, TypeMask.Anvil).Resources(anvil: 5))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel))
            .Build();

        var taunter = new SlotIndex(PlayerId.Two, 1);
        state.Board[taunter]!.GrantKeyword(KeywordFlags.Taunt);

        var move = Assert.Single(Generate(state).OfType<UseMoveAction>());

        Assert.Equal(taunter, move.ChosenTarget);
    }

    [Fact]
    public void Taunt_does_not_restrict_a_spell()
    {
        // A spell has no creature source, so there is nothing to be taunted away from.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.TargetedBolt).Resources(wheel: 5))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel))
            .Build();

        state.Board[new SlotIndex(PlayerId.Two, 1)]!.GrantKeyword(KeywordFlags.Taunt);

        Assert.Equal(2, Generate(state).OfType<PlayCardAction>().Count());
    }

    // -- Merging ---------------------------------------------------------------------------

    [Fact]
    public void Two_adjacent_friendly_creatures_can_merge_in_either_direction()
    {
        // Direction is a real choice, not a duplicate: the result occupies the target slot, and
        // which slot it sits in changes what it faces for scoring.
        var state = TwoFriendlies(0, 1);

        var merges = Generate(state).OfType<MergeAction>().ToList();

        Assert.Equal(2, merges.Count);
        Assert.Contains(merges, m => m.SourceSlot.Slot == 0 && m.TargetSlot.Slot == 1);
        Assert.Contains(merges, m => m.SourceSlot.Slot == 1 && m.TargetSlot.Slot == 0);
    }

    [Fact]
    public void Non_adjacent_creatures_cannot_merge()
    {
        var state = TwoFriendlies(0, 2);

        Assert.Empty(Generate(state).OfType<MergeAction>());
    }

    [Fact]
    public void A_creature_cannot_merge_with_an_enemy()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 5))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel))
            .Build();

        Assert.Empty(Generate(state).OfType<MergeAction>());
    }

    [Fact]
    public void A_creature_cannot_merge_with_itself()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 5))
            .Build();

        Assert.Empty(Generate(state).OfType<MergeAction>());
    }

    [Fact]
    public void An_already_merged_creature_cannot_merge_again()
    {
        // MaxMergeDepth of 2, checked against the COMBINED depth: a depth-2 creature plus a
        // depth-1 creature is 3, over the cap.
        var state = TwoFriendlies(0, 1);
        var merge = Generate(state).OfType<MergeAction>().First();
        ActionExecutor.Apply(state, TestCards.Database, merge);

        // Give the merged creature a fresh neighbour to attempt a second merge with.
        state.Board.Place(
            new SlotIndex(PlayerId.One, merge.TargetSlot.Slot == 0 ? 1 : 0),
            new Core.State.CreatureInstance(TestCards.Striker, 2, TypeMask.Wheel));

        Assert.Empty(Generate(state).OfType<MergeAction>());
    }

    [Fact]
    public void A_higher_merge_depth_cap_permits_a_second_merge()
    {
        // The cap is stated as a sum rather than as "neither may already be merged", which is
        // what makes a ruleset raising it behave sensibly instead of silently forbidding
        // everything.
        var rules = RuleSetTestHelper.WithMaxMergeDepth(3);

        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel)
                .Slot(2, TestCards.Striker, TypeMask.Wheel)
                .Resources(wheel: 5))
            .Build();

        var merge = Generate(state).OfType<MergeAction>()
            .First(m => m.SourceSlot.Slot == 0 && m.TargetSlot.Slot == 1);
        ActionExecutor.Apply(state, TestCards.Database, merge);

        Assert.NotEmpty(Generate(state).OfType<MergeAction>());
    }

    [Fact]
    public void Merging_is_absent_when_the_ruleset_disables_it()
    {
        var state = new StateBuilder()
            .WithRuleSet(RuleSetTestHelper.WithMergeEnabled(false))
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel)
                .Resources(wheel: 5))
            .Build();

        Assert.Empty(Generate(state).OfType<MergeAction>());
    }

    [Fact]
    public void Non_adjacent_merging_is_permitted_when_the_ruleset_allows_it()
    {
        var state = new StateBuilder()
            .WithRuleSet(RuleSetTestHelper.WithMergeRequiresAdjacent(false))
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(2, TestCards.Striker, TypeMask.Wheel)
                .Resources(wheel: 5))
            .Build();

        Assert.Equal(2, Generate(state).OfType<MergeAction>().Count());
    }

    // -- IsLegal ---------------------------------------------------------------------------

    [Fact]
    public void IsLegal_agrees_with_the_generated_list()
    {
        var state = WithHand(TestCards.Striker);

        foreach (var action in Generate(state))
        {
            Assert.True(ActionGenerator.IsLegal(state, TestCards.Database, action));
        }
    }

    [Fact]
    public void IsLegal_rejects_an_action_that_was_not_generated()
    {
        var state = WithHand(TestCards.Striker);

        var bogus = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0);

        Assert.False(ActionGenerator.IsLegal(state, TestCards.Database, bogus));
    }

    private static GameState TwoFriendlies(int a, int b) =>
        new StateBuilder()
            .P1(p => p
                .Slot(a, TestCards.Striker, TypeMask.Wheel)
                .Slot(b, TestCards.Striker, TypeMask.Wheel)
                .Resources(wheel: 5))
            .Build();
}
