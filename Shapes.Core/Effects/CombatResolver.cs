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

    // Ricochet redirects where the damage lands; reflect cancels it entirely. Both only trigger
    // against an attack from a creature (a move), never a spell -- gated on HasCreatureSource,
    // which is independent of whether the attack has a type (a spell can be typed; it is still
    // not a creature's attack, and neither keyword answers it).
    //
    // Ricochet is checked first, so a creature holding both redirects the hit to a neighbor and
    // keeps its reflect charge armed for the next attack, rather than spending both on one hit.
    private static void ApplyToTarget(EffectContext ctx, SlotIndex targetSlot, CreatureInstance target, int amount)
    {
        if (ctx.HasCreatureSource && target.HasKeyword(KeywordFlags.Ricochet))
        {
            // Left before right when both sides are armed (Snowball's Carom). A fixed
            // order rather than a choice: the defender does not pick where their own creature
            // deflects to, and a player-facing decision here would be a new prompt in the middle
            // of someone else's attack.
            if (TryFindRicochetNeighbor(ctx, targetSlot, target, out var neighbor, out var side))
            {
                // Only the side that actually redirected is spent, mirroring reflect one charge at
                // a time: a creature armed on both sides deflects twice, once per side, and a
                // grant that cannot redirect (no neighbor there) stays armed rather than being
                // wasted. The keyword itself clears once no side is left.
                target.ConsumeRicochet(side);

                // The ricochet trigger fires on the REDIRECTING creature (target), not the
                // neighbor who actually takes the damage -- Circle Bender is rewarded for
                // its own attack being deflected, regardless of what that damage then hits.
                FirePendingOnNextRicochet(ctx, targetSlot, target);
                neighbor.TakeDamage(amount);
                return;
            }
            // No friendly neighbor on any armed side: ricochet does not trigger, target takes it.
        }

        // Reflect negates the hit outright: the defender takes nothing and the attacker takes
        // nothing back. Nobody is damaged, so no on_next_damage_taken trigger fires either --
        // that trigger is armed against damage actually landing, and here none did.
        if (ctx.HasCreatureSource && target.ConsumeReflect())
        {
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

    // The first friendly neighbor on an armed ricochet side, left before right, along with WHICH
    // side that was -- the caller spends exactly that side, so an unused one survives for a later
    // attack. Returns false when no armed side has a neighbor, and the caller then lets the hit
    // land normally, leaving every side armed for an attack that can actually be deflected.
    private static bool TryFindRicochetNeighbor(
        EffectContext ctx, SlotIndex targetSlot, CreatureInstance target,
        out CreatureInstance neighbor, out RicochetDirection side)
    {
        foreach (var (candidate, offset) in RicochetSides)
        {
            if (!target.RicochetDirection.HasFlag(candidate))
            {
                continue;
            }

            var neighborSlot = targetSlot.Slot + offset;
            if (neighborSlot < 0 || neighborSlot >= SlotIndex.SlotsPerPlayer)
            {
                continue;
            }

            if (ctx.State.Board[new SlotIndex(targetSlot.Owner, neighborSlot)] is { } found)
            {
                neighbor = found;
                side = candidate;
                return true;
            }
        }

        neighbor = null!;
        side = RicochetDirection.None;
        return false;
    }

    private static readonly (RicochetDirection Side, int Offset)[] RicochetSides =
        [(RicochetDirection.Left, -1), (RicochetDirection.Right, 1)];

    private static void FirePendingOnNextRicochet(EffectContext ctx, SlotIndex targetSlot, CreatureInstance target)
    {
        if (target.ConsumePendingOnNextRicochet() is EffectNode pending)
        {
            EffectInterpreter.Apply(pending, ctx.WithSelfAsController(targetSlot));
        }
    }
}
