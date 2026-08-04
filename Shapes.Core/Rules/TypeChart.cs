using Shapes.Core.Primitives;

namespace Shapes.Core.Rules;

// The rock-paper-scissors damage rule.
//
// Cycle: Spike -> Wheel -> Anvil -> Spike, each dealing WeaknessMultiplier to the next.
// Everything else is 1x; there is no resistance/halving.
//
// The cycle and multiplier are configurable so Phase 3 can sweep them; the shipping values
// live once, in Default.
public sealed class TypeChart
{
    // beats[attacker] = the type the attacker deals bonus damage to.
    private readonly ResourceType[] _beats;

    public double WeaknessMultiplier { get; }

    // No default arguments here on purpose: the shipping values are stated once, in Default
    // below, so a caller cannot pick up a "default" multiplier that quietly disagrees with
    // the shipped ruleset. Construct explicitly, or start from Default and use With.
    public TypeChart(IReadOnlyDictionary<ResourceType, ResourceType> cycle, double weaknessMultiplier)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        if (weaknessMultiplier < 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weaknessMultiplier), weaknessMultiplier,
                "Weakness multiplier must be at least 1.0 (1.0 disables effectiveness).");
        }

        _beats = new ResourceType[ResourceTypes.Count];
        foreach (var attacker in ResourceTypes.All)
        {
            if (!cycle.TryGetValue(attacker, out var target))
            {
                throw new ArgumentException($"Type cycle is missing an entry for {attacker}.", nameof(cycle));
            }

            if (target == attacker)
            {
                throw new ArgumentException($"Type cycle has {attacker} beating itself.", nameof(cycle));
            }

            _beats[(int)attacker] = target;
        }

        // Each type must be beaten by exactly one other, or the cycle is not a cycle -- e.g.
        // Spike->Wheel, Anvil->Wheel, Wheel->Anvil leaves Spike invulnerable and Wheel doubly
        // weak. Catching that here means a malformed chart cannot reach the damage code.
        var beatenCounts = new int[ResourceTypes.Count];
        foreach (var target in _beats)
        {
            beatenCounts[(int)target]++;
        }

        for (var i = 0; i < beatenCounts.Length; i++)
        {
            if (beatenCounts[i] != 1)
            {
                throw new ArgumentException(
                    $"Type cycle is malformed: {(ResourceType)i} is beaten by {beatenCounts[i]} types, expected exactly 1.",
                    nameof(cycle));
            }
        }

        WeaknessMultiplier = weaknessMultiplier;
    }

    // The shipping effectiveness rules. Single source for these values; the loader tests
    // assert Shapes.Content/rulesets/default.json agrees with them.
    public static TypeChart Default { get; } = new(
        cycle: new Dictionary<ResourceType, ResourceType>
        {
            [ResourceType.Spike] = ResourceType.Wheel,
            [ResourceType.Wheel] = ResourceType.Anvil,
            [ResourceType.Anvil] = ResourceType.Spike,
        },
        weaknessMultiplier: 2.0);

    // A copy of this chart with a different multiplier. The natural way to express a Phase 3
    // sweep -- vary the multiplier without restating the cycle.
    public TypeChart With(double weaknessMultiplier) => new(BuildCycle(), weaknessMultiplier);

    private Dictionary<ResourceType, ResourceType> BuildCycle()
    {
        var cycle = new Dictionary<ResourceType, ResourceType>(ResourceTypes.Count);
        foreach (var t in ResourceTypes.All)
        {
            cycle[t] = Beats(t);
        }

        return cycle;
    }

    public ResourceType Beats(ResourceType attacker) => _beats[(int)attacker];

    public bool IsWeakTo(ResourceType target, ResourceType attacker) => Beats(attacker) == target;

    // The damage multiplier for an attack of `attacker` type against a target with `target`
    // types. Attacks with no creature source (spells) are typeless and never scale -- callers
    // handle that by not calling this.
    public double MultiplierAgainst(ResourceType attacker, TypeMask target) =>
        IsDoubled(attacker, target) ? WeaknessMultiplier : 1.0;

    // Base rule: the target must carry a type the attacker beats.
    //
    // A MULTI-TYPE target must additionally carry the attacker's own type. So Spike deals 2x
    // to Spike/Wheel but only 1x to Anvil/Wheel -- mixing types is a real defensive tradeoff
    // rather than pure upside, since it opens an exposure that a pure type does not have.
    //
    // That extra condition is deliberately NOT applied to single-type targets: requiring the
    // attacker's own type to be present is impossible for a creature with one type, and would
    // flatten the whole rock-paper-scissors cycle to 1x.
    private bool IsDoubled(ResourceType attacker, TypeMask target)
    {
        if (!target.Has(Beats(attacker)))
        {
            return false;
        }

        return !target.IsMultiType || target.Has(attacker);
    }
}
