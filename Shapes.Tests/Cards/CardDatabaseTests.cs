using Shapes.Core.Cards;
using Shapes.Core.Rules;

namespace Shapes.Tests.Cards;

// The loaded card set: id uniqueness, the move-list concatenation that CreatureInstance's
// once-per-turn bitmask indexes into, and deck construction against RuleSet limits.
public class CardDatabaseTests
{
    private static CardDefinition Creature(string id, params string[] moveNames)
    {
        var moves = moveNames
            .Select(n => new MoveDefinition(
                n,
                Core.Primitives.ResourcePool.Of(Core.Primitives.ResourceType.Spike, 1),
                [new Core.Effects.EffectNode("draw", Fixtures.Eff.Args(("amount", 1)))]))
            .ToList();

        return new CardDefinition(
            id, id, CardKind.Creature,
            Core.Primitives.ResourcePool.Of(Core.Primitives.ResourceType.Spike, 1),
            health: 2, types: Core.Primitives.TypeMask.Spike, moves: moves);
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        // Last-one-wins would silently make one card unplayable, and a balance run would
        // report results for a card that never appeared.
        var ex = Assert.Throws<CardLoadException>(
            () => new CardDatabase([Creature("dup"), Creature("dup")]));

        Assert.Contains("dup", ex.Message);
    }

    [Fact]
    public void Cards_are_retrievable_by_id()
    {
        var db = new CardDatabase([Creature("a"), Creature("b")]);

        Assert.Equal("a", db.Get("a").Id);
        Assert.Equal("b", db["b"].Id);
        Assert.True(db.Contains("a"));
        Assert.False(db.Contains("c"));
        Assert.Equal(2, db.Count);
    }

    [Fact]
    public void An_unknown_id_fails_loudly_rather_than_returning_null()
    {
        var db = new CardDatabase([Creature("a")]);

        Assert.Throws<CardLoadException>(() => db.Get("nope"));
    }

    [Fact]
    public void Declaration_order_is_preserved()
    {
        // Deck building iterates this, and a shuffled deck is only reproducible from its seed
        // if the unshuffled deck was built the same way every time.
        var db = new CardDatabase([Creature("c"), Creature("a"), Creature("b")]);

        Assert.Equal(["c", "a", "b"], db.All.Select(x => x.Id));
    }

    [Fact]
    public void Move_count_matches_the_card()
    {
        var db = new CardDatabase([Creature("one", "M1"), Creature("two", "M1", "M2")]);

        Assert.Equal(1, db.MoveCountOf("one"));
        Assert.Equal(2, db.MoveCountOf("two"));
    }

    [Fact]
    public void A_merged_creatures_moves_concatenate_in_merge_order()
    {
        // This ordering is a contract: MovesOf and CreatureInstance.MoveIndexOffset must agree
        // on what index 2 means, or two different moves would share a once-per-turn bit.
        var db = new CardDatabase([Creature("cadet", "Scout", "Rebound"), Creature("medic", "Mend")]);

        var moves = db.MovesOf(["cadet", "medic"]);

        Assert.Equal(["Scout", "Rebound", "Mend"], moves.Select(m => m.Name));
    }

    [Fact]
    public void Move_index_offsets_line_up_with_the_concatenated_list()
    {
        // The two halves of the once-per-turn rule, checked against each other: the offset
        // CreatureInstance computes must be the index at which that card's moves actually
        // start in the list MovesOf returns.
        var db = new CardDatabase([Creature("cadet", "Scout", "Rebound"), Creature("medic", "Mend")]);

        var creature = new Core.State.CreatureInstance("cadet", 2, Core.Primitives.TypeMask.Spike);
        creature.AbsorbMerge(new Core.State.CreatureInstance("medic", 2, Core.Primitives.TypeMask.Spike));

        var moves = db.MovesOf(creature.MergedFrom);

        Assert.Equal(0, creature.MoveIndexOffset(0, db.MoveCountOf));
        Assert.Equal(2, creature.MoveIndexOffset(1, db.MoveCountOf));
        Assert.Equal("Mend", moves[creature.MoveIndexOffset(1, db.MoveCountOf)].Name);
    }

