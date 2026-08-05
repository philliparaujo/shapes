using Shapes.Core.Actions;
using Shapes.Core.Primitives;

namespace Shapes.Sim;

// Per-card play rate and win-rate correlation, over every PlayCardAction in the batch regardless
// of which pairing or seat played it. "Win rate" here means: of the games where this card was
// played by a seat, how often did that seat go on to win -- the correlation PLAN.md step 1 asks
// for, not a claim of causation (a card correlated with wins might just be a strong agent's
// favorite, not a strong card; step 2 onward is where that gets pulled apart further).
public sealed class CardStat
{
    public required string CardId { get; init; }

    // Total times played across the batch, and in how many distinct games at least once --
    // PlayCount can exceed GamesPlayedIn since a card can be played more than once per game.
    public required int PlayCount { get; init; }

    public required int GamesPlayedIn { get; init; }

    public required int WinsWhenPlayed { get; init; }

    public double WinRateWhenPlayed => GamesPlayedIn == 0 ? 0.0 : (double)WinsWhenPlayed / GamesPlayedIn;

    // Drawn-but-not-necessarily-played -- the starting hand plus every mid-game draw, so this
    // catches a card that's strong-but-skipped or weak-but-always-cast differently from
    // WinRateWhenPlayed. A game where two copies of the same card were drawn still counts once
    // here (GamesDrawnIn), matching GamesPlayedIn's own per-game, not per-copy, counting.
    public required int TimesDrawn { get; init; }

    public required int GamesDrawnIn { get; init; }

    public required int WinsWhenDrawn { get; init; }

    public double WinRateWhenDrawn => GamesDrawnIn == 0 ? 0.0 : (double)WinsWhenDrawn / GamesDrawnIn;
}

// Per-move usage and win-rate correlation, the move-level counterpart of CardStat. Keyed by
// (CardId, MoveName) -- UseMoveAction.MoveIndex is only meaningful relative to one creature's
// concatenated move list (source-card order after merges), so it is not a stable identity on its
// own. CardId is the card that DECLARED the move, not necessarily the whole creature using it: a
// merged creature's move can belong to either source card. Including CardId also disambiguates
// two different cards that happen to share a move name, rather than silently merging their stats.
public sealed class MoveStat
{
    public required string CardId { get; init; }

    public required string MoveName { get; init; }

    public required int UseCount { get; init; }

    public required int GamesUsedIn { get; init; }

    public required int WinsWhenUsed { get; init; }

    public double WinRateWhenUsed => GamesUsedIn == 0 ? 0.0 : (double)WinsWhenUsed / GamesUsedIn;
}

// Whole-batch metrics: PLAN.md Phase 4 step 1's list (win rate by seat, game length, per-card
// play/draw/win-rate correlation, move usage, merge frequency, resource flow, ending type),
// computed once over every game in a BatchResult rather than per-pairing -- a per-card
// correlation or a seat win rate is only meaningful pooled across the whole matrix, unlike
// PairingSummary's per-(agentOne, agentTwo) breakdown.
public sealed class MetricsReport
{
    public required int GameCount { get; init; }

    // Seats, never pooled -- same reasoning as PairingSummary: pooling hides first-player
    // advantage, which is exactly what this number exists to surface (PLAN.md step 4's "watch
    // for first-player advantage beyond ~55%").
    public required double SeatOneWinRate { get; init; }

    public required double SeatTwoWinRate { get; init; }

    public required double AverageGameLength { get; init; }

    public required IReadOnlyList<CardStat> CardStats { get; init; }

    public required IReadOnlyList<MoveStat> MoveStats { get; init; }

    // Move usage: how many UseMoveAction choices occurred, out of every action taken -- a coarse
    // "how much of the game is spent attacking vs. other actions" signal. MoveStats above gives
    // the per-move breakdown this is the total of.
    public required int MoveUsageCount { get; init; }

    public required double MoveUsageRate { get; init; }

