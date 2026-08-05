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
        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(cards.BuildSymmetricDeck(rules));
            player.ShuffleDeck(random);
            player.Draw(rules.StartingHandSize);
        }

        state.AdvanceToActions();

        var actionCount = 0;
        var actionCountsByKind = new Dictionary<ActionKind, int>();

        while (!state.IsOver)
        {
            var agent = agents[state.ActivePlayer];
            var choice = agent.Choose(AgentContext.ForActivePlayer(state, cards));

            actionCountsByKind[choice.Kind] = actionCountsByKind.GetValueOrDefault(choice.Kind) + 1;
            actionCount++;

            ActionExecutor.Apply(state, cards, choice);
        }

        stopwatch.Stop();

        var winner = state.Winner!.Value;
        return new GameResult
        {
            AgentOne = agentOneKind,
            AgentTwo = agentTwoKind,
            Seed = seed,
            Winner = winner,
            WinnerScore = state[winner].Score,
            LoserScore = state[winner.Opponent()].Score,
            TurnCount = state.TurnNumber,
            ActionCount = actionCount,
            ActionCountsByKind = actionCountsByKind,
            Elapsed = stopwatch.Elapsed,
        };
    }
}
