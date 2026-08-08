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

    // PLAN.md B1a2: a board creature's hover payload is its full MERGED move list (MovesOf
    // across every card folded into it), which isn't any single CardDefinition's CardText -- see
    // HoverDetailPanel's header for why that rules out reusing CardFace's CardText-shaped event.
    public event Action<string, IReadOnlyList<MoveText>>? HoverStarted;
    public event Action? HoverEnded;

    [Export] public NodePath NameLabelPath { get; set; } = "Layout/HeaderRow/NameLabel";
    [Export] public NodePath TypeLabelPath { get; set; } = "Layout/HeaderRow/TypeLabel";
    [Export] public NodePath HealthLabelPath { get; set; } = "Layout/StatusRow/HealthLabel";
    [Export] public NodePath StatusBadgesPath { get; set; } = "Layout/StatusRow/StatusBadges";
    [Export] public NodePath MoveListPath { get; set; } = "Layout/MoveList";

    // "+N atk" reads as a stat, not a status icon -- amber and bold sets it apart from both the
    // plain-white health number and the dimmer glyph badges around it, so a glance distinguishes
    // "here is a number" from "here is a symbol you'd hover/tap to learn more about."
    private static readonly Color AttackBuffColor = new(1f, 0.82f, 0.35f);
    private static readonly Color ExpiringBadgeColor = new(1f, 1f, 1f, 0.55f);

    private Label? _nameLabel;
    private Label? _healthLabel;
    private Label? _typeLabel;
    private Container? _statusBadges;
    private VBoxContainer? _moveList;

    private SlotIndex _slot;
    private bool _hasFriendlyDraggableCreature;
    private string? _hoverStatLine;
    private IReadOnlyList<MoveText>? _hoverMoves;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _healthLabel = GetNode<Label>(HealthLabelPath);
        _typeLabel = GetNode<Label>(TypeLabelPath);
        _statusBadges = GetNode<Container>(StatusBadgesPath);
        _moveList = GetNode<VBoxContainer>(MoveListPath);
        ToggleMode = false;
        Pressed += () => Tapped?.Invoke();
        MouseEntered += () =>
        {
            if (_hoverStatLine is { } line && _hoverMoves is { } moves)
            {
                HoverStarted?.Invoke(line, moves);
            }
        };
        MouseExited += () => HoverEnded?.Invoke();
    }

    public void Render(
        SlotIndex slot, CreatureInstance? creature, CardDatabase cards, bool isDraggable,
        IReadOnlyList<(int Index, MoveText Text, bool IsUsable)> moves,
        IReadOnlyList<MoveText>? hoverMoves = null)
    {
        _slot = slot;
        _hasFriendlyDraggableCreature = creature is not null && isDraggable;

        foreach (var child in _moveList!.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var child in _statusBadges!.GetChildren())
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
            _hoverStatLine = null;
            _hoverMoves = null;
            return;
        }

        // Resource/type icons sit inline with the name (top-left) rather than the name owning
        // the whole header row -- PLAN.md B1b: a merged creature's concatenated name
        // ("Cadet+Medic") is the one thing here that can genuinely run long, so it's the label
        // that gets size_flags_horizontal=3 (expand + wrap) while everything else claims only
        // the width it needs. Health moved to its own row (StatusRow) alongside the status
        // badges instead of sharing the header with the name, which is what let a long merged
        // name push the whole slot taller before.
        var name = cards.TryGet(creature.CardId, out var card) ? card!.Name : creature.CardId;
        var displayName = creature.IsMerged ? $"{name}+" : name;
        _nameLabel!.Text = displayName;
        _typeLabel!.Text = ResourceIcons.Describe(creature.Types);
        _healthLabel!.Text = $"{creature.Health}/{creature.MaxHealth}";
        Disabled = false;

        var badges = StatusIcons.Describe(creature);
        foreach (var badge in badges)
        {
            var label = new Label { Text = badge.Glyph, TooltipText = badge.Tooltip };
            if (badge.IsText)
            {
                // The "+N atk" buff reads as a stat, not a symbol -- amber/bold sets it apart
                // from health (plain white, in the same row) and from the glyph badges beside it.
                label.AddThemeColorOverride("font_color", AttackBuffColor);
                label.AddThemeFontSizeOverride("font_size", 14);
            }
            else if (badge.IsExpiring)
            {
                label.Modulate = ExpiringBadgeColor;
            }

            _statusBadges.AddChild(label);
        }

        // Hover always shows the creature's full move list regardless of whose turn it is or
        // who owns it -- board buttons (below) stay restricted to the active player's own
        // creatures (moves aren't actionable otherwise and would just be board noise), but
        // reading what an opponent's creature CAN do is exactly what hover is for. Falls back to
        // `moves` itself when the caller didn't supply a separate hover list (the active
        // player's own creature, where the two lists are the same thing anyway).
        // Status folds into the same stat line HoverDetailPanel shows -- B1a2's own note flagged
        // this as B1b's natural extension point rather than a second hover mechanism.
        var statusSuffix = badges.Count == 0 ? string.Empty : $"  {string.Join(" ", badges.Select(b => b.Glyph))}";
        _hoverStatLine = $"{displayName}  {creature.Health}/{creature.MaxHealth} HP  {ResourceIcons.Describe(creature.Types)}{statusSuffix}";
        _hoverMoves = hoverMoves ?? [.. moves.Select(m => m.Text)];

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
