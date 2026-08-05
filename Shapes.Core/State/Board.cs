using Shapes.Core.Primitives;

namespace Shapes.Core.State;

// Both players' slots in one flat array, indexed by SlotIndex.ToFlatIndex().
//
// Slot geometry -- which slot faces which, which are adjacent -- lives on SlotIndex, not
// here. This class only stores occupancy and answers questions in terms of it.
public sealed class Board
{
    // Shared, never mutated: the common-case return of RemoveDead(), where a Fresh `List<T>`
    // would otherwise be allocated on every single Apply call just to say "nothing died".
    private static readonly List<(SlotIndex Slot, CreatureInstance Creature)> NoneRemoved = [];

    private readonly CreatureInstance?[] _slots;

    public Board()
    {
        _slots = new CreatureInstance?[SlotIndex.SlotsPerPlayer * PlayerIds.Count];
    }

    private Board(CreatureInstance?[] slots) => _slots = slots;

    public CreatureInstance? this[SlotIndex slot] => _slots[slot.ToFlatIndex()];

    public bool IsEmpty(SlotIndex slot) => this[slot] is null;

    public bool IsOccupied(SlotIndex slot) => this[slot] is not null;

    public void Place(SlotIndex slot, CreatureInstance creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (IsOccupied(slot))
        {
            throw new InvalidOperationException($"Slot {slot} is already occupied.");
        }

        _slots[slot.ToFlatIndex()] = creature;
    }

    public CreatureInstance? Remove(SlotIndex slot)
    {
        var creature = this[slot];
        _slots[slot.ToFlatIndex()] = null;
        return creature;
    }

    // The creature directly across from this slot, if any. Scoring is defined in terms of
    // this being null.
    public CreatureInstance? Opposing(SlotIndex slot) => this[slot.Opposing()];

    public bool IsUnopposed(SlotIndex slot) => IsOccupied(slot) && Opposing(slot) is null;

    public IEnumerable<(SlotIndex Slot, CreatureInstance Creature)> CreaturesOf(PlayerId player)
    {
        foreach (var slot in SlotIndex.AllFor(player))
        {
            var creature = this[slot];
            if (creature is not null)
            {
                yield return (slot, creature);
            }
        }
    }

    public IEnumerable<(SlotIndex Slot, CreatureInstance Creature)> AllCreatures()
    {
        foreach (var player in PlayerIds.All)
        {
            foreach (var entry in CreaturesOf(player))
            {
                yield return entry;
            }
        }
    }

    public IEnumerable<SlotIndex> EmptySlotsOf(PlayerId player) =>
        SlotIndex.AllFor(player).Where(IsEmpty);

    public int CountCreatures(PlayerId player) => CreaturesOf(player).Count();

    public bool HasRoom(PlayerId player) => SlotIndex.AllFor(player).Any(IsEmpty);

    // Clears dead creatures and reports where they were (and which creature it was), so callers
    // can trigger death-dependent effects (draw-on-destroy, the turn's destroyed-creature log,
    // and the like).
    //
    // Two allocation cuts, both aimed at the same fact: this runs on EVERY Apply call (via
    // ActionExecutor.ResolveEffects), whether or not anything actually died -- a kill is rare,
    // so the common case is "nothing to report" and should not allocate to say so.
    //
    //   1. Iterates AllCreatures() directly rather than a defensive .ToList() copy of it: the
    //      enumerator reads _slots[slot.ToFlatIndex()] fresh at each step from a fixed,
    //      precomputed slot-index sequence (SlotIndex.AllFor), so nulling an EARLIER slot
    //      mid-loop cannot shift or invalidate a LATER one -- there is no live view of a mutable
    //      collection to invalidate. The copy was pure allocation for a hazard that doesn't
    //      exist here.
    //   2. The result list itself is allocated lazily, only once a first dead creature is found,
    //      and the truly-empty case returns a single shared empty list rather than a fresh one --
    //      staying non-null and List-typed (not Nullable) so every existing caller's
    //      foreach/.Count/Assert.Empty usage keeps working unchanged.
    public List<(SlotIndex Slot, CreatureInstance Creature)> RemoveDead()
    {
        List<(SlotIndex, CreatureInstance)>? removed = null;

        foreach (var (slot, creature) in AllCreatures())
        {
            if (creature.IsDead)
            {
                _slots[slot.ToFlatIndex()] = null;
                (removed ??= []).Add((slot, creature));
            }
        }

        return removed ?? NoneRemoved;
    }

    public Board Clone()
    {
        var copy = new CreatureInstance?[_slots.Length];
        for (var i = 0; i < _slots.Length; i++)
        {
            copy[i] = _slots[i]?.Clone();
        }

        return new Board(copy);
    }
}
