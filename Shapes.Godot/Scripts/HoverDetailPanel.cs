using System.Collections.Generic;
using Godot;
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
    [Export] public NodePath NameLabelPath { get; set; } = "Panel/Layout/NameLabel";
    [Export] public NodePath CostLabelPath { get; set; } = "Panel/Layout/CostLabel";
    [Export] public NodePath StatLabelPath { get; set; } = "Panel/Layout/StatLabel";
    [Export] public NodePath EffectsLabelPath { get; set; } = "Panel/Layout/EffectsLabel";
    [Export] public NodePath MoveListPath { get; set; } = "Panel/Layout/MoveList";

    // Matches MoveButtonFactory's board-move font size, so a creature's hover panel is no larger
    // than its on-board move buttons already are.
    private const int MoveListFontSize = 11;

    private Label? _nameLabel;
    private Label? _costLabel;
    private Label? _statLabel;
    private Label? _effectsLabel;
    private VBoxContainer? _moveList;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _costLabel = GetNode<Label>(CostLabelPath);
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
    // cost) so each label hides rather than rendering an empty line when its field is blank.
    public void Show(string name, string cost, string statLine, string spellEffects, IReadOnlyList<MoveText> moves)
    {
        _nameLabel!.Text = name;
        _nameLabel.Visible = name.Length > 0;
        _costLabel!.Text = cost;
        _costLabel.Visible = cost.Length > 0;
        _statLabel!.Text = statLine;
        _effectsLabel!.Text = spellEffects;
        _effectsLabel.Visible = spellEffects.Length > 0;

        foreach (var child in _moveList!.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var move in moves)
        {
            var label = new Label
            {
                Text = $"{move.Name} [{move.Cost}]: {move.Effects}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            label.AddThemeFontSizeOverride("font_size", MoveListFontSize);
            _moveList.AddChild(label);
        }

        Visible = true;
    }

    // Convenience for the exact-one-CardDefinition case (CardFace's hand card).
    public void Show(CardText text) => Show(
        text.Name, text.Cost, text.IsCreature ? $"{text.Health} HP  {text.TypeIcons}" : "Spell",
        text.SpellEffects, text.Moves);
}