    public required int MergeCount { get; init; }

    public required double MergesPerGame { get; init; }

    // Total creatures played across the batch -- merging needs at least two on board, so this is
    // the opportunity denominator MergesPerCreaturePlayed is read against. Without it,
    // "X merges/game" alone can't say whether that's most of the creatures played merging, or a
    // small fraction of a much larger number.
    public required int CreaturesPlayedCount { get; init; }

    public required double MergesPerCreaturePlayed { get; init; }

    // Resource flow: mean unspent resources sitting in a player's pool at game end, by type,
    // averaged over both seats and every game -- a coarse "how much income goes unused" signal.
    public required double AverageUnspentSpike { get; init; }

    public required double AverageUnspentAnvil { get; init; }

    public required double AverageUnspentWheel { get; init; }

    public required IReadOnlyDictionary<EndingType, int> EndingCounts { get; init; }

    public static MetricsReport From(IReadOnlyList<GameResult> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        if (games.Count == 0)
        {
            throw new ArgumentException("Cannot compute metrics over zero games.", nameof(games));
        }

        var seatOneWins = games.Count(g => g.Winner == PlayerId.One);
        var seatTwoWins = games.Count(g => g.Winner == PlayerId.Two);

        var cardStats = ComputeCardStats(games);
        var moveStats = ComputeMoveStats(games);

        var moveUsageCount = games.Sum(g => g.ActionCountsByKind.GetValueOrDefault(ActionKind.UseMove));
        var totalActions = games.Sum(g => g.ActionCount);

        var mergeCount = games.Sum(g => g.MergeCountOne + g.MergeCountTwo);
        var creaturesPlayedCount = games.Sum(g => g.CreaturesPlayedOne + g.CreaturesPlayedTwo);

        var endingCounts = games
            .GroupBy(g => g.Ending)
            .ToDictionary(g => g.Key, g => g.Count());

        return new MetricsReport
        {
            GameCount = games.Count,
            SeatOneWinRate = (double)seatOneWins / games.Count,
            SeatTwoWinRate = (double)seatTwoWins / games.Count,
            AverageGameLength = games.Average(g => g.TurnCount),
            CardStats = cardStats,
            MoveStats = moveStats,
            MoveUsageCount = moveUsageCount,
            MoveUsageRate = totalActions == 0 ? 0.0 : (double)moveUsageCount / totalActions,
            MergeCount = mergeCount,
            MergesPerGame = (double)mergeCount / games.Count,
            CreaturesPlayedCount = creaturesPlayedCount,
            MergesPerCreaturePlayed = creaturesPlayedCount == 0 ? 0.0 : (double)mergeCount / creaturesPlayedCount,
            AverageUnspentSpike = games.Average(g =>
                (g.FinalResourcesOne.Spike + g.FinalResourcesTwo.Spike) / 2.0),
            AverageUnspentAnvil = games.Average(g =>
                (g.FinalResourcesOne.Anvil + g.FinalResourcesTwo.Anvil) / 2.0),
            AverageUnspentWheel = games.Average(g =>
                (g.FinalResourcesOne.Wheel + g.FinalResourcesTwo.Wheel) / 2.0),
            EndingCounts = endingCounts,
        };
    }

    // Shared by CardStats (play + draw) and MoveStats: for each game, walk one seat's list of ids
    // (played, drawn, or used -- a plain card id for cards, a (CardId, MoveName) tuple for moves),
    // bump a running total per id, and -- once per DISTINCT id per game, not once per occurrence
    // -- bump that id's "games it appeared in" and, if the seat won, its win count. The per-game
    // dedup is what makes two copies of the same card drawn in one game count as one game toward
    // the win-rate denominator rather than two.
    private static void AccountSeat<TKey>(
        IReadOnlyList<TKey> ids, bool seatWon,
        Dictionary<TKey, int> totalCounts, Dictionary<TKey, int> gamesIn,
        Dictionary<TKey, int> winsWhenPresent)
        where TKey : notnull
    {
        var distinctIds = new HashSet<TKey>();
        foreach (var id in ids)
        {
            totalCounts[id] = totalCounts.GetValueOrDefault(id) + 1;
            distinctIds.Add(id);
        }

        foreach (var id in distinctIds)
        {
            gamesIn[id] = gamesIn.GetValueOrDefault(id) + 1;
            if (seatWon)
            {
                winsWhenPresent[id] = winsWhenPresent.GetValueOrDefault(id) + 1;
            }
        }
    }

