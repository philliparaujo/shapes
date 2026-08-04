using Shapes.Core.Primitives;

namespace Shapes.Core.State;

// One player's private state: deck, hand, discard, resources, score.
//
// The board is shared and lives on GameState; everything here belongs to one side.
public sealed class PlayerState
{
    private readonly List<string> _deck;
    private readonly List<string> _hand;
    private readonly List<string> _discard;

    public PlayerId Id { get; }

    public ResourcePool Resources { get; private set; }

    // Resources granted by `gain_next_turn`, added to income on this player's next turn and
    // then cleared. Not visible via Resources until then -- the whole point is that it does
    // NOT land this turn.
    public ResourcePool PendingNextTurnResources { get; private set; }

    public int Score { get; private set; }

    // Deck order matters and is hidden: index 0 is the next card drawn.
    public IReadOnlyList<string> Deck => _deck;

    public IReadOnlyList<string> Hand => _hand;

    public IReadOnlyList<string> Discard => _discard;

    public PlayerState(PlayerId id, IEnumerable<string>? deck = null)
    {
        Id = id;
        _deck = deck?.ToList() ?? [];
        _hand = [];
        _discard = [];
        Resources = ResourcePool.Empty;
    }

    private PlayerState(
        PlayerId id, List<string> deck, List<string> hand, List<string> discard,
        ResourcePool resources, int score, ResourcePool pendingNextTurnResources)
    {
        Id = id;
        _deck = deck;
        _hand = hand;
        _discard = discard;
        Resources = resources;
        Score = score;
        PendingNextTurnResources = pendingNextTurnResources;
    }

    public bool DeckIsEmpty => _deck.Count == 0;

    public void GainResources(ResourcePool amount) => Resources = Resources.Add(amount);

    public void GainResource(ResourceType type, int amount) =>
        Resources = Resources.Add(type, amount);

    public void AddPendingNextTurnResources(ResourcePool amount) =>
        PendingNextTurnResources = PendingNextTurnResources.Add(amount);

    // Returns the pending grant and clears it -- called once by GameState.ApplyIncome so a
    // grant lands exactly on the next income phase, never twice.
    public ResourcePool ConsumePendingNextTurnResources()
    {
        var pending = PendingNextTurnResources;
        PendingNextTurnResources = ResourcePool.Empty;
        return pending;
    }

    public bool CanAfford(ResourcePool cost) => Resources.Covers(cost);

    // Throws when unaffordable rather than clamping: an unpayable cost means legal-action
    // generation let through an action the player cannot pay for, and the bug is upstream.
    public void Pay(ResourcePool cost) => Resources = Resources.Subtract(cost);

    // Returns the card drawn, or null when the deck is empty. Deck exhaustion is deliberately
    // not fatal -- the rule for what happens then is a RuleSet decision, and the caller owns
    // it. Returning null keeps that choice out of here.
    public string? Draw()
    {
        if (_deck.Count == 0)
        {
            return null;
        }

        var card = _deck[0];
        _deck.RemoveAt(0);
        _hand.Add(card);
        return card;
    }

    public List<string> Draw(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var drawn = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var card = Draw();
            if (card is null)
            {
                break;
            }

            drawn.Add(card);
        }

        return drawn;
    }

    public bool DiscardCard(string cardId)
    {
        if (!_hand.Remove(cardId))
        {
            return false;
        }

        _discard.Add(cardId);
        return true;
    }

    public string? DiscardCardAt(int handIndex)
    {
        if (handIndex < 0 || handIndex >= _hand.Count)
        {
            return null;
        }

        var card = _hand[handIndex];
        _hand.RemoveAt(handIndex);
        _discard.Add(card);
        return card;
    }

    // Removes a card from hand without discarding it -- used when playing it to the board.
    public bool RemoveFromHand(string cardId) => _hand.Remove(cardId);

    public void AddToHand(string cardId) => _hand.Add(cardId);

    public void SendToDiscard(string cardId) => _discard.Add(cardId);

    public void AddScore(int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        Score += points;
    }

    // Debug affordance for the console client's "adjustable score" requirement.
    public void SetScore(int score)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(score);
        Score = score;
    }

    public void SetResources(ResourcePool resources) => Resources = resources;

    // Fisher-Yates through the seeded source, so a shuffle is reproducible from the seed.
    public void ShuffleDeck(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        for (var i = _deck.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }
    }

    public void SetDeck(IEnumerable<string> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        _deck.Clear();
        _deck.AddRange(cards);
    }

    public PlayerState Clone() =>
        new(Id, [.. _deck], [.. _hand], [.. _discard], Resources, Score, PendingNextTurnResources);

    public override string ToString() =>
        $"P{Id.ToIndex() + 1} score={Score} res={Resources} hand={_hand.Count} deck={_deck.Count}";
}
