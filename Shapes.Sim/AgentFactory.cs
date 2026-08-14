using Shapes.Ai.Agents;
using Shapes.Ai.Search;
using Shapes.Core.Cards;
using Shapes.Core.State;

namespace Shapes.Sim;

// Mirrors Shapes.Console's BuildAgent switch (Program.cs). Kept here rather than shared via a
// project reference so Shapes.Sim has no dependency on the console client -- a headless batch
// runner should not need a text UI on its dependency graph.
public static class AgentFactory
{
    // `opponentDeck` is the decklist the agent's OPPONENT is playing, passed to IS-MCTS so its
    // determinizer can reconstruct hidden cards when the two seats are not playing the same deck.
    // Null keeps the symmetric-decklist path, which is what the default-deck mode uses.
    public static IAgent Build(
        string kind, ulong seed, CardDatabase cards, int iterations, Deck? opponentDeck = null) =>
        kind.ToLowerInvariant() switch
        {
            "random" => new RandomAgent(new SeededRandom(seed)),
            "greedy" => new GreedyAgent(new SeededRandom(seed)),
            "ismcts" => new IsMctsAgent(
                cards, new SeededRandom(seed), SearchBudget.OfIterations(iterations),
                opponentDeck: opponentDeck),
            // Step 3.2's playout-policy control: identical search, only the rollout differs, so
            // a Shapes.Sim matrix can measure the heuristic playout against its uniform baseline
            // directly rather than assuming it helps.
            "ismcts-heuristic" => new IsMctsAgent(
                cards, new SeededRandom(seed), SearchBudget.OfIterations(iterations),
                playoutPolicy: HeuristicPlayoutPolicy.Instance, opponentDeck: opponentDeck),
            _ => throw new ArgumentException(
                $"Unknown agent '{kind}'. Expected random, greedy, ismcts, or ismcts-heuristic."),
        };
}
