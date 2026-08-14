using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Sim;

namespace Shapes.Tests.Sim;

// The --deck flags and the DeckProvider they configure.
public class DeckOptionsTests
{
    private static CardDatabase BuildSet()
    {
        var cards = new List<CardDefinition>();
        foreach (var (type, mask) in new[]
        {
            (ResourceType.Spike, TypeMask.Spike),
            (ResourceType.Anvil, TypeMask.Anvil),
            (ResourceType.Wheel, TypeMask.Wheel),
        })
        {
            for (var i = 0; i < 6; i++)
            {
                cards.Add(new CardDefinition(
                    $"{type}_{i}".ToLowerInvariant(), $"{type} {i}", CardKind.Creature,
                    ResourcePool.Of(type, (i % 3) + 1), health: 2, types: mask));
            }
        }

        return new CardDatabase(cards);
    }

    [Theory]
    [InlineData("default", DeckSource.Default)]
    [InlineData("random", DeckSource.Random)]
    [InlineData("RANDOM", DeckSource.Random)]
    public void Deck_mode_parses_case_insensitively(string raw, DeckSource expected)
    {
        Assert.Equal(expected, SimOptions.Parse(["--deck", raw]).Deck);
    }

    [Fact]
    public void Deck_defaults_to_the_default_deck()
    {
        // Every number in balance/LOG.md was measured against one-of-each, so changing this
        // default would silently make new runs incomparable to every recorded one.
        Assert.Equal(DeckSource.Default, SimOptions.Parse([]).Deck);
    }

    [Fact]
    public void An_unknown_deck_mode_is_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => SimOptions.Parse(["--deck", "wat"]));
        Assert.Contains("wat", ex.Message);
    }

    [Fact]
    public void Custom_requires_a_deck_file()
    {
        var ex = Assert.Throws<ArgumentException>(() => SimOptions.Parse(["--deck", "custom"]));
        Assert.Contains("--deck-file", ex.Message);
    }

    [Fact]
    public void A_deck_file_without_custom_mode_is_rejected()
    {
        // Silently ignoring it would run the whole batch against the wrong deck and report it as
        // if nothing were wrong.
        var ex = Assert.Throws<ArgumentException>(
            () => SimOptions.Parse(["--deck", "random", "--deck-file", "d.txt"]));

        Assert.Contains("only valid with --deck custom", ex.Message);
    }

    [Fact]
    public void Random_deck_constraints_parse()
    {
        var options = SimOptions.Parse(
            ["--deck", "random", "--deck-cost-tolerance", "0.5", "--deck-min-per-type", "8"]);

        Assert.Equal(0.5, options.DeckCostTolerance);
        Assert.Equal(8, options.DeckMinPerType);
    }

    [Fact]
    public void Negative_constraints_are_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => SimOptions.Parse(["--deck-cost-tolerance", "-1"]));
        Assert.Throws<ArgumentException>(
            () => SimOptions.Parse(["--deck-min-per-type", "-1"]));
    }

    [Fact]
    public void Default_provider_gives_both_seats_the_same_deck()
    {
        var cards = BuildSet();
        var provider = new DeckProvider(DeckSource.Default, cards, RuleSet.Default);

        var (one, two) = provider.DecksFor(1);

        Assert.Same(one, two);
        Assert.Equal(cards.Count, one.Count);
    }

    [Fact]
    public void Random_provider_gives_the_two_seats_different_decks()
    {
        // Identical decks from one seed would silently turn every random-deck game back into a
        // mirror match, hiding exactly the deck-diversity effect the mode exists to measure.
        var provider = new DeckProvider(
            DeckSource.Random, BuildSet(),
            Fixtures.RuleSetTestHelper.CustomDeck(deckSize: 40, maxCopiesPerCard: 3));

        var (one, two) = provider.DecksFor(12345);

        Assert.NotEqual(one.Cards, two.Cards);
    }

    [Fact]
    public void Random_provider_is_reproducible_from_the_game_seed()
    {
        // Decks must not depend on how many games ran before this one -- under
        // BatchRunner's Parallel.ForEach that order is not even deterministic.
        var rules = Fixtures.RuleSetTestHelper.CustomDeck(deckSize: 40, maxCopiesPerCard: 3);
        var provider = new DeckProvider(DeckSource.Random, BuildSet(), rules);

        var first = provider.DecksFor(777);

        // Interleave other calls: a provider-level RNG would be advanced by these and the repeat
        // below would differ.
        provider.DecksFor(1);
        provider.DecksFor(2);

        var second = provider.DecksFor(777);

        Assert.Equal(first.One.Cards, second.One.Cards);
        Assert.Equal(first.Two.Cards, second.Two.Cards);
    }

    [Fact]
    public void Custom_provider_requires_a_deck()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DeckProvider(DeckSource.Custom, BuildSet(), RuleSet.Default));
    }

    [Fact]
    public void Game_results_record_the_decks_that_were_played()
    {
        // The included-win-rate metric reads these off the result, so a game that forgot to
        // record its decks would silently contribute nothing to it.
        var cards = BuildSet();
        var deck = DeckBuilder.Default(cards);

        var result = GameRunner.Play(
            "random", "random", seed: 3, cards, RuleSet.Default, iterations: 1, deck, deck);

        Assert.Equal("default", result.DeckNameOne);
        Assert.Equal("default", result.DeckNameTwo);
        Assert.Equal(cards.Count, result.DeckOne.Count);
        Assert.All(result.DeckOne.Values, copies => Assert.Equal(1, copies));
    }
}
