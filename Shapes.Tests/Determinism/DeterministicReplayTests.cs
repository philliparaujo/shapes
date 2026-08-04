using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Determinism;

// Phase 1 exit criterion: "A scripted game replays identically from a seed."
//
// LegalActionSoundnessTests and FuzzHarnessTests already prove determinism under RANDOM play --
// the same seed picks the same random actions twice. This is the narrower, explicit thing the
// exit criterion actually names: a fixed, hand-authored SCRIPT of actions (not randomly chosen)
// replayed against a fresh identically-seeded game, asserting the two runs are byte-identical at
// every step, not just at the end -- so a divergence is pinned to the exact action that caused
// it rather than discovered only in a final-state diff.
public class DeterministicReplayTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static RuleSet Rules => RuleSet.Default;

    private static GameState NewGame(ulong seed)
    {
        var random = new SeededRandom(seed);
        var state = new GameState(Rules, random, PlayerId.One);

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(Cards.BuildSymmetricDeck(Rules));
            player.ShuffleDeck(random);
            player.Draw(Rules.StartingHandSize);
        }

        state.AdvanceToActions();
        return state;
    }

    // A fixed sequence of choices: at each step, pick the Nth legal action (wrapping if the
    // count shrinks), rather than a random one. "Scripted" only needs to mean "not random" --
    // this reaches the same real board/hand/resource permutations every replay without being a
    // hand-typed 200-action fixture that breaks whenever a card is rebalanced.
    private static readonly int[] Script =
        [0, 1, 0, 2, 1, 0, 0, 3, 1, 2, 0, 1, 0, 0, 2, 1, 0, 3, 0, 1];

    private static List<string> Replay(ulong seed)
    {
        var state = NewGame(seed);
        var snapshots = new List<string> { StateSnapshot.Of(state) };

        foreach (var choice in Script)
        {
            if (state.IsOver)
            {
                break;
            }

            var actions = ActionGenerator.Generate(state, Cards);
            if (actions.Count == 0)
            {
                break;
            }

            var action = actions[choice % actions.Count];
            ActionExecutor.Apply(state, Cards, action);
            snapshots.Add($"{action.Describe()} -> {StateSnapshot.Of(state)}");
        }

        return snapshots;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    [InlineData(999999UL)]
    public void The_same_scripted_game_replays_identically_from_the_same_seed(ulong seed)
    {
        var first = Replay(seed);
        var second = Replay(seed);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.True(
                first[i] == second[i],
                $"seed {seed}: replay diverged at step {i}.\nRun 1: {first[i]}\nRun 2: {second[i]}");
        }
    }

    [Fact]
    public void A_scripted_replay_actually_takes_multiple_actions()
    {
        // Guards the property above against passing vacuously if the script's choices somehow
        // stalled the game at turn one (e.g. every choice resolving to EndTurn immediately).
        var snapshots = Replay(seed: 1);

        Assert.True(
            snapshots.Count > 5,
            $"Expected the scripted replay to take several actions; only reached {snapshots.Count - 1}.");
    }
}
