using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// Phase 5 step B1d: AnimationScript turns a StateDiff into an ordered cue list.
//
// StateDiff is an unordered SET of changes -- it says what differs, never what happened or in
// what order. These tests pin the two derivations that adds: rejoining a departure and an
// arrival into one Move (the diff reports them as two unrelated slot changes), and the ordering
// that keeps causes ahead of effects. Both are pure list-to-list translation, which is why they
// are testable here at all while everything in Shapes.Godot needs the editor.
public class AnimationScriptTests
{
    private static readonly SlotIndex P1Slot0 = new(PlayerId.One, 0);
    private static readonly SlotIndex P1Slot1 = new(PlayerId.One, 1);
    private static readonly SlotIndex P1Slot2 = new(PlayerId.One, 2);

    [Fact]
    public void An_empty_diff_produces_no_steps()
    {
        var state = new StateBuilder().Build();

        var steps = AnimationScript.From(StateDiff.Between(state, state));

        Assert.Empty(steps);
    }

    [Fact]
    public void A_creature_appearing_in_an_empty_slot_is_a_play()
    {
        var before = new StateBuilder().Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3))
            .Build();

        var step = Assert.Single(AnimationScript.From(StateDiff.Between(before, after)));

        Assert.Equal(AnimationCue.Play, step.Cue);
        Assert.Equal(P1Slot0, step.Slot);
    }

    [Fact]
    public void A_creature_leaving_with_no_arrival_is_a_destroy()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 1))
            .Build();
        var after = new StateBuilder().Build();

        var step = Assert.Single(AnimationScript.From(StateDiff.Between(before, after)));

        Assert.Equal(AnimationCue.Destroy, step.Cue);
        Assert.Equal(P1Slot0, step.Slot);
    }

    // The rejoin that the diff cannot do for itself: a move is reported as two separate slot
    // changes (one emptied, one filled) with nothing linking them, because StateDiff compares
    // slots independently and has no notion of creature identity across positions.
    [Fact]
    public void A_departure_and_a_matching_arrival_are_one_move_not_a_destroy_plus_a_play()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 2))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(1, "striker", TypeMask.Spike, maxHealth: 3, health: 2))
            .Build();

        var step = Assert.Single(AnimationScript.From(StateDiff.Between(before, after)));

        Assert.Equal(AnimationCue.Move, step.Cue);

        // Anchored at the destination: that is where the creature now is and where the player
        // should be looking once the action resolves.
        Assert.Equal(P1Slot1, step.Slot);
    }

    // The guard on the rejoin. A different card arriving as another leaves is two independent
    // events that merely happened in one action -- treating it as a move would draw a single
    // cue for a creature that never travelled.
    [Fact]
    public void A_departure_and_an_unrelated_arrival_stay_a_destroy_and_a_play()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 2))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(1, "monk", TypeMask.Anvil, maxHealth: 4, health: 4))
            .Build();

        var steps = AnimationScript.From(StateDiff.Between(before, after));

        Assert.Equal(2, steps.Count);
        Assert.Contains(steps, s => s.Cue == AnimationCue.Play && s.Slot == P1Slot1);
        Assert.Contains(steps, s => s.Cue == AnimationCue.Destroy && s.Slot == P1Slot0);
    }

    [Fact]
    public void Losing_health_is_damage_carrying_the_amount()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 5))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 2))
            .Build();

        var step = Assert.Single(AnimationScript.From(StateDiff.Between(before, after)));

        Assert.Equal(AnimationCue.Damage, step.Cue);
        Assert.Equal(3, step.Amount);
    }

    [Fact]
    public void Gaining_health_is_a_heal_carrying_the_amount()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 1))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 4))
            .Build();

        var step = Assert.Single(AnimationScript.From(StateDiff.Between(before, after)));

        Assert.Equal(AnimationCue.Heal, step.Cue);
        Assert.Equal(3, step.Amount);
    }

    [Fact]
    public void A_growing_merge_depth_is_a_merge()
    {
        var merged = new CreatureInstance("striker", maxHealth: 6, TypeMask.Spike, health: 6);
        merged.AbsorbMerge(
            new CreatureInstance("monk", maxHealth: 3, TypeMask.Anvil, health: 3),
            _ => 2);

        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 6, health: 6))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, merged))
            .Build();

        var steps = AnimationScript.From(StateDiff.Between(before, after));

        Assert.Contains(steps, s => s.Cue == AnimationCue.Merge && s.Slot == P1Slot0);
    }

    [Fact]
    public void A_score_increase_is_a_score_cue_for_that_player()
    {
        var before = new StateBuilder().P2(p => p.Score(1)).Build();
        var after = new StateBuilder().P2(p => p.Score(3)).Build();

        var step = Assert.Single(AnimationScript.From(StateDiff.Between(before, after)));

        Assert.Equal(AnimationCue.Score, step.Cue);
        Assert.Equal(PlayerId.Two, step.Player);
        Assert.Equal(2, step.Amount);

        // Score is not a board position -- it belongs to the player's status readout, so the
        // animator has nowhere on the board to draw it and must not guess a slot.
        Assert.Null(step.Slot);
    }

    // The ordering rule that matters most: a killing blow is damage AND a destroy, and drawing
    // the destroy first would animate the damage number over an already-empty slot.
    [Fact]
    public void Damage_is_ordered_before_the_destroy_it_caused()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 3))
            .P2(p => p.Slot(0, "monk", TypeMask.Anvil, maxHealth: 3, health: 1))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 1))
            .Build();

        var steps = AnimationScript.From(StateDiff.Between(before, after));

        var damageAt = steps.ToList().FindIndex(s => s.Cue == AnimationCue.Damage);
        var destroyAt = steps.ToList().FindIndex(s => s.Cue == AnimationCue.Destroy);

        Assert.True(damageAt >= 0 && destroyAt >= 0, "Expected both a damage and a destroy cue.");
        Assert.True(damageAt < destroyAt, "Damage must animate before the destroy it caused.");
    }

    [Fact]
    public void Score_is_ordered_last_after_every_board_cue()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 3).Score(0))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, "striker", TypeMask.Spike, maxHealth: 3, health: 1).Score(2))
            .Build();

        var steps = AnimationScript.From(StateDiff.Between(before, after));

        Assert.Equal(AnimationCue.Score, steps[^1].Cue);
    }

    // Two creatures hit by one spell must animate in a consistent order rather than whichever
    // order the diff happened to enumerate -- the sort is stable specifically to guarantee this.
    [Fact]
    public void Equal_ranked_cues_keep_a_stable_board_order()
    {
        var before = new StateBuilder()
            .P1(p => p
                .Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 5)
                .Slot(2, "monk", TypeMask.Anvil, maxHealth: 5, health: 5))
            .Build();
        var after = new StateBuilder()
            .P1(p => p
                .Slot(0, "striker", TypeMask.Spike, maxHealth: 5, health: 4)
                .Slot(2, "monk", TypeMask.Anvil, maxHealth: 5, health: 4))
            .Build();

        var steps = AnimationScript.From(StateDiff.Between(before, after));

        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.Equal(AnimationCue.Damage, s.Cue));
        Assert.Equal(P1Slot0, steps[0].Slot);
        Assert.Equal(P1Slot2, steps[1].Slot);
    }
}
