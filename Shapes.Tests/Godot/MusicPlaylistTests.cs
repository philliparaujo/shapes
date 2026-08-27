using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// DESIGN.md D4: the "1,2,3,4,1,2,3,4,..." rotation the request asked for, pinned as an actual
// sequence rather than trusted to modular arithmetic being obviously right.
public class MusicPlaylistTests
{
    // The headline property, and the exact sequence from the request. Two full cycles, because one
    // cycle would pass even if the wrap were broken.
    [Fact]
    public void Cycles_every_track_in_order_and_repeats()
    {
        var playlist = new MusicPlaylist(4);

        var played = new List<int?>();
        for (var i = 0; i < 9; i++)
        {
            played.Add(playlist.Next());
        }

        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3, 0], played);
    }

    // The off-by-one MusicPlaylist's constructor comment calls out: starting at index 0 and
    // advancing after would skip track 1 on the very first play, which is exactly the kind of bug
    // nobody notices because the music still works.
    [Fact]
    public void First_call_plays_the_first_track()
    {
        Assert.Equal(0, new MusicPlaylist(3).Next());
    }

    // An empty Audio/Music folder is a content state, not a crash -- see Next's own note.
    [Fact]
    public void Empty_playlist_yields_nothing()
    {
        var playlist = new MusicPlaylist(0);

        Assert.Null(playlist.Next());
        Assert.Null(playlist.Current);
    }

    // Current distinguishes "nothing has started" from "track 0 is playing", which a bare index
    // field conflates the moment it wraps back to zero.
    [Fact]
    public void Current_is_null_until_the_first_track_starts()
    {
        var playlist = new MusicPlaylist(2);
        Assert.Null(playlist.Current);

        playlist.Next();
        Assert.Equal(0, playlist.Current);

        playlist.Next();
        Assert.Equal(1, playlist.Current);
    }

    [Fact]
    public void Single_track_repeats_itself()
    {
        var playlist = new MusicPlaylist(1);

        Assert.Equal(0, playlist.Next());
        Assert.Equal(0, playlist.Next());
    }
}
