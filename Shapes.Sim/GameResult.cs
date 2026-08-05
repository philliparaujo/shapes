using Shapes.Core.Actions;
using Shapes.Core.Primitives;

namespace Shapes.Sim;

// The outcome of one played-out game, plus the behaviour counts the plan calls for alongside win
// rate (per Phase 2's blocking-slot lesson: an aggregate win rate can hide what an agent actually
// did with its turns). One row per game -- pairings are aggregated from a list of these, never
// pooled across seats, since pooling would hide first-player advantage.
public sealed class GameResult
{
    public required string AgentOne { get; init; }

    public required string AgentTwo { get; init; }

    public required ulong Seed { get; init; }

    public required PlayerId Winner { get; init; }

    public required int WinnerScore { get; init; }

    public required int LoserScore { get; init; }

    public required int TurnCount { get; init; }

    public required int ActionCount { get; init; }

    public required IReadOnlyDictionary<ActionKind, int> ActionCountsByKind { get; init; }

    // Elapsed wall-clock time for this single game. Not a substitute for Phase 3 step 3's
    // stopwatch-based before/after measurement -- this is per-game context for that later work,
    // not a benchmark result on its own.
    public required TimeSpan Elapsed { get; init; }

    public bool AgentOneWon => Winner == PlayerId.One;
}
