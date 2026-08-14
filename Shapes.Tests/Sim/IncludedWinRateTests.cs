using Shapes.Core.Primitives;
using Shapes.Sim;

namespace Shapes.Tests.Sim;

// CardStat.IncludedWinRate and its by-copy-count breakdown: of the decks that ran a card, how
// often did that seat win.
//
// Built on minimal literal GameResults (only the deck fields and the winner matter here) rather
// than played games, so the arithmetic is pinned directly -- the same approach MetricsReportTests
// takes, and for the same reason.
public class IncludedWinRateTests
{
    private static GameResult Game(
        PlayerId? winner,
        IReadOnlyDictionary<string, int>? deckOne = null,
        IReadOnlyDictionary<string, int>? deckTwo = null) =>
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
            DeckOne = deckOne ?? new Dictionary<string, int>(),
            DeckTwo = deckTwo ?? new Dictionary<string, int>(),
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

    private static CardStat StatFor(MetricsReport report, string cardId) =>
        report.CardStats.Single(s => s.CardId == cardId);

    [Fact]
    public void Counts_one_deck_as_one_trial_regardless_of_copies()
    {
        // Seat one runs 3 copies and wins; seat two runs 1 copy and loses. Both are ONE deck, so
        // the headline rate must be 1 win in 2 decks -- not 3 in 4, which is what copy-weighting
        // would produce.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One,
                deckOne: new Dictionary<string, int> { ["a"] = 3 },
                deckTwo: new Dictionary<string, int> { ["a"] = 1 }),
        ]);

        var stat = StatFor(report, "a");
        Assert.Equal(2, stat.DecksIncludedIn);
        Assert.Equal(1, stat.WinsWhenIncluded);
        Assert.Equal(0.5, stat.IncludedWinRate.Rate, 6);
    }

    [Fact]
    public void Only_decks_containing_the_card_are_counted()
    {
        // "b" is in seat two's deck only, and seat two loses both games -- so b is 0 for 2 while
        // a, in both decks, is 2 for 4.
        var withA = new Dictionary<string, int> { ["a"] = 1 };
        var withAB = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1 };

        var report = MetricsReport.From(
        [
            Game(PlayerId.One, deckOne: withA, deckTwo: withAB),
            Game(PlayerId.One, deckOne: withA, deckTwo: withAB),
        ]);

        var a = StatFor(report, "a");
        Assert.Equal(4, a.DecksIncludedIn);
        Assert.Equal(2, a.WinsWhenIncluded);

        var b = StatFor(report, "b");
        Assert.Equal(2, b.DecksIncludedIn);
        Assert.Equal(0, b.WinsWhenIncluded);
        Assert.Equal(0.0, b.IncludedWinRate.Rate, 6);
    }

    [Fact]
    public void By_copy_count_splits_decks_into_buckets()
    {
        // Three decks running 1, 2, and 3 copies. The 3-copy deck wins, the others lose, so the
        // buckets must separate rather than pooling into one 1-in-3 rate.
        var report = MetricsReport.From(
        [
            Game(PlayerId.Two,
                deckOne: new Dictionary<string, int> { ["a"] = 1 },
                deckTwo: new Dictionary<string, int> { ["a"] = 3 }),
            Game(PlayerId.Two,
                deckOne: new Dictionary<string, int> { ["a"] = 2 },
                deckTwo: new Dictionary<string, int> { ["z"] = 1 }),
        ]);

        var stat = StatFor(report, "a");

        Assert.Equal(1, stat.ByCopyCount[1].Decks);
        Assert.Equal(0, stat.ByCopyCount[1].Wins);

        Assert.Equal(1, stat.ByCopyCount[2].Decks);
        Assert.Equal(0, stat.ByCopyCount[2].Wins);

        Assert.Equal(1, stat.ByCopyCount[3].Decks);
        Assert.Equal(1, stat.ByCopyCount[3].Wins);
        Assert.Equal(1.0, stat.ByCopyCount[3].WinRate.Rate, 6);
    }

    [Fact]
    public void Copy_buckets_sum_to_the_headline_totals()
    {
        // The breakdown is a partition of the same trials, so it must reconcile exactly -- this
        // is what stops the two numbers drifting apart under a future refactor.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One,
                deckOne: new Dictionary<string, int> { ["a"] = 2 },
                deckTwo: new Dictionary<string, int> { ["a"] = 3 }),
            Game(PlayerId.Two,
                deckOne: new Dictionary<string, int> { ["a"] = 1 },
                deckTwo: new Dictionary<string, int> { ["a"] = 2 }),
        ]);

        var stat = StatFor(report, "a");

        Assert.Equal(stat.DecksIncludedIn, stat.ByCopyCount.Values.Sum(b => b.Decks));
        Assert.Equal(stat.WinsWhenIncluded, stat.ByCopyCount.Values.Sum(b => b.Wins));
    }

    [Fact]
    public void Absent_copy_counts_get_no_bucket()
    {
        // No deck ran 2 copies, so there must be no 2-bucket -- an empty zero-count bucket would
        // read as "two copies were tried and never won," which is a different claim.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One,
                deckOne: new Dictionary<string, int> { ["a"] = 1 },
                deckTwo: new Dictionary<string, int> { ["a"] = 3 }),
        ]);

        var stat = StatFor(report, "a");

        Assert.True(stat.ByCopyCount.ContainsKey(1));
        Assert.False(stat.ByCopyCount.ContainsKey(2));
        Assert.True(stat.ByCopyCount.ContainsKey(3));
    }

    [Fact]
    public void A_card_only_ever_in_decks_still_appears_in_the_report()
    {
        // Never drawn, never offered, never played -- inclusion is its only signal, and "in every
        // deck, never once seen" is a real finding rather than a row to drop.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One,
                deckOne: new Dictionary<string, int> { ["ghost"] = 2 },
                deckTwo: new Dictionary<string, int> { ["ghost"] = 2 }),
        ]);

        var stat = StatFor(report, "ghost");
        Assert.Equal(2, stat.DecksIncludedIn);
        Assert.Equal(0, stat.PlayCount);
        Assert.Equal(0, stat.TimesDrawn);
    }

    [Fact]
    public void Games_without_recorded_decks_contribute_nothing()
    {
        // Every pre-deck GameResult and most test fixtures carry empty deck dictionaries. Those
        // must not manufacture zero-count rows or drag a rate toward zero.
        var report = MetricsReport.From([Game(PlayerId.One)]);

        Assert.Empty(report.CardStats);
    }

    [Fact]
    public void A_zero_copy_entry_is_not_an_inclusion()
    {
        // Deck.CountsById never emits a zero, but a hand-built or round-tripped result could --
        // and counting it would put a card in the denominator of a deck that does not run it.
        var report = MetricsReport.From(
        [
            Game(PlayerId.One, deckOne: new Dictionary<string, int> { ["a"] = 0, ["b"] = 1 }),
        ]);

        Assert.DoesNotContain(report.CardStats, s => s.CardId == "a");
        Assert.Equal(1, StatFor(report, "b").DecksIncludedIn);
    }

    [Fact]
    public void A_drawn_game_counts_as_a_deck_but_not_a_win()
    {
        // A non-terminating game has no winner. Both decks played it, so both belong in the
        // denominator -- dropping them would quietly shrink the sample for whatever was in play
        // when games stall, which is exactly the situation worth measuring.
        var report = MetricsReport.From(
        [
            Game(null,
                deckOne: new Dictionary<string, int> { ["a"] = 1 },
                deckTwo: new Dictionary<string, int> { ["a"] = 1 }),
        ]);

        var stat = StatFor(report, "a");
        Assert.Equal(2, stat.DecksIncludedIn);
        Assert.Equal(0, stat.WinsWhenIncluded);
    }
}
