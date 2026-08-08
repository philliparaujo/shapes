using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// Godot's ResourceIcons duplicates Shapes.Console's (see that class's header for why); these
// tests pin the same behavior so the duplication cannot silently drift.
public class ResourceIconsTests
{
    [Theory]
    [InlineData(ResourceType.Spike, "△")]
    [InlineData(ResourceType.Anvil, "▢")]
    [InlineData(ResourceType.Wheel, "◯")]
    public void Of_returns_the_glyph_for_each_type(ResourceType type, string expected)
    {
        Assert.Equal(expected, ResourceIcons.Of(type));
    }

    [Fact]
    public void Describe_pool_always_prints_all_three_types_even_at_zero()
    {
        var pool = new ResourcePool(spike: 2, anvil: 0, wheel: 1);

        Assert.Equal("△2 ▢0 ◯1", ResourceIcons.Describe(pool));
    }

    [Fact]
    public void DescribeCost_omits_zero_types()
    {
        var cost = new ResourcePool(spike: 0, anvil: 2, wheel: 0);

        Assert.Equal("▢2", ResourceIcons.DescribeCost(cost));
    }

    [Fact]
    public void DescribeCost_of_zero_cost_reads_free()
    {
        Assert.Equal("free", ResourceIcons.DescribeCost(ResourcePool.Empty));
    }

    [Fact]
    public void Describe_types_joins_multiple_with_a_slash()
    {
        var merged = TypeMask.Of(ResourceType.Spike, ResourceType.Wheel);

        Assert.Equal("△/◯", ResourceIcons.Describe(merged));
    }

    [Fact]
    public void Describe_empty_types_reads_a_dash()
    {
        Assert.Equal("-", ResourceIcons.Describe(TypeMask.None));
    }
}
