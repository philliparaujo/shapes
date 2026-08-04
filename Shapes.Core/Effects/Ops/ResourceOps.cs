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
