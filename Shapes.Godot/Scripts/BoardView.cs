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

// The whole play area: both players' slot rows, both hands, both resource/score panels, and
// the end-turn control. Pure view over whatever GameRoot.Render feeds it -- it never touches
// GameSession, only raises C# events for GameRoot to translate into GameActions (PLAN.md A2's
// boundary). Plain events rather than Godot [Signal]s: SlotIndex/GameAction are not
// Variant-marshalable, and nothing here needs to be wired from the editor's Node dock.
// Targeting/placement are UI-only state that resets every RefreshAll.
//
// PLAN.md B1a: play/merge are drag-and-drop (routed through PlayerPanel/SlotView's drag
// events below); using a move is a tap on that move's always-visible button on the board
// (MoveChosen), not a drag -- a drag alone can't disambiguate a creature with 2+ legal moves.
// SlotTapped survives only for A5's chosen-target resolution now; there is no tap-to-play
// fallback (CardDetailPanel was removed with this step). PLAN.md B1a2 replaced it with
// HoverDetailPanel (fixed in one screen corner -- see its own header), owned directly here
// rather than bubbled up to GameRoot since hover never submits a GameAction -- see _Ready below.
public partial class BoardView : Control
{
    public event Action<SlotIndex>? SlotTapped;
    public event Action<SlotIndex, int>? MoveChosen;
    public event Action? EndTurnRequested;
    public event Action<string>? DiscardRequested;
    public event Action<string, SlotIndex>? CardDroppedOnSlot;
    public event Action<SlotIndex, SlotIndex>? CreatureDroppedOnSlot;
    public event Action<string>? SpellDroppedOnSelfArea;

    [Export] public NodePath OpponentPanelPath { get; set; } = "Layout/OpponentPanel";
    [Export] public NodePath SelfPanelPath { get; set; } = "Layout/SelfPanel";
    [Export] public NodePath OpponentScoreLabelPath { get; set; } = "Layout/StatusBar/OpponentInfo/OpponentScoreLabel";
    [Export] public NodePath OpponentResourceRowPath { get; set; } = "Layout/StatusBar/OpponentInfo/OpponentResourceRow";
    [Export] public NodePath OpponentHandCountLabelPath { get; set; } = "Layout/StatusBar/OpponentInfo/OpponentHandCountLabel";
    [Export] public NodePath SelfScoreLabelPath { get; set; } = "Layout/StatusBar/SelfInfo/SelfScoreLabel";
    [Export] public NodePath SelfResourceRowPath { get; set; } = "Layout/StatusBar/SelfInfo/SelfResourceRow";
    [Export] public NodePath TurnLabelPath { get; set; } = "Layout/StatusBar/TurnLabel";
    [Export] public NodePath EndTurnButtonPath { get; set; } = "Layout/StatusBar/EndTurnButton";
    [Export] public NodePath CancelTargetingButtonPath { get; set; } = "Layout/StatusBar/CancelTargetingButton";
    [Export] public NodePath GameOverPanelPath { get; set; } = "GameOverPanel";
    [Export] public NodePath HoverDetailPanelPath { get; set; } = "HoverDetailPanel";
    [Export] public NodePath BoardAnimatorPath { get; set; } = "BoardAnimator";

    private PlayerPanel? _opponentPanel;
    private PlayerPanel? _selfPanel;
    private Label? _opponentScoreLabel;
    private HBoxContainer? _opponentResourceRow;
    private Label? _opponentHandCountLabel;
    private Label? _selfScoreLabel;
    private HBoxContainer? _selfResourceRow;
    private Label? _turnLabel;
    private Button? _endTurnButton;
    private Button? _cancelTargetingButton;
    private GameOverPanel? _gameOverPanel;
    private HoverDetailPanel? _hoverDetailPanel;
    private BoardAnimator? _boardAnimator;

    private IReadOnlyList<GameAction>? _pendingTargetActions;
    private (StateDiff Diff, PlayerId SelfSeat)? _pendingAnimation;

    // Last resource totals this view actually drew, per seat -- compared against on the next
    // Render to tell "a resource count went up" (turn income, a card effect) from "nothing
    // changed, this Render was for something else entirely" (a targeting-state refresh, a hover).
    // Null until the first Render, so game start never reads as "every resource just increased."
    private ResourcePool? _lastOpponentResources;
    private ResourcePool? _lastSelfResources;

