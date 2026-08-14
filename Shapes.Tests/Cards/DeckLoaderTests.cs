using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Tests.Cards;

// The decklist text format: `cardId count` lines, comments, and the error messages a hand-edited
// 40-card list needs when one line is wrong.
public class DeckLoaderTests
{
    private static CardDatabase BuildSet()
    {
        var cards = new List<CardDefinition>();
        for (var i = 0; i < 10; i++)
        {
            cards.Add(new CardDefinition(
                $"c{i}", $"Card {i}", CardKind.Creature,
                ResourcePool.Of(ResourceType.Spike, 1), health: 2, types: TypeMask.Spike));
        }

        return new CardDatabase(cards);
    }

    // 10 cards x 3 copies = 30, matching the ruleset below, so a valid list is expressible.
    private static RuleSet Rules() =>
        Fixtures.RuleSetTestHelper.CustomDeck(deckSize: 30, maxCopiesPerCard: 3);

    private static string ValidText() =>
        string.Join("\n", Enumerable.Range(0, 10).Select(i => $"c{i} 3"));

    [Fact]
    public void Parses_card_id_and_count_lines()
    {
        var deck = DeckLoader.FromText("d", ValidText(), BuildSet(), Rules());

        Assert.Equal(30, deck.Count);
        Assert.All(Enumerable.Range(0, 10), i => Assert.Equal(3, deck.CopiesOf($"c{i}")));
    }

    [Fact]
    public void A_bare_card_id_means_one_copy()
    {
        // 9 cards x3 = 27, plus three bare lines = 30.
        var text = string.Join("\n", Enumerable.Range(0, 9).Select(i => $"c{i} 3"))
            + "\nc9\nc9\nc9";

        var deck = DeckLoader.FromText("d", text, BuildSet(), Rules());

        Assert.Equal(30, deck.Count);
        Assert.Equal(3, deck.CopiesOf("c9"));
    }

    [Fact]
    public void Blank_lines_and_comments_are_ignored()
    {
        var text = "# my deck\n\n" + ValidText() + "\n\n   \n# trailing note\n";

        var deck = DeckLoader.FromText("d", text, BuildSet(), Rules());

        Assert.Equal(30, deck.Count);
    }

    [Fact]
    public void A_trailing_comment_on_a_card_line_is_stripped()
    {
        var text = ValidText().Replace("c0 3", "c0 3  # the good one");

        var deck = DeckLoader.FromText("d", text, BuildSet(), Rules());

        Assert.Equal(3, deck.CopiesOf("c0"));
    }

    [Fact]
    public void A_bad_count_names_the_line_number()
    {
        // Line numbers are the whole point of the error: a 40-line decklist with one typo is
        // otherwise a hunt.
        var text = "c0 3\nc1 zzz\n";

        var ex = Assert.Throws<DeckBuildException>(
            () => DeckLoader.FromText("d", text, BuildSet(), Rules()));

        Assert.Contains("line 2", ex.Message);
        Assert.Contains("zzz", ex.Message);
    }

    [Fact]
    public void A_malformed_line_names_the_line_number()
    {
        var text = "c0 3\nc1 2 extra\n";

        var ex = Assert.Throws<DeckBuildException>(
            () => DeckLoader.FromText("d", text, BuildSet(), Rules()));

        Assert.Contains("line 2", ex.Message);
    }

    [Fact]
    public void A_zero_or_negative_count_is_rejected()
    {
        var ex = Assert.Throws<DeckBuildException>(
            () => DeckLoader.FromText("d", "c0 0\n", BuildSet(), Rules()));

        Assert.Contains("positive count", ex.Message);
    }

    [Fact]
    public void An_empty_list_is_rejected()
    {
        var ex = Assert.Throws<DeckBuildException>(
            () => DeckLoader.FromText("d", "# nothing but a comment\n", BuildSet(), Rules()));

        Assert.Contains("no cards", ex.Message);
    }

    [Fact]
    public void The_loaded_deck_is_validated_against_the_ruleset()
    {
        // Parsing succeeding is not enough -- a short list must still fail, and with the size
        // message rather than a parse one.
        var ex = Assert.Throws<DeckBuildException>(
            () => DeckLoader.FromText("d", "c0 3\n", BuildSet(), Rules()));

        Assert.Contains("30", ex.Message);
    }

    [Fact]
    public void A_missing_file_is_reported_clearly()
    {
        var ex = Assert.Throws<DeckBuildException>(
            () => DeckLoader.FromFile("no_such_deck_file.txt", BuildSet(), Rules()));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void A_file_round_trips_and_takes_its_name_from_the_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shapes_deck_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, ValidText());

            var deck = DeckLoader.FromFile(path, BuildSet(), Rules());

            Assert.Equal(30, deck.Count);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), deck.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
