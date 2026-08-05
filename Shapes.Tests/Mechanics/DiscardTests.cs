using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Mechanics;

// Discarding and turn-start drawing.
//
// Two rules that look similar and are deliberately NOT the same:
//
//   OVERDRAW BURNS.        A card drawn into a full hand goes straight to the discard pile. No
//                          choice, no prompt -- you cannot keep it by pitching something worse.
//   `discard N` CHOOSES.   A card effect saying "discard 2" makes the player pick which two,
//                          one card at a time, through DiscardAction.
//
// The asymmetry is the point (Hearthstone/Slay the Spire): a full hand is a real cost, while a
// card that asks you to discard is asking a real question. Tests below pin both halves, since
// collapsing them in either direction is the plausible regression.
public class DiscardTests
{
    private static void Apply(GameState state, GameAction action) =>
        ActionExecutor.Apply(state, TestCards.Database, action);

    private static IReadOnlyList<GameAction> Actions(GameState state) =>
        ActionGenerator.Generate(state, TestCards.Database);

    // -- Turn-start draw ---------------------------------------------------------------------

    [Fact]
    public void Drawing_happens_at_the_start_of_a_turn_not_the_end()
    {
        // The card drawn is available DURING the turn it was drawn, which is the whole reason
        // the draw moved. A draw at turn end would leave it unplayable until the next turn.
        var state = new StateBuilder()
            .Phase(TurnPhase.Scoring)
            .P1(p => p.Deck("a", "b"))
            .Build();

        state.AdvanceToActions();

        Assert.Equal(TurnPhase.Actions, state.Phase);
        Assert.Single(state[PlayerId.One].Hand);
        Assert.Equal("a", state[PlayerId.One].Hand[0]);
    }

    [Fact]
    public void The_draw_phase_sits_between_income_and_actions()
    {
        // Pins the order score -> income -> draw -> actions. Drawing before income would let a
        // card drawn this turn be paid for by income it should have preceded.
        var state = new StateBuilder()
            .Phase(TurnPhase.Scoring)
            .P1(p => p.Deck("a"))
            .Build();

        state.ApplyScoring();
        Assert.Equal(TurnPhase.Income, state.Phase);

        state.ApplyIncome();
        Assert.Equal(TurnPhase.Draw, state.Phase);
        Assert.Empty(state[PlayerId.One].Hand);

        state.ApplyDraw();
        Assert.Equal(TurnPhase.Actions, state.Phase);
        Assert.Single(state[PlayerId.One].Hand);
    }

    [Fact]
    public void A_scoring_play_that_wins_skips_both_income_and_the_draw()
    {
        // The win check sits between scoring and income, so a turn that wins never reaches the
        // draw phase. Worth pinning now that draw is part of the start-of-turn sequence: it
        // would otherwise be easy to append the draw after the early return and hand a card to
        // a player whose game had already ended.
        var rules = RuleSetTestHelper.WithScoreToWin(1);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .Phase(TurnPhase.Scoring)
            .P1(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2).Deck("a"))
            .Build();

        state.AdvanceToActions();

