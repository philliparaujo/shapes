using System.Threading;
using Shapes.Core.Actions;
using Shapes.Core.State;

namespace Shapes.Ai.Agents;

// Picks uniformly at random from the legal actions.
//
// The floor of the yardstick the plan's step 2.4 sets up: an IS-MCTS that cannot beat this
// >95% of the time has a bug, not a tuning problem. It is also the simplest possible proof
// that IAgent is implementable and that a game can be driven end-to-end through the interface.
//
// Randomness comes from an injected IRandomSource, not Random.Shared, for the same reason
// everything else in the engine does: a sim result that cannot be replayed from its seed
// cannot be investigated. Two RandomAgents built on sources with the same seed play the same
// game.
//
// Note this agent chooses EndTurn as readily as anything else, so its turns are short and its
// play is bad in a specific way -- it under-uses the board rather than misusing it. Step 2.4's
// GreedyAgent is the harder baseline; this one exists to be beaten decisively.
public sealed class RandomAgent : IAgent
{
    private readonly IRandomSource _random;

    public RandomAgent(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public string Name => "Random";

    public GameAction Choose(AgentContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // No cancellation check: the choice is O(1) and always completes. An agent that could
        // run long -- the search in step 2.5 -- observes the token instead of ignoring it.
        return context.LegalActions[_random.Next(context.LegalActions.Count)];
    }
}
