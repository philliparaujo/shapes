using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// DESIGN.md D4: which sounds one action makes. Same testability argument as ActionLogTests -- the
// mapping is a pure function of (action, diff), so it lives in the adapter and needs no editor.
//
// The two rules in SoundScript's header are what these pin: the action decides what the player
// DID, the diff decides what FOLLOWED, and no cue ever fires twice for one action.
public class SoundScriptTests
{
    private static readonly SlotIndex P1Slot0 = new(PlayerId.One, 0);

    // A state pair where nothing changed, for tests that care only about the action half.
    private static StateDiff NoChange()
    {
        var state = new StateBuilder().Build();
        return StateDiff.Between(state, state);
    }

    [Fact]
    public void Playing_a_card_sounds_the_card_cue()
    {
        var action = new PlayCardAction(PlayerId.One, TestCards.Bolt, targetSlot: null);

        Assert.Contains(SoundCue.CardPlay, SoundScript.From(NoChange(), action));
    }

    [Fact]
    public void Using_a_move_sounds_the_move_cue()
    {
        var action = new UseMoveAction(PlayerId.One, P1Slot0, moveIndex: 0);

        Assert.Contains(SoundCue.UseMove, SoundScript.From(NoChange(), action));
    }

    [Fact]
    public void Merging_sounds_the_merge_cue()
    {
        var action = new MergeAction(PlayerId.One, P1Slot0, new SlotIndex(PlayerId.One, 1));

        Assert.Contains(SoundCue.Merge, SoundScript.From(NoChange(), action));
    }

    // THE CASE A DIFF-ONLY VERSION GETS WRONG. A targetless spell can resolve without touching a
    // single slot -- no creature appears, no health moves -- so a mapping derived from slot changes
    // alone would play nothing at all for it. Reading the action is what makes this work, and this
    // test is the reason that decision is in the code.
    [Fact]
    public void A_spell_that_changes_no_slot_still_sounds_the_card_cue()
    {
        var action = new PlayCardAction(PlayerId.One, TestCards.Bolt, targetSlot: null);

        Assert.Equal([SoundCue.CardPlay], SoundScript.From(NoChange(), action));
    }

    // Turn-start income. The action is null here because nothing was chosen -- the state advanced
    // on its own, which is exactly the case SoundScript.From's `action` parameter documents.
    [Fact]
    public void Gaining_resources_sounds_the_resource_cue()
    {
        var before = new StateBuilder().P1(p => p.Resources(spike: 1)).Build();
        var after = new StateBuilder().P1(p => p.Resources(spike: 3)).Build();

        var cues = SoundScript.From(StateDiff.Between(before, after), action: null);

        Assert.Contains(SoundCue.GainResource, cues);
    }

    // SPENDING must not sound the gain cue -- it is the same field moving the other way, and the
    // obvious "did resources change" test would fire on every card played.
    [Fact]
    public void Spending_resources_does_not_sound_the_resource_cue()
    {
        var before = new StateBuilder().P1(p => p.Resources(spike: 3)).Build();
        var after = new StateBuilder().P1(p => p.Resources(spike: 1)).Build();

        var cues = SoundScript.From(StateDiff.Between(before, after), action: null);

        Assert.DoesNotContain(SoundCue.GainResource, cues);
    }

    [Fact]
    public void Scoring_sounds_the_hero_damage_cue()
    {
        var before = new StateBuilder().P1(p => p.Score(2)).Build();
        var after = new StateBuilder().P1(p => p.Score(3)).Build();

        var cues = SoundScript.From(StateDiff.Between(before, after), action: null);

        Assert.Contains(SoundCue.HeroDamage, cues);
    }

    // The rule from SoundScript's header: audio collapses per-slot events, because identical
    // samples starting on the same frame sum into one distorted noise rather than reading as
    // several events. Two creatures arriving at once is one card sound, not two.
    [Fact]
    public void One_action_never_sounds_the_same_cue_twice()
    {
        var before = new StateBuilder().Build();
        var after = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Spike)
                .Slot(1, TestCards.Striker, TypeMask.Spike))
            .Build();

        var action = new PlayCardAction(PlayerId.One, TestCards.Striker, P1Slot0);
        var cues = SoundScript.From(StateDiff.Between(before, after), action);

        Assert.Single(cues, cue => cue == SoundCue.CardPlay);
    }

    // EndTurn is the action that fires most often and has no sound of its own -- its consequences
    // (income, scoring) do. Pinned so a later edit cannot quietly give it a cue and put a noise on
    // every turn boundary.
    [Fact]
    public void Ending_a_turn_with_no_consequences_is_silent()
    {
        var cues = SoundScript.From(NoChange(), new EndTurnAction(PlayerId.One));

        Assert.Empty(cues);
    }

    // THE TURN-START COLLISION. Income and scoring resolve in the same engine step, so both cues
    // would otherwise fire on one frame and mask each other -- which is what happens in practice,
    // reported as "I only hear the resource sound". Scoring wins because it is the only event that
    // moves the win condition; see SoundScript's own note on why suppression beat offsetting.
    [Fact]
    public void Scoring_suppresses_the_income_cue_on_the_same_turn_start()
    {
        var before = new StateBuilder().P1(p => p.Resources(spike: 1).Score(2)).Build();
        var after = new StateBuilder().P1(p => p.Resources(spike: 3).Score(3)).Build();

        var cues = SoundScript.From(StateDiff.Between(before, after), action: null);

        Assert.Equal([SoundCue.HeroDamage], cues);
    }

    // The other half of that rule, and the one that keeps the suppression narrow: income is only
    // silenced when something is actually competing with it. Most turns do not score, and those
    // must still sound.
    [Fact]
    public void Income_still_sounds_on_a_turn_start_that_does_not_score()
    {
        var before = new StateBuilder().P1(p => p.Resources(spike: 1).Score(2)).Build();
        var after = new StateBuilder().P1(p => p.Resources(spike: 3).Score(2)).Build();

        var cues = SoundScript.From(StateDiff.Between(before, after), action: null);

        Assert.Equal([SoundCue.GainResource], cues);
    }
}
