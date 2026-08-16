using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// One player's slice of the board: 3 slots and a hand row. Used for both the opponent (top,
// hidden hand) and self (bottom, visible hand, discard-eligible) -- which one it is is passed
// into Render each frame, not baked into the scene, so the same scene works for either seat.
// Score/resources moved out to BoardView's consolidated top status bar (grouping request) --
// this panel no longer owns or displays them.
public partial class PlayerPanel : Control
{
    public event Action<SlotIndex>? SlotTapped;
    public event Action<SlotIndex, int>? MoveChosen;
    public event Action<string>? DiscardRequested;

    // PLAN.md B1a drag-and-drop events. Card/creature drops land on a specific slot;
    // SpellDroppedOnSelfArea is for a targetless spell dragged anywhere onto the self panel's
    // background rather than a particular slot, since it never occupies the board.
    public event Action<string, SlotIndex>? CardDroppedOnSlot;
    public event Action<SlotIndex, SlotIndex>? CreatureDroppedOnSlot;
    public event Action<string>? SpellDroppedOnSelfArea;

    // PLAN.md B1a2 hover events, forwarded from whichever child raised them. HoverDetailPanel is
    // fixed in one screen corner (see its own header on why), so unlike every drag/drop event
    // above these carry no position -- only what to show.
    public event Action<CardText>? HandCardHoverStarted;
    public event Action<CardText, string>? SlotHoverStarted;
    public event Action? HoverEnded;

    [Export] public NodePath SlotContainerPath { get; set; } = "Slots";

    private HBoxContainer? _slotContainer;

    // The hand is NOT a child of this panel any more (PLAN.md 5.C-UI): the board frame wraps the
    // six slots only, so the active player's fanned hand has to live outside it, below the frame.
    // BoardView owns that node and hands it over here, which keeps every hand-rendering decision
    // (what's playable, what's discardable, hover wiring) in the one place that already made them.
    private HandFan? _handContainer;

    // Null for the waiting seat, which renders no hand at all -- see BoardView.Render.
    public void AttachHand(HandFan? hand) => _handContainer = hand;
    private readonly Dictionary<SlotIndex, SlotView> _slotViews = new();

    // Set each Render, read by _CanDropData/_DropData for the self-area spell drop -- null
    // (opponent panel, or no legal targetless spell right now) means the panel background
    // simply doesn't accept a drop, same "report don't decide" split as everywhere else, since
    // the actual legality re-check still happens in GameRoot against real legal actions.
    private bool _acceptsSpellDrop;

    public override void _Ready()
    {
        _slotContainer = GetNode<HBoxContainer>(SlotContainerPath);
    }

    // `showHand` and `interactive` were one flag (`isActiveHand`) until PLAN.md D1 split them.
    // They answer different questions and only coincided because the board used to flip every
    // turn: "is this the viewer's own hand, so should its cards be face-up" is a question about
    // PERSPECTIVE, while "may this seat act right now" is a question about TURN ORDER. Against an
    // AI seat those come apart for the whole of the opponent's turn, and collapsing them back
    // together is exactly what would put the AI's hand face-up on screen again.
    public void Render(
        GameState state, CardDatabase cards, PlayerId player, bool showHand, bool interactive,
        IReadOnlyList<GameAction> legalActions, SpentMoveTracker spentMoves)
    {
        // A targetless spell can be dropped anywhere on the player's own panel, not a specific
        // slot -- offered only on the viewer's own panel, and only while it is actually their
        // turn, so a drag started during the opponent's turn has nowhere to land.
        _acceptsSpellDrop = interactive && legalActions.OfType<PlayCardAction>()
            .Any(a => a.TargetSlot is null && a.ChosenTarget is null);

        // Neither panel expands any more (PLAN.md 5.C-UI): each is exactly as tall as its slot
        // row, and the BoardArea wrapping the pair shrink-centres them. That is what closes the
        // gap the two rows used to have between them -- the self row now sits directly under the
        // frame's centre divider instead of being pushed to the bottom of a half-screen share.
        // The old Spacer went with it: as an expanding child it was the slack that made the
        // frame taller than the slots it wraps.

        RenderSlots(state, cards, player, interactive, legalActions, spentMoves);
        RenderHand(state, cards, player, showHand, interactive, legalActions);
    }

