using Shapes.Core.Primitives;

namespace Shapes.Tests.Mechanics;

// Board geometry. Scoring reads Opposing() and merging reads IsAdjacentTo(), so an error
// here is a silent rules bug rather than a crash -- which is why the pairings are pinned
// exhaustively.
public class SlotIndexTests
{
    [Fact]
    public void Opponent_flips_the_player()
    {
        Assert.Equal(PlayerId.Two, PlayerId.One.Opponent());
        Assert.Equal(PlayerId.One, PlayerId.Two.Opponent());
        Assert.Equal(PlayerId.One, PlayerId.One.Opponent().Opponent());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void Slot_must_be_on_the_board(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotIndex(PlayerId.One, slot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Slot_i_opposes_enemy_slot_i(int slot)
    {
        // Slot i faces enemy slot i -- NOT a mirrored 2-i. Scoring ("+1 per friendly creature
        // whose opposing slot is empty") depends on this exact pairing.
        var mine = new SlotIndex(PlayerId.One, slot);
        var across = mine.Opposing();

        Assert.Equal(PlayerId.Two, across.Owner);
        Assert.Equal(slot, across.Slot);
    }

    [Fact]
    public void Opposition_is_symmetric()
    {
        foreach (var player in PlayerIds.All)
        {
            foreach (var s in SlotIndex.AllFor(player))
            {
                Assert.Equal(s, s.Opposing().Opposing());
            }
        }
    }

    [Fact]
    public void Opposition_is_not_mirrored()
    {
        // Guards specifically against a 2-i mirroring mistake, which would look correct for
        // the middle slot and silently wrong for the outer two.
        var left = new SlotIndex(PlayerId.One, 0);

        Assert.Equal(0, left.Opposing().Slot);
        Assert.NotEqual(2, left.Opposing().Slot);
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, true)]
    [InlineData(0, 2, false)]  // ends of the board are not adjacent
    [InlineData(2, 0, false)]
    [InlineData(1, 1, false)]  // a slot is not adjacent to itself
    public void Adjacency_is_neighbouring_slots_on_the_same_side(int a, int b, bool expected)
    {
        var first = new SlotIndex(PlayerId.One, a);
        var second = new SlotIndex(PlayerId.One, b);

        Assert.Equal(expected, first.IsAdjacentTo(second));
    }

    [Fact]
    public void Slots_on_opposite_sides_are_never_adjacent()
    {
        // Merging requires two adjacent friendly creatures; an enemy slot must never qualify,
        // however close its index.
        foreach (var mine in SlotIndex.AllFor(PlayerId.One))
        {
            foreach (var theirs in SlotIndex.AllFor(PlayerId.Two))
            {
                Assert.False(mine.IsAdjacentTo(theirs));
            }
        }
    }

    [Fact]
    public void Flat_index_round_trips()
    {
        foreach (var player in PlayerIds.All)
        {
            foreach (var slot in SlotIndex.AllFor(player))
            {
                Assert.Equal(slot, SlotIndex.FromFlatIndex(slot.ToFlatIndex()));
            }
        }
    }

    [Fact]
    public void Flat_indices_are_unique_and_contiguous()
    {
        var all = PlayerIds.All.SelectMany(SlotIndex.AllFor).Select(s => s.ToFlatIndex()).ToList();

        Assert.Equal(SlotIndex.SlotsPerPlayer * PlayerIds.Count, all.Count);
        Assert.Equal(all.Distinct().Count(), all.Count);
        Assert.Equal(Enumerable.Range(0, all.Count), all.OrderBy(i => i));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Flat_index_must_be_on_the_board(int flat)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlotIndex.FromFlatIndex(flat));
    }

    [Fact]
    public void AllFor_returns_every_slot_for_one_player()
    {
        var slots = SlotIndex.AllFor(PlayerId.Two);

        Assert.Equal(SlotIndex.SlotsPerPlayer, slots.Length);
        Assert.All(slots, s => Assert.Equal(PlayerId.Two, s.Owner));
        Assert.Equal([0, 1, 2], slots.Select(s => s.Slot));
    }

    [Fact]
    public void Slots_are_value_equal()
    {
        Assert.Equal(new SlotIndex(PlayerId.One, 1), new SlotIndex(PlayerId.One, 1));
        Assert.True(new SlotIndex(PlayerId.One, 1) == new SlotIndex(PlayerId.One, 1));

        // Same slot number, different side: distinct positions.
        Assert.True(new SlotIndex(PlayerId.One, 1) != new SlotIndex(PlayerId.Two, 1));
    }

    [Fact]
    public void ToString_names_player_and_slot()
    {
        Assert.Equal("P1:0", new SlotIndex(PlayerId.One, 0).ToString());
        Assert.Equal("P2:2", new SlotIndex(PlayerId.Two, 2).ToString());
    }
}
