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

    // Status keywords granted by `grant_keyword` (taunt/reflect/ricochet). Persistent
    // until something removes them -- nothing does yet, since no card needs it. Reflect is
    // the exception: it is consumed the first time it triggers (see ConsumeReflect).
    public KeywordFlags Keywords { get; private set; }

    // Which side ricochet redirects to. Only meaningful while Keywords has Ricochet.
    public RicochetDirection RicochetDirection { get; private set; }

    // True while `stun` prevents this creature from using moves. Cleared at the start of its
    // controller's next turn, alongside the per-turn move-usage reset.
    public bool IsStunned { get; private set; }

    // One-shot flat damage modifiers set by `next_attack_bonus` / `next_damage_taken_bonus`.
    // Each applies once then clears itself -- see EffectContext for the consume call sites.
    public int NextAttackBonus { get; private set; }

    public int NextDamageTakenBonus { get; private set; }

    // Persistent, cumulative flat bonus to every future hit this creature deals, set by
    // `attack_buff`. Unlike NextAttackBonus this never clears itself -- "increase all damage
    // this does" is a standing buff, not a one-shot. Stacks if granted more than once.
    public int AttackBuff { get; private set; }

    // The resources paid to play this card, captured when it enters the board (see
    // ActionExecutor.ApplyPlayCard). Lets an effect (destroy_refund_cost) refund a destroyed
    // creature's cost without the State/Effects layers needing a CardDatabase lookup -- the
    // one piece of card data a creature carries about itself, set once at play time and never
    // mutated afterward. Empty for creatures constructed directly (tests, summon) that were
    // never "played" from a hand.
    public ResourcePool PlayCost { get; private set; }

    // True when this creature's Taunt keyword was granted with an expiry ("taunt until next
    // turn," e.g. Columns) rather than permanently. Cleared, along with the Taunt bit itself,
    // by ResetMovesForNewTurn -- the same turn-boundary reset stun already uses. A future card
    // granting permanent taunt would simply not set this flag.
    private bool _tauntExpiresNextTurn;

    // A pending effect to run the next time this creature takes damage from a creature-sourced
    // attack, or the next time an attack against it ricochets, respectively. Each fires once
    // then clears -- the same one-shot shape as NextAttackBonus, but carrying an arbitrary
    // effect (typically a resource gain or draw) rather than a flat number. See
    // CombatResolver for the fire points.
    //
    // Typed as `object?` rather than EffectNode: State must not reference Effects, which
    // already depends on State to mutate creatures and the board. Effects.Ops casts this back
    // to EffectNode when it sets or fires the trigger; State itself never inspects the value.
    public object? PendingOnNextDamageTaken { get; private set; }

    public object? PendingOnNextRicochet { get; private set; }

    // True for a creature conjured onto the board by `summon` rather than played from a hand.
    //
    // The distinction exists because a token is not a CARD: its id ("token_spike") need not name
    // anything in the CardDatabase, it was never in anyone's deck, and it must not enter a
    // discard pile when it dies. Every other creature came from a physical card that has to go
    // somewhere -- see PlayerState.SendToDiscard's call sites and the card-conservation invariant
    // in Shapes.Tests/Mechanics/CardConservationTests.cs, which the determinizer's accounting
    // depends on: a token in a discard pile would inflate a player's apparent card pool and let
    // the determinizer sample cards that never existed.
    //
    // A merge involving a token taints the whole result (see AbsorbMerge): the merged creature's
    // MergedFrom then contains an id that is not a card, so the safe reading is "this stack
    // cannot be fully accounted for as cards," and none of it is discarded.
    public bool IsToken { get; private set; }

    public CreatureInstance(
        string cardId, int maxHealth, TypeMask types, int? health = null,
        ResourcePool? playCost = null, bool isToken = false)
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
        PlayCost = playCost ?? ResourcePool.Empty;
        IsToken = isToken;
        _mergedFrom = [cardId];

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Health, MaxHealth);
        ArgumentOutOfRangeException.ThrowIfLessThan(Health, 1);
    }

    private CreatureInstance(
        string cardId, int health, int maxHealth, TypeMask types,
        List<string> mergedFrom, uint movesUsedThisTurn, KeywordFlags keywords,
        RicochetDirection ricochetDirection, bool isStunned, int nextAttackBonus,
        int nextDamageTakenBonus, int attackBuff, ResourcePool playCost,
        bool tauntExpiresNextTurn, object? pendingOnNextDamageTaken,
        object? pendingOnNextRicochet, bool isToken)
    {
        IsToken = isToken;
        CardId = cardId;
        Health = health;
        MaxHealth = maxHealth;
        Types = types;
        _mergedFrom = mergedFrom;
        _movesUsedThisTurn = movesUsedThisTurn;
        Keywords = keywords;
        RicochetDirection = ricochetDirection;
        IsStunned = isStunned;
        NextAttackBonus = nextAttackBonus;
        NextDamageTakenBonus = nextDamageTakenBonus;
        AttackBuff = attackBuff;
        PlayCost = playCost;
        _tauntExpiresNextTurn = tauntExpiresNextTurn;
        PendingOnNextDamageTaken = pendingOnNextDamageTaken;
        PendingOnNextRicochet = pendingOnNextRicochet;
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

    public void ResetMovesForNewTurn()
    {
        _movesUsedThisTurn = 0;
        IsStunned = false;

        if (_tauntExpiresNextTurn)
        {
            Keywords &= ~KeywordFlags.Taunt;
            _tauntExpiresNextTurn = false;
        }
    }

    // `untilNextTurn: true` clears the keyword again at this creature's controller's next turn
    // (see ResetMovesForNewTurn) -- Columns' "taunt until next turn" needs an expiry that
    // permanent taunt grants do not. Only meaningful for Taunt; a timed reflect or ricochet
    // would need its own flag, but nothing needs that yet.
    public void GrantKeyword(KeywordFlags keyword, bool untilNextTurn = false)
    {
        Keywords |= keyword;

        if (untilNextTurn && keyword == KeywordFlags.Taunt)
        {
            _tauntExpiresNextTurn = true;
        }
    }

    // Ricochet needs a side; the other keywords ignore the argument entirely.
    public void GrantRicochet(RicochetDirection direction)
    {
        Keywords |= KeywordFlags.Ricochet;
        RicochetDirection = direction;
    }

    public bool HasKeyword(KeywordFlags keyword) => (Keywords & keyword) == keyword;

    // Reflect fires once then clears -- a second attack this turn (or any later turn) is not
    // reflected until the keyword is granted again.
    public bool ConsumeReflect()
    {
        if (!HasKeyword(KeywordFlags.Reflect))
        {
            return false;
        }

        Keywords &= ~KeywordFlags.Reflect;
        return true;
    }

    public void Stun() => IsStunned = true;

    public void SetNextAttackBonus(int amount) => NextAttackBonus = amount;

    // Consumes the bonus: returns its value and resets it, so a second attack this turn does
    // not silently reapply it. The bonus is folded into the base damage before the
    // type-effectiveness multiplier is applied -- see the damage op.
    public int ConsumeNextAttackBonus()
    {
        var bonus = NextAttackBonus;
        NextAttackBonus = 0;
        return bonus;
    }

    public void SetNextDamageTakenBonus(int amount) => NextDamageTakenBonus = amount;

    public int ConsumeNextDamageTakenBonus()
    {
        var bonus = NextDamageTakenBonus;
        NextDamageTakenBonus = 0;
        return bonus;
    }

    // Persistent and additive: repeated grants stack, and nothing ever clears this on its own.
    public void AddAttackBuff(int amount) => AttackBuff += amount;

    public void SetPendingOnNextDamageTaken(object? trigger) => PendingOnNextDamageTaken = trigger;

    // Returns the pending trigger and clears it, so a second hit this turn (or any later turn)
    // does not refire it until granted again.
    public object? ConsumePendingOnNextDamageTaken()
    {
        var trigger = PendingOnNextDamageTaken;
        PendingOnNextDamageTaken = null;
        return trigger;
    }

    public void SetPendingOnNextRicochet(object? trigger) => PendingOnNextRicochet = trigger;

    public object? ConsumePendingOnNextRicochet()
    {
        var trigger = PendingOnNextRicochet;
        PendingOnNextRicochet = null;
        return trigger;
    }

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

        // Token-ness is contagious: MergedFrom now holds at least one id that names no card, so
        // the stack can no longer be discarded as a set of cards on death. Tainting the whole
        // creature is the conservative reading -- discarding only the non-token half would need
        // per-id provenance that nothing else wants, and letting the token half through would put
        // a non-card id in a discard pile. See IsToken.
        IsToken |= other.IsToken;
    }

    public CreatureInstance Clone() =>
        new(CardId, Health, MaxHealth, Types, [.. _mergedFrom], _movesUsedThisTurn, Keywords,
            RicochetDirection, IsStunned, NextAttackBonus, NextDamageTakenBonus, AttackBuff,
            PlayCost, _tauntExpiresNextTurn, PendingOnNextDamageTaken, PendingOnNextRicochet,
            IsToken);

    public override string ToString() =>
        $"{CardId} [{Types}] {Health}/{MaxHealth}{(IsMerged ? $" (merged x{MergeDepth})" : string.Empty)}";
}
