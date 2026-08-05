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
// Encodes a (declaring card id, move name) pair as a single string, so move-keyed dictionaries
// can survive JSON serialization -- System.Text.Json refuses a tuple as an object key.
//
// Card ids are validated identifiers and move names are authored card text, so "::" cannot
// appear in an id; splitting on the FIRST occurrence keeps the round trip exact even if a move
// name ever contained one.
public static class MoveKey
{
    private const string Separator = "::";

    public static string Of(string cardId, string moveName) => $"{cardId}{Separator}{moveName}";

    public static (string CardId, string MoveName) Split(string key)
    {
        var index = key.IndexOf(Separator, StringComparison.Ordinal);
        return index < 0
            ? (key, string.Empty)
            : (key[..index], key[(index + Separator.Length)..]);
    }
}

// How long one creature held its slot, in scoring steps, and whether that slot was ever
// unopposed while it stood there.
//
// A named record rather than a (string, int) tuple because this is serialized as part of
// GameResult -- System.Text.Json handles a record's properties fine but refuses tuples as
// dictionary keys and writes them as opaque Item1/Item2 in lists, which makes the --json output
// unreadable for exactly the audience it exists for.
//
// ScoredWhileAlive is the bridge between this metric and the unopposed-slot metric: a creature
// that survives a long time WITHOUT ever being unopposed is holding a contested lane (a blocker),
// while one that survives and scores is doing the thing the game rewards. Same lifetime, very
// different roles, and a balance change usually means to move one and not the other.
public sealed record CreatureLifetime(string CardId, int ScoringStepsSurvived, bool ScoredWhileAlive);

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

    // OPPORTUNITY DENOMINATORS (PLAN.md step 4.3's "rank outliers" needs these, not raw counts).
    //
    // How many distinct decision points offered this card as a legal play, per seat -- counted
    // once per (decision point, card id) even when the card appears as several actions at once
    // (one per empty slot, one per chosen target), because those are the same "could I play this
    // card" opportunity presented several ways. Without this, PlayCount conflates three separate
    // things: how often the card was drawn, how often it was affordable, and how often the agent
    // chose it over the alternatives. Plays divided by this isolates the third -- the only one of
    // the three that is a statement about the card's design rather than about the shuffle.
    //
    // This is the card-level analogue of MergesPerCreaturePlayed, which already exists for
    // exactly this reason: a bare count cannot distinguish "rarely taken" from "rarely offered."
    public required IReadOnlyDictionary<string, int> CardOffersOne { get; init; }

    public required IReadOnlyDictionary<string, int> CardOffersTwo { get; init; }

    // The same denominator for moves. A move that is legal constantly and never used is a dead
    // move; one that is used every single time it is legal has no decision in it. Both are step
    // 4.5 watch items, and neither is visible from UseCount alone.
    //
    // Keyed by MoveKey.Of(cardId, moveName) -- a delimited string rather than the
    // (CardId, MoveName) tuple MovesUsed* uses, because System.Text.Json cannot write a tuple as
    // a JSON object key and --json serializes this type wholesale. Caught by ResultWriterTests
    // rather than at compile time, since the failure is a serialization-time exception.
    public required IReadOnlyDictionary<string, int> MoveOffersOne { get; init; }

    public required IReadOnlyDictionary<string, int> MoveOffersTwo { get; init; }

    // Decision points that offered at least one legal merge, per seat. The denominator for
    // "merge take rate" -- step 4.2 measured this by instrumenting a bespoke run; carrying it in
    // GameResult makes it a standing metric that every batch reports for free.
    public required int MergeOffersOne { get; init; }

    public required int MergeOffersTwo { get; init; }

    // PER-TURN SERIES (score margin and resource levels sampled at each turn boundary).
    //
    // Score margin from seat one's perspective (ScoreOne - ScoreTwo) at the end of each turn.
    // Margin is a far lower-variance estimator than the binary win/loss: it uses the whole game
    // rather than one bit of it, so a first-player-advantage or income-compounding effect shows
    // up in dramatically fewer games than a win-rate difference would need. Step 4.1 dropped a
    // flattened score series as "size and noise"; this is the compact form that survives -- one
    // int per turn, and the aggregate reads it as a curve rather than storing per-game curves in
    // the report.
    public required IReadOnlyList<int> ScoreMarginByTurn { get; init; }

    // Unspent resources at each turn boundary, per seat -- the same signal as FinalResources*,
    // sampled where it is actually representative. End-of-game is the least typical moment in a
    // game: the winner has just spent everything to close it out and the loser has been starved,
    // so a game-end average mixes two opposite states and reports their midpoint as if it were a
    // typical resource level.
    public required IReadOnlyList<ResourcePool> ResourcesByTurnOne { get; init; }

    public required IReadOnlyList<ResourcePool> ResourcesByTurnTwo { get; init; }

    // UNOPPOSED-SLOT OCCUPANCY (the +1-per-unopposed-creature scoring rule's own denominator).
    //
    // Slot-turns spent unopposed, per seat: summed over each scoring step, how many of that
    // seat's slots held a creature whose opposing slot was empty. Sampled at the scoring step
    // specifically -- that is the moment the rule is actually read (GameState.PendingScore), so
    // this counts the same thing the score counts rather than a mid-turn approximation of it.
    //
    // Exists to separate two findings that score alone conflates and that need OPPOSITE fixes:
    // "PointsPerUnopposedCreature is too large" (few unopposed slot-turns, each worth a lot)
    // versus "slots go unopposed too easily" (many unopposed slot-turns, each worth little).
    // Step 4.2 measured unopposed streaks with bespoke instrumentation and confirmed the
    // compounding is real; this makes it a standing metric every batch reports.
    public required int UnopposedSlotTurnsOne { get; init; }

    public required int UnopposedSlotTurnsTwo { get; init; }

    // Scoring steps this game, per seat -- the denominator UnopposedSlotTurns* is read against.
    // Without it a raw slot-turn count cannot distinguish a long game from a dominant one.
    public required int ScoringStepsOne { get; init; }

    public required int ScoringStepsTwo { get; init; }

    // The longest run of consecutive scoring steps on which a seat held at least one unopposed
    // creature. The streak form of the same signal, kept because step 4.2's income-compounding
    // finding was phrased as a streak (a player who never sustained one 2+ turns running won no
    // sampled games) -- a total alone cannot express "sustained", which is the part that
    // compounds.
    public required int LongestUnopposedStreakOne { get; init; }

    public required int LongestUnopposedStreakTwo { get; init; }

    // CREATURE SURVIVAL (scoring steps survived, per creature that left the board).
    //
    // One entry per creature destroyed, pairing the card played with how long it lasted.
    // Separates two opposite balance problems that PlayTakeRate reports identically: a creature
    // played constantly that dies immediately (undercosted body, or a stat line that cannot hold
    // a slot) versus one that sticks (doing its job). Since holding a slot IS the win condition
    // here, time-on-board is much closer to "did this creature do its job" than a damage figure
    // would be.
    //
    // Attributed to the card that OCCUPIED the slot, not to every card folded into a merge --
    // that play is the one whose value is in question. Creatures alive at game end are excluded;
    // see GameRunner.RecordSurvival for why a censored sample is dropped rather than counted
    // short.
    public required IReadOnlyList<CreatureLifetime> CreatureSurvivalOne { get; init; }

    public required IReadOnlyList<CreatureLifetime> CreatureSurvivalTwo { get; init; }

    // AFFORDABILITY PRESSURE (decision points where a held card was legal-but-for-cost).
    //
    // Counted once per (decision point, card id) for cards in hand that failed ONLY the
    // affordability check. This is what makes the resource metrics diagnosable rather than
    // merely descriptive: high unspent resources can mean income is too generous OR that nothing
    // is worth buying, and those need opposite fixes. High blocked counts alongside high unspent
    // resources means the two are of the WRONG TYPES -- which neither number shows alone.
    public required IReadOnlyDictionary<string, int> CardsBlockedByCostOne { get; init; }

    public required IReadOnlyDictionary<string, int> CardsBlockedByCostTwo { get; init; }

    // Elapsed wall-clock time for this single game. Not a substitute for Phase 3 step 3's
    // stopwatch-based before/after measurement -- this is per-game context for that later work,
    // not a benchmark result on its own.
    public required TimeSpan Elapsed { get; init; }

    public bool AgentOneWon => Winner == PlayerId.One;
}
