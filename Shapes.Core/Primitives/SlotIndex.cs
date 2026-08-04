namespace Shapes.Core.Primitives;

// A board position: which player's side, and which of their slots.
//
// Slot i faces enemy slot i. Scoring depends on that opposition ("+1 per friendly creature
// whose opposing slot is empty"), and getting the pairing wrong is a silent scoring bug --
// so the rule lives here as Opposing() rather than as index arithmetic scattered across the
// engine. Adjacency for merges lives here for the same reason.
public readonly struct SlotIndex : IEquatable<SlotIndex>
{
    // Board size is structural, not a balance knob, so it is defined here and nowhere else --
    // deliberately not a RuleSet field. Scoring is expressed in terms of facing slots and the
    // flat board array is sized from this at compile time, so a runtime-configurable copy
    // could only drift out of agreement with it. Change the board size here.
    public const int SlotsPerPlayer = 3;

    public PlayerId Owner { get; }

    public int Slot { get; }

    public SlotIndex(PlayerId owner, int slot)
    {
        if (slot < 0 || slot >= SlotsPerPlayer)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot), slot, $"Slot must be in [0, {SlotsPerPlayer - 1}].");
        }

        Owner = owner;
        Slot = slot;
    }

    // The enemy slot directly across from this one.
    public SlotIndex Opposing() => new(Owner.Opponent(), Slot);

    public bool IsAdjacentTo(SlotIndex other) =>
        Owner == other.Owner && Math.Abs(Slot - other.Slot) == 1;

    // Flat index into a single board array laid out as [player-one slots..., player-two
    // slots...]. Keeps the board one contiguous allocation for the search.
    public int ToFlatIndex() => (Owner.ToIndex() * SlotsPerPlayer) + Slot;

    public static SlotIndex FromFlatIndex(int flat)
    {
        if (flat < 0 || flat >= SlotsPerPlayer * PlayerIds.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flat), flat, $"Flat index must be in [0, {(SlotsPerPlayer * PlayerIds.Count) - 1}].");
        }

        return new SlotIndex((PlayerId)(flat / SlotsPerPlayer), flat % SlotsPerPlayer);
    }

    public static SlotIndex[] AllFor(PlayerId player)
    {
        var result = new SlotIndex[SlotsPerPlayer];
        for (var i = 0; i < SlotsPerPlayer; i++)
        {
            result[i] = new SlotIndex(player, i);
        }

        return result;
    }

    public bool Equals(SlotIndex other) => Owner == other.Owner && Slot == other.Slot;

    public override bool Equals(object? obj) => obj is SlotIndex other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Owner, Slot);

    public static bool operator ==(SlotIndex a, SlotIndex b) => a.Equals(b);

    public static bool operator !=(SlotIndex a, SlotIndex b) => !a.Equals(b);

    // e.g. "P1:0"
    public override string ToString() => $"P{Owner.ToIndex() + 1}:{Slot}";
}
