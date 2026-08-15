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
    public event Action? BackToLobbyRequested;
    public event Action? ExitRequested;
    public event Action<string>? DiscardRequested;
    public event Action<string, SlotIndex>? CardDroppedOnSlot;
    public event Action<SlotIndex, SlotIndex>? CreatureDroppedOnSlot;
    public event Action<string>? SpellDroppedOnSelfArea;

    [Export] public NodePath OpponentPanelPath { get; set; } = "Layout/BoardArea/RowsMargin/Rows/OpponentPanel";
    [Export] public NodePath SelfPanelPath { get; set; } = "Layout/BoardArea/RowsMargin/Rows/SelfPanel";
    [Export] public NodePath OpponentSidePath { get; set; } = "SideRail/OpponentSide";
    [Export] public NodePath SelfSidePath { get; set; } = "SideRail/SelfSide";
    [Export] public NodePath EndTurnButtonPath { get; set; } = "SideRail/MiddleColumn/EndTurnButton";
    [Export] public NodePath CancelTargetingButtonPath { get; set; } = "SideRail/MiddleColumn/CancelTargetingButton";
    [Export] public NodePath MenuPanelPath { get; set; } = "MenuPanel";
    [Export] public NodePath HoverDetailPanelPath { get; set; } = "HoverDetailPanel";
    [Export] public NodePath BoardAnimatorPath { get; set; } = "BoardAnimator";
    [Export] public NodePath HandPath { get; set; } = "Hand";

    private PlayerPanel? _opponentPanel;
    private PlayerPanel? _selfPanel;
    private SidePanel? _opponentSide;
    private SidePanel? _selfSide;
    private Button? _endTurnButton;
    private Button? _cancelTargetingButton;
    private MenuPanel? _menuPanel;
    private HoverDetailPanel? _hoverDetailPanel;
    private BoardAnimator? _boardAnimator;

    // Lives here rather than inside a PlayerPanel because the board frame (PLAN.md 5.C-UI) wraps
    // the six slots only, so the fanned hand has to sit outside it. Handed to whichever panel is
    // the active seat each Render -- which seat that is swaps every turn.
    private HandFan? _hand;

    private IReadOnlyList<GameAction>? _pendingTargetActions;
    private (StateDiff Diff, PlayerId SelfSeat)? _pendingAnimation;

    // Each seat's avatar art, keyed by PlayerId and fixed for the whole match (see SetAvatars).
    //
    // Keyed by PLAYER, not by panel: which of the two rail panels is "self" swaps every turn (see
    // Render, where the active player takes the self panel), so binding a portrait to a panel
    // would make both faces trade places on every end-turn instead of following their owners.
    private readonly Dictionary<PlayerId, Texture2D?> _avatars = [];

    public override void _Ready()
    {
        _opponentPanel = GetNode<PlayerPanel>(OpponentPanelPath);
        _selfPanel = GetNode<PlayerPanel>(SelfPanelPath);
        _opponentSide = GetNode<SidePanel>(OpponentSidePath);
        _selfSide = GetNode<SidePanel>(SelfSidePath);

        // Rows mirror about the End Turn button between them: the opponent's counts sit nearest
        // the top of the rail and its resources nearest the button, the player's the other way
        // round -- see references/game screen.png.
        _opponentSide.Build(resourcesFirst: false);
        _selfSide.Build(resourcesFirst: true);

        _endTurnButton = GetNode<Button>(EndTurnButtonPath);
        _cancelTargetingButton = GetNode<Button>(CancelTargetingButtonPath);
        _menuPanel = GetNode<MenuPanel>(MenuPanelPath);
        _hoverDetailPanel = GetNode<HoverDetailPanel>(HoverDetailPanelPath);
        _boardAnimator = GetNode<BoardAnimator>(BoardAnimatorPath);
        _hand = GetNode<HandFan>(HandPath);

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

        // Forwarded rather than handled here: leaving a match means deciding what happens to the
        // save file, which is GameRoot's business (PLAN.md C6), not this view's.
        _menuPanel.BackToLobbyRequested += () => BackToLobbyRequested?.Invoke();
        _menuPanel.ExitRequested += () => ExitRequested?.Invoke();
        _menuPanel.ResumeRequested += () => _menuPanel.Close();

        _menuPanel.Visible = false;
    }

    // True while the pause/game-over overlay is up. GameRoot checks this so ESC cannot reopen a
    // menu that is already showing, and so a finished game's menu cannot be dismissed.
    public bool IsMenuOpen => _menuPanel?.Visible ?? false;

    // PLAN.md 5.C-UI: ESC opens the same panel the game-over screen uses, minus the finality --
    // a paused game keeps its Resume button, a finished one does not.
    public void OpenPauseMenu() => _menuPanel!.Open("Paused", canResume: true);

    // The portraits for the two seats, chosen once per match by GameRoot. Stored rather than
    // applied here: the panels are assigned per SEAT on every Render, so the mapping from player
    // to panel is only known there.
    public void SetAvatars(Texture2D? one, Texture2D? two)
    {
        _avatars[PlayerId.One] = one;
        _avatars[PlayerId.Two] = two;
    }

    private Texture2D? AvatarOf(PlayerId player) =>
        _avatars.TryGetValue(player, out var texture) ? texture : null;

    public void Render(GameState state, CardDatabase cards, IReadOnlyList<GameAction> legalActions)
    {
        var active = state.ActivePlayer;
        var waiting = active.Opponent();

        // Only the self panel ever draws a hand, so it is the only one given the shared fan --
        // the opponent panel must not hold a reference to it, or its own (hidden-hand) render
        // would clear the cards the self panel just laid out.
        _opponentPanel!.AttachHand(null);
        _selfPanel!.AttachHand(_hand!);

        _opponentPanel.Render(state, cards, waiting, isActiveHand: false, legalActions);
        _selfPanel.Render(state, cards, active, isActiveHand: true, legalActions);

        // Counts/resources/health live on the right rail now (PLAN.md 5.C-UI), not in the removed
        // top status bar. Read straight off GameState, the same way the status bar did.
        var waitingState = state[waiting];
        var activeState = state[active];

        // A seat's health is what is left of the win condition its OPPONENT is racing toward, so
        // each panel is handed the other side's score subtracted from ScoreToWin. Derived from
        // the ruleset rather than hardcoded to 7 so a balance sweep that retunes scoreToWin can
        // not leave this reading a stale maximum.
        var scoreToWin = state.Rules.ScoreToWin;
        _opponentSide!.Render(waitingState, scoreToWin - activeState.Score);
        _selfSide!.Render(activeState, scoreToWin - waitingState.Score);

        // Re-applied per Render because the two panels change hands every turn -- the seat that
        // was "self" last turn is "opponent" now, so the portraits must follow their players
        // across. Setting the same texture twice is free: PlayerBadge.Portrait ignores a write
        // that does not change the value, so this only redraws on an actual swap.
        _opponentSide.SetAvatar(AvatarOf(waiting));
        _selfSide.SetAvatar(AvatarOf(active));

        // RenderSlots rebuilds every SlotView from scratch, which would silently drop
        // targeting highlights applied by BeginTargeting -- reapply them here so a Render
        // triggered mid-targeting (e.g. to refresh the turn label) doesn't blank the board.
        if (_pendingTargetActions is { } pending)
        {
            var targetable = SlotsFor(pending);
            _opponentPanel.SetTargetable(targetable);
            _selfPanel.SetTargetable(targetable);
        }

        // The standalone turn label is gone (PLAN.md 5.C-UI): whose turn it is already reads off
        // the End Turn button's enabled state and the rail's two panels, so the button carries
        // the turn NUMBER -- the one piece that had nowhere else to live -- and takes over the
        // label's job of announcing a discard/targeting prompt.
        _endTurnButton!.Text = state.AwaitingDiscard
            ? $"Discard {state.PendingDiscards}"
            : IsTargeting
                ? "Choose a target"
                : $"End Turn {state.TurnNumber}";

        _endTurnButton.Disabled = !legalActions.OfType<EndTurnAction>().Any();

        // The menu is NOT hidden here. It used to be -- the old game-over panel was re-hidden on
        // every Render and re-shown by GameRoot afterwards -- but the pause menu can be open over
        // a live game, and an AI turn resolving behind it triggers a Render that would dismiss it
        // mid-look. Closing is now driven only by Resume, or by leaving the scene entirely.
        RefreshAnimatorLayout();
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

        // Health-loss cues land beside each seat's avatar -- above the opponent's, below the
        // player's -- rather than in the middle of the board where the old score text floated.
        _boardAnimator.UpdateLayout(
            rects,
            _selfSide!.HealthCueRect,
            _opponentSide!.HealthCueRect);
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

    // No Resume button: the game is over, so there is nothing to go back to -- the only ways out
    // are the lobby or quitting.
    public void ShowGameOver(PlayerId? winner)
    {
        var title = winner is { } player ? $"Player {player.ToIndex() + 1} wins!" : "Game over.";
        _menuPanel!.Open(title, canResume: false);
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
