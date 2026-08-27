using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// DESIGN.md D2 item 5: the running record of everything that has happened in a match -- card plays,
// move uses, turn ends, and the EFFECTS each caused (damage, healing, destruction, scoring,
// resource and card-count changes).
//
// A RENDERING OF StateDiff, NOT NEW BOOKKEEPING. This is the whole reason item 5 is small: the
// client already computes a StateDiff for every action at both submit seams (GameRoot.Submit for
// human input, GameRoot.RunAiTurns for AI), because A2 built StateDiff precisely for the thing
// GameState.TurnEvents cannot do -- its own header notes TurnEvents "has no damage/move-used/
// resource-change entries and is cleared on EndTurn." So the (GameAction, StateDiff) pair already
// carries every effect this log needs, and nothing here has to observe the engine a second time.
// Adding a parallel event stream inside Shapes.Core would also have violated the milestone's
// "Shapes.Core stays unmodified" rule for no gain.
//
// Lives in the adapter, not in Shapes.Godot, so it is `dotnet test`-reachable outside the editor
// (the reason this project exists at all -- see DESIGN.md's project-structure note). Formatting is a
// pure function of (action, diff, state-before, cards), which is what makes it testable without a
// running scene; the overlay in Shapes.Godot only renders the entries this produces.
public sealed class ActionLog
{
    private readonly List<ActionLogEntry> _entries = [];

    public IReadOnlyList<ActionLogEntry> Entries => _entries;

    public int Count => _entries.Count;

    // Appends one action and everything it caused.
    //
    // `before` is the state the action was applied TO -- ActionText resolves a UseMoveAction's move
    // name by reading the creature at its source slot, which the action may have just destroyed,
    // so describing against the AFTER state would silently lose the name of any lethal move. Same
    // reason StateDiff itself is built from a before/after pair rather than from the result alone.
    //
    // ENDING A TURN IS ONE ACTION THAT SPANS TWO TURNS, and this is where that is untangled.
    // ActionExecutor.Apply runs AdvanceToActions() internally, so a single EndTurn Submit carries
    // both the act of ending turn N *and* turn N+1's opening scoring, income and draw. Filing the
    // whole thing under one heading is wrong either way round: stamping from `before` put the new
    // turn's income and draws above the "Turn N+1" line that should contain them, and stamping
    // from `after` would file the player's own End Turn under a turn they had not begun.
    //
    // So it is split into two entries when the turn advanced -- the action under the turn it ended,
    // its start-of-turn effects under the turn they opened. Every other action leaves the turn
    // number alone and produces exactly one entry, unchanged.
    //
    // Describing always uses `before`: a lethal move destroys the creature whose move name has to
    // be resolved, so the names only reliably exist beforehand.
    public void Add(GameAction action, StateDiff diff, GameState before, GameState after, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(cards);

        var effects = ActionLogEffects.Of(diff, before, cards);
        var description = ActionLogText.Describe(action, before, cards);

        // Split on the SEAT changing hands, not on TurnNumber changing. Only the P2 -> P1 handover
        // increments the turn counter (a "turn" is both seats' go), but AdvanceToActions runs
        // scoring, income and the draw on EVERY handover -- so keying off the number left half of
        // them merged into the outgoing player's End Turn line, reading as though P1 ending their
        // turn had scored points for P2.
        var handedOver = after.ActivePlayer != before.ActivePlayer;

        _entries.Add(new ActionLogEntry(
            before.TurnNumber,
            action.Player,
            action.Kind,
            description,
            handedOver ? [] : effects));

        // The incoming seat's opening, attributed to the seat that RECEIVES it -- it is their
        // score, their income, their draw. Filed under the after-state's turn number, which is the
        // new one on a P2 -> P1 handover and unchanged otherwise.
        if (handedOver && effects.Count > 0)
        {
            _entries.Add(new ActionLogEntry(
                after.TurnNumber,
                after.ActivePlayer,
                ActionKind.EndTurn,
                TurnStartDescription,
                effects));
        }
    }

    // Marks the synthetic entry above as a turn opening rather than something a player chose to do.
    public const string TurnStartDescription = "Turn begins";

