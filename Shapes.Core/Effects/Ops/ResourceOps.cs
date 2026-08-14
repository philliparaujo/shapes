using Shapes.Core.Primitives;

namespace Shapes.Core.Effects.Ops;

// { "op": "gain_resource_scaled", "type": "spike", "scale": "health", "multiplier": 1,
//   "divisor": 1 }
//
// Shares DamageScaledOp's scale vocabulary (health/count/hand_size/hand_composition/resource),
// since the "how big a number" question is identical to damage_scaled's -- only what happens
// with the number differs (gain a resource here, deal damage there). "selector_health" is
// meaningless for a resource gain (there is no third creature to point at without a "target"
// this op does not have) and is rejected. T Flare: "gain 1 spike per health" is scale health.
// Rally: "gain 2 spike for each SPIKE card in hand" is scale hand_composition, which reads
// EffectContext.HandComposition[type] -- the same "type" argument this op already takes for
// which resource to gain -- so it counts hand cards whose cost includes THAT type, not hand
// size in general. Faithful to the original card text.
internal sealed class GainResourceScaledOp : EffectOp
{
    public override string Name => "gain_resource_scaled";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var type = GainResourceOp.ParseResourceType(args.String("type"));
        var scale = DamageScaledOp.ParseScale(args.String("scale"));

        if (scale == DamageScale.SelectorHealth)
        {
            throw new ArgumentException("gain_resource_scaled does not support scale 'selector_health'.");
        }

        var multiplier = args.IntOrDefault("multiplier", 1);
        var divisor = args.IntOrDefault("divisor", 1);

        var amount = DamageScaledOp.ComputeBase(ctx, scale, type) * multiplier / divisor;
        ctx.State[ctx.ControllingPlayer].GainResource(type, amount);
    }
}

// { "op": "gain_resource", "type": "spike", "amount": 1 }
internal sealed class GainResourceOp : EffectOp
{
    public override string Name => "gain_resource";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var type = ParseResourceType(args.String("type"));
        var amount = args.Int("amount");
        ctx.State[ctx.ControllingPlayer].GainResource(type, amount);
    }

    internal static ResourceType ParseResourceType(string raw) => raw switch
    {
        "spike" => ResourceType.Spike,
        "anvil" => ResourceType.Anvil,
        "wheel" => ResourceType.Wheel,
        _ => throw new ArgumentException($"Unknown resource type '{raw}'."),
    };
}

// { "op": "spend_all", "type": "spike", "effects": [ ... ] }
//
// Zeroes the controller's holding of `type`, then runs `effects` with the AMOUNT SPENT available
// to them via EffectContext.SpentAmount -- read by the "spent" scale on damage_scaled/draw_scaled
// and friends. Nova Burst: "spend all your spikes, deal that much damage, draw that many cards."
//
// The nested-effects shape is what makes the amount readable at all. A bare "spend all" op
// followed by sibling effects could not work: the scale vocabulary reads CURRENT resources, which
// this op has just set to zero, so every follower would compute 0. Capturing the total and
// scoping the dependent effects underneath keeps the two halves inseparable, the same way
// for_each scopes the effects that depend on its iteration.
internal sealed class SpendAllOp : EffectOp
{
    public override string Name => "spend_all";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var type = GainResourceOp.ParseResourceType(args.String("type"));
        var effects = args.Nodes("effects");

        var player = ctx.State[ctx.ControllingPlayer];
        var spent = player.Resources[type];

        player.Pay(ResourcePool.Of(type, spent));

        EffectInterpreter.ApplyAll(effects, ctx.WithSpentAmount(spent));
    }
}

// { "op": "free_moves", "type": "spike" }
//
// Makes every move of `type` cost nothing for the controller for the rest of this turn (Spike
// Rush and its anvil/wheel counterparts). Expires at end of turn -- see GameState.EndTurn.
//
// A move's type comes from its cost, so this discounts exactly the moves whose cost is paid in
// `type`. It applies to the PLAYER, not to the creatures on the board at the time: a creature
// played later in the same turn is covered too, which is what "all [type] moves are free" says.
internal sealed class FreeMovesOp : EffectOp
{
    public override string Name => "free_moves";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var type = GainResourceOp.ParseResourceType(args.String("type"));
        ctx.State[ctx.ControllingPlayer].GrantFreeMoves(type);
    }
}

// { "op": "gain_next_turn", "type": "spike", "amount": 1 } -- lands on the controller's next
// income phase, not this turn. See PlayerState.PendingNextTurnResources.
internal sealed class GainNextTurnOp : EffectOp
{
    public override string Name => "gain_next_turn";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var type = GainResourceOp.ParseResourceType(args.String("type"));
        var amount = args.Int("amount");
        ctx.State[ctx.ControllingPlayer].AddPendingNextTurnResources(ResourcePool.Of(type, amount));
    }
}
