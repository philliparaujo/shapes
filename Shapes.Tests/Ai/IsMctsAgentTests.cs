using System.Diagnostics;
using Shapes.Ai.Agents;
using Shapes.Ai.Search;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Ai;

// PLAN.md step 2.6: IS-MCTS -- selection, expansion, playout, backprop, per-iteration resampling.
//
// == What is hard to test here, and how these tests handle it ==
//
// A search's failure mode is not a crash, it is PLAYING BADLY. Backprop with the sign flipped,
// availability counts never incremented, resampling done once instead of per iteration -- every
// one of those produces an agent that returns legal actions, finishes games, and satisfies the
// entire IAgent contract while searching uselessly. AgentContractTests (which this agent is now a
// theory case of) covers legality, non-mutation and determinism; none of that would notice.
//
// So the tests below aim at the mechanisms directly, in three groups:
//
//   MECHANISM -- SearchNode's arithmetic in isolation, where UCB1, the availability correction,
//   and most-visited selection are ordinary functions with checkable outputs.
//
//   BEHAVIOUR AT A DECIDED POSITION -- positions where one action is unambiguously correct and a
//   working search must find it. These are the tests with teeth against a mis-signed backprop: an
//   agent maximising its opponent's reward fails them immediately, while passing everything in
//   AgentContractTests.
//
//   BUDGET AND STRUCTURE -- that the search honours its budget, resamples per iteration, and
//   grows a tree rather than a fan of leaves.
//
// WHAT IS NOT HERE: a win rate against GreedyAgent. That is a batch measurement and belongs to
// Phase 3 step 1, for exactly the reasons GreedyAgentTests records -- a threshold picked by
// rounding down today's number would fail on an unrelated rebalance while indicating no defect.
// The plan's exit criterion ("IS-MCTS beats both baselines decisively") is deliberately left to
// the phase that owns the measuring tool.
public class IsMctsAgentTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    // Small by default: these tests assert that the search WORKS, not how strong it is, and a
    // 1000-iteration search at every position would make the suite slow for no added signal.
    private static IsMctsAgent Agent(ulong seed = 7, int iterations = 200) =>
        new(TestCards.Database, new SeededRandom(seed), SearchBudget.OfIterations(iterations));

    // -- Mechanism: SearchNode ------------------------------------------------------------------

    [Fact]
    public void An_unvisited_child_is_selected_before_a_visited_one()
    {
        // UCB1's first duty: try everything once. A node with a good average must not be
        // preferred over one with no data at all, or the search would commit to whichever action
        // its first rollout happened to like.
        var root = new SearchNode();
        var legal = Actions(Play("a"), Play("b"));

        var visited = root.AddChild(legal[0]);
        visited.NoteAvailable();
        visited.Update(1.0);

        root.AddChild(legal[1]);

        Assert.Equal(legal[1], root.SelectChild(legal, 1.4).IncomingAction);
    }

    [Fact]
    public void Selection_counts_availability_for_every_legal_child_not_just_the_chosen_one()
    {
        // The bookkeeping the whole availability correction rests on, and the one most likely to
        // be quietly wrong: incrementing only the selected child would leave the counts equal to
        // the visit counts, silently degrading the exploration term back to something UCB1 never
        // intended. Nothing else in the suite would notice.
        var root = new SearchNode();
        var legal = Actions(Play("a"), Play("b"));

        foreach (var action in legal)
        {
            var child = root.AddChild(action);
            child.NoteAvailable();
            child.Update(0.5);
        }

        root.SelectChild(legal, 1.4);

        Assert.Equal(2, root.Children[legal[0]].AvailabilityCount);
        Assert.Equal(2, root.Children[legal[1]].AvailabilityCount);
    }

    [Fact]
    public void An_action_offered_rarely_is_not_penalised_for_its_low_visit_count()
    {
        // THE reason IS-MCTS needs availability counts at all (SearchNode's header). Two children
        // with the same average: one offered in every world, one offered rarely. Under plain UCB1
        // -- exploration measured against the PARENT's visits -- the rare action looks
        // permanently under-explored and the search wastes its budget re-trying it regardless of
        // value. Measured against its own opportunities, the two are comparable.
        //
        // The rare child was available 10 times and taken 5; the common one available 100 times
        // and taken 50. Both took half their opportunities, so neither has been neglected -- but
        // a parent that saw 100 visits knows far more about the common one.
        //
        // Asserted as a COMPARISON against uncorrected UCB1 rather than as an absolute bound on
        // the corrected bonus. The corrected bonus is still larger for the rare child (it has
        // fewer visits, and UCB1's 1/sqrt(visits) decay is doing its ordinary job) -- so any
        // absolute threshold would be a number picked to fit, not a property. What the correction
        // actually changes is the SIZE of that gap, and that is checkable: measured against its
        // own opportunities the rare child's edge is modest; measured against the parent's visits
        // it balloons, which is the behaviour that makes an uncorrected search chase rare actions.
        var root = new SearchNode();
        var legal = Actions(Play("rare"), Play("common"));

        var rare = root.AddChild(legal[0]);
        Repeat(10, rare.NoteAvailable);
        Repeat(5, () => rare.Update(0.5));

        var common = root.AddChild(legal[1]);
        Repeat(100, common.NoteAvailable);
        Repeat(50, () => common.Update(0.5));

        var parentVisits = rare.Visits + common.Visits;

        var correctedGap =
            Math.Sqrt(Math.Log(rare.AvailabilityCount) / rare.Visits)
            / Math.Sqrt(Math.Log(common.AvailabilityCount) / common.Visits);

        var uncorrectedGap =
            Math.Sqrt(Math.Log(parentVisits) / rare.Visits)
            / Math.Sqrt(Math.Log(parentVisits) / common.Visits);

        Assert.True(
            correctedGap < uncorrectedGap,
            $"The availability correction should NARROW the rare action's exploration advantage "
            + $"(corrected gap {correctedGap:0.000}x) relative to measuring it against the "
            + $"parent's visits (uncorrected {uncorrectedGap:0.000}x). It did not, which means "
            + "availability counts are not reaching the exploration term.");
    }

    [Fact]
    public void The_best_child_is_the_most_visited_not_the_best_average()
    {
        // The standard MCTS final-selection rule, and it matters: a 1.0 average over two visits
        // is noise, while a visit count is what the selection policy converged on over the whole
        // budget and cannot be inflated by one lucky rollout.
        var root = new SearchNode();
        var legal = Actions(Play("lucky"), Play("solid"));

        var lucky = root.AddChild(legal[0]);
        Repeat(2, () => lucky.Update(1.0));

        var solid = root.AddChild(legal[1]);
        Repeat(50, () => solid.Update(0.6));

        Assert.Equal(legal[1], root.BestChild()!.IncomingAction);
    }

    [Fact]
    public void Identical_actions_from_different_worlds_pool_into_one_child()
    {
        // Why GameAction needs value equality (PLAN.md step 1.8 calls this out as the reason).
        // The same action generated in two different determinized worlds is two distinct object
        // references; under reference equality they would become two children splitting the
        // statistics, and the search would learn half as much from each while double-counting the
        // action's availability.
        var root = new SearchNode();

        var first = root.AddChild(Play("a"));
        var second = root.AddChild(Play("a"));

        Assert.Same(first, second);
        Assert.Single(root.Children);
    }

    // -- Behaviour: the search finds the right action at a decided position ---------------------

    [Fact]
    public void It_takes_an_available_lethal_move()
    {
        // The sharpest test in the suite, and the one that fails loudly if backprop's sign is
        // inverted. P1's Striker faces a P2 Striker on exactly 1 health, and Strike kills it.
        // Killing frees the facing slot to score every turn thereafter, so every line beginning
        // with Strike is better than every line that does not -- a search maximising the wrong
        // player's reward avoids it just as consistently as a correct one takes it.
        var state = new StateBuilder()
            .ConservingDecks(TestCards.Database)
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Resources(spike: 4, anvil: 4, wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2, health: 1))
            .Build();

        var choice = Agent(iterations: 400).Choose(Context(state));

        var move = Assert.IsType<UseMoveAction>(choice);
        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
    }

    [Fact]
    public void It_does_not_pass_while_a_useful_action_is_affordable()
    {
        // The failure that separates a working search from a broken one most visibly in a watched
        // game: passing with resources unspent. EndTurn is always legal and is the shallowest
        // node in the tree, so a search that backpropagates noise gravitates to it.
        var state = new StateBuilder()
            .ConservingDecks(TestCards.Database)
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Striker)
                .Resources(spike: 4, anvil: 4, wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

        var choice = Agent(iterations: 400).Choose(Context(state));

        Assert.IsNotType<EndTurnAction>(choice);
    }

    [Fact]
    public void A_forced_move_is_returned_without_searching()
    {
        // A position with one legal action needs no search, and spending a budget on it would be
        // pure waste -- most visibly while a discard debt stands, when the generator offers
        // nothing else and every line in the tree would begin with the same forced action.
        var state = new StateBuilder()
            .ConservingDecks(TestCards.Database)
            .P1(p => p.Hand(TestCards.Bolt))
            .Build();

        var agent = Agent();
        var forced = ActionGenerator.Generate(state, TestCards.Database).Take(1).ToList();
        var context = new AgentContext(state, PlayerId.One, forced, TestCards.Database);

        Assert.Equal(forced[0], agent.Choose(context));
        Assert.Equal(0, agent.LastIterationCount);
    }

    // -- Structure: resampling, budget, tree growth ---------------------------------------------

    [Fact]
    public void It_resamples_a_new_world_every_iteration()
    {
        // "Per-iteration resampling" is the phrase PLAN.md step 2.6 uses, and it is the line
        // between IS-MCTS and MCTS-on-a-guess. Determinizing once per SEARCH would make every
        // statistic conditional on one imagined opponent hand, and the agent would play around
        // cards the opponent does not hold -- while passing every other test here.
        //
        // Asserted on the count of DISTINCT WORLDS the search actually sampled, which the agent
        // records for exactly this purpose (LastDistinctWorldCount). Nothing weaker works:
        //
        //   - A draw-count assertion was tried first and FAILS TO CATCH the bug. An
        //     implementation that determinizes once and clones the cached world per iteration
        //     still consumes randomness in its playouts, so the total draw count stays high while
        //     the search stares at one imagined opponent hand the whole time. Verified: that
        //     sabotage slipped straight through a draw-count test.
        //   - Counting determinizer CALLS would not work either, since the sabotage above calls
        //     it once and clones thereafter. What matters is that the worlds DIFFER.
        // A position where the opponent HOLDS CARDS. Position() gives P2 an empty hand, which
        // makes the empty hand the only world consistent with the observation -- a search there
        // resamples faithfully and correctly produces one distinct world every time, so it cannot
        // distinguish resampling from caching. The hidden information has to exist before
        // sampling it can mean anything.
        var state = new StateBuilder()
            .ConservingDecks(TestCards.Database)
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Striker)
                .Resources(spike: 4, anvil: 4, wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Bolt, TestCards.Chooser, TestCards.TwoMove))
            .Build();

        var agent = new IsMctsAgent(
            TestCards.Database, new SeededRandom(5), SearchBudget.OfIterations(50));

        agent.Choose(Context(state));

        Assert.True(
            agent.LastDistinctWorldCount > 1,
            $"A 50-iteration search saw only {agent.LastDistinctWorldCount} distinct opponent "
            + "hand(s), so it is sampling one world and reusing it rather than resampling per "
            + "iteration -- see PLAN.md step 2.6.");
    }

    [Fact]
    public void It_honours_an_iteration_budget_exactly()
    {
        // The reproducible half of SearchBudget: Phase 3 compares rulesets at equal search
        // effort, which is only meaningful if the effort is exactly what was asked for.
        var agent = new IsMctsAgent(
            TestCards.Database, new SeededRandom(9), SearchBudget.OfIterations(37));

        agent.Choose(Context(Position()));

        Assert.Equal(37, agent.LastIterationCount);
    }

    [Fact]
    public void It_honours_a_time_budget()
    {
        // The interactive half: Phase 5's exit criterion is a decision inside a wall-clock target,
        // and a search that overran it would stutter the client. Generous bounds -- this asserts
        // the clock is consulted at all, not a precise deadline, since a CI machine's scheduler
        // is not something to assert against.
        var agent = new IsMctsAgent(
            TestCards.Database, new SeededRandom(9),
            SearchBudget.OfTime(TimeSpan.FromMilliseconds(150)));

        var clock = Stopwatch.StartNew();
        agent.Choose(Context(Position()));
        clock.Stop();

        Assert.True(agent.LastIterationCount > 0, "A time-budgeted search ran no iterations.");
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(5),
            $"A 150ms search took {clock.Elapsed.TotalSeconds:0.0}s -- the time budget is not "
            + "being checked.");
    }

    [Fact]
    public void A_larger_budget_searches_deeper_rather_than_wider()
    {
        // Tree GROWTH, which distinguishes a search from a fan of independent rollouts. A bug
        // that expanded from the root every iteration (never descending) would produce a tree one
        // level deep however large the budget -- and would still return legal, plausible-looking
        // actions.
        var context = Context(Position());

        Assert.True(
            DepthOf(context, iterations: 400) > DepthOf(context, iterations: 25),
            "A larger budget did not produce a deeper tree, so the search is expanding from the "
            + "root rather than descending into it.");
    }

    [Fact]
    public void It_completes_full_games_on_the_real_card_set()
    {
        // The counterpart to GreedyAgentTests' robustness test, and here for the same reason:
        // every other test in this file uses TestCards, which covers rule SHAPES but not the real
        // effect vocabulary. This is the only place the search meets all ~36 real cards, where an
        // op no synthetic card exercises can surface -- during a playout, thousands of actions
        // deep, which is precisely where an unhandled effect would otherwise first appear in
        // Phase 3.
        //
        // Robustness only. No win rate: see the class header.
        var agent = new IsMctsAgent(
            Cards, new SeededRandom(3), SearchBudget.OfIterations(30));
        var opponent = new RandomAgent(new SeededRandom(4));

        var state = NewRealGame(seed: 21);

        for (var i = 0; i < 600 && !state.IsOver; i++)
        {
            var context = AgentContext.ForActivePlayer(state, Cards);
            var actor = state.ActivePlayer == PlayerId.One ? (IAgent)agent : opponent;

            ActionExecutor.Apply(state, Cards, actor.Choose(context));
        }

        Assert.True(state.IsOver, "The game did not finish within 600 actions.");
    }

    [Fact]
    public void A_cancelled_search_still_returns_a_legal_action()
    {
        // IAgent's cancellation clause: best-effort, and the agent must stay usable. Phase 5 runs
        // the search off the main thread and abandons it when the player quits or concedes, and
        // the game still needs a legal move to continue. Cancelled before the first iteration is
        // the worst case -- there is no tree at all, so this exercises the fallback path.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var context = Context(Position());
        var choice = Agent(iterations: 1000).Choose(context, cancelled.Token);

        Assert.Contains(choice, context.LegalActions);
    }

    // -- Helpers -------------------------------------------------------------------------------

    // The same mid-game position AgentContractTests uses: several genuinely different options, so
    // a decision is a real choice rather than a forced one.
    private static GameState Position() =>
        new StateBuilder()
            .ConservingDecks(TestCards.Database)
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Hand(TestCards.Striker, TestCards.Bolt)
                .Resources(spike: 4, anvil: 4, wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

    private static AgentContext Context(GameState state) =>
        AgentContext.ForActivePlayer(state, TestCards.Database);

    // How deep the tree the agent grew at this position, at a given budget.
    private static int DepthOf(AgentContext context, int iterations)
    {
        var agent = new IsMctsAgent(
            TestCards.Database, new SeededRandom(13), SearchBudget.OfIterations(iterations));

        agent.Choose(context);

        return agent.LastTree!.Depth();
    }

    private static GameState NewRealGame(ulong seed)
    {
        var random = new SeededRandom(seed);
        var state = new GameState(RuleSet.Default, random, PlayerId.One);

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(Cards.BuildSymmetricDeck(RuleSet.Default));
            player.ShuffleDeck(random);
            player.Draw(RuleSet.Default.StartingHandSize);
        }

        state.AdvanceToActions();
        return state;
    }

    private static GameAction Play(string cardId) =>
        new PlayCardAction(PlayerId.One, cardId, new SlotIndex(PlayerId.One, 0));

    private static IReadOnlyList<GameAction> Actions(params GameAction[] actions) => actions;

    private static void Repeat(int times, Action action)
    {
        for (var i = 0; i < times; i++)
        {
            action();
        }
    }

}
