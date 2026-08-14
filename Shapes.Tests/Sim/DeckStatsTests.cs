using Shapes.Core.Primitives;
using Shapes.Sim;

namespace Shapes.Tests.Sim;

// MetricsReport.DeckStats: win rate bucketed by deck composition.
//
// Pins the bucketing arithmetic -- boundary handling, fixed anchoring, empty-bucket retention --
// against literal GameResults, the same approach MetricsReportTests and IncludedWinRateTests take.
public class DeckStatsTests
{
    private static DeckProfile Profile(
        double meanCost = 2.5, int spikeCards = 14, int anvilCards = 13, int wheelCards = 13,
        int spikeCreatures = 12, int anvilCreatures = 12, int wheelCreatures = 12,
        int spikeCost = 36, int anvilCost = 36, int wheelCost = 36) =>
        new()
        {
            Name = "d",
            CardCount = 40,
            MeanCost = meanCost,
            SpikeCards = spikeCards,
            AnvilCards = anvilCards,
            WheelCards = wheelCards,
            SpikeCreatures = spikeCreatures,
            AnvilCreatures = anvilCreatures,
            WheelCreatures = wheelCreatures,
            SpikeCost = spikeCost,
            AnvilCost = anvilCost,
            WheelCost = wheelCost,
        };

    private static GameResult Game(PlayerId? winner, DeckProfile? one = null, DeckProfile? two = null) =>
        new()
        {
            AgentOne = "a",
            AgentTwo = "b",
            Seed = 1,
            Winner = winner,
            Ending = EndingType.ScoreThreshold,
            ScoreOne = winner == PlayerId.One ? 7 : 0,
            ScoreTwo = winner == PlayerId.Two ? 7 : 0,
            TurnCount = 5,
            ActionCount = 5,
            ActionCountsByKind = new Dictionary<Core.Actions.ActionKind, int>(),
            DeckProfileOne = one,
            DeckProfileTwo = two,
            CardsPlayedOne = [],
            CardsPlayedTwo = [],
            CardsDrawnOne = [],
            CardsDrawnTwo = [],
            MovesUsedOne = [],
            MovesUsedTwo = [],
            CreaturesPlayedOne = 0,
            CreaturesPlayedTwo = 0,
            MergeCountOne = 0,
            MergeCountTwo = 0,
            FinalResourcesOne = ResourcePool.Empty,
            FinalResourcesTwo = ResourcePool.Empty,
            FatigueTurnsOne = 0,
            FatigueTurnsTwo = 0,
            FirstFatigueTurnOne = null,
            FirstFatigueTurnTwo = null,
            FatigueScoreGainedOne = 0,
            FatigueScoreGainedTwo = 0,
            CardOffersOne = new Dictionary<string, int>(),
            CardOffersTwo = new Dictionary<string, int>(),
            MoveOffersOne = new Dictionary<string, int>(),
            MoveOffersTwo = new Dictionary<string, int>(),
            CardOffersByTurnOne = new Dictionary<string, int>(),
            CardOffersByTurnTwo = new Dictionary<string, int>(),
            CardPlaysByTurnOne = new Dictionary<string, int>(),
            CardPlaysByTurnTwo = new Dictionary<string, int>(),
            MoveOffersByTurnOne = new Dictionary<string, int>(),
            MoveOffersByTurnTwo = new Dictionary<string, int>(),
            MoveUsesByTurnOne = new Dictionary<string, int>(),
            MoveUsesByTurnTwo = new Dictionary<string, int>(),
            MergeOffersOne = 0,
            MergeOffersTwo = 0,
            ScoreMarginByTurn = [],
            ResourcesByTurnOne = [],
            ResourcesByTurnTwo = [],
            HandSizeByTurnOne = [],
            HandSizeByTurnTwo = [],
            UnopposedSlotTurnsOne = 0,
            UnopposedSlotTurnsTwo = 0,
            ScoringStepsOne = 0,
            ScoringStepsTwo = 0,
            LongestUnopposedStreakOne = 0,
            LongestUnopposedStreakTwo = 0,
            SlotsOccupiedByTurnOne = [],
            SlotsOccupiedByTurnTwo = [],
            CombinedHealthByTurnOne = [],
            CombinedHealthByTurnTwo = [],
            CreatureSurvivalOne = [],
            CreatureSurvivalTwo = [],
            CardsBlockedByCostOne = new Dictionary<string, int>(),
            CardsBlockedByCostTwo = new Dictionary<string, int>(),
            Elapsed = TimeSpan.Zero,
        };

    private static DeckStat CostStat(MetricsReport report) =>
        report.DeckStats.Single(s => s.Name == "Mean card cost");

    [Fact]
    public void No_deck_profiles_means_no_deck_stats()
    {
        // Every pre-deck GameResult and most test fixtures carry no profile. Those must produce
        // no section rather than a row of zeros.
        Assert.Empty(MetricsReport.From([Game(PlayerId.One)]).DeckStats);
    }

    [Fact]
    public void A_property_that_does_not_vary_is_not_reported()
    {
        // The --deck default case: every deck identical, so grouping by any property yields one
        // bucket holding everything, which says nothing and should not be printed at all.
        var same = Profile(meanCost: 2.5);

        var report = MetricsReport.From([Game(PlayerId.One, same, same), Game(PlayerId.Two, same, same)]);

        Assert.DoesNotContain(report.DeckStats, s => s.Name == "Mean card cost");
    }

