using Shapes.Core.Actions;

namespace Shapes.Ai.Search;

// One node of the IS-MCTS tree: a position reached by a particular sequence of actions, holding
// statistics pooled across every determinized world that reached it.
//
// == Why a node is an INFORMATION SET, not a state ==
//
// In plain MCTS a node is a game state. Here it cannot be: the searching player does not know the
// state -- they know the actions taken so far and their own cards, and the hidden remainder is
// resampled every iteration (PLAN.md's "per-iteration resampling"). So a node stands for the whole
// SET of states consistent with that action sequence, and its statistics are an average over the
// worlds that were actually sampled. That is the whole idea of Information Set MCTS: the tree is
// built in the space the player can distinguish, and the sampling supplies the rest.
//
// The concrete consequence, and the thing that makes this class more than a dictionary: DIFFERENT
// WORLDS OFFER DIFFERENT ACTIONS. A child that exists in the tree may be illegal in the world
// this iteration sampled -- an opponent's card that is not in their hand this time, a move whose
// condition fails on a different board. So selection can only consider the children legal RIGHT
// NOW, and the visit counts of the others must not be allowed to make a rarely-offered action
// look bad.
//
// == Availability counts, and why plain UCB1 is wrong here ==
//
// UCB1's exploration term is sqrt(ln(N) / n), where N is the parent's visits. That is correct
// when every child was available on every one of those N visits. It is not correct here: an
// action offered in only a tenth of the sampled worlds gets a tenth of the chances to be tried,
// and comparing its n against the full N makes it look under-explored forever -- the search would
// pour iterations into rare actions regardless of their value.
//
// The IS-MCTS fix (Cowling, Powley & Whitehouse 2012) is to count AVAILABILITY per child: how
// many times this child was among the legal options, not how many times the parent was visited.
// The exploration term becomes sqrt(ln(availability) / visits), so each action is measured against
// the number of opportunities it actually had. AvailabilityCount is incremented for every legal
// child at each selection step, including the ones not chosen -- that is the bookkeeping the
// whole correction rests on, and getting it wrong is silent: the search still runs, still returns
// legal moves, and simply explores the wrong things.
//
// == Whose reward ==
//
// Statistics are stored from the perspective of the player TO ACT AT THE PARENT, so the value of
// a child is always "how good is this for whoever chose it." IsMctsAgent handles the perspective
// flip at backprop; this class only stores what it is given.
public sealed class SearchNode
{
    // Children by the action leading to them. GameAction has value equality (PLAN.md step 1.8
    // made that a requirement precisely for this), so two identical actions generated in two
    // different determinized worlds map to ONE child and pool their statistics. Reference
    // equality would split a node's statistics across duplicate children and the search would
    // learn nothing from either.
    private readonly Dictionary<GameAction, SearchNode> _children = new();

    // The action that led here from the parent. Null at the root, which was not reached by an
    // action.
    public GameAction? IncomingAction { get; }

    public SearchNode? Parent { get; }

    // How many iterations passed through this node.
    public int Visits { get; private set; }

    // How many times this node's action was among its parent's legal actions -- see the class
    // comment. Always >= Visits, since being chosen requires being available.
    public int AvailabilityCount { get; private set; }

    // Summed reward, in the units IsMctsAgent backpropagates (1 win / 0.5 draw / 0 loss), from
    // the perspective of the player who chose IncomingAction.
    public double TotalReward { get; private set; }

    public SearchNode()
    {
    }

    private SearchNode(GameAction incomingAction, SearchNode parent)
    {
        IncomingAction = incomingAction;
        Parent = parent;
    }

    public IReadOnlyDictionary<GameAction, SearchNode> Children => _children;

    // Mean reward, or 0 for an unvisited node. Callers must not use this to compare an unvisited
    // node against a visited one -- SelectChild expands the unvisited ones first, precisely so
    // that comparison never arises.
    public double AverageReward => Visits == 0 ? 0.0 : TotalReward / Visits;

    // The actions legal here that have no child yet. These are what expansion picks from, and an
    // empty result is what makes a node "fully expanded" for THIS world -- a different world may
    // offer a different set, so fully-expanded is a property of a node and a legal-action list
    // together, never of a node alone. That is another thing that is true in MCTS and false in
    // IS-MCTS.
    public List<GameAction> UntriedActions(IReadOnlyList<GameAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(legalActions);

        var untried = new List<GameAction>();

        foreach (var action in legalActions)
        {
            if (!_children.ContainsKey(action))
            {
                untried.Add(action);
            }
        }

        return untried;
    }

    // Adds a child for `action`, or returns the existing one. Idempotent because two worlds can
    // reach the same action and both must land on the same node for the statistics to pool.
    public SearchNode AddChild(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_children.TryGetValue(action, out var existing))
        {
            return existing;
        }

