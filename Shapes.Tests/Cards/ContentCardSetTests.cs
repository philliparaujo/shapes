using Shapes.Core.Cards;
using Shapes.Core.Rules;

namespace Shapes.Tests.Cards;

// The shipped card set in Shapes.Content, loaded exactly as the game loads it.
//
// This is the test that catches a hand-edit mistake during Phase 4 balance work: the suites
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

        // The full set landed with step 1.10 -- a deck too small to deal the starting hand plus
        // several draws is a genuine content bug, not a "still being entered" placeholder.
        Assert.True(
            deck.Count >= rules.StartingHandSize + rules.CardsDrawnPerTurn * 5,
            $"Symmetric deck of {deck.Count} cards is too small to sustain even a few turns' draws.");
    }

    [Fact]
    public void All_36_reference_cards_are_present()
    {
        // Pins the count so a card accidentally left out of the working tree (as happened once
        // before, per step 1.8's notes) fails loudly here rather than only showing up as a
        // slightly-smaller-than-expected deck.
        var db = Load();

        Assert.Equal(36, db.Count);
    }

    [Fact]
    public void The_default_deck_is_one_of_every_content_card()
    {
        // The console's only deck, and the sim's default mode. Exercises the whole set by
        // construction, which is the property that makes a console game a card-watching tool.
        var db = Load();

        var deck = DeckBuilder.Default(db);

        Assert.Equal(db.Count, deck.Count);
        Assert.All(db.All, card => Assert.Equal(1, deck.CopiesOf(card.Id)));
    }

    [Fact]
    public void Random_decks_include_spells_at_roughly_their_share_of_the_card_set()
    {
        // REGRESSION GUARD. MinPerType counts cards by PLAY COST, so spells count toward the type
        // they drain exactly as creatures do. When the seeding phase drew from creatures only, the
        // three type minimums consumed ~30 of 40 slots before a spell was ever eligible, holding
        // spells to ~6% of a deck against the ~25% an unbiased draw gives -- which made every
        // spell's per-card metrics rest on roughly a third of the sample every creature's did.
        //
        // The bound is loose (half the natural share) because the cost-tolerance rejection still
        // mildly favours creatures, which are slightly more expensive on average. It is tight
        // enough to catch a return of the structural exclusion, which was a 4x miss.
        var db = Load();
        var rules = RuleSet.Default;
        var spellShareOfSet = (double)db.All.Count(c => c.Kind == CardKind.Spell) / db.Count;

        var totalCards = 0;
        var totalSpells = 0;
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var deck = DeckBuilder.Random($"r{seed}", db, rules, new Core.State.SeededRandom(seed));
            totalCards += deck.Count;
            totalSpells += deck.Cards.Count(id => db[id].Kind == CardKind.Spell);
        }

        var observed = (double)totalSpells / totalCards;
        Assert.True(
            observed > spellShareOfSet / 2,
            $"Spells are {observed:P1} of generated decks but {spellShareOfSet:P1} of the card set "
            + "-- the type-minimum seeding is excluding them again.");
    }

    [Fact]
    public void Random_deck_type_minimums_count_spells_by_cost()
    {
        // The constraint is about RESOURCE DEMAND: a deck whose cards nearly all cost spike
        // drains spike dry while anvil and wheel pile up unspent. A 3-spike spell is that demand
        // just as much as a 3-spike creature, so DeckBuilder.TypeCounts (what the generator
        // enforces against) must count both -- and must therefore not agree with a creature-only
        // count on a set that has spells.
        var db = Load();
        var deck = DeckBuilder.Random("r", db, RuleSet.Default, new Core.State.SeededRandom(3));

        var byCost = DeckBuilder.TypeCounts(deck.Cards, db);
        var byBoard = DeckBuilder.CreatureTypeCounts(deck.Cards, db);

        Assert.All(byCost, kv => Assert.True(kv.Value >= 10, $"{kv.Key} demand = {kv.Value}"));

        // Cost demand counts every card; board typing counts creatures only. With spells in the
        // deck the totals must differ, which is what proves the two are measuring different things.
        Assert.True(
            byCost.Values.Sum() > byBoard.Values.Sum(),
            "Cost-demand counts should exceed creature-board counts when the deck holds spells.");
    }

    [Fact]
    public void Constrained_random_decks_are_generatable_from_the_content_set()
    {
        // The generator's constraints (mean cost within +/-0.2 of the default deck, 10+ cards
        // demanding each resource type by play cost) are only satisfiable if the real card set
        // actually supports them -- a content
        // change that skewed the cost curve or dropped a type below the minimum would make
        // --deck random start throwing, and it should fail HERE with a clear reason instead of
        // mid-batch hours into a balance run.
        var db = Load();
        var rules = RuleSet.Default;
        var reference = DeckBuilder.Default(db);
        var target = DeckBuilder.MeanCost(reference.Cards, db);

        for (var seed = 1UL; seed <= 20; seed++)
        {
            var deck = DeckBuilder.Random(
                $"r{seed}", db, rules, new Core.State.SeededRandom(seed), reference);

            Assert.Equal(DeckBuilder.StandardDeckSize, deck.Count);
            Assert.All(deck.CountsById().Values, c => Assert.True(c <= DeckBuilder.StandardMaxCopiesPerCard));

            var counts = DeckBuilder.TypeCounts(deck.Cards, db);
            Assert.All(counts, kv => Assert.True(kv.Value >= 10, $"seed {seed}: {kv.Key}={kv.Value}"));

            var cost = DeckBuilder.MeanCost(deck.Cards, db);
            Assert.True(
                Math.Abs(cost - target) <= 0.2 + 1e-9,
                $"seed {seed}: mean cost {cost:F3} vs default-deck {target:F3}");
        }
    }
}
