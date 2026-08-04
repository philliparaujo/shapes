using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Ai.Agents;

// One player's view of a position: everything GameState knows, minus what that player could
// not legitimately know. The type that makes leaking impossible rather than merely forbidden --
// an agent given an ObservedState has no path back to the opponent's hand contents or deck
// order, because there is no property that carries them.
//
// What's hidden, and why (see PlayerState.Deck's own doc comment for the deck-order half of
// this): DECK ORDER is hidden from both players, including the observer's own -- a real player
// cannot see the face of their next card any more than their opponent's. Deck COMPOSITION (what
// remains, unordered) is fair game for the observer's own deck, since they built it and can in
// principle count what's left; it is not fair game for the opponent's, so the opponent's deck
// is reduced all the way to a bare count. Everything else -- the board, both discard piles, both
// resource pools and scores, and even the opponent's hand SIZE -- is public: hand size has to be
// knowable for step 2.3's determinizer to sample a hand of the right length, and the physical
// pile sizes are visible across the table in any real game.
//
// Deliberately NOT a projection of GameState's shape (no Board property duplicating
// GameState.Board, no Rules passthrough beyond what's needed) -- it exposes exactly the fields
// an agent needs and nothing that would let one reconstruct the source GameState. There is no
// property here that returns a GameState or an IRandomSource: the live RNG stream is shared
// engine machinery, not player knowledge, and a determinizer builds its own fresh source rather
// than reading the real one.
public sealed class ObservedState
{
    public RuleSet Rules { get; }

    public Board Board { get; }

    public PlayerId Observer { get; }

    public PlayerId ActivePlayer { get; }

    public TurnPhase Phase { get; }

    public int TurnNumber { get; }

    public int PendingDiscards { get; }

    public bool AwaitingDiscard => PendingDiscards > 0;

    // The observer's own side, in full -- exactly what a player can see of their own hand and
    // deck.
    public ObservedSelf Self { get; }

    // The opponent's side, narrowed to what's visible from across the table.
    public ObservedOpponent Opponent { get; }

    public ObservedState(GameState state, PlayerId observer)
    {
        ArgumentNullException.ThrowIfNull(state);

        Rules = state.Rules;
        Board = state.Board;
        Observer = observer;
        ActivePlayer = state.ActivePlayer;
        Phase = state.Phase;
        TurnNumber = state.TurnNumber;

        // Only the active player can owe a discard, and PendingDiscards only gates that
        // player's own actions -- but the debt's existence isn't hidden information (it changes
        // which actions the active player has, which is public the moment it's the active
        // player's turn), so it carries over unconditionally.
        PendingDiscards = state.PendingDiscards;

        var self = state[observer];
        var opponent = state[observer.Opponent()];

        // Sorted, not the raw list: sorting is what turns "the deck contains these cards" (fair
        // game -- the observer built this deck) into something that cannot also answer "what's
        // the very next card" (not fair game -- see the class doc comment). A stable alphabetical
        // order was picked specifically because it carries no information about draw order; any
        // ordering NOT derived from state.Random would do, but re-deriving one from the real
        // deck's position would defeat the point.
        var deckComposition = self.Deck.OrderBy(id => id, StringComparer.Ordinal).ToList();

        Self = new ObservedSelf(self.Hand, deckComposition, self.Deck.Count, self.Discard,
            self.Resources, self.PendingNextTurnResources, self.Score);

        Opponent = new ObservedOpponent(opponent.Hand.Count, opponent.Deck.Count,
            opponent.Discard, opponent.Resources, opponent.Score);
    }

    // Builds the view for whoever is about to act -- the common case, mirroring
    // AgentContext.ForActivePlayer.
    public static ObservedState ForActivePlayer(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ObservedState(state, state.ActivePlayer);
    }
}

// The observer's own hand in full, and their own deck as composition-without-order.
public sealed class ObservedSelf
{
    public IReadOnlyList<string> Hand { get; }

    // What remains in the observer's own deck, in a fixed sorted order that carries no draw-order
    // information -- NOT the real list, and specifically not "the real list, but it happens to be
    // the observer's own so it's fine": an agent reading index 0 here must not learn what it's
    // about to draw, or a search built on this projection would be quietly cheating against
    // itself. See the class doc comment for why composition is visible but order isn't, even for
    // one's own deck.
    public IReadOnlyList<string> DeckComposition { get; }

    public int DeckSize { get; }

    public IReadOnlyList<string> Discard { get; }

    public ResourcePool Resources { get; }

    public ResourcePool PendingNextTurnResources { get; }

    public int Score { get; }

    public ObservedSelf(
        IReadOnlyList<string> hand, IReadOnlyList<string> deckComposition, int deckSize,
        IReadOnlyList<string> discard, ResourcePool resources,
        ResourcePool pendingNextTurnResources, int score)
    {
        Hand = hand;
        DeckComposition = deckComposition;
        DeckSize = deckSize;
        Discard = discard;
        Resources = resources;
        PendingNextTurnResources = pendingNextTurnResources;
        Score = score;
    }
}

// The opponent's side, narrowed to what a player across the table can actually observe: pile
// sizes instead of pile contents for the hand and deck, since those are the two hidden zones.
public sealed class ObservedOpponent
{
    public int HandSize { get; }

    public int DeckSize { get; }

    public IReadOnlyList<string> Discard { get; }

    public ResourcePool Resources { get; }

    public int Score { get; }

    public ObservedOpponent(
        int handSize, int deckSize, IReadOnlyList<string> discard, ResourcePool resources,
        int score)
    {
        HandSize = handSize;
        DeckSize = deckSize;
        Discard = discard;
        Resources = resources;
        Score = score;
    }
}
