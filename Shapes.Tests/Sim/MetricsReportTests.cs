using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Sim;

namespace Shapes.Tests.Sim;

// PLAN.md Phase 4 step 1: win rate by seat, game length, per-card play/draw/win-rate correlation,
// move usage, merge frequency, resource flow, ending type -- plus step 3's prerequisites
// (confidence intervals, opportunity denominators, score margin). Testing is brief per the step's
// own scope -- these pin the aggregation arithmetic against small, literal GameResults rather than
// exercising GameRunner end to end (already covered by GameRunnerTests).
public class MetricsReportTests
{
    private static GameResult MakeGame(
        PlayerId? winner,
        EndingType ending = EndingType.ScoreThreshold,
        IReadOnlyList<string>? cardsPlayedOne = null,
        IReadOnlyList<string>? cardsPlayedTwo = null,
        IReadOnlyList<string>? cardsDrawnOne = null,
        IReadOnlyList<string>? cardsDrawnTwo = null,
        IReadOnlyList<(string CardId, string MoveName)>? movesUsedOne = null,
        IReadOnlyList<(string CardId, string MoveName)>? movesUsedTwo = null,
        int creaturesPlayedOne = 0,
        int creaturesPlayedTwo = 0,
        int mergeCountOne = 0,
        int mergeCountTwo = 0,
        ResourcePool? finalOne = null,
        ResourcePool? finalTwo = null,
        int turnCount = 5,
        int scoreOne = 0,
        int scoreTwo = 0,
        IReadOnlyDictionary<string, int>? cardOffersOne = null,
        IReadOnlyDictionary<string, int>? cardOffersTwo = null,
        IReadOnlyDictionary<string, int>? moveOffersOne = null,
        IReadOnlyDictionary<string, int>? moveOffersTwo = null,
        int mergeOffersOne = 0,
        int mergeOffersTwo = 0,
        IReadOnlyList<int>? scoreMarginByTurn = null,
        IReadOnlyList<ResourcePool>? resourcesByTurnOne = null,
        IReadOnlyList<ResourcePool>? resourcesByTurnTwo = null,
        int unopposedSlotTurnsOne = 0,
        int unopposedSlotTurnsTwo = 0,
        int scoringStepsOne = 0,
        int scoringStepsTwo = 0,
        int longestUnopposedStreakOne = 0,
        int longestUnopposedStreakTwo = 0,
        IReadOnlyList<CreatureLifetime>? creatureSurvivalOne = null,
        IReadOnlyList<CreatureLifetime>? creatureSurvivalTwo = null,
        IReadOnlyDictionary<string, int>? cardsBlockedByCostOne = null,
        IReadOnlyDictionary<string, int>? cardsBlockedByCostTwo = null) =>
        new()
        {
            AgentOne = "random",
            AgentTwo = "random",
            Seed = 1,
            Winner = winner,
            Ending = ending,
            ScoreOne = scoreOne,
            ScoreTwo = scoreTwo,
            TurnCount = turnCount,
            ActionCount = (movesUsedOne?.Count ?? 0) + (movesUsedTwo?.Count ?? 0) + 1,
            ActionCountsByKind = new Dictionary<ActionKind, int>
            {
                [ActionKind.UseMove] = (movesUsedOne?.Count ?? 0) + (movesUsedTwo?.Count ?? 0),
                [ActionKind.EndTurn] = 1,
            },
            CardsPlayedOne = cardsPlayedOne ?? [],
            CardsPlayedTwo = cardsPlayedTwo ?? [],
            CardsDrawnOne = cardsDrawnOne ?? [],
            CardsDrawnTwo = cardsDrawnTwo ?? [],
            MovesUsedOne = movesUsedOne ?? [],
            MovesUsedTwo = movesUsedTwo ?? [],
            CreaturesPlayedOne = creaturesPlayedOne,
            CreaturesPlayedTwo = creaturesPlayedTwo,
            MergeCountOne = mergeCountOne,
            MergeCountTwo = mergeCountTwo,
            FinalResourcesOne = finalOne ?? ResourcePool.Empty,
            FinalResourcesTwo = finalTwo ?? ResourcePool.Empty,
            CardOffersOne = cardOffersOne ?? new Dictionary<string, int>(),
            CardOffersTwo = cardOffersTwo ?? new Dictionary<string, int>(),
            MoveOffersOne = moveOffersOne ?? new Dictionary<string, int>(),
            MoveOffersTwo = moveOffersTwo ?? new Dictionary<string, int>(),
            MergeOffersOne = mergeOffersOne,
            MergeOffersTwo = mergeOffersTwo,
            ScoreMarginByTurn = scoreMarginByTurn ?? [],
            ResourcesByTurnOne = resourcesByTurnOne ?? [],
            ResourcesByTurnTwo = resourcesByTurnTwo ?? [],
            UnopposedSlotTurnsOne = unopposedSlotTurnsOne,
            UnopposedSlotTurnsTwo = unopposedSlotTurnsTwo,
            ScoringStepsOne = scoringStepsOne,
            ScoringStepsTwo = scoringStepsTwo,
            LongestUnopposedStreakOne = longestUnopposedStreakOne,
            LongestUnopposedStreakTwo = longestUnopposedStreakTwo,
            CreatureSurvivalOne = creatureSurvivalOne ?? [],
            CreatureSurvivalTwo = creatureSurvivalTwo ?? [],
            CardsBlockedByCostOne = cardsBlockedByCostOne ?? new Dictionary<string, int>(),
            CardsBlockedByCostTwo = cardsBlockedByCostTwo ?? new Dictionary<string, int>(),
            Elapsed = TimeSpan.Zero,
        };

