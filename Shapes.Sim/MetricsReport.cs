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
// One bucket of CardStat.ByCopyCount: how decks running exactly N copies of a card fared.
//
// A class with an explicit Wilson interval rather than a bare (wins, decks) tuple because the
// interval is the whole point of splitting the buckets -- a 3-of bucket with 12 decks in it looks
// like a strong signal as a raw percentage and like the noise it is as an interval, and the
// buckets are exactly where the sample gets thin enough for that to matter.
public sealed class CopyCountStat
{
    public required int Copies { get; init; }

    public required int Decks { get; init; }

    public required int Wins { get; init; }

    public Interval WinRate => Interval.Wilson(Wins, Decks);
}

// One bucket of a deck-stats grouping: the decks whose measured property fell in [Low, High), and
// how often those decks' seats won.
//
// Half-open on the right so adjacent buckets never double-count a deck sitting exactly on a
// boundary -- a deck averaging exactly 2.2 belongs to 2.2-2.4, not to both. The final bucket is
// closed on both ends so the maximum sample is not silently dropped.
public sealed class DeckBucket
{
    public required double Low { get; init; }

    public required double High { get; init; }

    // Whether this is the last bucket, and therefore includes its own upper bound.
    public required bool IncludesHigh { get; init; }

    // (game, seat) pairs whose deck fell in this bucket. One deck played by one seat in one game
    // is one trial -- the same one-deck-one-trial rule CardStat.IncludedWinRate follows, and for
    // the same reason: a seat has exactly one win/loss, so it cannot contribute more than one.
    public required int Decks { get; init; }

    public required int Wins { get; init; }

    public Interval WinRate => Interval.Wilson(Wins, Decks);

    // Bucket label as "2.00-2.20". Formatted here rather than at each of the three writers so the
    // console, the CSV, and the HTML cannot disagree about what a bucket is called.
    public string Label(int decimals = 2) =>
        $"{Low.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture)}-"
        + $"{High.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture)}";
}

// One deck property, bucketed, with a win rate per bucket. The "do decks with X win more" family:
// mean card cost, creature count per type, and cost pips per type.
public sealed class DeckStat
{
    // What was measured -- "Mean card cost", "Spike creatures", "Anvil cost pips".
    public required string Name { get; init; }

    // How many decimals the buckets should be labelled to. Cost averages want 2 ("2.00-2.20");
    // whole-card counts want 0 ("10-12").
    public required int Decimals { get; init; }

    public required IReadOnlyList<DeckBucket> Buckets { get; init; }

    // Every deck that carried this property, across all buckets -- the denominator a reader needs
    // to judge whether the spread between buckets is worth anything.
    public int TotalDecks => Buckets.Sum(b => b.Decks);