    public void Clear() => _entries.Clear();
}

// One logged action: who did it, on which turn, what it was, and what followed from it.
//
// Turn number and player are kept as data rather than baked into the text so the overlay can group
// and align entries (and colour them per seat) without parsing strings back apart.
public sealed record ActionLogEntry(
    int TurnNumber,
    PlayerId Player,
    ActionKind Kind,
    string Description,
    IReadOnlyList<string> Effects);

// Turns one StateDiff into the effect lines shown under its action.
//
// Ordered board-then-player deliberately: the board change is what the player was looking at, and
// the resource/card bookkeeping is the footnote. Within the board, destruction is listed after the
// damage that caused it, so a lethal hit reads as cause then consequence.
public static class ActionLogEffects
{
    // KeywordFlags.None is not a keyword, so it is excluded rather than filtered at each use.
    private static readonly KeywordFlags[] KeywordFlagValues =
        [KeywordFlags.Taunt, KeywordFlags.Reflect, KeywordFlags.Ricochet];

    public static IReadOnlyList<string> Of(StateDiff diff, GameState before, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(cards);

        var lines = new List<string>();

        foreach (var slot in diff.SlotChanges)
        {
            AppendSlotEffects(lines, slot, cards);
        }

        foreach (var player in diff.PlayerChanges)
        {
            AppendPlayerEffects(lines, player);
        }

        // Scoring is attributed per creature by StateDiff.ScoringSlots, so name them rather than
        // only reporting the total the PlayerDiff above already carries -- "who scored" is the
        // question a score change actually raises.
        foreach (var slot in diff.ScoringSlots)
        {
            var name = NameAt(before, slot, cards);
            lines.Add(name is null
                ? $"{Seat(slot.Owner)} scores from {DescribeSlot(slot)}"
                : $"{name} scores from {DescribeSlot(slot)}");
        }

        if (diff.GameEnded)
        {
            lines.Add(diff.Winner is { } winner ? $"{Seat(winner)} wins" : "Game ends in a draw");
        }

        return lines;
    }

    private static void AppendSlotEffects(List<string> lines, SlotDiff slot, CardDatabase cards)
    {
        var before = slot.Before;
        var after = slot.After;

        // Arrived: played, or moved into this slot. The action line already says which, so this
        // only reports the creature that is now here and how big it is.
        if (before is null && after is not null)
        {
            lines.Add(
                $"{NameOf(after, cards)} enters {DescribeSlot(slot.Slot)} ({after.Health}/{after.MaxHealth})");
            return;
        }

        if (before is not null && after is null)
        {
            lines.Add($"{NameOf(before, cards)} destroyed in {DescribeSlot(slot.Slot)}");
            return;
        }

        if (before is null || after is null)
        {
            return;
        }

        var name = NameOf(after, cards);

        // Damage and healing are the same field moving in opposite directions, and both matter --
        // a heal is invisible on a board that shows only a current total.
        if (after.Health < before.Health)
        {
            lines.Add($"{name} takes {before.Health - after.Health} ({after.Health}/{after.MaxHealth})");
        }
        else if (after.Health > before.Health)
        {
            lines.Add($"{name} heals {after.Health - before.Health} ({after.Health}/{after.MaxHealth})");
        }

        if (after.MaxHealth != before.MaxHealth)
        {
            lines.Add($"{name} max health {Signed(after.MaxHealth - before.MaxHealth)}");
        }

        if (after.AttackBuff != before.AttackBuff)
        {
            lines.Add($"{name} attack {Signed(after.AttackBuff - before.AttackBuff)}");
        }

        // A merge folds two creatures into one slot, which otherwise shows up only as a health
        // jump -- the depth change is what says it was a merge and not a heal.
        if (after.MergeDepth != before.MergeDepth)
        {
            lines.Add($"{name} merged ({after.Health}/{after.MaxHealth})");
        }

        if (after.IsStunned != before.IsStunned)
        {
            lines.Add(after.IsStunned ? $"{name} stunned" : $"{name} no longer stunned");
        }

        // Named individually rather than as a whole-flags dump, so a grant reads as "gains taunt"
        // instead of making the reader diff two lists themselves.
        foreach (var keyword in KeywordFlagValues)
        {
            var had = before.Keywords.HasFlag(keyword);
            var has = after.Keywords.HasFlag(keyword);
            if (had != has)
            {
                lines.Add($"{name} {(has ? "gains" : "loses")} {keyword.ToString().ToLowerInvariant()}");
            }
        }
    }