    [Fact]
    public void Seat_win_rates_are_reported_separately_not_pooled()
    {
        var games = new[]
        {
            MakeGame(PlayerId.One),
            MakeGame(PlayerId.One),
            MakeGame(PlayerId.Two),
            MakeGame(PlayerId.Two),
            MakeGame(PlayerId.Two),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(0.4, metrics.SeatOneWinRate.Rate, precision: 5);
        Assert.Equal(0.6, metrics.SeatTwoWinRate.Rate, precision: 5);
    }

    [Fact]
    public void Nonterminating_games_count_toward_neither_seats_win_rate()
    {
        var games = new[]
        {
            MakeGame(PlayerId.One),
            MakeGame(null, EndingType.NonTerminating),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(0.5, metrics.SeatOneWinRate.Rate, precision: 5);
        Assert.Equal(0.0, metrics.SeatTwoWinRate.Rate, precision: 5);
        Assert.Equal(1, metrics.EndingCounts[EndingType.NonTerminating]);
        Assert.Equal(1, metrics.EndingCounts[EndingType.ScoreThreshold]);
    }

    [Fact]
    public void Card_stats_count_plays_and_win_rate_when_played()
    {
        var games = new[]
        {
            MakeGame(PlayerId.One, cardsPlayedOne: ["spike_striker", "spike_striker"]),
            MakeGame(PlayerId.Two, cardsPlayedOne: ["spike_striker"]),
            MakeGame(PlayerId.One, cardsPlayedTwo: ["anvil_ward"]),
        };

        var metrics = MetricsReport.From(games);

        var spike = metrics.CardStats.Single(c => c.CardId == "spike_striker");
        Assert.Equal(3, spike.PlayCount);
        Assert.Equal(2, spike.GamesPlayedIn);
        Assert.Equal(1, spike.WinsWhenPlayed);
        Assert.Equal(0.5, spike.WinRateWhenPlayed.Rate, precision: 5);

        var anvil = metrics.CardStats.Single(c => c.CardId == "anvil_ward");
        Assert.Equal(1, anvil.PlayCount);
        Assert.Equal(0, anvil.WinsWhenPlayed);
    }

    [Fact]
    public void Card_stats_track_draw_win_rate_separately_from_play_win_rate()
    {
        // "spike_striker" is drawn in three games but only ever played in one of them -- the
        // draw and play win rates must diverge, proving they're independently tracked rather
        // than one derived from the other.
        var games = new[]
        {
            MakeGame(PlayerId.One, cardsDrawnOne: ["spike_striker"], cardsPlayedOne: ["spike_striker"]),
            MakeGame(PlayerId.Two, cardsDrawnOne: ["spike_striker"]),
            MakeGame(PlayerId.Two, cardsDrawnOne: ["spike_striker"]),
        };

        var metrics = MetricsReport.From(games);

        var spike = metrics.CardStats.Single(c => c.CardId == "spike_striker");
        Assert.Equal(3, spike.GamesDrawnIn);
        Assert.Equal(1, spike.WinsWhenDrawn);
        Assert.Equal(1.0 / 3.0, spike.WinRateWhenDrawn.Rate, precision: 5);
        Assert.Equal(1, spike.GamesPlayedIn);
        Assert.Equal(1, spike.WinsWhenPlayed);
        Assert.Equal(1.0, spike.WinRateWhenPlayed.Rate, precision: 5);
    }

    [Fact]
    public void Duplicate_copies_drawn_in_one_game_count_that_game_once()
    {
        var games = new[]
        {
            MakeGame(PlayerId.One, cardsDrawnOne: ["spike_striker", "spike_striker", "spike_striker"]),
        };

        var metrics = MetricsReport.From(games);

        var spike = metrics.CardStats.Single(c => c.CardId == "spike_striker");
        Assert.Equal(3, spike.TimesDrawn);
        Assert.Equal(1, spike.GamesDrawnIn);
        Assert.Equal(1, spike.WinsWhenDrawn);
    }

    [Fact]
    public void Move_stats_count_uses_and_win_rate_when_used()
    {
        var games = new[]
        {
            MakeGame(PlayerId.One, movesUsedOne: [("cadet", "Slash"), ("cadet", "Slash")]),
            MakeGame(PlayerId.Two, movesUsedOne: [("cadet", "Slash")]),
            MakeGame(PlayerId.One, movesUsedTwo: [("medic", "Guard")]),
        };

        var metrics = MetricsReport.From(games);

        var slash = metrics.MoveStats.Single(m => m.MoveName == "Slash");
        Assert.Equal("cadet", slash.CardId);
        Assert.Equal(3, slash.UseCount);
        Assert.Equal(2, slash.GamesUsedIn);
        Assert.Equal(1, slash.WinsWhenUsed);
        Assert.Equal(0.5, slash.WinRateWhenUsed.Rate, precision: 5);

        var guard = metrics.MoveStats.Single(m => m.MoveName == "Guard");
        Assert.Equal("medic", guard.CardId);
        Assert.Equal(1, guard.UseCount);
        Assert.Equal(0, guard.WinsWhenUsed);
    }

    [Fact]
    public void Move_stats_keep_the_same_move_name_from_different_cards_separate()
    {
        // Two different cards happening to share a move name must not collapse into one stat --
        // CardId is part of the identity precisely so this can't happen silently.
        var games = new[]
        {
            MakeGame(PlayerId.One, movesUsedOne: [("cadet", "Strike")]),
            MakeGame(PlayerId.Two, movesUsedOne: [("monk", "Strike")]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(2, metrics.MoveStats.Count(m => m.MoveName == "Strike"));
        var cadetStrike = metrics.MoveStats.Single(m => m.CardId == "cadet" && m.MoveName == "Strike");
        var monkStrike = metrics.MoveStats.Single(m => m.CardId == "monk" && m.MoveName == "Strike");
        Assert.Equal(1, cadetStrike.UseCount);
        Assert.Equal(1, monkStrike.UseCount);
    }

    [Fact]
    public void Merge_and_move_usage_are_summed_across_both_seats()
    {
        var games = new[]
        {
            MakeGame(
                PlayerId.One, mergeCountOne: 1, mergeCountTwo: 2,
                movesUsedOne: [("a", "x"), ("b", "y"), ("c", "z")]),
            MakeGame(PlayerId.Two, mergeCountOne: 0, mergeCountTwo: 1, movesUsedOne: [("a", "x")]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(4, metrics.MergeCount);
        Assert.Equal(2.0, metrics.MergesPerGame, precision: 5);
        Assert.Equal(4, metrics.MoveUsageCount);
    }

    [Fact]
    public void Merges_per_creature_played_normalizes_by_opportunity()
    {
        var games = new[]
        {
            MakeGame(
                PlayerId.One, mergeCountOne: 3, mergeCountTwo: 0,
                creaturesPlayedOne: 6, creaturesPlayedTwo: 0),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(3, metrics.MergeCount);
        Assert.Equal(6, metrics.CreaturesPlayedCount);
        Assert.Equal(0.5, metrics.MergesPerCreaturePlayed, precision: 5);
    }

    [Fact]
    public void Merges_per_creature_played_is_zero_not_a_divide_by_zero_when_no_creatures_played()
    {
        var games = new[] { MakeGame(PlayerId.One) };

        var metrics = MetricsReport.From(games);

        Assert.Equal(0.0, metrics.MergesPerCreaturePlayed, precision: 5);
    }

    [Fact]
    public void Resource_profiles_split_winner_from_loser_rather_than_averaging_them_together()
    {
        // The whole reason for the split: a single pooled average of these two seats would
        // report spike=5, describing neither the winner (who ran at 9) nor the loser (who ran
        // at 1). Seat one wins here, so seat one's samples must land in the WINNER profile
        // regardless of which seat index they came from.
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                resourcesByTurnOne: [new ResourcePool(9, 0, 0), new ResourcePool(9, 0, 0)],
                resourcesByTurnTwo: [new ResourcePool(1, 0, 0), new ResourcePool(1, 0, 0)]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(9.0, metrics.ResourcesWinners.Spike.Mean, precision: 5);
        Assert.Equal(1.0, metrics.ResourcesLosers.Spike.Mean, precision: 5);
    }

    [Fact]
    public void Resource_profiles_are_also_available_by_seat_independent_of_outcome()
    {
        // Seat two wins, so the seat profiles and the winner/loser profiles must disagree --
        // proving the seat split isn't just the outcome split relabelled.
        var games = new[]
        {
            MakeGame(
                PlayerId.Two,
                resourcesByTurnOne: [new ResourcePool(8, 0, 0)],
                resourcesByTurnTwo: [new ResourcePool(2, 0, 0)]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(8.0, metrics.ResourcesSeatOne.Spike.Mean, precision: 5);
        Assert.Equal(2.0, metrics.ResourcesSeatTwo.Spike.Mean, precision: 5);
        Assert.Equal(2.0, metrics.ResourcesWinners.Spike.Mean, precision: 5);
    }

    [Fact]
    public void Nonterminating_games_contribute_to_seat_profiles_but_to_neither_outcome_profile()
    {
        // A game with no winner has no winner's curve to add. It must not silently fall into
        // one bucket or the other, but its seat data is still real and still counted.
        var games = new[]
        {
            MakeGame(
                null, EndingType.NonTerminating,
                resourcesByTurnOne: [new ResourcePool(7, 0, 0)],
                resourcesByTurnTwo: [new ResourcePool(7, 0, 0)]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(0, metrics.ResourcesWinners.Spike.Count);
        Assert.Equal(0, metrics.ResourcesLosers.Spike.Count);
        Assert.Equal(1, metrics.ResourcesSeatOne.Spike.Count);
        Assert.Equal(1, metrics.ResourcesSeatTwo.Spike.Count);
    }

    [Fact]
    public void Cards_drawn_per_game_splits_winner_from_loser_rather_than_averaging_them_together()
    {
        // Same reasoning as the resource-profile split: a pooled average here would report
        // 3.5, describing neither the winner (2 draws) nor the loser (5 draws).
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                cardsDrawnOne: ["a", "b"],
                cardsDrawnTwo: ["a", "b", "c", "d", "e"]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(2.0, metrics.CardsDrawnWinners.Mean, precision: 5);
        Assert.Equal(5.0, metrics.CardsDrawnLosers.Mean, precision: 5);
    }

    [Fact]
    public void Cards_drawn_per_game_is_also_available_by_seat_independent_of_outcome()
    {
        // Seat two wins, so the seat split and the winner/loser split must disagree -- proving
        // the seat split isn't just the outcome split relabelled.
        var games = new[]
        {
            MakeGame(
                PlayerId.Two,
                cardsDrawnOne: ["a", "b", "c"],
                cardsDrawnTwo: ["a"]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(3.0, metrics.CardsDrawnSeatOne.Mean, precision: 5);
        Assert.Equal(1.0, metrics.CardsDrawnSeatTwo.Mean, precision: 5);
        Assert.Equal(1.0, metrics.CardsDrawnWinners.Mean, precision: 5);
    }

    [Fact]
    public void Nonterminating_games_contribute_cards_drawn_to_seat_profiles_but_to_neither_outcome_profile()
    {
        var games = new[]
        {
            MakeGame(
                null, EndingType.NonTerminating,
                cardsDrawnOne: ["a", "b"],
                cardsDrawnTwo: ["a", "b"]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(0, metrics.CardsDrawnWinners.Count);
        Assert.Equal(0, metrics.CardsDrawnLosers.Count);
        Assert.Equal(1, metrics.CardsDrawnSeatOne.Count);
        Assert.Equal(1, metrics.CardsDrawnSeatTwo.Count);
    }

    [Fact]
    public void Card_take_rate_divides_plays_by_the_times_the_play_was_legal()
    {
        // The metric raw PlayCount cannot express: "dud" is offered constantly and almost never
        // taken, "staple" is offered rarely and taken every time. By play count alone dud looks
        // like the more popular card (2 plays vs. 1); by take rate the ordering inverts, which
        // is the whole point of carrying the denominator.
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                cardsPlayedOne: ["dud", "dud", "staple"],
                cardOffersOne: new Dictionary<string, int> { ["dud"] = 100, ["staple"] = 1 }),
        };

        var metrics = MetricsReport.From(games);

        var dud = metrics.CardStats.Single(c => c.CardId == "dud");
        var staple = metrics.CardStats.Single(c => c.CardId == "staple");

        Assert.Equal(100, dud.OfferCount);
        Assert.Equal(0.02, dud.PlayTakeRate.Rate, precision: 5);
        Assert.Equal(1.0, staple.PlayTakeRate.Rate, precision: 5);
        Assert.True(staple.PlayTakeRate.Rate > dud.PlayTakeRate.Rate);
        Assert.True(dud.PlayCount > staple.PlayCount);
    }

    [Fact]
    public void A_card_offered_but_never_played_still_appears_with_a_zero_take_rate()
    {
        // Step 4.5's "never-played card" watch item. A card absent from every played list would
        // vanish from the report entirely if offers weren't part of the key set -- and a card
        // that never appears is precisely the one balance work needs to see.
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                cardOffersOne: new Dictionary<string, int> { ["ignored"] = 50 }),
        };

        var metrics = MetricsReport.From(games);

        var ignored = metrics.CardStats.Single(c => c.CardId == "ignored");
        Assert.Equal(0, ignored.PlayCount);
        Assert.Equal(50, ignored.OfferCount);
        Assert.Equal(0.0, ignored.PlayTakeRate.Rate, precision: 5);

        // Wilson, not the normal approximation: a 0/50 rate must still carry a non-zero upper
        // bound rather than collapsing to a falsely certain [0, 0].
        Assert.True(ignored.PlayTakeRate.High > 0.0);
    }

    [Fact]
    public void Move_take_rate_divides_uses_by_the_times_the_move_was_legal()
    {
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                movesUsedOne: [("cadet", "Slash"), ("cadet", "Slash")],
                moveOffersOne: new Dictionary<string, int> { [MoveKey.Of("cadet", "Slash")] = 8 }),
        };

        var metrics = MetricsReport.From(games);

        var slash = metrics.MoveStats.Single(m => m.MoveName == "Slash");
        Assert.Equal(8, slash.OfferCount);
        Assert.Equal(0.25, slash.UseTakeRate.Rate, precision: 5);
    }

    [Fact]
    public void Merge_take_rate_divides_merges_by_the_decisions_where_a_merge_was_legal()
    {
        var games = new[]
        {
            MakeGame(PlayerId.One, mergeCountOne: 3, mergeOffersOne: 10),
            MakeGame(PlayerId.Two, mergeCountTwo: 1, mergeOffersTwo: 10),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(4, metrics.MergeCount);
        Assert.Equal(0.2, metrics.MergeTakeRate.Rate, precision: 5);
        Assert.Equal(20, metrics.MergeTakeRate.Trials);
    }

    [Fact]
    public void Score_margin_is_signed_from_seat_ones_perspective()
    {
        var games = new[]
        {
            MakeGame(PlayerId.One, scoreOne: 10, scoreTwo: 4),
            MakeGame(PlayerId.Two, scoreOne: 2, scoreTwo: 10),
        };

        var metrics = MetricsReport.From(games);

        // +6 and -8 -> mean -1.
        Assert.Equal(-1.0, metrics.FinalScoreMargin.Mean, precision: 5);

        // Decisiveness ignores direction: |6| and |8| -> 7.
        Assert.Equal(7.0, metrics.AbsoluteScoreMargin.Mean, precision: 5);
    }

    [Fact]
    public void Score_margin_detects_a_seat_advantage_that_the_win_rate_interval_cannot()
    {
        // The reason margin was added, at a realistic batch size. 100 games, seat one wins 56 --
        // sitting exactly on the plan's ~55% first-player-advantage threshold, and a win-rate
        // interval of [46%, 65%] that cannot rule out an even matchup. The same 100 games give a
        // margin interval of [0.31, 1.29], which excludes zero. One metric resolves the question
        // and the other does not, on identical data: win rate discards everything except one bit
        // per game, margin keeps the size of the result too.
        var games = Enumerable.Range(0, 100)
            .Select(i => i < 56
                ? MakeGame(PlayerId.One, scoreOne: 10, scoreTwo: 7)
                : MakeGame(PlayerId.Two, scoreOne: 8, scoreTwo: 10))
            .ToList();

        var metrics = MetricsReport.From(games);

        Assert.Equal(0.56, metrics.SeatOneWinRate.Rate, precision: 5);
        Assert.False(metrics.SeatOneWinRate.Excludes(0.5));
        Assert.True(metrics.FinalScoreMargin.Excludes(0.0));
    }

    [Fact]
    public void Margin_by_turn_averages_only_over_games_that_reached_that_turn()
    {
        // Turn 3 exists in one game only. Averaging it against a padded value from the short
        // game would manufacture a data point no game played -- so the count at that index must
        // be 1, and the mean must be the long game's actual value.
        var games = new[]
        {
            MakeGame(PlayerId.One, scoreMarginByTurn: [1, 2]),
            MakeGame(PlayerId.One, scoreMarginByTurn: [3, 4, 9]),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(3, metrics.ScoreMarginByTurn.Count);
        Assert.Equal(2.0, metrics.ScoreMarginByTurn[0].Mean, precision: 5);
        Assert.Equal(2, metrics.ScoreMarginByTurn[0].Count);
        Assert.Equal(9.0, metrics.ScoreMarginByTurn[2].Mean, precision: 5);
        Assert.Equal(1, metrics.ScoreMarginByTurn[2].Count);
    }

    [Fact]
    public void Provenance_round_trips_onto_the_report_when_supplied()
    {
        var provenance = new RunProvenance
        {
            Agents = ["ismcts"],
            GamesPerPairing = 30,
            BaseSeed = 7,
            Iterations = 200,
            RuleSetName = "default",
            CardSetHash = "abc123",
            CardCount = 36,
            RunAtUtc = DateTimeOffset.UnixEpoch,
        };

        var metrics = MetricsReport.From([MakeGame(PlayerId.One)], provenance);

        Assert.Equal("default", metrics.Provenance!.RuleSetName);
        Assert.Equal("abc123", metrics.Provenance.CardSetHash);
        Assert.Equal(200, metrics.Provenance.Iterations);
    }

    [Fact]
    public void Unopposed_slot_rate_divides_slot_turns_by_steps_times_slots_not_by_steps()
    {
        // The denominator is (scoring steps x slots per seat), because a seat can hold more than
        // one unopposed slot on a single step. Dividing by steps alone would let the "rate"
        // exceed 1 -- 6 slot-turns over 4 steps here would read as 150%.
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                unopposedSlotTurnsOne: 6, scoringStepsOne: 2,
                unopposedSlotTurnsTwo: 0, scoringStepsTwo: 2),
        };

        var metrics = MetricsReport.From(games);

        // 6 unopposed of (4 steps x 3 slots) = 6/12.
        Assert.Equal(12, metrics.UnopposedSlotRate.Trials);
        Assert.Equal(0.5, metrics.UnopposedSlotRate.Rate, precision: 5);
    }

    [Fact]
    public void Unopposed_per_step_is_sampled_per_seat_and_skips_seats_with_no_steps()
    {
        // A seat with zero scoring steps has no rate -- contributing a zero would drag the mean
        // toward a value that seat never experienced.
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                unopposedSlotTurnsOne: 4, scoringStepsOne: 2,
                unopposedSlotTurnsTwo: 0, scoringStepsTwo: 0),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(1, metrics.UnopposedCreaturesPerStep.Count);
        Assert.Equal(2.0, metrics.UnopposedCreaturesPerStep.Mean, precision: 5);
    }

    [Fact]
    public void Games_with_no_sustained_unopposed_run_require_a_streak_of_two()
    {
        // "Sustained" is 2+ consecutive steps, matching how step 4.2 phrased the finding -- a
        // single unopposed step is common and is not the compounding phenomenon.
        var games = new[]
        {
            MakeGame(PlayerId.One, longestUnopposedStreakOne: 1, longestUnopposedStreakTwo: 1),
            MakeGame(PlayerId.One, longestUnopposedStreakOne: 2, longestUnopposedStreakTwo: 0),
            MakeGame(PlayerId.One, longestUnopposedStreakOne: 0, longestUnopposedStreakTwo: 5),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(1, metrics.GamesWithNoSustainedUnopposed);
    }

    [Fact]
    public void Cost_pressure_separates_priced_out_cards_from_declined_ones()
    {
        // The distinction the metric exists for: both cards are played zero times, but "costly"
        // was never affordable while "declined" was offered constantly and passed over. Take
        // rate reports them identically at 0%; cost pressure does not.
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                cardOffersOne: new Dictionary<string, int> { ["declined"] = 40 },
                cardsBlockedByCostOne: new Dictionary<string, int> { ["costly"] = 40 }),
        };

        var metrics = MetricsReport.From(games);

        var costly = metrics.CardStats.Single(c => c.CardId == "costly");
        var declined = metrics.CardStats.Single(c => c.CardId == "declined");

        Assert.Equal(1.0, costly.CostPressure.Rate, precision: 5);
        Assert.Equal(0.0, declined.CostPressure.Rate, precision: 5);
        Assert.Equal(0, costly.PlayCount);
        Assert.Equal(0, declined.PlayCount);
    }

    [Fact]
    public void Creature_survival_averages_only_instances_that_actually_died()
    {
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                creatureSurvivalOne:
                [
                    new CreatureLifetime("tough", 6, ScoredWhileAlive: true),
                    new CreatureLifetime("tough", 4, ScoredWhileAlive: false),
                    new CreatureLifetime("fragile", 1, ScoredWhileAlive: false),
                ]),
        };

        var metrics = MetricsReport.From(games);

        var tough = metrics.CardStats.Single(c => c.CardId == "tough");
        var fragile = metrics.CardStats.Single(c => c.CardId == "fragile");

        Assert.Equal(5.0, tough.SurvivalSteps.Mean, precision: 5);
        Assert.Equal(2, tough.SurvivalSteps.Count);
        Assert.Equal(1.0, fragile.SurvivalSteps.Mean, precision: 5);

        // Half of tough's instances were unopposed at some point -- the blocker/scorer split.
        Assert.Equal(0.5, tough.ScoredWhileAliveRate.Rate, precision: 5);
        Assert.Equal(0.0, fragile.ScoredWhileAliveRate.Rate, precision: 5);
    }

    [Fact]
    public void A_card_that_never_died_reports_zero_survival_samples_not_a_survival_of_zero()
    {
        // A spell has no lifetime at all, and a creature that always survived to game end has no
        // uncensored sample. Either way the answer is "no data", not "died instantly" -- which
        // would rank the most durable creatures as the most fragile.
        var games = new[] { MakeGame(PlayerId.One, cardsPlayedOne: ["a_spell"]) };

        var metrics = MetricsReport.From(games);

        var spell = metrics.CardStats.Single(c => c.CardId == "a_spell");
        Assert.Equal(0, spell.SurvivalSteps.Count);
        Assert.Equal(0, spell.ScoredWhileAliveRate.Trials);
    }

    [Fact]
    public void Empty_batch_throws()
    {
        Assert.Throws<ArgumentException>(() => MetricsReport.From([]));
    }
}
