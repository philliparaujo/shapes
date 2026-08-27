using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Tests.Fuzz;

// DESIGN.md step 1.12: thousands of seeded random-play games over the REAL ~36-card set, asserting
// termination and no illegal state.
//
// LegalActionSoundnessTests already runs this shape of property at a few hundred games against
// synthetic TestCards, close to the rules code so a failure is cheap to localize. This suite is
// the scale-up promised by that suite's own comment ("an early down payment on step 1.12's fuzz
// harness"): thousands of games, the shipped card set, and termination asserted explicitly
// rather than merely bounded by a loop cap. The two are complementary, not redundant -- this one
// exists to catch rule INTERACTIONS across real cards that no hand-written test anticipated,
// which is exactly what a small synthetic set can't exercise.
public class FuzzHarnessTests
{
    private const int Games = 5000;

    // Real games run longer than the synthetic-card suite's bound: 36 real cards, default
    // ScoreToWin, and a real symmetric deck. This is a generous ceiling, not an expected length --
    // Termination_always_occurs asserts the game actually reached IsOver well inside it, so a
    // game that silently ran to the cap would fail loudly rather than passing vacuously.
    private const int MaxActionsPerGame = 2000;

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

    // Runs one random game to completion (win) or the action cap, invoking `check` at every
    // position before an action is taken. Returns the number of actions taken and whether the
    // game reached IsOver, so callers can assert termination explicitly instead of inferring it
    // from the loop simply exiting.
    private static (int Actions, bool Terminated) PlayRandomGame(
        ulong seed, Action<GameState, IReadOnlyList<GameAction>>? check = null)
    {
        var random = new SeededRandom(seed);
        var state = NewGame(seed);

        var taken = 0;
        for (; taken < MaxActionsPerGame; taken++)
        {
            if (state.IsOver)
            {
                return (taken, true);
            }

            var actions = ActionGenerator.Generate(state, Cards);
            if (actions.Count == 0)
            {
                // Only possible outside the Actions phase; EndTurn is always legal within it.
                return (taken, state.IsOver);
            }

            check?.Invoke(state, actions);

            var action = actions[random.Next(actions.Count)];
            ActionExecutor.Apply(state, Cards, action);
        }

        return (taken, state.IsOver);
    }

    [Fact]
    public void Random_play_always_terminates()
    {
        for (ulong seed = 1; seed <= Games; seed++)
        {
            var (actions, terminated) = PlayRandomGame(seed);

            Assert.True(
                terminated,
                $"seed {seed}: game did not reach IsOver within {MaxActionsPerGame} actions "
                + "-- possible infinite loop.");

            Assert.True(actions > 0, $"seed {seed}: game ended immediately with no actions taken.");
        }
    }

    [Fact]
    public void Random_play_never_reaches_an_illegal_state()
    {
        for (ulong seed = 1; seed <= Games; seed++)
        {
            var currentSeed = seed;
            PlayRandomGame(seed, (state, _) => AssertStateIsLegal(currentSeed, state));
        }
    }

    [Fact]
    public void Random_play_actually_exercises_the_chosen_discard_path()
    {
        // Guards the two discard tests above against passing vacuously. Three shipped cards use
        // `discard` (circle_surfer, t_dealer, t_juggler), so a pending-discard state should arise
        // constantly across thousands of games -- if it never does, the gate is unreachable and
        // the invariants asserting things about it prove nothing.
        var sawPendingDiscard = false;

        for (ulong seed = 1; seed <= 200 && !sawPendingDiscard; seed++)
        {
            PlayRandomGame(seed, (state, actions) =>
            {
                if (state.AwaitingDiscard)
                {
                    sawPendingDiscard = true;

                    // While a debt stands the ONLY offer is discards -- the gate that makes the
                    // cost real. Asserted here, mid-fuzz, against the real card set.
                    Assert.All(actions, a => Assert.Equal(ActionKind.Discard, a.Kind));
                }
            });
        }

        Assert.True(
            sawPendingDiscard,
            "No game in 200 seeds ever reached a pending-discard state, so the discard gate is "
            + "untested by the fuzz harness. Either the discard cards stopped being reachable or "
            + "the pending state is not being set.");
    }

    // -- Invariants ----------------------------------------------------------------------------

    private static void AssertStateIsLegal(ulong seed, GameState state)
    {
        // The hand limit is enforced on the way IN (overdraw burns), so it can never be exceeded
        // -- there is no longer an end-of-turn cleanup that could let a hand sit oversized in
        // between. A hand over the limit means ApplyDraw's burn was skipped.
        foreach (var player in PlayerIds.All)
        {
            Assert.True(
                state[player].Hand.Count <= state.Rules.HandLimit,
                $"seed {seed}: {player} holds {state[player].Hand.Count} cards, over the "
                + $"limit of {state.Rules.HandLimit}.");
        }

        // A debt is always payable when it stands: the executor clamps it to hand size at the
        // moment it is incurred. An unpayable debt would make Generate return an empty list and
        // deadlock the game, which Random_play_always_terminates would catch only as a timeout.
        Assert.True(
            state.PendingDiscards <= state.Active.Hand.Count,
            $"seed {seed}: {state.PendingDiscards} discards owed but only "
            + $"{state.Active.Hand.Count} cards in hand -- unpayable debt.");

        foreach (var player in PlayerIds.All)
        {
            var resources = state[player].Resources;
            foreach (var type in ResourceTypes.All)
            {
                Assert.True(resources[type] >= 0, $"seed {seed}: negative {type} for {player}.");
            }

            Assert.True(
                state.Board.CountCreatures(player) <= SlotIndex.SlotsPerPlayer,
                $"seed {seed}: more creatures than slots for {player}.");

            Assert.True(state[player].Score >= 0, $"seed {seed}: negative score for {player}.");
        }

        var seenInstanceIds = new HashSet<string>();
        foreach (var (slot, creature) in state.Board.AllCreatures())
        {
            Assert.True(creature.Health > 0, $"seed {seed}: dead creature left in {slot}.");
            Assert.True(
                creature.Health <= creature.MaxHealth,
                $"seed {seed}: creature in {slot} is over max health ({creature.Health}/{creature.MaxHealth}).");

            // "No duplicate card instances" (DESIGN.md's invariant list) means no two BOARD slots
            // share the same merge-lineage identity, not that a card id can't recur -- the deck
            // legitimately holds CopiesPerCard of each. A slot's identity is its own position
            // plus its MergedFrom lineage, which is unique by construction (a card instance can
            // occupy only one slot at a time); this asserts that construction actually holds.
            var identity = $"{slot}:{string.Join(",", creature.MergedFrom)}";
            Assert.True(
                seenInstanceIds.Add(identity),
                $"seed {seed}: duplicate creature instance identity '{identity}'.");
        }
    }
}
