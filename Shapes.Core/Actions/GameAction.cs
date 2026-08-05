using Shapes.Core.Primitives;

namespace Shapes.Core.Actions;

// What kind of choice an action represents. Lets consumers switch without type-testing, which
// matters for the search loop and for the console's action rendering.
public enum ActionKind
{
    PlayCard = 0,
    UseMove = 1,
    Merge = 2,
    EndTurn = 3,
    Discard = 4,
}

// One atomic choice a player can make.
//
// "Atomic" is load-bearing for Phase 2: an MCTS node is one action, never a whole turn. A turn
// is a sequence of these ending in EndTurn, which keeps the per-node branching factor in the
// 10-40 range the plan budgets for rather than the combinatorial product of every action
// ordering.
//
// Immutable, and complete: an action carries everything needed to apply it, including a
// resolved chosen target. Nothing asks the player a question at apply time -- the choice was
// already made by picking THIS action out of the legal list. That is what lets the search
// treat an action as an opaque edge, and what lets the Phase 5 UI be a single selection state.
//
// Value equality is deliberate. The search compares and dedupes actions, and two actions
// describing the same choice must be the same edge; reference equality would silently create
// duplicate children for identical moves.
public abstract class GameAction : IEquatable<GameAction>
{
    public abstract ActionKind Kind { get; }

    // Who may take this action. Always the active player for generated actions -- stored so an
    // action remains self-describing when a consumer holds it apart from the state.
    public PlayerId Player { get; }

    protected GameAction(PlayerId player) => Player = player;

    // A short human-readable form. The console client numbers these; keeping the formatting
    // here rather than in the client means the AI's action logs read identically.
    public abstract string Describe();

    public abstract bool Equals(GameAction? other);

    public override bool Equals(object? obj) => Equals(obj as GameAction);

    public abstract override int GetHashCode();

    public override string ToString() => Describe();

    public static bool operator ==(GameAction? a, GameAction? b) =>
        a is null ? b is null : a.Equals(b);

    public static bool operator !=(GameAction? a, GameAction? b) => !(a == b);
}

// Play a card from hand, paying its top-left cost.
//
// TargetSlot is the board slot a creature is played into, and null for a spell (which never
// enters the board). ChosenTarget is the slot picked for the card's chosen_* selector, if it
// declares one -- at most one, per the single-target rule, which is why this is a single
// nullable field rather than a list.
public sealed class PlayCardAction : GameAction
{
    public string CardId { get; }

    public SlotIndex? TargetSlot { get; }

    public SlotIndex? ChosenTarget { get; }

    public PlayCardAction(
        PlayerId player, string cardId, SlotIndex? targetSlot = null, SlotIndex? chosenTarget = null)
        : base(player)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);

        CardId = cardId;
        TargetSlot = targetSlot;
        ChosenTarget = chosenTarget;
    }

    public override ActionKind Kind => ActionKind.PlayCard;

    public override string Describe()
    {
        var where = TargetSlot is { } slot ? $" into {slot}" : string.Empty;
        var at = ChosenTarget is { } chosen ? $" targeting {chosen}" : string.Empty;
        return $"Play {CardId}{where}{at}";
    }

    public override bool Equals(GameAction? other) =>
        other is PlayCardAction a
        && a.Player == Player
        && string.Equals(a.CardId, CardId, StringComparison.Ordinal)
        && a.TargetSlot == TargetSlot
        && a.ChosenTarget == ChosenTarget;

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Player, CardId, TargetSlot, ChosenTarget);
}

// Use one of a creature's moves.
//
// MoveIndex indexes the creature's CONCATENATED move list -- source cards in merge order, each
// card's moves in declaration order (see CardDatabase.MovesOf). It is not an index into one
// card's moves. That distinction only shows up on merged creatures, where it is also the index
// the once-per-turn bitmask uses, so the two agree by construction.
public sealed class UseMoveAction : GameAction
{
    public SlotIndex SourceSlot { get; }

    public int MoveIndex { get; }

    public SlotIndex? ChosenTarget { get; }

    public UseMoveAction(
        PlayerId player, SlotIndex sourceSlot, int moveIndex, SlotIndex? chosenTarget = null)
        : base(player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(moveIndex);

        SourceSlot = sourceSlot;
        MoveIndex = moveIndex;
        ChosenTarget = chosenTarget;
    }

