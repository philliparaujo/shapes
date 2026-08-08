using System;
using System.IO;
using System.Linq;
using Godot;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Owns the one GameSession for a hotseat game and is the only script that submits
// GameActions. Sub-views never touch GameSession directly -- they raise Godot signals
// (CardTapped, SlotTapped, EndTurnRequested, ...) that GameRoot listens to and turns into
// GameSession.Submit calls, then pushes the resulting StateDiff/legal-action list back down
// to RefreshAll. This keeps every scene a pure view: PLAN.md A2's "UI only ever submits
// GameActions and never mutates state" boundary, extended down to the scene tree.
public partial class GameRoot : Control
{
    [Export] public NodePath BoardViewPath { get; set; } = "BoardView";
    [Export] public NodePath CardDetailPanelPath { get; set; } = "CardDetailPanel";
    [Export] public ulong Seed { get; set; }

    private GameSession? _session;
    private CardDatabase? _cards;

    private BoardView? _boardView;
    private CardDetailPanel? _cardDetailPanel;

    public override void _Ready()
    {
        _boardView = GetNode<BoardView>(BoardViewPath);
        _cardDetailPanel = GetNode<CardDetailPanel>(CardDetailPanelPath);

        _boardView.CardTapped += OnCardTapped;
        _boardView.SlotTapped += OnSlotTapped;
        _boardView.MoveTapped += OnMoveTapped;
        _boardView.MergeTargetTapped += OnMergeTargetTapped;
        _boardView.EndTurnRequested += OnEndTurnRequested;
        _boardView.DiscardRequested += OnDiscardRequested;
        _cardDetailPanel.Closed += () => _boardView!.ClearSelection();

        StartNewGame(Seed == 0 ? (ulong)DateTime.UtcNow.Ticks : Seed);
    }

    private void StartNewGame(ulong seed)
    {
        var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
        _cards = CardLoader.FromDirectory(cardsDir);

        var rules = RuleSet.Default;
        var random = new SeededRandom(seed);
        _session = new GameSession(rules, _cards, random, PlayerId.One);
        _session.Start(rules.StartingHandSize);

        RefreshAll();
    }

    private void RefreshAll()
    {
        if (_session is null || _cards is null || _boardView is null)
        {
            return;
        }

        var legalActions = _session.LegalActions();
        _boardView.Render(_session.State, _cards, legalActions);

        if (_session.State.IsOver)
        {
            _boardView.ShowGameOver(_session.State.Winner);
        }
    }

    private void OnCardTapped(string cardId, bool fromActiveHand)
    {
        if (_cards is null)
        {
            return;
        }

        // Tap-to-inspect, not hover: PLAN.md A3's "no hover-dependent information" rule.
        // Tapping a hand card shows its detail; if it is legally playable a Play button in
        // the detail panel submits it, so the same tap that reveals the card also reaches
        // the action that plays it.
        var card = _cards.Get(cardId);
        var canPlay = fromActiveHand && (_session?.LegalActions()
            .OfType<PlayCardAction>()
            .Any(a => a.CardId == cardId) ?? false);

        _cardDetailPanel!.ShowCard(card, canPlay, () => SubmitPlayCard(cardId));
    }

    private void SubmitPlayCard(string cardId)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<PlayCardAction>()
            .FirstOrDefault(a => a.CardId == cardId && a.TargetSlot is null);

        if (action is null)
        {
            // A creature card with no free targetless play (needs a board slot): let
            // BoardView collect the slot via SlotTapped instead of guessing one here.
            _boardView!.BeginPlacingCard(cardId);
            return;
        }

        Submit(action);
    }

    private void OnSlotTapped(SlotIndex slot)
    {
        if (_session is null)
        {
            return;
        }

        if (_boardView!.PendingPlacementCardId is { } cardId)
        {
            var action = _session.LegalActions()
                .OfType<PlayCardAction>()
                .FirstOrDefault(a => a.CardId == cardId && a.TargetSlot == slot);
            if (action is not null)
            {
                Submit(action);
            }

            _boardView.CancelPlacement();
            return;
        }

        // Otherwise a slot tap opens that creature's move list (if any) via the board view's
        // own selection state -- BoardView raises MoveTapped once a move is chosen.
        _boardView.SelectSlot(slot, _session.State, _cards!, _session.LegalActions());
    }

    private void OnMoveTapped(SlotIndex source, int moveIndex)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<UseMoveAction>()
            .FirstOrDefault(a => a.SourceSlot == source && a.MoveIndex == moveIndex && a.ChosenTarget is null);

        if (action is not null)
        {
            Submit(action);
            return;
        }

        // Needs a chosen target (A5's territory) -- ask BoardView to enter targeting mode
        // for the legal ChosenTarget options this move offers.
        var withTargets = _session.LegalActions()
            .OfType<UseMoveAction>()
            .Where(a => a.SourceSlot == source && a.MoveIndex == moveIndex && a.ChosenTarget is not null)
            .ToList();
        if (withTargets.Count > 0)
        {
            _boardView!.BeginTargeting(withTargets.Cast<GameAction>().ToList(), slot => slot == source);
        }
    }

    private void OnMergeTargetTapped(SlotIndex source, SlotIndex target)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<MergeAction>()
            .FirstOrDefault(a => a.SourceSlot == source && a.TargetSlot == target);

        if (action is not null)
        {
            Submit(action);
        }
    }

    private void OnEndTurnRequested()
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions().OfType<EndTurnAction>().FirstOrDefault();
        if (action is not null)
        {
            Submit(action);
        }
    }

    private void OnDiscardRequested(string cardId)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<DiscardAction>()
            .FirstOrDefault(a => a.CardId == cardId);

        if (action is not null)
        {
            Submit(action);
        }
    }

    private void Submit(GameAction action)
    {
        if (_session is null)
        {
            return;
        }

        _session.Submit(action);
        _boardView!.ClearSelection();
        RefreshAll();
    }
}
