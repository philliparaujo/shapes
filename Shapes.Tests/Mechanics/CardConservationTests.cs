using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Mechanics;

// Cards are physical objects: one that exists must be findable somewhere.
//
// The invariant, per player, in symmetric-deck mode:
//
//     hand + deck + discard + (cards on the board, expanded through MergedFrom)
//         == the starting deck, as a MULTISET
//
// This is the premise the step 2.3 determinizer's accounting rests on. It reconstructs the
// opponent's unseen cards by subtracting what it can see (their discard, their board) from the
// decklist both players share, and deals the remainder into a hand and deck of the observed
// sizes. If a card can leave the game without landing in a visible zone, that subtraction
// over-counts and the determinizer samples opponent hands containing cards that are physically
// dead -- exactly the "sampling a deck containing a card already in the graveyard" bug DESIGN.md
// calls out as silently degrading play.
//
// It failed before GameState.DestroyCreature existed: a destroyed creature was removed from the
// board and logged as a turn event, but its card went nowhere. That is invisible in ordinary
// play (almost nothing reads a discard pile) which is why it survived Phase 1 -- these tests
// exist so it cannot come back.
//
// MULTISET, not set: with copiesPerCard = 2, seeing one copy of a card in a discard pile means
// one copy remains, not none. Set-based accounting is the plausible regression here and would
// pass a naive "are the same card ids present" check, so every assertion below counts.
public class CardConservationTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static RuleSet Rules => RuleSet.Default;

    // These tests use synthetic card ids ("base", "folded", "token", ...) that deliberately do
    // not exist in Cards, so they can exercise discard accounting without depending on real card
    // data. AbsorbMerge only needs move counts to shift the used-move bitmask, which none of them
    // assert on -- Cards.MoveCountOf would throw on the made-up ids.
    private static int AnyMoveCount(string cardId) => 2;

    // -- The rule itself, stated directly -------------------------------------------------------

    [Fact]
    public void A_creature_destroyed_by_damage_goes_to_its_owners_discard()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "attacker", TypeMask.Spike))
            .P2(p => p.Slot(0, "victim", TypeMask.Anvil, maxHealth: 1))
            .Build();

        // Through the real action path, not Board.RemoveDead directly: the discard is the
        // executor's post-effect sweep talking to GameState.DestroyCreature, and testing the
        // sweep in isolation would not prove the two are wired together.
        var move = new MoveDefinition(
            "Strike", ResourcePool.Empty,
            [Eff.Node("damage", ("target", "opposing"), ("amount", 5))], condition: null);

        ApplyMove(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), move);

        Assert.True(state.Board.IsEmpty(new SlotIndex(PlayerId.Two, 0)));
        Assert.Equal(["victim"], state[PlayerId.Two].Discard);

        // The attacker's owner gains nothing -- the card goes to the DEAD creature's owner.
        Assert.Empty(state[PlayerId.One].Discard);
    }

    [Fact]
    public void A_creature_removed_by_the_destroy_op_goes_to_its_owners_discard()
    {
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "victim", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy", ("target", "opposing")), ctx);

        Assert.Equal(["victim"], state[PlayerId.Two].Discard);
    }

    [Fact]
    public void A_creature_removed_by_destroy_refund_cost_goes_to_its_owners_discard()
    {
        // The refund path is a separate op sharing one helper; it discards through the same
        // route, so the resource refund and the card's destination are independent.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, "victim", TypeMask.Anvil))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy_refund_cost", ("target", "opposing")), ctx);

        Assert.Equal(["victim"], state[PlayerId.Two].Discard);
    }

    [Fact]
    public void A_destroyed_merged_creature_discards_every_card_folded_into_it()
    {
        // Two physical cards occupy one slot, and both die at once. Discarding only the surviving
        // instance's CardId would lose the other -- the merge-shaped version of the same leak,
        // and the reason DestroyCreature iterates MergedFrom rather than reading CardId.
        var merged = new CreatureInstance("base", 3, TypeMask.Spike);
        merged.AbsorbMerge(new CreatureInstance("folded", 3, TypeMask.Anvil), AnyMoveCount);

        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, merged))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy", ("target", "opposing")), ctx);

        Assert.Equal(["base", "folded"], state[PlayerId.Two].Discard);
    }

    [Fact]
    public void A_destroyed_token_discards_nothing()
    {
        // A summoned token was never a card: its id need not name anything in the CardDatabase,
        // and it came from no one's deck. Putting it in a discard pile would inflate that
        // player's apparent card pool -- the same accounting error as the vanishing card, in the
        // opposite direction.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(
            Eff.Node("summon", ("target", "all_friendlies"), ("card_id", "token"),
                ("health", 1), ("types", "spike")),
            ctx);

        var tokenSlot = new SlotIndex(PlayerId.One, 1);
        Assert.True(state.Board[tokenSlot]!.IsToken);

        EffectInterpreter.Apply(
            Eff.Node("destroy", ("target", "chosen_friendly")),
            new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), tokenSlot));

        Assert.True(state.Board.IsEmpty(tokenSlot));
        Assert.Empty(state[PlayerId.One].Discard);
    }

    [Fact]
    public void Merging_a_token_taints_the_result_so_nothing_is_discarded()
    {
        // MergedFrom would otherwise hold a mix of real card ids and a token id, and there is no
        // per-id provenance to tell them apart at death. Tainting the stack is the conservative
        // reading: better to under-count a player's discard than to put a non-card id in it.
        var merged = new CreatureInstance("real_card", 3, TypeMask.Spike);
        merged.AbsorbMerge(new CreatureInstance("token", 1, TypeMask.Anvil, isToken: true), AnyMoveCount);

        Assert.True(merged.IsToken);

        var state = new StateBuilder()
            .P1(p => p.Slot(0, "caster", TypeMask.Wheel))
            .P2(p => p.Slot(0, merged))
            .Build();
        var ctx = new EffectContext(state, PlayerId.One, new SlotIndex(PlayerId.One, 0), null);

        EffectInterpreter.Apply(Eff.Node("destroy", ("target", "opposing")), ctx);

        Assert.Empty(state[PlayerId.Two].Discard);
    }

    [Fact]
    public void Merging_does_not_discard_the_absorbed_creature()
    {
        // The counterpart to the rule above: a merge REMOVES a creature from a slot without
        // destroying it. Its card is not gone -- it lives on inside the merged creature's
        // MergedFrom, and will be discarded when that creature dies. Discarding here would
        // double-count it.
        // Real card ids here, unlike the synthetic ones elsewhere in this file: this goes through
        // ActionExecutor, which looks the merged creature's cards up to shift its used-move bits.
        var state = new StateBuilder()
            .P1(p => p.Slot(0, "basic_t", TypeMask.Spike).Slot(1, "t_body", TypeMask.Spike))
            .Build();

        ActionExecutor.Apply(
            state, Cards,
            new MergeAction(PlayerId.One, new SlotIndex(PlayerId.One, 0), new SlotIndex(PlayerId.One, 1)));

        Assert.Empty(state[PlayerId.One].Discard);
        Assert.Equal(
            ["t_body", "basic_t"],
            state.Board[new SlotIndex(PlayerId.One, 1)]!.MergedFrom);
    }

    // -- The conservation identity, over real games ---------------------------------------------

    [Fact]
    public void Every_card_is_accounted_for_across_thousands_of_random_games()
    {
        // The scale-up: the hand-written tests above pin the RULE, this pins the CONSEQUENCE
        // across every path real cards can take -- including ones no hand-written test
        // anticipated. This is the assertion the determinizer's correctness actually rests on.
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            PlayRandomGame(seed, AssertCardsAreConserved);
        }
    }

    [Fact]
    public void Random_play_actually_destroys_creatures()
    {
        // Guards the invariant above against passing vacuously. If no creature ever died, the
        // conservation identity would hold trivially and prove nothing about the destruction
        // path -- which is the exact path that used to be broken.
        var destroyed = 0;

        for (ulong seed = 1; seed <= 50; seed++)
        {
            PlayRandomGame(seed, state =>
                destroyed += state[PlayerId.One].Discard.Count + state[PlayerId.Two].Discard.Count);
        }

        Assert.True(
            destroyed > 0,
            "No creature was destroyed across 50 random games, so the conservation invariant is "
            + "not exercising the destruction path it exists to protect.");
    }

    private static void AssertCardsAreConserved(GameState state)
    {
        var startingDeck = Cards.BuildSymmetricDeck(Rules);

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];

            var accounted = new List<string>();
            accounted.AddRange(player.Hand);
            accounted.AddRange(player.Deck);
            accounted.AddRange(player.Discard);

            foreach (var (_, creature) in state.Board.CreaturesOf(playerId))
            {
                // Tokens are excluded on both sides of the identity: they were never in the
                // starting deck, so counting them here would break it just as surely as a
                // vanishing card does.
                if (!creature.IsToken)
                {
                    accounted.AddRange(creature.MergedFrom);
                }
            }

            AssertSameMultiset(startingDeck, accounted, playerId);
        }
    }

    private static void AssertSameMultiset(
        IReadOnlyList<string> expected, IReadOnlyList<string> actual, PlayerId player)
    {
        var expectedCounts = CountBy(expected);
        var actualCounts = CountBy(actual);

        foreach (var (cardId, count) in expectedCounts)
        {
            var found = actualCounts.GetValueOrDefault(cardId);
            Assert.True(
                found == count,
                $"{player}: expected {count} copies of '{cardId}' across hand/deck/discard/board, "
                + $"found {found}. A card has been created or has vanished.");
        }

        foreach (var (cardId, count) in actualCounts)
        {
            Assert.True(
                expectedCounts.ContainsKey(cardId),
                $"{player}: found {count} copies of '{cardId}', which is not in the starting deck "
                + "at all -- a card was created from nothing.");
        }
    }

    private static Dictionary<string, int> CountBy(IReadOnlyList<string> cards)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cardId in cards)
        {
            counts[cardId] = counts.GetValueOrDefault(cardId) + 1;
        }

        return counts;
    }

    // -- Harness -------------------------------------------------------------------------------

    private static void PlayRandomGame(ulong seed, Action<GameState> check)
    {
        var random = new SeededRandom(seed);
        var state = new GameState(Rules, random, PlayerId.One);

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(Cards.BuildSymmetricDeck(Rules));
            player.ShuffleDeck(random);
            player.Draw(Rules.StartingHandSize);
        }

        state.AdvanceToActions();

        for (var i = 0; i < 2000 && !state.IsOver; i++)
        {
            var actions = ActionGenerator.Generate(state, Cards);
            if (actions.Count == 0)
            {
                break;
            }

            check(state);
            ActionExecutor.Apply(state, Cards, actions[random.Next(actions.Count)]);
        }

        check(state);
    }

    // Runs `move` from the creature in `source`. The synthetic card's id must match that
    // creature's CardId, since CardDatabase.MovesOf resolves a creature's moves by looking its
    // MergedFrom ids up in the database.
    private static void ApplyMove(
        GameState state, PlayerId player, SlotIndex source, MoveDefinition move)
    {
        var creature = state.Board[source]!;

        var card = new CardDefinition(
            id: creature.CardId, name: "Synthetic Mover", kind: CardKind.Creature,
            cost: ResourcePool.Empty, health: creature.MaxHealth, types: creature.Types,
            moves: [move]);

        ActionExecutor.Apply(state, new CardDatabase([card]), new UseMoveAction(player, source, 0));
    }
}
