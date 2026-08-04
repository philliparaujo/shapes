using Shapes.Core.Cards;
using Shapes.Core.Rules;

namespace Shapes.Tests.Cards;

// The shipped card set in Shapes.Content, loaded exactly as the game loads it.
//
// This is the test that catches a hand-edit mistake during Phase 3 balance work: the suites
// above prove the validator rejects bad cards in principle, and this one applies it to the
// real data. It is written now, with step 1.7, rather than with the cards themselves in step
// 1.10 -- so the first real card is validated the moment it lands rather than whenever someone
// remembers to add the check.
public class ContentCardSetTests
{
    private static string CardsDirectory => Path.Combine(AppContext.BaseDirectory, "Content", "cards");

    // Loads once: parsing is cheap, but every test here wants the same set, and a failure
    // should read as "the content set is broken" rather than repeating per test.
    private static CardDatabase Load() => CardLoader.FromDirectory(CardsDirectory);

    [Fact]
    public void The_cards_directory_is_present_in_the_build_output()
    {
        // Guards the content-copy pipeline itself. Without this, an empty or missing directory
        // would make every other test in this class vacuously pass -- the failure mode where a
        // whole suite silently stops testing anything.
        Assert.True(
            Directory.Exists(CardsDirectory),
            $"Expected the content card directory at {CardsDirectory}. Check the Shapes.Content copy pipeline.");
    }

    [Fact]
    public void Every_card_in_the_content_set_loads_without_error()
    {
        // CardLoader validates each card as it reads it, so this single call asserts the whole
        // schema -- unknown ops, unknown selectors, negative amounts, and the single-target
        // rule -- across the entire shipped set.
        var db = Load();

        Assert.All(db.All, card => Assert.False(string.IsNullOrWhiteSpace(card.Id)));
    }

    [Fact]
    public void Card_ids_are_unique_across_the_whole_set()
    {
        // CardDatabase's constructor enforces this, so reaching this point is the assertion.
        // Stated explicitly because uniqueness spans files: two cards in different files can
        // collide in a way neither file's own review would catch.
        var db = Load();

        Assert.Equal(db.Count, db.All.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_symmetric_deck_builds_from_the_content_set()
    {
        // Phases 1-3 play with this deck, so it must be constructible from the real cards:
        // CopiesPerCard of every card in the set, in a stable order.
        var db = Load();
        var rules = RuleSet.Default;

        var deck = db.BuildSymmetricDeck(rules);

        Assert.Equal(db.Count * rules.CopiesPerCard, deck.Count);
        Assert.Equal(rules.CopiesPerCard, deck.Count(id => id == db.All[0].Id));

        // Deliberately NOT asserted here: that the deck is large enough to deal opening hands.
        // The set is still being entered (step 1.10), so that assertion would currently just
        // encode "the cards aren't in yet" as a test failure. It belongs with the full set,
        // where a deck too small to play from is a genuine content bug.
    }
}