        var child = new SearchNode(action, this);
        _children.Add(action, child);
        return child;
    }

    // Picks the child maximising UCB1 with the availability correction, from among the actions
    // legal in the current determinization.
    //
    // Every legal child has its availability incremented here whether or not it is chosen: that
    // is what "this action had another opportunity" means, and it is the count the exploration
    // term divides by. Callers must therefore call this exactly once per selection step.
    //
    // Assumes every legal action already has a child -- the caller expands untried actions first,
    // so a node reaching here is fully expanded for this world.
    public SearchNode SelectChild(IReadOnlyList<GameAction> legalActions, double explorationConstant)
    {
        ArgumentNullException.ThrowIfNull(legalActions);

        if (legalActions.Count == 0)
        {
            throw new ArgumentException(
                "Cannot select a child with no legal actions. A live position always offers at "
                + "least EndTurn; an empty list means the caller failed to check IsOver.",
                nameof(legalActions));
        }

        SearchNode? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var action in legalActions)
        {
            if (!_children.TryGetValue(action, out var child))
            {
                throw new InvalidOperationException(
                    $"SelectChild was given the legal action '{action.Describe()}' that has no "
                    + "child node. Untried actions must be expanded before selection; reaching "
                    + "here means the caller skipped that step.");
            }

            child.AvailabilityCount++;

            var score = Ucb1(child, explorationConstant);

            if (score > bestScore)
            {
                bestScore = score;
                best = child;
            }
        }

        // Unreachable: the loop above runs at least once and every finite score beats
        // NegativeInfinity. Kept as a hard failure rather than a null-forgiving operator so a
        // future NaN weight surfaces here rather than as a NullReferenceException three frames up.
        return best ?? throw new InvalidOperationException(
            "No child was selected despite legal actions being available -- a UCB1 score was NaN.");
    }

    // Exploitation plus the availability-corrected exploration bonus.
    //
    // A visited-zero child scores infinity so it is taken immediately, which cannot normally
    // happen (the caller expands untried actions before selecting) but keeps the function total
    // rather than dividing by zero.
    private static double Ucb1(SearchNode child, double explorationConstant)
    {
        if (child.Visits == 0)
        {
            return double.PositiveInfinity;
        }

        var exploration = explorationConstant
            * Math.Sqrt(Math.Log(child.AvailabilityCount) / child.Visits);

        return child.AverageReward + exploration;
    }

    // Records that this node's action was among the legal options, without selecting it.
    //
    // SelectChild does this for every legal sibling as part of choosing; this is the other route
    // in -- a child reached by EXPANSION, which never goes through SelectChild. Without it a
    // freshly expanded node would sit at availability 0 with visits 1, and its exploration term
    // (sqrt(ln(0)/1)) would be negative infinity: the search would never revisit anything it had
    // just expanded, and the tree would grow one useless leaf per iteration instead of deepening.
    public void NoteAvailable() => AvailabilityCount++;

    // Records one iteration's result at this node.
    public void Update(double reward)
    {
        Visits++;
        TotalReward += reward;
    }

    // The child to actually play, once the budget is spent: MOST VISITED, not highest average.
    //
    // The standard MCTS choice, and it matters. A high average over three visits is noise; the
    // visit count is what the search's own selection policy converged on, and it cannot be
    // inflated by a lucky rollout the way an average can. Ties break on average reward, then --
    // for reproducibility -- on the action's description, so the same seed at the same position
    // always names the same action rather than depending on dictionary enumeration order.
    public SearchNode? BestChild()
    {
        SearchNode? best = null;

        foreach (var child in _children.Values)
        {
            if (best is null || Prefers(child, best))
            {
                best = child;
            }
        }

        return best;
    }

    private static bool Prefers(SearchNode candidate, SearchNode incumbent)
    {
        if (candidate.Visits != incumbent.Visits)
        {
            return candidate.Visits > incumbent.Visits;
        }

        if (candidate.AverageReward != incumbent.AverageReward)
        {
            return candidate.AverageReward > incumbent.AverageReward;
        }

        // Total order over ties. Dictionary enumeration order is not contractually stable, and an
        // unstable tie-break would make the same seed produce different decisions across runs --
        // quietly destroying the reproducibility Phase 4's balance numbers depend on. Same
        // reasoning as Determinizer.UnseenCardsOf's sort.
        return string.CompareOrdinal(
            candidate.IncomingAction?.Describe(), incumbent.IncomingAction?.Describe()) < 0;
    }

    // Longest path from here to a leaf, in edges. A leaf is 0.
    //
    // For inspection and for the test that a bigger budget DEEPENS the tree rather than widening
    // it -- the observable difference between a search and a fan of independent rollouts from the
    // root, and a distinction no legality check can make.
    public int Depth()
    {
        var deepest = 0;

        foreach (var child in _children.Values)
        {
            deepest = Math.Max(deepest, child.Depth() + 1);
        }

        return deepest;
    }

    public override string ToString() =>
        $"{IncomingAction?.Describe() ?? "(root)"} visits={Visits}/{AvailabilityCount} "
        + $"avg={AverageReward:0.000} children={_children.Count}";
}
