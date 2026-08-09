using System.Collections.Generic;
using Godot;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// PLAN.md B1a2: the hover-triggered full-detail view B1a's round 3 promised (three times, in
// comments, without ever scheduling it) when it compacted hand cards to name+cost and deleted
// CardDetailPanel entirely. Desktop-only by nature -- there is no hover on a touch device, so
// this is purely additive over B1a's tap/drag model, never required to see or play a card.
//
// Shows the same full name/cost/stats/effects/move-text rendering CardDetailPanel used to, but
// read-only: no Play button, nothing here ever submits a GameAction. Dismissed on mouse-out
// rather than a click, so it never competes with the drag/tap gestures B1a already owns -- a
// tooltip that could itself be clicked would blur the line between "inspecting" and "acting."
//
// Fixed position (bottom-left of the screen), not anchored to whatever's hovered -- tried
// anchoring to the hovered control first (grow up/down/center depending on screen edges) and
// dropped it: the player's eyes had to track a different spot for every card/slot, and getting
// that positioning math right against every screen edge case proved far more fragile than
// reading from one place. One fixed spot means the same detail always reads from the same
// corner regardless of whether the hovered card is a hand card, one of your board creatures, or
// one of the opponent's -- SlotView/CardFace only need to say WHAT to show, never WHERE.
//
// Takes plain fields, not a CardText, because its two callers have different shapes to show: a
// CardFace's hand card is exactly one CardDefinition's CardText, but a SlotView's board creature
// shows the MERGED move list (MovesOf across every card folded into it via MergedFrom), which
// isn't any single CardDefinition's CardText. Both build what they already have and hand it over
// rather than this panel re-deriving one shape from the other.
public partial class HoverDetailPanel : Control
{
    [Export] public NodePath NameLabelPath { get; set; } = "Panel/PanelMargin/Layout/TitleRow/NameLabel";
    [Export] public NodePath CostBadgePath { get; set; } = "Panel/PanelMargin/Layout/TitleRow/CostBadge";
    [Export] public NodePath ArtHolderPath { get; set; } = "Panel/PanelMargin/Layout/ArtHolder";
    [Export] public NodePath StatLabelPath { get; set; } = "Panel/PanelMargin/Layout/StatLabel";
    [Export] public NodePath EffectsLabelPath { get; set; } = "Panel/PanelMargin/Layout/EffectsLabel";
    [Export] public NodePath MoveListPath { get; set; } = "Panel/PanelMargin/Layout/MoveList";

    private Label? _nameLabel;
    private Control? _costBadge;
    private Control? _artHolder;
    private Label? _statLabel;
    private Label? _effectsLabel;
    private VBoxContainer? _moveList;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _costBadge = GetNode<Control>(CostBadgePath);
        _artHolder = GetNode<Control>(ArtHolderPath);
        _statLabel = GetNode<Label>(StatLabelPath);
        _effectsLabel = GetNode<Label>(EffectsLabelPath);
        _moveList = GetNode<VBoxContainer>(MoveListPath);

        // Never itself the target of a mouse-enter/exit -- see the class header on why a
        // hoverable tooltip would fight the gesture it's describing.
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    // name/cost are individually optional (SlotView's board-creature hover has neither -- its
    // display name and stats are already one combined statLine, and a board creature has no
    // cost) so the name label hides rather than rendering an empty line when blank, and
    // primaryType null skips both the cost badge and the art placeholder (PLAN.md B1c: a board
    // creature's hover doesn't need either -- its icons are already visible inline on the slot).
    public void Show(
        string name, string statLine, string spellEffects, IReadOnlyList<MoveText> moves,
        ResourceType? primaryType = null, int costAmount = 0, string? cardId = null)
    {
        _nameLabel!.Text = name;
        _nameLabel.Visible = name.Length > 0;
        _statLabel!.Text = statLine;
        _effectsLabel!.Text = spellEffects;
        _effectsLabel.Visible = spellEffects.Length > 0;

        foreach (var child in _costBadge!.GetChildren())
        {
            child.QueueFree();
        }

        // CostBadge/ArtHolder are MarginContainers, so each sorts its child to fill itself -- no
        // anchors/offsets preset, which would bake in a pre-layout (0,0) rect.
        if (primaryType is { } costType)
        {
            _costBadge.AddChild(
                ResourceIconFactory.Create(costType, ResourceIconFactory.IconSize.Medium, costAmount));
        }

        foreach (var child in _artHolder!.GetChildren())
        {
            child.QueueFree();
        }

        if (primaryType is { } artType)
        {
            // cardId is optional only because Show's other callers predate art; when it is null
            // this still renders the placeholder, same as before.
            _artHolder.AddChild(cardId is null
                ? ResourceIconFactory.CreateArtPlaceholder(artType)
                : CardArt.For(cardId, artType));
        }

        foreach (var child in _moveList!.GetChildren())
        {
            child.QueueFree();
        }

        // Shared with the board's move buttons (MoveRowFactory): numbered cost pip, then the move
        // name over its description in smaller text. Previously this view built its own row with
        // an unnumbered pip and name+description concatenated into one label at one size, which is
        // exactly the drift a shared component removes.
        // Each row is clamped to a fixed height rather than sized by its own wrapped text: an
        // uncapped description label grows the row, the list, and the whole panel, which is what
        // let the move list swallow the tooltip. ClipContents inside the row (MoveRowFactory)
        // trims anything that still overflows rather than pushing the card taller.
        foreach (var move in moves)
        {
            var row = MoveRowFactory.CreateContent(move);
            row.CustomMinimumSize = new Vector2(0, CardMetrics.TooltipMoveHeight);
            row.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            _moveList.AddChild(row);
        }

        Visible = true;
    }

    // Convenience for the exact-one-CardDefinition case (CardFace's hand card). No type glyphs in
    // the stat line: the cost badge already carries the card's type as its shape and color, so
    // repeating it as text was redundant (and the PDF shows no such line).
    public void Show(CardText text) => Show(
        text.Name, text.IsCreature ? $"{text.Health} HP" : "Spell",
        text.SpellEffects, text.Moves, text.PrimaryType, text.CostAmount, text.CardId);
}
