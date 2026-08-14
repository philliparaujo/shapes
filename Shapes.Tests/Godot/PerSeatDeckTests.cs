using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// PLAN.md C2: the two seats can play DIFFERENT decklists, chosen by the lobby's per-seat deck
// dropdowns. Two properties matter and neither is obvious from the types alone:
//
//   1. Each seat is actually dealt its own deck (not both from seat one's).
//   2. A game dealt from custom decks RESUMES correctly -- replay re-runs the deal through the
//      same seeded stream, so resuming against the wrong decks desyncs the entire action log.
//      That failure is silent at the seam, which is why it is tested rather than reasoned about.
public class PerSeatDeckTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static RuleSet Rules => RuleSet.Default;

    // Two decks with no card in common, so "which deck did this seat draw from" is answerable
    // from a hand alone -- a shared-deck bug would show up as a card from the other list.
    private static (Deck One, Deck Two) DisjointDecks()
    {
        var creatures = Cards.All.Where(c => c.IsCreature).Select(c => c.Id).ToList();
        Assert.True(creatures.Count >= 8, "Card set too small to build two disjoint decks.");

        var half = creatures.Count / 2;
        var first = creatures.Take(half).ToList();
        var second = creatures.Skip(half).ToList();

        return (Repeat("deck-one", first), Repeat("deck-two", second));
    }

    // A 40-card deck cycling through `ids` -- copy limits do not apply here (these decks are
    // handed straight to GameSetup, not through DeckBuilder.Validate), so this only needs to
    // produce the right SIZE from the right card pool.
    private static Deck Repeat(string name, IReadOnlyList<string> ids)
    {
        var cards = new List<string>();
        for (var i = 0; i < DeckBuilder.StandardDeckSize; i++)
        {
            cards.Add(ids[i % ids.Count]);
        }

        return new Deck(name, cards);
    }

    private static GameSession Start(ulong seed, Deck? one, Deck? two)
    {
        var session = new GameSession(Rules, Cards, new SeededRandom(seed), PlayerId.One);
        session.Start(Rules.StartingHandSize, one, two);
        return session;
    }

    [Fact]
    public void Each_seat_is_dealt_its_own_deck()
    {
        var (one, two) = DisjointDecks();
        var session = Start(seed: 7, one, two);

        var handOne = session.State[PlayerId.One].Hand;
        var handTwo = session.State[PlayerId.Two].Hand;

        Assert.NotEmpty(handOne);
        Assert.NotEmpty(handTwo);
        Assert.All(handOne, id => Assert.Contains(id, one.Cards));
        Assert.All(handTwo, id => Assert.Contains(id, two.Cards));
    }

    [Fact]
    public void Both_decks_are_exposed_per_seat()
    {
        var (one, two) = DisjointDecks();
        var session = Start(seed: 7, one, two);

        Assert.Same(one, session.DeckOne);
        Assert.Same(two, session.DeckTwo);

        // The question an agent's determinizer actually asks -- and the one a call site can get
        // backwards silently, which is why GameSession answers it rather than each caller.
        Assert.Same(two, session.OpponentDeckOf(PlayerId.One));
        Assert.Same(one, session.OpponentDeckOf(PlayerId.Two));
    }

    [Fact]
    public void The_symmetric_overload_deals_both_seats_the_same_list()
    {
        var (one, _) = DisjointDecks();

        var session = new GameSession(Rules, Cards, new SeededRandom(3), PlayerId.One);
        session.Start(Rules.StartingHandSize);

        Assert.NotNull(session.DeckOne);
        Assert.Equal(session.DeckOne!.Cards, session.DeckTwo!.Cards);

        // Deck stays seat one's, for callers that predate per-seat decks.
        Assert.Same(session.DeckOne, session.Deck);

        var explicitBoth = Start(seed: 3, one, one);
        Assert.Same(one, explicitBoth.DeckOne);
        Assert.Same(one, explicitBoth.DeckTwo);
    }

    // Null means "the default deck" at every layer that takes a Deck -- the convention a lobby
    // left untouched relies on.
    [Fact]
    public void Null_decks_fall_back_to_the_default_deck()
    {
        var session = Start(seed: 11, null, null);
        var expected = DeckBuilder.Default(Cards);

        Assert.Equal(expected.Cards, session.DeckOne!.Cards);
        Assert.Equal(expected.Cards, session.DeckTwo!.Cards);
    }

    // THE property an interrupted custom-deck game depends on. A resume that re-deals from the
    // default deck would shuffle different cards, deal a different opening hand, and then apply
    // an action log describing cards no longer held.
    [Fact]
    public void Resume_reproduces_a_custom_deck_game_exactly()
    {
        var (one, two) = DisjointDecks();
        var live = Start(seed: 4242, one, two);

        var random = new SeededRandom(2024);
        var actions = new List<GameAction>();

        while (!live.State.IsOver && actions.Count < 5000)
        {
            var legal = live.LegalActions();
            var choice = legal[random.Next(legal.Count)];
            live.Submit(choice);
            actions.Add(choice);
        }

        Assert.True(live.State.IsOver, "Live playthrough did not terminate.");

        var resumed = GameSession.Resume(
            Rules, Cards, new SeededRandom(4242), PlayerId.One, Rules.StartingHandSize,
            actions, one, two);

        Assert.Equal(live.State.Winner, resumed.State.Winner);
        Assert.Equal(live.State.TurnNumber, resumed.State.TurnNumber);
        Assert.Equal(live.State[PlayerId.One].Hand, resumed.State[PlayerId.One].Hand);
        Assert.Equal(live.State[PlayerId.Two].Hand, resumed.State[PlayerId.Two].Hand);
        Assert.Equal(live.State[PlayerId.One].Score, resumed.State[PlayerId.One].Score);
        Assert.Equal(live.State[PlayerId.Two].Score, resumed.State[PlayerId.Two].Score);
    }

    // The same log resumed against the WRONG decks must not silently produce a matching game --
    // this is what makes persisting the decklists load-bearing rather than belt-and-braces.
    [Fact]
    public void Resume_against_the_default_deck_does_not_reproduce_a_custom_deck_game()
    {
        var (one, two) = DisjointDecks();
        var live = Start(seed: 99, one, two);

        var random = new SeededRandom(5);
        var actions = new List<GameAction>();

        for (var i = 0; i < 6 && !live.State.IsOver; i++)
        {
            var legal = live.LegalActions();
            var choice = legal[random.Next(legal.Count)];
            live.Submit(choice);
            actions.Add(choice);
        }

        // Replaying a custom-deck log against the default deck either throws (the logged action
        // references a card this deal never dealt) or lands somewhere different. Both are
        // acceptable failures; silently agreeing would not be.
        try
        {
            var resumed = GameSession.Resume(
                Rules, Cards, new SeededRandom(99), PlayerId.One, Rules.StartingHandSize, actions);

            Assert.NotEqual(live.State[PlayerId.One].Hand, resumed.State[PlayerId.One].Hand);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Threw on an unplayable logged action -- the desync surfaced loudly, as intended.
        }
    }

    // SavedDeckList is what carries a decklist across a save, and it must preserve the exact
    // pre-shuffle ORDER, not merely the multiset -- that order is an input to the seeded shuffle.
    [Fact]
    public void SavedDeckList_round_trips_a_decks_exact_order()
    {
        var (one, _) = DisjointDecks();

        var saved = SavedDeckList.Of(one);
        var restored = saved.ToDeck();

        Assert.Equal(one.Name, restored.Name);
        Assert.Equal(one.Cards, restored.Cards);
    }

    [Fact]
    public void SavedMatch_round_trips_both_decklists()
    {
        var (one, two) = DisjointDecks();

        var match = new SavedMatch(
            Seed: 5, SeatConfig.Human, SeatConfig.Human, [],
            SavedDeckList.Of(one), SavedDeckList.Of(two));

        var restored = SavedMatch.FromDto(match.ToDto());

        Assert.Equal(one.Cards, restored.DeckOne!.Cards);
        Assert.Equal(two.Cards, restored.DeckTwo!.Cards);
    }

    // A save written before per-seat decks existed has no deck fields at all; it must load as
    // "default deck" rather than failing.
    [Fact]
    public void SavedMatch_without_decks_loads_as_null()
    {
        var match = new SavedMatch(Seed: 5, SeatConfig.Human, SeatConfig.Human, []);
        var restored = SavedMatch.FromDto(match.ToDto());

        Assert.Null(restored.DeckOne);
        Assert.Null(restored.DeckTwo);
    }
}
