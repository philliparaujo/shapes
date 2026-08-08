using Shapes.Core.Primitives;

namespace Shapes.Core.Effects.Ops;

// { "op": "attack_buff", "target": "self", "amount": 2 } -- persistent and cumulative: unlike
// next_attack_bonus this never clears itself, so it applies to every future hit the creature
// deals (repeated grants stack). Basic Circle: "increase all damage this does by 2." See
// CombatResolver.DealDamage for where this and next_attack_bonus both fold into a hit.
internal sealed class AttackBuffOp : EffectOp
{
    public override string Name => "attack_buff";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.AddAttackBuff(amount);
        }
    }
}

// { "op": "attack_buff_scaled", "target": "self", "scale": "missing_health", "multiplier": 1,
//   "divisor": 1 }
//
// The scaled counterpart of attack_buff, sharing DamageScaledOp's scale vocabulary (see
// DamageOps.cs) rather than defining its own -- "missing_health" (Rally: "+attack equal to the
// damage this creature has taken") is the one this was added for, but every scale works.
//
// The amount is computed ONCE, before the target loop, not per target: the scales read the
// source creature and the controller's own state, so recomputing per target would give the same
// answer at extra cost -- and, for a multi-target selector, would silently diverge if an earlier
// iteration killed something the scale counts. Same reason `divisor` applies after the
// multiplier via integer division: a floor, not a fraction.
internal sealed class AttackBuffScaledOp : EffectOp
{
    public override string Name => "attack_buff_scaled";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var scale = DamageScaledOp.ParseScale(args.String("scale"));
        var multiplier = args.IntOrDefault("multiplier", 1);
        var divisor = args.IntOrDefault("divisor", 1);
        var resourceType = args.Has("resource_type")
            ? GainResourceOp.ParseResourceType(args.String("resource_type"))
            : (ResourceType?)null;
        var healthSource = args.Has("health_source")
            ? args.Target("health_source")
            : (TargetSelector?)null;

        var amount = DamageScaledOp.ComputeBase(ctx, scale, resourceType, healthSource)
            * multiplier / divisor;

        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.AddAttackBuff(amount);
        }
    }
}

// { "op": "next_attack_bonus", "target": "self", "amount": 1 } -- applies once, to the next
// damage this creature deals, then clears. See CombatResolver.DealDamage.
internal sealed class NextAttackBonusOp : EffectOp
{
    public override string Name => "next_attack_bonus";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.SetNextAttackBonus(amount);
        }
    }
}

// { "op": "next_damage_taken_bonus", "target": "opposing", "amount": 1 } -- applies once, to
// the next damage this creature takes, then clears.
internal sealed class NextDamageTakenBonusOp : EffectOp
{
    public override string Name => "next_damage_taken_bonus";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.SetNextDamageTakenBonus(amount);
        }
    }
}
