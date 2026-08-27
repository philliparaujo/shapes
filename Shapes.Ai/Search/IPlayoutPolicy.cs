using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.State;

namespace Shapes.Ai.Search;

// How IsMctsAgent's playout phase picks an action once selection/expansion have left the tree
// and the search is just simulating to a result. DESIGN.md step 3.2: uniform-random is the
// original, uninstrumented playout (kept as the control so a heuristic's effect can be measured
// against it, not assumed); a lightly heuristic policy is the alternative under test.
//
// Deliberately narrow: `state` and `legal` are exactly what PlayOut already has in hand, and
// nothing here may mutate `state` or retain either argument past the call, since both are
// reused across many playout steps within one search iteration.
public interface IPlayoutPolicy
{
    GameAction SelectAction(
        GameState state, IReadOnlyList<GameAction> legal, CardDatabase cards, IRandomSource random);
}
