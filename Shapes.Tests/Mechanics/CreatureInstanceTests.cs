using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Tests.Mechanics;

// Creature health, per-turn move tracking, and merging.
public class CreatureInstanceTests
{
    private static CreatureInstance Cadet(int maxHealth = 3, int? health = null) =>
        new("cadet", maxHealth, TypeMask.Wheel, health);

    [Fact]
    public void New_creature_starts_at_full_health()
    {
        var creature = Cadet(maxHealth: 4);

        Assert.Equal(4, creature.Health);
        Assert.Equal(4, creature.MaxHealth);
        Assert.False(creature.IsDamaged);
        Assert.False(creature.IsDead);
        Assert.False(creature.IsMerged);
        Assert.Equal(1, creature.MergeDepth);
    }

    [Fact]
    public void Creature_requires_a_type_and_positive_health()
    {
        Assert.Throws<ArgumentException>(() => new CreatureInstance("x", 3, TypeMask.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CreatureInstance("x", 0, TypeMask.Wheel));
        Assert.Throws<ArgumentException>(() => new CreatureInstance("  ", 3, TypeMask.Wheel));
    }

    [Fact]
    public void Damage_reduces_health_and_reports_what_it_dealt()
    {
        var creature = Cadet(maxHealth: 5);

        Assert.Equal(2, creature.TakeDamage(2));
        Assert.Equal(3, creature.Health);
        Assert.True(creature.IsDamaged);
    }

    [Fact]
    public void Overkill_reports_only_the_damage_actually_dealt()
    {
        // Effects that scale off damage dealt need the real figure, not the attempted one.
        var creature = Cadet(maxHealth: 2);

        Assert.Equal(2, creature.TakeDamage(10));
        Assert.Equal(0, creature.Health);
        Assert.True(creature.IsDead);
    }

    [Fact]
    public void Health_never_goes_negative()
    {
        var creature = Cadet(maxHealth: 1);
        creature.TakeDamage(99);

        Assert.Equal(0, creature.Health);
    }

    [Fact]
    public void Heal_caps_at_max_health()
    {
        var creature = Cadet(maxHealth: 5, health: 2);

        Assert.Equal(3, creature.Heal(10));
        Assert.Equal(5, creature.Health);
        Assert.False(creature.IsDamaged);
    }

    [Fact]
    public void Heal_on_a_full_creature_does_nothing()
    {
        var creature = Cadet(maxHealth: 3);

        Assert.Equal(0, creature.Heal(5));
        Assert.Equal(3, creature.Health);
    }

    [Fact]
    public void Buffing_max_health_raises_current_health_too()
    {
        // Otherwise a +health buff would leave the creature instantly "damaged", which reads
        // wrong and would trigger damage-dependent effects.
        var creature = Cadet(maxHealth: 3);
        creature.BuffMaxHealth(2);

        Assert.Equal(5, creature.Health);
        Assert.Equal(5, creature.MaxHealth);
        Assert.False(creature.IsDamaged);
    }

    [Fact]
    public void Moves_are_tracked_per_turn_and_independently()
    {
        var creature = Cadet();

        Assert.False(creature.HasUsedMove(0));
        creature.MarkMoveUsed(0);

        Assert.True(creature.HasUsedMove(0));
        Assert.False(creature.HasUsedMove(1));  // different moves are independent

        creature.ResetMovesForNewTurn();
        Assert.False(creature.HasUsedMove(0));
    }

    [Fact]
    public void Merging_sums_health_and_unions_typing()
    {
        var wheel = new CreatureInstance("cadet", 3, TypeMask.Wheel);
        var spike = new CreatureInstance("medic", 2, TypeMask.Spike);

        wheel.AbsorbMerge(spike);

        Assert.Equal(5, wheel.Health);
        Assert.Equal(5, wheel.MaxHealth);
        Assert.Equal(TypeMask.Wheel | TypeMask.Spike, wheel.Types);
        Assert.True(wheel.IsMerged);
        Assert.Equal(2, wheel.MergeDepth);
        Assert.Equal(["cadet", "medic"], wheel.MergedFrom);
    }

    [Fact]
    public void Merging_two_of_the_same_type_stays_single_type_but_counts_as_merged()
    {
        // The asymmetry worth remembering: a same-type merge gains stats with no defensive
        // downside, while a mixed merge opens a 2x exposure. Both are merge-locked.
        var a = new CreatureInstance("cadet", 3, TypeMask.Wheel);
        var b = new CreatureInstance("cadet", 3, TypeMask.Wheel);

        a.AbsorbMerge(b);

        Assert.Equal(TypeMask.Wheel, a.Types);
        Assert.False(a.Types.IsMultiType);
        Assert.True(a.IsMerged);
        Assert.Equal(6, a.Health);
    }

    [Fact]
    public void Merging_a_damaged_creature_carries_the_damage_over()
    {
        var healthy = new CreatureInstance("a", 4, TypeMask.Wheel);
        var hurt = new CreatureInstance("b", 4, TypeMask.Spike, health: 1);

        healthy.AbsorbMerge(hurt);

        Assert.Equal(5, healthy.Health);
        Assert.Equal(8, healthy.MaxHealth);
        Assert.True(healthy.IsDamaged);
    }

    [Fact]
    public void MergedFrom_is_the_move_list_in_concatenation_order()
    {
        // Moves are static card data looked up by id, not stored per creature, so MergedFrom
        // IS the move list: cards[a].Moves then cards[b].Moves. Order is the contract.
        var a = new CreatureInstance("cadet", 3, TypeMask.Wheel);
        a.AbsorbMerge(new CreatureInstance("medic", 2, TypeMask.Spike));

        Assert.Equal(["cadet", "medic"], a.MergedFrom);
    }

    [Fact]
    public void Move_index_offsets_follow_the_merge_order()
    {
        // Cadet has 2 moves, Medic has 3. In the merged creature Cadet occupies indices 0-1
        // and Medic 2-4. If these overlapped, two different moves would share a
        // once-per-turn bit -- a bug that is nearly invisible in play.
        var creature = new CreatureInstance("cadet", 3, TypeMask.Wheel);
        creature.AbsorbMerge(new CreatureInstance("medic", 2, TypeMask.Spike));

        int MoveCount(string id) => id switch { "cadet" => 2, "medic" => 3, _ => 0 };

        Assert.Equal(0, creature.MoveIndexOffset(0, MoveCount));
        Assert.Equal(2, creature.MoveIndexOffset(1, MoveCount));
    }

    [Fact]
    public void An_unmerged_creature_starts_its_moves_at_zero()
    {
        var creature = Cadet();

        Assert.Equal(0, creature.MoveIndexOffset(0, _ => 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => creature.MoveIndexOffset(1, _ => 2));
    }

    [Fact]
    public void Merged_creatures_track_each_source_cards_moves_separately()
    {
        // Using Cadet's move 0 must not mark Medic's move 0 (index 2) as used.
        var creature = new CreatureInstance("cadet", 3, TypeMask.Wheel);
        creature.AbsorbMerge(new CreatureInstance("medic", 2, TypeMask.Spike));

        int MoveCount(string id) => id switch { "cadet" => 2, "medic" => 3, _ => 0 };

        creature.MarkMoveUsed(creature.MoveIndexOffset(0, MoveCount) + 0);

        Assert.True(creature.HasUsedMove(0));
        Assert.False(creature.HasUsedMove(creature.MoveIndexOffset(1, MoveCount) + 0));
    }

    [Fact]
    public void Clone_is_independent()
    {
        var original = Cadet(maxHealth: 5);
        original.MarkMoveUsed(1);

        var copy = original.Clone();
        copy.TakeDamage(3);
        copy.MarkMoveUsed(2);

        Assert.Equal(5, original.Health);
        Assert.Equal(2, copy.Health);
        Assert.True(copy.HasUsedMove(1));       // state carried over
        Assert.False(original.HasUsedMove(2));  // later change did not leak back
    }

    [Fact]
    public void Clone_copies_the_merge_list_rather_than_sharing_it()
    {
        var original = new CreatureInstance("a", 3, TypeMask.Wheel);
        original.AbsorbMerge(new CreatureInstance("b", 3, TypeMask.Spike));

        var copy = original.Clone();
        copy.AbsorbMerge(new CreatureInstance("c", 3, TypeMask.Anvil));

        Assert.Equal(2, original.MergeDepth);
        Assert.Equal(3, copy.MergeDepth);
    }
}
