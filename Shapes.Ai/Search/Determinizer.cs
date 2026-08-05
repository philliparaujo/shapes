using Shapes.Ai.Agents;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Ai.Search;

// Samples a concrete GameState consistent with everything one player can observe.
//
// This is the piece that makes IS-MCTS possible on a hidden-information game. A search cannot
// expand a position it only partly knows, so each iteration draws one *possible world* -- a
// fully-specified state that agrees with every observation and guesses, uniformly at random,
// about everything else -- and searches that with the ordinary engine. Averaged over many
// iterations with a fresh sample each time (per-iteration resampling, PLAN.md step 2.6), the
// search's statistics approximate play against the real distribution of opponent hands rather
// than against one imagined hand.
//
// WHAT IS KNOWN, and therefore copied exactly: the board, both discard piles, both resource
// pools and scores, phase/turn bookkeeping, the pending-discard debt, the observer's own hand,
// and the SIZES of the opponent's hand and deck.
//
// WHAT IS GUESSED: the contents of the opponent's hand and deck, and the ORDER of both players'
// decks -- including the observer's own, whose composition is known but whose order is not.
//
// == How the opponent's cards are reconstructed ==
//
// ObservedState gives the opponent's hand and deck as bare counts, so their contents have to
// come from somewhere else: the DECKLIST. In symmetric mode both players play an identical,
// publicly-known deck (CardDatabase.BuildSymmetricDeck), so the opponent's unseen cards are
//
//     the shared decklist
//       - their discard pile          (visible across the table)
//       - their cards on the board    (visible, expanded through MergedFrom)
//
// and that remainder, shuffled, is dealt into a hand and a deck of the observed sizes.
//
// The subtraction is a MULTISET subtraction, not a set one. With copiesPerCard = 2, seeing one
// copy of a card in their discard means one copy remains, not zero. Treating it as a set would
// make every card the opponent has ever discarded impossible for them to hold -- a systematic
// bias in exactly the direction that makes the AI misplay against repeated cards.
//
// This is also why GameState.DestroyCreature exists. The subtraction only closes if every card
// that ever left a hand is findable in a visible zone; a destroyed creature that vanished
// instead of going to a discard pile would leave its card apparently still available, and the
// determinizer would deal the opponent cards that are physically dead. See
// Shapes.Tests/Mechanics/CardConservationTests.cs, which pins the identity this relies on.
//
// == Deliberate limitations ==
//
// SYMMETRIC DECKS ONLY. The whole reconstruction rests on the opponent's decklist being public.
// Under deckMode: "custom" it isn't, and a correct determinizer would need a belief distribution
// over possible decklists -- a different and larger problem. Rather than silently sampling
// nonsense, Determinize throws on a non-symmetric ruleset, mirroring BuildSymmetricDeck's own
// guard. Phases 1-3 are symmetric by design (PLAN.md, "Deck model").
//
// Lifting that assumption is scoped at PLAN.md Phase 4 step 9, alongside the deckbuilder that
// makes it necessary. The short version: this class's public surface does not change, and
// neither does anything downstream of it -- only UnseenCardsOf changes, from "start with the
// known decklist" to "start with a sampled one." The throw above is what makes that a
// compile-and-fix rather than a silent behaviour change, which is why it is a hard failure
// rather than a fallback.
//
// UNIFORM SAMPLING. Every world consistent with the observation is equally likely. A stronger
// agent would weight by opponent behaviour ("they held that card, so it is probably expensive"),
// but inference like that is a play-strength optimization for later tuning, not part of being
// correct. Uniform is the right starting point and the right baseline to measure against.
public sealed class Determinizer
{
    private readonly CardDatabase _cards;

    public Determinizer(CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        _cards = cards;
    }

    // Samples one possible world from `observed`, using `random` for every guess.
    //
    // The random source is a PARAMETER, not constructor state: a search calls this thousands of
    // times per decision and owns the stream, so the same seed reproduces the same sequence of
    // sampled worlds. That is what makes a search result replayable at all.
    public GameState Determinize(ObservedState observed, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(random);

        if (observed.Rules.DeckMode != DeckMode.Symmetric)
        {
            throw new InvalidOperationException(
                $"Determinize requires a symmetric ruleset, but '{observed.Rules.Name}' is "
                + $"{observed.Rules.DeckMode}. Reconstructing an opponent's hidden cards depends on "
                + "their decklist being public knowledge, which custom decks are not.");
        }

        var observer = observed.Observer;
        var opponent = observer.Opponent();

        // A fresh source rather than the live game's: ObservedState carries no IRandomSource by
        // design (there must be no path from an observation back to the real RNG stream), and a
        // rollout on this state must not advance the real game's randomness even indirectly. The
        // seed is drawn from the caller's stream so the whole thing stays reproducible.
        var stateRandom = new SeededRandom(unchecked((ulong)random.Next(int.MaxValue) + 1));

        var state = new GameState(observed.Rules, stateRandom, observed.ActivePlayer);

        // Cloned, not shared: ObservedState.Board is the LIVE board object, and a search rollout
        // mutating this state must not reach back into the real game.
        CopyBoard(observed.Board, state.Board);

        RestoreSelf(state, observed, observer, random);
        RestoreOpponent(state, observed, opponent, random);

        state.SetPhase(observed.Phase);
        state.SetTurnNumber(observed.TurnNumber);

        // Carried so a determinized state mid-debt generates the same discard actions the real
        // position does -- otherwise the search would explore a turn the real player cannot take.
        state.AddPendingDiscards(observed.PendingDiscards);

        return state;
    }

    // Convenience overload for the common "determinize what this agent is looking at" call.
    public GameState Determinize(AgentContext context, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Determinize(context.State, random);
    }

