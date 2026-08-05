using System.Diagnostics;
using System.Threading;
using Shapes.Ai.Agents;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Ai.Search;

// Single-observer Information Set MCTS with per-iteration resampling. PLAN.md step 2.6.
//
// One iteration, in the four standard phases:
//
//   0. DETERMINIZE. Sample a concrete world consistent with the observation (step 2.3's
//      Determinizer). A FRESH world each iteration -- this is the "per-iteration resampling" the
//      plan specifies, and the one line that separates IS-MCTS from "MCTS on a guess." Sampling
//      once per search would make every statistic conditional on whatever hand the opponent
//      happened to be given, and the search would confidently play around a hand they do not
//      hold. Resampling makes the tree's statistics an average over the opponent's plausible
//      hands, which is the thing worth averaging.
//   1. SELECT. Walk down the tree by UCB1 with the availability correction (see SearchNode),
//      considering only the actions legal in THIS world, until reaching a node with an untried
//      legal action or a terminal position.
//   2. EXPAND. Add one child for a random untried action and step into it.
//   3. PLAY OUT. Play uniformly at random to a terminal position or a depth cap.
//   4. BACK PROPAGATE. Push the result back up, from the perspective of whoever chose each edge.
//
// == The tree is over the searcher's ACTIONS, not over states ==
//
// A node stands for an action sequence, not a position -- see SearchNode's header for why that
// distinction is forced by hidden information, and what it costs (availability counts). The
// practical consequence here: the tree spans BOTH players' turns. The opponent's replies are
// nodes too, selected by the same UCB1 from their own perspective, which is what makes this a
// search rather than a one-sided rollout. Their hidden cards are supplied by the determinization,
// so a "reply" the search explores is a reply they could actually make in that sampled world.
//
// == One node is one ATOMIC ACTION, never a whole turn ==
//
// PLAN.md's branching-factor note ("treat each atomic action as one tree node -- never enumerate
// whole turns as single moves"). A turn here is a sequence of plays, moves and merges ended by
// EndTurn; enumerating turns as single edges would raise the branching factor to the product of
// every action sequence, which is exactly the combinatorial explosion the plan rules out. So
// EndTurn is an ordinary edge and the tree simply gets deeper. It also means a node's depth is
// not a turn count -- the depth cap below is in actions, not turns.
//
// == What this deliberately does NOT do yet ==
//
// THE PLAYOUT POLICY IS PLUGGABLE, BUT UNIFORM RANDOM REMAINS THE DEFAULT. Step 3.2 added
// HeuristicPlayoutPolicy as a lightly-heuristic alternative to UniformPlayoutPolicy, selectable
// via the constructor -- but the search's own correctness tests still exercise the original
// uniform playout by default, since a heuristic playout can paper over a selection/backprop bug
// by making even a broken search play plausibly. Which policy is actually stronger is Phase 3's
// Shapes.Sim to measure, not something to assume from here.
//
// IT CLONES RATHER THAN APPLYING AND UNDOING. Phase 3 step 3's optimisation. Determinize already
// returns a fresh GameState per iteration, so selection and playout mutate that private copy and
// nothing else -- correct, and slower than it will be. The interface does not change when that
// lands, which is the point of building the naive version first.
//
// ITS CONSTANTS ARE UNTUNED. The exploration constant, the depth cap, and the default budget are
// literature defaults and round numbers, not measurements. Phase 3 step 5 tunes them against its
// batch runner; picking numbers here would mean picking them before the tool that measures them
// exists.
public sealed class IsMctsAgent : IAgent
{
    private readonly CardDatabase _cards;
    private readonly IRandomSource _random;
    private readonly Determinizer _determinizer;
    private readonly IPlayoutPolicy _playoutPolicy;
    private readonly HashSet<string> _sampledWorlds = new(StringComparer.Ordinal);

    // UCB1's exploration weight. sqrt(2) is the theoretical value for rewards in [0,1], which is
    // the range Reward() produces. Phase 3 step 5 tunes it.
    private const double DefaultExploration = 1.4142135623730951;

