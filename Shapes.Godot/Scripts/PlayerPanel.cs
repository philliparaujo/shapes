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
    public event Action<string, bool>? CardTapped;
    public event Action<string>? DiscardRequested;

    [Export] public NodePath ScoreLabelPath { get; set; } = "Info/ScoreLabel";
    [Export] public NodePath ResourceLabelPath { get; set; } = "Info/ResourceLabel";
    [Export] public NodePath SlotContainerPath { get; set; } = "Slots";
    [Export] public NodePath HandContainerPath { get; set; } = "HandScroll/Hand";

    private Label? _scoreLabel;
    private Label? _resourceLabel;
    private HBoxContainer? _slotContainer;
    private HBoxContainer? _handContainer;
    private readonly Dictionary<SlotIndex, SlotView> _slotViews = new();

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
            var isTappable = creature is not null && legalActions.Any(a => a switch
            {
                UseMoveAction m => m.SourceSlot == slot,
                MergeAction merge => merge.SourceSlot == slot,
                _ => false,
            });
            slotView.Render(slot, creature, cards, isTappable);
            slotView.Tapped += () => SlotTapped?.Invoke(slot);
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
            face.Render(CardText.Of(card), isPlayable || isDiscardable);
            face.Tapped += () =>
            {
                if (state.AwaitingDiscard && isDiscardable)
                {
                    DiscardRequested?.Invoke(cardId);
                }
                else
                {
                    CardTapped?.Invoke(cardId, true);
                }
            };
        }
    }

    public SlotView GetSlotView(SlotIndex slot) => _slotViews[slot];

    public void SetTargetable(HashSet<SlotIndex>? targetable)
    {
        foreach (var (slot, view) in _slotViews)
        {
            view.SetHighlighted(targetable?.Contains(slot) ?? false);
        }
    }

    private static readonly PackedScene SlotViewScene = GD.Load<PackedScene>("res://Scenes/SlotView.tscn");
    private static readonly PackedScene CardFaceScene = GD.Load<PackedScene>("res://Scenes/CardFace.tscn");
}
