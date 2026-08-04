using Shapes.Core.Primitives;

namespace Shapes.Core.Effects.Ops;

// { "op": "damage", "target": "opposing", "amount": 1 }
internal sealed class DamageOp : EffectOp
{
    public override string Name => "damage";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            CombatResolver.DealDamage(ctx, slot, amount);
        }
    }
}

// How damage_scaled computes its base amount before the shared resolution path.
internal enum DamageScale
{
    Health,
    Count,
    HandSize,
}

// { "op": "damage_scaled", "target": "opposing", "scale": "health", "multiplier": 1 }
//
// "health": base = source creature's current health * multiplier.
// "count": base = number of friendly creatures * multiplier.
// "hand_size": base = controller's hand size * multiplier.
internal sealed class DamageScaledOp : EffectOp
{
    public override string Name => "damage_scaled";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var scale = ParseScale(args.String("scale"));
        var multiplier = args.IntOrDefault("multiplier", 1);
        var amount = ComputeBase(ctx, scale) * multiplier;

        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            CombatResolver.DealDamage(ctx, slot, amount);
        }
    }

    private static int ComputeBase(EffectContext ctx, DamageScale scale) => scale switch
    {
        DamageScale.Health => ctx.SourceCreature?.Health ?? 0,
        DamageScale.Count => ctx.State.Board.CountCreatures(ctx.ControllingPlayer),
        DamageScale.HandSize => ctx.State[ctx.ControllingPlayer].Hand.Count,
        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown damage scale."),
    };

    private static DamageScale ParseScale(string raw) => raw switch
    {
        "health" => DamageScale.Health,
        "count" => DamageScale.Count,
        "hand_size" => DamageScale.HandSize,
        _ => throw new ArgumentException($"Unknown damage_scaled scale '{raw}'."),
    };
}
