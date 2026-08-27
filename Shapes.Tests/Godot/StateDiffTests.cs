using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// Phase 5 step A2: StateDiff is the view-model scenes render from. Built by comparing
// GameState before/after ActionExecutor.Apply rather than reading GameState.TurnEvents,
// because TurnEvents has no damage/move-used/resource-change entries and is cleared on
// EndTurn (DESIGN.md A2). These tests build before/after states directly with StateBuilder so
// each assertion is about diff semantics, not about which action produced the change.
public class StateDiffTests
{
    [Fact]
    public void Identical_states_produce_an_empty_diff()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3).Resources(spike: 2))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3).Resources(spike: 2))
            .Build();

        var diff = StateDiff.Between(before, after);

        Assert.Empty(diff.SlotChanges);
        Assert.Empty(diff.PlayerChanges);
    }

    [Fact]
    public void Damage_to_a_creature_is_reported_as_a_slot_change()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 5))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 3))
            .Build();

        var diff = StateDiff.Between(before, after);

        var change = Assert.Single(diff.SlotChanges);
        Assert.Equal(new SlotIndex(PlayerId.One, 0), change.Slot);
        Assert.Equal(5, change.Before!.Health);
        Assert.Equal(3, change.After!.Health);
        Assert.Empty(diff.PlayerChanges);
    }

    [Fact]
    public void A_destroyed_creature_reports_after_as_null()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 1))
            .Build();
        var after = new StateBuilder().Build();

        var diff = StateDiff.Between(before, after);

        var change = Assert.Single(diff.SlotChanges);
        Assert.NotNull(change.Before);
        Assert.Null(change.After);
    }

    [Fact]
    public void A_played_creature_reports_before_as_null()
    {
        var before = new StateBuilder().Build();
        var after = new StateBuilder()
            .P2(p => p.Slot(1, "monk", TypeMask.Anvil, maxHealth: 4))
            .Build();

        var diff = StateDiff.Between(before, after);

        var change = Assert.Single(diff.SlotChanges);
        Assert.Equal(new SlotIndex(PlayerId.Two, 1), change.Slot);
        Assert.Null(change.Before);
        Assert.NotNull(change.After);
        Assert.Equal("monk", change.After!.CardId);
    }

    [Fact]
    public void Untouched_slots_are_not_included_in_the_diff()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 3))
            .P2(p => p.Slot(2, "monk", TypeMask.Anvil, maxHealth: 4, health: 4))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 1))
            .P2(p => p.Slot(2, "monk", TypeMask.Anvil, maxHealth: 4, health: 4))
            .Build();

        var diff = StateDiff.Between(before, after);

        var change = Assert.Single(diff.SlotChanges);
        Assert.Equal(new SlotIndex(PlayerId.One, 0), change.Slot);
    }

    [Fact]
    public void Score_and_resource_changes_are_reported_per_player()
    {
        var before = new StateBuilder()
            .P1(p => p.Resources(spike: 1).Score(2))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Resources(spike: 3).Score(3))
            .Build();

        var diff = StateDiff.Between(before, after);

        var change = Assert.Single(diff.PlayerChanges);
        Assert.Equal(PlayerId.One, change.Player);
        Assert.Equal(2, change.ScoreBefore);
        Assert.Equal(3, change.ScoreAfter);
        Assert.Equal(new ResourcePool(1, 0, 0), change.ResourcesBefore);
        Assert.Equal(new ResourcePool(3, 0, 0), change.ResourcesAfter);
    }

    [Fact]
    public void Hand_size_change_is_reported_without_a_score_or_resource_change()
    {
        var before = new StateBuilder()
            .P1(p => p.Hand("striker"))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Hand("striker", "monk"))
            .Build();

        var diff = StateDiff.Between(before, after);

        var change = Assert.Single(diff.PlayerChanges);
        Assert.Equal(1, change.HandSizeBefore);
        Assert.Equal(2, change.HandSizeAfter);
        Assert.Equal(change.ScoreBefore, change.ScoreAfter);
    }

    [Fact]
    public void Phase_and_active_player_transitions_are_captured()
    {
        var before = new StateBuilder().ActivePlayer(PlayerId.One).Phase(Shapes.Core.State.TurnPhase.Actions).Build();
        var after = new StateBuilder().ActivePlayer(PlayerId.Two).Phase(Shapes.Core.State.TurnPhase.Actions).Build();

        var diff = StateDiff.Between(before, after);

        Assert.Equal(PlayerId.One, diff.ActivePlayerBefore);
        Assert.Equal(PlayerId.Two, diff.ActivePlayerAfter);
    }
}
