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
    [Export] public NodePath OpponentResourceLabelPath { get; set; } = "Layout/StatusBar/OpponentInfo/OpponentResourceLabel";
    [Export] public NodePath OpponentHandCountLabelPath { get; set; } = "Layout/StatusBar/OpponentInfo/OpponentHandCountLabel";
    [Export] public NodePath SelfScoreLabelPath { get; set; } = "Layout/StatusBar/SelfInfo/SelfScoreLabel";
    [Export] public NodePath SelfResourceLabelPath { get; set; } = "Layout/StatusBar/SelfInfo/SelfResourceLabel";
    [Export] public NodePath TurnLabelPath { get; set; } = "Layout/StatusBar/TurnLabel";
    [Export] public NodePath EndTurnButtonPath { get; set; } = "Layout/StatusBar/EndTurnButton";
    [Export] public NodePath CancelTargetingButtonPath { get; set; } = "Layout/StatusBar/CancelTargetingButton";
    [Export] public NodePath GameOverPanelPath { get; set; } = "GameOverPanel";
    [Export] public NodePath HoverDetailPanelPath { get; set; } = "HoverDetailPanel";

    private PlayerPanel? _opponentPanel;
    private PlayerPanel? _selfPanel;
    private Label? _opponentScoreLabel;
    private Label? _opponentResourceLabel;
    private Label? _opponentHandCountLabel;
    private Label? _selfScoreLabel;
    private Label? _selfResourceLabel;
    private Label? _turnLabel;
    private Button? _endTurnButton;
    private Button? _cancelTargetingButton;
    private GameOverPanel? _gameOverPanel;
    private HoverDetailPanel? _hoverDetailPanel;

    private IReadOnlyList<GameAction>? _pendingTargetActions;

    public override void _Ready()
    {
        _opponentPanel = GetNode<PlayerPanel>(OpponentPanelPath);
        _selfPanel = GetNode<PlayerPanel>(SelfPanelPath);
        _opponentScoreLabel = GetNode<Label>(OpponentScoreLabelPath);
        _opponentResourceLabel = GetNode<Label>(OpponentResourceLabelPath);
        _opponentHandCountLabel = GetNode<Label>(OpponentHandCountLabelPath);
        _selfScoreLabel = GetNode<Label>(SelfScoreLabelPath);
        _selfResourceLabel = GetNode<Label>(SelfResourceLabelPath);
        _turnLabel = GetNode<Label>(TurnLabelPath);
        _endTurnButton = GetNode<Button>(EndTurnButtonPath);
        _cancelTargetingButton = GetNode<Button>(CancelTargetingButtonPath);
        _gameOverPanel = GetNode<GameOverPanel>(GameOverPanelPath);
        _hoverDetailPanel = GetNode<HoverDetailPanel>(HoverDetailPanelPath);

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
            panel.SlotHoverStarted += (name, statLine, moves) =>
                _hoverDetailPanel!.Show(name, statLine, string.Empty, moves);
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
        _opponentResourceLabel!.Text = ResourceIcons.Describe(waitingState.Resources);
        _opponentHandCountLabel!.Text = $"{waitingState.Hand.Count} card(s)";
        _selfScoreLabel!.Text = $"You — Score {activeState.Score}";
        _selfResourceLabel!.Text = ResourceIcons.Describe(activeState.Resources);

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
