using Shapes.Core.Cards;
using Shapes.Core.Primitives;

namespace Shapes.Core.State;

// Deals a game: decks in, shuffled, opening hands drawn, seat-two compensation applied.
//
// THE single place a game is set up, and the reason it exists: "every game is played with a Deck"
// is an invariant, and an invariant enforced by four independent copies of the same five-line deal
// loop (console, sim, Godot adapter, test fixtures) is one typo away from not holding. Each of
// those call sites previously inlined `SetDeck(cards.BuildSymmetricDeck(rules))`, which is also
// why per-seat decks were not expressible -- there was no seam to pass one through.
//
// Mirrors the ordering GameState.ApplySecondSeatCompensation documents and depends on: both decks
// set and shuffled, both opening hands dealt, THEN compensation, THEN the caller's first
// AdvanceToActions(). Getting that order wrong is silent -- seat two's extra card would come off
// an unshuffled deck -- which is precisely the kind of thing that belongs in one function rather
// than in a comment at four call sites.
public static class GameSetup
{
    // Deals `deckOne`/`deckTwo` to their seats and returns the cards each drew, in seat order, so
    // a caller tracking draws for metrics can account for the opening hand and seat two's
    // compensation the same way it accounts for every later draw. Callers that measure nothing
    // (the console, the Godot client) ignore the return.
    //
    // Does NOT call AdvanceToActions: the caller owns entering turn one, because the sim harvests
    // turn events immediately after it and the console renders before it.
    public static (IReadOnlyList<string> One, IReadOnlyList<string> Two) Deal(
        GameState state, Deck deckOne, Deck deckTwo)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(deckOne);
        ArgumentNullException.ThrowIfNull(deckTwo);

        var drawnOne = new List<string>();
        var drawnTwo = new List<string>();

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            var deck = playerId == PlayerId.One ? deckOne : deckTwo;

            // Shuffled through the game's own RNG (not a fresh one) so the deal is part of the
            // seeded stream a replay reproduces. Deck.Shuffled returns a copy rather than
            // shuffling in place, which is what lets both seats share one Deck instance -- the
            // sim does exactly that for the default-deck mode across a whole batch.
            player.SetDeck(deck.Shuffled(state.Random));

            var openingHand = player.Draw(state.Rules.StartingHandSize);
            (playerId == PlayerId.One ? drawnOne : drawnTwo).AddRange(openingHand);
        }

        drawnTwo.AddRange(state.ApplySecondSeatCompensation());

        return (drawnOne, drawnTwo);
    }

    // Convenience for the symmetric case: both seats play the same decklist. Still two shuffles
    // off the shared deck, never one shuffle used twice -- see Deal.
    public static (IReadOnlyList<string> One, IReadOnlyList<string> Two) Deal(GameState state, Deck deck) =>
        Deal(state, deck, deck);
}
