using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects;

// Resolves a TargetSelector to concrete board slots.
//
// Used two places: legal-action generation (step 1.8) enumerates ChosenEnemyCandidates /
// ChosenFriendlyCandidates to expand one action per valid target, and the interpreter calls
// Resolve to get the slots an effect actually applies to once a choice (if any) has been made.
//
// TAUNT lives here, in both halves: it redirects an `opposing` attack (ResolveOpposing) and it
// restricts a `chosen_enemy` pick (ChosenEnemyCandidates). Both are needed -- the first is how
// nearly every attacking card in the set targets, the second is the only one a player picks --
// and keeping them in one file is what stops the two readings of "attacks must target the
// taunter" from drifting apart.
public static class TargetResolver
{
    // All slots this selector applies to, given the already-resolved chosen target (if the
    // selector needs one). Empty when a required chosen target is missing, or when an
    // automatic selector currently has no valid slot (e.g. Opposing with an empty facing
    // slot, LeftFriendly from slot 0).
    public static IReadOnlyList<SlotIndex> Resolve(EffectContext ctx, TargetSelector selector)
    {
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
            // Deliberately symmetric: hits the controller's own board too. Board.AllCreatures
            // walks PlayerIds.All in slot order, so the resolution order is deterministic and
            // seeded replays stay byte-identical -- an unordered set here would desync search.
            TargetSelector.AllCreatures => ctx.State.Board.AllCreatures()
                .Select(c => c.Slot).ToList(),
            TargetSelector.ChosenEnemy or TargetSelector.ChosenFriendly =>
                ctx.ChosenTarget is { } chosen ? [chosen] : [],
            _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown target selector."),
        };
    }

    // "All enemy creature attacks target this creature" -- so a taunting enemy REDIRECTS an
    // `opposing` attack away from the facing slot, it does not merely restrict a player's choice.
    //
    // This is where taunt does most of its work: 32 of the card set's 48 cards attack via
    // `opposing` and only 3 via `chosen_enemy`, so enforcing taunt solely in ChosenEnemyCandidates
    // (as this once did) left the keyword doing nothing on almost every attack in the game.
    //
    // Restricted to attacks with a CREATURE source, matching chosen_enemy's rule and reflect's and
    // ricochet's: taunt answers being attacked by a creature, and a spell is not that.
    private static IReadOnlyList<SlotIndex> ResolveOpposing(EffectContext ctx)
    {
        if (ctx.SourceSlot is not { } self)
        {
            return [];
        }

        if (ctx.HasCreatureSource && TauntingEnemy(ctx) is { } taunted)
        {
            return [taunted];
        }

        var opposing = self.Opposing();
        return ctx.State.Board.IsOccupied(opposing) ? [opposing] : [];
    }

    // The enemy creature a taunt redirects to, or null when none is taunting.
    //
    // With several taunting at once the FACING one wins if it is among them, else the
    // lowest-slot taunter. A deterministic rule rather than a player choice: an `opposing` move
    // carries no target prompt, and turning one into a choice mid-attack would change the action
    // shape (and the search's branching) for every attacking card in the set. Lowest-slot is
    // arbitrary but stable, which is what seeded replays need.
    private static SlotIndex? TauntingEnemy(EffectContext ctx)
    {
        var taunting = ctx.State.Board.CreaturesOf(ctx.ControllingPlayer.Opponent())
            .Where(c => c.Creature.HasKeyword(KeywordFlags.Taunt))
            .Select(c => c.Slot)
            .ToList();

        if (taunting.Count == 0)
        {
            return null;
        }

        var facing = ctx.SourceSlot!.Value.Opposing();
        return taunting.Contains(facing) ? facing : taunting[0];
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
