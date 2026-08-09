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
    [Export] public NodePath SpacerPath { get; set; } = "Spacer";
    [Export] public NodePath HandContainerPath { get; set; } = "HandScroll/HandMargin/Hand";

    private HBoxContainer? _slotContainer;
    private Control? _spacer;
    private HBoxContainer? _handContainer;
    private readonly Dictionary<SlotIndex, SlotView> _slotViews = new();

    // Set each Render, read by _CanDropData/_DropData for the self-area spell drop -- null
    // (opponent panel, or no legal targetless spell right now) means the panel background
    // simply doesn't accept a drop, same "report don't decide" split as everywhere else, since
    // the actual legality re-check still happens in GameRoot against real legal actions.
    private bool _acceptsSpellDrop;

    public override void _Ready()
    {
        _slotContainer = GetNode<HBoxContainer>(SlotContainerPath);
        _spacer = GetNode<Control>(SpacerPath);
        _handContainer = GetNode<HBoxContainer>(HandContainerPath);
    }

    public void Render(
        GameState state, CardDatabase cards, PlayerId player, bool isActiveHand,
        IReadOnlyList<GameAction> legalActions)
    {
        // A targetless spell can be dropped anywhere on the active player's own panel, not a
        // specific slot -- only offered on the self panel while it's that player's turn.
        _acceptsSpellDrop = isActiveHand && legalActions.OfType<PlayCardAction>()
            .Any(a => a.TargetSlot is null && a.ChosenTarget is null);

        // The spacer between Slots and HandScroll only earns its keep when there's a real hand
        // row to push toward the bottom of the panel's share -- the opponent's collapsed
        // "N card(s)" line has nothing to gain from that and everything to lose, since an
        // always-expanding spacer here just floats the label in the middle of a big empty gap
        // instead of it sitting right under their slots.
        _spacer!.SizeFlagsVertical = isActiveHand ? SizeFlags.ExpandFill : SizeFlags.Fill;

        RenderSlots(state, cards, player, legalActions);
        RenderHand(state, cards, player, isActiveHand, legalActions);
    }

    private void RenderSlots(GameState state, CardDatabase cards, PlayerId player, IReadOnlyList<GameAction> legalActions)
    {
        foreach (var child in _slotContainer!.GetChildren())
        {
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
                var boardMoves = moveDefs.Select(MoveText.Of).ToList();

                var isOwnCreature = slot.Owner == state.ActivePlayer;
                for (var i = 0; i < moveDefs.Count; i++)
                {
                    var isUsable = isOwnCreature && legalActions.OfType<UseMoveAction>()
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

            var isDraggable = creature is not null && legalActions.OfType<MergeAction>()
                .Any(a => a.SourceSlot == slot);

            slotView.Render(slot, creature, cards, isDraggable, moves, hoverCards);
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
        GameState state, CardDatabase cards, PlayerId player, bool isActiveHand,
        IReadOnlyList<GameAction> legalActions)
    {
        foreach (var child in _handContainer!.GetChildren())
        {
            child.QueueFree();
        }

        var hand = state[player].Hand;

        // Waiting seat's hand renders as nothing here -- PLAN.md's console precedent (step 2.5)
        // carried over: hiding is done by suppressing what this view shows, not by handing it a
        // narrowed ObservedState, so --reveal-style debugging stays possible later without
        // restructuring this method. The card count itself lives in BoardView's status bar now,
        // next to the opponent's score/resources, not in the hand row.
        if (!isActiveHand)
        {
            return;
        }

        var playableIds = legalActions.OfType<PlayCardAction>().Select(a => a.CardId).ToHashSet();
        var discardableIds = legalActions.OfType<DiscardAction>().Select(a => a.CardId).ToHashSet();

        // Left padding so the first card or two doesn't render underneath HoverDetailPanel's
        // fixed bottom-left box (PLAN.md B1a2) -- the tooltip is mouse-transparent so it never
        // blocks a click/drag, but it can still visually sit in front of a card there.
        _handContainer.AddChild(new Control { CustomMinimumSize = new Vector2(HoverPanelClearanceWidth, 0) });

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

    // Matches (with a little breathing room past) HoverDetailPanel's fixed bottom-left box width
    // -- see RenderHand's own note on why the hand row needs to clear it. Derived from the same
    // CardMetrics constant the panel itself is sized from, so the two can't drift apart.
    private const float HoverPanelClearanceWidth = CardMetrics.TooltipWidth + 32f;

    private static readonly PackedScene SlotViewScene = GD.Load<PackedScene>("res://Scenes/SlotView.tscn");
    private static readonly PackedScene CardFaceScene = GD.Load<PackedScene>("res://Scenes/CardFace.tscn");
}
