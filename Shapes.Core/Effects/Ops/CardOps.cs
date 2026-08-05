using Shapes.Core.State;

namespace Shapes.Core.Effects.Ops;

// { "op": "draw_scaled", "scale": "destroyed_this_turn", "multiplier": 1 }
//
// The one scale outside DamageScaledOp's shared vocabulary: how many creatures were destroyed
// THIS TURN (either side, any cause -- combat death or a direct destroy effect), read from
// GameState.TurnEvents. Gravewarden: "draw 1 for each creature destroyed this turn". Kept
// separate from DamageScale rather than folded in, since nothing else in the vocabulary counts
// turn events and damage_scaled/gain_resource_scaled/heal_scaled have no use for it.
internal sealed class DrawScaledOp : EffectOp
{
    public override string Name => "draw_scaled";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var scale = args.String("scale");
        if (scale != "destroyed_this_turn")
        {
            throw new ArgumentException($"Unknown draw_scaled scale '{scale}'.");
        }

        var multiplier = args.IntOrDefault("multiplier", 1);
        var destroyed = ctx.State.TurnEvents.Count(e => e.Kind == TurnEventKind.CreatureDestroyed);

        ctx.State.DrawWithBurn(ctx.ControllingPlayer, destroyed * multiplier);
    }
}

// { "op": "draw", "amount": 1 } -- always the controlling player's own deck/hand.
//
// Goes through GameState.DrawWithBurn, so a card drawn into a full hand burns exactly as the
// turn-start draw does. The hand limit is a property of drawing, not of the turn step.
internal sealed class DrawOp : EffectOp
{
    public override string Name => "draw";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        ctx.State.DrawWithBurn(ctx.ControllingPlayer, args.Int("amount"));
    }
}

// { "op": "discard", "amount": 1 } -- the controlling player chooses which cards to discard.
//
// Records a DEBT rather than discarding anything itself. An effect cannot stop mid-resolution to
// ask the player a question -- every choice in this engine is a distinct legal action -- so the
// op increments GameState.PendingDiscards, resolution finishes normally, and the action generator
// then offers a DiscardAction per card in hand until the debt clears. The console renders those
// as an ordinary numbered menu and the search expands them as ordinary edges; neither needs to
// know a discard is special.
//
// The debt is clamped to hand size by ActionExecutor once the whole effect list has resolved, so
// "discard 2" with one card in hand costs one card rather than deadlocking a generator that
// offers nothing but discards. Clamping is deliberately NOT done here: a later effect in the same
// list may draw, and the player should have to pay a debt they can afford by then.
internal sealed class DiscardOp : EffectOp
{
    public override string Name => "discard";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        ctx.State.AddPendingDiscards(args.Int("amount"));
    }
}

// { "op": "draw_up_to", "amount": 5 } -- draws until hand size reaches `amount`. A no-op, not
// negative, when already at or above the target.
internal sealed class DrawUpToOp : EffectOp
{
    public override string Name => "draw_up_to";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var target = args.Int("amount");
        var player = ctx.State[ctx.ControllingPlayer];

        var needed = target - player.Hand.Count;
        if (needed > 0)
        {
            // Same burn path as every other draw. It can only actually burn if a card names a
            // target above the hand limit, which no shipped card does -- routed through anyway
            // so the rule holds for whatever Phase 4 balance work adds.
            ctx.State.DrawWithBurn(ctx.ControllingPlayer, needed);
        }
    }
}
