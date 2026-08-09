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
    // Carries a whole CardText (PLAN.md B1c): the board creature's hover used to send only a name
    // and a move list, so its tooltip had no cost pip, no art and no printed HP -- the three
    // things that make a tooltip look like the card it describes. The live Health/MaxHealth still
    // travels separately, since that is instance state the CardDefinition cannot know.
    public event Action<CardText, string>? HoverStarted;
    public event Action? HoverEnded;

    [Export] public NodePath NameLabelPath { get; set; } = "Layout/CardBody/CardMargin/CardLayout/HeaderRow/NameLabel";
    [Export] public NodePath TypeBadgePath { get; set; } = "Layout/CardBody/CardMargin/CardLayout/HeaderRow/TypeBadge";
    [Export] public NodePath ArtHolderPath { get; set; } = "Layout/CardBody/CardMargin/CardLayout/ArtHolder";
    [Export] public NodePath MoveListPath { get; set; } = "Layout/CardBody/CardMargin/CardLayout/MoveList";
    [Export] public NodePath HealthLabelPath { get; set; } = "Layout/StatusBar/StatusMargin/StatusRow/HealthLabel";
    [Export] public NodePath StatusBadgesPath { get; set; } = "Layout/StatusBar/StatusMargin/StatusRow/StatusBadges";

    // "+N atk" reads as a stat, not a status icon -- amber and bold sets it apart from both the
    // plain-white health number and the dimmer glyph badges around it, so a glance distinguishes
    // "here is a number" from "here is a symbol you'd hover/tap to learn more about."
    private static readonly Color AttackBuffColor = new(1f, 0.82f, 0.35f);
    private static readonly Color ExpiringBadgeColor = new(1f, 1f, 1f, 0.55f);

    private Label? _nameLabel;
    private Control? _typeBadge;
    private Control? _artHolder;
    private Label? _healthLabel;
    private Container? _statusBadges;
    private Container? _moveList;

    private SlotIndex _slot;
    private bool _hasFriendlyDraggableCreature;
    // One CardText per card folded into this creature, in merge order -- the same order the art
    // panes render left to right, which is what lets a hover pick by horizontal position.
    private IReadOnlyList<CardText>? _hoverCards;
    private string? _hoverStatLine;

    // Which source card the cursor is currently over, so crossing the midline of a merged
    // creature swaps the tooltip without needing to leave and re-enter the slot. -1 = not hovered.
    private int _hoveredCardIndex = -1;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _typeBadge = GetNode<Control>(TypeBadgePath);
        _artHolder = GetNode<Control>(ArtHolderPath);
        _healthLabel = GetNode<Label>(HealthLabelPath);
        _statusBadges = GetNode<Container>(StatusBadgesPath);
        _moveList = GetNode<Container>(MoveListPath);
        ToggleMode = false;
        Pressed += () => Tapped?.Invoke();
        MouseEntered += () => RaiseHover();
        MouseExited += () =>
        {
            _hoveredCardIndex = -1;
            HoverEnded?.Invoke();
        };
    }

    // Mouse motion inside the slot re-checks which half is hovered, so a merged creature swaps
    // its tooltip as the cursor crosses the midline. MouseEntered alone can't do this: it fires
    // once on entry and carries no position.
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion)
        {
            RaiseHover();
        }
    }

    // Picks the source card under the cursor and raises it. For an unmerged creature there is
    // only one card, so the position never matters; for a merged one the slot's width is split
    // evenly in merge order, matching the left-to-right order the art panes render in.
    private void RaiseHover()
    {
        if (_hoverCards is not { Count: > 0 } cards || _hoverStatLine is not { } line)
        {
            return;
        }

        var index = 0;
        if (cards.Count > 1 && Size.X > 0f)
        {
            var fraction = GetLocalMousePosition().X / Size.X;
            index = Mathf.Clamp((int)(fraction * cards.Count), 0, cards.Count - 1);
        }

        if (index == _hoveredCardIndex)
        {
            return;
        }

        _hoveredCardIndex = index;
        HoverStarted?.Invoke(cards[index], line);
    }

    public void Render(
        SlotIndex slot, CreatureInstance? creature, CardDatabase cards, bool isDraggable,
        IReadOnlyList<(int Index, MoveText Text, bool IsUsable)> moves,
        IReadOnlyList<CardText>? hoverCards = null)
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

        foreach (var child in _typeBadge!.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var child in _artHolder!.GetChildren())
        {
            child.QueueFree();
        }

        if (creature is null)
        {
            _nameLabel!.Text = "—";
            _healthLabel!.Text = string.Empty;
            Disabled = false; // empty slots stay tappable for merge/placement targeting
            TooltipText = string.Empty;
            _hoverCards = null;
            _hoverStatLine = null;
            _hoveredCardIndex = -1;
            return;
        }

        // Resource/type icons sit inline with the name (top-left) rather than the name owning
        // the whole header row -- PLAN.md B1b: a merged creature's concatenated name
        // ("Cadet+Medic") is the one thing here that can genuinely run long, so it's the label
        // that gets size_flags_horizontal=3 (expand + wrap) while everything else claims only
        // the width it needs. Health moved to its own row (StatusRow) alongside the status
        // badges instead of sharing the header with the name, which is what let a long merged
        // name push the whole slot taller before.
        // A merged creature names every card folded into it ("Basic T + Columns"), not just the
        // one whose CardId it kept plus a "+" -- the previous form silently dropped the second
        // card's identity, which is the one thing a player most needs to read off a merge (it
        // determines the combined move list and both defensive types). MergedFrom already holds
        // the ids in merge order, and is capped at RuleSet.MaxMergeDepth (2).
        var displayName = string.Join(
            " + ",
            creature.MergedFrom.Select(id => cards.TryGet(id, out var c) ? c!.Name : id));
        if (displayName.Length == 0)
        {
            displayName = cards.TryGet(creature.CardId, out var card) ? card!.Name : creature.CardId;
        }

        _nameLabel!.Text = displayName;
        _healthLabel!.Text = $"{creature.Health}/{creature.MaxHealth}";
        Disabled = false;

        // Type badge: the creature's own defensive TypeMask can hold 2-3 types once merged, so
        // this shows one small icon per type rather than trying to pick a single "primary" one --
        // unlike a hand/tooltip card's single play-cost type, a board creature's type set is the
        // actual rules-relevant fact (TypeChart's 2x-vulnerability rule reads all of it).
        foreach (var type in creature.Types.ToArray())
        {
            _typeBadge.AddChild(ResourceIconFactory.Create(type, ResourceIconFactory.IconSize.Small));
        }

        // Placeholder art (PLAN.md B1c): one giant shape for an unmerged creature, or two
        // side-by-side (one per source card) for a merged one -- MergedFrom is capped at
        // RuleSet.MaxMergeDepth (2), so "two panes" is the real worst case, not a guess. Each
        // pane keys off that source card's own play-cost type (CardText.SinglePipType's
        // derivation, re-applied per source id) rather than the union in creature.Types, so a
        // Spike+Anvil merge shows one triangle pane and one square pane instead of picking just
        // one to show.
        foreach (var sourceId in creature.MergedFrom)
        {
            if (!cards.TryGet(sourceId, out var sourceCard) || sourceCard is null)
            {
                continue;
            }

            var sourceType = CardText.SinglePipType(sourceCard.Cost);
            if (sourceType is not { } t)
            {
                continue;
            }

            var pane = ResourceIconFactory.CreateArtPlaceholder(t);
            pane.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            pane.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _artHolder.AddChild(pane);
        }

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

        // Hover shows one card's worth of detail even for a merged creature (the caller decides
        // which -- see PlayerPanel.RenderSlots), so a merged tooltip stays the same size as every
        // other card's rather than growing to hold up to 4 moves. The board slot itself already
        // shows the full merged move set as buttons, so nothing is unreachable. Falls back to
        // `moves` when the caller supplied no separate hover list (an unmerged creature, where the
        // two lists are the same thing anyway).
        // Status folds into the same stat line HoverDetailPanel shows -- B1a2's own note flagged
        // this as B1b's natural extension point rather than a second hover mechanism.
        var statusSuffix = badges.Count == 0 ? string.Empty : $"  {string.Join(" ", badges.Select(b => b.Glyph))}";

        // The stat line carries LIVE health plus status; everything else the tooltip shows (name,
        // cost pip, art, printed HP, moves) comes from the CardText of whichever source card is
        // hovered. Health has to travel separately because a merged creature's current/max is a
        // property of the instance, not of either card that made it.
        _hoverStatLine = $"{creature.Health}/{creature.MaxHealth} HP{statusSuffix}";
        _hoverCards = hoverCards;
        _hoveredCardIndex = -1;

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