    public override void _Ready()
    {
        _opponentPanel = GetNode<PlayerPanel>(OpponentPanelPath);
        _selfPanel = GetNode<PlayerPanel>(SelfPanelPath);
        _opponentScoreLabel = GetNode<Label>(OpponentScoreLabelPath);
        _opponentResourceRow = GetNode<HBoxContainer>(OpponentResourceRowPath);
        _opponentHandCountLabel = GetNode<Label>(OpponentHandCountLabelPath);
        _selfScoreLabel = GetNode<Label>(SelfScoreLabelPath);
        _selfResourceRow = GetNode<HBoxContainer>(SelfResourceRowPath);
        _turnLabel = GetNode<Label>(TurnLabelPath);
        _endTurnButton = GetNode<Button>(EndTurnButtonPath);
        _cancelTargetingButton = GetNode<Button>(CancelTargetingButtonPath);
        _gameOverPanel = GetNode<GameOverPanel>(GameOverPanelPath);
        _hoverDetailPanel = GetNode<HoverDetailPanel>(HoverDetailPanelPath);
        _boardAnimator = GetNode<BoardAnimator>(BoardAnimatorPath);

        foreach (var panel in new[] { _opponentPanel!, _selfPanel! })
        {
            panel.SlotTapped += slot => SlotTapped?.Invoke(slot);
            panel.MoveChosen += (slot, index) => MoveChosen?.Invoke(slot, index);
            panel.CardDroppedOnSlot += (id, slot) => CardDroppedOnSlot?.Invoke(id, slot);
            panel.CreatureDroppedOnSlot += (source, target) => CreatureDroppedOnSlot?.Invoke(source, target);
            panel.SpellDroppedOnSelfArea += id => SpellDroppedOnSelfArea?.Invoke(id);

            // PLAN.md B1a2: hover never submits a GameAction, so unlike every other gesture in
            // this file it terminates here rather than bubbling up to GameRoot -- there is
            // nothing for GameRoot to decide. HoverDetailPanel is fixed in one screen corner (see
            // its own header), so every source just says what to show, never where.
            panel.HandCardHoverStarted += text => _hoverDetailPanel!.Show(text);
            // A board creature's tooltip is the full card (cost pip, art, moves) with its LIVE
            // health substituted for the card's printed value -- otherwise a damaged 2/5 creature
            // would show its card's "5 HP" and read as undamaged.
            panel.SlotHoverStarted += (card, statLine) =>
                _hoverDetailPanel!.Show(
                    card.Name, statLine, card.SpellEffects, card.Moves, card.PrimaryType,
                    card.CostAmount, card.CardId);
            panel.HoverEnded += () => _hoverDetailPanel!.Hide();
        }

        _selfPanel.DiscardRequested += id => DiscardRequested?.Invoke(id);
        _endTurnButton.Pressed += () => EndTurnRequested?.Invoke();
        _cancelTargetingButton.Pressed += ClearSelection;

        _gameOverPanel.Visible = false;
    }

    public void Render(GameState state, CardDatabase cards, IReadOnlyList<GameAction> legalActions)
    {
        var active = state.ActivePlayer;
        var waiting = active.Opponent();

        _opponentPanel!.Render(state, cards, waiting, isActiveHand: false, legalActions);
        _selfPanel!.Render(state, cards, active, isActiveHand: true, legalActions);

        // Score/resources live in the consolidated status bar now (grouping request), not in
        // each PlayerPanel -- read directly off GameState the same way PlayerPanel used to.
        var waitingState = state[waiting];
        var activeState = state[active];
        _opponentScoreLabel!.Text = $"Opponent — Score {waitingState.Score}";
        RenderResourceRow(_opponentResourceRow!, waitingState.Resources, _lastOpponentResources);
        _lastOpponentResources = waitingState.Resources;
        _opponentHandCountLabel!.Text = $"{waitingState.Hand.Count} card(s)";
        _selfScoreLabel!.Text = $"You — Score {activeState.Score}";
        RenderResourceRow(_selfResourceRow!, activeState.Resources, _lastSelfResources);
        _lastSelfResources = activeState.Resources;

        // RenderSlots rebuilds every SlotView from scratch, which would silently drop
        // targeting highlights applied by BeginTargeting -- reapply them here so a Render
        // triggered mid-targeting (e.g. to refresh the turn label) doesn't blank the board.
        if (_pendingTargetActions is { } pending)
        {
            var targetable = SlotsFor(pending);
            _opponentPanel.SetTargetable(targetable);
            _selfPanel.SetTargetable(targetable);
        }

        _turnLabel!.Text = state.AwaitingDiscard
            ? $"Turn {state.TurnNumber} — Player {active.ToIndex() + 1}: discard {state.PendingDiscards} card(s)"
            : IsTargeting
                ? $"Turn {state.TurnNumber} — Player {active.ToIndex() + 1}: choose a target"
                : $"Turn {state.TurnNumber} — Player {active.ToIndex() + 1} to act";

        _endTurnButton!.Disabled = !legalActions.OfType<EndTurnAction>().Any();
        _gameOverPanel!.Visible = false;

        RefreshAnimatorLayout();
    }

