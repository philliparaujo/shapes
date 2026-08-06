using Shapes.Console;
using Shapes.Core.Primitives;

namespace Shapes.Tests.Console;

// Icons instead of words for costs and creature types, matching the glyph alphabet the resource
// pool line already used (△/▢/◯ for Spike/Anvil/Wheel).
public class ResourceIconsTests
{
    [Fact]
    public void Pool_always_shows_all_three_types_even_at_zero()
    {
        var text = ResourceIcons.Describe(new ResourcePool(2, 0, 1));

        Assert.Equal("△2 ▢0 ◯1", text);
    }

    [Fact]
    public void Cost_omits_zero_types()
    {
        var text = ResourceIcons.DescribeCost(new ResourcePool(0, 3, 0));

        Assert.Equal("▢3", text);
    }

    [Fact]
    public void Zero_cost_reads_as_free()
    {
        Assert.Equal("free", ResourceIcons.DescribeCost(ResourcePool.Empty));
    }

    [Fact]
    public void Multi_type_cost_lists_every_charged_type()
    {
        var text = ResourceIcons.DescribeCost(new ResourcePool(1, 1, 0));

        Assert.Equal("△1 ▢1", text);
    }

    [Fact]
    public void TypeMask_renders_as_icons_not_words()
    {
        var text = ResourceIcons.Describe(TypeMask.Wheel);

        Assert.Equal("◯", text);
        Assert.DoesNotContain("Wheel", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_type_mask_joins_icons_with_a_slash()
    {
        var text = ResourceIcons.Describe(TypeMask.Of(ResourceType.Spike, ResourceType.Wheel));

        Assert.Equal("△/◯", text);
    }

    [Fact]
    public void Empty_type_mask_renders_as_a_dash()
    {
        Assert.Equal("-", ResourceIcons.Describe(TypeMask.None));
    }
}
