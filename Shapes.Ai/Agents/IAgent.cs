using System.Threading;
using Shapes.Core.Actions;

namespace Shapes.Ai.Agents;

// A decision-maker: given what it can see, pick one action.
//
// This is the seam the whole of Phase 2 hangs off. The console client, the Phase 3 sim runner,
// and the Phase 4 Godot client all drive play through this one method, so a RandomAgent, a
// GreedyAgent, and a full IS-MCTS search are interchangeable at every call site. Nothing
// downstream may type-test for a particular agent -- if a caller needs to know which agent it
// holds, the abstraction has already failed.
//
// Three properties of the signature, each load-bearing:
//
//   1. IT RETURNS AN ACTION, NOT A STATE. An agent never mutates the game. It picks an edge
//      out of a list the engine generated and hands it back; the caller applies it via
//      ActionExecutor. A search that wants to explore does so on its own clones, never on the
//      state it was handed.
//   2. IT CHOOSES FROM A GIVEN LIST. The legal actions arrive on the context rather than
//      being re-derived here. ActionGenerator is the single definition of legality (see its
//      header); an agent computing its own candidate list is a second definition, and the two
//      drift. The returned action MUST be one of context.LegalActions.
//   3. IT TAKES A CancellationToken. An IS-MCTS search is budget-bounded and may run for
//      seconds. Phase 4 runs it off the main thread and needs to abandon a search when the
//      player quits or the turn is conceded; a cooperative token is how that happens without
//      killing a thread mid-mutation.
//
// Implementations must be deterministic given the same context and the same RNG stream --
// every random choice goes through IRandomSource, never Random.Shared. A balance run that
// cannot be replayed from its seed is not evidence of anything.
public interface IAgent
{
    // A short name for logs, sim output, and the console's opponent picker. Distinct per agent
    // configuration rather than per type: "ISMCTS(10000)" and "ISMCTS(500)" are different
    // players as far as a balance table is concerned, and collapsing them loses the comparison.
    string Name { get; }

    // Pick one action from context.LegalActions.
    //
    // Must not return null and must not return an action outside the list -- the caller applies
    // the result without re-validating it, matching the generator/executor contract. The list is
    // never empty while the game is live (EndTurn is always legal during the Actions phase), so
    // there is no "no legal move" case to handle.
    //
    // Cancellation is best-effort and must leave the agent reusable: an agent that observes the
    // token should return its best action so far rather than throwing, since a cancelled search
    // still has to hand back a legal move for the game to continue.
    GameAction Choose(AgentContext context, CancellationToken cancellationToken = default);
}
