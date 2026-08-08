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
// fallback and no card-detail inspect panel (CardDetailPanel was removed with this step).
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
    [Export] public NodePath TurnLabelPath { get; set; } = "Layout/TurnBar/TurnLabel";
    [Export] public NodePath EndTurnButtonPath { get; set; } = "Layout/TurnBar/EndTurnButton";
    [Export] public NodePath CancelTargetingButtonPath { get; set; } = "Layout/TurnBar/CancelTargetingButton";
    [Export] public NodePath GameOverPanelPath { get; set; } = "GameOverPanel";

    private PlayerPanel? _opponentPanel;
    private PlayerPanel? _selfPanel;
    private Label? _turnLabel;
    private Button? _endTurnButton;
    private Button? _cancelTargetingButton;
    private GameOverPanel? _gameOverPanel;

    private IReadOnlyList<GameAction>? _pendingTargetActions;

    public override void _Ready()
    {
        _opponentPanel = GetNode<PlayerPanel>(OpponentPanelPath);
        _selfPanel = GetNode<PlayerPanel>(SelfPanelPath);
        _turnLabel = GetNode<Label>(TurnLabelPath);
        _endTurnButton = GetNode<Button>(EndTurnButtonPath);
        _cancelTargetingButton = GetNode<Button>(CancelTargetingButtonPath);
        _gameOverPanel = GetNode<GameOverPanel>(GameOverPanelPath);

        foreach (var panel in new[] { _opponentPanel!, _selfPanel! })
        {
            panel.SlotTapped += slot => SlotTapped?.Invoke(slot);
            panel.MoveChosen += (slot, index) => MoveChosen?.Invoke(slot, index);
            panel.CardDroppedOnSlot += (id, slot) => CardDroppedOnSlot?.Invoke(id, slot);
            panel.CreatureDroppedOnSlot += (source, target) => CreatureDroppedOnSlot?.Invoke(source, target);
            panel.SpellDroppedOnSelfArea += id => SpellDroppedOnSelfArea?.Invoke(id);
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
