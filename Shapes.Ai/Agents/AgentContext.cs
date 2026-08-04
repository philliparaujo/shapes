using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Ai.Agents;

// Everything an agent is allowed to see when choosing an action.
//
// Bundled into one object rather than passed as loose parameters so that step 2.2 could tighten
// what an agent observes without touching IAgent or any call site. That is the point of the
// indirection: the leak-proofing work was a change to this type's State property, not a
// signature change rippling through every agent.
//
// State is an ObservedState, a projection that structurally cannot expose the opponent's hand
// contents or deck order -- see ObservedState's own doc comment for exactly what's hidden and
// why. An agent that wants to explore forward (a search rollout) builds its own concrete
// GameState via the step 2.3 determinizer; nothing here hands one back.
public sealed class AgentContext
{
    // The position to reason about, from the deciding player's side. See ObservedState's own
    // doc comment for exactly what it hides and why.
    public ObservedState State { get; }

    // Whose decision this is. Always State.ActivePlayer for a live game, carried explicitly so
    // an agent never has to assume that -- and so a test can hand an agent a position from the
    // other side without rebuilding it.
    public PlayerId Player { get; }

    // The candidate actions, exactly as ActionGenerator produced them. The chosen action must
    // come from here; see IAgent.Choose.
    public IReadOnlyList<GameAction> LegalActions { get; }

    // Static card data: costs, moves, health. Needed by any agent that evaluates rather than
    // guesses, and shared rather than copied since nothing mutates it.
    public CardDatabase Cards { get; }

    // Takes the live GameState only to build the observation and validate the action list
    // against it -- the constructor does not keep it, and State exposes the ObservedState
    // projection, never the source. A caller with only an ObservedState in hand (e.g. a test
    // written against one side of a hidden-information position) uses the other constructor
    // overload below.
    public AgentContext(
        GameState state, PlayerId player, IReadOnlyList<GameAction> legalActions, CardDatabase cards)
        : this(new ObservedState(state, player), player, legalActions, cards)
    {
    }

    public AgentContext(
        ObservedState state, PlayerId player, IReadOnlyList<GameAction> legalActions,
        CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legalActions);
        ArgumentNullException.ThrowIfNull(cards);

        // An empty list would leave Choose with nothing to return, and it should be
        // unreachable: EndTurn is always legal during the Actions phase. Failing loudly here
        // points at the generator or at a caller asking a finished game for a decision, rather
        // than surfacing as a confusing null or index error inside some agent.
        if (legalActions.Count == 0)
        {
            throw new ArgumentException(
                "An agent cannot be asked to choose from an empty action list. A live game always "
                + "offers at least EndTurn; an empty list means the game is over or the state is "
                + "not in the Actions phase.",
                nameof(legalActions));
        }

        State = state;
        Player = player;
        LegalActions = legalActions;
        Cards = cards;
    }

    // Builds the context for whoever is to act, generating the legal actions from the state.
    // The common path -- it keeps callers from pairing a state with a stale action list, which
    // would let an agent return something the executor then rejects.
    public static AgentContext ForActivePlayer(GameState state, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);

        return new AgentContext(
            state, state.ActivePlayer, ActionGenerator.Generate(state, cards), cards);
    }
}
