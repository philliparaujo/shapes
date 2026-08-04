using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Tests.Fixtures;

// Builds a RuleSet that differs from the default in exactly one field.
//
// RuleSet is immutable with a seventeen-argument constructor and no copy-with, so a test
// varying one knob would otherwise restate every other value -- and would then silently stop
// tracking the default if one of them changed. Restating them once, here, means a test reads as
// "the default, but MaxMergeDepth is 3", which is what it actually means.
public static class RuleSetTestHelper
{
    public static RuleSet WithMaxMergeDepth(int maxMergeDepth) => Build(maxMergeDepth: maxMergeDepth);

    public static RuleSet WithMergeEnabled(bool mergeEnabled) => Build(mergeEnabled: mergeEnabled);

    public static RuleSet WithMergeRequiresAdjacent(bool requiresAdjacent) =>
        Build(mergeRequiresAdjacent: requiresAdjacent);

    public static RuleSet WithHandLimit(int handLimit) => Build(handLimit: handLimit);

    public static RuleSet WithCardsDrawnPerTurn(int cardsDrawnPerTurn) =>
        Build(cardsDrawnPerTurn: cardsDrawnPerTurn);

    public static RuleSet WithScoreToWin(int scoreToWin) => Build(scoreToWin: scoreToWin);

    private static RuleSet Build(
        string? name = null,
        int? startingHandSize = null,
        int? cardsDrawnPerTurn = null,
        int? handLimit = null,
        ResourcePool? baseIncome = null,
        int? incomePerCreatureType = null,
        int? pointsPerUnopposedCreature = null,
        int? scoreToWin = null,
        bool? mergeEnabled = null,
        bool? mergeRequiresAdjacent = null,
        bool? mergeCostsAction = null,
        int? maxMergeDepth = null,
        DeckMode? deckMode = null,
        int? copiesPerCard = null,
        int? deckSize = null,
        int? maxCopiesPerCard = null,
        TypeChart? typeChart = null)
    {
        var d = RuleSet.Default;

        return new RuleSet(
            name: name ?? d.Name,
            startingHandSize: startingHandSize ?? d.StartingHandSize,
            cardsDrawnPerTurn: cardsDrawnPerTurn ?? d.CardsDrawnPerTurn,
            handLimit: handLimit ?? d.HandLimit,
            baseIncome: baseIncome ?? d.BaseIncome,
            incomePerCreatureType: incomePerCreatureType ?? d.IncomePerCreatureType,
            pointsPerUnopposedCreature: pointsPerUnopposedCreature ?? d.PointsPerUnopposedCreature,
            scoreToWin: scoreToWin ?? d.ScoreToWin,
            mergeEnabled: mergeEnabled ?? d.MergeEnabled,
            mergeRequiresAdjacent: mergeRequiresAdjacent ?? d.MergeRequiresAdjacent,
            mergeCostsAction: mergeCostsAction ?? d.MergeCostsAction,
            maxMergeDepth: maxMergeDepth ?? d.MaxMergeDepth,
            deckMode: deckMode ?? d.DeckMode,
            copiesPerCard: copiesPerCard ?? d.CopiesPerCard,
            deckSize: deckSize ?? d.DeckSize,
            maxCopiesPerCard: maxCopiesPerCard ?? d.MaxCopiesPerCard,
            typeChart: typeChart ?? d.TypeChart);
    }
}
