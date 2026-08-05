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

        while (!state.IsOver && state.TurnNumber <= MaxTurns)
        {
            var agent = agents[state.ActivePlayer];
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
                    break;
                case UseMoveAction useMove:
                    {
                        var creature = state.Board[useMove.SourceSlot]!;
                        var (ownerCardId, moveName) = ResolveMove(cards, creature.MergedFrom, useMove.MoveIndex);
                        (useMove.Player == PlayerId.One ? movesUsedOne : movesUsedTwo)
                            .Add((ownerCardId, moveName));
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
            }

            ActionExecutor.Apply(state, cards, choice);

            // Harvesting after every Apply, not just EndTurn, catches a draw from a card effect
            // mid-turn (e.g. Gravewarden's draw_scaled) as well as the turn-start draw -- both
            // append to the same TurnEvents list Apply may have just grown.
            HarvestDrawEvents(state, cardsDrawnOne, cardsDrawnTwo, ref harvestedEventCount);
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
            Elapsed = stopwatch.Elapsed,
        };
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
