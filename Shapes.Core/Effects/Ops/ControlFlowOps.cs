using Shapes.Core.Primitives;

namespace Shapes.Core.Effects.Ops;

// { "op": "conditional", "condition": { "predicate": "self_at_full_health" },
//   "then": [ ... ], "else": [ ... ] }
//
// "else" is optional; an unmet condition with no "else" is a no-op. Only one predicate exists
// so far -- the plan's own card example uses just this one -- so ConditionEvaluator is a
// single-case switch rather than a general boolean-expression language, extended when a real
// card (step 1.10) needs a second predicate.
internal sealed class ConditionalOp : EffectOp
{
    public override string Name => "conditional";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var condition = args.Node("condition");
        var branch = ConditionEvaluator.Evaluate(ctx, condition) ? "then" : "else";

        if (args.Has(branch))
        {
            EffectInterpreter.ApplyAll(args.Nodes(branch), ctx);
        }
    }
}

internal static class ConditionEvaluator
{
    public static bool Evaluate(EffectContext ctx, EffectNode condition) => condition.Op switch
    {
        "self_at_full_health" => ctx.SourceCreature is { IsDamaged: false },
        _ => throw new ArgumentException($"Unknown condition predicate '{condition.Op}'."),
    };
}

// { "op": "for_each", "collection": "friendly_creatures", "effects": [ ... ] }
//
// Collection is one of: friendly_creatures, enemy_creatures, all_creatures, hand.
// An optional "filter" narrows a creature collection: "damaged" or "full_health", or
// "type:<spike|anvil|wheel>". Iterating an empty collection is a no-op. Each iteration re-runs
// the nested effects with a context whose "self" is rebound to the iterated creature's slot --
// this is what lets "heal self 1" inside a for_each mean "heal the creature just iterated",
// not the original move's source.
internal sealed class ForEachOp : EffectOp
{
    public override string Name => "for_each";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var collection = args.String("collection");
        var filter = args.StringOrDefault("filter", string.Empty);
        var effects = args.Nodes("effects");

        if (collection == "hand")
        {
            // Hand cards are not board slots, so nested effects run with the original
            // context unchanged -- for_each over hand exists for counting-style effects
            // (e.g. draw scaled by hand size is damage_scaled, not this), so as of step 1.6
            // no hand-specific per-card rebinding is needed.
            foreach (var _ in ctx.State[ctx.ControllingPlayer].Hand)
            {
                EffectInterpreter.ApplyAll(effects, ctx);
            }
            return;
        }

        foreach (var slot in ResolveCreatures(ctx, collection, filter))
        {
            EffectInterpreter.ApplyAll(effects, ctx.WithSelf(slot));
        }
    }

    private static IEnumerable<SlotIndex> ResolveCreatures(EffectContext ctx, string collection, string filter)
    {
        var creatures = collection switch
        {
            "friendly_creatures" => ctx.State.Board.CreaturesOf(ctx.ControllingPlayer),
            "enemy_creatures" => ctx.State.Board.CreaturesOf(ctx.ControllingPlayer.Opponent()),
            "all_creatures" => ctx.State.Board.AllCreatures(),
            _ => throw new ArgumentException($"Unknown for_each collection '{collection}'."),
        };

        if (filter.Length > 0)
        {
            creatures = creatures.Where(c => MatchesFilter(c.Creature, filter));
        }

        return creatures.Select(c => c.Slot).ToList();
    }

    private static bool MatchesFilter(State.CreatureInstance creature, string filter) => filter switch
    {
        "damaged" => creature.IsDamaged,
        "full_health" => !creature.IsDamaged,
        _ when filter.StartsWith("type:", StringComparison.Ordinal) =>
            creature.Types.Has(GainResourceOp.ParseResourceType(filter["type:".Length..])),
        _ => throw new ArgumentException($"Unknown for_each filter '{filter}'."),
    };
}
