using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Tests.Mechanics;

// Deck, hand, discard, resources and score for one player.
public class PlayerStateTests
{
    [Fact]
    public void A_new_player_starts_empty()
    {
        var player = new PlayerState(PlayerId.One);

        Assert.Empty(player.Hand);
        Assert.Empty(player.Deck);
        Assert.Empty(player.Discard);
        Assert.Equal(ResourcePool.Empty, player.Resources);
        Assert.Equal(0, player.Score);
    }

    [Fact]
    public void Drawing_moves_the_top_card_to_hand()
    {
        var player = new PlayerState(PlayerId.One, ["a", "b", "c"]);

        Assert.Equal("a", player.Draw());
        Assert.Equal(["a"], player.Hand);
        Assert.Equal(["b", "c"], player.Deck);
    }

    [Fact]
    public void Drawing_from_an_empty_deck_returns_null_rather_than_throwing()
    {
        // Deck exhaustion is a RuleSet decision the caller owns; the state model just reports
        // that nothing was drawn.
        var player = new PlayerState(PlayerId.One);

        Assert.Null(player.Draw());
        Assert.Empty(player.Hand);
    }

    [Fact]
    public void Drawing_several_stops_at_deck_exhaustion()
    {
        var player = new PlayerState(PlayerId.One, ["a", "b"]);

        var drawn = player.Draw(5);

        Assert.Equal(["a", "b"], drawn);
        Assert.Equal(2, player.Hand.Count);
        Assert.True(player.DeckIsEmpty);
    }

    [Fact]
    public void Discarding_moves_a_card_from_hand_to_discard()
    {
        var player = new PlayerState(PlayerId.One, ["a"]);
        player.Draw();

        Assert.True(player.DiscardCard("a"));
        Assert.Empty(player.Hand);
        Assert.Equal(["a"], player.Discard);
    }

    [Fact]
    public void Discarding_a_card_not_in_hand_reports_failure()
    {
        var player = new PlayerState(PlayerId.One);

        Assert.False(player.DiscardCard("nope"));
        Assert.Empty(player.Discard);
    }

    [Fact]
    public void Discarding_by_index_out_of_range_returns_null()
    {
        var player = new PlayerState(PlayerId.One);

        Assert.Null(player.DiscardCardAt(0));
        Assert.Null(player.DiscardCardAt(-1));
    }

    [Fact]
    public void Discarding_only_removes_one_copy_of_a_duplicated_card()
    {
        var player = new PlayerState(PlayerId.One, ["a", "a"]);
        player.Draw(2);

        player.DiscardCard("a");

        Assert.Single(player.Hand);
        Assert.Single(player.Discard);
    }

    [Fact]
    public void Paying_deducts_resources()
    {
        var player = new PlayerState(PlayerId.One);
        player.GainResources(new ResourcePool(3, 3, 3));

        player.Pay(new ResourcePool(1, 2, 0));

        Assert.Equal(new ResourcePool(2, 1, 3), player.Resources);
    }

    [Fact]
    public void Paying_more_than_held_throws()
    {
        var player = new PlayerState(PlayerId.One);
        player.GainResource(ResourceType.Spike, 1);

        Assert.False(player.CanAfford(new ResourcePool(2, 0, 0)));
        Assert.Throws<InvalidOperationException>(() => player.Pay(new ResourcePool(2, 0, 0)));
    }

    [Fact]
    public void Shuffling_is_reproducible_from_the_seed()
    {
        var cards = Enumerable.Range(0, 30).Select(i => $"card{i}").ToList();

        var a = new PlayerState(PlayerId.One, cards);
        var b = new PlayerState(PlayerId.One, cards);

        a.ShuffleDeck(new SeededRandom(77));
        b.ShuffleDeck(new SeededRandom(77));

        Assert.Equal(a.Deck, b.Deck);
    }

    [Fact]
    public void Shuffling_preserves_every_card()
    {
        var cards = Enumerable.Range(0, 30).Select(i => $"card{i}").ToList();
        var player = new PlayerState(PlayerId.One, cards);

        player.ShuffleDeck(new SeededRandom(5));

        Assert.Equal(cards.OrderBy(c => c), player.Deck.OrderBy(c => c));
    }

    [Fact]
    public void Shuffling_actually_reorders()
    {
        var cards = Enumerable.Range(0, 30).Select(i => $"card{i}").ToList();
        var player = new PlayerState(PlayerId.One, cards);

        player.ShuffleDeck(new SeededRandom(5));

        Assert.NotEqual(cards, player.Deck);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var player = new PlayerState(PlayerId.One, ["a", "b"]);
        player.Draw();
        player.GainResource(ResourceType.Anvil, 2);
        player.AddScore(3);

        var copy = player.Clone();
        copy.Draw();
        copy.AddScore(5);
        copy.GainResource(ResourceType.Anvil, 5);

        Assert.Single(player.Hand);
        Assert.Equal(3, player.Score);
        Assert.Equal(2, player.Resources.Anvil);
    }
}
