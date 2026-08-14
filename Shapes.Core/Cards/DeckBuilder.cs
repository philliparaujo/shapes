using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Core.Cards;

// Thrown when a deck cannot be built or is not legal for a ruleset. Distinct from
// CardValidationException (which is about a card's own definition being malformed) because the
// card set here is fine -- it is the SELECTION from it that is wrong, and the two are fixed in
// different places: one in Shapes.Content/cards, the other in whatever built the deck.
public sealed class DeckBuildException : Exception
{
    public DeckBuildException(string message) : base(message)
    {
    }
}

// Builds and validates decks: the default one-of-each, an explicit custom list, and a randomly
// generated one constrained to resemble the default.
//
// Separate from CardDatabase because these are POLICIES over a card set, not properties of it.
// CardDatabase.BuildSymmetricDeck stays where it is (the determinizer and every symmetric-ruleset
// caller still want it); this is the layer that turns a ruleset plus an intent into a Deck.
public static class DeckBuilder
{
    // The default deck: exactly ONE copy of every card in the set.
    //
    // Deliberately exempt from RuleSet.DeckSize -- it is however many cards exist, currently ~36,
    // not 40. That exemption is the point: the console's only deck is this one, and it is the
    // deck every card is guaranteed to appear in, so a console game exercises the whole card set
    // rather than a 40-card slice of it. Sizing it to DeckSize would mean either duplicating cards
    // (no longer one-of-each) or excluding some (no longer every card).
    //
    // Card order is CardDatabase.All's order, which is filename order for a directory load, so
    // this is deterministic across runs -- required for seeded replay (see Deck's class note).
    public static Deck Default(CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        return new Deck("default", cards.All.Select(c => c.Id));
    }

    // An explicit decklist, validated against the ruleset's size and copy limits.
    public static Deck Custom(string name, IEnumerable<string> cardIds, CardDatabase cards, RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rules);

