using Shapes.Core.Primitives;

namespace Shapes.Tests.Mechanics;

// Pins the resource type enum. The numeric values are used as array indices in the hot
// search path and are persisted in card JSON, so reordering them silently corrupts both.
public class ResourceTypeTests
{
    [Fact]
    public void Count_matches_the_number_of_declared_types()
    {
        Assert.Equal(ResourceTypes.Count, Enum.GetValues<ResourceType>().Length);
        Assert.Equal(ResourceTypes.Count, ResourceTypes.All.Length);
    }

    [Theory]
    [InlineData(ResourceType.Spike, 0)]
    [InlineData(ResourceType.Anvil, 1)]
    [InlineData(ResourceType.Wheel, 2)]
    public void Values_are_stable(ResourceType type, int expected)
    {
        // These double as array indices; changing them is a breaking data-format change.
        Assert.Equal(expected, (int)type);
    }

    [Fact]
    public void All_contains_every_type_exactly_once()
    {
        Assert.Equal(Enum.GetValues<ResourceType>().OrderBy(t => t), ResourceTypes.All.OrderBy(t => t));
    }
}