    // Whether ANY pair of buckets has non-overlapping win-rate intervals. This is the honest
    // headline for a deckbuilding signal: a monotone-looking climb across buckets whose intervals
    // all overlap is noise, and reading a trend into it is exactly the mistake the intervals exist
    // to prevent.
    public bool HasSeparatedBuckets
    {
        get
        {
            var live = Buckets.Where(b => b.Decks > 0).ToList();
            for (var i = 0; i < live.Count; i++)
            {
                for (var j = i + 1; j < live.Count; j++)
                {
                    if (live[i].WinRate.High < live[j].WinRate.Low
                        || live[j].WinRate.High < live[i].WinRate.Low)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

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

    // Turns in which this card was offered at least one decision, and, of those, turns in which
    // it was played at least once. Same ratio as PlayTakeRate but deduplicated to "this turn,"
    // not "this decision" -- separates a card the agent wants but doesn't rush (offered every
    // turn, played most turns, just rarely the very first thing it does that turn -- a low
    // decision-level rate but a high turn-level one) from a card nobody wants at all (low on
    // both). PlayTakeRate alone cannot tell those apart; this is the number that does.
    public required int OfferedInTurns { get; init; }

    public required int PlayedInTurns { get; init; }

    public Interval PlayTakeRatePerTurn =>
        Interval.Wilson(Math.Min(PlayedInTurns, OfferedInTurns), OfferedInTurns);

    public Interval WinRateWhenPlayed => Interval.Wilson(WinsWhenPlayed, GamesPlayedIn);

    // Drawn-but-not-necessarily-played -- the starting hand plus every mid-game draw, so this
    // catches a card that's strong-but-skipped or weak-but-always-cast differently from
    // WinRateWhenPlayed. A game where two copies of the same card were drawn still counts once
    // here (GamesDrawnIn), matching GamesPlayedIn's own per-game, not per-copy, counting.
    public required int TimesDrawn { get; init; }

    public required int GamesDrawnIn { get; init; }

    public required int WinsWhenDrawn { get; init; }

    public Interval WinRateWhenDrawn => Interval.Wilson(WinsWhenDrawn, GamesDrawnIn);

    // INCLUDED WIN RATE -- of the (game, seat) pairs whose DECK ran at least one copy of this
    // card, how often that seat won. The deckbuilding counterpart of WinRateWhenPlayed: that one
    // is conditioned on the card being drawn AND cast, this one only on the deckbuilder having
    // chosen it.
    //
    // WHY IT IS THE RIGHT QUESTION FOR DECK MODES. A card that sits in the deck undrawn all game
    // still contributes to that deck's record, and those games are invisible to both
    // WinRateWhenPlayed and WinRateWhenDrawn -- which conditions on the card having shown up.
    // Conditioning on a draw is conditioning on an event the card's own cost and the game's length
    // influence, so the played/drawn rates quietly select on games that went a certain way. Deck
    // inclusion is decided before the game starts and is independent of everything that happens
    // in it, which is what makes this the one card win-rate that is not conditioned on
    // mid-game selection.
    //
    // UNDER THE DEFAULT DECK THIS IS UNINFORMATIVE, BY CONSTRUCTION. One-of-each means every deck
    // runs every card, so DecksIncludedIn is 2 x GameCount for every card and the rate collapses
    // to the pooled seat win rate -- identical for all cards. That is not a bug to correct; it is
    // the honest answer to "how much did including this card matter" when inclusion was never a
    // choice. Read it only for --deck random (and --deck custom across varied lists), which is
    // where inclusion actually varies. The same compression caveat that applies to
    // WinRateWhenPlayed under symmetric decks applies here, only total rather than partial.
    public required int DecksIncludedIn { get; init; }

    public required int WinsWhenIncluded { get; init; }

    public Interval IncludedWinRate => Interval.Wilson(WinsWhenIncluded, DecksIncludedIn);

    // The same measurement split by HOW MANY copies the deck ran, indexed by copy count (1, 2,
    // 3...). This is what answers the "should more copies weight it higher" question empirically
    // rather than by assumption: if a 3-of wins more than a 1-of, the card rewards commitment and
    // the trend shows up here as a monotone climb across the buckets.
    //
    // Deliberately NOT folded into IncludedWinRate as a copy-weighted numerator/denominator.
    // Weighting would let one deck contribute three "observations" of a single game outcome --
    // three counts that are perfectly correlated, since they share one win/loss. Wilson's interval
    // assumes independent trials, so a weighted version would report an interval up to sqrt(3)
    // narrower than the evidence supports, and it would do so for exactly the cards people run
    // three of. Keeping the headline rate unweighted (one deck = one trial) and putting the copy
    // signal in a breakdown gives both numbers without either lying about its confidence.
    public required IReadOnlyDictionary<int, CopyCountStat> ByCopyCount { get; init; }

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

    // Per-turn counterpart of UseTakeRate, same reasoning as CardStat.PlayTakeRatePerTurn: a
    // move used reliably once per turn but rarely used FIRST reads as low-take-rate identically
    // to a move nobody wants, on the decision-level denominator alone.
    public required int OfferedInTurns { get; init; }

    public required int UsedInTurns { get; init; }

    public Interval UseTakeRatePerTurn =>
        Interval.Wilson(Math.Min(UsedInTurns, OfferedInTurns), OfferedInTurns);

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

// The per-turn counterpart of ResourceProfile -- ResourceProfile collapses a seat's whole game
// into one mean per resource type, which is the right shape for "winners vs. losers" (a
// game-outcome question) but the wrong shape for "does income outpace spending as the game goes
// on" (a turn-by-turn question, same reasoning as ScoreMarginByTurn/HandSizeByTurn*). One series
// per resource type rather than a single pooled total, because Spike/Anvil/Wheel building up
// unevenly (e.g. Anvil piling up while Spike/Wheel stay spent) is invisible in a summed total and
// is exactly the kind of type-distribution problem CostPressure already flags at the batch level
// -- this is the per-turn, per-type view of the same question.
public sealed class ResourceSeriesProfile
{
    public required IReadOnlyList<MeanEstimate> Spike { get; init; }

    public required IReadOnlyList<MeanEstimate> Anvil { get; init; }

    public required IReadOnlyList<MeanEstimate> Wheel { get; init; }
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

    // Hand size at each turn index, per seat -- same per-turn-index averaging as
    // ScoreMarginByTurn (turn t averages only over games that reached it), split by seat rather
    // than expressed as a single margin because hand size has no natural "P1 minus P2" framing:
    // both seats being low is a starved economy, both being high is a bloated one, and those are
    // opposite findings that a difference would erase. Read alongside CostPressure and the
    // resource profiles: a hand hovering near 0-1 most turns with LOW cost pressure means the
    // hand itself is the bottleneck (not enough draw), while low hand size with HIGH cost
    // pressure means resources are the bottleneck and the hand is just waiting on affordability.
    // A hand routinely at 6+ suggests income/draw outpacing what a turn can spend, or removal/
    // board clears resetting the board without spending the hand down.
    public required IReadOnlyList<MeanEstimate> HandSizeByTurnOne { get; init; }

    public required IReadOnlyList<MeanEstimate> HandSizeByTurnTwo { get; init; }

    // Resource levels at each turn index, per seat, per resource type -- the per-turn
    // counterpart of ResourcesSeatOne/Two below (those collapse a whole game into one mean; this
    // is the curve). Same reasoning and same turn-alignment rule as ScoreMarginByTurn/
    // HandSizeByTurn*, split by seat rather than winner/loser since (like hand size) there is no
    // "P1 minus P2" framing for a resource level -- both seats sitting on a pile of unspent Anvil
    // is one finding, one seat starved of Spike while the other floods is a different one, and
    // pooling would erase the distinction. Read against CostPressure per resource type: a level
    // that climbs turn over turn with low cost pressure for that type means income outpaces what
    // there is to spend it on; climbing with high cost pressure means the type itself is the
    // bottleneck (nothing affordable asks for it), not the amount.
    public required ResourceSeriesProfile ResourcesByTurnOne { get; init; }

    public required ResourceSeriesProfile ResourcesByTurnTwo { get; init; }

    // BOARD PRESENCE by turn, per seat -- slots occupied and combined CURRENT (not max) creature
    // health, sampled at the same scoring-step boundary as UnopposedSlotRate. Neither is visible
    // from the unopposed-slot metrics alone: a seat can hold every slot unopposed with three
    // 1-health creatures or one full-health creature and two empty slots, and those are very
    // different board states producing the identical score. Combined health, not slot count
    // alone, is what separates merely "present" from "actually threatening" -- a mauled board
    // occupies the same slots as a fresh one but defends nothing like as well. Same "no natural
    // P1-minus-P2 framing" reasoning as HandSizeByTurn*/ResourcesByTurn*: one seat's board being
    // empty and the other's full is one finding, both being thin is a different one, and a
    // difference would erase which is which.
    public required IReadOnlyList<MeanEstimate> SlotsOccupiedByTurnOne { get; init; }

    public required IReadOnlyList<MeanEstimate> SlotsOccupiedByTurnTwo { get; init; }

    public required IReadOnlyList<MeanEstimate> CombinedHealthByTurnOne { get; init; }

    public required IReadOnlyList<MeanEstimate> CombinedHealthByTurnTwo { get; init; }

    public required MeanEstimate GameLength { get; init; }

    // The same lengths as a distribution rather than a centre. Read the median and p95 first: a
    // mean well above the median means a long right tail, which for game length means some games
    // are not ending rather than all games being longer. See Distribution.
    //
    // NOT `required`, unlike every field above it, and neither are the fatigue fields below --
    // deliberately. `--compare` reads two saved --metrics-json files, and the whole point of that
    // command is diffing a run against an OLDER baseline; every report written before step 5b
    // lacks these properties, and a `required` member makes System.Text.Json reject the file
    // outright rather than defaulting it. A missing distribution is a real, expected state ("this
    // run predates the metric"), not malformed input, so it defaults and the compare report
    // renders those rows as absent. The alternative -- rewriting every archived balance/ report --
    // would destroy the provenance those files exist to preserve.
    public Distribution GameLengthDistribution { get; init; }

    // FATIGUE (PLAN.md step 5b), per seat. DeckExhaustionRate is the share of games where that
    // seat ever started a turn with an empty deck; FirstFatigueTurn is when it first happened,
    // over only the games where it happened at all (so it reads as "when it fires, it fires
    // around turn N," not diluted by games that never reached it).
    public Interval DeckExhaustionRateSeatOne { get; init; }

    public Interval DeckExhaustionRateSeatTwo { get; init; }

    public MeanEstimate FirstFatigueTurnSeatOne { get; init; }

    public MeanEstimate FirstFatigueTurnSeatTwo { get; init; }

    // Total fatigue score conceded by each seat across the batch -- the raw magnitude behind the
    // rates above.
    public int FatigueScoreConcededSeatOne { get; init; }

    public int FatigueScoreConcededSeatTwo { get; init; }

    // Games where the winner's final margin was no larger than the fatigue score they were handed
    // -- i.e. games the timer decided rather than play. The rule is meant to be a backstop that
    // rarely matters; a large share here means it has become the win condition, which is the
    // specific way this change could go wrong.
    public Interval GamesDecidedByFatigue { get; init; }

    public required IReadOnlyList<CardStat> CardStats { get; init; }

    // DECK STATS -- win rate grouped by a deck's own measurable properties (mean card cost,
    // creatures per type, cost pips per type), asking "what kind of deck wins" rather than "which
    // card wins".
    //
    // Empty under --deck default, and correctly so: one-of-each means every deck is the SAME deck,
    // so there is exactly one value of every property and nothing to group by. These only carry
    // information when decks actually vary (--deck random, or custom runs across varied lists).
    public required IReadOnlyList<DeckStat> DeckStats { get; init; }

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
            ScoreMarginByTurn = ComputeSeriesByTurn(games, g => g.ScoreMarginByTurn),
            HandSizeByTurnOne = ComputeSeriesByTurn(games, g => g.HandSizeByTurnOne),
            HandSizeByTurnTwo = ComputeSeriesByTurn(games, g => g.HandSizeByTurnTwo),
            ResourcesByTurnOne = ComputeResourceSeriesByTurn(games, g => g.ResourcesByTurnOne),
            ResourcesByTurnTwo = ComputeResourceSeriesByTurn(games, g => g.ResourcesByTurnTwo),
            SlotsOccupiedByTurnOne = ComputeSeriesByTurn(games, g => g.SlotsOccupiedByTurnOne),
            SlotsOccupiedByTurnTwo = ComputeSeriesByTurn(games, g => g.SlotsOccupiedByTurnTwo),
            CombinedHealthByTurnOne = ComputeSeriesByTurn(games, g => g.CombinedHealthByTurnOne),
            CombinedHealthByTurnTwo = ComputeSeriesByTurn(games, g => g.CombinedHealthByTurnTwo),
            GameLength = MeanEstimate.From(games.Select(g => (double)g.TurnCount)),
            GameLengthDistribution = Distribution.From(games.Select(g => (double)g.TurnCount)),
            DeckExhaustionRateSeatOne =
                Interval.Wilson(games.Count(g => g.FatigueTurnsOne > 0), games.Count),
            DeckExhaustionRateSeatTwo =
                Interval.Wilson(games.Count(g => g.FatigueTurnsTwo > 0), games.Count),

            // Conditioned on it happening: a game that never exhausted contributes no sample
            // rather than a zero, so this reads "when a seat decks out, it decks out around turn
            // N." Averaging in the games that never got there would pull it toward zero and make
            // an early-fatigue format look identical to one where fatigue is rare.
            FirstFatigueTurnSeatOne = MeanEstimate.From(
                games.Where(g => g.FirstFatigueTurnOne is not null)
                     .Select(g => (double)g.FirstFatigueTurnOne!.Value)),
            FirstFatigueTurnSeatTwo = MeanEstimate.From(
                games.Where(g => g.FirstFatigueTurnTwo is not null)
                     .Select(g => (double)g.FirstFatigueTurnTwo!.Value)),
            FatigueScoreConcededSeatOne = games.Sum(g => g.FatigueScoreGainedTwo),
            FatigueScoreConcededSeatTwo = games.Sum(g => g.FatigueScoreGainedOne),
            GamesDecidedByFatigue =
                Interval.Wilson(games.Count(DecidedByFatigue), games.Count),
            CardStats = cardStats,
            DeckStats = ComputeDeckStats(games),
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

    // Transposes a per-game per-turn series (margin, hand size, ...) into per-turn-index means.
    // Games have different lengths, so turn index t averages only over the games that reached
    // turn t -- the alternative (padding short games with their final value) would manufacture a
    // plateau that no game actually played, and bias the tail toward whatever decided the
    // shortest games. Shared by ScoreMarginByTurn and HandSizeByTurn* -- same transpose, applied
    // to whichever per-game series the caller selects.
    // Whether the winner's margin was no larger than the fatigue score they were handed -- i.e.
    // remove the fatigue points and the game was tied or lost. That is the honest reading of
    // "decided by the timer": not merely that fatigue fired, but that it was load-bearing.
    //
    // A drawn/non-terminating game has no winner and cannot have been decided by anything, so it
    // is excluded rather than counted either way.
    private static bool DecidedByFatigue(GameResult game)
    {
        if (game.Winner is not { } winner)
        {
            return false;
        }

        var winnerIsOne = winner == PlayerId.One;
        var margin = winnerIsOne ? game.ScoreOne - game.ScoreTwo : game.ScoreTwo - game.ScoreOne;
        var fatigueGained = winnerIsOne ? game.FatigueScoreGainedOne : game.FatigueScoreGainedTwo;

        return fatigueGained > 0 && margin <= fatigueGained;
    }

    private static IReadOnlyList<MeanEstimate> ComputeSeriesByTurn(
        IReadOnlyList<GameResult> games, Func<GameResult, IReadOnlyList<int>> series) =>
        ComputeSeriesByTurn(games, g => series(g), v => v);

    // General form: series(game) yields the raw per-turn samples of any type T (an int for
    // margin/hand size, a ResourcePool for resource-by-turn), and value() projects out the double
    // this particular caller wants to average -- e.g. r => r.Spike for one resource type. Same
    // transpose as the int-only overload above, generalized so ResourceSeriesByTurn can reuse it
    // once per resource type instead of re-deriving the turn-alignment logic three times.
    private static IReadOnlyList<MeanEstimate> ComputeSeriesByTurn<T>(
        IReadOnlyList<GameResult> games, Func<GameResult, IReadOnlyList<T>> series, Func<T, double> value)
    {
        var longest = games.Max(g => series(g).Count);
        var byTurn = new List<MeanEstimate>(longest);

        for (var turn = 0; turn < longest; turn++)
        {
            var atTurn = new List<double>();
            foreach (var game in games)
            {
                var values = series(game);
                if (turn < values.Count)
                {
                    atTurn.Add(value(values[turn]));
                }
            }

            byTurn.Add(MeanEstimate.From(atTurn));
        }

        return byTurn;
    }

    // One ComputeSeriesByTurn transpose per resource type, over the same ResourcesByTurn* raw
    // samples ResourceProfile.From already pools into a single game-long mean -- this is the
    // per-turn-index counterpart, split by type instead of collapsed.
    private static ResourceSeriesProfile ComputeResourceSeriesByTurn(
        IReadOnlyList<GameResult> games, Func<GameResult, IReadOnlyList<ResourcePool>> series) =>
        new()
        {
            Spike = ComputeSeriesByTurn(games, series, r => r.Spike),
            Anvil = ComputeSeriesByTurn(games, series, r => r.Anvil),
            Wheel = ComputeSeriesByTurn(games, series, r => r.Wheel),
        };

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

    // One seat's decklist, for the included-win-rate metric. Each distinct card in the deck counts
    // the (game, seat) pair ONCE toward that card's inclusion denominator regardless of how many
    // copies are run -- one deck is one trial, since all its copies share a single win/loss. The
    // copy count is recorded separately as a bucket key, which is where the "do more copies win
    // more" signal lives without contaminating the headline rate's independence. See
    // CardStat.ByCopyCount.
    //
    // A seat whose deck was not recorded (an empty dictionary -- every pre-deck GameResult and
    // every test fixture that doesn't care) contributes nothing, so this is a no-op rather than a
    // source of zero-count noise for those.
    private static void AccountDeck(
        IReadOnlyDictionary<string, int> deck, bool seatWon,
        Dictionary<string, int> decksIncludedIn, Dictionary<string, int> winsWhenIncluded,
        Dictionary<(string, int), int> decksByCopies, Dictionary<(string, int), int> winsByCopies)
    {
        foreach (var (cardId, copies) in deck)
        {
            // A recorded count of zero means the card is absent, not included -- Deck.CountsById
            // never emits one, but a hand-built fixture or a round-tripped JSON could, and
            // counting it would put a card in the denominator of a deck that does not run it.
            if (copies <= 0)
            {
                continue;
            }

            decksIncludedIn[cardId] = decksIncludedIn.GetValueOrDefault(cardId) + 1;
            var bucket = (cardId, copies);
            decksByCopies[bucket] = decksByCopies.GetValueOrDefault(bucket) + 1;

            if (seatWon)
            {
                winsWhenIncluded[cardId] = winsWhenIncluded.GetValueOrDefault(cardId) + 1;
                winsByCopies[bucket] = winsByCopies.GetValueOrDefault(bucket) + 1;
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

    // The per-copy-count buckets for one card, ordered by copy count so a reader sees 1, 2, 3 in
    // the order the trend would run. Only counts that actually occurred get a bucket -- an absent
    // "3 copies" entry means no deck ran three, which is different from three copies having been
    // tried and lost, and an empty zero-count bucket would read as the latter.
    private static IReadOnlyDictionary<int, CopyCountStat> BuildCopyBuckets(
        string cardId, Dictionary<(string CardId, int Copies), int> decksByCopies,
        Dictionary<(string CardId, int Copies), int> winsByCopies)
    {
        var buckets = new SortedDictionary<int, CopyCountStat>();

        foreach (var ((id, copies), decks) in decksByCopies)
        {
            if (!string.Equals(id, cardId, StringComparison.Ordinal))
            {
                continue;
            }

            buckets[copies] = new CopyCountStat
            {
                Copies = copies,
                Decks = decks,
                Wins = winsByCopies.GetValueOrDefault((id, copies)),
            };
        }

        return buckets;
    }

    // Win rate grouped by deck property. One (game, seat) pair per deck played -- the same
    // one-deck-one-trial rule the included-win-rate metric uses.
    //
    // Returns EMPTY when no deck property varies across the batch, which is the --deck default
    // case (every deck identical, so every sample lands in one bucket and the "grouping" says
    // nothing). Checked per property rather than globally so a batch that varies cost but not
    // type count reports the cost grouping alone rather than all-or-nothing.
    private static IReadOnlyList<DeckStat> ComputeDeckStats(IReadOnlyList<GameResult> games)
    {
        // (property value, did that seat win) for every deck played in the batch.
        var samples = new List<(DeckProfile Profile, bool Won)>();
        foreach (var game in games)
        {
            if (game.DeckProfileOne is { } one)
            {
                samples.Add((one, game.Winner == PlayerId.One));
            }

            if (game.DeckProfileTwo is { } two)
            {
                samples.Add((two, game.Winner == PlayerId.Two));
            }
        }

        if (samples.Count == 0)
        {
            return [];
        }

        var stats = new List<DeckStat>();

        // Mean card cost in 0.2-wide buckets -- the requested "2.0-2.2, 2.2-2.4" grouping, and the
        // headline deckbuilding axis since it is what the random generator's own constraint
        // targets.
        Add(stats, "Mean card cost", samples, p => p.MeanCost, width: 0.2, decimals: 2);

        // Cards DEMANDING each type by play cost -- the quantity the generator's own MinPerType
        // constrains, and the one that answers "is this deck bottlenecked on one resource while
        // two pile up unspent". Listed before the board-type counts because it is the constrained
        // one and therefore the one a reader is usually checking.
        //
        // Width 2 for counts (a 40-card deck spans maybe 10-20 of a type, so 1-wide buckets would
        // be mostly noise and 5-wide would be two buckets) and 4 for pips, which range wider.
        foreach (var (type, label) in new[]
        {
            (ResourceType.Spike, "Spike"),
            (ResourceType.Anvil, "Anvil"),
            (ResourceType.Wheel, "Wheel"),
        })
        {
            Add(stats, $"{label} cards (by cost)", samples, p => p.CardsOfType(type), width: 2, decimals: 0);
        }

        // Creature counts by BOARD type -- what the deck fields, as opposed to what it pays for.
        // A deck can demand plenty of spike while fielding few spike creatures (spending it on
        // spells instead), and that is a different shape of deck than the cost counts alone show.
        foreach (var (type, label) in new[]
        {
            (ResourceType.Spike, "Spike"),
            (ResourceType.Anvil, "Anvil"),
            (ResourceType.Wheel, "Wheel"),
        })
        {
            Add(stats, $"{label} creatures", samples, p => p.CreaturesOfType(type), width: 2, decimals: 0);
        }

        foreach (var (type, label) in new[]
        {
            (ResourceType.Spike, "Spike"),
            (ResourceType.Anvil, "Anvil"),
            (ResourceType.Wheel, "Wheel"),
        })
        {
            Add(stats, $"{label} cost pips", samples, p => p.CostOfType(type), width: 4, decimals: 0);
        }

        return stats;
    }

    // Buckets one property and appends it, unless the property does not vary (every deck the same
    // value) -- in which case there is no grouping to report and the row is skipped entirely.
    private static void Add(
        List<DeckStat> stats, string name, IReadOnlyList<(DeckProfile Profile, bool Won)> samples,
        Func<DeckProfile, double> value, double width, int decimals)
    {
        var values = samples.Select(s => value(s.Profile)).ToList();
        var min = values.Min();
        var max = values.Max();

        if (max - min < 1e-9)
        {
            return;
        }

        // Buckets are anchored to multiples of the width rather than to the observed minimum, so
        // "2.0-2.2" means the same range in every run and two reports stay comparable. Anchoring
        // to the min would shift every boundary whenever the luckiest deck in a batch changed.
        var first = Math.Floor(min / width) * width;
        var count = (int)Math.Floor((max - first) / width) + 1;

        var decks = new int[count];
        var wins = new int[count];

        foreach (var (profile, won) in samples)
        {
            // Clamped rather than trusted: floating-point division can land the maximum sample one
            // index past the end, and an out-of-range write here would be a crash in a reporting
            // path rather than a visible wrong number.
            var index = Math.Clamp((int)Math.Floor((value(profile) - first) / width), 0, count - 1);
            decks[index]++;
            if (won)
            {
                wins[index]++;
            }
        }

        var buckets = new List<DeckBucket>(count);
        for (var i = 0; i < count; i++)
        {
            // Empty interior buckets are KEPT: a gap in the middle of a distribution is
            // information ("no deck ran 14-16 spike creatures"), and dropping it would make the
            // remaining buckets look adjacent when they are not.
            buckets.Add(new DeckBucket
            {
                Low = first + (i * width),
                High = first + ((i + 1) * width),
                IncludesHigh = i == count - 1,
                Decks = decks[i],
                Wins = wins[i],
            });
        }

        stats.Add(new DeckStat { Name = name, Decimals = decimals, Buckets = buckets });
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
        var offeredInTurns = new Dictionary<string, int>();
        var playedInTurns = new Dictionary<string, int>();
        var survivalByCard = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var scoredWhileAlive = new Dictionary<string, int>(StringComparer.Ordinal);
        var lifetimeCount = new Dictionary<string, int>(StringComparer.Ordinal);

        // Included-win-rate accumulators. Keyed by card id for the headline rate, and by
        // (card id, copies) for the by-copy-count breakdown.
        var decksIncludedIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var winsWhenIncluded = new Dictionary<string, int>(StringComparer.Ordinal);
        var decksByCopies = new Dictionary<(string CardId, int Copies), int>();
        var winsByCopies = new Dictionary<(string CardId, int Copies), int>();

        foreach (var game in games)
        {
            var oneWon = game.Winner == PlayerId.One;
            var twoWon = game.Winner == PlayerId.Two;

            AccountDeck(game.DeckOne, oneWon, decksIncludedIn, winsWhenIncluded, decksByCopies, winsByCopies);
            AccountDeck(game.DeckTwo, twoWon, decksIncludedIn, winsWhenIncluded, decksByCopies, winsByCopies);

            AccountSeat(game.CardsPlayedOne, oneWon, playCounts, gamesPlayedIn, winsWhenPlayed);
            AccountSeat(game.CardsPlayedTwo, twoWon, playCounts, gamesPlayedIn, winsWhenPlayed);
            AccountSeat(game.CardsDrawnOne, oneWon, drawCounts, gamesDrawnIn, winsWhenDrawn);
            AccountSeat(game.CardsDrawnTwo, twoWon, drawCounts, gamesDrawnIn, winsWhenDrawn);
            AccumulateOffers(game.CardOffersOne, offerCounts);
            AccumulateOffers(game.CardOffersTwo, offerCounts);
            AccumulateOffers(game.CardsBlockedByCostOne, blockedCounts);
            AccumulateOffers(game.CardsBlockedByCostTwo, blockedCounts);
            AccumulateOffers(game.CardOffersByTurnOne, offeredInTurns);
            AccumulateOffers(game.CardOffersByTurnTwo, offeredInTurns);
            AccumulateOffers(game.CardPlaysByTurnOne, playedInTurns);
            AccumulateOffers(game.CardPlaysByTurnTwo, playedInTurns);

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

        // Deck keys: a card can be in every deck and never once be drawn, offered, or played --
        // in which case inclusion is its ONLY signal and this union is the only thing that puts
        // it in the report at all. That card (in decks, never seen) is a real finding worth
        // surfacing, not a row to drop.
        everyCardId.UnionWith(decksIncludedIn.Keys);

        return everyCardId
            .Select(cardId => new CardStat
            {
                CardId = cardId,
                PlayCount = playCounts.GetValueOrDefault(cardId),
                GamesPlayedIn = gamesPlayedIn.GetValueOrDefault(cardId),
                WinsWhenPlayed = winsWhenPlayed.GetValueOrDefault(cardId),
                OfferCount = offerCounts.GetValueOrDefault(cardId),
                OfferedInTurns = offeredInTurns.GetValueOrDefault(cardId),
                PlayedInTurns = playedInTurns.GetValueOrDefault(cardId),
                TimesDrawn = drawCounts.GetValueOrDefault(cardId),
                GamesDrawnIn = gamesDrawnIn.GetValueOrDefault(cardId),
                WinsWhenDrawn = winsWhenDrawn.GetValueOrDefault(cardId),
                BlockedByCostCount = blockedCounts.GetValueOrDefault(cardId),
                DecksIncludedIn = decksIncludedIn.GetValueOrDefault(cardId),
                WinsWhenIncluded = winsWhenIncluded.GetValueOrDefault(cardId),
                ByCopyCount = BuildCopyBuckets(cardId, decksByCopies, winsByCopies),
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
        var stringKeyedOfferedInTurns = new Dictionary<string, int>(StringComparer.Ordinal);
        var stringKeyedUsedInTurns = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var game in games)
        {
            AccountSeat(
                game.MovesUsedOne, game.Winner == PlayerId.One, useCounts, gamesUsedIn, winsWhenUsed);
            AccountSeat(
                game.MovesUsedTwo, game.Winner == PlayerId.Two, useCounts, gamesUsedIn, winsWhenUsed);
            AccumulateOffers(game.MoveOffersOne, stringKeyedOffers);
            AccumulateOffers(game.MoveOffersTwo, stringKeyedOffers);
            AccumulateOffers(game.MoveOffersByTurnOne, stringKeyedOfferedInTurns);
            AccumulateOffers(game.MoveOffersByTurnTwo, stringKeyedOfferedInTurns);
            AccumulateOffers(game.MoveUsesByTurnOne, stringKeyedUsedInTurns);
            AccumulateOffers(game.MoveUsesByTurnTwo, stringKeyedUsedInTurns);
        }

        var offerCounts = stringKeyedOffers.ToDictionary(
            kv => MoveKey.Split(kv.Key), kv => kv.Value);
        var offeredInTurns = stringKeyedOfferedInTurns.ToDictionary(
            kv => MoveKey.Split(kv.Key), kv => kv.Value);
        var usedInTurns = stringKeyedUsedInTurns.ToDictionary(
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
                OfferedInTurns = offeredInTurns.GetValueOrDefault(key),
                UsedInTurns = usedInTurns.GetValueOrDefault(key),
            })
            .OrderByDescending(s => s.UseCount)
            .ThenBy(s => s.CardId, StringComparer.Ordinal)
            .ThenBy(s => s.MoveName, StringComparer.Ordinal)
            .ToList();
    }
}
