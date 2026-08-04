using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

public class TargetResolverTests
{
    [Fact]
    public void Self_resolves_to_the_source_slot()
    {
        var state = new StateBuilder().P1(p => p.Slot(0, "a", TypeMask.Wheel)).Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Equal([new SlotIndex(PlayerId.One, 0)], TargetResolver.Resolve(ctx, TargetSelector.Self));
    }

    [Fact]
    public void Opposing_resolves_to_empty_when_the_facing_slot_is_empty()
    {
        var state = new StateBuilder().P1(p => p.Slot(0, "a", TypeMask.Wheel)).Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Empty(TargetResolver.Resolve(ctx, TargetSelector.Opposing));
    }

    [Fact]
    public void Left_friendly_from_slot_zero_has_no_neighbor()
    {
        var state = new StateBuilder().P1(p => p.Slot(0, "a", TypeMask.Wheel)).Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Empty(TargetResolver.Resolve(ctx, TargetSelector.LeftFriendly));
    }

    [Fact]
    public void Left_friendly_from_slot_one_resolves_to_slot_zero_when_occupied()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 1), null);

        Assert.Equal([new SlotIndex(PlayerId.One, 0)], TargetResolver.Resolve(ctx, TargetSelector.LeftFriendly));
    }

    [Fact]
    public void All_enemies_resolves_to_every_occupied_enemy_slot()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(0, "x", TypeMask.Anvil).Slot(2, "y", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Equal(2, TargetResolver.Resolve(ctx, TargetSelector.AllEnemies).Count);
    }

    [Fact]
    public void All_friendlies_resolves_to_every_occupied_friendly_slot()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel).Slot(1, "b", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Equal(2, TargetResolver.Resolve(ctx, TargetSelector.AllFriendlies).Count);
    }

    [Fact]
    public void Chosen_enemy_resolves_to_the_resolved_choice()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(1, "x", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0),
            new SlotIndex(PlayerId.Two, 1));

        Assert.Equal([new SlotIndex(PlayerId.Two, 1)], TargetResolver.Resolve(ctx, TargetSelector.ChosenEnemy));
    }

    [Fact]
    public void Chosen_selector_with_no_resolved_choice_is_empty()
    {
        var state = new StateBuilder().P1(p => p.Slot(0, "a", TypeMask.Wheel)).Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Empty(TargetResolver.Resolve(ctx, TargetSelector.ChosenEnemy));
    }

    [Fact]
    public void Chosen_enemy_candidates_are_every_valid_target_when_none_taunts()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(0, "x", TypeMask.Anvil).Slot(1, "y", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Equal(2, TargetResolver.ChosenCandidates(ctx, TargetSelector.ChosenEnemy).Count);
    }

    [Fact]
    public void Chosen_enemy_candidates_zero_when_no_enemies_are_present()
    {
        var state = new StateBuilder().P1(p => p.Slot(0, "a", TypeMask.Wheel)).Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        Assert.Empty(TargetResolver.ChosenCandidates(ctx, TargetSelector.ChosenEnemy));
    }
}
