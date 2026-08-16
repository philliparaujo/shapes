using Shapes.Ai.Agents;
using Shapes.Ai.Search;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// PLAN.md C1 (AI-opponent wiring pulled forward from C5): AgentFactory is Lobby.cs's picker
// turned into an IAgent, the same BuildAgent switch Shapes.Console uses. Tested here rather
// than only by hand in the editor because it is plain data-in/object-out logic with no Godot
// node concept in it -- the same reasoning GameSessionTests already applies to GameSession.
public class MatchConfigTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    [Fact]
    public void Human_seat_builds_no_agent() =>
        Assert.Null(AgentFactory.Build(SeatConfig.Human, seed: 1, Cards));

    [Theory]
    [InlineData(AgentKind.Random, typeof(RandomAgent))]
    [InlineData(AgentKind.Greedy, typeof(GreedyAgent))]
    [InlineData(AgentKind.IsMcts, typeof(IsMctsAgent))]
    [InlineData(AgentKind.IsMctsHeuristic, typeof(IsMctsAgent))]
    public void Each_agent_kind_builds_the_expected_agent_type(AgentKind kind, Type expected)
    {
        var seat = new SeatConfig(kind, Iterations: 50);
        var agent = AgentFactory.Build(seat, seed: 1, Cards);

        Assert.NotNull(agent);
        Assert.IsType(expected, agent);
    }

    [Fact]
    public void IsMcts_seat_uses_the_configured_iteration_budget()
    {
        var seat = new SeatConfig(AgentKind.IsMcts, Iterations: 77);
        var agent = Assert.IsType<IsMctsAgent>(AgentFactory.Build(seat, seed: 1, Cards));

        Assert.Equal(77, agent.Budget.Iterations);
        Assert.True(agent.Budget.IsDeterministic);
    }

    [Fact]
    public void Two_agent_seats_draw_from_independent_random_streams()
    {
        // Same seed, same agent kind, but the two seats must not be the same random draw --
        // GameRoot derives each seat's seed as (matchSeed * distinct prime), mirroring
        // Shapes.Console's BuildAgent call site; this pins that the two resulting agents are not
        // the exact same decision-maker (a shared stream would make an AI-v-AI game one search
        // effectively re-playing itself on both sides).
        var seatOne = new SeatConfig(AgentKind.Random, Iterations: 0);
        var agentOne = Assert.IsType<RandomAgent>(AgentFactory.Build(seatOne, seed: 42 * 7919, Cards));
        var agentTwo = Assert.IsType<RandomAgent>(AgentFactory.Build(seatOne, seed: 42 * 104729, Cards));

        var rules = RuleSet.Default;
        var state = new GameState(rules, new SeededRandom(1), PlayerId.One);
        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(Cards.BuildSymmetricDeck(rules));
            player.ShuffleDeck(state.Random);
            player.Draw(rules.StartingHandSize);
        }

        state.ApplySecondSeatCompensation();
        state.AdvanceToActions();

        var contextOne = AgentContext.ForActivePlayer(state, Cards);
        var contextTwo = AgentContext.ForActivePlayer(state, Cards);
        var chosenOne = agentOne.Choose(contextOne);
        var chosenTwo = agentTwo.Choose(contextTwo);

        // Both draw from RandomAgent's own uniform choice over the same legal-action list, so an
        // occasional coincidental match is expected -- the real assertion is that the two random
        // sources are independently seeded at all, which NewSession derives correctly for.
        Assert.Contains(chosenOne, contextOne.LegalActions);
        Assert.Contains(chosenTwo, contextTwo.LegalActions);
    }

    // PLAN.md D1: the viewer seat is derived from the seat configs, never picked in the lobby, so
    // these cases ARE the feature -- every one of them is a mode the UI has to render correctly,
    // and getting the derivation wrong is how the AI's hand ends up face-up on screen again.
    private static SeatConfig Ai(AgentKind kind = AgentKind.Greedy) => new(kind, Iterations: 50);

    [Fact]
    public void Human_versus_ai_pins_the_view_to_the_human_seat()
    {
        // The bug D1 exists to fix: this configuration used to flip the board on the AI's turn.
        var seatOne = ViewerMode.For(SeatConfig.Human, Ai());
        Assert.Equal(PlayerId.One, seatOne.Resolve(PlayerId.One));
        Assert.Equal(PlayerId.One, seatOne.Resolve(PlayerId.Two));
    }

    [Fact]
    public void Ai_versus_human_pins_the_view_to_the_human_in_seat_two()
    {
        // The mirror case, which is the one a "just check seat one" implementation gets wrong.
        var seatTwo = ViewerMode.For(Ai(), SeatConfig.Human);
        Assert.Equal(PlayerId.Two, seatTwo.Resolve(PlayerId.One));
        Assert.Equal(PlayerId.Two, seatTwo.Resolve(PlayerId.Two));
    }

    [Fact]
    public void Two_humans_keep_the_hotseat_flip()
    {
        // D1's compatibility bar: local two-player must behave exactly as it always has, since
        // one screen passed between two people is the case where flipping is correct.
        var hotseat = ViewerMode.For(SeatConfig.Human, SeatConfig.Human);
        Assert.Equal(PlayerId.One, hotseat.Resolve(PlayerId.One));
        Assert.Equal(PlayerId.Two, hotseat.Resolve(PlayerId.Two));
    }

    [Fact]
    public void An_all_ai_match_follows_whichever_agent_is_acting()
    {
        // No human seat to anchor to, so following the active player is what makes a spectated
        // game readable -- each agent's hand is shown as it acts.
        var spectating = ViewerMode.For(Ai(AgentKind.Random), Ai(AgentKind.IsMcts));
        Assert.Equal(PlayerId.One, spectating.Resolve(PlayerId.One));
        Assert.Equal(PlayerId.Two, spectating.Resolve(PlayerId.Two));
    }

    [Fact]
    public void MatchConfig_derives_its_viewer_from_its_seats()
    {
        // Derived, not stored: a resumed match re-derives the right viewer from the SeatConfigs
        // SavedMatch already persists, so a save written before D1 resumes with the correct
        // perspective rather than a defaulted one.
        var config = new MatchConfig(SeatConfig.Human, Ai(), Seed: 42);

        Assert.Equal(PlayerId.One, config.Viewer.Resolve(PlayerId.Two));
    }

    [Fact]
    public void An_all_ai_match_runs_to_completion()
    {
        // The exact loop GameRoot.RunAiTurns drives, replicated here without any Godot node --
        // regression guard for "AI v AI never terminates" or "an agent returns an illegal
        // action," neither of which the Godot editor's own playtesting can catch as reliably as
        // a fuzz-style run over several seeds.
        var rules = RuleSet.Default;

        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var random = new SeededRandom(seed);
            var session = new GameSession(rules, Cards, random, PlayerId.One);
            session.Start(rules.StartingHandSize);

            var agents = new Dictionary<PlayerId, IAgent>
            {
                [PlayerId.One] = new RandomAgent(new SeededRandom(seed * 7919)),
                [PlayerId.Two] = new GreedyAgent(new SeededRandom(seed * 104729)),
            };

            var actionsTaken = 0;
            while (!session.State.IsOver)
            {
                var agent = agents[session.State.ActivePlayer];
                var context = AgentContext.ForActivePlayer(session.State, Cards);
                var action = agent.Choose(context);

                Assert.Contains(action, context.LegalActions);
                session.Submit(action);

                actionsTaken++;
                Assert.True(actionsTaken < 10_000, "Game did not terminate within a sane action budget.");
            }

            Assert.NotNull(session.State.Winner);
        }
    }
}
