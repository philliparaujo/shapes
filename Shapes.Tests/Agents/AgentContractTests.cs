using Shapes.Ai.Agents;
using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Agents;

// The IAgent contract, tested against the interface rather than any one implementation.
//
// Everything Phase 2 builds -- GreedyAgent, IS-MCTS -- has to satisfy these same properties, so
// the assertions are written as theories over an agent factory. Step 2.4's GreedyAgent is wired
// in here rather than getting a parallel suite that might check less; step 2.6's IS-MCTS adds
// one line to Agents below.
//
// The contract has three clauses, and each has a test below:
//   1. The chosen action is one of the legal actions offered.
//   2. The agent does not mutate the state it was handed.
//   3. Given the same seed, the same decisions -- the determinism the whole plan rests on.
public class AgentContractTests
{
    // Every IAgent implementation, as a seed -> agent factory. A theory over this is what makes
    // the clauses below apply to the interface rather than to RandomAgent specifically: a new
    // agent that violates the contract fails these without anyone remembering to test it.
    //
    // Named rather than bare factories so a failure message says which agent broke the clause.
    public static TheoryData<string> Agents => new() { "Random", "Greedy" };

    private static IAgent Agent(string name, ulong seed) => name switch
    {
        "Random" => new RandomAgent(new SeededRandom(seed)),
        "Greedy" => new GreedyAgent(new SeededRandom(seed)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown agent."),
    };
    private static AgentContext Context(GameState state) =>
        AgentContext.ForActivePlayer(state, TestCards.Database);

    // A mid-game position with several genuinely different options available -- creatures to
    // play, a move to use, and a merge -- so "picked something legal" is a real assertion
    // rather than a test that EndTurn is the only choice.
    private static GameState Position() =>
        new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Hand(TestCards.Striker, TestCards.Bolt)
                .Resources(spike: 4, anvil: 4, wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

    // -- Clause 1: the chosen action is legal -----------------------------------------------

    [Theory]
    [MemberData(nameof(Agents))]
    public void An_agent_chooses_from_the_offered_actions(string name)
    {
        // Repeated because a single draw could pass by luck even if the agent were indexing
        // the wrong list -- over many draws it would eventually return something foreign.
        var agent = Agent(name, 7);
        var context = Context(Position());

        for (var i = 0; i < 200; i++)
        {
            Assert.Contains(agent.Choose(context), context.LegalActions);
        }
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void A_chosen_action_is_applicable_without_throwing(string name)
    {
        // The generator/executor contract seen from the agent's side: whatever an agent picks
        // out of the legal list, the caller applies WITHOUT re-validating it. If an agent could
        // return something unapplicable, every consumer would need a defensive re-check --
        // which is the second definition of "legal" the codebase deliberately does not have.
        var agent = Agent(name, 11);

        for (var i = 0; i < 100; i++)
        {
            var state = Position();
            var action = agent.Choose(Context(state));

            ActionExecutor.Apply(state, TestCards.Database, action);
        }
    }

    // -- Clause 2: choosing does not mutate the state ---------------------------------------

    [Theory]
    [MemberData(nameof(Agents))]
    public void Choosing_does_not_mutate_the_state_it_was_given(string name)
    {
        // An agent returns an edge; it never walks one. A search that explored on the caller's
        // state instead of its own clone would corrupt the live game, and the failure would
        // show up far from its cause -- as a board that changed without an action being
        // applied. StateSnapshot is the same byte-comparison the apply/undo property test uses.
        //
        // GreedyAgent makes this a live risk rather than a formality: it reads creature state
        // (AttackBuff, NextAttackBonus) that the real damage path CONSUMES, so a scoring pass
        // written against the engine's own consume-methods would silently clear a bonus the
        // player had not yet spent.
        var state = Position();
        var before = StateSnapshot.Of(state);

        var agent = Agent(name, 3);
        for (var i = 0; i < 50; i++)
        {
            agent.Choose(Context(state));
        }

        Assert.Equal(before, StateSnapshot.Of(state));
    }

    // -- Clause 3: determinism --------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Agents))]
    public void The_same_seed_produces_the_same_decisions(string name)
    {
        // The property every balance number in Phase 3 depends on. Without it a suspicious sim
        // result cannot be re-run and inspected, so a bug found in Phase 3 could not be
        // isolated to a game.
        var context = Context(Position());

        var first = Decisions(Agent(name, 42), context);
        var second = Decisions(Agent(name, 42), context);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seeds_produce_different_decisions_for_a_random_agent()
    {
        // Guards the degenerate way the previous test could pass: an agent that always returned
        // LegalActions[0] would be perfectly reproducible and completely useless. This asserts
        // the randomness is actually wired to the seed.
        //
        // Applies to RandomAgent only, and NOT to GreedyAgent -- see the next test for why that
        // is a real difference rather than an oversight.
        var context = Context(Position());

        var first = Decisions(new RandomAgent(new SeededRandom(1)), context);
        var second = Decisions(new RandomAgent(new SeededRandom(999)), context);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_greedy_agent_ignores_its_seed_when_one_action_scores_highest()
    {
        // The counterpart to the test above, stating the distinction rather than leaving
        // GreedyAgent quietly exempt from it. Greedy's RNG breaks TIES ONLY: given a position
        // with a single best action, every seed must produce it, because the heuristic -- not
        // the stream -- is what decided. An implementation that let randomness leak into a
        // non-tied choice would still pass the same-seed test while playing differently run to
        // run at the same position, which is exactly the failure that makes a baseline useless
        // as a yardstick.
        //
        // Position() has an unambiguous best action: P1's Striker in slot 0 faces P2's Striker,
        // and its Strike move is the only one that damages anything.
        var context = Context(Position());

        var decisions = new[] { 1UL, 42UL, 999UL, 12345UL }
            .Select(seed => new GreedyAgent(new SeededRandom(seed)).Choose(context))
            .ToList();

        Assert.Single(decisions.Distinct());
    }

    private static List<GameAction> Decisions(IAgent agent, AgentContext context) =>
        [.. Enumerable.Range(0, 40).Select(_ => agent.Choose(context))];

    // -- AgentContext ------------------------------------------------------------------------

    [Fact]
    public void Context_for_the_active_player_generates_the_engines_legal_actions()
    {
        // The context must not offer a list of its own devising. Pinning it as equal to
        // ActionGenerator's output is what keeps AgentContext a courier rather than a second
        // opinion about the rules.
        var state = Position();

        var context = Context(state);

        Assert.Equal(ActionGenerator.Generate(state, TestCards.Database), context.LegalActions);
        Assert.Equal(state.ActivePlayer, context.Player);
    }

    [Fact]
    public void Context_rejects_an_empty_action_list()
    {
        // Unreachable in a live game -- EndTurn is always available -- so reaching it means the
        // caller asked a finished game for a decision. Failing here names that, instead of the
        // error surfacing as an index-out-of-range inside whichever agent happened to run.
        var state = Position();

        Assert.Throws<ArgumentException>(() =>
            new AgentContext(state, PlayerId.One, [], TestCards.Database));
    }

    [Fact]
    public void A_finished_game_offers_no_actions_to_wrap()
    {
        // The concrete route into that empty list: a won game generates nothing at all, so a
        // driver loop must check IsOver before asking an agent to move.
        var state = new StateBuilder()
            .P1(p => p.Score(RuleSet.Default.ScoreToWin))
            .Build();

        Assert.True(state.IsOver);
        Assert.Empty(ActionGenerator.Generate(state, TestCards.Database));
        Assert.Throws<ArgumentException>(() => Context(state));
    }
}
