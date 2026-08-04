using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Actions;

// Property tests over random legal play: whatever the generator offers, the executor must
// accept.
//
// This is the suite that catches what the example tests above cannot. Hand-written tests check
// the rules someone thought to check; these play hundreds of random games and assert the
// invariants hold in positions nobody anticipated -- merged creatures with partially-used move
// lists, boards emptied mid-turn, hands drained to nothing.
//
// It is also an early down payment on step 1.13's fuzz harness. That step adds scale and the
// remaining invariants; the soundness property belongs here, with the API it constrains.
//
// STRUCTURE: the cheap position-invariants share ONE playthrough rather than each replaying the
// same games. They are independent assertions about the same positions, so re-deriving those
// positions per property was pure duplicated work -- six identical random games differing only
// in what they checked. Checking them together costs one playthrough instead of five and, more
// usefully, a failure now reports every invariant a position breaks rather than only the first
// test to run. The two properties that genuinely need their own driver -- the clone-heavy
// soundness probe and determinism, which needs two runs by definition -- keep one.
public class LegalActionSoundnessTests
{
    private const int Games = 300;
    private const int MaxActionsPerGame = 400;

    // The soundness probe clones the state once per candidate action, so it costs roughly a
    // branching factor more per position than the others. Fewer games buys the same confidence:
    // it is the positions that vary, and 120 seeds still reach the merged/partially-used states
    // that matter. Raise it when chasing a specific failure.
    private const int SoundnessGames = 120;

    private const int DeterminismGames = 50;

    // Runs one random game, invoking `check` at every position before an action is taken.
    // Returns the number of actions taken, so a caller can assert the game actually progressed
    // rather than passing vacuously on a position with nothing to do.
    private static int PlayRandomGame(ulong seed, Action<GameState, IReadOnlyList<GameAction>> check)
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

            check(state, actions);

            var action = actions[random.Next(actions.Count)];
            ActionExecutor.Apply(state, cards, action);

