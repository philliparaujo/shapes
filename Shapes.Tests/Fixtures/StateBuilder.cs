using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Tests.Fixtures;

// Builds exact board positions directly, instead of playing toward them.
//
// Without this, tests become long action sequences that break whenever an unrelated rule
// changes -- the usual reason card-game suites get abandoned. A test should state the
// position it cares about and nothing else.
//
//   var state = new StateBuilder()
//       .P1(p => p.Slot(0, "cadet", TypeMask.Wheel, health: 2).Resources(spike: 3))
//       .P2(p => p.Slot(1, "monk", TypeMask.Anvil).Score(4))
//       .Build();
public sealed class StateBuilder
{
    private RuleSet _rules = RuleSet.Default;
    private ulong _seed = 1;
    private PlayerId _activePlayer = PlayerId.One;
    private TurnPhase _phase = TurnPhase.Actions;
    private CardDatabase? _conservingDecks;
    private readonly Dictionary<PlayerId, PlayerBuilder> _players = new()
    {
        [PlayerId.One] = new PlayerBuilder(),
        [PlayerId.Two] = new PlayerBuilder(),
    };

    public StateBuilder WithRuleSet(RuleSet rules)
    {
        _rules = rules;
        return this;
    }

    public StateBuilder WithSeed(ulong seed)
    {
        _seed = seed;
        return this;
    }

    public StateBuilder ActivePlayer(PlayerId player)
    {
        _activePlayer = player;
        return this;
    }

    public StateBuilder Phase(TurnPhase phase)
    {
        _phase = phase;
        return this;
    }

    // Fills each player's deck with whatever the symmetric decklist has left over after their
    // hand, board and discard -- making the position satisfy the card-conservation identity
    //
    //     hand + deck + discard + (board, expanded through MergedFrom) == the starting deck
    //
    // OPT-IN, because most tests do not need it and it would otherwise silently deal every
    // position a 20-card deck that the test never asked for -- changing draw behaviour, deck
    // exhaustion, and hand limits underneath suites that pin them.
    //
    // Any test involving a DETERMINIZING agent needs it. A hand-built position ordinarily leaves
    // both decks empty, which says "these players hold no cards anywhere" while the board says
    // otherwise; Determinizer checks that identity and throws rather than sampling a world it
    // knows is impossible (which is exactly the guard step 2.3 built). That throw is the
    // determinizer being right about a fixture being unrealistic, so the fix belongs here.
    public StateBuilder ConservingDecks(CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        _conservingDecks = cards;
        return this;
    }

    public StateBuilder P1(Action<PlayerBuilder> configure)
    {
        configure(_players[PlayerId.One]);
        return this;
    }

    public StateBuilder P2(Action<PlayerBuilder> configure)
    {
        configure(_players[PlayerId.Two]);
        return this;
    }

    public GameState Build()
    {
        var state = new GameState(_rules, new SeededRandom(_seed), _activePlayer);
        state.SetPhase(_phase);

        foreach (var (id, builder) in _players)
        {
            builder.ApplyTo(state, id);
        }

        if (_conservingDecks is not null)
        {
            foreach (var id in PlayerIds.All)
            {
                FillRemainderDeck(state, id, _conservingDecks);
            }
        }

        return state;
    }

    // Sets a player's deck to the decklist minus everything of theirs already placed.
    //
    // Multiset subtraction, matching Determinizer.UnseenCardsOf: with copiesPerCard = 2, a card
    // held in hand leaves one copy for the deck, not zero. A card on the board or in hand that
    // the decklist does not contain is left alone rather than throwing -- several suites build
    // creatures from ids that are not real cards, and this helper's job is to make the deck add
    // up, not to police the fixture.
    private static void FillRemainderDeck(GameState state, PlayerId id, CardDatabase cards)
    {
        var player = state[id];
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var cardId in cards.BuildSymmetricDeck(state.Rules))
        {
            remaining[cardId] = remaining.GetValueOrDefault(cardId) + 1;
        }

        var placed = player.Hand.Concat(player.Discard)
            .Concat(state.Board.CreaturesOf(id).SelectMany(entry => entry.Creature.MergedFrom));

        foreach (var cardId in placed)
        {
            if (remaining.TryGetValue(cardId, out var count) && count > 0)
            {
                remaining[cardId] = count - 1;
            }
        }

        var deck = new List<string>();
        foreach (var (cardId, count) in remaining)
        {
            for (var i = 0; i < count; i++)
            {
                deck.Add(cardId);
            }
        }

        // Sorted for the same reason Determinizer.UnseenCardsOf sorts: dictionary enumeration
        // order is not contractually stable, and a fixture whose deck order varied run to run
        // would make seeded tests flaky in a way that looks like a search bug.
        deck.Sort(StringComparer.Ordinal);
        player.SetDeck(deck);
    }

    public sealed class PlayerBuilder
    {
        private readonly List<(int Slot, CreatureInstance Creature)> _creatures = [];
        private readonly List<string> _hand = [];
        private readonly List<string> _deck = [];
        private ResourcePool _resources = ResourcePool.Empty;
        private int _score;

        // Places a creature. Health defaults to full; pass it explicitly to start damaged.
        public PlayerBuilder Slot(int slot, string cardId, TypeMask types, int maxHealth = 3, int? health = null)
        {
            _creatures.Add((slot, new CreatureInstance(cardId, maxHealth, types, health)));
            return this;
        }

        // Places a pre-built creature, for cases needing a merged one.
        public PlayerBuilder Slot(int slot, CreatureInstance creature)
        {
            _creatures.Add((slot, creature));
            return this;
        }

        public PlayerBuilder Resources(int spike = 0, int anvil = 0, int wheel = 0)
        {
            _resources = new ResourcePool(spike, anvil, wheel);
            return this;
        }

        public PlayerBuilder Score(int score)
        {
            _score = score;
            return this;
        }

        public PlayerBuilder Hand(params string[] cardIds)
        {
            _hand.AddRange(cardIds);
            return this;
        }

        public PlayerBuilder Deck(params string[] cardIds)
        {
            _deck.AddRange(cardIds);
            return this;
        }

        internal void ApplyTo(GameState state, PlayerId id)
        {
            var player = state[id];

            foreach (var (slot, creature) in _creatures)
            {
                state.Board.Place(new SlotIndex(id, slot), creature);
            }

            foreach (var card in _hand)
            {
                player.AddToHand(card);
            }

            player.SetDeck(_deck);
            player.SetResources(_resources);
            player.SetScore(_score);
        }
    }
}
