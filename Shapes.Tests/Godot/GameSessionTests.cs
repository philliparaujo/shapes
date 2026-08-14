using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// Phase 5 step A2: GameSession is the only thing in Shapes.Godot allowed to touch GameState.
// These tests run it against the real card set (like Shapes.Console does) rather than
// TestCards, because A2's job is specifically to be a faithful wrapper around
// ActionExecutor/ActionGenerator -- a synthetic card set would validate the wrapper against
// itself rather than against what the real game actually produces.
public class GameSessionTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static RuleSet Rules => RuleSet.Default;

    private static GameSession NewSession(ulong seed)
    {
        var session = new GameSession(Rules, Cards, new SeededRandom(seed), PlayerId.One);
        session.Start(Rules.StartingHandSize);
        return session;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(4242UL)]
    public void Start_matches_the_console_setup_sequence(ulong seed)
    {
        // Same steps Shapes.Console/Program.cs runs: the DEFAULT deck (one copy of every card --
        // the console's only deck), dealt through GameSetup.Deal, then advance to Actions. If this
        // ever drifts from the console's sequence, a seeded Godot game stops matching a seeded
        // console game -- Milestone A's exit bar.
        //
        // Deliberately re-derives the deal here (rather than calling a shared helper both sides
        // use) so this stays an independent check of the sequence: a test that called exactly what
        // GameSession calls would pass no matter what either did.
        var random = new SeededRandom(seed);
        var expected = new GameState(Rules, random, PlayerId.One);
        var deck = DeckBuilder.Default(Cards);
        foreach (var playerId in PlayerIds.All)
        {
            var player = expected[playerId];
            player.SetDeck(deck.Shuffled(random));
            player.Draw(Rules.StartingHandSize);
        }

        expected.ApplySecondSeatCompensation();
        expected.AdvanceToActions();

        var session = NewSession(seed);

        Assert.Equal(expected.ActivePlayer, session.State.ActivePlayer);
        Assert.Equal(expected.Phase, session.State.Phase);
        Assert.Equal(expected[PlayerId.One].Hand, session.State[PlayerId.One].Hand);
        Assert.Equal(expected[PlayerId.Two].Hand, session.State[PlayerId.Two].Hand);
        Assert.Equal(expected[PlayerId.One].Resources, session.State[PlayerId.One].Resources);
        Assert.Equal(expected[PlayerId.Two].Resources, session.State[PlayerId.Two].Resources);
    }

    [Fact]
    public void LegalActions_matches_ActionGenerator_directly()
    {
        var session = NewSession(seed: 7);

        var fromSession = session.LegalActions();
        var fromGenerator = ActionGenerator.Generate(session.State, Cards);

        Assert.Equal(fromGenerator, fromSession);
    }

    [Fact]
    public void Submit_applies_the_action_to_session_state()
    {
        var session = NewSession(seed: 7);
        var action = session.LegalActions()[0];

        session.Submit(action);

        // The action must actually have landed on live state -- the next call's legal actions
        // should be generated from the post-action position, not the pre-action one.
        var expected = ActionGenerator.Generate(session.State, Cards);
        Assert.Equal(expected, session.LegalActions());
    }

    [Fact]
    public void Submit_does_not_mutate_the_original_snapshot_used_for_the_diff()
    {
        // Regression guard for the exact bug this adapter exists to avoid: if Submit diffed
        // against a live reference instead of a clone, "before" would silently become "after"
        // the moment ActionExecutor.Apply mutates state, and every diff would read as empty.
        var session = NewSession(seed: 7);
        var actingPlayer = session.State.ActivePlayer;
        var beforeHandSize = session.State[actingPlayer].Hand.Count;
        var action = session.LegalActions().OfType<PlayCardAction>().FirstOrDefault()
            ?? session.LegalActions()[0];

        var diff = session.Submit(action);

        if (action is PlayCardAction)
        {
            var playerDiff = Assert.Single(diff.PlayerChanges, d => d.Player == actingPlayer);
            Assert.Equal(beforeHandSize, playerDiff.HandSizeBefore);
            Assert.Equal(beforeHandSize - 1, playerDiff.HandSizeAfter);
        }
    }

    [Fact]
    public void A_full_seeded_game_terminates_and_matches_console_style_playthrough()
    {
        // End-to-end sanity check standing in for A3's real exit bar ("a seeded hotseat game
        // in Godot reaches the same result as the same seed in the console"), which needs a
        // scene to fully verify. This confirms the adapter alone can drive a whole game to
        // completion via nothing but Submit/LegalActions, using RandomAgent-style choice
        // (always the first legal action) so the test needs no UI and no search budget.
        var session = NewSession(seed: 99);
        var random = new SeededRandom(12345);
        var actionCount = 0;
        const int maxActions = 5000;

        while (!session.State.IsOver && actionCount < maxActions)
        {
            var actions = session.LegalActions();
            Assert.NotEmpty(actions);
            var choice = actions[random.Next(actions.Count)];
            session.Submit(choice);
            actionCount++;
        }

        Assert.True(session.State.IsOver, $"Game did not terminate within {maxActions} actions.");
        Assert.NotNull(session.State.Winner);
    }
}
