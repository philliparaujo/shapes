using Shapes.Ai.Agents;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Agents;

// The property step 2.2 exists to guarantee: an ObservedState cannot expose the opponent's
// hand contents, or ANY player's deck order (including the observer's own -- see
// ObservedState's class doc comment for why "it's my own deck" isn't an exemption). "Cannot" is
// meant structurally, not just "currently doesn't" -- these tests pin exactly what's visible and
// what's reduced to a count or a sorted composition, on both sides of the projection, so a
// future field added to either observed type that accidentally forwards a real ordered list
// would fail here immediately.
public class ObservedStateTests
{
    private static GameState Position() =>
        new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Striker, TestCards.Bolt)
                .Deck(TestCards.Chooser, TestCards.Gated, TestCards.FreeMove)
                .Resources(spike: 2, anvil: 3, wheel: 1)
                .Score(4))
            .P2(p => p
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Hand(TestCards.Costly, TestCards.TargetedBolt, TestCards.DiscardTwo)
                .Deck(TestCards.RallyLike, TestCards.Striker)
                .Resources(spike: 1, anvil: 0, wheel: 5)
                .Score(2))
            .Build();

    // -- The opponent's hand: size only, never contents ---------------------------------------

    [Fact]
    public void Opponent_hand_is_reduced_to_a_count()
    {
        var state = Position();

        var observed = new ObservedState(state, PlayerId.One);

        Assert.Equal(state[PlayerId.Two].Hand.Count, observed.Opponent.HandSize);
    }

    [Fact]
    public void Opponent_hand_contents_are_not_reachable_from_any_public_member()
    {
        // ObservedOpponent has no property returning card ids for the hand -- HandSize is an
        // int. This test exists so that if a future edit adds one, it has to be a deliberate,
        // visible change to this file rather than a silent addition.
        var opponentType = typeof(ObservedOpponent);

        Assert.DoesNotContain(
            opponentType.GetProperties(),
            p => p.Name.Contains("Hand", StringComparison.Ordinal)
                 && p.PropertyType != typeof(int));
    }

    // -- The opponent's deck: size only, never order or contents ------------------------------

    [Fact]
    public void Opponent_deck_is_reduced_to_a_count()
    {
        var state = Position();

        var observed = new ObservedState(state, PlayerId.One);

        Assert.Equal(state[PlayerId.Two].Deck.Count, observed.Opponent.DeckSize);
    }

    [Fact]
    public void Opponent_deck_contents_are_not_reachable_from_any_public_member()
    {
        var opponentType = typeof(ObservedOpponent);

        Assert.DoesNotContain(
            opponentType.GetProperties(),
            p => p.Name.Contains("Deck", StringComparison.Ordinal)
                 && p.PropertyType != typeof(int));
    }

    // -- Everything else on the opponent's side is public --------------------------------------

    [Fact]
    public void Opponent_discard_resources_and_score_are_visible_in_full()
    {
        var state = Position();
        var opponent = state[PlayerId.Two];

        var observed = new ObservedState(state, PlayerId.One);

        Assert.Equal(opponent.Discard, observed.Opponent.Discard);
        Assert.Equal(opponent.Resources, observed.Opponent.Resources);
        Assert.Equal(opponent.Score, observed.Opponent.Score);
    }

    // -- The observer's own side is visible in full, except deck order -------------------------

    [Fact]
    public void Own_hand_and_discard_are_visible_in_full()
    {
        var state = Position();
        var self = state[PlayerId.One];

        var observed = new ObservedState(state, PlayerId.One);

        Assert.Equal(self.Hand, observed.Self.Hand);
        Assert.Equal(self.Discard, observed.Self.Discard);
        Assert.Equal(self.Resources, observed.Self.Resources);
        Assert.Equal(self.Score, observed.Self.Score);
    }

    // -- Own deck: composition visible, order is not, even to its owner --------------------------

    [Fact]
    public void Own_deck_composition_matches_the_real_deck_as_a_multiset()
    {
        var state = Position();
        var self = state[PlayerId.One];

        var observed = new ObservedState(state, PlayerId.One);

        Assert.Equal(self.Deck.Count, observed.Self.DeckSize);
        Assert.Equal(
            self.Deck.OrderBy(id => id, StringComparer.Ordinal),
            observed.Self.DeckComposition);
    }

    [Fact]
    public void Own_deck_composition_is_in_a_fixed_order_independent_of_the_real_draw_order()
    {
        // Two decks with the same cards in different orders must observe identically -- if
        // DeckComposition varied with draw order, it would BE draw order under another name.
        var shuffled = new StateBuilder()
            .P1(p => p.Deck(TestCards.FreeMove, TestCards.Chooser, TestCards.Gated))
            .Build();
        var sorted = new StateBuilder()
            .P1(p => p.Deck(TestCards.Chooser, TestCards.FreeMove, TestCards.Gated))
            .Build();

        var shuffledView = new ObservedState(shuffled, PlayerId.One);
        var sortedView = new ObservedState(sorted, PlayerId.One);

        Assert.Equal(sortedView.Self.DeckComposition, shuffledView.Self.DeckComposition);
    }

    // -- The board is public to both sides -------------------------------------------------------

    [Fact]
    public void The_board_is_the_same_object_for_either_observer()
    {
        var state = Position();

        var p1View = new ObservedState(state, PlayerId.One);
        var p2View = new ObservedState(state, PlayerId.Two);

        Assert.Same(state.Board, p1View.Board);
        Assert.Same(state.Board, p2View.Board);
    }

    // -- Switching which player observes flips which side is narrowed ---------------------------

    [Fact]
    public void Observing_from_the_other_side_flips_which_hand_is_hidden()
    {
        var state = Position();

        var p1View = new ObservedState(state, PlayerId.One);
        var p2View = new ObservedState(state, PlayerId.Two);

        Assert.Equal(state[PlayerId.One].Hand, p1View.Self.Hand);
        Assert.Equal(state[PlayerId.Two].Hand.Count, p1View.Opponent.HandSize);

        Assert.Equal(state[PlayerId.Two].Hand, p2View.Self.Hand);
        Assert.Equal(state[PlayerId.One].Hand.Count, p2View.Opponent.HandSize);
    }

    // -- Never carries the live GameState or its RNG ---------------------------------------------

    [Fact]
    public void No_public_member_exposes_the_source_GameState_or_its_random_source()
    {
        var forbidden = new[] { typeof(GameState), typeof(IRandomSource) };

        foreach (var type in new[] { typeof(ObservedState), typeof(ObservedSelf), typeof(ObservedOpponent) })
        {
            Assert.DoesNotContain(
                type.GetProperties(), p => forbidden.Contains(p.PropertyType));
        }
    }

    // -- AgentContext wiring: State is now an ObservedState, not the raw GameState --------------

    [Fact]
    public void AgentContext_state_is_the_observed_projection_for_the_active_player()
    {
        var state = Position();

        var context = AgentContext.ForActivePlayer(state, TestCards.Database);

        Assert.IsType<ObservedState>(context.State);
        Assert.Equal(state.ActivePlayer, context.State.Observer);
        Assert.Equal(state[state.ActivePlayer].Hand, context.State.Self.Hand);
        Assert.Equal(
            state[state.ActivePlayer.Opponent()].Hand.Count, context.State.Opponent.HandSize);
    }

    // -- Mirrors GameState phase/turn bookkeeping, which is public information ------------------

    [Fact]
    public void Turn_bookkeeping_is_carried_through_unchanged()
    {
        var state = Position();

        var observed = new ObservedState(state, PlayerId.One);

        Assert.Equal(state.ActivePlayer, observed.ActivePlayer);
        Assert.Equal(state.Phase, observed.Phase);
        Assert.Equal(state.TurnNumber, observed.TurnNumber);
        Assert.Equal(state.PendingDiscards, observed.PendingDiscards);
        Assert.Equal(state.AwaitingDiscard, observed.AwaitingDiscard);
    }
}