        Assert.True(state.IsOver);
        Assert.Equal(TurnPhase.Ended, state.Phase);
        Assert.Empty(state[PlayerId.One].Hand);
    }

    [Fact]
    public void Drawing_on_an_empty_deck_is_not_fatal()
    {
        // Deck exhaustion gives the player nothing. No damage, no loss -- unchanged by the move
        // to turn-start drawing.
        var state = new StateBuilder()
            .Phase(TurnPhase.Scoring)
            .Build();

        state.AdvanceToActions();

        Assert.Equal(TurnPhase.Actions, state.Phase);
        Assert.Empty(state[PlayerId.One].Hand);
    }

    // -- Overdraw burns ----------------------------------------------------------------------

    [Fact]
    public void A_card_drawn_into_a_full_hand_is_burned()
    {
        // HandLimit 4 is the floor RuleSet allows (it may not sit below StartingHandSize).
        var rules = RuleSetTestHelper.WithHandLimit(4);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .Phase(TurnPhase.Scoring)
            .P1(p => p.Hand("a", "b", "c", "d").Deck("e"))
            .Build();

        state.AdvanceToActions();

        // Hand stays at the limit and the DRAWN card is the one burned -- not an older one.
        // Burning "e" rather than "a" is what makes this a burn rather than a hand-limit
        // discard the player might reasonably have wanted to direct.
        Assert.Equal(["a", "b", "c", "d"], state[PlayerId.One].Hand);
        Assert.Equal(["e"], state[PlayerId.One].Discard);
    }

    [Fact]
    public void Burning_an_overdrawn_card_asks_the_player_nothing()
    {
        // The critical difference from `discard N`: no pending state, no gate on the action
        // list. Overdraw happens constantly, and routing it through the generator would put a
        // discard prompt in front of the player most turns.
        var rules = RuleSetTestHelper.WithHandLimit(4);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .Phase(TurnPhase.Scoring)
            .P1(p => p
                .Hand(TestCards.Bolt, TestCards.Bolt, TestCards.Bolt, TestCards.Bolt)
                .Deck(TestCards.Striker))
            .Build();

        state.AdvanceToActions();

        Assert.False(state.AwaitingDiscard);
        Assert.Single(state[PlayerId.One].Discard);
        Assert.Contains(Actions(state), a => a.Kind == ActionKind.EndTurn);
    }

    [Fact]
    public void A_card_effect_that_draws_into_a_full_hand_also_burns()
    {
        // The hand limit is a property of DRAWING, not of the turn step. A `draw` effect on a
        // card burns exactly as the turn draw does -- otherwise a hand could sit permanently
        // over the limit, which is precisely what the fuzz harness caught when this op still
        // called PlayerState.Draw directly.
        // Five cards against a limit of 4. Playing the Bolt removes it from hand first, leaving
        // exactly 4 -- so its "draw 1" lands into a hand already AT the limit and burns. Sizing
        // this correctly matters: with four cards to start, playing one leaves room and nothing
        // would burn.
        var rules = RuleSetTestHelper.WithHandLimit(4);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .P1(p => p
                .Hand(TestCards.Bolt, TestCards.Gated, TestCards.Chooser, TestCards.Costly,
                      TestCards.TwoMove)
                .Deck(TestCards.Striker)
                .Resources(wheel: 5))
            .Build();

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.Bolt));

        Assert.Equal(4, state[PlayerId.One].Hand.Count);
        Assert.DoesNotContain(TestCards.Striker, state[PlayerId.One].Hand);
        Assert.Contains(TestCards.Striker, state[PlayerId.One].Discard);
    }

    [Fact]
    public void A_burned_card_is_logged_as_a_turn_event()
    {
        // Phase 4 measures how often the hand limit actually costs a card. Inferring that from
        // hand sizes after the fact is not possible, so the burn is logged when it happens.
        var rules = RuleSetTestHelper.WithHandLimit(4);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .Phase(TurnPhase.Scoring)
            .P1(p => p.Hand("a", "b", "c", "d").Deck("e"))
            .Build();

        state.AdvanceToActions();

        Assert.Contains(
            state.TurnEvents,
            e => e.Kind == TurnEventKind.CardBurned && e.CardId == "e");
    }

    [Fact]
    public void A_hand_under_the_limit_keeps_the_drawn_card()
    {
        var rules = RuleSetTestHelper.WithHandLimit(4);
        var state = new StateBuilder()
            .WithRuleSet(rules)
            .Phase(TurnPhase.Scoring)
            .P1(p => p.Hand("a", "b").Deck("c"))
            .Build();

        state.AdvanceToActions();

        Assert.Equal(["a", "b", "c"], state[PlayerId.One].Hand);
        Assert.Empty(state[PlayerId.One].Discard);
    }

    // -- Chosen discard: the pending state ----------------------------------------------------

    [Fact]
    public void A_pending_discard_offers_one_action_per_distinct_card()
    {
        var state = PendingDiscardState(count: 1, hand: ["a", "b", "c"]);

        var actions = Actions(state);

        Assert.Equal(3, actions.Count);
        Assert.All(actions, a => Assert.Equal(ActionKind.Discard, a.Kind));
        Assert.Equal(
            ["a", "b", "c"],
            actions.Cast<DiscardAction>().Select(a => a.CardId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Duplicate_cards_in_hand_collapse_to_one_discard_action()
    {
        // Two copies of a card are the same choice. Offering both would split the search's
        // statistics across identical edges -- the same reason playing a card collapses copies.
        var state = PendingDiscardState(count: 1, hand: ["a", "a", "b"]);

        var actions = Actions(state);

        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public void A_pending_discard_suppresses_every_other_action_including_end_turn()
    {
        // The gate is what makes the debt a cost rather than a suggestion: a player who could
        // simply end the turn would never pay it.
        var state = PendingDiscardState(count: 1, hand: ["a"], resources: 5, alsoInHand: TestCards.Striker);

        var actions = Actions(state);

        Assert.All(actions, a => Assert.Equal(ActionKind.Discard, a.Kind));
        Assert.DoesNotContain(actions, a => a.Kind == ActionKind.EndTurn);
        Assert.DoesNotContain(actions, a => a.Kind == ActionKind.PlayCard);
    }

    [Fact]
    public void Discarding_moves_the_chosen_card_and_pays_down_the_debt()
    {
        var state = PendingDiscardState(count: 1, hand: ["a", "b"]);

        Apply(state, new DiscardAction(PlayerId.One, "b"));

        Assert.Equal(["a"], state[PlayerId.One].Hand);
        Assert.Equal(["b"], state[PlayerId.One].Discard);
        Assert.False(state.AwaitingDiscard);
    }

    [Fact]
    public void Discarding_the_last_owed_card_returns_play_to_normal()
    {
        // Real card ids here, unlike the tests above: once the debt clears the generator resumes
        // enumerating plays, which means looking every hand card up in the database.
        var state = PendingDiscardState(count: 1, hand: [TestCards.Bolt, TestCards.Striker]);

        Apply(state, new DiscardAction(PlayerId.One, TestCards.Bolt));

        Assert.Contains(Actions(state), a => a.Kind == ActionKind.EndTurn);
    }

    [Fact]
    public void Discarding_several_cards_narrows_the_choice_one_card_at_a_time()
    {
        // PLAN.md's stated shape: a four-card hand owing 3 offers 4 options, then 3, then 2,
        // then ordinary play resumes. Linear in hand size rather than binomial in combinations.
        string[] hand = [TestCards.Bolt, TestCards.Striker, TestCards.Chooser, TestCards.Gated];
        var state = PendingDiscardState(count: 3, hand: hand);

        Assert.Equal(4, Actions(state).Count);
        Apply(state, new DiscardAction(PlayerId.One, TestCards.Bolt));

        Assert.Equal(3, Actions(state).Count);
        Apply(state, new DiscardAction(PlayerId.One, TestCards.Striker));

        Assert.Equal(2, Actions(state).Count);
        Apply(state, new DiscardAction(PlayerId.One, TestCards.Chooser));

        Assert.False(state.AwaitingDiscard);
        Assert.Equal([TestCards.Gated], state[PlayerId.One].Hand);
        Assert.Equal(
            [TestCards.Bolt, TestCards.Striker, TestCards.Chooser],
            state[PlayerId.One].Discard);
        Assert.Contains(Actions(state), a => a.Kind == ActionKind.EndTurn);
    }

    // -- Chosen discard: unpayable debts -------------------------------------------------------

    [Fact]
    public void A_debt_larger_than_the_hand_costs_only_what_is_held()
    {
        // "Discard 5" with one card discards that one and forgets the rest. Carrying the debt
        // would let a card tax a future turn; leaving it outstanding would deadlock the
        // generator, which offers nothing but discards while a debt stands.
        var state = PendingDiscardState(count: 5, hand: [TestCards.Bolt]);

        Apply(state, new DiscardAction(PlayerId.One, TestCards.Bolt));

        Assert.False(state.AwaitingDiscard);
        Assert.Empty(state[PlayerId.One].Hand);
        Assert.Contains(Actions(state), a => a.Kind == ActionKind.EndTurn);
    }

    [Fact]
    public void A_discard_effect_with_an_empty_hand_never_gates_the_turn()
    {
        // The deadlock case: a debt that can never be paid, with no cards to offer. The clamp
        // has to run at the moment the debt is incurred, or Generate would return an empty list
        // and the game would stop.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.DiscardTwo).Resources(wheel: 5))
            .Build();

        // Playing the card empties the hand (it is the only card) and then owes 2.
        Apply(state, new PlayCardAction(PlayerId.One, TestCards.DiscardTwo));

        Assert.False(state.AwaitingDiscard);
        Assert.NotEmpty(Actions(state));
        Assert.Contains(Actions(state), a => a.Kind == ActionKind.EndTurn);
    }

    [Fact]
    public void Discard_two_with_one_card_empties_the_hand_and_never_asks_again()
    {
        // The full "discard 2 with 1 card" story, end to end: the one card is discarded, the
        // hand is empty, the debt is gone, play resumes, and -- the part worth pinning -- the
        // remainder never resurfaces. Ending the turn and coming back around must not present a
        // discard the player still "owes" from last turn.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.DiscardTwo, TestCards.Bolt).Resources(wheel: 5))
            .P2(p => p.Resources(wheel: 5))
            .Build();

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.DiscardTwo));

        // One card left, so the debt of 2 was clamped to 1 and is offered as a single choice.
        Assert.Equal(1, state.PendingDiscards);
        var offered = Actions(state);
        Assert.Single(offered);
        Assert.Equal(new DiscardAction(PlayerId.One, TestCards.Bolt), offered[0]);

        Apply(state, offered[0]);

        // Hand empty, debt settled, ordinary play available again.
        Assert.Empty(state[PlayerId.One].Hand);
        Assert.False(state.AwaitingDiscard);
        Assert.Contains(Actions(state), a => a.Kind == ActionKind.EndTurn);

        // Round-trip the turn: player one must not be asked for a leftover discard on their
        // next turn. (Their draw at turn start refills the hand, so a lingering debt WOULD have
        // something to take -- which is exactly why this needs asserting rather than assuming.)
        Apply(state, new EndTurnAction(PlayerId.One));
        Apply(state, new EndTurnAction(PlayerId.Two));

        Assert.Equal(PlayerId.One, state.ActivePlayer);
        Assert.False(state.AwaitingDiscard);
        Assert.Contains(Actions(state), a => a.Kind == ActionKind.EndTurn);
    }

    [Fact]
    public void Discard_two_with_an_empty_hand_asks_for_nothing_at_all()
    {
        // The 0-card case stated as its own scenario: the debt is clamped to zero the moment it
        // is incurred, so not even one discard action is offered and the turn continues
        // uninterrupted.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.DiscardTwo).Resources(wheel: 5))
            .P2(p => p.Resources(wheel: 5))
            .Build();

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.DiscardTwo));

        Assert.Equal(0, state.PendingDiscards);
        Assert.Empty(state[PlayerId.One].Hand);
        Assert.DoesNotContain(Actions(state), a => a.Kind == ActionKind.Discard);

        Apply(state, new EndTurnAction(PlayerId.One));
        Apply(state, new EndTurnAction(PlayerId.Two));

        Assert.False(state.AwaitingDiscard);
        Assert.DoesNotContain(Actions(state), a => a.Kind == ActionKind.Discard);
    }

    [Fact]
    public void A_discard_effect_owing_more_than_the_hand_holds_is_clamped_on_resolution()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.DiscardTwo, TestCards.Bolt).Resources(wheel: 5))
            .Build();

        Apply(state, new PlayCardAction(PlayerId.One, TestCards.DiscardTwo));

        // One card left in hand, so the debt of 2 is clamped to 1.
        Assert.Equal(1, state.PendingDiscards);
        Assert.Single(Actions(state));
    }

    // -- Interaction with the turn loop --------------------------------------------------------

    [Fact]
    public void A_discard_debt_does_not_survive_into_the_opponents_turn()
    {
        // Defensive: the generator will not offer EndTurn while a debt stands, so this is only
        // reachable through a hand-built state or a replay. Clearing it means a stale debt can
        // never gate the opponent into paying for someone else's card.
        var state = new StateBuilder()
            .P1(p => p.Hand("a"))
            .Build();

        state.AddPendingDiscards(1);
        state.EndTurn();

        Assert.False(state.AwaitingDiscard);
    }

    [Fact]
    public void Discarding_is_reflected_in_a_cloned_state()
    {
        // PendingDiscards is part of the position, so a search clone must carry it -- otherwise
        // a rollout would explore a state where the debt had silently vanished.
        var state = PendingDiscardState(count: 2, hand: ["a", "b"]);

        var clone = state.Clone();

        Assert.Equal(2, clone.PendingDiscards);
        Assert.Equal(StateSnapshot.Of(state), StateSnapshot.Of(clone));
    }

    // -- A real shipped card -------------------------------------------------------------------

    [Fact]
    public void A_real_cards_discard_gates_play_and_resolves_its_other_effects_first()
    {
        // T Juggler's "Toss": discard 1, then gain 3 spike. Uses the real card set rather than
        // synthetic ones, because the ordering guarantee only matters for cards that actually
        // pair a discard with something else -- and all three shipped users of `discard` do.
        //
        // The resource gain must land immediately: the debt is a separate, later choice, so an
        // effect list is never left half-resolved waiting on the player.
        var cards = CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, "t_juggler", TypeMask.Spike, maxHealth: 3)
                .Hand("t_body", "t_flare")
                .Resources(spike: 5))
            .Build();

        var toss = ActionGenerator.Generate(state, cards)
            .OfType<UseMoveAction>()
            .First(a => a.SourceSlot == new SlotIndex(PlayerId.One, 0) && a.MoveIndex == 0);

        ActionExecutor.Apply(state, cards, toss);

        // The gain resolved (5 - 1 cost + 3 gained), and the discard is now owed.
        Assert.Equal(7, state[PlayerId.One].Resources[ResourceType.Spike]);
        Assert.True(state.AwaitingDiscard);

        // Play is gated on paying it: two cards in hand, two ways to pay, nothing else offered.
        var gated = ActionGenerator.Generate(state, cards);
        Assert.Equal(2, gated.Count);
        Assert.All(gated, a => Assert.Equal(ActionKind.Discard, a.Kind));

        ActionExecutor.Apply(state, cards, new DiscardAction(PlayerId.One, "t_flare"));

        Assert.False(state.AwaitingDiscard);
        Assert.Equal(["t_body"], state[PlayerId.One].Hand);
        Assert.Equal(["t_flare"], state[PlayerId.One].Discard);
    }

    // Builds a state already owing `count` discards, bypassing the card that would cause it --
    // these tests are about the pending state's behaviour, not about any particular card.
    private static GameState PendingDiscardState(
        int count, string[] hand, int resources = 0, string? alsoInHand = null)
    {
        var full = alsoInHand is null ? hand : [.. hand, alsoInHand];

        var state = new StateBuilder()
            .P1(p => p.Hand(full).Resources(wheel: resources))
            .Build();

        state.AddPendingDiscards(count);
        return state;
    }
}