    private void RenderSlots(
        GameState state, CardDatabase cards, PlayerId player, bool interactive,
        IReadOnlyList<GameAction> legalActions, SpentMoveTracker spentMoves)
    {
        // RemoveChild before QueueFree, not QueueFree alone: QueueFree only marks a node for
        // deletion at end of frame, so a bare QueueFree leaves the old SlotViews as children of
        // _slotContainer until then. The AddChild loop below runs in the SAME frame, so without
        // the RemoveChild the container briefly holds 6 children (3 dying + 3 new) instead of 3,
        // which skews the HBoxContainer's centered layout and the GlobalPosition BoardAnimator
        // reads off the new SlotViews via CollectSlotRects -- exactly the "animation renders at
        // the wrong slot" symptom this fixes.
        foreach (var child in _slotContainer!.GetChildren())
        {
            _slotContainer.RemoveChild(child);
            child.QueueFree();
        }

        _slotViews.Clear();

        foreach (var slot in SlotIndex.AllFor(player))
        {
            var slotView = SlotViewScene.Instantiate<SlotView>();
            _slotContainer.AddChild(slotView);
            var creature = state.Board[slot];

            // Board buttons: every move on every creature renders, including the opponent's
            // (PLAN.md B1a extended by B1c). Ownership decides whether a move is *usable*, never
            // whether it is *visible* -- an opponent's creature is exactly the thing a player
            // needs to read before committing an attack, and hiding its moves made an enemy
            // creature look like it had none. Opponent moves come through with IsUsable false, so
            // MoveButtonFactory renders them disabled and dimmed, same as any unaffordable move.
            List<CardText>? hoverCards = null;
            var moves = new List<(int Index, MoveText Text, bool IsUsable)>();
            if (creature is not null)
            {
                var moveDefs = cards.MovesOf(creature.MergedFrom);

                // Costed against the creature's OWNER, not the active player: an opponent's moves
                // render too (see the note above), and their costs are theirs -- a free-moves
                // spell the active player cast must not appear to discount enemy moves.
                var boardMoves = moveDefs
                    .Select(move => MoveText.Of(move, state.CostOfMove(slot.Owner, move.Cost)))
                    .ToList();

                // Usable only if this panel is the viewer's own AND it is their turn (PLAN.md D1).
                // The `slot.Owner == state.ActivePlayer` test this replaces was a correct reading
                // of "is this my creature" only while the viewer WAS the active player -- under a
                // Fixed viewer it would light up the opponent's move buttons as usable on the
                // opponent's turn. `interactive` already carries both halves, and the panel is
                // rendered for exactly one seat, so no separate ownership test is needed.
                for (var i = 0; i < moveDefs.Count; i++)
                {
                    var isUsable = interactive && legalActions.OfType<UseMoveAction>()
                        .Any(a => a.SourceSlot == slot && a.MoveIndex == i);
                    moves.Add((i, boardMoves[i], isUsable));
                }

                // One CardText per card folded into this creature, in merge order -- the same
                // order SlotView renders the art panes left to right, so the slot can pick by
                // which half the cursor is over (PLAN.md B1c). A merged creature's tooltip shows
                // one original card rather than the merged whole: four moves would make it taller
                // than every other card's tooltip, and the slot's own 2x2 grid already shows the
                // full merged move set, so nothing is unreachable.
                hoverCards = [.. creature.MergedFrom
                    .Select(id => cards.TryGet(id, out var c) && c is not null ? CardText.Of(c) : null)
                    .Where(t => t is not null)
                    .Select(t => t!)];
            }

            // Same gate as the move buttons: a merge drag is an action, so it needs the viewer's
            // own turn, not merely a creature that has a legal merge somewhere in the list.
            var isDraggable = interactive && creature is not null && legalActions.OfType<MergeAction>()
                .Any(a => a.SourceSlot == slot);

            slotView.Render(slot, creature, cards, isDraggable, moves, hoverCards, spentMoves);
            slotView.Tapped += () => SlotTapped?.Invoke(slot);
            slotView.MoveChosen += index => MoveChosen?.Invoke(slot, index);
            slotView.HandCardDropped += cardId => CardDroppedOnSlot?.Invoke(cardId, slot);
            slotView.CreatureDropped += source => CreatureDroppedOnSlot?.Invoke(source, slot);
            slotView.HoverStarted += (card, statLine) => SlotHoverStarted?.Invoke(card, statLine);
            slotView.HoverEnded += () => HoverEnded?.Invoke();
            _slotViews[slot] = slotView;
        }
    }

