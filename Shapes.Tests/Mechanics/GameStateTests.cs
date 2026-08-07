using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Mechanics;

// Scoring and income -- the two rules that drive the whole game, and the two the design notes
// flagged for Phase 4 measurement.
public class GameStateTests
{
    // These tests merge only to exercise income/scoring, never move indices, so the exact counts
    // do not matter -- AbsorbMerge just needs some lookup to shift the used-move bitmask by.
    private static int MoveCount(string cardId) => 2;

    // RuleSet has no with-support (plain class, not a record); this rebuilds Default with only
    // IncomePerCreatureType overridden, for the tests that specifically exercise per-creature
    // income now that the shipping default has it switched off.
    private static RuleSet WithIncomePerCreatureType(int incomePerCreatureType) => new(
        RuleSet.Default.Name,
        RuleSet.Default.StartingHandSize,
        RuleSet.Default.CardsDrawnPerTurn,
        RuleSet.Default.HandLimit,
        RuleSet.Default.BaseIncome,
        incomePerCreatureType,
        RuleSet.Default.PointsPerUnopposedCreature,
        RuleSet.Default.ScoreToWin,
        RuleSet.Default.MergeEnabled,
        RuleSet.Default.MergeRequiresAdjacent,
        RuleSet.Default.MergeCostsAction,
        RuleSet.Default.MaxMergeDepth,
        RuleSet.Default.DeckMode,
        RuleSet.Default.CopiesPerCard,
        RuleSet.Default.DeckSize,
        RuleSet.Default.MaxCopiesPerCard,
        RuleSet.Default.TypeChart);

