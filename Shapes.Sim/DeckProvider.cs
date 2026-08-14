using Shapes.Core.Cards;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Sim;

// Supplies the two decks a game is played with, per the batch's --deck mode.
//
// Exists so GameRunner does not branch on DeckSource itself: the runner's contract becomes "you
// are handed two decks and you play them," which is the same contract whether they came from a
// file, a generator, or the default set. That also keeps the reproducibility rule in one place --
// see DecksFor.
public sealed class DeckProvider
{
    private readonly DeckSource _source;
    private readonly CardDatabase _cards;
    private readonly RuleSet _rules;
    private readonly DeckBuilder.RandomDeckConstraints _constraints;

    // Built once for the whole batch in the non-random modes: the default and custom decks are
    // the same every game, and rebuilding a 40-card list per game to get an identical answer is
    // pure waste in a batch that plays thousands. A Deck is immutable and Shuffled() copies, so
    // sharing one instance across every game AND both seats is safe -- see Deck's class note.
    private readonly Deck? _fixedDeck;

    public DeckProvider(
        DeckSource source, CardDatabase cards, RuleSet rules, Deck? customDeck = null,
        DeckBuilder.RandomDeckConstraints? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rules);

        _source = source;
        _cards = cards;
        _rules = rules;
        _constraints = constraints ?? new DeckBuilder.RandomDeckConstraints();

        _fixedDeck = source switch
        {
            DeckSource.Default => DeckBuilder.Default(cards),
            DeckSource.Custom => customDeck
                ?? throw new ArgumentNullException(
                    nameof(customDeck), "DeckSource.Custom requires a deck."),
            DeckSource.Random => null,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown deck source."),
        };
    }

    public DeckSource Source => _source;

    // The two decks for one game, seeded from that game's own seed.
    //
    // REPRODUCIBILITY: random decks are generated from a stream derived from the GAME's seed, not
    // from a provider-level RNG advanced per call. A provider-level stream would make a game's
    // decks depend on how many games happened to be generated before it -- which under
    // BatchRunner's Parallel.ForEach is not even deterministic, since completion order varies run
    // to run. Deriving from the game seed means game N always gets the same decks whether it runs
    // first, last, or alone.
    //
    // The two seats draw from DIFFERENT derived streams (distinct multipliers, matching the scheme
    // GameRunner already uses for its agents) so seat one and seat two do not get identical decks
    // from one seed -- which would silently turn every random-deck game back into a mirror match
    // and hide exactly the deck-diversity effect the mode exists to measure.
    public (Deck One, Deck Two) DecksFor(ulong gameSeed)
    {
        if (_fixedDeck is not null)
        {
            return (_fixedDeck, _fixedDeck);
        }

        var one = DeckBuilder.Random(
            $"random#{gameSeed}a", _cards, _rules, new SeededRandom(gameSeed * 2654435761UL),
            constraints: _constraints);
        var two = DeckBuilder.Random(
            $"random#{gameSeed}b", _cards, _rules, new SeededRandom(gameSeed * 2246822519UL),
            constraints: _constraints);

        return (one, two);
    }

    // A short human-readable description for the run header and the report's provenance.
    public string Describe() => _source switch
    {
        DeckSource.Default => $"default (one of each, {_fixedDeck!.Count} cards)",
        DeckSource.Custom => $"custom '{_fixedDeck!.Name}' ({_fixedDeck.Count} cards)",
        _ => $"random per seat per game ({DeckBuilder.DeckSizeOf(_rules)} cards, "
            + $"cost +/-{_constraints.CostTolerance}, min {_constraints.MinPerType}/type)",
    };
}