    [Fact]
    public void Mean_cost_buckets_are_two_tenths_wide_and_anchored_to_fixed_multiples()
    {
        // Anchored to multiples of the width, NOT to the observed minimum -- so "2.00-2.20" means
        // the same range in every run and two reports stay comparable.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One, Profile(meanCost: 2.05), Profile(meanCost: 2.35)),
        ]);

        var stat = CostStat(report);
        Assert.Equal(2.0, stat.Buckets[0].Low, 6);
        Assert.Equal(2.2, stat.Buckets[0].High, 6);
        Assert.Equal(2.2, stat.Buckets[1].Low, 6);
        Assert.Equal(2.4, stat.Buckets[1].High, 6);
    }

    [Fact]
    public void A_value_on_a_boundary_lands_in_the_upper_bucket()
    {
        // Half-open on the right: 2.2 belongs to 2.2-2.4, not to 2.0-2.2 as well. Double-counting
        // a boundary deck would inflate both buckets it touched.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One, Profile(meanCost: 2.2), Profile(meanCost: 2.0)),
        ]);

        var stat = CostStat(report);
        Assert.Equal(1, stat.Buckets[0].Decks);   // 2.0 -> [2.0, 2.2)
        Assert.Equal(1, stat.Buckets[1].Decks);   // 2.2 -> [2.2, 2.4)
    }

    [Fact]
    public void The_last_bucket_includes_its_upper_bound()
    {
        var report = MetricsReport.From(
        [
            Game(PlayerId.One, Profile(meanCost: 2.0), Profile(meanCost: 2.4)),
        ]);

        var stat = CostStat(report);
        Assert.True(stat.Buckets[^1].IncludesHigh);
        Assert.False(stat.Buckets[0].IncludesHigh);

        // The maximum sample must be counted somewhere -- dropping it would silently lose the
        // most extreme deck in the batch, which is usually the interesting one.
        Assert.Equal(2, stat.TotalDecks);
    }

    [Fact]
    public void Each_deck_counts_once_and_wins_follow_its_own_seat()
    {
        // One deck played by one seat is one trial. Seat one wins, seat two loses, and the two
        // decks sit in different buckets, so each bucket sees exactly its own seat's result.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One, Profile(meanCost: 2.1), Profile(meanCost: 2.9)),
        ]);

        var stat = CostStat(report);
        Assert.Equal(2, stat.TotalDecks);
        Assert.Equal(1, stat.Buckets[0].Decks);
        Assert.Equal(1, stat.Buckets[0].Wins);
        Assert.Equal(1, stat.Buckets[^1].Decks);
        Assert.Equal(0, stat.Buckets[^1].Wins);
    }

    [Fact]
    public void Empty_interior_buckets_are_kept()
    {
        // A gap is information ("no deck landed here"). Dropping it would make the surrounding
        // buckets look adjacent when they are not.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One, Profile(meanCost: 2.1), Profile(meanCost: 2.9)),
        ]);

        var stat = CostStat(report);
        Assert.True(stat.Buckets.Count >= 4);
        Assert.Contains(stat.Buckets, b => b.Decks == 0);
    }

    [Fact]
    public void A_drawn_game_counts_as_decks_but_no_wins()
    {
        var report = MetricsReport.From(
        [
            Game(null, Profile(meanCost: 2.1), Profile(meanCost: 2.9)),
        ]);

        var stat = CostStat(report);
        Assert.Equal(2, stat.TotalDecks);
        Assert.Equal(0, stat.Buckets.Sum(b => b.Wins));
    }

    [Fact]
    public void Cost_demand_board_type_and_pips_are_reported_separately()
    {
        // Three different questions about the same resource: how many cards DEMAND spike (what
        // the MinPerType constraint governs), how many creatures ARE spike on the board, and how
        // many spike pips the deck pays in total. A deck can demand plenty of spike while
        // fielding few spike creatures, spending it on spells instead.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One,
                Profile(spikeCards: 11, spikeCreatures: 10, spikeCost: 30),
                Profile(spikeCards: 19, spikeCreatures: 18, spikeCost: 50)),
        ]);

        Assert.Contains(report.DeckStats, s => s.Name == "Spike cards (by cost)");
        Assert.Contains(report.DeckStats, s => s.Name == "Spike creatures");
        Assert.Contains(report.DeckStats, s => s.Name == "Spike cost pips");
    }

    [Fact]
    public void Bucket_labels_render_to_the_stats_decimals()
    {
        // Spike counts must VARY for that grouping to be reported at all, hence the differing
        // creature counts alongside the differing costs.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One,
                Profile(meanCost: 2.1, spikeCreatures: 10),
                Profile(meanCost: 2.5, spikeCreatures: 16)),
        ]);

        // Costs label to 2dp ("2.00-2.20"); whole-card counts label to none ("10-12").
        var stat = CostStat(report);
        Assert.Equal("2.00-2.20", stat.Buckets[0].Label(stat.Decimals));

        var creatures = report.DeckStats.Single(s => s.Name == "Spike creatures");
        Assert.Equal(0, creatures.Decimals);
        Assert.Equal("10-12", creatures.Buckets[0].Label(creatures.Decimals));
    }

    [Fact]
    public void Separation_is_reported_only_when_intervals_actually_separate()
    {
        // Two decks, one win each, on a tiny sample: the intervals are enormous and overlap, so
        // the flag must say so rather than letting the raw 100%-vs-0% spread read as a finding.
        var noisy = MetricsReport.From(
        [
            Game(PlayerId.One, Profile(meanCost: 2.1), Profile(meanCost: 2.9)),
        ]);

        Assert.False(CostStat(noisy).HasSeparatedBuckets);

        // Many decks, cleanly split: cheap decks always win, expensive ones always lose.
        var decisive = Enumerable.Range(0, 40)
            .Select(_ => Game(PlayerId.One, Profile(meanCost: 2.1), Profile(meanCost: 2.9)))
            .ToList();

        Assert.True(CostStat(MetricsReport.From(decisive)).HasSeparatedBuckets);
    }
}
