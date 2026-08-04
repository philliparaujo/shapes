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
