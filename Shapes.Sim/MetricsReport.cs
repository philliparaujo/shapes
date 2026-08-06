using Shapes.Core.Actions;
using Shapes.Core.Primitives;

namespace Shapes.Sim;

// Identifies WHAT PRODUCED a metrics report. Phase 4's method is paired comparison -- change one
// card or rule, rerun, diff against a frozen baseline (PLAN.md step 4.4) -- and a diff between
// two reports is meaningless if you cannot tell what differed between the runs that made them.
// Without this, a balance/ directory of report files is a pile of anonymous numbers.
//
// Deliberately includes the agent configuration even though Phase 4 freezes it: "frozen" is a
// claim worth being able to verify from the artifact rather than trusting from memory, and a
// report accidentally produced at the wrong iteration count is otherwise indistinguishable from
// a real content effect.
public sealed class RunProvenance
{
    public required IReadOnlyList<string> Agents { get; init; }

    public required int GamesPerPairing { get; init; }

    public required ulong BaseSeed { get; init; }

    public required int Iterations { get; init; }

    // Name of the ruleset in play. Every balance experiment is "a named ruleset file" by design
    // (PLAN.md's rules-as-configuration decision), so this is the primary axis a sweep varies.
    public required string RuleSetName { get; init; }

    // Fingerprint of the card data actually loaded, so two reports can be compared for
    // content-identity without diffing 36 JSON files. Order-independent over card ids and their
    // definitions -- an edit to any card's stats changes it, but merely renaming a file does not.
    public required string CardSetHash { get; init; }

    public required int CardCount { get; init; }

    public required DateTimeOffset RunAtUtc { get; init; }
}

// Per-card play rate and win-rate correlation, over every PlayCardAction in the batch regardless
// of which pairing or seat played it.
//
// TWO CAVEATS, both structural rather than fixable by more games:
//
//  1. "Win rate" means: of the games where this card was played by a seat, how often did that
//     seat go on to win. That is a correlation, not causation -- a card correlated with wins
//     might just be a strong agent's favorite.
//  2. Under deckMode "symmetric" BOTH seats hold every card, so most cards are played by both
//     seats in most games, contributing one win and one loss and pulling the rate mechanically
//     toward 0.5. Win rate therefore COMPRESSES under symmetric decks and cannot by itself rank
//     cards -- read PlayTakeRate (a within-decision choice measure, immune to this) as the
//     primary balance signal, and treat win rate as corroboration. This is why step 4.3's sweep
//     is delta-based (change one thing, diff two reports) rather than ranking one report's
//     cards against each other in isolation.
public sealed class CardStat
{
    public required string CardId { get; init; }

    // Total times played across the batch, and in how many distinct games at least once --
    // PlayCount can exceed GamesPlayedIn since a card can be played more than once per game.
    public required int PlayCount { get; init; }

    public required int GamesPlayedIn { get; init; }

    public required int WinsWhenPlayed { get; init; }

    // How many decision points offered this card as a legal play (per GameResult.CardOffers*).
    // The denominator PlayCount alone is missing.
    public required int OfferCount { get; init; }

    // THE PRIMARY BALANCE SIGNAL: of the decisions where playing this card was legal, how often
    // was it chosen. Unlike win rate this is a direct measure of a strong agent's revealed
    // preference, it does not compress under symmetric decks, and it separates the two step 4.5
    // watch items that raw counts conflate -- a near-zero rate is a dead card (offered, always
    // declined), a near-one rate is an auto-include (offered, never declined, no decision in it).
    //
    // Note the denominator counts decision points, not turns: a card stays legal across every
    // decision in a turn until it is played or becomes unaffordable, so this rate runs low in
    // absolute terms. Compare cards against each other, not against an absolute threshold.
    public Interval PlayTakeRate => Interval.Wilson(Math.Min(PlayCount, OfferCount), OfferCount);

    public Interval WinRateWhenPlayed => Interval.Wilson(WinsWhenPlayed, GamesPlayedIn);

