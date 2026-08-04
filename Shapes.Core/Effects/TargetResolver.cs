using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects;

// Resolves a TargetSelector to concrete board slots.
//
// Used two places: legal-action generation (step 1.8) enumerates ChosenEnemyCandidates /
// ChosenFriendlyCandidates to expand one action per valid target, and the interpreter calls
// Resolve to get the slots an effect actually applies to once a choice (if any) has been made.
public static class TargetResolver
{
    // All slots this selector applies to, given the already-resolved chosen target (if the
    // selector needs one). Empty when a required chosen target is missing, or when an
    // automatic selector currently has no valid slot (e.g. Opposing with an empty facing
    // slot, LeftFriendly from slot 0).
    public static IReadOnlyList<SlotIndex> Resolve(EffectContext ctx, TargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        return selector switch
        {
            TargetSelector.Self => ctx.SourceSlot is { } self ? [self] : [],
            TargetSelector.Opposing => ResolveOpposing(ctx),
            TargetSelector.LeftFriendly => ResolveLeftFriendly(ctx),
            TargetSelector.RightFriendly => ResolveRightFriendly(ctx),
            TargetSelector.AllEnemies => ctx.State.Board.CreaturesOf(ctx.ControllingPlayer.Opponent())
                .Select(c => c.Slot).ToList(),
            TargetSelector.AllFriendlies => ctx.State.Board.CreaturesOf(ctx.ControllingPlayer)
                .Select(c => c.Slot).ToList(),
            TargetSelector.ChosenEnemy or TargetSelector.ChosenFriendly =>
                ctx.ChosenTarget is { } chosen ? [chosen] : [],
            _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown target selector."),
        };
    }

    private static IReadOnlyList<SlotIndex> ResolveOpposing(EffectContext ctx)
    {
        if (ctx.SourceSlot is not { } self)
        {
            return [];
        }

        var opposing = self.Opposing();
        return ctx.State.Board.IsOccupied(opposing) ? [opposing] : [];
    }

    private static IReadOnlyList<SlotIndex> ResolveLeftFriendly(EffectContext ctx)
    {
        if (ctx.SourceSlot is not { } self || self.Slot == 0)
        {
            return [];
        }

        var left = new SlotIndex(self.Owner, self.Slot - 1);
        return ctx.State.Board.IsOccupied(left) ? [left] : [];
    }

    private static IReadOnlyList<SlotIndex> ResolveRightFriendly(EffectContext ctx)
    {
        if (ctx.SourceSlot is not { } self || self.Slot == SlotIndex.SlotsPerPlayer - 1)
        {
            return [];
        }

        var right = new SlotIndex(self.Owner, self.Slot + 1);
        return ctx.State.Board.IsOccupied(right) ? [right] : [];
    }

    // Legal candidates for a chosen_* selector -- what legal-action generation expands into
    // one action per element. Applies the taunt restriction: if the effect has a creature
    // source (a move, not a spell) and any enemy creature holds taunt, chosen_enemy is
    // restricted to taunted creatures only.
    public static IReadOnlyList<SlotIndex> ChosenCandidates(EffectContext ctx, TargetSelector selector)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        return selector switch
        {
            TargetSelector.ChosenEnemy => ChosenEnemyCandidates(ctx),
            TargetSelector.ChosenFriendly => ctx.State.Board.CreaturesOf(ctx.ControllingPlayer)
                .Select(c => c.Slot).ToList(),
            _ => throw new ArgumentException($"{selector} is not a chosen_* selector.", nameof(selector)),
        };
    }

    private static IReadOnlyList<SlotIndex> ChosenEnemyCandidates(EffectContext ctx)
    {
        var enemies = ctx.State.Board.CreaturesOf(ctx.ControllingPlayer.Opponent()).ToList();

        if (ctx.HasCreatureSource)
        {
            var taunting = enemies.Where(c => c.Creature.HasKeyword(KeywordFlags.Taunt)).ToList();
            if (taunting.Count > 0)
            {
                return taunting.Select(c => c.Slot).ToList();
            }
        }

        return enemies.Select(c => c.Slot).ToList();
    }
}
