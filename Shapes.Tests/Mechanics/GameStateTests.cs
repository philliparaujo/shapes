using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Mechanics;

// Scoring and income -- the two rules that drive the whole game, and the two the design notes
// flagged for Phase 4 measurement.
public class GameStateTests
{
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
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Score(3))
            .Build();

        state.ApplyScoring();

        Assert.Equal(4, state[PlayerId.One].Score);
        Assert.Equal(0, state[PlayerId.Two].Score);
    }

    [Fact]
    public void Base_income_arrives_with_an_empty_board()
    {
        var state = new StateBuilder().Build();

        Assert.Equal(new ResourcePool(1, 1, 1), state.PendingIncome(PlayerId.One));
    }

    [Fact]
    public void Each_creature_adds_income_of_its_own_type()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Spike).Slot(1, "b", TypeMask.Spike))
            .Build();

        // base 1/1/1 plus two spike creatures
        Assert.Equal(new ResourcePool(3, 1, 1), state.PendingIncome(PlayerId.One));
    }

    [Fact]
    public void A_merged_creature_generates_one_resource_per_type()
    {
        // The compounding the design notes flagged: mixing types buys extra income, paid for
        // with a 2x defensive exposure.
        var merged = new CreatureInstance("a", 3, TypeMask.Spike);
        merged.AbsorbMerge(new CreatureInstance("b", 3, TypeMask.Wheel));

        var state = new StateBuilder()
            .P1(p => p.Slot(0, merged))
            .Build();

        Assert.Equal(new ResourcePool(2, 1, 2), state.PendingIncome(PlayerId.One));
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
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Spike))
            .P2(p => p.Slot(1, "b", TypeMask.Anvil))
            .Build();

        Assert.Equal(new ResourcePool(2, 1, 1), state.PendingIncome(PlayerId.One));
        Assert.Equal(new ResourcePool(1, 2, 1), state.PendingIncome(PlayerId.Two));
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
        Assert.Equal(new ResourcePool(1, 1, 2), state[PlayerId.One].Resources);
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
