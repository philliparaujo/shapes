using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Tests.Mechanics;

// RuleSet validation. Every rule the design flagged as volatile is a field here, so the
// guard rails matter: a malformed ruleset should fail at construction rather than produce a
// nonsense game hours into a simulation run.
public class RuleSetTests
{
    [Fact]
    public void Default_matches_the_documented_shipping_rules()
    {
        var d = RuleSet.Default;

        Assert.Equal("default", d.Name);
        Assert.Equal(4, d.StartingHandSize);
        Assert.Equal(1, d.CardsDrawnPerTurn);
        Assert.Equal(RuleSet.NoHandLimit, d.HandLimit);
        Assert.Equal(new ResourcePool(2, 2, 2), d.BaseIncome);
        Assert.Equal(0, d.IncomePerCreatureType);
        Assert.Equal(1, d.PointsPerUnopposedCreature);
        Assert.False(d.ScoreByCreatureDelta);
        Assert.Equal(10, d.ScoreToWin);
        Assert.True(d.MergeEnabled);
        Assert.True(d.MergeRequiresAdjacent);
        Assert.False(d.MergeCostsAction);
        Assert.Equal(2, d.MaxMergeDepth);
        Assert.Equal(DeckMode.Symmetric, d.DeckMode);
        Assert.Equal(2, d.CopiesPerCard);
    }

    private static RuleSet Build(
        string name = "test",
        int startingHandSize = 4,
        int cardsDrawnPerTurn = 1,
        int handLimit = 8,
        int incomePerCreatureType = 1,
        int pointsPerUnopposedCreature = 1,
        int scoreToWin = 10,
        int maxMergeDepth = 2,
        DeckMode deckMode = DeckMode.Symmetric,
        int copiesPerCard = 2,
        int deckSize = 0,
        int maxCopiesPerCard = 0) =>
        new(name, startingHandSize, cardsDrawnPerTurn, handLimit,
            new ResourcePool(1, 1, 1), incomePerCreatureType,
            pointsPerUnopposedCreature, scoreToWin,
            true, true, false, maxMergeDepth,
            deckMode, copiesPerCard, deckSize, maxCopiesPerCard,
            TypeChart.Default);

    [Fact]
    public void Name_must_not_be_empty()
    {
        Assert.Throws<ArgumentException>(() => Build(name: "   "));
    }

    [Fact]
    public void Score_to_win_must_be_positive()
    {
        // Zero would mean the game is won before the first turn is played.
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(scoreToWin: 0));
    }

    [Fact]
    public void Hand_limit_must_not_be_below_the_starting_hand()
    {
        // Otherwise the opening hand forces an immediate discard, which is a typo rather
        // than a rule anyone means to write.
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(startingHandSize: 6, handLimit: 4));
    }

    [Fact]
    public void Merge_depth_must_allow_at_least_a_pair()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(maxMergeDepth: 1));
    }

    [Theory]
    [InlineData(-1, 1, 1, 1)]
    [InlineData(1, -1, 1, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, 1, -1)]
    public void Negative_counts_are_rejected(int hand, int draw, int income, int points)
    {
        Assert.ThrowsAny<ArgumentException>(() => Build(
            startingHandSize: hand,
            cardsDrawnPerTurn: draw,
            incomePerCreatureType: income,
            pointsPerUnopposedCreature: points,
            handLimit: 99));
    }

    [Fact]
    public void Symmetric_decks_need_at_least_one_copy_per_card()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Build(deckMode: DeckMode.Symmetric, copiesPerCard: 0));
    }

    [Fact]
    public void Custom_decks_need_a_positive_size_and_copy_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Build(deckMode: DeckMode.Custom, deckSize: 0, maxCopiesPerCard: 2));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Build(deckMode: DeckMode.Custom, deckSize: 30, maxCopiesPerCard: 0));
    }

    [Fact]
    public void Custom_deck_must_be_big_enough_for_the_opening_hand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(
            deckMode: DeckMode.Custom, startingHandSize: 10, handLimit: 10,
            deckSize: 5, maxCopiesPerCard: 2));
    }

    [Fact]
    public void Zero_income_per_creature_is_allowed()
    {
        // A legitimate Phase 4 sweep: turns off the creature-income compounding to measure
        // how much of the runaway-leader effect it accounts for.
        var rules = Build(incomePerCreatureType: 0);

        Assert.Equal(0, rules.IncomePerCreatureType);
    }
}
