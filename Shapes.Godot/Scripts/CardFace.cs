using System;
using Godot;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// A hand card's face: title, art, and a cost pip that bleeds off the top-left corner -- the
// "In hand" column of references/card dimensions.pdf, which draws exactly those three elements at
// 7:6 (7:5 art, 5:1 title, 2:1 pip) and no others. No move list and no HP here on purpose: the
// PDF's hand card is a cut-off full card, and a hand of them is for recognizing what you hold,
// not reading it. Full text (moves, HP, descriptions) is the hover tooltip's job instead
// (HoverStarted below, PLAN.md B1a2); there is no tap-to-inspect panel (PLAN.md B1a removed
// CardDetailPanel along with tap-to-play).
//
// The script is attached directly to the root Button (CardFace IS the Button) -- load-bearing,
// not stylistic, same reasoning as SlotView: Godot's _GetDragData is dispatched to whatever
// Control is under the mouse, and a wrapper Control with a child Button meant the Button
// (topmost, default mouse_filter Stop) absorbed the mouse-down-and-drag gesture before the
// wrapper's override was ever consulted, so drags silently never started.
//
// The drag source for PLAN.md B1a: a playable card can be picked up and dropped on a board slot
// (play/place) or the self play area (a targetless spell) -- the only way to play a card.
// Tapped survives only for discard (AwaitingDiscard is tap-based, see PlayerPanel).
public partial class CardFace : Button
{
    public event Action? Tapped;

    // PLAN.md B1a2: mouse-only, no touch equivalent -- desktop players get the full detail this
    // way instead of needing a click. HoverStarted carries the CardText so GameRoot never has to
    // look the card back up; HoverEnded carries nothing since dismissing needs no card identity.
    // Deliberately NOT resizing this card (or a neighboring spacer) on hover to make room for the
    // tooltip -- both were tried and dropped: any layout change near the cursor while it's
    // stationary risks moving a different control under the pointer, which can itself fire a new
    // hover event and cascade unpredictably. HoverDetailPanel is mouse-transparent and correctly
    // sized/positioned instead (see its own notes), which removes the need to shift anything.
    public event Action<CardText>? HoverStarted;
    public event Action? HoverEnded;

    [Export] public NodePath NameLabelPath { get; set; } = "Layout/NameLabel";
    [Export] public NodePath ArtHolderPath { get; set; } = "Layout/ArtHolder";
    [Export] public NodePath CostBadgePath { get; set; } = "CostBadge";

    private Label? _nameLabel;
    private Control? _artHolder;
    private Control? _costBadge;

    private string? _cardId;
    private bool _isDraggable;
    private CardText? _text;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _artHolder = GetNode<Control>(ArtHolderPath);
        _costBadge = GetNode<Control>(CostBadgePath);
        Pressed += () => Tapped?.Invoke();
        MouseEntered += () => { if (_text is { } t) HoverStarted?.Invoke(t); };
        MouseExited += () => HoverEnded?.Invoke();
    }

    public void Render(string cardId, CardText text, bool isActionable)
    {
        _cardId = cardId;
        _isDraggable = isActionable;
        _text = text;

        _nameLabel!.Text = text.Name;
        Modulate = isActionable ? Colors.White : new Color(1f, 1f, 1f, 0.55f);

        foreach (var child in _artHolder!.GetChildren())
        {
            child.QueueFree();
        }

        if (text.PrimaryType is { } artType)
        {
            // ArtHolder is a MarginContainer, so it sorts this child to fill itself -- no
            // anchors/offsets preset, which would bake in a pre-layout (0,0) rect here.
            // Real art when this card has some, the geometric placeholder otherwise (CardArt).
            _artHolder.AddChild(CardArt.For(cardId, artType));
        }

        foreach (var child in _costBadge!.GetChildren())
        {
            child.QueueFree();
        }

        if (text.PrimaryType is { } costType)
        {
            _costBadge.AddChild(
                ResourceIconFactory.Create(costType, ResourceIconFactory.IconSize.Medium, text.CostAmount));
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!_isDraggable || _cardId is null)
        {
            return default;
        }

        var preview = new Label { Text = _nameLabel!.Text };
        SetDragPreview(preview);

        return DragPayload.ForHandCard(_cardId);
    }
}
