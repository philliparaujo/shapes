namespace Shapes.Core.Effects;

// One entry in the effect vocabulary. Each op is a single stateless instance registered once
// in EffectRegistry -- see that file for why the registry is the single source of truth for
// "what ops exist" rather than a separately maintained list.
public abstract class EffectOp
{
    // The JSON "op" value, e.g. "damage". Matched case-sensitively; card data is written by
    // hand so a consistent snake_case convention matters more than leniency.
    public abstract string Name { get; }

    public abstract void Apply(EffectContext ctx, EffectArgs args);
}
