using Shapes.Console;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Console;

// Step 2.5: the console hides the non-active seat's hand unless --reveal is passed.
//
// DESIGN.md's coverage target says console rendering is not worth testing, and that stands -- none
// of this asserts layout, spacing, or wording beyond what it must. What it does assert is WHICH
// HAND IS HIDDEN, which is not a rendering question: it is the property that makes a human-v-AI
// result mean anything. Before this step the human read the AI's hand while the AI could not read
// theirs, so "I beat the AI" measured nothing.
//
// The natural regression is subtle and silent -- hiding the wrong seat, or hiding by active-player
// index in a way that inverts when player two is to act -- and it would look fine in a screenshot
// taken during player one's turn. So both seats are tested, not just the default one.
public class HiddenHandTests
{
    private static readonly CardDatabase Cards = TestCards.Database;

    // Two distinctly-named hands, so a test can tell not merely THAT something was hidden but
    // whose cards leaked when it wasn't.
    private static Shapes.Core.State.GameState Position(PlayerId active) =>
        new StateBuilder()
            .ActivePlayer(active)
            .P1(p => p.Hand(TestCards.Striker, TestCards.Bolt))
            .P2(p => p.Hand(TestCards.Striker, TestCards.Striker, TestCards.Bolt))
            .Build();

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void Active_players_own_hand_is_always_visible(PlayerId active)
    {
        // The active player is the one being asked to choose from their hand, so hiding it would
        // make the game unplayable rather than merely fair.
        var state = Position(active);

        var rendered = BoardView.DescribeHand(state, Cards, active, reveal: false);

        Assert.DoesNotContain("hidden", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Cards.Get(TestCards.Striker).Name, rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void Waiting_players_hand_is_hidden_by_default(PlayerId active)
    {
        // The whole point of the step. Tested from both seats because hiding the wrong one is the
        // regression that a single-seat test would pass straight through.
        var state = Position(active);
        var waiting = active.Opponent();

        var rendered = BoardView.DescribeHand(state, Cards, waiting, reveal: false);

        Assert.DoesNotContain(Cards.Get(TestCards.Striker).Name, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(Cards.Get(TestCards.Bolt).Name, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TestCards.Striker, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TestCards.Bolt, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_hand_still_shows_its_size()
    {
        // A count, not a blank. Hand size is public information -- visible across a table, and
        // step 2.3's determinizer depends on it being knowable to sample a correctly-sized hand.
        // Blanking it would show the human LESS than the AI is entitled to, overshooting the
        // fairness this step exists to establish.
        var state = Position(PlayerId.One);

        var rendered = BoardView.DescribeHand(state, Cards, PlayerId.Two, reveal: false);

        Assert.Contains("3", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void Reveal_restores_full_visibility_for_both_seats(PlayerId active)
    {
        // --reveal is the debugging view: watching an AI-v-AI game or inspecting a strange board
        // has no one to hide information from. If this regressed, the flag would still parse and
        // still be documented while doing nothing.
        var state = Position(active);

        foreach (var seat in PlayerIds.All)
        {
            var rendered = BoardView.DescribeHand(state, Cards, seat, reveal: true);

            Assert.DoesNotContain("hidden", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Cards.Get(TestCards.Striker).Name, rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_empty_waiting_hand_reads_as_a_count_not_as_empty()
    {
        // Zero cards is still a count, and it must not fall through to the "(empty)" branch by
        // accident -- that branch is the visible-hand path, and reaching it would mean the hiding
        // check is being skipped whenever the hand happens to be empty.
        var state = new StateBuilder()
            .ActivePlayer(PlayerId.One)
            .P1(p => p.Hand(TestCards.Striker))
            .Build();

        var rendered = BoardView.DescribeHand(state, Cards, PlayerId.Two, reveal: false);

        Assert.Contains("hidden", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Reveal_defaults_to_off()
    {
        // The default is the entire safety property: a flag that defaults to on would leave the
        // AI's hand visible to anyone who did not know to pass anything.
        Assert.False(ConsoleOptions.Parse([]).Reveal);
    }

    [Fact]
    public void Reveal_flag_is_parsed()
    {
        Assert.True(ConsoleOptions.Parse(["--reveal"]).Reveal);
    }
}
