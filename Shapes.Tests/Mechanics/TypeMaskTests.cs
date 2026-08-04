using Shapes.Core.Primitives;

namespace Shapes.Tests.Mechanics;

// Type sets. Count drives per-turn income (one resource per type), and multi-type creatures
// are the ones exposed to 2x damage, so both are pinned here.
public class TypeMaskTests
{
    [Fact]
    public void None_contains_nothing()
    {
        Assert.True(TypeMask.None.IsEmpty);
        Assert.Equal(0, TypeMask.None.Count);
        Assert.False(TypeMask.None.IsMultiType);
        Assert.Empty(TypeMask.None.ToArray());

        foreach (var t in ResourceTypes.All)
        {
            Assert.False(TypeMask.None.Has(t));
        }
    }

    [Theory]
    [InlineData(ResourceType.Spike)]
    [InlineData(ResourceType.Anvil)]
    [InlineData(ResourceType.Wheel)]
    public void Single_type_mask_contains_only_that_type(ResourceType type)
    {
        var mask = TypeMask.Of(type);

        Assert.True(mask.Has(type));
        Assert.Equal(1, mask.Count);
        Assert.False(mask.IsMultiType);

        foreach (var other in ResourceTypes.All.Where(t => t != type))
        {
            Assert.False(mask.Has(other));
        }
    }

    [Fact]
    public void Static_singles_match_their_factory_equivalents()
    {
        Assert.Equal(TypeMask.Of(ResourceType.Spike), TypeMask.Spike);
        Assert.Equal(TypeMask.Of(ResourceType.Anvil), TypeMask.Anvil);
        Assert.Equal(TypeMask.Of(ResourceType.Wheel), TypeMask.Wheel);
    }

    [Fact]
    public void Union_combines_typings()
    {
        // This is what merging does: the result carries both operands' types.
        var merged = TypeMask.Spike.Union(TypeMask.Wheel);

        Assert.True(merged.Has(ResourceType.Spike));
        Assert.True(merged.Has(ResourceType.Wheel));
        Assert.False(merged.Has(ResourceType.Anvil));
        Assert.Equal(2, merged.Count);
        Assert.True(merged.IsMultiType);
    }

    [Fact]
    public void Merging_two_of_the_same_type_stays_single_type()
    {
        // Two Spike creatures merge into a creature still typed Spike alone: one resource of
        // income per turn rather than two, and no exposure to the multi-type 2x rule. It is
        // still merge-locked, but that lock is creature state rather than typing, so it is
        // tracked on CreatureInstance rather than here.
        var merged = TypeMask.Spike.Union(TypeMask.Spike);

        Assert.Equal(TypeMask.Spike, merged);
        Assert.Equal(1, merged.Count);
        Assert.False(merged.IsMultiType);
    }

    [Fact]
    public void Union_is_idempotent_and_commutative()
    {
        var a = TypeMask.Spike | TypeMask.Anvil;
        var b = TypeMask.Anvil | TypeMask.Spike;

        Assert.Equal(a, b);
        Assert.Equal(a, a | a);
    }

    [Fact]
    public void Tri_type_is_representable()
    {
        // Reachable by merging a two-type creature with a third type; the effectiveness
        // rules have to handle it.
        var all = TypeMask.Of(ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel);

        Assert.Equal(3, all.Count);
        Assert.True(all.IsMultiType);
        Assert.Equal(ResourceTypes.All, all.ToArray());
    }

    [Fact]
    public void Count_is_the_number_of_resources_generated_per_turn()
    {
        // Income is 1 per type held, so Count is the income contribution directly.
        Assert.Equal(1, TypeMask.Spike.Count);
        Assert.Equal(2, (TypeMask.Spike | TypeMask.Wheel).Count);
        Assert.Equal(3, TypeMask.Of(ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel).Count);
    }

    [Fact]
    public void Intersect_and_overlaps_find_shared_types()
    {
        var spikeWheel = TypeMask.Spike | TypeMask.Wheel;
        var anvilWheel = TypeMask.Anvil | TypeMask.Wheel;

        Assert.Equal(TypeMask.Wheel, spikeWheel & anvilWheel);
        Assert.True(spikeWheel.Overlaps(anvilWheel));
        Assert.False(TypeMask.Spike.Overlaps(TypeMask.Anvil));
    }

    [Fact]
    public void ToArray_is_in_declaration_order()
    {
        var mask = TypeMask.Wheel | TypeMask.Spike;

        // Spike (0) before Wheel (2) regardless of union order, so income and rendering are
        // deterministic.
        Assert.Equal([ResourceType.Spike, ResourceType.Wheel], mask.ToArray());
    }

    [Fact]
    public void Masks_are_value_equal()
    {
        Assert.Equal(TypeMask.Spike | TypeMask.Wheel, TypeMask.Wheel | TypeMask.Spike);
        Assert.True((TypeMask.Spike | TypeMask.Wheel) == (TypeMask.Wheel | TypeMask.Spike));
        Assert.True(TypeMask.Spike != TypeMask.Anvil);
    }

    [Fact]
    public void ToString_lists_types_or_a_dash_when_empty()
    {
        Assert.Equal("-", TypeMask.None.ToString());
        Assert.Equal("Spike", TypeMask.Spike.ToString());
        Assert.Equal("Spike/Wheel", (TypeMask.Spike | TypeMask.Wheel).ToString());
    }
}
