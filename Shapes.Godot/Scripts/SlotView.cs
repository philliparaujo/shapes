using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// One board slot: empty, or a creature summary (name, health, type icons) plus a row of
// always-visible move buttons -- PLAN.md B1a's replacement for the tap-slot-then-MoveMenu-popup
// path, chosen specifically so a creature's moves are readable without any interaction (fixes
// "moves not shown on the board") and so using a move is one click instead of two. Every move
// the creature has renders, not just the currently-usable ones -- an unusable move (on cooldown
// this turn, its condition unmet, unaffordable) still tells the player what the creature *can*
// do, just not right now, so it renders disabled/dimmed rather than being omitted.
//
// The script is attached directly to the root Button (SlotView IS the Button, not a wrapper
// around one) -- this is load-bearing, not stylistic. Godot's drag-and-drop virtuals
// (_GetDragData/_CanDropData/_DropData) are dispatched to whichever Control is actually under
// the mouse; a wrapper Control with a child Button meant the Button (topmost, default
// mouse_filter Stop) absorbed every mouse event and Godot never called the wrapper's overrides
// at all, so drags silently never started. Root-IS-the-Button fixes that: the node Godot asks
// for drag data is the same node whose script provides it. Sized to the >=44px touch-target
// floor via custom_minimum_size.
//
// Also a drag source (a friendly creature can be dragged onto an adjacent friendly slot to
// merge) and a drop target (a hand card can be dragged here to play/place; a friendly creature
// can be dragged here to merge into this one). Moves are deliberately NOT drag targets -- see
// PLAN.md B1a's note on why a drag alone can't disambiguate a creature with 2+ legal moves.
public partial class SlotView : Button
{
    public event Action? Tapped;
    public event Action<int>? MoveChosen;

    // Raised on a successful drop. GameRoot resolves these against real legal actions the same
    // way every other event here does -- SlotView only reports "the user dragged X onto me,"
    // never decides legality itself.
    public event Action<string>? HandCardDropped;
    public event Action<SlotIndex>? CreatureDropped;

    [Export] public NodePath NameLabelPath { get; set; } = "Layout/NameLabel";
    [Export] public NodePath HealthLabelPath { get; set; } = "Layout/HealthLabel";
    [Export] public NodePath TypeLabelPath { get; set; } = "Layout/TypeLabel";
    [Export] public NodePath MoveListPath { get; set; } = "Layout/MoveList";

    private Label? _nameLabel;
    private Label? _healthLabel;
    private Label? _typeLabel;
    private VBoxContainer? _moveList;

    private SlotIndex _slot;
    private bool _hasFriendlyDraggableCreature;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _healthLabel = GetNode<Label>(HealthLabelPath);
        _typeLabel = GetNode<Label>(TypeLabelPath);
        _moveList = GetNode<VBoxContainer>(MoveListPath);
        ToggleMode = false;
        Pressed += () => Tapped?.Invoke();
    }

    public void Render(
        SlotIndex slot, CreatureInstance? creature, CardDatabase cards, bool isDraggable,
        IReadOnlyList<(int Index, MoveText Text, bool IsUsable)> moves)
    {
        _slot = slot;
        _hasFriendlyDraggableCreature = creature is not null && isDraggable;

        foreach (var child in _moveList!.GetChildren())
        {
            child.QueueFree();
        }

        if (creature is null)
        {
            _nameLabel!.Text = "—";
            _healthLabel!.Text = string.Empty;
            _typeLabel!.Text = string.Empty;
            Disabled = false; // empty slots stay tappable for merge/placement targeting
            TooltipText = string.Empty;
            return;
        }

        var name = cards.TryGet(creature.CardId, out var card) ? card!.Name : creature.CardId;
        _nameLabel!.Text = creature.IsMerged ? $"{name}+" : name;
        _healthLabel!.Text = $"{creature.Health}/{creature.MaxHealth}";
        _typeLabel!.Text = ResourceIcons.Describe(creature.Types);
        Disabled = false;

        foreach (var (index, text, isUsable) in moves)
        {
            _moveList.AddChild(MoveButtonFactory.Create(text, isUsable, () => MoveChosen?.Invoke(index)));
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        Modulate = highlighted ? new Color(1f, 0.85f, 0.4f) : Colors.White;
    }

    // Drag SOURCE: only a friendly creature the active player can act with offers itself for
    // drag (merge). Returning null tells Godot "nothing draggable here," which is also what
    // makes an empty slot or the opponent's creature simply not pick up a drag gesture.
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!_hasFriendlyDraggableCreature)
        {
            return default;
        }

        var preview = new Label { Text = _nameLabel!.Text };
        SetDragPreview(preview);

        return DragPayload.ForCreature(_slot);
    }

    // Drop TARGET: accepts a hand card (play/place here) or a friendly creature (merge here).
    // Legality is not checked here -- GameRoot resolves the drop against real legal actions, the
    // same "view reports, GameRoot decides" split every other gesture in this codebase follows.
    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        DragPayload.TryRead(data, out _);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!DragPayload.TryRead(data, out var payload))
        {
            return;
        }

        if (payload.CardId is { } cardId)
        {
            HandCardDropped?.Invoke(cardId);
        }
        else if (payload.SourceSlot is { } source)
        {
            CreatureDropped?.Invoke(source);
        }
    }
}
