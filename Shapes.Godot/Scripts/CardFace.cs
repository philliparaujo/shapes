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

    [Export] public NodePath NameLabelPath { get; set; } = "Body/BodyMargin/Layout/TitleRow/NameLabel";
    [Export] public NodePath ArtHolderPath { get; set; } = "Body/BodyMargin/Layout/ArtHolder";
    [Export] public NodePath CostBadgePath { get; set; } = "Body/BodyMargin/Layout/TitleRow/CostBadge";
    [Export] public NodePath EffectsLabelPath { get; set; } = "Body/BodyMargin/Layout/EffectsLabel";
    [Export] public NodePath MoveListPath { get; set; } = "Body/BodyMargin/Layout/MoveList";
    [Export] public NodePath StatLabelPath { get; set; } = "Body/BodyMargin/Layout/StatLabel";

    // Opaque: only RGB is scaled, alpha stays 1 (see Render's note on why not translucency).
    private static readonly Color UnplayableTint = new(0.62f, 0.62f, 0.66f, 1f);

    private Label? _nameLabel;
    private Control? _artHolder;
    private Control? _costBadge;
    private Label? _effectsLabel;
    private Container? _moveList;
    private Label? _statLabel;

    private string? _cardId;
    private bool _isDraggable;
    private CardText? _text;

    // A CardFace is a fixed-size object: exactly CardMetrics.HandCardSize, never larger. Godot's
    // default is the opposite -- a Control reports its content's combined minimum and the parent
    // grows it to fit -- which is what let a card with a long move description stand taller than
    // its neighbours in the fan. Reporting the card's own size as the minimum makes the content
    // fit the card instead of the card fitting the content.
    public override Vector2 _GetMinimumSize() => CardMetrics.HandCardSize;

    // Godot draws a Button's StyleBox after _Draw, so this lands UNDER the card body -- which is
    // where a drop shadow belongs. Gives the fanned hand depth against the backdrop and against
    // the cards it overlaps.
    public override void _Draw() => CardStyle.DrawCardShadow(this, Size);

    public override void _Ready()
    {
        // Opaque card stock on every Button state -- without it the CardFace draws Godot's
        // default button panel, which is semi-transparent and differs per state, so overlapping
        // cards in the fan showed each other through. Shared with SlotView and the tooltip via
        // CardStyle so all three cannot drift apart.
        CardStyle.ApplyToButton(this, CardStyle.StockColor, CardStyle.EdgeColor);
        _nameLabel = GetNode<Label>(NameLabelPath);
        _artHolder = GetNode<Control>(ArtHolderPath);
        _costBadge = GetNode<Control>(CostBadgePath);
        _effectsLabel = GetNode<Label>(EffectsLabelPath);
        _moveList = GetNode<Container>(MoveListPath);
        _statLabel = GetNode<Label>(StatLabelPath);
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
        // Unplayable cards DARKEN rather than go translucent. Alpha let the card behind show
        // through wherever the fan overlaps, which no card game does -- a held card is opaque
        // card stock. Scaling RGB with alpha left at 1 dims it against the table instead.
        Modulate = isActionable ? Colors.White : UnplayableTint;

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

        // A hand card now renders the SAME bands as the tooltip (PLAN.md 5.C-UI): effects, moves
        // and a stat line, not just a cropped art thumbnail with a title. MoveRowFactory is the
        // shared component the tooltip and the board's move buttons already use, so a move reads
        // identically everywhere it appears.
        _effectsLabel!.Text = text.SpellEffects;
        _effectsLabel.Visible = text.SpellEffects.Length > 0;
        _statLabel!.Text = text.IsCreature ? $"{text.Health} HP" : "Spell";

        foreach (var child in _moveList!.GetChildren())
        {
            _moveList.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var move in text.Moves)
        {
            var row = MoveRowFactory.CreateContent(move);

            // CustomMinimumSize is a FLOOR, not a ceiling -- on its own it cannot stop a row from
            // growing. MoveRowFactory's description label word-wraps with no line limit, so a
            // long effect became 3-4 lines, which grew the row, then MoveList's minimum, then the
            // whole CardFace past the 243px the fan lays out. That is what made both the title
            // band and the card's top edge sit at a different height on every card.
            //
            // Capping the label's visible lines caps its minimum height, which is the only thing
            // the layout pass actually reads. Clipping (tried first) does not help: it crops what
            // is DRAWN and leaves the reported minimum untouched.
            ClampDescriptionLines(row);

            row.CustomMinimumSize = new Vector2(0, HandMoveRowHeight);
            row.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            _moveList.AddChild(row);
        }
    }

    // Finds the wrapped description inside a MoveRowFactory row and limits it to two lines. Walks
    // the row rather than having the factory take a parameter: the tooltip and the board's move
    // buttons want the full uncapped text, and this constraint belongs to the hand card alone.
    private static void ClampDescriptionLines(Node row)
    {
        foreach (var child in row.GetChildren())
        {
            if (child is Label { AutowrapMode: not TextServer.AutowrapMode.Off } label)
            {
                label.MaxLinesVisible = MaxDescriptionLines;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            }

            ClampDescriptionLines(child);
        }
    }

    private const int MaxDescriptionLines = 2;

    // Shorter than the tooltip's row: a hand card is narrower, and its move list has to share the
    // card with the art band rather than owning the panel's whole lower half.
    private const float HandMoveRowHeight = 26f;

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