    public override ActionKind Kind => ActionKind.UseMove;

    public override string Describe()
    {
        var at = ChosenTarget is { } chosen ? $" targeting {chosen}" : string.Empty;
        return $"Move #{MoveIndex} from {SourceSlot}{at}";
    }

    public override bool Equals(GameAction? other) =>
        other is UseMoveAction a
        && a.Player == Player
        && a.SourceSlot == SourceSlot
        && a.MoveIndex == MoveIndex
        && a.ChosenTarget == ChosenTarget;

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Player, SourceSlot, MoveIndex, ChosenTarget);
}

// Fold the creature in SourceSlot into the one in TargetSlot.
//
// Direction matters even though health and typing sum commutatively: the result occupies
// TargetSlot, and which slot a creature ends up in changes both scoring (its opposing slot may
// differ) and move order in the concatenated list. So Merge(0 -> 1) and Merge(1 -> 0) are two
// genuinely different actions and both are generated.
public sealed class MergeAction : GameAction
{
    public SlotIndex SourceSlot { get; }

    public SlotIndex TargetSlot { get; }

    public MergeAction(PlayerId player, SlotIndex sourceSlot, SlotIndex targetSlot)
        : base(player)
    {
        if (sourceSlot == targetSlot)
        {
            throw new ArgumentException("A creature cannot merge with itself.", nameof(targetSlot));
        }

        SourceSlot = sourceSlot;
        TargetSlot = targetSlot;
    }

    public override ActionKind Kind => ActionKind.Merge;

    public override string Describe() => $"Merge {SourceSlot} into {TargetSlot}";

    public override bool Equals(GameAction? other) =>
        other is MergeAction a
        && a.Player == Player
        && a.SourceSlot == SourceSlot
        && a.TargetSlot == TargetSlot;

    public override int GetHashCode() => HashCode.Combine(Kind, Player, SourceSlot, TargetSlot);
}

// Discard one named card from hand, paying down one point of a pending "discard N" debt.
//
// Exists because discard is a player CHOICE and every choice in this engine is a distinct legal
// action -- an effect cannot stop mid-resolution to ask a question. The `discard` op records a
// debt on GameState.PendingDiscards instead, and the generator then offers one of these per
// distinct card in hand until the debt clears.
//
// ONE CARD AT A TIME, deliberately. "Discard 3 from a hand of 4" is three successive single-card
// choices (4 options, then 3, then 2), not one action enumerating all 4-choose-3 combinations.
// That keeps the branching factor linear in hand size rather than binomial -- the same reasoning
// that makes an MCTS node one atomic action rather than a whole turn -- and it gives the console
// a flat numbered list instead of a combination picker.
//
// Identified by card id, not hand index: two copies of the same card are the same choice, so
// value equality collapses them into one edge rather than giving the search two identical
// children. It also survives the hand shifting under it, which an index would not.
//
// Note this is NOT the path for overdraw. A card drawn into a full hand is burned automatically
// (GameState.ApplyDraw) and never becomes an action.
public sealed class DiscardAction : GameAction
{
    public string CardId { get; }

    public DiscardAction(PlayerId player, string cardId)
        : base(player)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        CardId = cardId;
    }

    public override ActionKind Kind => ActionKind.Discard;

    public override string Describe() => $"Discard {CardId}";

    public override bool Equals(GameAction? other) =>
        other is DiscardAction a
        && a.Player == Player
        && string.Equals(a.CardId, CardId, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Kind, Player, CardId);
}

// End the turn: pass to the opponent, whose turn then scores, takes income, and draws.
//
// Always legal during the Actions phase, which guarantees the legal-action list is never empty
// and so a player can never be stuck with no move. The fuzz harness (step 1.13) depends on
// that for termination.
public sealed class EndTurnAction : GameAction
{
    public EndTurnAction(PlayerId player)
        : base(player)
    {
    }

    public override ActionKind Kind => ActionKind.EndTurn;

    public override string Describe() => "End turn";

    public override bool Equals(GameAction? other) => other is EndTurnAction a && a.Player == Player;

    public override int GetHashCode() => HashCode.Combine(Kind, Player);
}
