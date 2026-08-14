using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Tests.Cards;

// Deck construction: the default one-of-each deck, explicit custom lists, and the constrained
// random generator. The invariant under all of it is that every game is played with a Deck --
// these pin what a legal one is.
public class DeckBuilderTests
{
    // A card set big enough to build a real 40-card deck from: 20 cards across the three types
    // at a spread of costs, so the type minimums and the cost tolerance are both satisfiable
    // and both capable of failing. TestCards is too small and too uniform to exercise either.
    private static CardDatabase BuildSet(int perType = 6)
    {
        var cards = new List<CardDefinition>();

        foreach (var (type, mask) in new[]
        {
            (ResourceType.Spike, TypeMask.Spike),
            (ResourceType.Anvil, TypeMask.Anvil),
            (ResourceType.Wheel, TypeMask.Wheel),
        })
        {
            for (var i = 0; i < perType; i++)
            {
                // Costs cycle 1..3 so the set has a spread rather than one price -- a generator
                // that ignored cost would still pass a tolerance check against a flat set.
                cards.Add(new CardDefinition(
                    $"{type}_{i}".ToLowerInvariant(), $"{type} {i}", CardKind.Creature,
                    ResourcePool.Of(type, (i % 3) + 1), health: 2, types: mask,
                    moves: [new MoveDefinition(
                        "hit", ResourcePool.Of(type, 1),
                        [new Core.Effects.EffectNode("draw", Fixtures.Eff.Args(("amount", 1)))])]));
            }
        }

        return new CardDatabase(cards);
    }

    private static RuleSet Rules(int deckSize = 40, int maxCopies = 3) =>
        Fixtures.RuleSetTestHelper.CustomDeck(deckSize: deckSize, maxCopiesPerCard: maxCopies);

    [Fact]
    public void Default_deck_is_exactly_one_of_every_card()
    {
        var cards = BuildSet();
        var deck = DeckBuilder.Default(cards);

        Assert.Equal(cards.Count, deck.Count);
        Assert.All(cards.All, card => Assert.Equal(1, deck.CopiesOf(card.Id)));
    }

    [Fact]
    public void Default_deck_is_not_bound_by_the_rulesets_deck_size()
    {
        // The console's only deck is one-of-each, whatever that totals -- deliberately exempt
        // from DeckSize, since sizing it to 40 would mean either duplicating cards or dropping
        // some, and both defeat the point of a deck that contains every card.
        var cards = BuildSet();
        var deck = DeckBuilder.Default(cards);

        Assert.NotEqual(40, deck.Count);
        Assert.Equal(cards.Count, deck.Count);
    }

    [Fact]
    public void Default_deck_order_is_deterministic()
    {
        // Seeded replay depends on the pre-shuffle order being identical across runs.
        var cards = BuildSet();

        Assert.Equal(DeckBuilder.Default(cards).Cards, DeckBuilder.Default(cards).Cards);
    }

    [Fact]
    public void Custom_deck_accepts_a_legal_list()
    {
        // 18 cards x2 = 36, plus 4 more copies to reach exactly 40, all within the 3-copy limit.
        var cards = BuildSet();
        var ids = cards.All.SelectMany(c => Enumerable.Repeat(c.Id, 2))
            .Concat(cards.All.Take(4).Select(c => c.Id))
            .ToList();

        var deck = DeckBuilder.Custom("legal", ids, cards, Rules());

        Assert.Equal(40, deck.Count);
        Assert.Equal("legal", deck.Name);
    }

    [Fact]
    public void Custom_deck_rejects_the_wrong_size()
    {
        var cards = BuildSet();
        var ids = cards.All.Take(10).Select(c => c.Id).ToList();

        var ex = Assert.Throws<DeckBuildException>(
            () => DeckBuilder.Custom("short", ids, cards, Rules()));

        Assert.Contains("10", ex.Message);
        Assert.Contains("40", ex.Message);
    }

    [Fact]
    public void Custom_deck_rejects_too_many_copies()
    {
        var cards = BuildSet();

        // 4 copies of one card against a 3-copy limit. Size is left correct at 40 so the copy
        // limit is unambiguously what fails -- 17 other cards x2 = 34, plus 2 more, plus the 4.
        var ids = Enumerable.Repeat(cards.All[0].Id, 4)
            .Concat(cards.All.Skip(1).SelectMany(c => Enumerable.Repeat(c.Id, 2)))
            .Concat(cards.All.Skip(1).Take(2).Select(c => c.Id))
            .ToList();

        var ex = Assert.Throws<DeckBuildException>(
            () => DeckBuilder.Custom("greedy", ids, cards, Rules()));

        Assert.Contains("4 copies", ex.Message);
    }

    [Fact]
    public void Custom_deck_rejects_unknown_card_ids()
    {
        var cards = BuildSet();
        var ids = Enumerable.Repeat("no_such_card", 40).ToList();

        var ex = Assert.Throws<DeckBuildException>(
            () => DeckBuilder.Custom("ghosts", ids, cards, Rules()));

        Assert.Contains("no_such_card", ex.Message);
    }

