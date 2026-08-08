using System;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// A hand card's face: name, cost, health, and (for creatures) a compact name+cost line per
// move -- enough to recognize what the card can eventually do without needing full effect text
// in a space this small. There is deliberately no tap-to-inspect panel here (PLAN.md B1a removed
// CardDetailPanel along with tap-to-play): a per-card detail view showing full move text is
// planned as its own later piece, likely shown on hover, rather than kept as a tap panel or
// crammed into the compact hand card in the meantime.
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

    [Export] public NodePath NameLabelPath { get; set; } = "Layout/NameLabel";
    [Export] public NodePath CostLabelPath { get; set; } = "Layout/CostLabel";
    [Export] public NodePath StatLabelPath { get; set; } = "Layout/StatLabel";
    [Export] public NodePath MoveListPath { get; set; } = "Layout/MoveList";

    private Label? _nameLabel;
    private Label? _costLabel;
    private Label? _statLabel;
    private VBoxContainer? _moveList;

    private string? _cardId;
    private bool _isDraggable;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _costLabel = GetNode<Label>(CostLabelPath);
        _statLabel = GetNode<Label>(StatLabelPath);
        _moveList = GetNode<VBoxContainer>(MoveListPath);
        Pressed += () => Tapped?.Invoke();
    }

    public void Render(string cardId, CardText text, bool isActionable)
    {
        _cardId = cardId;
        _isDraggable = isActionable;

        _nameLabel!.Text = text.Name;
        _costLabel!.Text = text.Cost;
        _statLabel!.Text = text.IsCreature ? $"{text.Health} HP  {text.TypeIcons}" : "Spell";
        Modulate = isActionable ? Colors.White : new Color(1f, 1f, 1f, 0.55f);

        foreach (var child in _moveList!.GetChildren())
        {
            child.QueueFree();
        }

        // Compact name+cost only, deliberately not the full MoveButtonFactory rendering
        // SlotView uses -- a hand card's real estate can't fit full effect text without either
        // scrolling or overlapping neighboring rows (see PLAN.md B1a's layout-overflow history),
        // and the full text isn't actionable yet anyway since the card isn't on the board. Full
        // text is planned to show on hover instead.
        foreach (var move in text.Moves)
        {
            _moveList.AddChild(new Label
            {
                Text = $"{move.Name} [{move.Cost}]",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
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
