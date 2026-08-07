using System.Diagnostics;
using Shapes.Ai.Agents;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Sim;

// Plays one game to completion, headless. Mirrors Shapes.Console/Program.cs's loop (state
// construction, deck setup, AdvanceToActions, the while-!IsOver loop) with the rendering and
// human-input branches removed -- there is never a human seat in a batch run.
public static class GameRunner
{
    // Safety valve, not an expected length: FuzzHarnessTests bounds random play at 2000 actions
    // over the real card set, and agent play is never longer than random play. A game that hits
    // this cap is reported as EndingType.NonTerminating instead of hanging the batch -- PLAN.md
    // step 4.5 calls out "non-terminating games" as a balance smell to watch for, so a stall
    // needs to surface as a counted outcome, not a stuck process.
    private const int MaxTurns = 500;

    public static GameResult Play(
        string agentOneKind, string agentTwoKind, ulong seed, CardDatabase cards, RuleSet rules,
        int iterations)
    {
        var stopwatch = Stopwatch.StartNew();

        var random = new SeededRandom(seed);

        // Same derived-stream scheme as the console client: distinct multipliers so neither
        // agent's draws interleave with the other's, and so swapping one agent's kind never
        // changes the other's decisions for the same seed.
        var agentOne = AgentFactory.Build(agentOneKind, seed * 7919, cards, iterations);
        var agentTwo = AgentFactory.Build(agentTwoKind, seed * 104729, cards, iterations);
        var agents = new Dictionary<PlayerId, IAgent>
        {
            [PlayerId.One] = agentOne,
            [PlayerId.Two] = agentTwo,
        };

        var state = new GameState(rules, random, PlayerId.One);
        var cardsDrawnOne = new List<string>();
        var cardsDrawnTwo = new List<string>();

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(cards.BuildSymmetricDeck(rules));
            player.ShuffleDeck(random);
            var openingHand = player.Draw(rules.StartingHandSize);
            (playerId == PlayerId.One ? cardsDrawnOne : cardsDrawnTwo).AddRange(openingHand);
        }

        state.AdvanceToActions();
        var harvestedEventCount = 0;
        HarvestDrawEvents(state, cardsDrawnOne, cardsDrawnTwo, ref harvestedEventCount);

        var actionCount = 0;
        var actionCountsByKind = new Dictionary<ActionKind, int>();
        var cardsPlayedOne = new List<string>();
        var cardsPlayedTwo = new List<string>();
        var movesUsedOne = new List<(string CardId, string MoveName)>();
        var movesUsedTwo = new List<(string CardId, string MoveName)>();
        var creaturesPlayedOne = 0;
        var creaturesPlayedTwo = 0;
        var mergeCountOne = 0;
        var mergeCountTwo = 0;

        var cardOffersOne = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardOffersTwo = new Dictionary<string, int>(StringComparer.Ordinal);
        var moveOffersOne = new Dictionary<string, int>(StringComparer.Ordinal);
        var moveOffersTwo = new Dictionary<string, int>(StringComparer.Ordinal);
        // Boxed as single-element arrays so the shared CountOffers helper can bump whichever seat
        // is acting without a conditional `ref`, which C# does not allow.
        var mergeOffersOne = new int[1];
        var mergeOffersTwo = new int[1];

        // PER-TURN denominators: "was this offered/played at all THIS turn," not "at how many
        // decision points" -- deduplicated per (turn number, id) via a per-seat HashSet reset
        // each time TurnNumber advances, mirroring AccountSeat's per-game dedup one level down.
        var cardOffersByTurnOne = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardOffersByTurnTwo = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardPlaysByTurnOne = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardPlaysByTurnTwo = new Dictionary<string, int>(StringComparer.Ordinal);
        var moveOffersByTurnOne = new Dictionary<string, int>(StringComparer.Ordinal);
        var moveOffersByTurnTwo = new Dictionary<string, int>(StringComparer.Ordinal);
        var moveUsesByTurnOne = new Dictionary<string, int>(StringComparer.Ordinal);
        var moveUsesByTurnTwo = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardsSeenThisTurnOne = new HashSet<string>(StringComparer.Ordinal);
        var cardsSeenThisTurnTwo = new HashSet<string>(StringComparer.Ordinal);
        var movesSeenThisTurnOne = new HashSet<string>(StringComparer.Ordinal);
        var movesSeenThisTurnTwo = new HashSet<string>(StringComparer.Ordinal);
        var cardsPlayedThisTurnOne = new HashSet<string>(StringComparer.Ordinal);
        var cardsPlayedThisTurnTwo = new HashSet<string>(StringComparer.Ordinal);
        var movesUsedThisTurnOne = new HashSet<string>(StringComparer.Ordinal);
        var movesUsedThisTurnTwo = new HashSet<string>(StringComparer.Ordinal);
        var perTurnTurnNumber = 0;