    // How many actions a playout may take before being scored as an unfinished game.
    //
    // A cap is not optional. Uniform-random play can end a turn, take income, and end again
    // indefinitely without either score reaching the win threshold -- the fuzz harness terminates
    // because it plays real games from a real start, not because random play is guaranteed to
    // finish from every position. Without a cap a single unlucky playout would hang the search.
    //
    // In ACTIONS, not turns, since a node is one atomic action (see above). PLAN.md step 3.3a:
    // TUNED, not a round number -- 200 is the measured p90 of uniform-random playout length from
    // realistic mid-search positions (4000-sample distribution: p50=90, p90=198, p95=244, p99=330,
    // max=541), chosen because profiling (step 3.3) found playout ActionGenerator.Generate and
    // ActionExecutor.Apply calls are 86.4% of one iteration's cost, and this is the single lever
    // that cuts directly into how many of those calls a playout makes. The other 9.6% of playouts
    // hit the cap and are scored by Reward()'s margin heuristic instead of a decided result --
    // soft precision loss, not a correctness bug, and a good trade against roughly halving
    // worst-case playout cost versus the previous, untuned 400.
    private const int DefaultPlayoutDepth = 200;

    public IsMctsAgent(
        CardDatabase cards, IRandomSource random, SearchBudget? budget = null,
        double explorationConstant = DefaultExploration, int playoutDepth = DefaultPlayoutDepth,
        IPlayoutPolicy? playoutPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegative(explorationConstant);
        ArgumentOutOfRangeException.ThrowIfLessThan(playoutDepth, 1);

        _cards = cards;
        _random = random;
        _determinizer = new Determinizer(cards);
        _playoutPolicy = playoutPolicy ?? UniformPlayoutPolicy.Instance;

        Budget = budget ?? SearchBudget.Default;
        ExplorationConstant = explorationConstant;
        PlayoutDepth = playoutDepth;
    }

    public SearchBudget Budget { get; }

    public double ExplorationConstant { get; }

    public int PlayoutDepth { get; }

    // Includes the budget, because IAgent.Name is what a balance table keys on and "ISMCTS(100)"
    // and "ISMCTS(10000)" are different players as far as that table is concerned. Collapsing
    // them to "ISMCTS" would silently pool two different agents' results. The playout policy is
    // folded in the same way, but only when it isn't the uniform default -- so step 3.2's
    // heuristic-vs-uniform comparison in Shapes.Sim gets two distinct rows ("ISMCTS(200)" and
    // "ISMCTS(200,heuristic)") instead of two agents silently sharing one label.
    public string Name => _playoutPolicy is UniformPlayoutPolicy
        ? $"ISMCTS({Budget})"
        : $"ISMCTS({Budget},heuristic)";

    // How many iterations the last Choose actually ran. Not part of IAgent -- it exists because a
    // time budget makes the count vary, and a caller measuring throughput or checking that a
    // budget was honoured has no other way to see it. Reset at the start of every Choose.
    public int LastIterationCount { get; private set; }

    // The tree built by the last Choose, or null if none was built (a forced move, or a search
    // cancelled before its first iteration).
    //
    // Per-decision scratch, exposed for INSPECTION rather than as state: a tree is the only place
    // a search's reasoning is visible, and PLAN.md's step 2.4 lesson -- that a green suite says
    // an agent's decisions were legal, not what it did with its turns -- applies twice over to a
    // search, whose failures are all "played badly" rather than "threw". A test asserting the tree
    // deepens with budget, or a debugging session asking why a move was picked, has no other
    // route in. Overwritten by the next Choose; nothing reads it during one.
    public SearchNode? LastTree { get; private set; }

    // How many DISTINCT possible worlds the last Choose sampled, by opponent hand.
    //
    // Instrumentation for the one property of this search that nothing else can observe from
    // outside: that resampling really happens per iteration. A search that determinized once and
    // reused the result would still run its full budget, still consume randomness in its
    // playouts, still return legal actions, and still be perfectly reproducible -- every external
    // signal looks identical, and the agent would simply be playing around one imagined opponent
    // hand forever. This counter is the difference, and IsMctsAgentTests asserts on it.
    //
    // Counted by hand contents rather than by determinizer calls, because the failure being
    // guarded against can call the determinizer once and clone its result thereafter. Distinct
    // WORLDS is the property; distinct calls is not.
    public int LastDistinctWorldCount { get; private set; }