    private static void CopyBoard(Board source, Board destination)
    {
        foreach (var (slot, creature) in source.AllCreatures())
        {
            destination.Place(slot, creature.Clone());
        }
    }

    // The observer's own side: everything is known except deck ORDER.
    private static void RestoreSelf(
        GameState state, ObservedState observed, PlayerId observer, IRandomSource random)
    {
        var player = state[observer];
        var self = observed.Self;

        foreach (var cardId in self.Hand)
        {
            player.AddToHand(cardId);
        }

        foreach (var cardId in self.Discard)
        {
            player.SendToDiscard(cardId);
        }

        // Shuffled, NOT copied in composition order. Self.DeckComposition is sorted
        // alphabetically precisely so it carries no draw-order information; using it verbatim
        // would have the search believe it draws its own deck in alphabetical order, which is
        // both wrong and -- because it is deterministic -- consistently wrong in a way that would
        // bias every rollout the same direction.
        player.SetDeck(Shuffled(self.DeckComposition, random));

        player.SetResources(self.Resources);
        player.AddPendingNextTurnResources(self.PendingNextTurnResources);
        player.SetScore(self.Score);
    }

    // The opponent's side: discard, resources and score are known; hand and deck CONTENTS are
    // sampled from what the decklist says they must still be holding.
    private void RestoreOpponent(
        GameState state, ObservedState observed, PlayerId opponent, IRandomSource random)
    {
        var player = state[opponent];
        var view = observed.Opponent;

        foreach (var cardId in view.Discard)
        {
            player.SendToDiscard(cardId);
        }

        var unseen = Shuffled(UnseenCardsOf(state, observed, opponent), random);

        // Deal the hand first, then the rest becomes the deck. Which cards land where is exactly
        // the guess being made; the SIZES are observations and must come out right, so a
        // mismatch here is a bug rather than something to paper over -- see the check below.
        if (unseen.Count != view.HandSize + view.DeckSize)
        {
            throw new InvalidOperationException(
                $"Cannot determinize: player {opponent} is observed to hold {view.HandSize} cards "
                + $"and have {view.DeckSize} in deck ({view.HandSize + view.DeckSize} unseen), but "
                + $"{unseen.Count} cards of the decklist are unaccounted for. The card-conservation "
                + "invariant has been violated -- see GameState.DestroyCreature and "
                + "CardConservationTests.");
        }

        for (var i = 0; i < view.HandSize; i++)
        {
            player.AddToHand(unseen[i]);
        }

        player.SetDeck(unseen.Skip(view.HandSize));

        player.SetResources(view.Resources);
        player.SetScore(view.Score);
    }

    // The decklist minus everything the observer can see of `player`'s cards: their discard pile
    // and their creatures in play. What remains is distributed between their hand and their deck,
    // and the observer cannot tell which.
    //
    // Note this reads the BOARD from the state being built (already populated by CopyBoard),
    // which is the same board the observation carries -- board creatures are public to both
    // sides, so there is no hidden information entering here.
    //
    // Only ever called for the OPPONENT: it reads observed.Opponent.Discard, and the observer's
    // own unseen cards are not a guess at all (Self.DeckComposition names them exactly). The
    // parameter exists to attribute failures in the error messages, not to generalize the method.
    private List<string> UnseenCardsOf(GameState state, ObservedState observed, PlayerId opponent)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cardId in _cards.BuildSymmetricDeck(observed.Rules))
        {
            remaining[cardId] = remaining.GetValueOrDefault(cardId) + 1;
        }

        foreach (var cardId in observed.Opponent.Discard)
        {
            Decrement(remaining, cardId, opponent, "discard pile");
        }

        foreach (var (_, creature) in state.Board.CreaturesOf(opponent))
        {
            // Tokens were never cards and are not in the decklist, so subtracting them would
            // remove a real card from the pool that the opponent might legitimately still hold.
            if (creature.IsToken)
            {
                continue;
            }

            // A merged creature is several physical cards in one slot; every one of them is
            // visibly out of the opponent's hand.
            foreach (var cardId in creature.MergedFrom)
            {
                Decrement(remaining, cardId, opponent, "board");
            }
        }

        var unseen = new List<string>();
        foreach (var (cardId, count) in remaining)
        {
            for (var i = 0; i < count; i++)
            {
                unseen.Add(cardId);
            }
        }

        // Sorted before the caller shuffles it. Dictionary enumeration order is not contractually
        // stable, and an unstable starting order would make the same seed produce different
        // samples across runs -- quietly destroying the reproducibility the whole engine is built
        // on. The shuffle is what randomizes; this only makes the input to it deterministic.
        unseen.Sort(StringComparer.Ordinal);
        return unseen;
    }

    private static void Decrement(
        Dictionary<string, int> remaining, string cardId, PlayerId player, string zone)
    {
        // A card visible in a zone that the decklist says should not exist (or should have fewer
        // copies) means the observation and the decklist disagree -- a corrupted state, or a
        // determinizer being handed a game it was not built for. Failing here names the card,
        // rather than letting it surface later as an off-by-one in the deal.
        if (!remaining.TryGetValue(cardId, out var count) || count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot determinize: player {player} has a copy of '{cardId}' in their {zone}, but "
                + "the decklist accounts for no more copies of it. The observed state is "
                + "inconsistent with the ruleset's deck.");
        }

        remaining[cardId] = count - 1;
    }

    // Fisher-Yates through the caller's source, matching PlayerState.ShuffleDeck so a determinized
    // shuffle is drawn from the same distribution the real game uses.
    private static List<string> Shuffled(IReadOnlyList<string> cards, IRandomSource random)
    {
        var copy = new List<string>(cards);
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }
}