    private void RenderHand(
        GameState state, CardDatabase cards, PlayerId player, bool showHand, bool interactive,
        IReadOnlyList<GameAction> legalActions)
    {
        // The seat the viewer is NOT sitting in renders no hand here -- PLAN.md's console
        // precedent (step 2.5) carried over: hiding is done by suppressing what this view shows,
        // not by handing it a narrowed ObservedState, so --reveal-style debugging stays possible
        // later without restructuring this method. The card count itself lives in BoardView's
        // status bar now, next to the opponent's score/resources, not in the hand row.
        //
        // Gated on showHand rather than on "is it this seat's turn" (PLAN.md D1): those were the
        // same flag until a Fixed viewer made them differ, and this is the line that decides
        // whether an opponent's hand is face-up on screen. Keying it to perspective means the AI's
        // hand stays hidden on the AI's own turn -- which under the old flag was precisely when it
        // was revealed.
        //
        // Checked before touching _handContainer: the other seat is given no fan at all now that
        // the hand lives outside the panels (see AttachHand), and clearing it here would wipe the
        // cards the viewer's panel had just laid out into the shared node.
        if (!showHand || _handContainer is null)
        {
            return;
        }

        // RemoveChild before QueueFree -- see RenderSlots' note on why a bare QueueFree leaves
        // stale children in the container for the rest of this frame.
        foreach (var child in _handContainer.GetChildren())
        {
            _handContainer.RemoveChild(child);
            child.QueueFree();
        }

        var hand = state[player].Hand;

        // Empty unless it is actually the viewer's turn (PLAN.md D1). `legalActions` is always the
        // ACTIVE player's list, so during the opponent's turn these sets would otherwise describe
        // the OPPONENT's options while being applied to the VIEWER's cards -- lighting up cards as
        // draggable by card-id coincidence. The viewer's hand stays on screen either way; it just
        // renders as unplayable, which is what "wait, it's not your turn" should look like.
        var playableIds = interactive
            ? legalActions.OfType<PlayCardAction>().Select(a => a.CardId).ToHashSet()
            : [];
        var discardableIds = interactive
            ? legalActions.OfType<DiscardAction>().Select(a => a.CardId).ToHashSet()
            : [];

        // No clearance spacer here any more (it used to push the first cards clear of
        // HoverDetailPanel's fixed bottom-left box): HandFan lays out every child as a card, so a
        // spacer child would be dealt a slot in the arc. The fan is centred and width-capped
        // instead, which keeps it off the tooltip's corner on its own.
        foreach (var cardId in hand)
        {
            var face = CardFaceScene.Instantiate<CardFace>();
            _handContainer.AddChild(face);
            var card = cards.Get(cardId);
            var isPlayable = playableIds.Contains(cardId);
            var isDiscardable = discardableIds.Contains(cardId);
            face.Render(cardId, CardText.Of(card), isPlayable || isDiscardable);

            // Tap-to-play was removed with CardDetailPanel (PLAN.md B1a) -- dragging is the
            // only way to play a card now. A tap still matters for discard, since
            // AwaitingDiscard is a distinct, rare, explicitly-gated mode with no drag
            // precedent (PLAN.md B1a's own note on why discard stayed tap-based).
            if (isDiscardable)
            {
                face.Tapped += () =>
                {
                    if (state.AwaitingDiscard)
                    {
                        DiscardRequested?.Invoke(cardId);
                    }
                };
            }

            face.HoverStarted += text => HandCardHoverStarted?.Invoke(text);
            face.HoverEnded += () => HoverEnded?.Invoke();
        }

        // HandFan is a Control, not a Container, so adding children raises no sort notification
        // -- without this the cards would all stack at (0,0) until the next resize.
        _handContainer.Relayout();
    }

    // Where each slot currently sits in global coordinates, for BoardAnimator's overlay
    // (PLAN.md B1d). Rects, not SlotView references: RenderSlots QueueFrees every view on the
    // next render, so handing out nodes would hand out something about to dangle -- the overlay
    // needs a position, and a position outlives the node it came from.
    public void CollectSlotRects(Dictionary<SlotIndex, Rect2> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (var (slot, view) in _slotViews)
        {
            into[slot] = new Rect2(view.GlobalPosition, view.Size);
        }
    }

    public void SetTargetable(HashSet<SlotIndex>? targetable)
    {
        foreach (var (slot, view) in _slotViews)
        {
            view.SetHighlighted(targetable?.Contains(slot) ?? false);
        }
    }

    // Drop target for a targetless spell dragged onto this panel's background rather than a
    // specific slot (the slots themselves are covered by SlotView's own drop handling).
    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        _acceptsSpellDrop && DragPayload.TryRead(data, out var payload) && payload.CardId is not null;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (DragPayload.TryRead(data, out var payload) && payload.CardId is { } cardId)
        {
            SpellDroppedOnSelfArea?.Invoke(cardId);
        }
    }

    private static readonly PackedScene SlotViewScene = GD.Load<PackedScene>("res://Scenes/SlotView.tscn");
    private static readonly PackedScene CardFaceScene = GD.Load<PackedScene>("res://Scenes/CardFace.tscn");
}
