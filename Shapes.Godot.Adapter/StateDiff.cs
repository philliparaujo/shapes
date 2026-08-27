using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// The view-model scenes bind to. Built by comparing GameState before/after one
// ActionExecutor.Apply call -- see GameSession. Deliberately not sourced from
// GameState.TurnEvents: TurnEvents has no damage/move-used/resource-change entries and is
// cleared on EndTurn, so it cannot describe what a single action did (DESIGN.md A2).
public sealed record StateDiff(
    IReadOnlyList<SlotDiff> SlotChanges,
    IReadOnlyList<PlayerDiff> PlayerChanges,
    TurnPhase PhaseBefore,
    TurnPhase PhaseAfter,
    PlayerId ActivePlayerBefore,
    PlayerId ActivePlayerAfter,
    bool GameEnded,
    PlayerId? Winner,
    IReadOnlyList<SlotIndex> ScoringSlots)
{
    public static StateDiff Between(GameState before, GameState after)
    {
        var slotChanges = new List<SlotDiff>();
        foreach (var slot in AllSlots())
        {
            var diff = SlotDiff.Between(slot, before.Board[slot], after.Board[slot]);
            if (diff is not null)
            {
                slotChanges.Add(diff);
            }
        }

        var playerChanges = PlayerIds.All
            .Select(player => PlayerDiff.Between(player, before[player], after[player]))
            .Where(diff => diff is not null)
            .Select(diff => diff!)
            .ToList();

        return new StateDiff(
            slotChanges,
            playerChanges,
            before.Phase,
            after.Phase,
            before.ActivePlayer,
            after.ActivePlayer,
            after.IsOver,
            after.Winner,
            ScoringSlotsOf(before, playerChanges));
    }

    // WHICH creatures earned the points, so the animator can cue each one rather than only
    // flashing a total (DESIGN.md 5.C-UI). A PlayerDiff says a score went up but not from where;
    // scoring is per unopposed creature, so the slots are recoverable -- but only from the BEFORE
    // board, since scoring resolves at turn start and the after-board may already differ.
    //
    // Empty under ScoreByCreatureDelta: that mode scores on a board-presence count with no
    // per-slot attribution, so there is no honest set of slots to point at.
    private static IReadOnlyList<SlotIndex> ScoringSlotsOf(
        GameState before, IReadOnlyList<PlayerDiff> playerChanges)
    {
        if (before.Rules.ScoreByCreatureDelta)
        {
            return [];
        }

        var scored = playerChanges
            .Where(p => p.ScoreAfter > p.ScoreBefore)
            .Select(p => p.Player)
            .ToHashSet();

        return
        [
            .. scored
                .SelectMany(SlotIndex.AllFor)
                .Where(before.Board.IsUnopposed)
        ];
    }

    private static IEnumerable<SlotIndex> AllSlots()
    {
        foreach (var player in PlayerIds.All)
        {
            foreach (var slot in SlotIndex.AllFor(player))
            {
                yield return slot;
            }
        }
    }
}

// One board slot's change, or null (via Between) if nothing about it differs.
public sealed record SlotDiff(
    SlotIndex Slot,
    CreatureSnapshot? Before,
    CreatureSnapshot? After)
{
    public static SlotDiff? Between(SlotIndex slot, CreatureInstance? before, CreatureInstance? after)
    {
        var beforeSnapshot = CreatureSnapshot.Of(before);
        var afterSnapshot = CreatureSnapshot.Of(after);
        return beforeSnapshot == afterSnapshot ? null : new SlotDiff(slot, beforeSnapshot, afterSnapshot);
    }
}

// Value-equal snapshot of a CreatureInstance's diff-relevant fields. CreatureInstance itself
// is a mutable reference type with no equality override, so a snapshot is what makes
// before/after comparable at all.
public sealed record CreatureSnapshot(
    string CardId,
    int Health,
    int MaxHealth,
    TypeMask Types,
    int MergeDepth,
    KeywordFlags Keywords,
    bool IsStunned,
    int AttackBuff)
{
    public static CreatureSnapshot? Of(CreatureInstance? creature) => creature is null
        ? null
        : new CreatureSnapshot(
            creature.CardId,
            creature.Health,
            creature.MaxHealth,
            creature.Types,
            creature.MergeDepth,
            creature.Keywords,
            creature.IsStunned,
            creature.AttackBuff);
}

// One player's non-board change: score, resources, or hand/deck/discard size.
public sealed record PlayerDiff(
    PlayerId Player,
    int ScoreBefore,
    int ScoreAfter,
    ResourcePool ResourcesBefore,
    ResourcePool ResourcesAfter,
    int HandSizeBefore,
    int HandSizeAfter,
    int DeckSizeBefore,
    int DeckSizeAfter,
    int DiscardSizeBefore,
    int DiscardSizeAfter)
{
    public static PlayerDiff? Between(PlayerId player, PlayerState before, PlayerState after)
    {
        if (before.Score == after.Score
            && before.Resources.Equals(after.Resources)
            && before.Hand.Count == after.Hand.Count
            && before.Deck.Count == after.Deck.Count
            && before.Discard.Count == after.Discard.Count)
        {
            return null;
        }

        return new PlayerDiff(
            player,
            before.Score,
            after.Score,
            before.Resources,
            after.Resources,
            before.Hand.Count,
            after.Hand.Count,
            before.Deck.Count,
            after.Deck.Count,
            before.Discard.Count,
            after.Discard.Count);
    }
}