    // Renders a player's resource totals as the same icon chips ResourceIconFactory already
    // draws for card costs, creature types, and move costs (PLAN.md's "professional icons"
    // request) -- replaces the old ResourceIcons.Describe text glyphs (e.g. "△2 ▢0 ◯1") in the
    // status bar, so a resource count and a cost badge for the same type now look identical
    // rather than one being a flat character and the other real geometry. Wheel/Anvil/Spike
    // order matches ResourceIconFactory's own type-badge ordering elsewhere (SlotView). Rebuilt
    // from scratch each Render rather than diffed -- same RemoveChild-before-QueueFree pattern
    // PlayerPanel.RenderSlots/RenderHand already use, for the same reason: QueueFree alone would
    // leave stale children in the row for the rest of this frame.
    //
    // previous is compared per-type against the new total (turn income landing here is the usual
    // case, per the "getting resources at turn start" request) so a gain reads as a pulse + a
    // floating "+N" rather than the number just silently switching, which is easy to miss --
    // Render happens far more often than a resource actually changes (targeting refreshes, hover
    // updates), so animating unconditionally on every Render would be near-constant motion for no
    // reason; comparing against the last DRAWN total is what limits this to real changes only.
    private void RenderResourceRow(HBoxContainer row, ResourcePool resources, ResourcePool? previous)
    {
        foreach (var child in row.GetChildren())
        {
            row.RemoveChild(child);
            child.QueueFree();
        }

        AddResourceIcon(row, ResourceType.Wheel, resources.Wheel, previous?.Wheel);
        AddResourceIcon(row, ResourceType.Anvil, resources.Anvil, previous?.Anvil);
        AddResourceIcon(row, ResourceType.Spike, resources.Spike, previous?.Spike);
    }

    private void AddResourceIcon(HBoxContainer row, ResourceType type, int count, int? previousCount)
    {
        var icon = ResourceIconFactory.Create(type, ResourceIconFactory.IconSize.Medium, count);
        row.AddChild(icon);

        if (previousCount is { } prev && count > prev)
        {
            // Deferred one frame: the icon just joined the tree this call, and GlobalPosition/Size
            // aren't settled until Godot's layout pass runs -- reading them synchronously here
            // yields a pre-layout (0,0) rect, the same trap RefreshAnimatorLayout's own note
            // documents for SlotView.
            var gain = count - prev;
            CallDeferred(nameof(PulseResourceIcon), icon, gain);
        }
    }

    private const float ResourcePulseSeconds = 0.32f;
    private const float ResourceFloatSeconds = 0.6f;
    private const float ResourceFloatRisePixels = 22f;
    private static readonly Color ResourceGainColor = new("8affa0");

    // Small scale-bounce on the icon itself plus a floating "+N" above it -- same recipe
    // BoardAnimator.FloatText uses for damage/heal/score numbers (rise + fade in parallel,
    // self-freeing), reimplemented locally rather than routed through BoardAnimator: the
    // resource row has no StateDiff cue of its own (it's a status-bar total, not a board-slot
    // effect), and this view already owns the icon's rect directly, so there's nothing gained by
    // detouring through the overlay's cue pipeline for one node it can position itself.
    private void PulseResourceIcon(Control icon, int gain)
    {
        if (!IsInstanceValid(icon))
        {
            return;
        }

        icon.PivotOffset = icon.Size / 2f;
        var pulse = CreateTween();
        pulse.TweenProperty(icon, "scale", new Vector2(1.35f, 1.35f), ResourcePulseSeconds * 0.4f)
            .SetEase(Tween.EaseType.Out);
        pulse.TweenProperty(icon, "scale", Vector2.One, ResourcePulseSeconds * 0.6f)
            .SetEase(Tween.EaseType.In);

        var label = new Label
        {
            Text = $"+{gain}",
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1,
        };
        label.AddThemeColorOverride("font_color", ResourceGainColor);
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 4);

        AddChild(label);
        var iconCenter = icon.GlobalPosition - GlobalPosition + icon.Size / 2f;
        label.Position = iconCenter - label.Size / 2f + new Vector2(0f, -icon.Size.Y * 0.6f);

