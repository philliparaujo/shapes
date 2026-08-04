using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Cards;

// A generated smoke test per real card: it can be legally played from a suitable state, and
// (for a creature) each of its moves is a legal action once it is on the board. Cheap across
// the whole ~36-card set, and it is the layer that catches the case CardValidator cannot: a
// misspelled EFFECT ARGUMENT (e.g. "amnount") is only caught by the op itself throwing when it
// runs, not at load time (see CardValidator's notes on why args cannot be schema-checked the
// way "op" and "target" are). Without this test, that class of typo would ship silently until
// the first time a real game happened to use the broken move.
public class CardSmokeTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    // A generous ruleset: enough resources and slots that affordability and board space are
    // never the reason a card fails to play. The smoke test is about the card's OWN shape, not
    // about a resource-starved edge case (those are the property suite's job).
    private static readonly ResourcePool AmpleResources = new(20, 20, 20);

    public static IEnumerable<object[]> AllCardIds() => Cards.All.Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(AllCardIds))]
    public void Every_card_is_playable_from_a_suitable_state(string cardId)
    {
        var card = Cards.Get(cardId);
        var state = BuildSuitableState(card);

        var playAction = ActionGenerator.Generate(state, Cards)
            .OfType<PlayCardAction>()
            .FirstOrDefault(a => a.CardId == cardId);

        Assert.True(
            playAction is not null,
            $"'{cardId}' was not offered as a legal play from a state built to support it.");

        var ex = Record.Exception(() => ActionExecutor.Apply(state, Cards, playAction!));
        Assert.True(ex is null, $"Playing '{cardId}' threw {ex?.GetType().Name}: {ex?.Message}");
    }

    [Theory]
    [MemberData(nameof(AllCardIds))]
    public void Every_move_on_every_creature_card_is_usable(string cardId)
    {
        var card = Cards.Get(cardId);
        if (!card.IsCreature)
        {
            return;
        }

        for (var moveIndex = 0; moveIndex < card.Moves.Count; moveIndex++)
        {
            var move = card.Moves[moveIndex];

            // A move's own condition (if any) may require the source at full health (Circle
            // Priest, Circle Cadet) or damaged (Monk, Relic, T Medic) -- try both rather than
            // picking one and having the other class of card fail this test through no fault
            // of its own.
            var action = TryFindMoveAction(card, moveIndex, atFullHealth: true, out var state)
                ?? TryFindMoveAction(card, moveIndex, atFullHealth: false, out state);

            Assert.True(
                action is not null,
                $"'{cardId}' move '{move.Name}' (index {moveIndex}) was not offered as a legal action "
                + "at full health or damaged.");

            var ex = Record.Exception(() => ActionExecutor.Apply(state!, Cards, action!));
            Assert.True(
                ex is null,
                $"'{cardId}' move '{move.Name}' threw {ex?.GetType().Name}: {ex?.Message}");
        }
    }

    private static UseMoveAction? TryFindMoveAction(
        CardDefinition card, int moveIndex, bool atFullHealth, out GameState? state)
    {
        state = BuildSuitableState(card);
        var health = atFullHealth ? card.Health : Math.Max(1, card.Health - 1);
        state.Board.Place(
            new SlotIndex(PlayerId.One, 0), new CreatureInstance(card.Id, card.Health, card.Types, health));

        return ActionGenerator.Generate(state, Cards)
            .OfType<UseMoveAction>()
            .FirstOrDefault(a => a.SourceSlot == new SlotIndex(PlayerId.One, 0) && a.MoveIndex == moveIndex);
    }

    // A board generous enough that every move's targeting selector and every condition in the
    // real card set has something to act on: a damaged, unopposed, full-health, and empty-slot
    // situation are all represented somewhere. Built once per card rather than shared, since
    // playing/using a move mutates it.
    // A real, unremarkable creature card used purely as filler for the OTHER slots -- board
    // occupants must be real card ids, since ActionGenerator looks up their moves via
    // CardDatabase.MovesOf.
    private const string FillerCardId = "basic_square";

    private static GameState BuildSuitableState(CardDefinition card)
    {
        var filler = Cards.Get(FillerCardId);
        var state = new StateBuilder()
            .WithRuleSet(GenerousRules())
            .P1(p => p.Hand(card.Id).Resources(AmpleResources.Spike, AmpleResources.Anvil, AmpleResources.Wheel)
                      .Deck(FillerCardId, FillerCardId, FillerCardId, FillerCardId, FillerCardId))
            .P2(p => p.Slot(1, filler.Id, filler.Types, maxHealth: filler.Health, health: filler.Health)
                      .Slot(2, filler.Id, filler.Types, maxHealth: filler.Health, health: 1))
            .Build();

        // Slot 0 stays empty on P1 so a creature card always has somewhere to land; when this
        // method is also used to seed a creature already on the board (the move-usability
        // test), that creature occupies slot 0 and a second friendly at slot 1 gives
        // left_friendly/right_friendly selectors something to resolve.
        Place(state, PlayerId.One, 1, damagedFriendly: true);

        return state;
    }

    private static void Place(GameState state, PlayerId player, int slot, bool damagedFriendly)
    {
        var filler = Cards.Get(FillerCardId);
        var health = damagedFriendly ? Math.Max(1, filler.Health - 1) : filler.Health;
        var creature = new CreatureInstance(filler.Id, filler.Health, filler.Types, health);
        state.Board.Place(new SlotIndex(player, slot), creature);
    }

    private static RuleSet GenerousRules() => RuleSetTestHelper.WithHandLimit(20);
}