            // Score and income are step 1.9's turn loop, not this step's. Applying them inline
            // here keeps random play from stalling in a resource-starved position forever and
            // makes the games reach genuinely varied board states -- which is the whole point
            // of a property test.
            if (state.Phase == TurnPhase.Scoring)
            {
                state.ApplyScoring();
                state.ApplyIncome();
            }
        }

        return taken;
    }

    private static string[] StartingDeck() =>
    [
        TestCards.Striker, TestCards.TwoMove, TestCards.Chooser, TestCards.Gated,
        TestCards.FreeMove, TestCards.Bolt, TestCards.TargetedBolt, TestCards.Striker,
        TestCards.TwoMove, TestCards.Bolt,
    ];

    [Fact]
    public void Random_legal_play_upholds_every_position_invariant()
    {
        // Four independent properties over the same positions, asserted in one pass:
        //
        //   1. NON-EMPTINESS   -- EndTurn is always offered, so a player is never stuck and
        //                         random play terminates rather than deadlocking.
        //   2. AFFORDABILITY   -- no offered action costs more than the player holds.
        //   3. NO DUPLICATES   -- two identical edges would split MCTS statistics across them.
        //   4. LEGAL STATE     -- no negative resources, no over-max or dead creatures left on
        //                         the board, never more creatures than slots.
        //
        // Each carries its own message, so a failure still names which invariant broke.
        var totalActions = 0;

        for (ulong seed = 1; seed <= Games; seed++)
        {
            var currentSeed = seed;

            totalActions += PlayRandomGame(seed, (state, actions) =>
            {
                AssertEndTurnIsOffered(currentSeed, actions);
                AssertAllAffordable(currentSeed, state, actions);
                AssertNoDuplicates(currentSeed, actions);
                AssertStateIsLegal(currentSeed, state);
            });
        }

        // Guards against the whole property passing vacuously -- if the driver stalled at turn
        // one, every assertion above would hold trivially.
        Assert.True(totalActions > Games, $"Expected meaningful play; only {totalActions} actions taken.");
    }

    [Fact]
    public void Every_generated_action_applies_without_throwing()
    {
        // Soundness, and the reason ActionExecutor is allowed to assume legality and re-check
        // nothing. A failure here is a generator bug by definition.
        //
        // Kept separate from the position-invariants above because it is the expensive one: it
        // applies every candidate action, not just the one played.
        var totalActions = 0;

        for (ulong seed = 1; seed <= SoundnessGames; seed++)
        {
            var currentSeed = seed;

            totalActions += PlayRandomGame(seed, (state, actions) =>
            {
                // Applying every action would fork the game, so each candidate is applied to its
                // own CLONE, leaving the real game to continue down the single randomly chosen
                // path.
                foreach (var action in actions)
                {
                    var probe = state.Clone();
                    var ex = Record.Exception(() => ActionExecutor.Apply(probe, TestCards.Database, action));

                    Assert.True(
                        ex is null,
                        $"seed {currentSeed}: legal action '{action.Describe()}' threw "
                        + $"{ex?.GetType().Name}: {ex?.Message}");
                }
            });
        }

        Assert.True(totalActions > SoundnessGames, $"Expected meaningful play; only {totalActions} actions.");
    }

    [Fact]
    public void The_same_seed_and_actions_produce_the_same_game_twice()
    {
        // Determinism. Everything downstream -- reproducible bug reports, comparable balance
        // runs, seed replay on a phone in Phase 4 -- rests on this. Needs its own driver by
        // definition: the property IS that two independent runs agree.
        for (ulong seed = 1; seed <= DeterminismGames; seed++)
        {
            Assert.Equal(TranscriptOf(seed), TranscriptOf(seed));
        }
    }

    // -- Invariants ----------------------------------------------------------------------------

    private static void AssertEndTurnIsOffered(ulong seed, IReadOnlyList<GameAction> actions) =>
        Assert.True(
            actions.Any(a => a.Kind == ActionKind.EndTurn),
            $"seed {seed}: no EndTurn offered -- the player is stuck.");

    private static void AssertAllAffordable(
        ulong seed, GameState state, IReadOnlyList<GameAction> actions)
    {
        var resources = state[state.ActivePlayer].Resources;

        foreach (var action in actions)
        {
            var cost = CostOf(state, action);
            Assert.True(
                resources.Covers(cost),
                $"seed {seed}: '{action.Describe()}' costs {cost} but the player holds {resources}.");
        }
    }

    private static void AssertNoDuplicates(ulong seed, IReadOnlyList<GameAction> actions) =>
        Assert.True(
            actions.Count == actions.Distinct().Count(),
            $"seed {seed}: the generated list contains duplicate actions.");

    private static void AssertStateIsLegal(ulong seed, GameState state)
    {
        foreach (var player in PlayerIds.All)
        {
            var resources = state[player].Resources;
            foreach (var type in ResourceTypes.All)
            {
                Assert.True(resources[type] >= 0, $"seed {seed}: negative {type}.");
            }

            Assert.True(
                state.Board.CountCreatures(player) <= SlotIndex.SlotsPerPlayer,
                $"seed {seed}: more creatures than slots for {player}.");
        }

        foreach (var (slot, creature) in state.Board.AllCreatures())
        {
            Assert.True(creature.Health > 0, $"seed {seed}: dead creature left in {slot}.");
            Assert.True(
                creature.Health <= creature.MaxHealth,
                $"seed {seed}: creature in {slot} is over max health.");
        }
    }

    // -- Helpers -------------------------------------------------------------------------------

    private static List<string> TranscriptOf(ulong seed)
    {
        var transcript = new List<string>();
        var random = new SeededRandom(seed);
        var cards = TestCards.Database;

        var state = new StateBuilder()
            .WithSeed(seed)
            .P1(p => p.Deck(StartingDeck()).Resources(spike: 2, anvil: 2, wheel: 2))
            .P2(p => p.Deck(StartingDeck()).Resources(spike: 2, anvil: 2, wheel: 2))
            .Build();

        state[PlayerId.One].Draw(4);
        state[PlayerId.Two].Draw(4);

        for (var i = 0; i < MaxActionsPerGame; i++)
        {
            var actions = ActionGenerator.Generate(state, cards);
            if (actions.Count == 0)
            {
                break;
            }

            var action = actions[random.Next(actions.Count)];
            ActionExecutor.Apply(state, cards, action);
            transcript.Add($"{action.Describe()} -> {state}");

            if (state.Phase == TurnPhase.Scoring)
            {
                state.ApplyScoring();
                state.ApplyIncome();
            }
        }

        return transcript;
    }

    // What an action costs its player. Merge and EndTurn are free -- merging deliberately so
    // (it is a free action), EndTurn structurally.
    private static ResourcePool CostOf(GameState state, GameAction action) => action switch
    {
        PlayCardAction play => TestCards.Database.Get(play.CardId).Cost,
        UseMoveAction move => MoveCost(state, move),
        _ => ResourcePool.Empty,
    };

    private static ResourcePool MoveCost(GameState state, UseMoveAction action)
    {
        var creature = state.Board[action.SourceSlot]!;
        return TestCards.Database.MovesOf(creature.MergedFrom)[action.MoveIndex].Cost;
    }
}
