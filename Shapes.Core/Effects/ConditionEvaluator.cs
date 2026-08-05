using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects;

// Evaluates the predicate vocabulary -- a separate, much smaller language from the effect ops.
//
// Two callers, and the distinction between them is a real rules decision:
//
//   - `conditional` branches on a predicate mid-effect-list. Both branches are legal; which one
//     runs is decided at resolution.
//   - a MOVE's `condition` gates the move entirely. An unmet condition means the move is not a
//     legal action at all, rather than a legal action that resolves to nothing (see
//     MoveDefinition.Condition). ActionGenerator calls this, which is why it is public here
//     rather than internal to the control-flow op.
//
// That difference matters to the AI: a move that cannot do anything must not appear in the
// legal list, or the search wastes iterations on edges that change nothing, and the console
// offers the player a move that visibly does nothing when chosen.
//
// A single generic predicate, `creature_state`, rather than one bespoke name per card need
// (self_at_full_health, target_damaged, self_unopposed, ...): every predicate step 1.10's card
// set needs is "is the creature at THIS target in THIS state", so the target selector and the
// state check are both arguments rather than baked into the op name. This reuses the same
// small state vocabulary ForEachOp's "filter" already has (damaged/full_health/type:<x>),
// extended with "unopposed" and "health_at_most:<n>". Unknown checks and targets that resolve
// to no creature both throw/fail rather than silently defaulting to false: a typo that made
// every gated move illegal would otherwise be near-invisible.
public static class ConditionEvaluator
{
    public static bool Evaluate(EffectContext ctx, EffectNode condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (condition.Op != "creature_state")
        {
            throw new ArgumentException($"Unknown condition predicate '{condition.Op}'.");
        }

        var selector = condition.Args.Target();
        var check = condition.Args.String("check");

        // "unopposed" asks about the FACING slot of the source creature, not any target
        // creature's own fields -- "no one is across from THIS" only makes sense for self, so
        // it is the one check that does not resolve `selector` to a creature at all.
        if (check == "unopposed")
        {
            if (selector != TargetSelector.Self)
            {
                throw new ArgumentException("creature_state check 'unopposed' only supports target 'self'.");
            }

            return TargetResolver.Resolve(ctx, TargetSelector.Opposing).Count == 0;
        }

        var slots = TargetResolver.Resolve(ctx, selector);
        if (slots.Count != 1)
        {
            // No creature at that target (an empty opposing slot, a left_friendly from slot 0,
            // etc.) -- the state being checked cannot hold of a creature that is not there.
            return false;
        }

        var creature = ctx.State.Board[slots[0]]
            ?? throw new InvalidOperationException(
                $"creature_state target '{selector}' resolved to an empty slot.");

        return MatchesCheck(creature, check);
    }

    private static bool MatchesCheck(CreatureInstance creature, string check) => check switch
    {
        "damaged" => creature.IsDamaged,
        "full_health" => !creature.IsDamaged,
        _ when check.StartsWith("health_at_most:", StringComparison.Ordinal) =>
            creature.Health <= int.Parse(check["health_at_most:".Length..]),
        _ => throw new ArgumentException($"Unknown creature_state check '{check}'."),
    };
}