    public GameAction Choose(AgentContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A forced move is not worth a search. Beyond the obvious saving, this is what keeps a
        // pending-discard debt cheap: while one stands the generator offers nothing else, and
        // searching a position whose every line begins with the same forced action learns
        // nothing about it.
        if (context.LegalActions.Count == 1)
        {
            LastIterationCount = 0;
            LastTree = null;
            LastDistinctWorldCount = 0;
            return context.LegalActions[0];
        }

        var root = new SearchNode();
        LastTree = root;
        _sampledWorlds.Clear();
        var clock = Stopwatch.StartNew();
        var iterations = 0;

        while (Budget.Allows(iterations, clock))
        {
            // Cooperative cancellation, checked between iterations rather than inside one: an
            // iteration is short and bounded, and abandoning one mid-playout would leave the tree
            // holding a selection path that was never backpropagated -- statistics claiming
            // availability without a result. Per IAgent's contract this RETURNS the best action so
            // far rather than throwing, since a cancelled search still owes the caller a legal
            // move for the game to continue.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            RunIteration(root, context);
            iterations++;
        }

        LastIterationCount = iterations;
        LastDistinctWorldCount = _sampledWorlds.Count;

        // No child at all means every iteration was cancelled before expanding. Falling back to a
        // legal action keeps the cancellation contract ("must leave the agent reusable, must
        // still return a legal move") rather than throwing on a race.
        return root.BestChild()?.IncomingAction ?? context.LegalActions[0];
    }

    // One iteration: determinize, select, expand, play out, back propagate.
    private void RunIteration(SearchNode root, AgentContext context)
    {
        // A fresh world, drawn from the agent's own stream. The determinizer builds the sampled
        // state its own RNG, so mutating this state cannot touch the real game's randomness --
        // and the state itself is a private copy, so neither can mutating the board.
        var state = _determinizer.Determinize(context.State, _random);

        // Records which world this iteration drew, for LastDistinctWorldCount. Keyed on the
        // opponent's hand because that IS the hidden information being sampled -- the rest of a
        // determinized state is copied from observations and identical across worlds.
        _sampledWorlds.Add(string.Join(",", state[context.Player.Opponent()].Hand));

        var node = root;
        var depth = 0;

        // -- Select ---------------------------------------------------------------------------
        //
        // Descend while the position is live and this node is fully expanded FOR THIS WORLD. That
        // qualifier is the IS-MCTS difference: a node fully expanded in one determinization can
        // have untried actions in the next, because a different sampled hand offers different
        // plays. So the untried set is recomputed against this world's legal actions every time
        // rather than cached on the node.
        while (!state.IsOver)
        {
            var legal = ActionGenerator.Generate(state, _cards);

            if (legal.Count == 0)
            {
                break;
            }

            var untried = node.UntriedActions(legal);

            if (untried.Count > 0)
            {
                // -- Expand -------------------------------------------------------------------
                //
                // One child per iteration, chosen uniformly from the untried actions. Uniformly
                // rather than in generator order: the generator emits by slot and hand position,
                // so taking the first untried action would expand low slot indices first at every
                // node -- a systematic bias in the same family as the one GreedyAgent's random
                // tie-break exists to avoid.
                var action = untried[_random.Next(untried.Count)];
                node = node.AddChild(action);

                // The expanded child was available this iteration, and nothing else counted it:
                // AvailabilityCount is incremented by SelectChild, which does not run on a node
                // reached by expansion. Missing this would leave a fresh child with availability
                // 0 and visits 1, making its exploration term log(0) -- negative infinity -- and
                // the search would never revisit anything it had just expanded.
                node.NoteAvailable();

                ActionExecutor.Apply(state, _cards, action);
                depth++;
                break;
            }

            node = node.SelectChild(legal, ExplorationConstant);
            ActionExecutor.Apply(state, _cards, node.IncomingAction!);
            depth++;
        }

        // -- Play out and back propagate ------------------------------------------------------
        var reward = PlayOut(state, depth);
        BackPropagate(node, reward);
    }

