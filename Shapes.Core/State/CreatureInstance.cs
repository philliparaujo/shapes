using Shapes.Core.Primitives;

namespace Shapes.Core.State;

// A creature in play.
//
// A mutable class for now, per "build the naive version first". The plan's target layout is a
// struct in a flat array for the MCTS hot path; that conversion belongs with the apply/undo
// work in Phase 2, once the tests here pin the behaviour. Doing it now would mean a mutable
// struct holding a reference field (MergedFrom), which is exactly the shape that silently
// copies when you least expect it.
public sealed class CreatureInstance
{
    // Which card this was played from. Cards themselves arrive in step 1.7; the state model
    // only needs the identity.
    public string CardId { get; }

    public int Health { get; private set; }

    public int MaxHealth { get; private set; }

    // One bit per type. Single creatures have one; merging unions the operands'.
    public TypeMask Types { get; private set; }

    // Card ids folded into this creature, including its own, in merge order. Length is the
    // merge depth, which RuleSet.MaxMergeDepth caps.
    //
    // This IS the creature's move list, indirectly: moves are static card data, so they are
    // looked up rather than stored per-instance. Storing them here would duplicate an
    // identical list across every copy of a card and every MCTS clone. A merged creature's
    // moves are the concatenation, in this order:
    //
    //     MergedFrom.SelectMany(id => cards[id].Moves)
    //
    // Order matters and is why this is a list rather than a set -- see MoveIndexOffset.
    public IReadOnlyList<string> MergedFrom => _mergedFrom;
    private readonly List<string> _mergedFrom;

    // Moves used this turn, as a bitmask over the concatenated move list described above.
    // A move may be used once per turn; different moves are independent. Cleared at end of
    // turn.
    private uint _movesUsedThisTurn;

    public CreatureInstance(string cardId, int maxHealth, TypeMask types, int? health = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHealth, 1);

        if (types.IsEmpty)
        {
            throw new ArgumentException("A creature must have at least one type.", nameof(types));
        }

        CardId = cardId;
        MaxHealth = maxHealth;
        Health = health ?? maxHealth;
        Types = types;
        _mergedFrom = [cardId];

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Health, MaxHealth);
        ArgumentOutOfRangeException.ThrowIfLessThan(Health, 1);
    }

    private CreatureInstance(
        string cardId, int health, int maxHealth, TypeMask types,
        List<string> mergedFrom, uint movesUsedThisTurn)
    {
        CardId = cardId;
        Health = health;
        MaxHealth = maxHealth;
        Types = types;
        _mergedFrom = mergedFrom;
        _movesUsedThisTurn = movesUsedThisTurn;
    }

    // True once this creature is the product of a merge, which locks it out of merging again
    // (subject to RuleSet.MaxMergeDepth). Note this is independent of typing: merging two
    // creatures of the same type produces a merged but still single-type creature.
    public bool IsMerged => _mergedFrom.Count > 1;

    public int MergeDepth => _mergedFrom.Count;

    public bool IsDead => Health <= 0;

    public bool IsDamaged => Health < MaxHealth;

    // Returns the damage actually dealt, which is less than `amount` when the blow is lethal.
    // Callers that report damage (draw-on-damage effects, for instance) need the real figure.
    public int TakeDamage(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var dealt = Math.Min(amount, Health);
        Health -= dealt;
        return dealt;
    }

    // Returns the healing actually applied, capped at the missing health.
    public int Heal(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var healed = Math.Min(amount, MaxHealth - Health);
        Health += healed;
        return healed;
    }

    public void HealToFull() => Health = MaxHealth;

    // Raises the ceiling and the current value together, so a buff never leaves the creature
    // instantly damaged.
    public void BuffMaxHealth(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        MaxHealth += amount;
        Health += amount;
    }

    // Where a source card's moves start in the concatenated move list.
    //
    // For an unmerged creature this is always 0. For a Cadet(2 moves)+Medic(2 moves) merge,
    // Cadet's moves are indices 0-1 and Medic's are 2-3, so the offset for Medic is 2. The
    // caller supplies the per-card move counts because card data lives outside the state
    // model -- but the ORDER is fixed here, so every caller agrees on what index 3 means.
    //
    // Without this the once-per-turn rule would break on merged creatures in a way that is
    // very hard to notice: two different moves could share a bit.
    public int MoveIndexOffset(int mergedFromIndex, Func<string, int> moveCountOf)
    {
        ArgumentNullException.ThrowIfNull(moveCountOf);
        ArgumentOutOfRangeException.ThrowIfNegative(mergedFromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mergedFromIndex, _mergedFrom.Count);

        var offset = 0;
        for (var i = 0; i < mergedFromIndex; i++)
        {
            offset += moveCountOf(_mergedFrom[i]);
        }

        return offset;
    }

    public bool HasUsedMove(int moveIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(moveIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(moveIndex, 32);

        return (_movesUsedThisTurn & (1u << moveIndex)) != 0;
    }

    public void MarkMoveUsed(int moveIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(moveIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(moveIndex, 32);

        _movesUsedThisTurn |= 1u << moveIndex;
    }

    public void ResetMovesForNewTurn() => _movesUsedThisTurn = 0;

    // Folds `other` into this creature: health and max health sum, typings union, and the
    // merged-from lists concatenate. The caller is responsible for checking legality
    // (adjacency, merge depth) and for removing `other` from the board.
    public void AbsorbMerge(CreatureInstance other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Health += other.Health;
        MaxHealth += other.MaxHealth;
        Types = Types.Union(other.Types);
        _mergedFrom.AddRange(other._mergedFrom);
    }

    public CreatureInstance Clone() =>
        new(CardId, Health, MaxHealth, Types, [.. _mergedFrom], _movesUsedThisTurn);

    public override string ToString() =>
        $"{CardId} [{Types}] {Health}/{MaxHealth}{(IsMerged ? $" (merged x{MergeDepth})" : string.Empty)}";
}