        var scoreMarginByTurn = new List<int>();
        var resourcesByTurnOne = new List<ResourcePool>();
        var resourcesByTurnTwo = new List<ResourcePool>();
        var handSizeByTurnOne = new List<int>();
        var handSizeByTurnTwo = new List<int>();
        var lastSampledTurn = 0;

        var trackers = new Dictionary<PlayerId, SeatTracker>
        {
            [PlayerId.One] = new SeatTracker(PlayerId.One),
            [PlayerId.Two] = new SeatTracker(PlayerId.Two),
        };

        // Scoring steps, not turns: each player scores once per turn, so this advances twice per
        // TurnNumber. Creature lifetimes are measured in these because scoring steps are the unit
        // the +1-unopposed rule actually pays out in -- a creature that survives "two turns" has
        // been scored past twice, which is what its survival is worth.
        var scoringStep = 0;
        var harvestedCreatureEvents = 0;

        while (!state.IsOver && state.TurnNumber <= MaxTurns)
        {
            var agent = agents[state.ActivePlayer];

            // Counted BEFORE the agent chooses and from the same generator call the agent's own
            // legal list comes from -- this is the set of things it could have done, which is
            // exactly the denominator "how often was this taken when available" needs. Counting
            // after Apply would measure a different (post-action) state.
            var acting = state.ActivePlayer;

            // A new turn number resets which cards/moves have already been counted THIS turn --
            // must happen before CountOffers so the very first decision of a turn always counts.
            if (state.TurnNumber != perTurnTurnNumber)
            {
                perTurnTurnNumber = state.TurnNumber;
                cardsSeenThisTurnOne.Clear();
                cardsSeenThisTurnTwo.Clear();
                movesSeenThisTurnOne.Clear();
                movesSeenThisTurnTwo.Clear();
                cardsPlayedThisTurnOne.Clear();
                cardsPlayedThisTurnTwo.Clear();
                movesUsedThisTurnOne.Clear();
                movesUsedThisTurnTwo.Clear();
            }

            CountOffers(
                state, cards,
                acting == PlayerId.One ? cardOffersOne : cardOffersTwo,
                acting == PlayerId.One ? moveOffersOne : moveOffersTwo,
                acting == PlayerId.One ? mergeOffersOne : mergeOffersTwo,
                acting == PlayerId.One ? cardOffersByTurnOne : cardOffersByTurnTwo,
                acting == PlayerId.One ? moveOffersByTurnOne : moveOffersByTurnTwo,
                acting == PlayerId.One ? cardsSeenThisTurnOne : cardsSeenThisTurnTwo,
                acting == PlayerId.One ? movesSeenThisTurnOne : movesSeenThisTurnTwo);

            // Affordability is sampled at the same decision points as offers, so the two share a
            // denominator: "offered N times, blocked by cost M times" is only a coherent
            // statement if both were counted over the same set of decisions.
            trackers[acting].ObserveAffordability(state, cards);

            var choice = agent.Choose(AgentContext.ForActivePlayer(state, cards));

            actionCountsByKind[choice.Kind] = actionCountsByKind.GetValueOrDefault(choice.Kind) + 1;
            actionCount++;

            switch (choice)
            {
                case PlayCardAction playCard:
                    (playCard.Player == PlayerId.One ? cardsPlayedOne : cardsPlayedTwo)
                        .Add(playCard.CardId);
                    if (cards[playCard.CardId].IsCreature)
                    {
                        if (playCard.Player == PlayerId.One) creaturesPlayedOne++; else creaturesPlayedTwo++;
                    }

                    RecordOncePerTurn(
                        playCard.CardId,
                        playCard.Player == PlayerId.One ? cardsPlayedThisTurnOne : cardsPlayedThisTurnTwo,
                        playCard.Player == PlayerId.One ? cardPlaysByTurnOne : cardPlaysByTurnTwo);
                    break;
                case UseMoveAction useMove:
                    {
                        var creature = state.Board[useMove.SourceSlot]!;
                        var (ownerCardId, moveName) = ResolveMove(cards, creature.MergedFrom, useMove.MoveIndex);
                        (useMove.Player == PlayerId.One ? movesUsedOne : movesUsedTwo)
                            .Add((ownerCardId, moveName));

                        RecordOncePerTurn(
                            MoveKey.Of(ownerCardId, moveName),
                            useMove.Player == PlayerId.One ? movesUsedThisTurnOne : movesUsedThisTurnTwo,
                            useMove.Player == PlayerId.One ? moveUsesByTurnOne : moveUsesByTurnTwo);
                        break;
                    }
                case MergeAction merge:
                    if (merge.Player == PlayerId.One) mergeCountOne++; else mergeCountTwo++;
                    break;
            }

            // EndTurn() clears GameState.TurnEvents, so the count must reset to zero at exactly
            // the same moment -- otherwise the next harvest would either skip the first event of
            // the new turn (cursor left too high) or re-add events already counted (cursor too
            // low). Reading Kind here rather than tracking "did this Apply call EndTurn" keeps
            // that in one place instead of two ways of asking the same question.
            if (choice.Kind == ActionKind.EndTurn)
            {
                harvestedEventCount = 0;
                harvestedCreatureEvents = 0;
            }

            // A merge is the one board change that produces no CreatureDestroyed event -- the
            // vacated creature is folded in, not killed -- so the tracker is told before Apply,
            // while both slots still hold what the action refers to.
            if (choice is MergeAction mergeChoice)
            {
                trackers[mergeChoice.Player].OnMergedAway(mergeChoice.SourceSlot);
            }

            ActionExecutor.Apply(state, cards, choice);

            // Harvesting after every Apply, not just EndTurn, catches a draw from a card effect
            // mid-turn (e.g. Gravewarden's draw_scaled) as well as the turn-start draw -- both
            // append to the same TurnEvents list Apply may have just grown.
            HarvestDrawEvents(state, cardsDrawnOne, cardsDrawnTwo, ref harvestedEventCount);
            HarvestCreatureEvents(state, trackers, scoringStep, ref harvestedCreatureEvents);

            // Sample once per turn boundary, driven by TurnNumber changing rather than by
            // observing EndTurn: Apply's EndTurn path runs score->income->draw for the next
            // player before handing back control, so sampling here catches the state a turn
            // actually left behind. Guarding on the number (not the action kind) also means a
            // game that ends mid-turn contributes its final position exactly once.
            // A scoring step happens each time the turn passes: Apply's EndTurn path hands over
            // and immediately runs score->income->draw for the NEW active player.
            //
            // The seat observed is therefore the one RECEIVING the turn, not the one that ended
            // it, and the board is read after Apply -- exactly the board GameState.ApplyScoring
            // just read. Getting this wrong is not a rounding error: observing the ending seat
            // instead counts its slots before the opponent has had a turn to contest them, which
            // over-counts unopposed slot-turns by roughly 40% against the score they produced.
            // Pinned by test against PendingScore rather than by inspection, since both versions
            // produce plausible-looking numbers.
            if (choice.Kind == ActionKind.EndTurn)
            {
                scoringStep++;
                trackers[state.ActivePlayer].ObserveScoringStep(state.Board);
            }

            if (state.TurnNumber != lastSampledTurn)
            {
                lastSampledTurn = state.TurnNumber;
                scoreMarginByTurn.Add(state[PlayerId.One].Score - state[PlayerId.Two].Score);
                resourcesByTurnOne.Add(state[PlayerId.One].Resources);
                resourcesByTurnTwo.Add(state[PlayerId.Two].Resources);
                handSizeByTurnOne.Add(state[PlayerId.One].Hand.Count);
                handSizeByTurnTwo.Add(state[PlayerId.Two].Hand.Count);
            }
        }

