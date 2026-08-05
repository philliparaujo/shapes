using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Sim;

namespace Shapes.Tests.Sim;

// PLAN.md Phase 4 step 1: win rate by seat, game length, per-card play/draw/win-rate correlation,
// move usage, merge frequency, resource flow, ending type. Testing is brief per the step's own
// scope -- these pin the aggregation arithmetic against small, literal GameResults rather than
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
        int turnCount = 5) =>
        new()
        {
            AgentOne = "random",
            AgentTwo = "random",
            Seed = 1,
            Winner = winner,
            Ending = ending,
            ScoreOne = 0,
            ScoreTwo = 0,
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

        Assert.Equal(0.4, metrics.SeatOneWinRate, precision: 5);
        Assert.Equal(0.6, metrics.SeatTwoWinRate, precision: 5);
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

        Assert.Equal(0.5, metrics.SeatOneWinRate, precision: 5);
        Assert.Equal(0.0, metrics.SeatTwoWinRate, precision: 5);
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
        Assert.Equal(0.5, spike.WinRateWhenPlayed, precision: 5);

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
        Assert.Equal(1.0 / 3.0, spike.WinRateWhenDrawn, precision: 5);
        Assert.Equal(1, spike.GamesPlayedIn);
        Assert.Equal(1, spike.WinsWhenPlayed);
        Assert.Equal(1.0, spike.WinRateWhenPlayed, precision: 5);
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
        Assert.Equal(0.5, slash.WinRateWhenUsed, precision: 5);

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
    public void Unspent_resources_are_averaged_across_both_seats_and_all_games()
    {
        var games = new[]
        {
            MakeGame(
                PlayerId.One,
                finalOne: new ResourcePool(2, 0, 0),
                finalTwo: new ResourcePool(0, 4, 0)),
        };

        var metrics = MetricsReport.From(games);

        Assert.Equal(1.0, metrics.AverageUnspentSpike, precision: 5);
        Assert.Equal(2.0, metrics.AverageUnspentAnvil, precision: 5);
        Assert.Equal(0.0, metrics.AverageUnspentWheel, precision: 5);
    }

    [Fact]
    public void Empty_batch_throws()
    {
        Assert.Throws<ArgumentException>(() => MetricsReport.From([]));
    }
}
