namespace Shapes.Core.Effects.Ops;

// { "op": "heal", "target": "self", "amount": 1 }
internal sealed class HealOp : EffectOp
{
    public override string Name => "heal";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.Heal(amount);
        }
    }
}

// { "op": "heal_to_full", "target": "self" }
internal sealed class HealToFullOp : EffectOp
{
    public override string Name => "heal_to_full";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.HealToFull();
        }
    }
}

// { "op": "set_health", "target": "self", "amount": 1 }
//
// Sets current health directly, clamped to [1, MaxHealth] via Heal/TakeDamage rather than a
// separate setter -- a set_health effect that would reduce a creature to 0 or below should
// kill it exactly like damage does, not leave it in an invalid state.
internal sealed class SetHealthOp : EffectOp
{
    public override string Name => "set_health";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            var creature = ctx.State.Board[slot];
            if (creature is null)
            {
                continue;
            }

            var delta = amount - creature.Health;
            if (delta > 0)
            {
                creature.Heal(delta);
            }
            else if (delta < 0)
            {
                creature.TakeDamage(-delta);
            }
        }
    }
}

// { "op": "buff_max_health", "target": "self", "amount": 1 }
internal sealed class BuffMaxHealthOp : EffectOp
{
    public override string Name => "buff_max_health";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.BuffMaxHealth(amount);
        }
    }
}

// { "op": "self_damage", "amount": 1 } -- always targets the source creature, no "target" key.
// Bypasses type effectiveness and reflect/ricochet entirely: this is the creature paying its
// own cost, not an attack.
internal sealed class SelfDamageOp : EffectOp
{
    public override string Name => "self_damage";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        ctx.SourceCreature?.TakeDamage(amount);
    }
}