    private static void AppendPlayerEffects(List<string> lines, PlayerDiff player)
    {
        var seat = Seat(player.Player);

        if (player.ScoreAfter != player.ScoreBefore)
        {
            lines.Add(
                $"{seat} score {Signed(player.ScoreAfter - player.ScoreBefore)} (now {player.ScoreAfter})");
        }

        // Per component rather than via ResourcePool.Subtract, which throws on a negative result
        // (its header: a bad payment should fail loudly rather than clamp) -- and here a negative
        // IS the normal case, since spending is the common direction.
        var spent = DescribeResourceDelta(player.ResourcesBefore, player.ResourcesAfter);
        if (spent is not null)
        {
            lines.Add($"{seat} resources {spent}");
        }

        if (player.HandSizeAfter != player.HandSizeBefore)
        {
            var delta = player.HandSizeAfter - player.HandSizeBefore;
            lines.Add(delta > 0
                ? $"{seat} draws {delta} (hand {player.HandSizeAfter})"
                : $"{seat} hand {Signed(delta)} (hand {player.HandSizeAfter})");
        }

        // Deck and discard are reported only when they move independently of the hand, so an
        // ordinary draw does not print three lines saying the same thing.
        var handDelta = player.HandSizeAfter - player.HandSizeBefore;
        var deckDelta = player.DeckSizeAfter - player.DeckSizeBefore;
        if (deckDelta != 0 && deckDelta != -handDelta)
        {
            lines.Add($"{seat} deck {Signed(deckDelta)} ({player.DeckSizeAfter} left)");
        }

        var discardDelta = player.DiscardSizeAfter - player.DiscardSizeBefore;
        if (discardDelta != 0)
        {
            lines.Add($"{seat} discard pile {Signed(discardDelta)} ({player.DiscardSizeAfter})");
        }
    }

    // "-2 spike, +1 wheel", or null when nothing moved. Reads as a delta rather than as a new
    // total because a resource line is almost always about what an action COST.
    private static string? DescribeResourceDelta(ResourcePool before, ResourcePool after)
    {
        var parts = new List<string>();
        foreach (var type in ResourceTypes.All)
        {
            var delta = after[type] - before[type];
            if (delta != 0)
            {
                // The type's NAME, not ResourceIcons.Of's glyph: this log is a wall of text read
                // line by line, where "spike" scans and a bare △ does not.
                parts.Add($"{Signed(delta)} {type.ToString().ToLowerInvariant()}");
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    // The creature's printed name, via the FIRST card folded into it -- a merged creature has no
    // single card of its own, and the base card is the half a player identifies it by.
    private static string NameOf(CreatureSnapshot creature, CardDatabase cards) =>
        cards.TryGet(creature.CardId, out var card) && card is not null ? card.Name : creature.CardId;

    private static string? NameAt(GameState state, SlotIndex slot, CardDatabase cards)
    {
        var creature = state.Board[slot];
        return creature is null
            ? null
            : cards.TryGet(creature.CardId, out var card) && card is not null ? card.Name : creature.CardId;
    }

    // "Player 2", not "P2" -- these lines are read as sentences, and the overlay already spells the
    // seat out in its action headline, so the abbreviated form only made the two disagree.
    private static string Seat(PlayerId player) => $"Player {player.ToIndex() + 1}";

    // "P2's middle slot", not SlotIndex.ToString()'s "P2:1". That form is a debugging identity --
    // fine in a test failure or a console trace, and unreadable in a sentence a player is meant to
    // scan. Three slots have names a person can point at on the board.
    public static string DescribeSlot(SlotIndex slot) =>
        $"{Seat(slot.Owner)}'s {SlotNames[slot.Slot]} slot";

    private static readonly string[] SlotNames = ["left", "middle", "right"];

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();
}