    // Plays uniformly at random from `state` until the game ends or the depth cap is reached,
    // then scores the result from PLAYER ONE's perspective (BackPropagate flips as needed).
    //
    // Mutates `state`, which is this iteration's private determinized copy and is discarded
    // immediately afterwards.
    private double PlayOut(GameState state, int depth)
    {
        while (!state.IsOver && depth < PlayoutDepth)
        {
            var legal = ActionGenerator.Generate(state, _cards);

            // Defensive rather than expected: the generator's non-emptiness invariant says a live
            // position always offers at least EndTurn (and, mid-debt, at least one discard). If
            // that were ever violated a playout would spin, so this stops instead.
            if (legal.Count == 0)
            {
                break;
            }

            ActionExecutor.Apply(state, _cards, _playoutPolicy.SelectAction(state, legal, _cards, _random));
            depth++;
        }

        return Reward(state);
    }

    // What a finished (or truncated) playout was worth, in [0,1] from PLAYER ONE's perspective.
    //
    // A decided game is 1 or 0. A playout that hit the depth cap is scored by SCORE MARGIN
    // squashed into (0,1) rather than being thrown away or called a draw:
    //
    //   - Discarding it would bias the statistics toward lines that happen to finish inside the
    //     cap, which are the fast aggressive ones -- the search would systematically undervalue
    //     control play for a reason that has nothing to do with the game.
    //   - Calling every truncated playout a flat 0.5 would make a position where the searcher is
    //     two points from winning indistinguishable from one where they are losing badly, which
    //     throws away the only signal a truncated playout carries.
    //
    // The margin is divided by ScoreToWin so the measure is "how much of the race is won,"
    // independent of the ruleset's win threshold -- Phase 4 sweeps that value, and a reward scale
    // that shifted with it would make searches at different rulesets incomparable. Clamped into
    // [0,1] so a truncated playout can never outrank a real win, which would let the search prefer
    // a long good-looking line over an actual victory.
    private static double Reward(GameState state)
    {
        if (state.Winner is { } winner)
        {
            return winner == PlayerId.One ? 1.0 : 0.0;
        }

        var margin = state[PlayerId.One].Score - state[PlayerId.Two].Score;
        var normalized = 0.5 + (margin / (2.0 * state.Rules.ScoreToWin));

        return Math.Clamp(normalized, 0.0, 1.0);
    }

    // Pushes the result up the selection path.
    //
    // The perspective flip is the subtle part, and getting it backwards produces a search that
    // plays deliberately badly -- which looks like weak play rather than like a bug, so it is
    // worth stating exactly. Each node's statistics belong to the player who CHOSE the incoming
    // action, because SelectChild compares siblings on behalf of whoever is to act at the parent.
    // A node whose action was player one's stores the player-one reward; a node whose action was
    // player two's stores its complement.
    //
    // GameAction.Player is the authority for whose action it was -- it is set by the generator
    // and carried on every action, so this never has to infer it from tree depth (which would be
    // wrong the moment a turn contains more than one action, i.e. always).
    // Note there is no `searcher` parameter: rewards are stored per ACTING player, read off the
    // action itself, so backprop does not need to know whose search this is. Choose then reads the
    // root's children, which are the searcher's own actions and therefore already in the
    // searcher's units.
    private static void BackPropagate(SearchNode? node, double reward)
    {
        while (node is not null)
        {
            if (node.IncomingAction is { } action)
            {
                node.Update(action.Player == PlayerId.One ? reward : 1.0 - reward);
            }
            else
            {
                // The root belongs to nobody; its visit count is still worth keeping as the
                // iteration count that reached it.
                node.Update(reward);
            }

            node = node.Parent;
        }
    }
}