    // Drawn-but-not-necessarily-played -- the starting hand plus every mid-game draw, so this
    // catches a card that's strong-but-skipped or weak-but-always-cast differently from
    // WinRateWhenPlayed. A game where two copies of the same card were drawn still counts once
    // here (GamesDrawnIn), matching GamesPlayedIn's own per-game, not per-copy, counting.
    public required int TimesDrawn { get; init; }

    public required int GamesDrawnIn { get; init; }

    public required int WinsWhenDrawn { get; init; }

    public Interval WinRateWhenDrawn => Interval.Wilson(WinsWhenDrawn, GamesDrawnIn);

    // Decision points where this card sat in hand and the ONLY thing stopping it being played
    // was its cost. Read against OfferCount: a card with 20 offers and 200 blocked decisions is
    // priced out of most of the game, whatever its take rate says about the times it was
    // available. This is the number that tells "too expensive" apart from "not worth it" -- a
    // card with a low take rate AND a low blocked count is being declined on merit, while a low
    // take rate with a high blocked count is a cost problem.
    public required int BlockedByCostCount { get; init; }

    // Of the decisions where this card was in hand at all (offered + blocked), the share where
    // cost was the blocker. Bounded and comparable across cards, unlike the raw count, which
    // scales with how often the card is drawn.
    public Interval CostPressure =>
        Interval.Wilson(BlockedByCostCount, BlockedByCostCount + OfferCount);

    // CREATURE SURVIVAL. Zero-count for spells (they never occupy a slot) and for creatures that
    // were never destroyed in the batch -- read Count before reading Mean.
    //
    // Mean scoring steps this creature held its slot before dying, over every instance that DID
    // die. Censored samples (alive at game end) are excluded rather than counted at their
    // truncated length, which would drag the mean toward zero for exactly the creatures that
    // survive best. The bias that remains runs the other way -- creatures good enough to survive
    // to the end are under-represented -- so treat this as a floor on survival, not an estimate.
    public required MeanEstimate SurvivalSteps { get; init; }

    // Of this creature's destroyed instances, the share that were unopposed at some point while
    // alive -- i.e. actually scored. Distinguishes a creature that holds a contested lane (a
    // blocker: long survival, rarely unopposed) from one that converts board presence into
    // points (long survival, often unopposed). Same lifetime, different roles, and a balance
    // change usually intends to move one and not the other.
    public required Interval ScoredWhileAliveRate { get; init; }
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

    public required int OfferCount { get; init; }

    // Of the decisions where this move was legal, how often it was used. Same role as
    // CardStat.PlayTakeRate: this is what distinguishes a move nobody wants from a move nobody
    // gets the chance to make. A move on a rarely-played creature has a small denominator here
    // and a correspondingly wide interval, which is the correct outcome -- it says the data
    // cannot rank that move yet, rather than ranking it on four uses.
    public Interval UseTakeRate => Interval.Wilson(Math.Min(UseCount, OfferCount), OfferCount);

    public Interval WinRateWhenUsed => Interval.Wilson(WinsWhenUsed, GamesUsedIn);
}

// Resource levels averaged over turns, split so the winner's and loser's curves never get mixed.
// Reported per-turn rather than at game end because game end is the least representative moment
// in a game: the winner has just spent everything to close it out and the loser has been starved
// for several turns, so a game-end average reports the midpoint of two opposite states as if it
// were a typical level.
public sealed class ResourceProfile
{
    public required MeanEstimate Spike { get; init; }

    public required MeanEstimate Anvil { get; init; }

    public required MeanEstimate Wheel { get; init; }

    public static ResourceProfile From(IReadOnlyList<ResourcePool> samples) =>
        new()
        {
            Spike = MeanEstimate.From(samples.Select(r => (double)r.Spike)),
            Anvil = MeanEstimate.From(samples.Select(r => (double)r.Anvil)),
            Wheel = MeanEstimate.From(samples.Select(r => (double)r.Wheel)),
        };
}