    [Fact]
    public void Symmetric_deck_contains_the_configured_copies_of_every_card()
    {
        var db = new CardDatabase([Creature("a"), Creature("b"), Creature("c")]);

        var deck = db.BuildSymmetricDeck(RuleSet.Default);

        Assert.Equal(3 * RuleSet.Default.CopiesPerCard, deck.Count);
        Assert.Equal(RuleSet.Default.CopiesPerCard, deck.Count(id => id == "a"));
        Assert.Equal(RuleSet.Default.CopiesPerCard, deck.Count(id => id == "c"));
    }

    [Fact]
    public void Symmetric_deck_building_is_deterministic()
    {
        // Unshuffled and order-stable, so the seeded shuffle is the only source of variation.
        var db = new CardDatabase([Creature("a"), Creature("b")]);

        Assert.Equal(db.BuildSymmetricDeck(RuleSet.Default), db.BuildSymmetricDeck(RuleSet.Default));
    }

    private static RuleSet CustomRules(int deckSize, int maxCopies) => new(
        name: "custom", startingHandSize: 4, cardsDrawnPerTurn: 1, handLimit: 8,
        baseIncome: RuleSet.Default.BaseIncome, incomePerCreatureType: 1,
        pointsPerUnopposedCreature: 1, scoreToWin: 10,
        mergeEnabled: true, mergeRequiresAdjacent: true, mergeCostsAction: false, maxMergeDepth: 2,
        deckMode: DeckMode.Custom, copiesPerCard: 0, deckSize: deckSize, maxCopiesPerCard: maxCopies,
        typeChart: TypeChart.Default);

    [Fact]
    public void A_custom_deck_within_the_limits_validates()
    {
        var db = new CardDatabase([Creature("a"), Creature("b")]);
        var deck = new[] { "a", "a", "b", "b" };

        db.ValidateDeck(deck, CustomRules(deckSize: 4, maxCopies: 2));
    }

    [Fact]
    public void A_deck_of_the_wrong_size_is_rejected()
    {
        var db = new CardDatabase([Creature("a")]);

        var ex = Assert.Throws<CardValidationException>(
            () => db.ValidateDeck(["a", "a"], CustomRules(deckSize: 4, maxCopies: 4)));

        Assert.Contains("4", ex.Message);
    }

    [Fact]
    public void A_deck_exceeding_max_copies_is_rejected()
    {
        var db = new CardDatabase([Creature("a"), Creature("b")]);

        var ex = Assert.Throws<CardValidationException>(
            () => db.ValidateDeck(["a", "a", "a", "b"], CustomRules(deckSize: 4, maxCopies: 2)));

        Assert.Contains("a", ex.Message);
    }

    [Fact]
    public void A_deck_naming_an_unknown_card_is_rejected()
    {
        var db = new CardDatabase([Creature("a")]);

        // Deck size 4, not 2: RuleSet requires deckSize >= startingHandSize, so a smaller
        // deck fails ruleset construction before ValidateDeck is ever reached.
        var ex = Assert.Throws<CardValidationException>(
            () => db.ValidateDeck(["a", "a", "a", "ghost"], CustomRules(deckSize: 4, maxCopies: 4)));

        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public void Deck_helpers_reject_a_ruleset_in_the_wrong_mode()
    {
        // Calling BuildSymmetricDeck on a custom ruleset (or vice versa) means the caller has
        // misread the configuration -- a silent wrong-sized deck would be far worse.
        var db = new CardDatabase([Creature("a")]);

        Assert.Throws<InvalidOperationException>(
            () => db.BuildSymmetricDeck(CustomRules(deckSize: 4, maxCopies: 2)));
        Assert.Throws<InvalidOperationException>(
            () => db.ValidateDeck(["a"], RuleSet.Default));
    }
}
