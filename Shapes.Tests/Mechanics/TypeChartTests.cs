using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Tests.Mechanics;

// The rock-paper-scissors damage rule, including the merged-target case.
public class TypeChartTests
{
    private static readonly TypeChart Chart = TypeChart.Default;

    [Theory]
    // The cycle: each type deals 2x to the next.
    [InlineData(ResourceType.Spike, ResourceType.Wheel, 2.0)]
    [InlineData(ResourceType.Wheel, ResourceType.Anvil, 2.0)]
    [InlineData(ResourceType.Anvil, ResourceType.Spike, 2.0)]
    // The reverse direction is neutral -- there is no resistance, only neutral and double.
    [InlineData(ResourceType.Wheel, ResourceType.Spike, 1.0)]
    [InlineData(ResourceType.Anvil, ResourceType.Wheel, 1.0)]
    [InlineData(ResourceType.Spike, ResourceType.Anvil, 1.0)]
    // Same type is neutral.
    [InlineData(ResourceType.Spike, ResourceType.Spike, 1.0)]
    [InlineData(ResourceType.Anvil, ResourceType.Anvil, 1.0)]
    [InlineData(ResourceType.Wheel, ResourceType.Wheel, 1.0)]
    public void Single_type_matchups(ResourceType attacker, ResourceType target, double expected)
    {
        Assert.Equal(expected, Chart.MultiplierAgainst(attacker, TypeMask.Of(target)));
    }

    [Fact]
    public void Merged_target_takes_double_when_one_type_matches_and_another_is_weak()
    {
        // The rule from the design notes: Spike deals 2x to Spike/Wheel, because Spike
        // matches one of the target's types and Wheel is weak to Spike.
        var spikeWheel = TypeMask.Spike | TypeMask.Wheel;

        Assert.Equal(2.0, Chart.MultiplierAgainst(ResourceType.Spike, spikeWheel));
    }

    [Fact]
    public void Merged_target_with_a_match_but_no_weak_type_is_neutral()
    {
        // Spike attacking Spike/Anvil: the attacker's type is present, but Spike beats Wheel
        // and the target has no Wheel, so there is nothing to double against.
        var spikeAnvil = TypeMask.Spike | TypeMask.Anvil;

        Assert.Equal(1.0, Chart.MultiplierAgainst(ResourceType.Spike, spikeAnvil));
    }

    [Fact]
    public void Merged_target_with_a_weak_type_but_no_match_is_neutral()
    {
        // Spike attacking Anvil/Wheel: Wheel is weak to Spike, but the target carries no
        // Spike type, so it stays 1x. This is the case most likely to be read differently by
        // a future implementer, so it is pinned explicitly.
        var anvilWheel = TypeMask.Anvil | TypeMask.Wheel;

        Assert.Equal(1.0, Chart.MultiplierAgainst(ResourceType.Spike, anvilWheel));
    }

    [Fact]
    public void Tri_type_target_follows_the_same_rule()
    {
        // Everything present, so any attacker both matches and finds its weak target.
        var all = TypeMask.Of(ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel);

        foreach (var attacker in ResourceTypes.All)
        {
            Assert.Equal(2.0, Chart.MultiplierAgainst(attacker, all));
        }
    }

    [Fact]
    public void Same_type_merge_is_treated_as_a_plain_single_type_creature()
    {
        // Two Spike creatures merge into a still-Spike creature, so it takes damage exactly
        // as an unmerged Spike does: 2x from Anvil, 1x from Spike. Merging same-type gains
        // stats and moves with no defensive downside, unlike a mixed merge.
        var merged = TypeMask.Spike | TypeMask.Spike;

        Assert.Equal(Chart.MultiplierAgainst(ResourceType.Anvil, TypeMask.Spike),
                     Chart.MultiplierAgainst(ResourceType.Anvil, merged));
        Assert.Equal(2.0, Chart.MultiplierAgainst(ResourceType.Anvil, merged));
        Assert.Equal(1.0, Chart.MultiplierAgainst(ResourceType.Spike, merged));
    }

    [Fact]
    public void Empty_type_mask_is_neutral()
    {
        Assert.Equal(1.0, Chart.MultiplierAgainst(ResourceType.Spike, TypeMask.None));
    }

    [Fact]
    public void Multiplier_of_one_disables_effectiveness()
    {
        var flat = TypeChart.Default.With(weaknessMultiplier: 1.0);

        Assert.Equal(1.0, flat.MultiplierAgainst(ResourceType.Spike, TypeMask.Wheel));
    }

    // Builds a chart from a cycle alone, for the validation tests below.
    private static TypeChart WithCycle(Dictionary<ResourceType, ResourceType> cycle) =>
        new(cycle, weaknessMultiplier: 2.0);

    [Fact]
    public void Beats_and_IsWeakTo_agree()
    {
        foreach (var attacker in ResourceTypes.All)
        {
            var beaten = Chart.Beats(attacker);
            Assert.True(Chart.IsWeakTo(beaten, attacker));
            Assert.False(Chart.IsWeakTo(attacker, attacker));
        }
    }

    [Fact]
    public void Every_type_is_beaten_by_exactly_one_other()
    {
        var beaten = ResourceTypes.All.Select(Chart.Beats).ToList();

        Assert.Equal(ResourceTypes.Count, beaten.Distinct().Count());
    }

    [Fact]
    public void Malformed_cycle_is_rejected()
    {
        // Spike and Anvil both beat Wheel: Spike is invulnerable and Wheel doubly weak.
        var broken = new Dictionary<ResourceType, ResourceType>
        {
            [ResourceType.Spike] = ResourceType.Wheel,
            [ResourceType.Anvil] = ResourceType.Wheel,
            [ResourceType.Wheel] = ResourceType.Anvil,
        };

        Assert.Throws<ArgumentException>(() => WithCycle(broken));
    }

    [Fact]
    public void Cycle_with_a_self_beat_is_rejected()
    {
        var broken = new Dictionary<ResourceType, ResourceType>
        {
            [ResourceType.Spike] = ResourceType.Spike,
            [ResourceType.Anvil] = ResourceType.Wheel,
            [ResourceType.Wheel] = ResourceType.Anvil,
        };

        Assert.Throws<ArgumentException>(() => WithCycle(broken));
    }

    [Fact]
    public void Incomplete_cycle_is_rejected()
    {
        var incomplete = new Dictionary<ResourceType, ResourceType>
        {
            [ResourceType.Spike] = ResourceType.Wheel,
        };

        Assert.Throws<ArgumentException>(() => WithCycle(incomplete));
    }

    [Fact]
    public void Multiplier_below_one_is_rejected()
    {
        // Below 1.0 would mean the "weak" type takes reduced damage -- an inverted rule,
        // almost certainly a typo rather than an intended sweep.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TypeChart.Default.With(weaknessMultiplier: 0.5));
    }

    [Fact]
    public void With_preserves_the_cycle()
    {
        var chart = TypeChart.Default.With(weaknessMultiplier: 3.0);

        Assert.Equal(3.0, chart.WeaknessMultiplier);

        foreach (var t in ResourceTypes.All)
        {
            Assert.Equal(TypeChart.Default.Beats(t), chart.Beats(t));
        }
    }
}
