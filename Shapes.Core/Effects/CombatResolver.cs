using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects;

// Shared damage-application path for `damage` and `damage_scaled` -- both ops compute an
// amount differently but resolve identically once they have one, and that resolution (bonus
// ordering, type effectiveness, reflect, ricochet) is exactly the part that is easy to get
// subtly wrong twice.
internal static class CombatResolver
{
    // Resolves one hit of `baseAmount` from ctx's source onto `targetSlot`.
    //
    // Ordering, pinned by the confirmed ruleset: (base + next_attack_bonus) * typeMultiplier,
    // and next_damage_taken_bonus on the target is folded into the base the same way before
    // the multiplier applies -- both bonuses are flat adjustments to the pre-multiplier
    // amount, not the post-multiplier one.
    public static void DealDamage(EffectContext ctx, SlotIndex targetSlot, int baseAmount)
    {
        var target = ctx.State.Board[targetSlot];
        if (target is null)
        {
            return;
        }

        var attacker = ctx.SourceCreature;

        var amount = baseAmount;
        if (attacker is not null)
        {
            amount += attacker.ConsumeNextAttackBonus();
        }
        amount += target.ConsumeNextDamageTakenBonus();
        amount = Math.Max(0, amount);

        var multiplier = ctx.MoveType is { } attackType
            ? ctx.State.Rules.TypeChart.MultiplierAgainst(attackType, target.Types)
            : 1.0;

        var finalAmount = (int)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);

        ApplyToTarget(ctx, targetSlot, target, finalAmount);
    }

    // Reflect and ricochet redirect where the damage actually lands; both only trigger against
    // an attack from a creature (a move), never a typeless spell.
    private static void ApplyToTarget(EffectContext ctx, SlotIndex targetSlot, CreatureInstance target, int amount)
    {
        if (ctx.HasCreatureSource && target.HasKeyword(KeywordFlags.Ricochet))
        {
            var side = target.RicochetDirection == RicochetDirection.Left ? -1 : 1;
            var neighborSlot = targetSlot.Slot + side;

            if (neighborSlot >= 0 && neighborSlot < SlotIndex.SlotsPerPlayer)
            {
                var neighbor = ctx.State.Board[new SlotIndex(targetSlot.Owner, neighborSlot)];
                if (neighbor is not null)
                {
                    neighbor.TakeDamage(amount);
                    return;
                }
            }
            // No friendly neighbor on that side: ricochet does not trigger, target takes it.
        }

        if (ctx.HasCreatureSource && target.ConsumeReflect())
        {
            ctx.SourceCreature?.TakeDamage(amount);
            return;
        }

        target.TakeDamage(amount);
    }
}
