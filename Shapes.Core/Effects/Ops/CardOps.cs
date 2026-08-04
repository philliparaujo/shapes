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

        ctx.State[ctx.ControllingPlayer].Draw(destroyed * multiplier);
    }
}

// { "op": "draw", "amount": 1 } -- always the controlling player's own deck/hand.
internal sealed class DrawOp : EffectOp
{
    public override string Name => "draw";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        ctx.State[ctx.ControllingPlayer].Draw(amount);
    }
}

// { "op": "discard", "amount": 1 } -- discards from the front of hand. Card-specific discard
// (a chosen card) is not in the vocabulary; nothing referenced it.
internal sealed class DiscardOp : EffectOp
{
    public override string Name => "discard";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        var player = ctx.State[ctx.ControllingPlayer];

        for (var i = 0; i < amount && player.Hand.Count > 0; i++)
        {
            player.DiscardCardAt(0);
        }
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
            player.Draw(needed);
        }
    }
}
