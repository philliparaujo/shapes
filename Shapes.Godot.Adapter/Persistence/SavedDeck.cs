using System.Text.Json.Serialization;
using Shapes.Core.Cards;
using Shapes.Core.Rules;

namespace Shapes.Godot.Adapter;

// A player-built decklist as it lives on disk, plus the rules for turning one into a real
// Shapes.Core Deck (DESIGN.md C2).
//
// Split from the Godot-side DeckStore for the same reason SavedMatch is split from
// MatchSaveStore: this half is pure and testable outside the editor, while only the file I/O
// needs Godot's user:// resolution. The two halves meet at DeckSlots, which is the whole
// serialized document.
//
// STORED AS ID->COUNT PAIRS, not as the flat card list Deck itself holds. Deck's own header
// explains why IT stores a flat list (PlayerState.SetDeck wants exactly that, and a stable
// pre-shuffle order is what makes a seeded game replayable) -- but that reasoning is about a
// deck being PLAYED, not a deck being EDITED. The deckbuilder's entire vocabulary is "how many
// copies of this card," every edit is +1/-1 against a count, and a flat 40-entry list on disk
// would make a hand-inspected save file needlessly unreadable. ToDeck expands counts back to a
// flat list in a fixed order (see its note), so determinism survives the round trip.
public sealed class SavedDeck
{
    // Empty rather than null for an unused slot: an unnamed slot with no cards IS the empty
    // state, so the file always has ten well-formed entries and loading never has to reason
    // about holes in the array.
    public string Name { get; set; } = "";

    public List<DeckEntryDto> Cards { get; set; } = [];

    public bool IsEmpty => Cards.Count == 0 && Name.Length == 0;

    public int TotalCards
    {
        get
        {
            var total = 0;
            foreach (var entry in Cards)
            {
                total += entry.Count;
            }

            return total;
        }
    }

    // The flat card list this decklist expands to, in a deterministic order.
    //
    // Ordered by card id, with a slot's copies adjacent, rather than in the order the player
    // happened to add cards: Deck's header requires the pre-shuffle order be deterministic for a
    // given decklist, and "the order the UI's buttons were clicked in" is not a property that
    // survives being saved and reloaded. Sorting by id makes two identical decklists built by
    // different click sequences produce the identical Deck, which is what lets a seed replay.
    public IReadOnlyList<string> ToCardIds()
    {
        var ids = new List<string>(TotalCards);
        foreach (var entry in Cards.OrderBy(e => e.CardId, StringComparer.Ordinal))
        {
            for (var i = 0; i < entry.Count; i++)
            {
                ids.Add(entry.CardId);
            }
        }

        return ids;
    }

    // The real engine Deck this slot plays as, validated against the ruleset (exactly DeckSize
    // cards, no more than MaxCopiesPerCard of any one, every id known).
    //
    // Throws DeckBuildException on an illegal deck rather than silently repairing it -- this is
    // the same DeckBuilder.Custom path the sim's --deck-file uses, so a deck built here is legal
    // by exactly the criteria every other engine applies, which is the whole point of routing
    // through DeckBuilder rather than constructing a Deck directly. The deckbuilder UI lets a
    // partial deck be SAVED (work in progress is worth keeping); it is at match start that a
    // decklist has to be a legal 40, and this is where that is enforced.
    public Deck ToDeck(CardDatabase cards, RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rules);

        var name = Name.Length > 0 ? Name : "custom";
        return DeckBuilder.Custom(name, ToCardIds(), cards, rules);
    }

    // How many copies of one card this slot runs; 0 when absent.
    public int CopiesOf(string cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        foreach (var entry in Cards)
        {
            if (string.Equals(entry.CardId, cardId, StringComparison.Ordinal))
            {
                return entry.Count;
            }
        }

        return 0;
    }

    // Sets a card's copy count, adding or removing the entry as needed. Count 0 removes the row
    // entirely rather than leaving a zero behind, so Cards is always exactly "what's in the deck"
    // and the UI can render it without filtering.
    public void SetCopies(string cardId, int count)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        var index = Cards.FindIndex(e => string.Equals(e.CardId, cardId, StringComparison.Ordinal));

        if (count <= 0)
        {
            if (index >= 0)
            {
                Cards.RemoveAt(index);
            }

            return;
        }

        if (index >= 0)
        {
            Cards[index].Count = count;
            return;
        }

        Cards.Add(new DeckEntryDto { CardId = cardId, Count = count });
    }

    public SavedDeck Clone() => new()
    {
        Name = Name,
        Cards = [.. Cards.Select(e => new DeckEntryDto { CardId = e.CardId, Count = e.Count })],
    };
}

// One `cardId count` pair -- the same two columns DeckLoader's plain-text format uses, so a
// saved slot and a hand-written decklist carry identical information.
public sealed class DeckEntryDto
{
    public string CardId { get; set; } = "";
    public int Count { get; set; }
}

// The whole saved document: a fixed ten slots, index-aligned with the deckbuilder's slot picker
// and with the lobby's per-seat deck dropdown.
//
// FIXED-LENGTH rather than a growable list of named decks, because the slot INDEX is the stable
// identity a lobby selection refers to. A list would renumber every deck after one is deleted,
// silently repointing a seat's saved choice at a different deck; ten always-present slots make
// "delete" mean "this slot is empty again" and leave every other slot's index untouched.
public sealed class DeckSlots
{
    public const int SlotCount = 10;

    public List<SavedDeck> Slots { get; set; } = [];

    // Guarantees exactly SlotCount well-formed entries regardless of what was on disk -- an older
    // or truncated file is padded rather than rejected, and a longer one is trimmed. Loading a
    // save must never throw over slot arithmetic: DeckStore.Load treats a corrupt file as "no
    // decks yet," and this is what makes every *readable* file usable.
    public void Normalize()
    {
        Slots ??= [];

        while (Slots.Count < SlotCount)
        {
            Slots.Add(new SavedDeck());
        }

        if (Slots.Count > SlotCount)
        {
            Slots.RemoveRange(SlotCount, Slots.Count - SlotCount);
        }

        for (var i = 0; i < Slots.Count; i++)
        {
            Slots[i] ??= new SavedDeck();
            Slots[i].Name ??= "";
            Slots[i].Cards ??= [];
        }
    }

    public static DeckSlots Empty()
    {
        var slots = new DeckSlots();
        slots.Normalize();
        return slots;
    }
}

// Source-generated JSON, matching SavedMatchJsonContext's own reason: Godot's .NET export runs
// with trimming, and reflection-based serialization is exactly what trimming removes.
[JsonSerializable(typeof(DeckSlots))]
[JsonSerializable(typeof(SavedDeck))]
[JsonSerializable(typeof(DeckEntryDto))]
[JsonSerializable(typeof(List<SavedDeck>))]
[JsonSerializable(typeof(List<DeckEntryDto>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public sealed partial class DeckSlotsJsonContext : JsonSerializerContext
{
}