    [Fact]
    public void Random_deck_is_legal_for_the_ruleset()
    {
        var cards = BuildSet();
        var rules = Rules();

        var deck = DeckBuilder.Random("r", cards, rules, new SeededRandom(1));

        Assert.Equal(40, deck.Count);
        DeckBuilder.Validate(deck, cards, rules);
        Assert.All(deck.CountsById().Values, count => Assert.True(count <= 3));
    }

    [Fact]
    public void Random_deck_meets_the_minimum_per_type()
    {
        var cards = BuildSet();
        var constraints = new DeckBuilder.RandomDeckConstraints { MinPerType = 10 };

        // Several seeds: a single one passing could be luck rather than the seeding working.
        for (var seed = 1UL; seed <= 10; seed++)
        {
            var deck = DeckBuilder.Random(
                $"r{seed}", cards, Rules(), new SeededRandom(seed), constraints: constraints);

            var counts = DeckBuilder.TypeCounts(deck.Cards, cards);
            Assert.True(counts[ResourceType.Spike] >= 10, $"spike={counts[ResourceType.Spike]}");
            Assert.True(counts[ResourceType.Anvil] >= 10, $"anvil={counts[ResourceType.Anvil]}");
            Assert.True(counts[ResourceType.Wheel] >= 10, $"wheel={counts[ResourceType.Wheel]}");
        }
    }

    [Fact]
    public void Random_deck_stays_within_the_cost_tolerance_of_the_reference()
    {
        var cards = BuildSet();
        var reference = DeckBuilder.Default(cards);
        var target = DeckBuilder.MeanCost(reference.Cards, cards);
        var constraints = new DeckBuilder.RandomDeckConstraints { CostTolerance = 0.2 };

        for (var seed = 1UL; seed <= 10; seed++)
        {
            var deck = DeckBuilder.Random(
                $"r{seed}", cards, Rules(), new SeededRandom(seed), reference, constraints);

            var cost = DeckBuilder.MeanCost(deck.Cards, cards);
            Assert.True(
                Math.Abs(cost - target) <= 0.2 + 1e-9,
                $"seed {seed}: mean cost {cost:F3} vs target {target:F3}");
        }
    }

    [Fact]
    public void Random_deck_is_reproducible_from_its_seed()
    {
        // The whole balance pipeline rests on this: a run is only comparable to another run if
        // the same seed produced the same decks.
        var cards = BuildSet();

        var first = DeckBuilder.Random("r", cards, Rules(), new SeededRandom(99));
        var second = DeckBuilder.Random("r", cards, Rules(), new SeededRandom(99));

        Assert.Equal(first.Cards, second.Cards);
    }

    [Fact]
    public void Different_seeds_give_different_random_decks()
    {
        var cards = BuildSet();

        var a = DeckBuilder.Random("a", cards, Rules(), new SeededRandom(1));
        var b = DeckBuilder.Random("b", cards, Rules(), new SeededRandom(2));

        Assert.NotEqual(a.Cards, b.Cards);
    }

    [Fact]
    public void Random_deck_throws_when_the_card_set_is_too_small()
    {
        // 5 cards at 3 copies is 15 available against a 40-card deck -- unsatisfiable, and worth
        // a clear message rather than an attempt loop that fails obscurely.
        var cards = BuildSet(perType: 2);

        var ex = Assert.Throws<DeckBuildException>(
            () => DeckBuilder.Random("r", cards, Rules(), new SeededRandom(1)));

        Assert.Contains("40-card deck", ex.Message);
    }

    [Fact]
    public void Random_deck_throws_when_constraints_are_unsatisfiable()
    {
        var cards = BuildSet();

        // 20 of each type out of 40 cards is 60 type-slots in a 40-card deck: impossible for a
        // single-typed set, and it must fail loudly rather than return a deck that violates it.
        var constraints = new DeckBuilder.RandomDeckConstraints { MinPerType = 20, MaxAttempts = 5 };

        Assert.Throws<DeckBuildException>(
            () => DeckBuilder.Random("r", cards, Rules(), new SeededRandom(1), constraints: constraints));
    }

    [Fact]
    public void Shuffled_does_not_mutate_the_deck()
    {
        // A Deck is shared across both seats and every game in a batch -- shuffling in place
        // would have seat two draw from seat one's shuffle.
        var cards = BuildSet();
        var deck = DeckBuilder.Default(cards);
        var before = deck.Cards.ToList();

        var shuffled = deck.Shuffled(new SeededRandom(5));

        Assert.Equal(before, deck.Cards);
        Assert.Equal(before.Count, shuffled.Count);
        Assert.Equal(before.OrderBy(x => x), shuffled.OrderBy(x => x));
    }

    [Fact]
    public void CopiesOf_counts_duplicates()
    {
        var deck = new Deck("d", ["a", "b", "a", "a"]);

        Assert.Equal(3, deck.CopiesOf("a"));
        Assert.Equal(1, deck.CopiesOf("b"));
        Assert.Equal(0, deck.CopiesOf("c"));
        Assert.True(deck.Contains("a"));
        Assert.False(deck.Contains("c"));
    }

    [Fact]
    public void Empty_deck_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Deck("empty", []));
    }
}