    private static IReadOnlyList<CardStat> ComputeCardStats(IReadOnlyList<GameResult> games)
    {
        var playCounts = new Dictionary<string, int>();
        var gamesPlayedIn = new Dictionary<string, int>();
        var winsWhenPlayed = new Dictionary<string, int>();
        var drawCounts = new Dictionary<string, int>();
        var gamesDrawnIn = new Dictionary<string, int>();
        var winsWhenDrawn = new Dictionary<string, int>();

        foreach (var game in games)
        {
            var oneWon = game.Winner == PlayerId.One;
            var twoWon = game.Winner == PlayerId.Two;

            AccountSeat(game.CardsPlayedOne, oneWon, playCounts, gamesPlayedIn, winsWhenPlayed);
            AccountSeat(game.CardsPlayedTwo, twoWon, playCounts, gamesPlayedIn, winsWhenPlayed);
            AccountSeat(game.CardsDrawnOne, oneWon, drawCounts, gamesDrawnIn, winsWhenDrawn);
            AccountSeat(game.CardsDrawnTwo, twoWon, drawCounts, gamesDrawnIn, winsWhenDrawn);
        }

        var everyCardId = new HashSet<string>(StringComparer.Ordinal);
        everyCardId.UnionWith(playCounts.Keys);
        everyCardId.UnionWith(drawCounts.Keys);

        return everyCardId
            .Select(cardId => new CardStat
            {
                CardId = cardId,
                PlayCount = playCounts.GetValueOrDefault(cardId),
                GamesPlayedIn = gamesPlayedIn.GetValueOrDefault(cardId),
                WinsWhenPlayed = winsWhenPlayed.GetValueOrDefault(cardId),
                TimesDrawn = drawCounts.GetValueOrDefault(cardId),
                GamesDrawnIn = gamesDrawnIn.GetValueOrDefault(cardId),
                WinsWhenDrawn = winsWhenDrawn.GetValueOrDefault(cardId),
            })
            .OrderByDescending(s => s.PlayCount)
            .ThenBy(s => s.CardId, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<MoveStat> ComputeMoveStats(IReadOnlyList<GameResult> games)
    {
        var useCounts = new Dictionary<(string CardId, string MoveName), int>();
        var gamesUsedIn = new Dictionary<(string CardId, string MoveName), int>();
        var winsWhenUsed = new Dictionary<(string CardId, string MoveName), int>();

        foreach (var game in games)
        {
            AccountSeat(
                game.MovesUsedOne, game.Winner == PlayerId.One, useCounts, gamesUsedIn, winsWhenUsed);
            AccountSeat(
                game.MovesUsedTwo, game.Winner == PlayerId.Two, useCounts, gamesUsedIn, winsWhenUsed);
        }

        return useCounts
            .Select(kv => new MoveStat
            {
                CardId = kv.Key.CardId,
                MoveName = kv.Key.MoveName,
                UseCount = kv.Value,
                GamesUsedIn = gamesUsedIn.GetValueOrDefault(kv.Key),
                WinsWhenUsed = winsWhenUsed.GetValueOrDefault(kv.Key),
            })
            .OrderByDescending(s => s.UseCount)
            .ThenBy(s => s.CardId, StringComparer.Ordinal)
            .ThenBy(s => s.MoveName, StringComparer.Ordinal)
            .ToList();
    }
}
