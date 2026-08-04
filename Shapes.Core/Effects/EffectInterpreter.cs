namespace Shapes.Core.Effects;

// Applies parsed effect trees against a GameState. The only entry point ops and callers
// (UseMove/PlayCard, once step 1.8 exists) need -- everything else in this namespace is
// either state (EffectContext/EffectArgs) or vocabulary (EffectRegistry/Ops).
public static class EffectInterpreter
{
    public static void Apply(EffectNode node, EffectContext ctx)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(ctx);

        EffectRegistry.Resolve(node.Op).Apply(ctx, node.Args);
    }

    // Applies a move/spell's full effect list in declared order. An effect that kills a
    // creature mid-sequence does not corrupt the remaining effects -- later effects simply
    // resolve against a board where that creature (and any slot it occupied) is gone, since
    // every op already reads the board fresh rather than caching a stale reference.
    //
    // Dead creatures are NOT swept off the board between effects: an effect list may
    // deliberately reference a creature at 0 health before the death-cleanup step runs (e.g.
    // a follow-up heal in the same list). RemoveDead() runs once per action, after the whole
    // effect list completes -- that ordering is a step 1.8 concern, not this one.
    public static void ApplyAll(IReadOnlyList<EffectNode> nodes, EffectContext ctx)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(ctx);

        foreach (var node in nodes)
        {
            Apply(node, ctx);
        }
    }
}
