using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// PLAN.md D2 item 5. The log is a RENDERING of StateDiff, so what it says is a pure function of
// (action, diff, before-state, cards) -- which is exactly why it lives in the adapter and can be
// tested here, with no Godot scene and no editor, unlike the screenshot harnesses.
//
// These pin the FORMATTER, not the diff: StateDiffTests already covers what a diff contains, so
// each state pair below is built directly with StateBuilder to say what the log should make of it.
public class ActionLogTests
{
    private static CardDatabase Cards => TestCards.Database;

    [Fact]
    public void Damage_is_logged_with_the_creature_name_and_resulting_health()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 5, health: 5))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 5, health: 3))
            .Build();

        var effects = ActionLogEffects.Of(StateDiff.Between(before, after), before, Cards);

        Assert.Contains(effects, line => line.Contains("takes 2") && line.Contains("3/5"));
    }

    // Healing and damage are the same field moving opposite ways, and a heal is the one that is
    // invisible on a board showing only a current total -- so it gets its own assertion rather
    // than being assumed to fall out of the damage case.
    [Fact]
    public void Healing_is_logged_distinctly_from_damage()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 5, health: 2))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 5, health: 4))
            .Build();

        var effects = ActionLogEffects.Of(StateDiff.Between(before, after), before, Cards);

        Assert.Contains(effects, line => line.Contains("heals 2"));
        Assert.DoesNotContain(effects, line => line.Contains("takes"));
    }

    [Fact]
    public void A_destroyed_creature_is_logged_as_destroyed()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 2, health: 1))
            .Build();
        var after = new StateBuilder().Build();

        var effects = ActionLogEffects.Of(StateDiff.Between(before, after), before, Cards);

        Assert.Contains(effects, line => line.Contains("destroyed"));
    }

    // Spending is the common direction, and ResourcePool.Subtract THROWS on a negative result
    // (deliberately -- see its header). This pins that the formatter computes per-component deltas
    // instead, which is the bug the straightforward implementation would have.
    [Fact]
    public void Spending_resources_is_logged_as_a_negative_delta_and_does_not_throw()
    {
        var before = new StateBuilder().P1(p => p.Resources(spike: 3, wheel: 1)).Build();
        var after = new StateBuilder().P1(p => p.Resources(spike: 1, wheel: 1)).Build();

        var effects = ActionLogEffects.Of(StateDiff.Between(before, after), before, Cards);

        Assert.Contains(effects, line => line.Contains("-2 spike"));
        Assert.DoesNotContain(effects, line => line.Contains("wheel"));
    }

    [Fact]
    public void Drawing_a_card_is_logged_once_rather_than_as_three_count_changes()
    {
        var before = new StateBuilder().P1(p => p.Hand(TestCards.Bolt).Deck(TestCards.Striker)).Build();
        var after = new StateBuilder().P1(p => p.Hand(TestCards.Bolt, TestCards.Striker)).Build();

        var effects = ActionLogEffects.Of(StateDiff.Between(before, after), before, Cards);

        Assert.Contains(effects, line => line.Contains("draws 1"));

        // The deck line is suppressed when it merely mirrors the hand gaining a card -- otherwise
        // an ordinary draw prints the same event twice.
        Assert.DoesNotContain(effects, line => line.Contains("deck"));
    }

    [Fact]
    public void An_entry_records_the_turn_the_seat_and_the_described_action()
    {
        var before = new StateBuilder()
            .P1(p => p.Hand(TestCards.Bolt).Resources(wheel: 1))
            .Build();
        var after = new StateBuilder().P1(p => p.Resources(wheel: 1)).Build();
        var action = new PlayCardAction(PlayerId.One, TestCards.Bolt, targetSlot: null);

        var log = new ActionLog();
        log.Add(action, StateDiff.Between(before, after), before, after, Cards);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(PlayerId.One, entry.Player);
        Assert.Equal(ActionKind.PlayCard, entry.Kind);
        Assert.Equal(before.TurnNumber, entry.TurnNumber);

        // The card's NAME (these synthetic fixtures name themselves after their id, so the two
        // coincide here) and, the actual point, no restated cost: a log line is prose, not the
        // console's action-menu identity. See ActionLogText's header on why this is not ActionText.
        Assert.Contains("test_bolt", entry.Description);
        Assert.DoesNotContain("[", entry.Description);
    }

    // Slots are named the way a player would point at one. SlotIndex.ToString()'s "P2:1" is a
    // debugging identity and unreadable mid-sentence.
    [Fact]
    public void Slots_are_named_rather_than_printed_as_indices()
    {
        var before = new StateBuilder()
            .P1(p => p.Hand(TestCards.Striker).Resources(wheel: 5))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(1, TestCards.Striker, TypeMask.Wheel, health: 2))
            .Build();
        var action = new PlayCardAction(
            PlayerId.One, TestCards.Striker, new SlotIndex(PlayerId.One, 1));

        var log = new ActionLog();
        log.Add(action, StateDiff.Between(before, after), before, after, Cards);

        var entry = Assert.Single(log.Entries);
        Assert.Contains("middle slot", entry.Description);
        Assert.DoesNotContain("P1:1", entry.Description);
    }

    // The reason ActionLog.Add takes the BEFORE state: a lethal move destroys the very creature
    // whose move name has to be resolved, so describing against the after-state would lose it.
    [Fact]
    public void A_move_that_kills_its_own_creature_is_still_named_from_the_before_state()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, health: 2))
            .Build();
        var after = new StateBuilder().Build();
        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), moveIndex: 0);

        var log = new ActionLog();
        log.Add(action, StateDiff.Between(before, after), before, after, Cards);

        var entry = Assert.Single(log.Entries);
        Assert.Contains("Strike", entry.Description);
    }

    // THE HANDOVER SPLIT. One EndTurn Submit carries both the act of ending a go and the NEXT
    // seat's opening scoring/income/draw, because ActionExecutor.Apply runs AdvanceToActions()
    // internally. Filed under a single heading, those opening effects appeared above the turn line
    // that should contain them, credited to the player who had just finished.
    [Fact]
    public void Ending_a_turn_files_its_start_of_turn_effects_under_the_incoming_seat()
    {
        var before = new StateBuilder().P2(p => p.Resources(spike: 1)).Build();

        // Stands in for what AdvanceToActions produces: the seat has changed, the turn counter has
        // rolled over (P2 -> P1 is the handover that does that), and the incoming seat has income.
        var after = new StateBuilder().P2(p => p.Resources(spike: 3)).Build();
        after.SetTurnNumber(before.TurnNumber + 1);
        after.SetActivePlayer(PlayerId.Two);

        var log = new ActionLog();
        log.Add(new EndTurnAction(PlayerId.One), StateDiff.Between(before, after), before, after, Cards);

        Assert.Equal(2, log.Count);

        // The action itself stays in the turn it ended, credited to who ended it, with no effects.
        Assert.Equal(before.TurnNumber, log.Entries[0].TurnNumber);
        Assert.Equal(PlayerId.One, log.Entries[0].Player);
        Assert.Empty(log.Entries[0].Effects);

        // Its effects open the next turn and belong to the seat receiving it.
        Assert.Equal(after.TurnNumber, log.Entries[1].TurnNumber);
        Assert.Equal(PlayerId.Two, log.Entries[1].Player);
        Assert.Equal(ActionLog.TurnStartDescription, log.Entries[1].Description);
        Assert.NotEmpty(log.Entries[1].Effects);
    }

    // The P1 -> P2 handover does NOT bump the turn counter (a turn is both seats' go), but it does
    // run scoring/income/draw all the same. Keying the split off TurnNumber missed exactly this
    // case, leaving P2's scoring attributed to P1's End Turn line.
    [Fact]
    public void A_handover_within_one_turn_still_splits()
    {
        var before = new StateBuilder().P2(p => p.Resources(spike: 1)).Build();
        var after = new StateBuilder().P2(p => p.Resources(spike: 3)).Build();
        after.SetActivePlayer(PlayerId.Two);

        Assert.Equal(before.TurnNumber, after.TurnNumber);

        var log = new ActionLog();
        log.Add(new EndTurnAction(PlayerId.One), StateDiff.Between(before, after), before, after, Cards);

        Assert.Equal(2, log.Count);
        Assert.Equal(PlayerId.Two, log.Entries[1].Player);
        Assert.Equal(ActionLog.TurnStartDescription, log.Entries[1].Description);
    }

    // Only a turn-advancing action splits; everything else stays one entry.
    [Fact]
    public void An_ordinary_action_produces_exactly_one_entry()
    {
        var before = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 5, health: 5))
            .Build();
        var after = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 5, health: 3))
            .Build();
        var action = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), moveIndex: 0);

        var log = new ActionLog();
        log.Add(action, StateDiff.Between(before, after), before, after, Cards);

        Assert.Single(log.Entries);
        Assert.NotEmpty(log.Entries[0].Effects);
    }
}
