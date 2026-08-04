using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Actions;

// What applying an action actually does to the state.
//
// Paired with ActionGeneratorTests: that suite says which actions exist, this one says what they
// mean. Both matter -- a rule enforced only in generation would let a hand-constructed action
// (from a replay, a debug command, or a Phase 2 search shortcut) do something the rules forbid.
public class ActionExecutorTests
{
    private static void Apply(GameState state, GameAction action) =>
        ActionExecutor.Apply(state, TestCards.Database, action);

    // -- Playing cards ---------------------------------------------------------------------

    [Fact]
    public void Playing_a_creature_places_it_and_pays_its_cost()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Striker).Resources(wheel: 3))
            .Build();

        var slot = new SlotIndex(PlayerId.One, 1);
        Apply(state, new PlayCardAction(PlayerId.One, TestCards.Striker, slot));

        var creature = state.Board[slot];
        Assert.NotNull(creature);
        Assert.Equal(TestCards.Striker, creature.CardId);
        Assert.Equal(2, creature.Health);
        Assert.Equal(2, state[PlayerId.One].Resources[ResourceType.Wheel]);
        Assert.Empty(state[PlayerId.One].Hand);
    }

    [Fact]
    public void Playing_one_copy_leaves_the_other_in_hand()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Striker, TestCards.Striker).Resources(wheel: 3))
            .Build();

        Apply(state, new PlayCardAction(
            PlayerId.One, TestCards.Striker, new SlotIndex(PlayerId.One, 0)));

        Assert.Single(state[PlayerId.One].Hand);
    }

    [Fact]
    public void A_played_creature_enters_at_full_health()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.TwoMove).Resources(spike: 3))
            .Build();

        var slot = new SlotIndex(PlayerId.One, 0);
        Apply(state, new PlayCardAction(PlayerId.One, TestCards.TwoMove, slot));

        var creature = state.Board[slot]!;
        Assert.Equal(creature.MaxHealth, creature.Health);
    }

    [Fact]
    public void Playing_a_spell_resolves_it_and_sends_it_to_discard()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Bolt).Deck("x", "y").Resources(wheel: 3))
            .Build();

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.Bolt));

        var player = state[PlayerId.One];
        Assert.Contains(TestCards.Bolt, player.Discard);
        Assert.Single(player.Hand);          // drew one card
        Assert.Equal("x", player.Hand[0]);
    }

    [Fact]
    public void A_spell_never_occupies_a_slot()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Bolt).Deck("x").Resources(wheel: 3))
            .Build();

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.Bolt));

        Assert.All(SlotIndex.AllFor(PlayerId.One), s => Assert.True(state.Board.IsEmpty(s)));
    }

    [Fact]
    public void A_targeted_spell_damages_the_chosen_creature_only()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.TargetedBolt).Resources(wheel: 3))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 5)
                .Slot(1, TestCards.Striker, TypeMask.Wheel, maxHealth: 5))
            .Build();

        var target = new SlotIndex(PlayerId.Two, 1);
        Apply(state, new PlayCardAction(
            PlayerId.One, TestCards.TargetedBolt, targetSlot: null, chosenTarget: target));

        Assert.Equal(5, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
        Assert.Equal(3, state.Board[target]!.Health);
    }

    [Fact]
    public void An_unaffordable_action_throws_rather_than_playing_for_free()
    {
        // The generator/executor contract: the executor assumes legality, so an illegal action
        // reaching it must fail loudly. Silently clamping the payment would hide the real bug --
        // a generator that offered an unpayable action -- and would hand out free plays.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Costly).Resources(anvil: 1))
            .Build();

        Assert.ThrowsAny<Exception>(() => Apply(
            state, new PlayCardAction(PlayerId.One, TestCards.Costly, new SlotIndex(PlayerId.One, 0))));
    }

    // -- Using moves -----------------------------------------------------------------------

    [Fact]
    public void Using_a_move_pays_its_cost_and_resolves_its_effects()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 3))
            .P2(p => p.Slot(0, TestCards.TwoMove, TypeMask.Spike, maxHealth: 4))
            .Build();

        Apply(state, new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0));

        Assert.Equal(2, state[PlayerId.One].Resources[ResourceType.Wheel]);
        Assert.Equal(3, state.Board[new SlotIndex(PlayerId.Two, 0)]!.Health);
    }

    [Fact]
    public void Using_a_move_marks_it_used_for_the_turn()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Spike).Resources(spike: 3))
            .Build();

        var slot = new SlotIndex(PlayerId.One, 0);
        Apply(state, new UseMoveAction(PlayerId.One, slot, 1));

        var creature = state.Board[slot]!;
        Assert.True(creature.HasUsedMove(1));
        Assert.False(creature.HasUsedMove(0));
    }

    [Fact]
    public void A_lethal_move_clears_the_slot()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 3))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 1))
            .Build();

        Apply(state, new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0));

        // The sweep runs once per action, after the whole effect list -- so a creature that
        // reached 0 is gone by the time the action returns.
        Assert.True(state.Board.IsEmpty(new SlotIndex(PlayerId.Two, 0)));
    }

    [Fact]
    public void A_move_with_no_opposing_creature_still_resolves()
    {
        // "opposing" resolving to nothing is a no-op, not a crash. The cost is still paid --
        // the player chose to use the move.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 3))
            .Build();

        Apply(state, new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0));

        Assert.Equal(2, state[PlayerId.One].Resources[ResourceType.Wheel]);
    }

    [Fact]
    public void A_move_index_out_of_range_throws()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 3))
            .Build();

        Assert.Throws<InvalidOperationException>(() => Apply(
            state, new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 7)));
    }

    // -- Merging ---------------------------------------------------------------------------

    [Fact]
    public void Merging_sums_health_unions_types_and_frees_the_source_slot()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Resources(wheel: 3))
            .Build();

        var source = new SlotIndex(PlayerId.One, 0);
        var target = new SlotIndex(PlayerId.One, 1);
        Apply(state, new MergeAction(PlayerId.One, source, target));

        var merged = state.Board[target]!;
        Assert.True(state.Board.IsEmpty(source));
        Assert.Equal(5, merged.Health);
        Assert.Equal(5, merged.MaxHealth);
        Assert.True(merged.Types.Has(ResourceType.Wheel));
        Assert.True(merged.Types.Has(ResourceType.Spike));
        Assert.True(merged.IsMerged);
    }

    [Fact]
    public void Merging_costs_no_resources()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel)
                .Resources(spike: 2, anvil: 2, wheel: 2))
            .Build();

        var before = state[PlayerId.One].Resources;
        Apply(state, new MergeAction(
            PlayerId.One, new SlotIndex(PlayerId.One, 0), new SlotIndex(PlayerId.One, 1)));

        Assert.Equal(before, state[PlayerId.One].Resources);
    }

    [Fact]
    public void Merging_does_not_end_the_turn()
    {
        // Merging is a free action: the player keeps acting afterwards.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.Striker, TypeMask.Wheel)
                .Resources(wheel: 5))
            .Build();

        Apply(state, new MergeAction(
            PlayerId.One, new SlotIndex(PlayerId.One, 0), new SlotIndex(PlayerId.One, 1)));

        Assert.Equal(PlayerId.One, state.ActivePlayer);
        Assert.Equal(TurnPhase.Actions, state.Phase);
    }

    [Fact]
    public void A_merged_creature_offers_both_source_cards_moves()
    {
        // Move lists union, which is the whole point of merging. Asserted through the generated
        // action list, since the concatenated indexing is what callers actually consume.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike)
                .Resources(spike: 5, wheel: 5))
            .Build();

        Apply(state, new MergeAction(
            PlayerId.One, new SlotIndex(PlayerId.One, 0), new SlotIndex(PlayerId.One, 1)));

        var moves = ActionGenerator.Generate(state, TestCards.Database).OfType<UseMoveAction>().ToList();

        // Striker's 1 move plus TwoMove's 2, all on the surviving creature.
        Assert.Equal(3, moves.Count);
        Assert.Equal([0, 1, 2], moves.Select(m => m.MoveIndex).OrderBy(i => i));
    }

    [Fact]
    public void Each_move_of_a_merged_creature_is_independently_once_per_turn()
    {
        // The bitmask indexes the CONCATENATED list, so two source cards' moves must not share
        // a bit. This is the failure MoveIndexOffset exists to prevent, and it is invisible
        // without a merged creature to test it on.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike)
                .Resources(spike: 5, wheel: 5))
            .Build();

        var target = new SlotIndex(PlayerId.One, 1);
        Apply(state, new MergeAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), target));

        Apply(state, new UseMoveAction(PlayerId.One, target, 0));

        var remaining = ActionGenerator.Generate(state, TestCards.Database)
            .OfType<UseMoveAction>().Select(m => m.MoveIndex).OrderBy(i => i).ToList();

        Assert.Equal([1, 2], remaining);
    }

    // -- Ending the turn -------------------------------------------------------------------

    [Fact]
    public void Ending_the_turn_draws_and_passes_to_the_opponent()
    {
        var state = new StateBuilder()
            .P1(p => p.Deck("a", "b"))
            .Build();

        Apply(state, new EndTurnAction(PlayerId.One));

        Assert.Equal(PlayerId.Two, state.ActivePlayer);
        Assert.Single(state[PlayerId.One].Hand);
        Assert.Equal("a", state[PlayerId.One].Hand[0]);
    }

    [Fact]
    public void Ending_the_turn_on_an_empty_deck_draws_nothing_and_does_not_throw()
    {
        // Deck exhaustion is deliberately not fatal: the player simply gets nothing.
        var state = new StateBuilder().Build();

        Apply(state, new EndTurnAction(PlayerId.One));

        Assert.Empty(state[PlayerId.One].Hand);
        Assert.Equal(PlayerId.Two, state.ActivePlayer);
    }

    [Fact]
    public void Ending_the_turn_discards_down_to_the_hand_limit()
    {
        var rules = RuleSetTestHelper.WithHandLimit(4);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p.Hand("a", "b", "c", "d").Deck("e"))
            .Build();

        Apply(state, new EndTurnAction(PlayerId.One));

        // Drew to 5, then discarded back to the limit of 4. Draw-then-discard is the order that
        // makes the limit bite rather than being skipped.
        Assert.Equal(4, state[PlayerId.One].Hand.Count);
        Assert.Single(state[PlayerId.One].Discard);
    }

    [Fact]
    public void A_hand_within_the_limit_is_not_discarded_from()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand("a", "b"))
            .Build();

        Apply(state, new EndTurnAction(PlayerId.One));

        Assert.Empty(state[PlayerId.One].Discard);
    }

    [Fact]
    public void Ending_the_turn_resets_the_opponents_move_usage_on_their_next_turn()
    {
        // EndTurn resets the ENDING player's creatures, so their moves are fresh when play
        // returns to them.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.TwoMove, TypeMask.Spike).Resources(spike: 5))
            .Build();

        var slot = new SlotIndex(PlayerId.One, 0);
        Apply(state, new UseMoveAction(PlayerId.One, slot, 0));
        Apply(state, new EndTurnAction(PlayerId.One));

        Assert.False(state.Board[slot]!.HasUsedMove(0));
    }

    [Fact]
    public void Ending_the_turn_scores_and_pays_income_for_the_new_active_player()
    {
        // The turn loop: EndTurn must not merely pass the turn -- it drives the new active
        // player's Scoring and Income phases immediately, landing back in Actions, so a caller
        // never has to notice the intermediate phases or call ApplyScoring/ApplyIncome itself.
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Slot(0, "a", TypeMask.Wheel))
            .P2(p => p.Slot(1, "b", TypeMask.Spike).Score(2))
            .Build();

        Apply(state, new EndTurnAction(PlayerId.One));

        Assert.Equal(PlayerId.Two, state.ActivePlayer);
        Assert.Equal(TurnPhase.Actions, state.Phase);
        // P2's unopposed spike creature scored, then income landed (base 1/1/1 + 1 spike).
        Assert.Equal(3, state[PlayerId.Two].Score);
        Assert.Equal(new ResourcePool(2, 1, 1), state[PlayerId.Two].Resources);
    }

    [Fact]
    public void Ending_the_turn_into_the_opponents_win_leaves_the_game_over()
    {
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P2(p => p.Slot(0, "b", TypeMask.Spike).Score(RuleSet.Default.ScoreToWin - 1))
            .Build();

        Apply(state, new EndTurnAction(PlayerId.One));

        Assert.True(state.IsOver);
        Assert.Equal(PlayerId.Two, state.Winner);
        Assert.Equal(TurnPhase.Ended, state.Phase);
    }

    // -- Turn-loop plumbing: PlayCost capture and the turn event log ------------------------

    [Fact]
    public void Playing_a_creature_captures_its_play_cost_on_the_instance()
    {
        // destroy_refund_cost reads this later without a CardDatabase lookup -- see Suffocate.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Striker).Resources(wheel: 3))
            .Build();
        var slot = new SlotIndex(PlayerId.One, 0);

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.Striker, slot));

        Assert.Equal(new ResourcePool(0, 0, 1), state.Board[slot]!.PlayCost);
    }

    [Fact]
    public void Playing_a_creature_records_a_turn_event()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Striker).Resources(wheel: 3))
            .Build();
        var slot = new SlotIndex(PlayerId.One, 0);

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.Striker, slot));

        Assert.Contains(state.TurnEvents, e =>
            e.Kind == TurnEventKind.CreaturePlayed && e.Slot == slot && e.CardId == TestCards.Striker);
    }

    [Fact]
    public void A_lethal_move_records_a_destroyed_turn_event_via_the_dead_sweep()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel).Resources(wheel: 3))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 1))
            .Build();

        Apply(state, new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 0));

        Assert.Contains(state.TurnEvents, e =>
            e.Kind == TurnEventKind.CreatureDestroyed && e.Slot == new SlotIndex(PlayerId.Two, 0));
    }

    [Fact]
    public void Ending_the_turn_clears_the_turn_event_log()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Striker).Resources(wheel: 3))
            .Build();
        Apply(state, new PlayCardAction(PlayerId.One, TestCards.Striker, new SlotIndex(PlayerId.One, 0)));
        Assert.NotEmpty(state.TurnEvents);

        Apply(state, new EndTurnAction(PlayerId.One));

        Assert.Empty(state.TurnEvents);
    }

    [Fact]
    public void Playing_a_spell_computes_hand_composition_from_the_remaining_hand()
    {
        // The Rally shape: ActionExecutor must precompute EffectContext.HandComposition from
        // CardDatabase before the effect runs, since Shapes.Core.Effects itself has no Cards
        // dependency to compute it. The card being played is already removed from hand by the
        // time this runs (see ApplyPlayCard), so it must not count itself.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.RallyLike, TestCards.Striker, TestCards.TwoMove)
                      .Resources(wheel: 3, spike: 3))
            .Build();

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.RallyLike));

        // RallyLike costs 1 wheel to play. HandComposition[Spike] counts hand cards whose cost
        // includes spike -- faithful to Rally's real text ("per SPIKE card in hand"), not just
        // hand size. Of the two cards left in hand after RallyLike is removed, only TwoMove
        // (spike-cost) qualifies; Striker costs wheel. So gain 2 * 1 = 2, added to the 3 already
        // held.
        Assert.Equal(2, state[PlayerId.One].Resources.Wheel);
        Assert.Equal(5, state[PlayerId.One].Resources.Spike);
    }

    // -- Action identity -------------------------------------------------------------------

    [Fact]
    public void Actions_describing_the_same_choice_are_equal()
    {
        // Value equality is what keeps the search from creating duplicate children for
        // identical moves, splitting its statistics across them.
        var a = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 1);
        var b = new UseMoveAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), 1);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Actions_differing_in_chosen_target_are_not_equal()
    {
        var a = new UseMoveAction(
            PlayerId.One, new SlotIndex(PlayerId.One, 0), 0, new SlotIndex(PlayerId.Two, 0));
        var b = new UseMoveAction(
            PlayerId.One, new SlotIndex(PlayerId.One, 0), 0, new SlotIndex(PlayerId.Two, 1));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Actions_of_different_kinds_are_never_equal()
    {
        GameAction end = new EndTurnAction(PlayerId.One);
        GameAction merge = new MergeAction(
            PlayerId.One, new SlotIndex(PlayerId.One, 0), new SlotIndex(PlayerId.One, 1));

        Assert.NotEqual(end, merge);
    }

    [Fact]
    public void Merging_a_creature_with_itself_is_rejected_at_construction()
    {
        var slot = new SlotIndex(PlayerId.One, 0);

        Assert.Throws<ArgumentException>(() => new MergeAction(PlayerId.One, slot, slot));
    }
}