    [Fact]
    public void Unopposed_creatures_score_one_point_each()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Spike))
            .Build();

        Assert.Equal(2, state.PendingScore(PlayerId.One));
    }

    [Fact]
    public void An_opposed_creature_does_not_score()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(0, "b", TypeMask.Spike))
            .Build();

        Assert.Equal(0, state.PendingScore(PlayerId.One));
        Assert.Equal(0, state.PendingScore(PlayerId.Two));
    }

    [Fact]
    public void Opposition_is_slot_for_slot_not_mirrored()
    {
        // P1 slot 0 vs P2 slot 2: under a mirrored board these would face each other and
        // neither would score. They must not.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(2, "b", TypeMask.Spike))
            .Build();

        Assert.Equal(1, state.PendingScore(PlayerId.One));
        Assert.Equal(1, state.PendingScore(PlayerId.Two));
    }

    [Fact]
    public void An_empty_board_scores_nothing()
    {
        var state = new StateBuilder().Build();

        Assert.Equal(0, state.PendingScore(PlayerId.One));
        Assert.Equal(0, state.PendingScore(PlayerId.Two));
    }

    // -- ScoreByCreatureDelta: a pure board-presence race, positioning irrelevant ----------------

    [Fact]
    public void Creature_delta_scores_the_net_creature_count_advantage()
    {
        var rules = RuleSetTestHelper.WithScoreByCreatureDelta(true);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Spike).Slot(2, "c", TypeMask.Anvil))
            .P2(p => p.Slot(0, "d", TypeMask.Spike))
            .Build();

        // 3 vs 1 -> delta 2, regardless of which slots are individually opposed (P1 slot 0 IS
        // opposed by P2 slot 0 here, and still counts toward the 3).
        Assert.Equal(2, state.PendingScore(PlayerId.One));
        Assert.Equal(0, state.PendingScore(PlayerId.Two));
    }

    [Fact]
    public void Creature_delta_never_goes_negative()
    {
        var rules = RuleSetTestHelper.WithScoreByCreatureDelta(true);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(0, "b", TypeMask.Spike).Slot(1, "c", TypeMask.Anvil))
            .Build();

        // P1 has fewer creatures than P2 -- P1 must score 0, not a negative number.
        Assert.Equal(0, state.PendingScore(PlayerId.One));
        Assert.Equal(1, state.PendingScore(PlayerId.Two));
    }

    [Fact]
    public void Creature_delta_scores_zero_on_a_mirrored_board()
    {
        var rules = RuleSetTestHelper.WithScoreByCreatureDelta(true);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Spike))
            .P2(p => p.Slot(0, "c", TypeMask.Spike).Slot(2, "d", TypeMask.Anvil))
            .Build();

        // 2 vs 2 -- equal counts, even though the occupied slots don't line up 1:1.
        Assert.Equal(0, state.PendingScore(PlayerId.One));
        Assert.Equal(0, state.PendingScore(PlayerId.Two));
    }

    [Fact]
    public void Creature_delta_respects_the_ruleset_multiplier()
    {
        var rules = new RuleSet(
            "delta-double", 4, 1, 8, new ResourcePool(1, 1, 1), 1,
            pointsPerUnopposedCreature: 3, scoreToWin: 10,
            true, true, false, 2, DeckMode.Symmetric, 2, 0, 0, TypeChart.Default,
            scoreByCreatureDelta: true);

        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Spike))
            .Build();

        // Delta 2 x multiplier 3 -- confirms the delta path multiplies rather than hard-coding
        // 1 point per net creature.
        Assert.Equal(6, state.PendingScore(PlayerId.One));
    }

    [Fact]
    public void Scoring_respects_the_ruleset_multiplier()
    {
        var rules = new RuleSet(
            "double", 4, 1, 8, new ResourcePool(1, 1, 1), 1,
            pointsPerUnopposedCreature: 2, scoreToWin: 10,
            true, true, false, 2, DeckMode.Symmetric, 2, 0, 0, TypeChart.Default);

        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .Build();

        Assert.Equal(2, state.PendingScore(PlayerId.One));
    }

    [Fact]
    public void ApplyScoring_adds_to_the_active_players_score()
    {
        // The deck is stocked only so fatigue (step 5b) stays out of this test's way -- an empty
        // deck would hand P2 a point in the same step and make the P2 assertion below about
        // fatigue rather than about unopposed scoring.
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Score(3).Deck("basic_t"))
            .Build();

        state.ApplyScoring();

        Assert.Equal(4, state[PlayerId.One].Score);
        Assert.Equal(0, state[PlayerId.Two].Score);
    }

    // -- Fatigue (PLAN.md step 5b) --------------------------------------------------------------

    [Fact]
    public void Fatigue_scores_for_the_OPPONENT_of_the_player_who_ran_out_of_cards()
    {
        // Running out of cards is a cost, not a reward -- the seat that decked out hands score to
        // the other one. Getting this backwards would still terminate games, which is exactly why
        // it is worth pinning: the bug would look like the feature working.
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Score(0))
            .P2(p => p.Score(0))
            .Build();

        Assert.True(state[PlayerId.One].DeckIsEmpty);

        state.ApplyScoring();

        Assert.Equal(0, state[PlayerId.One].Score);
        Assert.Equal(1, state[PlayerId.Two].Score);
    }

    [Fact]
    public void Fatigue_stacks_with_normal_unopposed_scoring_rather_than_replacing_it()
    {
        // Both score sources resolve in the same step; fatigue is additive, not an alternative.
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .Build();

        state.ApplyScoring();

        Assert.Equal(1, state[PlayerId.One].Score);  // its own unopposed creature
        Assert.Equal(1, state[PlayerId.Two].Score);  // P1's empty deck
    }

    [Fact]
    public void A_player_with_cards_left_concedes_no_fatigue()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Deck("basic_t", "basic_t"))
            .Build();

        Assert.False(state.IsFatigued(PlayerId.One));

        state.ApplyScoring();

        Assert.Equal(0, state[PlayerId.Two].Score);
    }

    [Fact]
    public void Fatigue_score_of_zero_disables_the_rule_entirely()
    {
        // Every balance run before step 5b was played without fatigue, so those rulesets have to
        // stay reproducible rather than silently gaining a new score source.
        var state = new StateBuilder()
            .WithRuleSet(WithFatigue(0))
            .ActivePlayer(PlayerId.One)
            .Build();

        Assert.True(state[PlayerId.One].DeckIsEmpty);
        Assert.False(state.IsFatigued(PlayerId.One));

        state.ApplyScoring();

        Assert.Equal(0, state[PlayerId.Two].Score);
    }

    [Fact]
    public void Fatigue_logs_the_seat_that_ran_out_not_the_seat_that_scored()
    {
        // The batch runner reads this event to build per-seat deck-exhaustion rates, so which
        // seat it names is the whole meaning of the metric.
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.Two)
            .Build();

        state.ApplyScoring();

        var fatigue = state.TurnEvents.Where(e => e.Kind == TurnEventKind.Fatigued).ToList();
        Assert.Single(fatigue);
        Assert.Equal(PlayerId.Two, fatigue[0].Player);
    }

    [Fact]
    public void Fatigue_eventually_ends_a_game_neither_player_can_score_in()
    {
        // The reason the rule exists: an empty board scores nothing under the unopposed rule, so
        // without fatigue this state would loop forever (PLAN.md step 5b's 501-turn game). Driving
        // the real turn loop rather than asserting on the rule in isolation is the point.
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .Build();

        var guard = 0;
        while (!state.IsOver && guard++ < 500)
        {
            state.AdvanceToActions();
            state.EndTurn();
        }

        Assert.True(state.IsOver);
        Assert.NotNull(state.Winner);
    }

    private static RuleSet WithFatigue(int fatigueScorePerTurn) => new(
        RuleSet.Default.Name,
        RuleSet.Default.StartingHandSize,
        RuleSet.Default.CardsDrawnPerTurn,
        RuleSet.Default.HandLimit,
        RuleSet.Default.BaseIncome,
        RuleSet.Default.IncomePerCreatureType,
        RuleSet.Default.PointsPerUnopposedCreature,
        RuleSet.Default.ScoreToWin,
        RuleSet.Default.MergeEnabled,
        RuleSet.Default.MergeRequiresAdjacent,
        RuleSet.Default.MergeCostsAction,
        RuleSet.Default.MaxMergeDepth,
        RuleSet.Default.DeckMode,
        RuleSet.Default.CopiesPerCard,
        RuleSet.Default.DeckSize,
        RuleSet.Default.MaxCopiesPerCard,
        RuleSet.Default.TypeChart,
        RuleSet.Default.ScoreByCreatureDelta,
        fatigueScorePerTurn);

    [Fact]
    public void Base_income_arrives_with_an_empty_board()
    {
        var state = new StateBuilder().Build();

        Assert.Equal(new ResourcePool(2, 2, 2), state.PendingIncome(PlayerId.One));
    }

    [Fact]
    public void Each_creature_adds_income_of_its_own_type()
    {
        var rules = WithIncomePerCreatureType(1);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Spike).Slot(1, "b", TypeMask.Spike))
            .Build();

        // base 2/2/2 plus two spike creatures, with per-creature income opted back in
        Assert.Equal(new ResourcePool(4, 2, 2), state.PendingIncome(PlayerId.One));
    }

    [Fact]
    public void A_merged_creature_generates_one_resource_per_type()
    {
        // The compounding the design notes flagged: mixing types buys extra income, paid for
        // with a 2x defensive exposure.
        var rules = WithIncomePerCreatureType(1);
        var merged = new CreatureInstance("a", 3, TypeMask.Spike);
        merged.AbsorbMerge(new CreatureInstance("b", 3, TypeMask.Wheel), MoveCount);

        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, merged))
            .Build();

        Assert.Equal(new ResourcePool(3, 2, 3), state.PendingIncome(PlayerId.One));
    }

    [Fact]
    public void Income_per_creature_can_be_switched_off()
    {
        // A legitimate Phase 4 sweep: isolates how much of the runaway-leader effect comes
        // from creature income rather than scoring.
        var rules = new RuleSet(
            "flat", 4, 1, 8, new ResourcePool(1, 1, 1),
            incomePerCreatureType: 0,
            pointsPerUnopposedCreature: 1, scoreToWin: 10,
            true, true, false, 2, DeckMode.Symmetric, 2, 0, 0, TypeChart.Default);

        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Spike).Slot(1, "b", TypeMask.Spike))
            .Build();

        Assert.Equal(new ResourcePool(1, 1, 1), state.PendingIncome(PlayerId.One));
    }

    [Fact]
    public void Only_the_owners_creatures_count_toward_income()
    {
        var rules = WithIncomePerCreatureType(1);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Slot(0, "a", TypeMask.Spike))
            .P2(p => p.Slot(1, "b", TypeMask.Anvil))
            .Build();

        Assert.Equal(new ResourcePool(3, 2, 2), state.PendingIncome(PlayerId.One));
        Assert.Equal(new ResourcePool(2, 3, 2), state.PendingIncome(PlayerId.Two));
    }

    [Fact]
    public void Ending_a_turn_passes_play_and_clears_move_usage()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .Build();

        state.Board[new SlotIndex(PlayerId.One, 0)]!.MarkMoveUsed(0);
        state.EndTurn();

        Assert.Equal(PlayerId.Two, state.ActivePlayer);
        Assert.Equal(TurnPhase.Scoring, state.Phase);
        Assert.False(state.Board[new SlotIndex(PlayerId.One, 0)]!.HasUsedMove(0));
    }

    [Fact]
    public void Turn_number_advances_when_play_returns_to_player_one()
    {
        var state = new StateBuilder().ActivePlayer(PlayerId.One).Build();

        Assert.Equal(1, state.TurnNumber);

        state.EndTurn();
        Assert.Equal(1, state.TurnNumber);  // P2's half of the round

        state.EndTurn();
        Assert.Equal(2, state.TurnNumber);
    }

    [Fact]
    public void Game_is_over_once_a_player_reaches_the_win_score()
    {
        var state = new StateBuilder()
            .P1(p => p.Score(RuleSet.Default.ScoreToWin))
            .Build();

        Assert.True(state.IsOver);
        Assert.Equal(PlayerId.One, state.Winner);
    }

    [Fact]
    public void Game_is_not_over_below_the_win_score()
    {
        var state = new StateBuilder()
            .P1(p => p.Score(RuleSet.Default.ScoreToWin - 1))
            .Build();

        Assert.False(state.IsOver);
        Assert.Null(state.Winner);
    }

    // -- Turn loop: score -> income -> actions ----------------------------------------------

    [Fact]
    public void A_freshly_built_game_starts_in_the_scoring_phase()
    {
        // Turn one runs the same score -> income -> actions sequence as every later turn --
        // scoring an empty board is simply a no-op, rather than turn one being a special case
        // that skips straight to Actions.
        var state = new StateBuilder().Phase(TurnPhase.Scoring).Build();

        Assert.Equal(TurnPhase.Scoring, state.Phase);
    }

    [Fact]
    public void AdvanceToActions_runs_scoring_then_income_and_lands_in_actions()
    {
        var state = new StateBuilder()
            .Phase(TurnPhase.Scoring)
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Score(3))
            .Build();

        state.AdvanceToActions();

        Assert.Equal(TurnPhase.Actions, state.Phase);
        Assert.Equal(4, state[PlayerId.One].Score);
        Assert.Equal(new ResourcePool(2, 2, 2), state[PlayerId.One].Resources);
    }

    [Fact]
    public void AdvanceToActions_from_income_only_runs_income()
    {
        var state = new StateBuilder()
            .Phase(TurnPhase.Income)
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Score(3))
            .Build();

        state.AdvanceToActions();

        Assert.Equal(TurnPhase.Actions, state.Phase);
        // Scoring must not have re-run from the Income phase -- score stays at 3, not 4.
        Assert.Equal(3, state[PlayerId.One].Score);
    }

    [Fact]
    public void AdvanceToActions_is_a_no_op_once_already_in_actions()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Score(3).Resources(spike: 2))
            .Build();

        state.AdvanceToActions();

        Assert.Equal(TurnPhase.Actions, state.Phase);
        Assert.Equal(3, state[PlayerId.One].Score);
        Assert.Equal(new ResourcePool(2, 0, 0), state[PlayerId.One].Resources);
    }

    [Fact]
    public void Scoring_into_a_win_stops_before_income_runs()
    {
        // The win check must land between scoring and income: a player who scores past the
        // threshold wins immediately, and no further phase (income, let alone actions) should
        // execute on a decided game.
        var state = new StateBuilder()
            .Phase(TurnPhase.Scoring)
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Score(RuleSet.Default.ScoreToWin - 1))
            .Build();

        state.AdvanceToActions();

        Assert.Equal(TurnPhase.Ended, state.Phase);
        Assert.True(state.IsOver);
        Assert.Equal(PlayerId.One, state.Winner);
        // Income never ran: base income would otherwise have landed.
        Assert.Equal(ResourcePool.Empty, state[PlayerId.One].Resources);
    }

    [Fact]
    public void Clone_is_fully_independent()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel, maxHealth: 5).Resources(spike: 2).Score(1).Hand("x"))
            .Build();

        var copy = state.Clone();

        copy[PlayerId.One].AddScore(5);
        copy[PlayerId.One].GainResource(ResourceType.Spike, 10);
        copy.Board[new SlotIndex(PlayerId.One, 0)]!.TakeDamage(3);
        copy[PlayerId.One].AddToHand("y");

        Assert.Equal(1, state[PlayerId.One].Score);
        Assert.Equal(2, state[PlayerId.One].Resources.Spike);
        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.One, 0)]!.Health);
        Assert.Single(state[PlayerId.One].Hand);
    }

    [Fact]
    public void Clone_forks_the_random_source_rather_than_sharing_it()
    {
        // A search rollout on a clone must not advance the real game's stream, or replaying
        // the same seed would stop reproducing the same game.
        var state = new StateBuilder().WithSeed(42).Build();
        var copy = state.Clone();

        for (var i = 0; i < 50; i++)
        {
            _ = copy.Random.Next(100);
        }

        var fresh = new SeededRandom(42);
        Assert.Equal(fresh.Next(100), state.Random.Next(100));
    }
}
