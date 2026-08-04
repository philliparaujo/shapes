namespace Shapes.Core.Effects;

// Walks nested effect trees.
//
// Control-flow ops (conditional, for_each) carry effect lists in their arguments, so anything
// asking a question about "every effect on this card" has to descend into them. Two callers
// need that walk and must agree: CardValidator rejects a second chosen_* target hidden in a
// conditional's else branch, and ActionGenerator expands legal actions from the chosen_*
// selector it finds. If those two disagreed, a card could validate as single-target and then
// generate the wrong actions -- or none at all, making the card silently unplayable.
//
// Descent is keyed off argument SHAPE rather than a list of control-flow op names: any argument
// holding a nested node or node list is descended into, so a future control-flow op is covered
// without editing this.
public static class EffectTree
{
    // Branches holding effect lists. "condition" is deliberately absent -- a condition holds a
    // predicate, a separate vocabulary from effect ops, so validating it against the effect
    // registry would reject every valid conditional.
    private static readonly string[] EffectKeys = ["effects", "then", "else"];

    // The chosen-selector walk descends into conditions too. No predicate takes a target today,
    // so this changes nothing yet -- but a future predicate that did would otherwise smuggle a
    // second player choice past the single-target rule.
    private static readonly string[] AllKeys = ["effects", "then", "else", "condition"];

    // Every effect in the tree rooted at `node`, including `node` itself, excluding conditions.
    public static IEnumerable<EffectNode> Walk(EffectNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return WalkUnder(node, EffectKeys);
    }

    public static IEnumerable<EffectNode> WalkAll(IReadOnlyList<EffectNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes.SelectMany(Walk);
    }

    // As Walk, but descending into conditions as well. This is the walk the single-target rule
    // uses -- it must see every place a target could hide.
    public static IEnumerable<EffectNode> WalkIncludingConditions(EffectNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return WalkUnder(node, AllKeys);
    }

    // The one chosen_* selector declared anywhere in these effect trees, or null if there is
    // none. Returns the FIRST found, which is unambiguous because CardValidator has already
    // rejected any card declaring more than one distinct chosen selector -- that guarantee is
    // what lets an action carry a single nullable ChosenTarget instead of a list.
    public static TargetSelector? FindChosenSelector(IReadOnlyList<EffectNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var node in nodes.SelectMany(WalkIncludingConditions))
        {
            if (!node.Args.Has("target"))
            {
                continue;
            }

            var selector = EffectArgs.ParseSelector(node.Args.String("target"));
            if (selector.IsChosen())
            {
                return selector;
            }
        }

        return null;
    }

    private static IEnumerable<EffectNode> WalkUnder(EffectNode node, string[] keys)
    {
        yield return node;

        foreach (var key in keys)
        {
            foreach (var child in node.Args.NodesOrSingle(key))
            {
                foreach (var descendant in WalkUnder(child, keys))
                {
                    yield return descendant;
                }
            }
        }
    }
}
