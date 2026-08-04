using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Tests.Mechanics;

// Board occupancy. Geometry itself is SlotIndex's job and tested there.
public class BoardTests
{
    private static CreatureInstance Creature(string id = "c", TypeMask? types = null) =>
        new(id, 3, types ?? TypeMask.Wheel);

    private static SlotIndex P1(int slot) => new(PlayerId.One, slot);

    private static SlotIndex P2(int slot) => new(PlayerId.Two, slot);

    [Fact]
    public void A_new_board_is_empty()
    {
        var board = new Board();

        foreach (var player in PlayerIds.All)
        {
            Assert.Equal(0, board.CountCreatures(player));
            Assert.True(board.HasRoom(player));
        }
    }

    [Fact]
    public void Placing_and_removing_a_creature()
    {
        var board = new Board();
        var creature = Creature();

        board.Place(P1(1), creature);
        Assert.True(board.IsOccupied(P1(1)));
        Assert.Same(creature, board[P1(1)]);

        Assert.Same(creature, board.Remove(P1(1)));
        Assert.True(board.IsEmpty(P1(1)));
    }

    [Fact]
    public void Placing_into_an_occupied_slot_throws()
    {
        var board = new Board();
        board.Place(P1(0), Creature());

        Assert.Throws<InvalidOperationException>(() => board.Place(P1(0), Creature()));
    }

    [Fact]
    public void Removing_from_an_empty_slot_returns_null()
    {
        Assert.Null(new Board().Remove(P1(0)));
    }

    [Fact]
    public void Board_caps_at_three_creatures_per_player()
    {
        var board = new Board();

        for (var i = 0; i < SlotIndex.SlotsPerPlayer; i++)
        {
            board.Place(P1(i), Creature());
        }

        Assert.Equal(3, board.CountCreatures(PlayerId.One));
        Assert.False(board.HasRoom(PlayerId.One));
        Assert.Empty(board.EmptySlotsOf(PlayerId.One));

        // The opponent's side is unaffected.
        Assert.True(board.HasRoom(PlayerId.Two));
    }

    [Fact]
    public void Opposing_reads_the_facing_slot()
    {
        var board = new Board();
        var enemy = Creature("enemy");
        board.Place(P2(1), enemy);

        Assert.Same(enemy, board.Opposing(P1(1)));
        Assert.Null(board.Opposing(P1(0)));
    }

    [Fact]
    public void A_creature_is_unopposed_only_when_the_facing_slot_is_empty()
    {
        var board = new Board();
        board.Place(P1(0), Creature());
        board.Place(P1(1), Creature());
        board.Place(P2(1), Creature());

        Assert.True(board.IsUnopposed(P1(0)));
        Assert.False(board.IsUnopposed(P1(1)));

        // An empty slot is not "unopposed" -- there is nothing there to score.
        Assert.False(board.IsUnopposed(P1(2)));
    }

    [Fact]
    public void CreaturesOf_returns_only_that_players_creatures()
    {
        var board = new Board();
        board.Place(P1(0), Creature("mine"));
        board.Place(P2(0), Creature("theirs"));

        var mine = board.CreaturesOf(PlayerId.One).ToList();

        Assert.Single(mine);
        Assert.Equal("mine", mine[0].Creature.CardId);
        Assert.Equal(P1(0), mine[0].Slot);
    }

    [Fact]
    public void RemoveDead_clears_dead_creatures_and_reports_their_slots()
    {
        var board = new Board();
        var dying = Creature("dying");
        board.Place(P1(0), dying);
        board.Place(P1(1), Creature("alive"));
        dying.TakeDamage(99);

        var removed = board.RemoveDead();

        Assert.Equal([P1(0)], removed);
        Assert.True(board.IsEmpty(P1(0)));
        Assert.True(board.IsOccupied(P1(1)));
    }

    [Fact]
    public void RemoveDead_removes_across_both_sides()
    {
        var board = new Board();
        var a = Creature("a");
        var b = Creature("b");
        board.Place(P1(0), a);
        board.Place(P2(2), b);
        a.TakeDamage(99);
        b.TakeDamage(99);

        Assert.Equal(2, board.RemoveDead().Count);
        Assert.Empty(board.AllCreatures());
    }

    [Fact]
    public void RemoveDead_on_a_healthy_board_does_nothing()
    {
        var board = new Board();
        board.Place(P1(0), Creature());

        Assert.Empty(board.RemoveDead());
        Assert.Equal(1, board.CountCreatures(PlayerId.One));
    }

    [Fact]
    public void Clearing_a_slot_changes_opposition_for_scoring()
    {
        var board = new Board();
        board.Place(P1(0), Creature("mine"));
        var blocker = Creature("blocker");
        board.Place(P2(0), blocker);

        Assert.False(board.IsUnopposed(P1(0)));

        blocker.TakeDamage(99);
        board.RemoveDead();

        Assert.True(board.IsUnopposed(P1(0)));
    }

    [Fact]
    public void Clone_is_independent_in_both_occupancy_and_creature_state()
    {
        var board = new Board();
        board.Place(P1(0), Creature("a"));

        var copy = board.Clone();
        copy[P1(0)]!.TakeDamage(1);
        copy.Place(P1(1), Creature("b"));

        Assert.Equal(3, board[P1(0)]!.Health);
        Assert.True(board.IsEmpty(P1(1)));
    }
}