// Whole-batch metrics: PLAN.md Phase 4 step 1's list, extended by step 3's prerequisites
// (confidence intervals, opportunity denominators, score margin, provenance). Computed once over
// every game in a BatchResult rather than per-pairing -- a per-card correlation or a seat win
// rate is only meaningful pooled across the whole matrix, unlike PairingSummary's
// per-(agentOne, agentTwo) breakdown.
public sealed class MetricsReport
{
    // Null only when a report is built outside a real Shapes.Sim run (tests aggregating literal
    // GameResults). Every report written to disk carries it.
    public RunProvenance? Provenance { get; init; }

    public required int GameCount { get; init; }

    // Seats, never pooled -- same reasoning as PairingSummary: pooling hides first-player
    // advantage, which is exactly what this number exists to surface (PLAN.md step 4.5's "watch
    // for first-player advantage beyond ~55%").
    public required Interval SeatOneWinRate { get; init; }

    public required Interval SeatTwoWinRate { get; init; }

    // FINAL SCORE MARGIN from seat one's perspective. The low-variance counterpart to seat win
    // rate: a win rate throws away everything except one bit per game, so at realistic batch
    // sizes its interval is far too wide to answer "is first-player advantage beyond 55%."
    // Margin uses the size of the result as well as its direction, so its interval tightens much
    // faster. An interval excluding zero (MeanEstimate.Excludes()) is a real seat asymmetry;
    // one straddling zero is not, however lopsided the win rate looks.
    public required MeanEstimate FinalScoreMargin { get; init; }

    // |margin|, ignoring direction -- how DECISIVE games are, independent of who won. A healthy
    // ruleset wants this modest: a large mean absolute margin means games are blowouts settled
    // early, which is a balance smell even when the seat win rate is a perfect 50/50 because the
    // two effects are invisible to each other.
    public required MeanEstimate AbsoluteScoreMargin { get; init; }

    // Mean seat-one margin at each turn index, 1-based by position: index 0 is the margin after
    // turn 1. Averaged across every game still running at that turn, so later entries have
    // smaller samples (short games have dropped out) -- each carries its own Count, and the tail
    // where Count gets small should be read with that in mind. This is the shape step 4.2's
    // income-compounding finding needs: a lead that widens monotonically turn over turn is
    // compounding, one that oscillates is not.
    public required IReadOnlyList<MeanEstimate> ScoreMarginByTurn { get; init; }

    public required MeanEstimate GameLength { get; init; }

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

    // Of the decisions where at least one merge was legal, how often one was taken. The direct
    // form of step 4.2's merge question -- that step answered it with a bespoke instrumented run,
    // and this makes it a standing metric every batch reports. A rate near 1.0 would mean merge
    // IS the free strictly-better action the design worried about; step 4.2 measured ~0.33-0.39,
    // and this is where a content change would show that moving.
    public required Interval MergeTakeRate { get; init; }

    // Resource levels sampled at every turn boundary, split by eventual outcome and by seat.
    // The winner/loser split is the load-bearing one: those two populations have opposite
    // resource stories and averaging them together (as a single game-end mean does) reports a
    // midpoint that describes neither.
    public required ResourceProfile ResourcesWinners { get; init; }

    public required ResourceProfile ResourcesLosers { get; init; }

    public required ResourceProfile ResourcesSeatOne { get; init; }

    public required ResourceProfile ResourcesSeatTwo { get; init; }

    // CARDS DRAWN PER GAME -- total draws (opening hand plus every mid-game draw), the same
    // winner/loser split as the resource profiles and for the same reason: a losing seat has
    // typically played more turns catching up (or been eliminated early), so pooling the two
    // reports a midpoint that describes neither. Draw count is mechanical (RuleSet.CardsDrawnPerTurn
    // plus the opening hand, minus burn), so this reads mainly as a GAME LENGTH proxy split by
    // outcome -- a decisive winner/loser gap here says winners are closing games out before the
    // draw step compounds, not a claim about any one card.
    public required MeanEstimate CardsDrawnWinners { get; init; }

    public required MeanEstimate CardsDrawnLosers { get; init; }

    public required MeanEstimate CardsDrawnSeatOne { get; init; }

    public required MeanEstimate CardsDrawnSeatTwo { get; init; }

