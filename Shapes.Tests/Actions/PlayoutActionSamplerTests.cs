using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Actions;

// DESIGN.md step 3.3b: PlayoutActionSampler.SampleOne must choose from the IDENTICAL legal set
// ActionGenerator.Generate would have produced, and with the identical (uniform) distribution --
// it is a faster path to the same answer, not a second implementation of legality that happens to
// agree most of the time.
//
// NOT tested as exact per-call agreement with `legal[random.Next(legal.Count)]` even when both
// are handed a same-position IRandomSource.Fork(): reservoir sampling consumes one random draw
// per candidate (N draws for N candidates) where index-pick consumes exactly one, so the two
// walk the SAME underlying random stream differently and land on different (still each
// individually uniform) picks. Verified by hand while building this test -- the two are provably
// different draws for the same random state, not a bug in either. So the properties actually
// checked are the ones that matter for correctness:
//
//   MEMBERSHIP AT EVERY POSITION -- over many driven-random-game positions
//   (LegalActionSoundnessTests' shape), SampleOne's result must always appear in Generate's list,
//   and must be null exactly when Generate's list is empty.
//
//   UNIFORMITY AT A FIXED POSITION -- over many independent draws at one position with several
//   legal actions, every action should come up with roughly equal frequency.
public class PlayoutActionSamplerTests
{
    private const int Games = 300;
    private const int MaxActionsPerGame = 400;

    [Fact]
    public void SampleOne_never_returns_an_action_Generate_would_not_have_offered()
    {
        var comparisons = 0;

        for (ulong seed = 1; seed <= Games; seed++)
        {
            comparisons += PlayRandomGame(seed, (state, driveRandom) =>
            {
                var legal = ActionGenerator.Generate(state, TestCards.Database);
                var sampled = PlayoutActionSampler.SampleOne(state, TestCards.Database, driveRandom.Fork());

                if (legal.Count == 0)
                {
                    Assert.Null(sampled);
                }
                else
                {
                    Assert.Contains(sampled, legal);
                }
            });
        }

        Assert.True(comparisons > Games, $"Expected meaningful play; only {comparisons} positions checked.");
    }

    [Fact]
    public void SampleOne_returns_null_exactly_when_the_game_is_over()
    {
        var state = new StateBuilder()
            .P1(p => p.Score(100))
            .Build();

        Assert.True(state.IsOver);
        Assert.Null(PlayoutActionSampler.SampleOne(state, TestCards.Database, new SeededRandom(1)));
    }

    [Fact]
    public void SampleOne_samples_uniformly_over_many_draws_at_a_fixed_position()
    {
        // A distribution check independent of Generate, at a position with several genuinely
        // different options: over many independent draws every legal action should appear with
        // roughly equal frequency. Loose bounds -- this is a sanity check against a systematically
        // biased reservoir (e.g. an off-by-one in the 1/n replacement probability), not a
        // statistical proof.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 5)
                .Hand(TestCards.Bolt)
                .Resources(spike: 4, anvil: 4, wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 5))
            .ConservingDecks(TestCards.Database)
            .Build();

        var legal = ActionGenerator.Generate(state, TestCards.Database);
        Assert.True(legal.Count > 1, "This position needs more than one legal action to test a distribution.");

        var counts = new Dictionary<GameAction, int>();
        const int Draws = 20_000;

        for (var i = 0; i < Draws; i++)
        {
            var picked = PlayoutActionSampler.SampleOne(state, TestCards.Database, new SeededRandom((ulong)(i + 1)));
            counts[picked!] = counts.GetValueOrDefault(picked!) + 1;
        }

        Assert.Equal(legal.Count, counts.Count);

        var expected = (double)Draws / legal.Count;
        foreach (var (action, count) in counts)
        {
            Assert.True(
                Math.Abs(count - expected) < expected * 0.35,
                $"'{action.Describe()}' was picked {count} times of {Draws}, expected ~{expected:0} -- "
                + "the reservoir sample looks biased.");
        }
    }

    // Runs one random game, invoking `check` at every position before an action is taken, mirroring
    // LegalActionSoundnessTests.PlayRandomGame's shape. `check` receives the SAME random source
    // that then drives the real game forward, so the caller can Fork() it to compare draws made
    // from an identical position without disturbing the game's own random stream.
    private static int PlayRandomGame(
        ulong seed, Action<GameState, IRandomSource> check)
    {
        var cards = TestCards.Database;
        var random = new SeededRandom(seed);

        var state = new StateBuilder()
            .WithSeed(seed)
            .P1(p => p.Deck(StartingDeck()).Resources(spike: 2, anvil: 2, wheel: 2))
            .P2(p => p.Deck(StartingDeck()).Resources(spike: 2, anvil: 2, wheel: 2))
            .Build();

        state[PlayerId.One].Draw(4);
        state[PlayerId.Two].Draw(4);

        var taken = 0;

        for (; taken < MaxActionsPerGame; taken++)
        {
            var actions = ActionGenerator.Generate(state, cards);

            if (actions.Count == 0)
            {
                break;
            }

            check(state, random);

            var action = actions[random.Next(actions.Count)];
            ActionExecutor.Apply(state, cards, action);
        }

        return taken;
    }

    private static string[] StartingDeck() =>
    [
        TestCards.Striker, TestCards.TwoMove, TestCards.Chooser, TestCards.Gated,
        TestCards.FreeMove, TestCards.Bolt, TestCards.TargetedBolt, TestCards.Striker,
        TestCards.TwoMove, TestCards.Bolt,
    ];
}