        stopwatch.Stop();

        var winner = state.Winner;
        var ending = winner is null ? EndingType.NonTerminating : EndingType.ScoreThreshold;

        return new GameResult
        {
            AgentOne = agentOneKind,
            AgentTwo = agentTwoKind,
            Seed = seed,
            Winner = winner,
            Ending = ending,
            ScoreOne = state[PlayerId.One].Score,
            ScoreTwo = state[PlayerId.Two].Score,
            TurnCount = state.TurnNumber,
            ActionCount = actionCount,
            ActionCountsByKind = actionCountsByKind,
            CardsPlayedOne = cardsPlayedOne,
            CardsPlayedTwo = cardsPlayedTwo,
            CardsDrawnOne = cardsDrawnOne,
            CardsDrawnTwo = cardsDrawnTwo,
            MovesUsedOne = movesUsedOne,
            MovesUsedTwo = movesUsedTwo,
            CreaturesPlayedOne = creaturesPlayedOne,
            CreaturesPlayedTwo = creaturesPlayedTwo,
            MergeCountOne = mergeCountOne,
            MergeCountTwo = mergeCountTwo,
            FinalResourcesOne = state[PlayerId.One].Resources,
            FinalResourcesTwo = state[PlayerId.Two].Resources,
            CardOffersOne = cardOffersOne,
            CardOffersTwo = cardOffersTwo,
            MoveOffersOne = moveOffersOne,
            MoveOffersTwo = moveOffersTwo,
            CardOffersByTurnOne = cardOffersByTurnOne,
            CardOffersByTurnTwo = cardOffersByTurnTwo,
            CardPlaysByTurnOne = cardPlaysByTurnOne,
            CardPlaysByTurnTwo = cardPlaysByTurnTwo,
            MoveOffersByTurnOne = moveOffersByTurnOne,
            MoveOffersByTurnTwo = moveOffersByTurnTwo,
            MoveUsesByTurnOne = moveUsesByTurnOne,
            MoveUsesByTurnTwo = moveUsesByTurnTwo,
            MergeOffersOne = mergeOffersOne[0],
            MergeOffersTwo = mergeOffersTwo[0],
            ScoreMarginByTurn = scoreMarginByTurn,
            ResourcesByTurnOne = resourcesByTurnOne,
            ResourcesByTurnTwo = resourcesByTurnTwo,
            HandSizeByTurnOne = handSizeByTurnOne,
            HandSizeByTurnTwo = handSizeByTurnTwo,
            UnopposedSlotTurnsOne = trackers[PlayerId.One].UnopposedSlotTurns,
            UnopposedSlotTurnsTwo = trackers[PlayerId.Two].UnopposedSlotTurns,
            ScoringStepsOne = trackers[PlayerId.One].ScoringSteps,
            ScoringStepsTwo = trackers[PlayerId.Two].ScoringSteps,
            LongestUnopposedStreakOne = trackers[PlayerId.One].LongestUnopposedStreak,
            LongestUnopposedStreakTwo = trackers[PlayerId.Two].LongestUnopposedStreak,
            SlotsOccupiedByTurnOne = trackers[PlayerId.One].SlotsOccupiedByStep,
            SlotsOccupiedByTurnTwo = trackers[PlayerId.Two].SlotsOccupiedByStep,
            CombinedHealthByTurnOne = trackers[PlayerId.One].CombinedHealthByStep,
            CombinedHealthByTurnTwo = trackers[PlayerId.Two].CombinedHealthByStep,
            CreatureSurvivalOne = trackers[PlayerId.One].Survival,
            CreatureSurvivalTwo = trackers[PlayerId.Two].Survival,
            CardsBlockedByCostOne = trackers[PlayerId.One].BlockedByCost,
            CardsBlockedByCostTwo = trackers[PlayerId.Two].BlockedByCost,
            Elapsed = stopwatch.Elapsed,
        };
    }

    // Feeds CreaturePlayed/CreatureDestroyed events to the seat trackers, which turn them into
    // creature lifetimes. Shares HarvestDrawEvents' cursor discipline exactly -- GameState clears
    // TurnEvents on EndTurn, so the caller must reset this cursor in the same Apply call, or a
    // creature event is either skipped or double-counted.
    //
    // Events are attributed by their own Player field rather than by the acting player: a
    // creature destroyed during an opponent's turn belongs to its OWNER's survival record, which
    // is the seat whose card it was.
    private static void HarvestCreatureEvents(
        GameState state, Dictionary<PlayerId, SeatTracker> trackers, int scoringStep,
        ref int alreadyHarvested)
    {
        var events = state.TurnEvents;
        for (var i = alreadyHarvested; i < events.Count; i++)
        {
            var turnEvent = events[i];
            switch (turnEvent.Kind)
            {
                case TurnEventKind.CreaturePlayed:
                    trackers[turnEvent.Player]
                        .OnCreaturePlayed(turnEvent.Slot, turnEvent.CardId, scoringStep);
                    break;
                case TurnEventKind.CreatureDestroyed:
                    trackers[turnEvent.Player].OnCreatureDestroyed(turnEvent.Slot, scoringStep);
                    break;
            }
        }

        alreadyHarvested = events.Count;
    }

    // Records what the active player COULD have done at one decision point -- the opportunity
    // denominators for card play rate, move use rate, and merge take rate.
    //
    // Distinctness is the whole point: ActionGenerator expands one card into several actions (one
    // per empty slot, one per chosen target) and one move into several (one per target), but
    // those are one opportunity presented several ways, not several opportunities. Counting raw
    // actions would inflate the denominator for exactly the cards with the most targeting
    // flexibility, making them look systematically less-chosen than they are.
    //
    // Costs one extra ActionGenerator.Generate per decision. That is real but acceptable here:
    // Shapes.Sim is a batch measurement tool, not the search hot path that Phase 3 step 3
    // optimized -- and the agent's own Choose does vastly more work than this per decision.
    private static void CountOffers(
        GameState state, CardDatabase cards,
        Dictionary<string, int> cardOffers,
        Dictionary<string, int> moveOffers,
        int[] mergeOffers,
        Dictionary<string, int> cardOffersByTurn,
        Dictionary<string, int> moveOffersByTurn,
        HashSet<string> cardsSeenThisTurn,
        HashSet<string> movesSeenThisTurn)
    {
        var legal = ActionGenerator.Generate(state, cards);

        var cardsSeen = new HashSet<string>(StringComparer.Ordinal);
        var movesSeen = new HashSet<string>(StringComparer.Ordinal);
        var mergeSeen = false;

        foreach (var action in legal)
        {
            switch (action)
            {
                case PlayCardAction play:
                    cardsSeen.Add(play.CardId);
                    break;
                case UseMoveAction useMove:
                    {
                        var creature = state.Board[useMove.SourceSlot]!;
                        var (cardId, moveName) =
                            ResolveMove(cards, creature.MergedFrom, useMove.MoveIndex);
                        movesSeen.Add(MoveKey.Of(cardId, moveName));
                        break;
                    }
                case MergeAction:
                    mergeSeen = true;
                    break;
            }
        }

        foreach (var cardId in cardsSeen)
        {
            cardOffers[cardId] = cardOffers.GetValueOrDefault(cardId) + 1;
            RecordOncePerTurn(cardId, cardsSeenThisTurn, cardOffersByTurn);
        }

        foreach (var move in movesSeen)
        {
            moveOffers[move] = moveOffers.GetValueOrDefault(move) + 1;
            RecordOncePerTurn(move, movesSeenThisTurn, moveOffersByTurn);
        }

        if (mergeSeen)
        {
            mergeOffers[0]++;
        }
    }

    // Bumps `counts[id]` at most once per turn: `seenThisTurn` is cleared by the caller whenever
    // TurnNumber advances, so a second decision point (or a second play/use) offering/taking the
    // same id later in the same turn is a no-op here. This is what turns a per-decision count
    // into a per-turn count -- the only difference from CardOffers*/MovesUsed* is this dedup.
    private static void RecordOncePerTurn(string id, HashSet<string> seenThisTurn, Dictionary<string, int> counts)
    {
        if (seenThisTurn.Add(id))
        {
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
    }

    // Resolves a UseMoveAction's MoveIndex to the move definition AND the id of the card that
    // declared it. MoveIndex only makes sense relative to the creature's concatenated move list
    // (mergedFrom's cards, in order, each card's own moves in declaration order -- the same
    // layout CreatureInstance.MoveIndexOffset assumes), so for a merged creature the owning card
    // can be either half of the merge, not necessarily "the creature" as a whole.
    private static (string CardId, string MoveName) ResolveMove(
        CardDatabase cards, IReadOnlyList<string> mergedFrom, int moveIndex)
    {
        var offset = 0;
        foreach (var cardId in mergedFrom)
        {
            var moveCount = cards.MoveCountOf(cardId);
            if (moveIndex < offset + moveCount)
            {
                return (cardId, cards[cardId].Moves[moveIndex - offset].Name);
            }

            offset += moveCount;
        }

        throw new ArgumentOutOfRangeException(
            nameof(moveIndex), moveIndex, $"No move at index {moveIndex} among {string.Join(",", mergedFrom)}.");
    }

    // Reads TurnEventKind.CardDrawn events at index >= alreadyHarvested (everything new since the
    // last call) and appends each to its owning seat's running list. GameState.TurnEvents only
    // ever holds events from the CURRENT turn -- EndTurn() clears it -- so the caller must reset
    // alreadyHarvested to 0 in the same Apply call that ends the turn, or this either skips the
    // new turn's first event (cursor stale-high) or re-adds an already-counted one
    // (cursor stale-low).
    private static void HarvestDrawEvents(
        GameState state, List<string> drawnOne, List<string> drawnTwo, ref int alreadyHarvested)
    {
        var events = state.TurnEvents;
        for (var i = alreadyHarvested; i < events.Count; i++)
        {
            var turnEvent = events[i];
            if (turnEvent.Kind == TurnEventKind.CardDrawn)
            {
                (turnEvent.Player == PlayerId.One ? drawnOne : drawnTwo).Add(turnEvent.CardId);
            }
        }

        alreadyHarvested = events.Count;
    }
}
