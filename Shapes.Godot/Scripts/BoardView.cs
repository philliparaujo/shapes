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
// Selection/targeting/placement are UI-only state that resets every RefreshAll.
public partial class BoardView : Control
{
    public event Action<string, bool>? CardTapped;
    public event Action<SlotIndex>? SlotTapped;
    public event Action<SlotIndex, int>? MoveTapped;
    public event Action<SlotIndex, SlotIndex>? MergeTargetTapped;
    public event Action? EndTurnRequested;
    public event Action<string>? DiscardRequested;

    [Export] public NodePath OpponentPanelPath { get; set; } = "Layout/OpponentPanel";
    [Export] public NodePath SelfPanelPath { get; set; } = "Layout/SelfPanel";
    [Export] public NodePath TurnLabelPath { get; set; } = "Layout/TurnBar/TurnLabel";
    [Export] public NodePath EndTurnButtonPath { get; set; } = "Layout/TurnBar/EndTurnButton";
    [Export] public NodePath MoveMenuPath { get; set; } = "MoveMenu";
    [Export] public NodePath GameOverPanelPath { get; set; } = "GameOverPanel";

    private PlayerPanel? _opponentPanel;
    private PlayerPanel? _selfPanel;
    private Label? _turnLabel;
    private Button? _endTurnButton;
    private MoveMenu? _moveMenu;
    private GameOverPanel? _gameOverPanel;

    public string? PendingPlacementCardId { get; private set; }

    private SlotIndex? _selectedSlot;

    public override void _Ready()
    {
        _opponentPanel = GetNode<PlayerPanel>(OpponentPanelPath);
        _selfPanel = GetNode<PlayerPanel>(SelfPanelPath);
        _turnLabel = GetNode<Label>(TurnLabelPath);
        _endTurnButton = GetNode<Button>(EndTurnButtonPath);
        _moveMenu = GetNode<MoveMenu>(MoveMenuPath);
        _gameOverPanel = GetNode<GameOverPanel>(GameOverPanelPath);

        _opponentPanel.SlotTapped += slot => SlotTapped?.Invoke(slot);
        _selfPanel.SlotTapped += slot => SlotTapped?.Invoke(slot);
        _opponentPanel.CardTapped += (id, active) => CardTapped?.Invoke(id, active);
        _selfPanel.CardTapped += (id, active) => CardTapped?.Invoke(id, active);
        _selfPanel.DiscardRequested += id => DiscardRequested?.Invoke(id);
        _endTurnButton.Pressed += () => EndTurnRequested?.Invoke();
        _moveMenu.MoveChosen += index => { if (_selectedSlot is { } s) MoveTapped?.Invoke(s, index); };
        _moveMenu.MergeChosen += target => { if (_selectedSlot is { } s) MergeTargetTapped?.Invoke(s, target); };
        _moveMenu.Cancelled += ClearSelection;

        _gameOverPanel.Visible = false;
    }

    public void Render(GameState state, CardDatabase cards, IReadOnlyList<GameAction> legalActions)
    {
        var active = state.ActivePlayer;
        var waiting = active.Opponent();

        _opponentPanel!.Render(state, cards, waiting, isActiveHand: false, legalActions);
        _selfPanel!.Render(state, cards, active, isActiveHand: true, legalActions);

        _turnLabel!.Text = state.AwaitingDiscard
            ? $"Turn {state.TurnNumber} — Player {active.ToIndex() + 1}: discard {state.PendingDiscards} card(s)"
            : $"Turn {state.TurnNumber} — Player {active.ToIndex() + 1} to act";

        _endTurnButton!.Disabled = !legalActions.OfType<EndTurnAction>().Any();
        _gameOverPanel!.Visible = false;
    }

    public void ShowGameOver(PlayerId? winner)
    {
        _gameOverPanel!.Show(winner);
    }

    // Tapping a slot with a friendly creature and no pending placement opens its move list
    // (plus adjacent merge targets) via MoveMenu, positioned at the slot so the choice reads
    // as belonging to it rather than a floating dialog -- the tap path A3 requires in place
    // of a hover tooltip.
    public void SelectSlot(SlotIndex slot, GameState state, CardDatabase cards, IReadOnlyList<GameAction> legalActions)
    {
        var creature = state.Board[slot];
        if (creature is null || slot.Owner != state.ActivePlayer)
        {
            return;
        }

        var moves = cards.MovesOf(creature.MergedFrom);
        var usable = new List<(int Index, MoveText Text)>();
        for (var i = 0; i < moves.Count; i++)
        {
            var legal = legalActions.OfType<UseMoveAction>().Any(a => a.SourceSlot == slot && a.MoveIndex == i);
            if (legal)
            {
                usable.Add((i, MoveText.Of(moves[i])));
            }
        }

        var mergeTargets = legalActions.OfType<MergeAction>()
            .Where(a => a.SourceSlot == slot)
            .Select(a => a.TargetSlot)
            .ToList();

        if (usable.Count == 0 && mergeTargets.Count == 0)
        {
            return;
        }

        _selectedSlot = slot;
        var panel = slot.Owner == state.ActivePlayer ? _selfPanel! : _opponentPanel!;
        _moveMenu!.Open(panel.GetSlotView(slot).GlobalPosition, usable, mergeTargets);
    }

    // A move needing a chosen target (A5's territory) highlights the legal target slots so
    // the next SlotTapped on one of them can be resolved back into the right GameAction by
    // GameRoot. This vertical slice wires the highlight and the tap path; GameRoot owns
    // matching the tapped slot back to the specific action.
    public void BeginTargeting(IReadOnlyList<GameAction> actionsNeedingTarget, Func<SlotIndex, bool> sourceMatch)
    {
        var targetable = SlotsFor(actionsNeedingTarget);
        _opponentPanel!.SetTargetable(targetable);
        _selfPanel!.SetTargetable(targetable);
    }

    private static HashSet<SlotIndex> SlotsFor(IReadOnlyList<GameAction> actions) =>
        [.. actions.Select(a => a switch
        {
            UseMoveAction m => m.ChosenTarget,
            PlayCardAction p => p.ChosenTarget,
            _ => null,
        }).Where(s => s is not null).Select(s => s!.Value)];

    public void BeginPlacingCard(string cardId)
    {
        PendingPlacementCardId = cardId;
    }

    public void CancelPlacement()
    {
        PendingPlacementCardId = null;
    }

    public void ClearSelection()
    {
        _selectedSlot = null;
        PendingPlacementCardId = null;
        _moveMenu?.Close();
        _opponentPanel?.SetTargetable(null);
        _selfPanel?.SetTargetable(null);
    }
}
