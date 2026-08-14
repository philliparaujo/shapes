using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects.Ops;

// { "op": "damage", "target": "opposing", "amount": 1 }
internal sealed class DamageOp : EffectOp
{
    public override string Name => "damage";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var amount = args.Int("amount");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            CombatResolver.DealDamage(ctx, slot, amount);
        }
    }
}

// How damage_scaled (and gain_resource_scaled/heal_scaled, which share this enum) compute their
// base amount before their own resolution path.
internal enum DamageScale
{
    Health,
    Count,
    HandSize,
    SelectorHealth,
    HandComposition,
    Resource,
    MissingHealth,
    SelectorMissingHealth,
    DestroyedThisTurn,
    DamagedCreatures,
    Spent,
}

// { "op": "damage_scaled", "target": "opposing", "scale": "health", "multiplier": 1,
//   "divisor": 1 }
//
// "health": base = SOURCE creature's current health * multiplier / divisor (T Swarm: "1 per 2
//   health" is scale health, divisor 2; Monk: "damage equal to health" is scale health,
//   multiplier 1).
// "count": base = number of friendly creatures * multiplier.
// "hand_size": base = controller's hand size * multiplier.
// "selector_health": base = the health of whichever creature `health_source` resolves to --
//   NOT necessarily the creature being hit (that is `target`) and not necessarily the move's
//   own source (that is scale "health"). Worshipper: "damage [an enemy] equal to the LEFT
//   FRIENDLY's health" is target:opposing, scale:selector_health, health_source:left_friendly
//   -- three independent slots (attacker, health source, victim) needed three independent
//   selectors, which is exactly why this is not folded into "health" or "target".
// "resource": base = the controller's current amount of `resource_type` (Champion T: "deal 1
//   for each spike [resource]"). Needs its own argument, "resource_type", since "target" is
//   already the damage target -- see ComputeBase.
//
// "divisor" defaults to 1 and applies via integer division, after the multiplier: T Swarm's
// "1 damage per 2 health" needs the floor, not a fraction, so 5 health deals 2, not 2.5.
internal sealed class DamageScaledOp : EffectOp
{
    public override string Name => "damage_scaled";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var scale = ParseScale(args.String("scale"));
        var multiplier = args.IntOrDefault("multiplier", 1);
        var divisor = args.IntOrDefault("divisor", 1);
        var selector = args.Target();
        var resourceType = args.Has("resource_type")
            ? GainResourceOp.ParseResourceType(args.String("resource_type"))
            : (ResourceType?)null;
        var healthSource = args.Has("health_source")
            ? args.Target("health_source")
            : (TargetSelector?)null;

        foreach (var slot in TargetResolver.Resolve(ctx, selector))
        {
            var amount = ComputeBase(ctx, scale, resourceType, healthSource) * multiplier / divisor;
            CombatResolver.DealDamage(ctx, slot, amount);
        }
    }

    // `resourceType` is meaningful for HandComposition (how many hand cards cost that type) and
    // Resource (how much of that resource the controller currently holds). `healthSource` is
    // meaningful only for SelectorHealth: it names a DIFFERENT selector to resolve and read the
    // health of, independent of whatever `target` this call is hitting. Every other scale
    // ignores whichever of the two it doesn't need.
    internal static int ComputeBase(
        EffectContext ctx, DamageScale scale, ResourceType? resourceType = null,
        TargetSelector? healthSource = null) => scale switch
    {
        DamageScale.Health => ctx.SourceCreature?.Health ?? 0,
        DamageScale.Count => ctx.State.Board.CountCreatures(ctx.ControllingPlayer),
        DamageScale.HandSize => ctx.State[ctx.ControllingPlayer].Hand.Count,
        DamageScale.SelectorHealth => ResolveSelectorHealth(ctx, healthSource),
        DamageScale.HandComposition => resourceType is { } t ? ctx.HandComposition[t] : 0,
        DamageScale.Resource => resourceType is { } r ? ctx.State[ctx.ControllingPlayer].Resources[r] : 0,
        // Damage already taken by the SOURCE creature (max - current), the counterpart of
        // "health". Zero at full health, so a card scaling on it does nothing until hurt.
        DamageScale.MissingHealth => ctx.SourceCreature is { } c ? Math.Max(0, c.MaxHealth - c.Health) : 0,
        // The selector-named counterpart of MissingHealth, standing to it exactly as
        // SelectorHealth stands to Health. A SPELL has no SourceCreature (EffectContext.SourceSlot
        // is null for spells), so "missing_health" silently computes 0 there -- a spell that wants
        // to read a creature's damage must name that creature via health_source instead.
        DamageScale.SelectorMissingHealth => ResolveSelectorMissingHealth(ctx, healthSource),
        // Creatures destroyed THIS TURN, either side, any cause -- read from TurnEvents, the same
        // source draw_scaled has always used. Promoted into the shared vocabulary (rather than
        // left as draw_scaled's private special case) once a second op needed it: Gravewarden's
        // Reap now buffs max health per kill instead of drawing per kill.
        DamageScale.DestroyedThisTurn =>
            ctx.State.TurnEvents.Count(e => e.Kind == TurnEventKind.CreatureDestroyed),
        // Damaged creatures currently on the board, either side -- the board-state counterpart
        // of DestroyedThisTurn. Enrage already expresses "for each damaged creature" via
        // for_each + draw, but for_each rebinds "self" to each iterated creature, so an effect
        // that must land on the SOURCE (T Berserker: "+1 attack for each damaged creature")
        // cannot be written that way and needs this as a scale instead.
        DamageScale.DamagedCreatures =>
            ctx.State.Board.AllCreatures().Count(c => c.Creature.IsDamaged),
        // How much the enclosing `spend_all` just consumed. Zero outside one -- see
        // EffectContext.SpentAmount.
        DamageScale.Spent => ctx.SpentAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown damage scale."),
    };

    private static int ResolveSelectorHealth(EffectContext ctx, TargetSelector? healthSource)
    {
        if (healthSource is not { } selector)
        {
            throw new ArgumentException("scale 'selector_health' requires a 'health_source' argument.");
        }

        var slots = TargetResolver.Resolve(ctx, selector);
        return slots.Count == 1 ? ctx.State.Board[slots[0]]?.Health ?? 0 : 0;
    }

    private static int ResolveSelectorMissingHealth(EffectContext ctx, TargetSelector? healthSource)
    {
        if (healthSource is not { } selector)
        {
            throw new ArgumentException(
                "scale 'selector_missing_health' requires a 'health_source' argument.");
        }

        var slots = TargetResolver.Resolve(ctx, selector);
        if (slots.Count != 1 || ctx.State.Board[slots[0]] is not { } creature)
        {
            return 0;
        }

        return Math.Max(0, creature.MaxHealth - creature.Health);
    }

    internal static DamageScale ParseScale(string raw) => raw switch
    {
        "health" => DamageScale.Health,
        "count" => DamageScale.Count,
        "hand_size" => DamageScale.HandSize,
        "selector_health" => DamageScale.SelectorHealth,
        "hand_composition" => DamageScale.HandComposition,
        "resource" => DamageScale.Resource,
        "missing_health" => DamageScale.MissingHealth,
        "selector_missing_health" => DamageScale.SelectorMissingHealth,
        "destroyed_this_turn" => DamageScale.DestroyedThisTurn,
        "damaged_creatures" => DamageScale.DamagedCreatures,
        "spent" => DamageScale.Spent,
        _ => throw new ArgumentException($"Unknown scale '{raw}'."),
    };
}
