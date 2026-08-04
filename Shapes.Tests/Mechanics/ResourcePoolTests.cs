using Shapes.Core.Primitives;

namespace Shapes.Tests.Mechanics;

// Resource arithmetic. Paying costs runs through this on every action, so the affordability
// and non-negativity rules are pinned here rather than left to the callers.
public class ResourcePoolTests
{
    [Fact]
    public void Empty_pool_is_all_zero()
    {
        var pool = ResourcePool.Empty;

        Assert.Equal(0, pool.Spike);
        Assert.Equal(0, pool.Anvil);
        Assert.Equal(0, pool.Wheel);
        Assert.Equal(0, pool.Total);
        Assert.True(pool.IsEmpty);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Cannot_construct_a_negative_pool(int spike, int anvil, int wheel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourcePool(spike, anvil, wheel));
    }

    [Fact]
    public void Indexer_returns_the_matching_count()
    {
        var pool = new ResourcePool(spike: 1, anvil: 2, wheel: 3);

        Assert.Equal(1, pool[ResourceType.Spike]);
        Assert.Equal(2, pool[ResourceType.Anvil]);
        Assert.Equal(3, pool[ResourceType.Wheel]);
    }

    [Fact]
    public void Of_sets_only_the_named_type()
    {
        var pool = ResourcePool.Of(ResourceType.Anvil, 4);

        Assert.Equal(new ResourcePool(0, 4, 0), pool);
        Assert.Equal(4, pool.Total);
    }

    [Fact]
    public void Add_sums_per_type()
    {
        var a = new ResourcePool(1, 2, 3);
        var b = new ResourcePool(10, 20, 30);

        Assert.Equal(new ResourcePool(11, 22, 33), a.Add(b));
        Assert.Equal(new ResourcePool(11, 22, 33), a + b);
    }

    [Fact]
    public void Add_by_type_increments_one_pool()
    {
        var pool = new ResourcePool(1, 1, 1).Add(ResourceType.Wheel, 2);

        Assert.Equal(new ResourcePool(1, 1, 3), pool);
    }

    [Theory]
    // Exact cover and surplus are affordable; any shortfall in any single type is not.
    [InlineData(2, 2, 2, 2, 2, 2, true)]
    [InlineData(3, 3, 3, 1, 1, 1, true)]
    [InlineData(2, 2, 2, 3, 2, 2, false)]
    [InlineData(2, 2, 2, 2, 3, 2, false)]
    [InlineData(2, 2, 2, 2, 2, 3, false)]
    public void Covers_requires_enough_of_every_type(
        int hs, int ha, int hw, int cs, int ca, int cw, bool expected)
    {
        var held = new ResourcePool(hs, ha, hw);
        var cost = new ResourcePool(cs, ca, cw);

        Assert.Equal(expected, held.Covers(cost));
    }

    [Fact]
    public void Subtract_deducts_per_type()
    {
        var held = new ResourcePool(5, 5, 5);
        var cost = new ResourcePool(1, 2, 3);

        Assert.Equal(new ResourcePool(4, 3, 2), held.Subtract(cost));
        Assert.Equal(new ResourcePool(4, 3, 2), held - cost);
    }

    [Fact]
    public void Subtract_throws_when_unaffordable()
    {
        // Not clamped to zero: an unaffordable payment means legal-action generation let
        // through an action the player cannot pay for. Silently absorbing that would hide
        // the actual bug, which lives upstream.
        var held = new ResourcePool(1, 1, 1);
        var cost = new ResourcePool(2, 0, 0);

        Assert.Throws<InvalidOperationException>(() => held.Subtract(cost));
    }

    [Fact]
    public void TrySubtract_reports_failure_without_throwing()
    {
        var held = new ResourcePool(1, 1, 1);

        Assert.False(held.TrySubtract(new ResourcePool(2, 0, 0), out _));
        Assert.True(held.TrySubtract(new ResourcePool(1, 1, 1), out var exact));
        Assert.Equal(ResourcePool.Empty, exact);
    }

    [Fact]
    public void Pools_are_value_equal()
    {
        Assert.Equal(new ResourcePool(1, 2, 3), new ResourcePool(1, 2, 3));
        Assert.True(new ResourcePool(1, 2, 3) == new ResourcePool(1, 2, 3));
        Assert.True(new ResourcePool(1, 2, 3) != new ResourcePool(3, 2, 1));

        Assert.Equal(
            new ResourcePool(1, 2, 3).GetHashCode(),
            new ResourcePool(1, 2, 3).GetHashCode());
    }

    [Fact]
    public void Pool_ordering_is_not_confused_between_types()
    {
        // 1/2/3 and 3/2/1 hold the same total but are different pools. Guards against a
        // field-ordering mix-up in the constructor or the indexer.
        var a = new ResourcePool(1, 2, 3);
        var b = new ResourcePool(3, 2, 1);

        Assert.Equal(a.Total, b.Total);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_is_spike_anvil_wheel()
    {
        Assert.Equal("2/0/1", new ResourcePool(2, 0, 1).ToString());
    }
}
