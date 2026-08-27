using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// The two player portraits (DESIGN.md 5.C-UI). Tested here rather than by eye in the editor
// because the properties that matter are invisible on screen: that a resumed match re-derives
// the SAME two faces, that the two seats never share a resource type, and that the picks never
// come from the game's own random stream. All plain data-in/data-out, the same reasoning
// MatchConfigTests applies to AgentFactory.
public class AvatarPickerTests
{
    private static AvatarPicker.Candidate Candidate(string id, ResourceType type) => new(id, type);

    // Three of each type, so a same-type pair is available for the picker to (correctly) avoid.
    private static readonly AvatarPicker.Candidate[] Pool =
    [
        Candidate("anchor", ResourceType.Anvil),
        Candidate("bastion", ResourceType.Anvil),
        Candidate("columns", ResourceType.Anvil),
        Candidate("guardian", ResourceType.Wheel),
        Candidate("monk", ResourceType.Wheel),
        Candidate("relic", ResourceType.Wheel),
        Candidate("sentry", ResourceType.Spike),
        Candidate("titan", ResourceType.Spike),
    ];

    private static ResourceType TypeOf(string id) =>
        Pool.Single(candidate => candidate.Id == id).Type;

    [Fact]
    public void Picks_two_different_cards()
    {
        var (one, two) = AvatarPicker.Pick(seed: 12345, Pool);

        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotEqual(one, two);
    }

    // The headline rule: never two wheel creatures facing each other.
    [Fact]
    public void The_two_portraits_never_share_a_resource_type()
    {
        for (ulong seed = 0; seed < 500; seed++)
        {
            var (one, two) = AvatarPicker.Pick(seed, Pool);
            Assert.NotEqual(TypeOf(one!), TypeOf(two!));
        }
    }

    // The resume guarantee: a saved match replays its seed and must come back wearing the same
    // faces, because the avatars are not written to the save file.
    [Fact]
    public void Same_seed_picks_the_same_pair()
    {
        var first = AvatarPicker.Pick(seed: 999, Pool);
        var second = AvatarPicker.Pick(seed: 999, Pool);

        Assert.Equal(first, second);
    }

    // Enumeration order must not leak into the result -- CardArt.AvatarCandidates walks the card
    // database, and a card added to (or renamed in) the set would otherwise silently change every
    // existing seed's portraits.
    [Fact]
    public void Pick_does_not_depend_on_the_order_of_the_candidates()
    {
        var reversed = Pool.Reverse().ToArray();

        Assert.Equal(AvatarPicker.Pick(seed: 77, Pool), AvatarPicker.Pick(seed: 77, reversed));
    }

    [Fact]
    public void Duplicate_candidates_cannot_produce_two_identical_portraits()
    {
        var repeated = new[]
        {
            Candidate("monk", ResourceType.Wheel),
            Candidate("monk", ResourceType.Wheel),
            Candidate("monk", ResourceType.Wheel),
        };

        var (one, two) = AvatarPicker.Pick(seed: 5, repeated);

        Assert.Equal("monk", one);
        Assert.Null(two);
    }

    // Art is filled in one card at a time, so a thin or empty set is a legitimate state that has
    // to degrade to the flat placeholder rather than throw.
    [Fact]
    public void Empty_pool_yields_no_portraits()
    {
        var (one, two) = AvatarPicker.Pick(seed: 5, []);

        Assert.Null(one);
        Assert.Null(two);
    }

    [Fact]
    public void Single_candidate_gives_one_seat_art_and_the_other_the_placeholder()
    {
        var (one, two) = AvatarPicker.Pick(seed: 5, [Candidate("relic", ResourceType.Wheel)]);

        Assert.Equal("relic", one);
        Assert.Null(two);
    }

    // A pool with only one type present cannot honour the different-types rule, so the second
    // seat takes the placeholder rather than the rule being quietly relaxed.
    [Fact]
    public void Single_type_pool_gives_the_second_seat_the_placeholder()
    {
        var oneType = new[]
        {
            Candidate("guardian", ResourceType.Wheel),
            Candidate("monk", ResourceType.Wheel),
            Candidate("relic", ResourceType.Wheel),
        };

        for (ulong seed = 0; seed < 50; seed++)
        {
            var (one, two) = AvatarPicker.Pick(seed, oneType);
            Assert.NotNull(one);
            Assert.Null(two);
        }
    }

    // Both seats must be reachable for every card -- an off-by-one in the second draw's indexing
    // would quietly pin a seat to a subset of the pool for every seed.
    [Fact]
    public void Different_seeds_reach_every_card_in_the_pool()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 0; seed < 500; seed++)
        {
            var (one, two) = AvatarPicker.Pick(seed, Pool);
            seen.Add(one!);
            seen.Add(two!);
        }

        Assert.Equal(
            Pool.Select(candidate => candidate.Id).OrderBy(id => id, StringComparer.Ordinal),
            seen.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_pick_comes_from_the_candidate_pool()
    {
        var ids = Pool.Select(candidate => candidate.Id).ToList();
        for (ulong seed = 0; seed < 200; seed++)
        {
            var (one, two) = AvatarPicker.Pick(seed, Pool);
            Assert.Contains(one!, ids);
            Assert.Contains(two!, ids);
        }
    }

    // Guards the real content set, not a synthetic pool: the rules above are only worth anything
    // if the shipped card set can actually satisfy them. Mirrors the filtering
    // CardArt.AvatarCandidates does, minus the res:// art probe that needs a live Godot runtime.
    [Fact]
    public void The_real_card_set_offers_creatures_of_at_least_two_types()
    {
        var cards = CardLoader.FromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

        var types = cards.All
            .Where(card => card.IsCreature)
            .Select(card => CardText.SinglePipType(card.Cost))
            .Where(type => type is not null)
            .Distinct()
            .ToList();

        Assert.True(
            types.Count >= 2,
            $"Avatars need creatures of at least two resource types to give the seats different "
            + $"portraits; the card set offers {types.Count}.");
    }
}
