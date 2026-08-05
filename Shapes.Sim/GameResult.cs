using Shapes.Core.Actions;
using Shapes.Core.Primitives;

namespace Shapes.Sim;

// How a game concluded. Score-threshold is the only real win condition (RuleSet.ScoreToWin) --
// NonTerminating exists only as a safety valve (GameRunner's turn cap) so a rules/content bug
// that produces a stalemate shows up as a distinct, countable outcome in a batch run instead of
// hanging the whole matrix. PLAN.md step 4.5 calls out "non-terminating games" as something to
// watch for explicitly.
public enum EndingType
{
    ScoreThreshold = 0,
    NonTerminating = 1,
}

// The outcome of one played-out game, plus the behaviour counts the plan calls for alongside win
// rate (per Phase 2's blocking-slot lesson: an aggregate win rate can hide what an agent actually
// did with its turns). One row per game -- pairings are aggregated from a list of these, never
// pooled across seats, since pooling would hide first-player advantage.
public sealed class GameResult
{
    public required string AgentOne { get; init; }

    public required string AgentTwo { get; init; }

    public required ulong Seed { get; init; }

    // Null only for EndingType.NonTerminating -- a stalled game has no winner to report.
    public required PlayerId? Winner { get; init; }

    public required EndingType Ending { get; init; }

    // Seat-based, not winner/loser-based, so both are well-defined even for
    // EndingType.NonTerminating (no winner to anchor "winner score" to).
    public required int ScoreOne { get; init; }

    public required int ScoreTwo { get; init; }

    public required int TurnCount { get; init; }

    public required int ActionCount { get; init; }

    public required IReadOnlyDictionary<ActionKind, int> ActionCountsByKind { get; init; }

    // Every card id played via PlayCardAction, in play order, per seat -- source data for
    // per-card play-rate and (joined with Winner) win-rate correlation.
    public required IReadOnlyList<string> CardsPlayedOne { get; init; }

    public required IReadOnlyList<string> CardsPlayedTwo { get; init; }

    // Every card id that entered a seat's hand this game and was kept -- the starting hand plus
    // every GameState.TurnEventKind.CardDrawn event, in order, duplicates included. Source data
    // for draw win-rate correlation (distinct from play win-rate: a card can be drawn and never
    // played).
    public required IReadOnlyList<string> CardsDrawnOne { get; init; }

    public required IReadOnlyList<string> CardsDrawnTwo { get; init; }

    // Every move used via UseMoveAction, per seat -- source data for move-usage counts and
    // win-rate correlation, the move-level counterpart of CardStat. Paired with the id of the
    // card that DECLARED the move (not necessarily the whole creature using it -- a merged
    // creature's move can belong to either source card), so MetricsReport can report which
    // creature a move belongs to instead of a bare move name that two different cards might share.
    public required IReadOnlyList<(string CardId, string MoveName)> MovesUsedOne { get; init; }

    public required IReadOnlyList<(string CardId, string MoveName)> MovesUsedTwo { get; init; }

    // Creatures played (PlayCardAction for a creature card, not a spell) -- the denominator for
    // "merges per creature played," a coarse merge-opportunity proxy: merging needs at least two
    // creatures on board, so this says how many chances existed, not just how many were taken.
    public required int CreaturesPlayedOne { get; init; }

    public required int CreaturesPlayedTwo { get; init; }

    public required int MergeCountOne { get; init; }

    public required int MergeCountTwo { get; init; }

    // Unspent resources at game end, per seat -- a coarse resource-flow signal (income the
    // player never got around to spending).
    public required ResourcePool FinalResourcesOne { get; init; }

    public required ResourcePool FinalResourcesTwo { get; init; }

    // Elapsed wall-clock time for this single game. Not a substitute for Phase 3 step 3's
    // stopwatch-based before/after measurement -- this is per-game context for that later work,
    // not a benchmark result on its own.
    public required TimeSpan Elapsed { get; init; }

    public bool AgentOneWon => Winner == PlayerId.One;
}