        var floatTween = CreateTween().SetParallel();
        floatTween.TweenProperty(label, "position:y", label.Position.Y - ResourceFloatRisePixels, ResourceFloatSeconds)
            .SetEase(Tween.EaseType.Out);
        floatTween.TweenProperty(label, "modulate:a", 0f, ResourceFloatSeconds).SetEase(Tween.EaseType.In);
        floatTween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    // Slot rects for BoardAnimator (PLAN.md B1d). Deferred by one frame on purpose: RenderSlots
    // has just replaced every SlotView, and a freshly added Control's Size/GlobalPosition are
    // not settled until Godot's layout pass runs -- reading them synchronously here yields the
    // pre-layout (0,0) rect, the same trap ResourceIconFactory's own notes already document for
    // shapes drawn before their container sizes them.
    private void RefreshAnimatorLayout()
    {
        CallDeferred(nameof(CollectAnimatorLayout));
    }

    private void CollectAnimatorLayout()
    {
        if (_boardAnimator is null || _opponentPanel is null || _selfPanel is null)
        {
            return;
        }

        var rects = new Dictionary<SlotIndex, Rect2>();
        _opponentPanel.CollectSlotRects(rects);
        _selfPanel.CollectSlotRects(rects);

        _boardAnimator.UpdateLayout(
            rects,
            new Rect2(_selfScoreLabel!.GlobalPosition, _selfScoreLabel.Size),
            new Rect2(_opponentScoreLabel!.GlobalPosition, _opponentScoreLabel.Size));
    }

    // One action's visible feedback. Called after Render, so the overlay draws over the board as
    // it now is -- the state has already changed (see BoardAnimator's header on why animation
    // never gates input).
    public void PlayAnimation(StateDiff diff, PlayerId selfSeat)
    {
        // Stashed in a field rather than passed as CallDeferred arguments: those marshal through
        // Godot's Variant, which cannot carry a plain C# record like StateDiff. Deferred for the
        // same reason the layout collection is -- this runs immediately after a Render, and the
        // rects it needs are only correct once that render's layout pass has run.
        _pendingAnimation = (diff, selfSeat);
        CallDeferred(nameof(PlayPendingAnimation));
    }

    private void PlayPendingAnimation()
    {
        if (_pendingAnimation is not var (diff, selfSeat) || diff is null)
        {
            return;
        }

        _pendingAnimation = null;
        CollectAnimatorLayout();
        _boardAnimator?.Play(diff, selfSeat);
    }

    public void ShowGameOver(PlayerId? winner)
    {
        _gameOverPanel!.Show(winner);
    }

    // A move or spell needing a chosen target (single-target rule, PLAN.md A5) highlights the
    // legal target slots and remembers the actions that produced them, so the next SlotTapped
    // on one of those slots resolves back to a real GameAction via TryResolveTarget rather than
    // GameRoot having to re-derive "which chosen_* action did this tap mean."
    public void BeginTargeting(IReadOnlyList<GameAction> actionsNeedingTarget)
    {
        _pendingTargetActions = actionsNeedingTarget;
        var targetable = SlotsFor(actionsNeedingTarget);
        _opponentPanel!.SetTargetable(targetable);
        _selfPanel!.SetTargetable(targetable);
        _cancelTargetingButton!.Visible = true;
    }

    public bool IsTargeting => _pendingTargetActions is not null;

    // Resolves a slot tap taken while IsTargeting against the remembered action list. Null
    // means the tap missed every highlighted slot (e.g. an empty or non-targetable slot) --
    // GameRoot treats that as "do nothing," not "cancel," so a stray tap can't silently drop
    // the player out of targeting.
    public GameAction? TryResolveTarget(SlotIndex slot) =>
        _pendingTargetActions?.FirstOrDefault(a => TargetOf(a) == slot);

    private static SlotIndex? TargetOf(GameAction action) => action switch
    {
        UseMoveAction m => m.ChosenTarget,
        PlayCardAction p => p.ChosenTarget,
        _ => null,
    };

    private static HashSet<SlotIndex> SlotsFor(IReadOnlyList<GameAction> actions) =>
        [.. actions.Select(TargetOf).Where(s => s is not null).Select(s => s!.Value)];

    public void ClearSelection()
    {
        _pendingTargetActions = null;
        _opponentPanel?.SetTargetable(null);
        _selfPanel?.SetTargetable(null);
        if (_cancelTargetingButton is not null)
        {
            _cancelTargetingButton.Visible = false;
        }
    }
}
