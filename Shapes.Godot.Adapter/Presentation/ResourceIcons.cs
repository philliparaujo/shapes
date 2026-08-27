using Shapes.Core.Primitives;

namespace Shapes.Godot.Adapter;

// Icon glyphs for the three resource types. Godot's equivalent of Shapes.Console's
// ResourceIcons -- that class lives in Shapes.Console, which Shapes.Godot cannot reference
// (a Godot-SDK project referencing a console exe is not a normal dependency direction), so
// this is a second copy of the same small formatting logic rather than a shared one. Keep
// the glyphs identical to the console's if either ever changes.
public static class ResourceIcons
{
    public static string Of(ResourceType type) => type switch
    {
        ResourceType.Spike => "△",
        ResourceType.Anvil => "▢",
        ResourceType.Wheel => "◯",
        _ => type.ToString(),
    };

    public static string Describe(ResourcePool pool) =>
        $"{Of(ResourceType.Spike)}{pool.Spike} {Of(ResourceType.Anvil)}{pool.Anvil} {Of(ResourceType.Wheel)}{pool.Wheel}";

    public static string DescribeCost(ResourcePool cost)
    {
        var parts = ResourceTypes.All
            .Where(type => cost[type] > 0)
            .Select(type => $"{Of(type)}{cost[type]}")
            .ToArray();

        return parts.Length == 0 ? "free" : string.Join(" ", parts);
    }

    public static string Describe(TypeMask types) =>
        types.IsEmpty ? "-" : string.Join("/", types.ToArray().Select(Of));
}
