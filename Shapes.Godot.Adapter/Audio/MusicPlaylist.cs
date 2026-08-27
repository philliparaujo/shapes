namespace Shapes.Godot.Adapter;

// The order the music tracks play in (DESIGN.md D4).
//
// "SHUFFLING BETWEEN .OGG FILES IN A PREDICTABLE ORDER (1,2,3,4,1,2,3,4,...)" -- the request's own
// words, and the two halves of that phrase pull in opposite directions, so this class is where the
// tension is resolved rather than left to a caller to guess at. It is a fixed ROTATION, not a
// random shuffle: the same cycle every launch, each track once before any repeats. That is what
// makes it predictable enough to reason about (and to test) while still moving through the whole
// set rather than looping one track forever.
//
// Pure index arithmetic with no Godot type in it, so the ordering rule is testable outside the
// editor -- the same reason AnimationScript lives here rather than in Shapes.Godot.
public sealed class MusicPlaylist
{
    private readonly int _count;
    private int _index;

    // Starts BEFORE the first track rather than on it, so the first Next() returns index 0. The
    // alternative (starting at 0 and advancing after) would either skip track 1 or need the
    // caller to special-case the first call, and a playlist that quietly never plays its first
    // track is exactly the kind of off-by-one nobody notices for months.
    public MusicPlaylist(int trackCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trackCount);
        _count = trackCount;
        _index = -1;
    }

    public int TrackCount => _count;

    // The index of the next track to play, wrapping back to 0 after the last one.
    //
    // Returns null for an empty playlist rather than throwing: a build with no music files in
    // Audio/Music is a content-authoring state, not a programming error, and the audio layer
    // should fall silent rather than take the game down with it.
    public int? Next()
    {
        if (_count == 0)
        {
            return null;
        }

        _index = (_index + 1) % _count;
        return _index;
    }

    // The track currently playing, or null if Next() has not been called yet (or there are no
    // tracks). Exposed so a caller can tell "nothing has started" from "track 0 is playing",
    // which _index alone conflates once it wraps.
    public int? Current => _count == 0 || _index < 0 ? null : _index;
}
