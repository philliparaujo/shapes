using Shapes.Core.Primitives;

namespace Shapes.Core.Effects.Ops;

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
