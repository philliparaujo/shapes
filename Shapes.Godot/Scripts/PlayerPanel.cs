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

// One player's slice of the board: 3 slots, resource/score readout, and hand row. Used for
// both the opponent (top, hidden hand) and self (bottom, visible hand, discard-eligible) --
// which one it is is passed into Render each frame, not baked into the scene, so the same
// scene works for either seat.
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

    [Export] public NodePath ScoreLabelPath { get; set; } = "Info/ScoreLabel";
    [Export] public NodePath ResourceLabelPath { get; set; } = "Info/ResourceLabel";
    [Export] public NodePath SlotContainerPath { get; set; } = "Slots";
    [Export] public NodePath HandContainerPath { get; set; } = "HandScroll/Hand";

    private Label? _scoreLabel;
    private Label? _resourceLabel;
    private HBoxContainer? _slotContainer;
    private HBoxContainer? _handContainer;
    private readonly Dictionary<SlotIndex, SlotView> _slotViews = new();

    // Set each Render, read by _CanDropData/_DropData for the self-area spell drop -- null
    // (opponent panel, or no legal targetless spell right now) means the panel background
    // simply doesn't accept a drop, same "report don't decide" split as everywhere else, since
    // the actual legality re-check still happens in GameRoot against real legal actions.
    private bool _acceptsSpellDrop;

    public override void _Ready()
    {
        _scoreLabel = GetNode<Label>(ScoreLabelPath);
        _resourceLabel = GetNode<Label>(ResourceLabelPath);
        _slotContainer = GetNode<HBoxContainer>(SlotContainerPath);
        _handContainer = GetNode<HBoxContainer>(HandContainerPath);
    }

    public void Render(
        GameState state, CardDatabase cards, PlayerId player, bool isActiveHand,
        IReadOnlyList<GameAction> legalActions)
    {
        var playerState = state[player];
        _scoreLabel!.Text = $"Score {playerState.Score}";
        _resourceLabel!.Text = ResourceIcons.Describe(playerState.Resources);

        // A targetless spell can be dropped anywhere on the active player's own panel, not a
        // specific slot -- only offered on the self panel while it's that player's turn.
        _acceptsSpellDrop = isActiveHand && legalActions.OfType<PlayCardAction>()
            .Any(a => a.TargetSlot is null && a.ChosenTarget is null);

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

            // Every move on the creature renders, not just the currently-usable ones (PLAN.md
            // B1a) -- only computed for the active player's own creatures, since the opponent's
            // moves are never actionable and showing them would just be board noise.
            var moves = new List<(int Index, MoveText Text, bool IsUsable)>();
            if (creature is not null && slot.Owner == state.ActivePlayer)
            {
                var moveDefs = cards.MovesOf(creature.MergedFrom);
                for (var i = 0; i < moveDefs.Count; i++)
                {
                    var isUsable = legalActions.OfType<UseMoveAction>().Any(a => a.SourceSlot == slot && a.MoveIndex == i);
                    moves.Add((i, MoveText.Of(moveDefs[i]), isUsable));
                }
            }

            var isDraggable = creature is not null && legalActions.OfType<MergeAction>()
                .Any(a => a.SourceSlot == slot);

            slotView.Render(slot, creature, cards, isDraggable, moves);
            slotView.Tapped += () => SlotTapped?.Invoke(slot);
            slotView.MoveChosen += index => MoveChosen?.Invoke(slot, index);
            slotView.HandCardDropped += cardId => CardDroppedOnSlot?.Invoke(cardId, slot);
            slotView.CreatureDropped += source => CreatureDroppedOnSlot?.Invoke(source, slot);
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

        // Waiting seat's hand renders as a count, never contents -- PLAN.md's console
        // precedent (step 2.5) carried over verbatim: hiding is done by suppressing what
        // this view shows, not by handing it a narrowed ObservedState, so --reveal-style
        // debugging stays possible later without restructuring this method.
        if (!isActiveHand)
        {
            var countLabel = new Label { Text = $"{hand.Count} card(s)" };
            _handContainer.AddChild(countLabel);
            return;
        }

        var playableIds = legalActions.OfType<PlayCardAction>().Select(a => a.CardId).ToHashSet();
        var discardableIds = legalActions.OfType<DiscardAction>().Select(a => a.CardId).ToHashSet();

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
