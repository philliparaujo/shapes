using Shapes.Core.Primitives;

namespace Shapes.Core.Rules;

// How decks are built for a game.
public enum DeckMode
{
    // Both players get the same deck: CopiesPerCard of every card in the set. Used for
    // Phases 1-3 so card balance can be measured without deck composition confounding it.
    Symmetric = 0,

    // Players supply their own decks, validated against DeckSize/MaxCopiesPerCard.
    // Phase 5 deckbuilding.
    Custom = 1,
}

// All tunable game rules in one place.
//
// Income, scoring, draw, hand limits, and win conditions change frequently during balance
// work, so none of them may be hard-coded elsewhere in the engine. A balance experiment is a
// named ruleset file; Phase 4 sweeps over them.
//
// Board size is deliberately absent -- it is structural rather than a balance knob, and lives
// once as SlotIndex.SlotsPerPlayer.
//
// Validation happens in the constructor so a malformed ruleset fails at load rather than
// producing a nonsense game hours into a simulation run.
public sealed class RuleSet
{
    public string Name { get; }

    // Cards
    public int StartingHandSize { get; }
    public int CardsDrawnPerTurn { get; }
    public int HandLimit { get; }

    // Income: a flat pool each turn, plus IncomePerCreatureType per type of each creature
    // controlled. A Spike/Wheel creature pays 1 spike and 1 wheel at IncomePerCreatureType 1.
    public ResourcePool BaseIncome { get; }
    public int IncomePerCreatureType { get; }

    // Scoring: at the start of a player's turn, each friendly creature whose opposing slot is
    // empty scores PointsPerUnopposedCreature.
    public int PointsPerUnopposedCreature { get; }
    public int ScoreToWin { get; }

    // Merging
    public bool MergeEnabled { get; }
    public bool MergeRequiresAdjacent { get; }
    public bool MergeCostsAction { get; }
    // How many base creatures may be folded into one. 2 means a merged creature cannot merge
    // again; 3 would allow a merged creature to absorb one more.
    public int MaxMergeDepth { get; }

    // Decks
    public DeckMode DeckMode { get; }
    public int CopiesPerCard { get; }
    public int DeckSize { get; }
    public int MaxCopiesPerCard { get; }

    public TypeChart TypeChart { get; }

    public RuleSet(
        string name,
        int startingHandSize,
        int cardsDrawnPerTurn,
        int handLimit,
        ResourcePool baseIncome,
        int incomePerCreatureType,
        int pointsPerUnopposedCreature,
        int scoreToWin,
        bool mergeEnabled,
        bool mergeRequiresAdjacent,
        bool mergeCostsAction,
        int maxMergeDepth,
        DeckMode deckMode,
        int copiesPerCard,
        int deckSize,
        int maxCopiesPerCard,
        TypeChart typeChart)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Ruleset name must not be empty.", nameof(name));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startingHandSize);
        ArgumentOutOfRangeException.ThrowIfNegative(cardsDrawnPerTurn);
        ArgumentOutOfRangeException.ThrowIfNegative(incomePerCreatureType);
        ArgumentOutOfRangeException.ThrowIfNegative(pointsPerUnopposedCreature);

        // A hand limit below the starting hand size would force a discard before the first
        // turn is played -- almost certainly a typo rather than an intent.
        ArgumentOutOfRangeException.ThrowIfLessThan(handLimit, startingHandSize);

        // Zero would mean the game is already won before the first turn.
        ArgumentOutOfRangeException.ThrowIfLessThan(scoreToWin, 1);

        // Merging always folds at least two creatures together.
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMergeDepth, 2);

        ArgumentNullException.ThrowIfNull(typeChart);

        if (deckMode == DeckMode.Symmetric)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(copiesPerCard, 1);
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(deckSize, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxCopiesPerCard, 1);

            // A deck that cannot legally be filled would fail at deckbuild time with a far
            // more confusing message than this one.
            ArgumentOutOfRangeException.ThrowIfGreaterThan(startingHandSize, deckSize);
        }

        Name = name;
        StartingHandSize = startingHandSize;
        CardsDrawnPerTurn = cardsDrawnPerTurn;
        HandLimit = handLimit;
        BaseIncome = baseIncome;
        IncomePerCreatureType = incomePerCreatureType;
        PointsPerUnopposedCreature = pointsPerUnopposedCreature;
        ScoreToWin = scoreToWin;
        MergeEnabled = mergeEnabled;
        MergeRequiresAdjacent = mergeRequiresAdjacent;
        MergeCostsAction = mergeCostsAction;
        MaxMergeDepth = maxMergeDepth;
        DeckMode = deckMode;
        CopiesPerCard = copiesPerCard;
        DeckSize = deckSize;
        MaxCopiesPerCard = maxCopiesPerCard;
        TypeChart = typeChart;
    }

    // The shipping rules, matching Shapes.Content/rulesets/default.json. Tests and tools use
    // this rather than loading from disk; the loader tests assert the two agree.
    public static RuleSet Default { get; } = new(
        name: "default",
        startingHandSize: 4,
        cardsDrawnPerTurn: 1,
        handLimit: 8,
        baseIncome: new ResourcePool(1, 1, 1),
        incomePerCreatureType: 1,
        pointsPerUnopposedCreature: 1,
        scoreToWin: 10,
        mergeEnabled: true,
        mergeRequiresAdjacent: true,
        mergeCostsAction: false,
        maxMergeDepth: 2,
        deckMode: DeckMode.Symmetric,
        copiesPerCard: 2,
        deckSize: 0,
        maxCopiesPerCard: 0,
        typeChart: TypeChart.Default);

    public override string ToString() => Name;
}
