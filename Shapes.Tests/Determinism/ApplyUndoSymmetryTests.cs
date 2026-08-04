using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Determinism;

// Phase 1 exit criterion: "Apply/undo property tests pass."
//
// PLAN.md's own design section ("Apply/undo over clone") is explicit that the undo-RECORD
// mechanism itself is a Phase 2 optimization -- Shapes.Core has no Undo API yet, only Clone().
// It also says to write this property "in Phase 1 even while still cloning": right now, CLONE
// *is* the undo mechanism -- reverting a speculative action means discarding the mutated clone
// and keeping the untouched original, rather than replaying an inverse. So the property that is
// actually testable today, and the one every future undo-record implementation must continue to
// satisfy, is: cloning a state and then mutating the clone must never affect the original, and
// the clone must start out byte-identical to what it was cloned from. That is "apply then undo
// (discard the clone) returns a state byte-identical to the original" with TODAY's undo mechanism.
public class ApplyUndoSymmetryTests
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

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(4242UL)]
    public void Cloning_a_state_produces_a_byte_identical_copy(ulong seed)
    {
        var state = NewGame(seed);
        var clone = state.Clone();

        Assert.Equal(StateSnapshot.Of(state), StateSnapshot.Of(clone));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(4242UL)]
    public void Applying_an_action_to_a_clone_never_mutates_the_original(ulong seed)
    {
        var random = new SeededRandom(seed);

        for (var step = 0; step < 30; step++)
        {
            var state = NewGame(seed);
            AdvanceRandomly(state, random, step);

            if (state.IsOver)
            {
                break;
            }

            var actions = ActionGenerator.Generate(state, Cards);
            if (actions.Count == 0)
            {
                break;
            }

            var before = StateSnapshot.Of(state);
            var clone = state.Clone();

            var action = actions[random.Next(actions.Count)];

            // "Undo" today is: apply to the clone, then discard it. The property is what that
            // discard depends on -- the original was never touched.
            ActionExecutor.Apply(clone, Cards, action);

            Assert.True(
                StateSnapshot.Of(state) == before,
                $"seed {seed} step {step}: applying '{action.Describe()}' to a CLONE mutated the "
                + "original -- Clone() is not a true deep copy, so apply/undo-over-clone is unsound.");
        }
    }

    // Walks the real game forward a few random actions so the property is checked from a variety
    // of mid-game positions -- merged creatures, partially spent hands, drawn resources -- not
    // only the empty starting board every seed begins at.
    private static void AdvanceRandomly(GameState state, IRandomSource random, int steps)
    {
        for (var i = 0; i < steps && !state.IsOver; i++)
        {
            var actions = ActionGenerator.Generate(state, Cards);
            if (actions.Count == 0)
            {
                return;
            }

            ActionExecutor.Apply(state, Cards, actions[random.Next(actions.Count)]);
        }
    }
}
