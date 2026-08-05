using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.State;

namespace Shapes.Ai.Search;

// The original playout policy: pick uniformly at random among the legal actions. Kept as the
// control for step 3.2's comparison -- if the heuristic policy is not measurably better than
// this, it is not worth the extra cost per playout.
public sealed class UniformPlayoutPolicy : IPlayoutPolicy
{
    public static readonly UniformPlayoutPolicy Instance = new();

    public GameAction SelectAction(
        GameState state, IReadOnlyList<GameAction> legal, CardDatabase cards, IRandomSource random) =>
        legal[random.Next(legal.Count)];
}
