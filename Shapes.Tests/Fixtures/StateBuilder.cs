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

        return state;
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
