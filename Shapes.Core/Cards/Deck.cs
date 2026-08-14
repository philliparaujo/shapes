using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Core.Cards;

// A decklist: the multiset of card ids a player brings to a game, plus the name it was built under.
//
// THE INVARIANT THIS TYPE EXISTS TO ENFORCE: every game is played with a Deck. Before this, deck
// construction was `cards.BuildSymmetricDeck(rules)` inlined at each of the four call sites that
// set a game up (console, sim, Godot adapter, test fixtures), which meant "what deck is this game
// using" had no answer other than "whatever that call returns" -- and no way to answer it
// differently per seat. A Deck is that answer, and it is the only thing PlayerState.SetDeck is
// ever handed now.
//
// Stored as CARDS (a flat, ordered list with duplicates) rather than as id->count pairs, because
// the consumer is PlayerState.SetDeck, which wants exactly that. Counts are derived on demand for
// validation and reporting -- the reverse (storing counts, expanding per use) would make the flat
// order non-deterministic across runs, since dictionary enumeration order is not contractual, and
// deterministic pre-shuffle order is what makes a seeded game replayable (see
// CardDatabase.BuildSymmetricDeck's own note).
//
// Immutable: a Deck handed to two seats must not be mutable by either, and the sim shares one
// Deck instance across every game in a batch.
public sealed class Deck
{
    private readonly List<string> _cards;

    // Names a deck for reporting -- "default", "random#3", or a user-supplied deck's own name.
    // Carried on the deck rather than tracked alongside it so a metrics report can attribute a
    // result to a decklist without the caller re-threading a label through every layer.
    public string Name { get; }

    // Deck order as built, before shuffling. Deterministic for a given (card set, seed) so the
    // same seed replays the same game -- see the class note.
    public IReadOnlyList<string> Cards => _cards;

    public int Count => _cards.Count;

    public Deck(string name, IEnumerable<string> cards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(cards);

        Name = name;
        _cards = [.. cards];

        if (_cards.Count == 0)
        {
            throw new ArgumentException($"Deck '{name}' is empty.", nameof(cards));
        }
    }

    // Copies per card id. Built fresh per call rather than cached: this is used by validation and
    // by the sim's per-deck card accounting, neither of which is a hot path, and a cached
    // dictionary on an immutable type is a field that has to be kept honest for no measured gain.
    public IReadOnlyDictionary<string, int> CountsById()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cardId in _cards)
        {
            counts[cardId] = counts.GetValueOrDefault(cardId) + 1;
        }

        return counts;
    }

    // How many copies of one card this deck runs. The question the sim's included-win-rate
    // by-copy-count breakdown asks per (deck, card), so it gets a name rather than being a
    // CountsById lookup at each call site.
    public int CopiesOf(string cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        var copies = 0;
        foreach (var id in _cards)
        {
            if (string.Equals(id, cardId, StringComparison.Ordinal))
            {
                copies++;
            }
        }

        return copies;
    }

    public bool Contains(string cardId) => CopiesOf(cardId) > 0;

    // A shuffled copy of this deck's cards, for handing to PlayerState.SetDeck.
    //
    // Returns cards rather than mutating: a Deck is shared across both seats and across every game
    // in a sim batch, so shuffling in place would have seat two draw from seat one's shuffle.
    // Fisher-Yates through the caller's seeded source, matching PlayerState.ShuffleDeck exactly so
    // a deck shuffled here is drawn from the same distribution as one shuffled there.
    public IReadOnlyList<string> Shuffled(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var copy = new List<string>(_cards);
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }

    public override string ToString() => $"{Name} ({_cards.Count} cards)";
}
