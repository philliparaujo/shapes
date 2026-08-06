using Shapes.Core.Primitives;

namespace Shapes.Console;

// Icon glyphs for the three resource types, shared by every place the console renders a cost or
// a creature's type -- one spelling of "△ is Spike" rather than each caller inventing its own.
// BoardView's resource-pool line already used these three glyphs; this pulls them out so cost
// and type rendering can match it instead of falling back to the word form ("Spike/Wheel").
public static class ResourceIcons
{
    public static string Of(ResourceType type) => type switch
    {
        ResourceType.Spike => "△",
        ResourceType.Anvil => "▢",
        ResourceType.Wheel => "◯",
        _ => type.ToString(),
    };

    // A player's resource pool, e.g. "△2 ▢2 ◯2" -- always all three, even at zero, so the
    // reader can track a resource across turns without it popping in and out of the line.
    public static string Describe(ResourcePool pool) =>
        $"{Of(ResourceType.Spike)}{pool.Spike} {Of(ResourceType.Anvil)}{pool.Anvil} {Of(ResourceType.Wheel)}{pool.Wheel}";

    // A cost, e.g. "△1 ▢2" -- only the types actually charged, unlike a resource pool, since a
    // card's cost is usually single-type and printing "△0 ▢0 ◯3" next to every action would bury
    // the one number that matters. "free" for a zero-cost card/move.
    public static string DescribeCost(ResourcePool cost)
    {
        var parts = ResourceTypes.All
            .Where(type => cost[type] > 0)
            .Select(type => $"{Of(type)}{cost[type]}")
            .ToArray();

        return parts.Length == 0 ? "free" : string.Join(" ", parts);
    }

    // A creature's type mask as icons, e.g. "△/◯" for a merged Spike/Wheel creature, "-" when
    // empty (a spell, which has no board type). Mirrors TypeMask.ToString()'s word form
    // ("Spike/Wheel") but in the same glyph alphabet as resources and costs.
    public static string Describe(TypeMask types) =>
        types.IsEmpty ? "-" : string.Join("/", types.ToArray().Select(Of));
}
