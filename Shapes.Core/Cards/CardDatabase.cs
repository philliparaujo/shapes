using Shapes.Core.Rules;

namespace Shapes.Core.Cards;

// The loaded card set, keyed by id.
//
// One instance per game (or per sim batch), shared by every player, every CreatureInstance,
// and every MCTS clone -- card data is static, so nothing here is ever mutated by play. That
// sharing is the point: a creature stores only its card id and looks its moves up here, rather
// than carrying a copy of the move list into every clone.
public sealed class CardDatabase
{
    private readonly Dictionary<string, CardDefinition> _byId;
    private readonly List<CardDefinition> _ordered;

    public CardDatabase(IEnumerable<CardDefinition> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        _ordered = [.. cards];
        _byId = new Dictionary<string, CardDefinition>(_ordered.Count, StringComparer.Ordinal);

        foreach (var card in _ordered)
        {
            // Duplicate ids are a load-time error, not a last-one-wins merge: two cards
            // sharing an id would silently make one of them unplayable, and a balance run
            // would report results for a card that never appeared.
            if (!_byId.TryAdd(card.Id, card))
            {
                throw new CardLoadException($"Duplicate card id '{card.Id}'.");
            }
        }
    }

    public int Count => _ordered.Count;

    // Declaration order, which for a directory load is filename order. Deck building iterates
    // this, so it must be deterministic -- a shuffled deck is only reproducible from its seed
    // if the unshuffled deck was built the same way every time.
    public IReadOnlyList<CardDefinition> All => _ordered;

    public bool Contains(string cardId) => _byId.ContainsKey(cardId);

    public CardDefinition this[string cardId] => Get(cardId);

    public CardDefinition Get(string cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        if (!_byId.TryGetValue(cardId, out var card))
        {
            throw new CardLoadException($"Unknown card id '{cardId}'.");
        }

        return card;
    }

    public bool TryGet(string cardId, out CardDefinition? card) => _byId.TryGetValue(cardId, out card);

    // How many moves a card declares. This is the function CreatureInstance.MoveIndexOffset
    // takes: a merged creature's move list is the concatenation of its source cards' moves, and
    // the once-per-turn bitmask indexes into that concatenation, so the offsets must be
    // computed from the same counts the move lookup uses.
    public int MoveCountOf(string cardId) => Get(cardId).Moves.Count;

    // A creature's full move list, in the order MoveIndexOffset assumes: source cards in merge
    // order, each card's moves in declaration order. Owning this here means no caller
    // reinvents the concatenation and risks disagreeing about what move index 3 means.
    public IReadOnlyList<MoveDefinition> MovesOf(IReadOnlyList<string> mergedFrom)
    {
        ArgumentNullException.ThrowIfNull(mergedFrom);

        if (mergedFrom.Count == 1)
        {
            return Get(mergedFrom[0]).Moves;
        }

        var moves = new List<MoveDefinition>();
        foreach (var cardId in mergedFrom)
        {
            moves.AddRange(Get(cardId).Moves);
        }

        return moves;
    }

    // Builds the deck both players share in symmetric mode: CopiesPerCard of every card in the
    // set, unshuffled. Shuffling is the caller's job, through the seeded IRandomSource, so this
    // stays deterministic and testable on its own.
    public IReadOnlyList<string> BuildSymmetricDeck(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.DeckMode != DeckMode.Symmetric)
        {
            throw new InvalidOperationException(
                $"BuildSymmetricDeck requires a symmetric ruleset, but '{rules.Name}' is {rules.DeckMode}.");
        }

        var deck = new List<string>(_ordered.Count * rules.CopiesPerCard);
        foreach (var card in _ordered)
        {
            for (var i = 0; i < rules.CopiesPerCard; i++)
            {
                deck.Add(card.Id);
            }
        }

        return deck;
    }

    // Checks a custom deck against the ruleset's limits. Phase 4's deckbuilder calls this so
    // the UI cannot construct a deck the engine would reject -- one definition of "legal deck",
    // not two.
    public void ValidateDeck(IReadOnlyList<string> deck, RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.DeckMode != DeckMode.Custom)
        {
            throw new InvalidOperationException(
                $"ValidateDeck requires a custom-deck ruleset, but '{rules.Name}' is {rules.DeckMode}.");
        }

        if (deck.Count != rules.DeckSize)
        {
            throw new CardValidationException(
                $"Deck has {deck.Count} cards; ruleset '{rules.Name}' requires exactly {rules.DeckSize}.");
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cardId in deck)
        {
            if (!Contains(cardId))
            {
                throw new CardValidationException($"Deck contains unknown card id '{cardId}'.");
            }

            counts[cardId] = counts.GetValueOrDefault(cardId) + 1;
        }

        foreach (var (cardId, count) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (count > rules.MaxCopiesPerCard)
            {
                throw new CardValidationException(
                    $"Deck contains {count} copies of '{cardId}'; the limit is {rules.MaxCopiesPerCard}.");
            }
        }
    }
}
