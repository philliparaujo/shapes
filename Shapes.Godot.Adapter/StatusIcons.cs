using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// PLAN.md B1b: a compact, always-visible readout of a creature's status/keyword state, so a
// player reads taunt/reflect/ricochet/stun/buffs at a glance instead of needing to tap or hover.
// One badge per active status; SlotView renders these as a single wrapped icon row under health.
// IsExpiring marks the "gone at the start of your next turn" case (only reachable today via
// taunt-until-next-turn) so the caller can dim/clock-badge it -- same glyph either way, since the
// keyword itself is identical and only its durability differs. IsText marks a badge whose Glyph
// is itself the readable content (the "+N atk" buff) rather than a symbol standing in for a
// tooltip -- SlotView styles that one apart from the emoji/arrow glyphs (and from health) instead
// of running every badge through identical formatting.
public sealed record StatusBadge(string Glyph, string Tooltip, bool IsExpiring = false, bool IsText = false);

public static class StatusIcons
{
    // Dimmed (not a different glyph) distinguishes "expires at the start of your next turn" from
    // permanent taunt -- the underlying keyword is the same, only its durability differs, and the
    // caller (SlotView) is the one with a Modulate-style dimming mechanism already, so this just
    // flags it rather than encoding two glyphs for one keyword.
    public const string TauntGlyph = "🛡";
    public const string ReflectGlyph = "🪞";
    public const string RicochetLeftGlyph = "↩";
    public const string RicochetRightGlyph = "↪";
    public const string StunGlyph = "⚡";
    public const string NextAttackBonusGlyph = "⚔";
    public const string NextDamageTakenBonusGlyph = "🎯";
    public const string PendingTriggerGlyph = "⏳";

    public static IReadOnlyList<StatusBadge> Describe(CreatureInstance creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var badges = new List<StatusBadge>();

        if (creature.HasKeyword(KeywordFlags.Taunt))
        {
            var expiring = creature.TauntExpiresNextTurn;
            badges.Add(new StatusBadge(
                TauntGlyph, expiring ? "Taunt (until your next turn)" : "Taunt", expiring));
        }

        if (creature.HasKeyword(KeywordFlags.Reflect))
        {
            badges.Add(new StatusBadge(ReflectGlyph, "Reflect"));
        }

        if (creature.HasKeyword(KeywordFlags.Ricochet))
        {
            var (glyph, side) = creature.RicochetDirection == RicochetDirection.Left
                ? (RicochetLeftGlyph, "left")
                : (RicochetRightGlyph, "right");
            badges.Add(new StatusBadge(glyph, $"Ricochet ({side})"));
        }

        if (creature.IsStunned)
        {
            badges.Add(new StatusBadge(StunGlyph, "Stunned"));
        }

        if (creature.AttackBuff != 0)
        {
            badges.Add(new StatusBadge(
                $"+{creature.AttackBuff} atk", "Attack buff (persistent)", IsText: true));
        }

        if (creature.NextAttackBonus != 0)
        {
            badges.Add(new StatusBadge(NextAttackBonusGlyph, $"Next attack {(creature.NextAttackBonus > 0 ? "+" : string.Empty)}{creature.NextAttackBonus}"));
        }

        if (creature.NextDamageTakenBonus != 0)
        {
            badges.Add(new StatusBadge(NextDamageTakenBonusGlyph, $"Next damage taken {(creature.NextDamageTakenBonus > 0 ? "+" : string.Empty)}{creature.NextDamageTakenBonus}"));
        }

        // Reactive triggers (on_next_damage_taken / on_next_ricochet) arm an arbitrary nested
        // effect rather than a flat number, so there's no fixed phrase to hand-author -- reusing
        // EffectText.Describe on the single stored node is the same synthesis-not-authoring
        // rule CardText/MoveText already follow, so a balance edit to the armed effect can never
        // leave a stale tooltip behind. State typed these `object?` specifically so State needn't
        // reference Effects; the adapter is outside that boundary, so it can cast back safely.
        // The armed node is wrapped back into the op that armed it rather than described bare and
        // hand-prefixed: EffectText already words both triggers ("Gain 3 [anvil] next time this
        // takes damage"), so prefixing here would state the trigger twice, in two different
        // phrasings, once the synthesizer's own wording changed.
        if (creature.PendingOnNextDamageTaken is EffectNode onDamageTaken)
        {
            badges.Add(new StatusBadge(PendingTriggerGlyph, Trigger("on_next_damage_taken", onDamageTaken)));
        }

        if (creature.PendingOnNextRicochet is EffectNode onRicochet)
        {
            badges.Add(new StatusBadge(PendingTriggerGlyph, Trigger("on_next_ricochet", onRicochet)));
        }

        return badges;
    }

    // Targets "self" because a pending trigger always belongs to the creature holding it, which
    // is what makes the synthesized text read as "... next time this takes damage".
    private static string Trigger(string op, EffectNode effect) =>
        EffectText.Describe(
        [
            new EffectNode(op, new EffectArgs(new Dictionary<string, object?>
            {
                ["target"] = "self",
                ["effect"] = effect,
            })),
        ],
            CardTextFormat.Resource);
}
