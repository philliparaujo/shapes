using System.Text.Json;
using Shapes.Core.Cards;
using Shapes.Core.Rules;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// PLAN.md C2: SavedDeck/DeckSlots are the deckbuilder's persistence format. The bar here is that
// a deck built in the UI becomes a REAL engine Deck -- validated by the same DeckBuilder path
// every other engine uses -- rather than a Godot-only parallel notion of a decklist.
public class SavedDeckTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static RuleSet Rules => RuleSet.Default;

    // A legal 40-card deck: 3 copies each of as many cards as fit, remainder topped up.
    private static SavedDeck LegalDeck(string name = "test")
    {
        var deck = new SavedDeck { Name = name };
        var ids = Cards.All.Select(c => c.Id).ToList();

        var i = 0;
        while (deck.TotalCards < DeckBuilder.StandardDeckSize)
        {
            var remaining = DeckBuilder.StandardDeckSize - deck.TotalCards;
            deck.SetCopies(ids[i], Math.Min(DeckBuilder.StandardMaxCopiesPerCard, remaining));
            i++;
        }

        return deck;
    }

    [Fact]
    public void ToDeck_builds_a_validated_engine_deck()
    {
        var deck = LegalDeck("aggro").ToDeck(Cards, Rules);

        Assert.Equal("aggro", deck.Name);
        Assert.Equal(DeckBuilder.StandardDeckSize, deck.Count);
    }

    // The whole reason ToDeck routes through DeckBuilder.Custom rather than constructing a Deck
    // directly: an illegal decklist must be rejected by the SAME rules the sim's --deck-file
    // path applies, not by a second Godot-local copy of them.
    [Fact]
    public void ToDeck_rejects_a_partial_deck()
    {
        var deck = new SavedDeck { Name = "partial" };
        deck.SetCopies(Cards.All[0].Id, 3);

        var ex = Assert.Throws<DeckBuildException>(() => deck.ToDeck(Cards, Rules));
        Assert.Contains("40", ex.Message);
    }

    [Fact]
    public void ToDeck_rejects_too_many_copies()
    {
        var deck = LegalDeck();
        var id = Cards.All[0].Id;
        deck.SetCopies(id, DeckBuilder.StandardMaxCopiesPerCard + 1);

        Assert.Throws<DeckBuildException>(() => deck.ToDeck(Cards, Rules));
    }

    // Deck's own header requires a deterministic pre-shuffle order, and a saved deck is exactly
    // where that could be lost: the UI's click order is not a property that survives a round
    // trip. Two decks with identical CONTENT built in different orders must expand identically.
    [Fact]
    public void ToCardIds_is_order_independent()
    {
        var first = new SavedDeck();
        first.SetCopies("basic_circle", 2);
        first.SetCopies("basic_square", 3);

        var second = new SavedDeck();
        second.SetCopies("basic_square", 3);
        second.SetCopies("basic_circle", 2);

        Assert.Equal(first.ToCardIds(), second.ToCardIds());
    }

    [Fact]
    public void SetCopies_zero_removes_the_entry_entirely()
    {
        var deck = new SavedDeck();
        deck.SetCopies("basic_circle", 2);
        deck.SetCopies("basic_circle", 0);

        Assert.Empty(deck.Cards);
        Assert.Equal(0, deck.CopiesOf("basic_circle"));
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var slots = DeckSlots.Empty();
        slots.Slots[3] = LegalDeck("midrange");

        var json = JsonSerializer.Serialize(slots, DeckSlotsJsonContext.Default.DeckSlots);
        var loaded = JsonSerializer.Deserialize(json, DeckSlotsJsonContext.Default.DeckSlots);

        Assert.NotNull(loaded);
        loaded.Normalize();

        Assert.Equal("midrange", loaded.Slots[3].Name);
        Assert.Equal(DeckBuilder.StandardDeckSize, loaded.Slots[3].TotalCards);
        Assert.Equal(slots.Slots[3].ToCardIds(), loaded.Slots[3].ToCardIds());
    }

    // Normalize is what lets DeckStore.Load treat any READABLE file as usable -- a save written
    // by an older build with fewer slots must pad rather than throw, since the alternative is a
    // player who cannot open the tab at all.
    [Fact]
    public void Normalize_pads_a_short_slot_list()
    {
        var slots = new DeckSlots { Slots = [LegalDeck("only")] };
        slots.Normalize();

        Assert.Equal(DeckSlots.SlotCount, slots.Slots.Count);
        Assert.Equal("only", slots.Slots[0].Name);
        Assert.True(slots.Slots[9].IsEmpty);
    }

    [Fact]
    public void Normalize_trims_an_over_long_slot_list()
    {
        var slots = new DeckSlots
        {
            Slots = [.. Enumerable.Range(0, DeckSlots.SlotCount + 5).Select(_ => new SavedDeck())],
        };

        slots.Normalize();
        Assert.Equal(DeckSlots.SlotCount, slots.Slots.Count);
    }

    [Fact]
    public void An_untouched_slot_reads_as_empty()
    {
        Assert.True(DeckSlots.Empty().Slots[0].IsEmpty);
    }
}