        var deck = new Deck(name, cardIds);
        Validate(deck, cards, rules);
        return deck;
    }

    // Checks a deck against the ruleset: every id known, exactly DeckSize cards, no more than
    // MaxCopiesPerCard of any one.
    //
    // Unlike CardDatabase.ValidateDeck this does NOT require rules.DeckMode == Custom. The default
    // deck and the sim's random decks are real decks played under the shipping symmetric ruleset,
    // and gating validation on deck mode would leave exactly those unvalidated. Size/copy limits
    // are read off the ruleset regardless of mode -- see DeckSizeOf.
    public static void Validate(Deck deck, CardDatabase cards, RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rules);

        var counts = deck.CountsById();

        foreach (var cardId in counts.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!cards.Contains(cardId))
            {
                throw new DeckBuildException(
                    $"Deck '{deck.Name}' contains unknown card id '{cardId}'.");
            }
        }

        var maxCopies = MaxCopiesOf(rules);
        foreach (var (cardId, count) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (count > maxCopies)
            {
                throw new DeckBuildException(
                    $"Deck '{deck.Name}' contains {count} copies of '{cardId}'; the limit is {maxCopies}.");
            }
        }

        var size = DeckSizeOf(rules);
        if (deck.Count != size)
        {
            throw new DeckBuildException(
                $"Deck '{deck.Name}' has {deck.Count} cards; ruleset '{rules.Name}' requires exactly {size}.");
        }
    }

    // Deck size for a ruleset, falling back to the standard 40 when the ruleset does not set one.
    //
    // The shipping symmetric ruleset leaves DeckSize at 0 (it had no use for it -- a symmetric
    // deck is "the whole card set, CopiesPerCard times"), so a random deck built under it would
    // otherwise be asked for zero cards. Falling back keeps constructed decks a coherent 40 under
    // every shipping ruleset while still letting a custom ruleset say otherwise.
    public const int StandardDeckSize = 40;

    public const int StandardMaxCopiesPerCard = 3;

    public static int DeckSizeOf(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.DeckSize > 0 ? rules.DeckSize : StandardDeckSize;
    }

    public static int MaxCopiesOf(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.MaxCopiesPerCard > 0 ? rules.MaxCopiesPerCard : StandardMaxCopiesPerCard;
    }

    // Constraints a generated deck must satisfy. Defaults are the ones the task specifies: stay
    // within +/-0.2 of the default deck's average card cost, and run at least 10 cards of each
    // creature type.
    public sealed record RandomDeckConstraints
    {
        // How far a generated deck's mean card cost (total pips, all three resources) may sit from
        // the reference deck's. Keeps generated decks economically comparable to the default, so a
        // batch's win rates reflect card quality rather than one seat drawing a deck it cannot
        // afford to play.
        public double CostTolerance { get; init; } = 0.2;

        // Minimum cards DEMANDING each resource type, counted by PLAY COST.
        //
        // The constraint is about RESOURCE DEMAND, not board typing: income arrives as three
        // separate pools, so a deck whose cards nearly all cost spike drains spike dry while anvil
        // and wheel pile up unspent. That deck is bottlenecked on one resource and idle on two,
        // and a batch full of those measures the income curve rather than the cards.
        //
        // Counted by cost means SPELLS COUNT TOO. A spell costing 3 spike draws the spike pool
        // down exactly as a 3-spike creature does -- the pool cannot tell them apart, so a
        // constraint that ignored spells would be measuring something other than what it is for.
        // (This was the original bug: counting creature TypeMask excluded every spell from the
        // seeding phase, which quietly held spells to ~6% of a deck against the 25% an unbiased
        // draw gives.)
        //
        // CardValidator guarantees every cost is single-type, so each card contributes to exactly
        // one type here.
        public int MinPerType { get; init; } = 10;

        // Attempts before giving up. Generation is rejection-sampled (build a candidate, test it),
        // so this bounds the work rather than the quality. A failure here means the constraints
        // are unsatisfiable for this card set, which is a caller error worth reporting loudly
        // rather than silently returning a deck that violates them.
        public int MaxAttempts { get; init; } = 200;
    }

    // Builds a random legal deck constrained to resemble `reference` (normally the default deck).
    //
    // REJECTION SAMPLING, not repair: build a whole candidate uniformly at random within the copy
    // limit, test it against the constraints, and start over if it misses. The alternative --
    // generating freely then swapping cards until the constraints hold -- biases the result toward
    // whatever the repair step reaches for, which for a cost constraint means systematically
    // over-picking the cheapest cards that close the gap. Rejection keeps every accepted deck a
    // uniform draw from the set of decks that satisfy the constraints, which is what makes a batch
    // of them a fair sample of "legal decks" rather than of the generator's repair heuristic.
    //
    // The type minimum is enforced by SEEDING those cards first (drawing the required count per
    // type before filling the remainder freely) rather than by rejection alone: with 3 types at 10
    // each out of 40 cards, uniform draws satisfy all three minimums so rarely that rejection
    // alone would burn every attempt. Seeding is uniform WITHIN each type, so the bias it
    // introduces is exactly the constraint the caller asked for and nothing more.
    public static Deck Random(
        string name, CardDatabase cards, RuleSet rules, IRandomSource random,
        Deck? reference = null, RandomDeckConstraints? constraints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        constraints ??= new RandomDeckConstraints();
        reference ??= Default(cards);

        var size = DeckSizeOf(rules);
        var maxCopies = MaxCopiesOf(rules);

        if (cards.Count * maxCopies < size)
        {
            throw new DeckBuildException(
                $"Cannot build a {size}-card deck: the set has {cards.Count} cards at "
                + $"{maxCopies} copies each ({cards.Count * maxCopies} cards available).");
        }

        var targetCost = MeanCost(reference.Cards, cards);

        for (var attempt = 0; attempt < constraints.MaxAttempts; attempt++)
        {
            var candidate = BuildCandidate(cards, random, size, maxCopies, constraints);
            if (candidate is null)
            {
                continue;
            }

            var cost = MeanCost(candidate, cards);
            if (Math.Abs(cost - targetCost) > constraints.CostTolerance)
            {
                continue;
            }

            var deck = new Deck(name, candidate);
            Validate(deck, cards, rules);
            return deck;
        }

        throw new DeckBuildException(
            $"Could not generate a deck satisfying the constraints in {constraints.MaxAttempts} attempts "
            + $"(target mean cost {targetCost:F2} +/-{constraints.CostTolerance}, "
            + $"min {constraints.MinPerType} creatures per type). The card set may not support them.");
    }

    // One candidate deck: type minimums seeded first, remainder filled uniformly. Returns null
    // when the type minimums cannot be met from this card set, which the caller treats as a failed
    // attempt rather than an error -- the type seeding draws without replacement past the copy
    // limit, so an unlucky draw order can exhaust a type legitimately.
    private static List<string>? BuildCandidate(
        CardDatabase cards, IRandomSource random, int size, int maxCopies,
        RandomDeckConstraints constraints)
    {
        var deck = new List<string>(size);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Seed each type's minimum. Ordered by ResourceType so the draw sequence is deterministic
        // for a given seed -- iterating a set here would make the same seed produce different
        // decks across runs.
        foreach (var type in new[] { ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel })
        {
            // Every card whose PLAY COST demands this type -- creatures and spells alike, since
            // both draw down the same pool. Drawing the seeding pool from creatures only is what
            // previously starved spells of deck slots; see RandomDeckConstraints.MinPerType.
            var pool = cards.All
                .Where(c => DemandsType(c, type))
                .Select(c => c.Id)
                .ToList();

            if (pool.Count == 0)
            {
                return null;
            }

            // Counted fresh each pass rather than tracked incrementally: a card added while
            // seeding Spike can also count toward a later type if it demands more than one, so a
            // later type may already be partly satisfied. Recounting is what lets those overlaps
            // reduce the work instead of over-filling the deck. (No shipping card has a mixed
            // cost -- CardValidator forbids it -- but the recount costs nothing and keeps this
            // correct if that ever changes.)
            while (CountOfType(deck, cards, type) < constraints.MinPerType)
            {
                if (deck.Count >= size)
                {
                    return null;
                }

                var picked = PickAvailable(pool, counts, maxCopies, random);
                if (picked is null)
                {
                    return null;
                }

                deck.Add(picked);
                counts[picked] = counts.GetValueOrDefault(picked) + 1;
            }
        }

        // Fill the remainder from the whole set, uniformly within the copy limit.
        var allIds = cards.All.Select(c => c.Id).ToList();
        while (deck.Count < size)
        {
            var picked = PickAvailable(allIds, counts, maxCopies, random);
            if (picked is null)
            {
                return null;
            }

            deck.Add(picked);
            counts[picked] = counts.GetValueOrDefault(picked) + 1;
        }

        return deck;
    }

    // Uniformly picks an id from `pool` that is not already at the copy limit, or null when every
    // candidate is exhausted. Filters first and picks once, rather than picking-and-retrying,
    // so the draw is uniform over the AVAILABLE ids -- retry-on-collision would consume a variable
    // number of random values per pick and make the sequence depend on how full the deck already
    // is, which is reproducible but needlessly fragile to reason about.
    private static string? PickAvailable(
        IReadOnlyList<string> pool, Dictionary<string, int> counts, int maxCopies, IRandomSource random)
    {
        var available = new List<string>(pool.Count);
        foreach (var id in pool)
        {
            if (counts.GetValueOrDefault(id) < maxCopies)
            {
                available.Add(id);
            }
        }

        return available.Count == 0 ? null : available[random.Next(available.Count)];
    }

    // Whether a card's PLAY COST demands `type` -- i.e. whether playing it draws that pool down.
    //
    // THE single definition of "this card needs spike", used by both the seeding pool and the
    // counting below so the generator cannot satisfy a minimum it then measures differently.
    // Kind-agnostic on purpose: a 3-spike spell and a 3-spike creature are the same demand on the
    // same pool, which is what MinPerType is about.
    //
    // A free card (zero cost) demands nothing and counts toward no type. That is correct rather
    // than an oversight -- it can be cast whatever the pools look like, so it does not help a deck
    // that is starved of a resource.
    public static bool DemandsType(CardDefinition card, ResourceType type)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Cost[type] > 0;
    }

    // Cards in `deck` whose play cost demands `type`. A card with a mixed cost would count toward
    // each type it demands; no shipping card has one (CardValidator forbids it).
    private static int CountOfType(IReadOnlyList<string> deck, CardDatabase cards, ResourceType type)
    {
        var count = 0;
        foreach (var cardId in deck)
        {
            if (DemandsType(cards[cardId], type))
            {
                count++;
            }
        }

        return count;
    }

    // Mean total pip cost across a decklist, counting duplicates -- the quantity the cost
    // tolerance is measured against on both sides.
    public static double MeanCost(IReadOnlyList<string> deck, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(cards);

        if (deck.Count == 0)
        {
            return 0.0;
        }

        var total = 0;
        foreach (var cardId in deck)
        {
            total += cards[cardId].Cost.Total;
        }

        return (double)total / deck.Count;
    }

    // CARDS DEMANDING each type, by play cost -- creatures and spells together. This is what
    // RandomDeckConstraints.MinPerType is enforced against, so the report and the constraint are
    // reading the same number; a report that counted something else would make a deck look to
    // violate a minimum it actually satisfies.
    public static IReadOnlyDictionary<ResourceType, int> TypeCounts(
        IReadOnlyList<string> deck, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(cards);

        return new Dictionary<ResourceType, int>
        {
            [ResourceType.Spike] = CountOfType(deck, cards, ResourceType.Spike),
            [ResourceType.Anvil] = CountOfType(deck, cards, ResourceType.Anvil),
            [ResourceType.Wheel] = CountOfType(deck, cards, ResourceType.Wheel),
        };
    }

    // CREATURES carrying each type on the BOARD, which is a different question from the above and
    // worth reporting alongside it: cost type is what a card DEMANDS to be played, board type is
    // what it IS once in play (what it scores as and what the type chart hits it for). Spells have
    // no board type at all -- they resolve and are gone -- so they are absent here by definition,
    // and a multi-type creature counts once per type it carries.
    public static IReadOnlyDictionary<ResourceType, int> CreatureTypeCounts(
        IReadOnlyList<string> deck, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(cards);

        var counts = new Dictionary<ResourceType, int>
        {
            [ResourceType.Spike] = 0,
            [ResourceType.Anvil] = 0,
            [ResourceType.Wheel] = 0,
        };

        foreach (var cardId in deck)
        {
            var card = cards[cardId];
            if (card.Kind != CardKind.Creature)
            {
                continue;
            }

            foreach (var type in new[] { ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel })
            {
                if (card.Types.Has(type))
                {
                    counts[type]++;
                }
            }
        }

        return counts;
    }

    // Total pips of each resource across a decklist's play costs, counting duplicates.
    //
    // Distinct from TypeCounts, and the difference matters for reading the report: a card's TYPE
    // is what it is on the board (what it scores and defends as), while its COST is what you pay
    // to get it there. A deck can be heavily spike-typed while paying mostly in anvil, and those
    // are different balance stories -- a type shortage loses the type-chart matchup, a cost
    // concentration means the deck stalls whenever that one resource runs dry.
    public static ResourcePool CostByType(IReadOnlyList<string> deck, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(cards);

        var total = ResourcePool.Empty;
        foreach (var cardId in deck)
        {
            total = total.Add(cards[cardId].Cost);
        }

        return total;
    }
}
