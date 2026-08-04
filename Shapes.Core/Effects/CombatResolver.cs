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
    // Ordering, pinned by the confirmed ruleset: (base + next_attack_bonus + attack_buff) *
    // typeMultiplier, and next_damage_taken_bonus on the target is folded into the base the
    // same way before the multiplier applies -- all three are flat adjustments to the
    // pre-multiplier amount, not the post-multiplier one. attack_buff is read, not consumed:
    // unlike next_attack_bonus it is persistent and applies to every future hit.
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
            amount += attacker.AttackBuff;
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
    // an attack from a creature (a move), never a spell -- gated on HasCreatureSource, which is
    // independent of whether the attack has a type (a spell can be typed; it still has no
    // creature to redirect the hit back onto).
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
                    // The ricochet trigger fires on the REDIRECTING creature (target), not the
                    // neighbor who actually takes the damage -- Circle Bender is rewarded for
                    // its own attack being deflected, regardless of what that damage then hits.
                    FirePendingOnNextRicochet(ctx, targetSlot, target);
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
        FirePendingOnNextDamageTaken(ctx, targetSlot, target);
    }

    // Fires and clears whatever `on_next_damage_taken` armed on `target`, if anything -- with
    // "self" rebound to the creature that just took the hit, so the trigger's own effect (a
    // resource gain or draw) resolves as that creature's action, not the original attacker's.
    // Runs even on a lethal hit: the trigger is a punish-the-attacker mechanic, not a survival
    // bonus, so a creature that dies from the blow still fires it.
    private static void FirePendingOnNextDamageTaken(EffectContext ctx, SlotIndex targetSlot, CreatureInstance target)
    {
        if (target.ConsumePendingOnNextDamageTaken() is EffectNode pending)
        {
            EffectInterpreter.Apply(pending, ctx.WithSelfAsController(targetSlot));
        }
    }

    private static void FirePendingOnNextRicochet(EffectContext ctx, SlotIndex targetSlot, CreatureInstance target)
    {
        if (target.ConsumePendingOnNextRicochet() is EffectNode pending)
        {
            EffectInterpreter.Apply(pending, ctx.WithSelfAsController(targetSlot));
        }
    }
}
