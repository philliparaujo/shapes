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
// One predicate exists so far -- the plan's own card example uses just this one -- so this is a
// single-case switch rather than a general boolean-expression language. It grows when a real
// card (step 1.10) needs a second predicate. Unknown predicates throw rather than defaulting to
// false: a typo that silently made every gated move illegal would be near-invisible.
public static class ConditionEvaluator
{
    public static bool Evaluate(EffectContext ctx, EffectNode condition)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(condition);

        return condition.Op switch
        {
            "self_at_full_health" => ctx.SourceCreature is { IsDamaged: false },
            _ => throw new ArgumentException($"Unknown condition predicate '{condition.Op}'."),
        };
    }
}