    // UNOPPOSED-SLOT OCCUPANCY -- the scoring rule's own denominator, pooled across both seats.
    //
    // Of all (scoring step x slot) pairs in the batch, the share where a seat held a creature
    // whose opposing slot was empty. This is what separates the two opposite fixes for a runaway
    // score: a LOW rate means slots are hard to keep unopposed and each one is worth a lot (so
    // PointsPerUnopposedCreature is the knob), while a HIGH rate means slots go unopposed easily
    // and the points follow inevitably (so board size, removal, or creature durability is the
    // knob). The score alone cannot tell these apart -- both produce fast games.
    public required Interval UnopposedSlotRate { get; init; }

    // Mean unopposed creatures held per scoring step, per seat. The same signal in the units the
    // rule pays out in: at PointsPerUnopposedCreature = 1 this IS the average points-per-turn
    // each seat earns, so comparing it against ScoreToWin / GameLength says whether scoring is
    // dominated by the unopposed rule or by something else.
    public required MeanEstimate UnopposedCreaturesPerStep { get; init; }

    // Longest run of consecutive scoring steps a seat held any unopposed creature, averaged over
    // both seats in every game. Step 4.2's finding was phrased as a streak (a player who never
    // sustained one 2+ steps won no sampled games), because sustained occupancy is what
    // compounds -- a total cannot express that.
    public required MeanEstimate LongestUnopposedStreak { get; init; }

    // Games in which NEITHER seat ever sustained an unopposed creature across consecutive
    // scoring steps. A high count here would mean the compounding step 4.2 found is not
    // operating in this configuration -- the direct check on whether a balance change actually
    // defused it.
    public required int GamesWithNoSustainedUnopposed { get; init; }

    // AFFORDABILITY PRESSURE, pooled. The share of (decision x held card) pairs where cost was
    // the sole blocker. Read alongside the resource profiles: high unspent resources with LOW
    // cost pressure means income genuinely exceeds what there is to buy, while high unspent
    // resources with HIGH cost pressure means players are holding the wrong resource types --
    // a type-chart or card-cost distribution problem, not an income-level one. Those are
    // different edits, and the resource numbers alone cannot choose between them.
    public required Interval CostPressure { get; init; }

    public required IReadOnlyDictionary<EndingType, int> EndingCounts { get; init; }

    public static MetricsReport From(IReadOnlyList<GameResult> games, RunProvenance? provenance = null)
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
        var mergeOffers = games.Sum(g => g.MergeOffersOne + g.MergeOffersTwo);

        var endingCounts = games
            .GroupBy(g => g.Ending)
            .ToDictionary(g => g.Key, g => g.Count());

        var margins = games.Select(g => (double)(g.ScoreOne - g.ScoreTwo)).ToList();

        var unopposedSlotTurns = games.Sum(g => g.UnopposedSlotTurnsOne + g.UnopposedSlotTurnsTwo);
        var scoringSteps = games.Sum(g => g.ScoringStepsOne + g.ScoringStepsTwo);

        // Slot-turns are counted per seat over SlotIndex.AllFor(seat), so the denominator is
        // (scoring steps x slots per seat), not scoring steps alone -- otherwise a seat holding
        // two unopposed slots on one step would read as a rate above 1.
        var slotOpportunities = scoringSteps * SlotIndex.SlotsPerPlayer;

        var blockedByCost = games.Sum(g =>
            g.CardsBlockedByCostOne.Values.Sum() + g.CardsBlockedByCostTwo.Values.Sum());
        var cardOffers = games.Sum(g =>
            g.CardOffersOne.Values.Sum() + g.CardOffersTwo.Values.Sum());

