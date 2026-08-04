namespace Shapes.Core.Primitives;

// Which of the two players. Values pinned to 0/1 so they can index two-element arrays
// directly in the search hot path.
public enum PlayerId
{
    One = 0,
    Two = 1,
}

public static class PlayerIds
{
    public const int Count = 2;

    public static readonly PlayerId[] All = [PlayerId.One, PlayerId.Two];

    public static PlayerId Opponent(this PlayerId player) =>
        player == PlayerId.One ? PlayerId.Two : PlayerId.One;

    public static int ToIndex(this PlayerId player) => (int)player;
}