        return new MetricsReport
        {
            Provenance = provenance,
            GameCount = games.Count,
            SeatOneWinRate = Interval.Wilson(seatOneWins, games.Count),
            SeatTwoWinRate = Interval.Wilson(seatTwoWins, games.Count),
            FinalScoreMargin = MeanEstimate.From(margins),
            AbsoluteScoreMargin = MeanEstimate.From(margins.Select(Math.Abs)),
            ScoreMarginByTurn = ComputeMarginByTurn(games),
            GameLength = MeanEstimate.From(games.Select(g => (double)g.TurnCount)),
            CardStats = cardStats,
            MoveStats = moveStats,
            MoveUsageCount = moveUsageCount,
            MoveUsageRate = totalActions == 0 ? 0.0 : (double)moveUsageCount / totalActions,
            MergeCount = mergeCount,
            MergesPerGame = (double)mergeCount / games.Count,
            CreaturesPlayedCount = creaturesPlayedCount,
            MergesPerCreaturePlayed = creaturesPlayedCount == 0 ? 0.0 : (double)mergeCount / creaturesPlayedCount,
            MergeTakeRate = Interval.Wilson(Math.Min(mergeCount, mergeOffers), mergeOffers),
            ResourcesWinners = ResourceProfile.From(ResourceSamples(games, winners: true, seat: null)),
            ResourcesLosers = ResourceProfile.From(ResourceSamples(games, winners: false, seat: null)),
            ResourcesSeatOne = ResourceProfile.From(ResourceSamples(games, winners: null, seat: PlayerId.One)),
            ResourcesSeatTwo = ResourceProfile.From(ResourceSamples(games, winners: null, seat: PlayerId.Two)),
            CardsDrawnWinners = MeanEstimate.From(CardsDrawnSamples(games, winners: true, seat: null)),
            CardsDrawnLosers = MeanEstimate.From(CardsDrawnSamples(games, winners: false, seat: null)),
            CardsDrawnSeatOne = MeanEstimate.From(CardsDrawnSamples(games, winners: null, seat: PlayerId.One)),
            CardsDrawnSeatTwo = MeanEstimate.From(CardsDrawnSamples(games, winners: null, seat: PlayerId.Two)),
            UnopposedSlotRate = Interval.Wilson(
                Math.Min(unopposedSlotTurns, slotOpportunities), slotOpportunities),
            UnopposedCreaturesPerStep = MeanEstimate.From(PerStepUnopposed(games)),
            LongestUnopposedStreak = MeanEstimate.From(games
                .SelectMany(g => new[]
                {
                    (double)g.LongestUnopposedStreakOne,
                    (double)g.LongestUnopposedStreakTwo,
                })),

            // "Sustained" means 2+ consecutive steps, matching how step 4.2 phrased the finding.
            // A streak of 1 is a single unopposed step, which is common and not the compounding
            // phenomenon in question.
            GamesWithNoSustainedUnopposed = games.Count(g =>
                g.LongestUnopposedStreakOne < 2 && g.LongestUnopposedStreakTwo < 2),
            CostPressure = Interval.Wilson(blockedByCost, blockedByCost + cardOffers),
            EndingCounts = endingCounts,
        };
    }

    // One sample per (game, seat): that seat's mean unopposed creatures per scoring step. Sampled
    // per seat rather than pooling every step into one population because the two seats are the
    // unit a first-player-advantage question is asked about, and a seat with no scoring steps
    // (a game decided immediately) contributes nothing rather than a spurious zero.
    private static IEnumerable<double> PerStepUnopposed(IReadOnlyList<GameResult> games)
    {
        foreach (var game in games)
        {
            if (game.ScoringStepsOne > 0)
            {
                yield return (double)game.UnopposedSlotTurnsOne / game.ScoringStepsOne;
            }

            if (game.ScoringStepsTwo > 0)
            {
                yield return (double)game.UnopposedSlotTurnsTwo / game.ScoringStepsTwo;
            }
        }
    }

    // Collects per-turn resource samples filtered either by outcome (winner's samples vs.
    // loser's) or by seat. A drawn/non-terminating game has no winner, so it contributes to the
    // seat profiles but to neither outcome profile -- it genuinely has no winner's curve to add.
    private static IReadOnlyList<ResourcePool> ResourceSamples(
        IReadOnlyList<GameResult> games, bool? winners, PlayerId? seat)
    {
        var samples = new List<ResourcePool>();

        foreach (var game in games)
        {
            if (seat is { } wanted)
            {
                samples.AddRange(
                    wanted == PlayerId.One ? game.ResourcesByTurnOne : game.ResourcesByTurnTwo);
                continue;
            }

            if (game.Winner is not { } winner)
            {
                continue;
            }

            var winnerSamples =
                winner == PlayerId.One ? game.ResourcesByTurnOne : game.ResourcesByTurnTwo;
            var loserSamples =
                winner == PlayerId.One ? game.ResourcesByTurnTwo : game.ResourcesByTurnOne;

            samples.AddRange(winners == true ? winnerSamples : loserSamples);
        }

        return samples;
    }

    // One sample per (game, seat wanted): total cards drawn that game, mirroring
    // ResourceSamples' outcome/seat filtering exactly (same reasoning: a drawn/non-terminating
    // game has no winner, so it contributes to the seat profiles but neither outcome profile).
    private static IEnumerable<double> CardsDrawnSamples(
        IReadOnlyList<GameResult> games, bool? winners, PlayerId? seat)
    {
        foreach (var game in games)
        {
            if (seat is { } wanted)
            {
                yield return (wanted == PlayerId.One ? game.CardsDrawnOne : game.CardsDrawnTwo).Count;
                continue;
            }

            if (game.Winner is not { } winner)
            {
                continue;
            }

            var winnerCount = (winner == PlayerId.One ? game.CardsDrawnOne : game.CardsDrawnTwo).Count;
            var loserCount = (winner == PlayerId.One ? game.CardsDrawnTwo : game.CardsDrawnOne).Count;

            yield return winners == true ? winnerCount : loserCount;
        }
    }

    // Transposes per-game margin series into per-turn-index means. Games have different lengths,
    // so turn index t averages only over the games that reached turn t -- the alternative
    // (padding short games with their final margin) would manufacture a plateau that no game
    // actually played, and bias the tail toward whatever decided the shortest games.
    private static IReadOnlyList<MeanEstimate> ComputeMarginByTurn(IReadOnlyList<GameResult> games)
    {
        var longest = games.Max(g => g.ScoreMarginByTurn.Count);
        var byTurn = new List<MeanEstimate>(longest);

        for (var turn = 0; turn < longest; turn++)
        {
            var atTurn = new List<double>();
            foreach (var game in games)
            {
                if (turn < game.ScoreMarginByTurn.Count)
                {
                    atTurn.Add(game.ScoreMarginByTurn[turn]);
                }
            }

            byTurn.Add(MeanEstimate.From(atTurn));
        }

        return byTurn;
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

    private static void AccumulateOffers<TKey>(
        IReadOnlyDictionary<TKey, int> offers, Dictionary<TKey, int> into)
        where TKey : notnull
    {
        foreach (var (key, count) in offers)
        {
            into[key] = into.GetValueOrDefault(key) + count;
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
        var offerCounts = new Dictionary<string, int>();
        var blockedCounts = new Dictionary<string, int>();
        var survivalByCard = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var scoredWhileAlive = new Dictionary<string, int>(StringComparer.Ordinal);
        var lifetimeCount = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var game in games)
        {
            var oneWon = game.Winner == PlayerId.One;
            var twoWon = game.Winner == PlayerId.Two;

            AccountSeat(game.CardsPlayedOne, oneWon, playCounts, gamesPlayedIn, winsWhenPlayed);
            AccountSeat(game.CardsPlayedTwo, twoWon, playCounts, gamesPlayedIn, winsWhenPlayed);
            AccountSeat(game.CardsDrawnOne, oneWon, drawCounts, gamesDrawnIn, winsWhenDrawn);
            AccountSeat(game.CardsDrawnTwo, twoWon, drawCounts, gamesDrawnIn, winsWhenDrawn);
            AccumulateOffers(game.CardOffersOne, offerCounts);
            AccumulateOffers(game.CardOffersTwo, offerCounts);
            AccumulateOffers(game.CardsBlockedByCostOne, blockedCounts);
            AccumulateOffers(game.CardsBlockedByCostTwo, blockedCounts);

            foreach (var lifetime in game.CreatureSurvivalOne.Concat(game.CreatureSurvivalTwo))
            {
                if (!survivalByCard.TryGetValue(lifetime.CardId, out var list))
                {
                    list = [];
                    survivalByCard[lifetime.CardId] = list;
                }

                list.Add(lifetime.ScoringStepsSurvived);
                lifetimeCount[lifetime.CardId] = lifetimeCount.GetValueOrDefault(lifetime.CardId) + 1;

                if (lifetime.ScoredWhileAlive)
                {
                    scoredWhileAlive[lifetime.CardId] =
                        scoredWhileAlive.GetValueOrDefault(lifetime.CardId) + 1;
                }
            }
        }

        var everyCardId = new HashSet<string>(StringComparer.Ordinal);
        everyCardId.UnionWith(playCounts.Keys);
        everyCardId.UnionWith(drawCounts.Keys);
        everyCardId.UnionWith(offerCounts.Keys);
        everyCardId.UnionWith(blockedCounts.Keys);

        // Survival keys too: in a real game a creature with a lifetime was necessarily played,
        // so this adds nothing -- but the aggregation must not depend on that coincidence, or a
        // card whose only signal is survival silently vanishes from the report.
        everyCardId.UnionWith(survivalByCard.Keys);

        return everyCardId
            .Select(cardId => new CardStat
            {
                CardId = cardId,
                PlayCount = playCounts.GetValueOrDefault(cardId),
                GamesPlayedIn = gamesPlayedIn.GetValueOrDefault(cardId),
                WinsWhenPlayed = winsWhenPlayed.GetValueOrDefault(cardId),
                OfferCount = offerCounts.GetValueOrDefault(cardId),
                TimesDrawn = drawCounts.GetValueOrDefault(cardId),
                GamesDrawnIn = gamesDrawnIn.GetValueOrDefault(cardId),
                WinsWhenDrawn = winsWhenDrawn.GetValueOrDefault(cardId),
                BlockedByCostCount = blockedCounts.GetValueOrDefault(cardId),
                SurvivalSteps = MeanEstimate.From(
                    survivalByCard.GetValueOrDefault(cardId) ?? []),
                ScoredWhileAliveRate = Interval.Wilson(
                    scoredWhileAlive.GetValueOrDefault(cardId),
                    lifetimeCount.GetValueOrDefault(cardId)),
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

        // Offers arrive string-keyed (GameResult.MoveOffers* must be JSON-serializable) and are
        // decoded back to the tuple key the use counts are already in, so both halves of a
        // MoveStat come from one identity rather than two that might drift apart.
        var stringKeyedOffers = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var game in games)
        {
            AccountSeat(
                game.MovesUsedOne, game.Winner == PlayerId.One, useCounts, gamesUsedIn, winsWhenUsed);
            AccountSeat(
                game.MovesUsedTwo, game.Winner == PlayerId.Two, useCounts, gamesUsedIn, winsWhenUsed);
            AccumulateOffers(game.MoveOffersOne, stringKeyedOffers);
            AccumulateOffers(game.MoveOffersTwo, stringKeyedOffers);
        }

        var offerCounts = stringKeyedOffers.ToDictionary(
            kv => MoveKey.Split(kv.Key), kv => kv.Value);

        var everyMove = new HashSet<(string CardId, string MoveName)>(useCounts.Keys);
        everyMove.UnionWith(offerCounts.Keys);

        return everyMove
            .Select(key => new MoveStat
            {
                CardId = key.CardId,
                MoveName = key.MoveName,
                UseCount = useCounts.GetValueOrDefault(key),
                GamesUsedIn = gamesUsedIn.GetValueOrDefault(key),
                WinsWhenUsed = winsWhenUsed.GetValueOrDefault(key),
                OfferCount = offerCounts.GetValueOrDefault(key),
            })
            .OrderByDescending(s => s.UseCount)
            .ThenBy(s => s.CardId, StringComparer.Ordinal)
            .ThenBy(s => s.MoveName, StringComparer.Ordinal)
            .ToList();
    }
}
