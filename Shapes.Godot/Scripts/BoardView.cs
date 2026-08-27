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
// GameSession, only raises C# events for GameRoot to translate into GameActions (DESIGN.md A2's
// boundary). Plain events rather than Godot [Signal]s: SlotIndex/GameAction are not
// Variant-marshalable, and nothing here needs to be wired from the editor's Node dock.
// Targeting/placement are UI-only state that resets every RefreshAll.
//
// DESIGN.md B1a: play/merge are drag-and-drop (routed through PlayerPanel/SlotView's drag
// events below); using a move is a tap on that move's always-visible button on the board
// (MoveChosen), not a drag -- a drag alone can't disambiguate a creature with 2+ legal moves.
// SlotTapped survives only for A5's chosen-target resolution now; there is no tap-to-play
// fallback (CardDetailPanel was removed with this step). DESIGN.md B1a2 replaced it with
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
    [Export] public NodePath TutorialOverlayPath { get; set; } = "TutorialOverlay";
    [Export] public NodePath SoundsPanelPath { get; set; } = "SoundsPanel";
    [Export] public NodePath HoverDetailPanelPath { get; set; } = "HoverDetailPanel";
    [Export] public NodePath BoardAnimatorPath { get; set; } = "BoardAnimator";
    [Export] public NodePath TypeCycleChartPath { get; set; } = "TypeCycleChart";
    [Export] public NodePath SettingsButtonPath { get; set; } = "SettingsButton";
    [Export] public NodePath HandPath { get; set; } = "Hand";
    [Export] public NodePath ActionRecapPanelPath { get; set; } = "ActionRecapPanel";
    [Export] public NodePath ActionLogOverlayPath { get; set; } = "ActionLogOverlay";
    [Export] public NodePath LogButtonPath { get; set; } = "LogButton";
    [Export] public NodePath LayoutPath { get; set; } = "Layout";
    [Export] public NodePath BoardAreaPath { get; set; } = "Layout/BoardArea";
    [Export] public NodePath SideRailPath { get; set; } = "SideRail";

    private PlayerPanel? _opponentPanel;
    private PlayerPanel? _selfPanel;
    private SidePanel? _opponentSide;
    private SidePanel? _selfSide;
    private Button? _endTurnButton;
    private Button? _cancelTargetingButton;
    private MenuPanel? _menuPanel;
    private TutorialOverlay? _tutorialOverlay;
    private SoundsPanel? _soundsPanel;
    private HoverDetailPanel? _hoverDetailPanel;
    private TypeCycleChart? _typeCycleChart;
    private Button? _settingsButton;
    private BoardAnimator? _boardAnimator;
    private ActionRecapPanel? _actionRecapPanel;
    private ActionLogOverlay? _actionLogOverlay;
    private Button? _logButton;

    // Supplies the log's entries at the moment it opens (DESIGN.md D2 item 5). A callback rather
    // than a stored list because GameRoot owns the log -- it is the only thing that sees every
    // action, human and AI alike -- and this view should not hold a second reference that could
    // fall out of date with it.
    public Func<IReadOnlyList<ActionLogEntry>>? ActionLogSource { get; set; }

    // Lives here rather than inside a PlayerPanel because the board frame (DESIGN.md 5.C-UI) wraps
    // the six slots only, so the fanned hand has to sit outside it. Handed to whichever panel is
    // the active seat each Render -- which seat that is swaps every turn.
    private HandFan? _hand;

    // Left edge shared by the type-cycle chart and the hover tooltip below it, so the two read as
    // one column (DESIGN.md D7). Matches HoverDetailPanel.tscn's own offset_left.
    private const float TypeChartLeft = 12f;

    // How far the side rail moves left of its authored position on touch, so the settings gear and
    // log button no longer overlap it. The buttons are 44 wide and sit 14 from the edge, plus the
    // 30-unit safe-area inset and a small gap.
    private const float RailButtonClearance = 96f;

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
        // DESIGN.md D3 phase 1. Applied here rather than on GameRoot because this is the node that
        // owns the visual tree -- the overlays (menu, rules, log) are its children, so they inherit
        // too. C-UI already styled most of this screen by hand, so the theme mainly reaches the
        // controls that were left stock: the pause menu's buttons and the log overlay's chrome.
        // DESIGN.md D7: content scale, applied before anything measures itself. Idempotent and
        // no-op on desktop -- see Platform.ApplyContentScale.
        Platform.ApplyContentScale(this);

        UiTheme.ApplyTo(this);

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
        _tutorialOverlay = GetNode<TutorialOverlay>(TutorialOverlayPath);
        _soundsPanel = GetNode<SoundsPanel>(SoundsPanelPath);
        _hoverDetailPanel = GetNode<HoverDetailPanel>(HoverDetailPanelPath);
        _boardAnimator = GetNode<BoardAnimator>(BoardAnimatorPath);
        _hand = GetNode<HandFan>(HandPath);
        _typeCycleChart = GetNode<TypeCycleChart>(TypeCycleChartPath);
        _settingsButton = GetNode<Button>(SettingsButtonPath);
        _actionRecapPanel = GetNode<ActionRecapPanel>(ActionRecapPanelPath);
        _actionLogOverlay = GetNode<ActionLogOverlay>(ActionLogOverlayPath);
        _logButton = GetNode<Button>(LogButtonPath);

        // NO YIELDING BETWEEN THE RECAP AND THE HOVER TOOLTIP. An earlier cut had the recap hide
        // whenever the tooltip appeared, which fixed the overlap and broke the common case: playing
        // a card yourself moves the cursor over the hand, so your own recap flashed up and vanished
        // immediately. The left edge has room for both once the recap is sized honestly -- a played
        // card shows only the card (no caption above it) and a used move shows a 60px strip rather
        // than a whole card face -- so the fix is the sizing, not a visibility rule.

        foreach (var panel in new[] { _opponentPanel!, _selfPanel! })
        {
            panel.SlotTapped += slot => SlotTapped?.Invoke(slot);
            panel.MoveChosen += (slot, index) => MoveChosen?.Invoke(slot, index);
            panel.CardDroppedOnSlot += (id, slot) => CardDroppedOnSlot?.Invoke(id, slot);
            panel.CreatureDroppedOnSlot += (source, target) => CreatureDroppedOnSlot?.Invoke(source, target);
            panel.SpellDroppedOnSelfArea += id => SpellDroppedOnSelfArea?.Invoke(id);

            // DESIGN.md B1a2: hover never submits a GameAction, so unlike every other gesture in
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
        // save file, which is GameRoot's business (DESIGN.md C6), not this view's.
        _menuPanel.BackToLobbyRequested += () => BackToLobbyRequested?.Invoke();
        _menuPanel.ExitRequested += () => ExitRequested?.Invoke();
        _menuPanel.ResumeRequested += () => _menuPanel.Close();
        _menuPanel.RulesRequested += () => _tutorialOverlay!.Open();

        // DESIGN.md D4: Sounds sits directly below Rules in the pause menu and opens the same panel
        // scene the lobby's own Sounds button does -- one page, two entry points, exactly as C7
        // arranged for the rules overlay.
        _menuPanel.SoundsRequested += () => _soundsPanel!.Open();

        _menuPanel.Visible = false;

        _tutorialOverlay.CloseRequested += () => _tutorialOverlay.Close();
        _tutorialOverlay.Visible = false;

        _soundsPanel.CloseRequested += () => _soundsPanel.Close();
        _soundsPanel.Visible = false;

        // Opens the same panel ESC does -- the button is a discoverable, mouse-only entry point
        // to the pause menu, not a second menu with its own behaviour.
        _settingsButton!.Pressed += OpenPauseMenu;

        // DESIGN.md D2 item 5. Bottom-RIGHT because the hover detail panel and the recap both own the
        // bottom-left; the corner is otherwise unused (the settings gear is top-right) and HandFan
        // spans that band with mouse_filter = ignore, so nothing swallows the click.
        _logButton!.Pressed += OpenActionLog;
        _actionLogOverlay!.CloseRequested += () => _actionLogOverlay.Close();
        _actionLogOverlay.Visible = false;

        ApplyTouchLayout();
    }

    // DESIGN.md D7's mobile fit-and-finish, in one place and behind one gate.
    //
    // EVERY LINE BELOW IS INERT ON DESKTOP, and deliberately by an early return rather than by
    // arithmetic that comes out even (Platform's header explains why that distinction is the point
    // of the whole gate). Milestone D was tuned against the desktop window, so D7's hard
    // requirement is that desktop renders identically -- with this shape, "the .tscn offsets are
    // untouched on desktop" is a property of one branch, checkable by reading it.
    //
    // APPLIED ONCE FROM _Ready, WITH NO Resized HOOK (D7c iii). The app is landscape-locked, so
    // neither the safe area nor the canvas aspect changes after startup, and a resize handler would
    // introduce runtime code mutating positions that are currently purely static -- risking the
    // pre-layout (0,0) read SideRail.Align already documents.
    private void ApplyTouchLayout()
    {
        if (!Platform.IsTouch)
        {
            return;
        }

        var layout = GetNode<MarginContainer>(LayoutPath);

        // THE VERTICAL BUDGET IS WHAT PAYS FOR THE CONTENT SCALE, and it has to be reclaimed before
        // anything else: at scale 1.12 the canvas is ~893 units tall, while the desktop margins
        // (96 top, 210 hand band) plus two 297-unit rows need ~992. Dropping both frees the ~107
        // units that make the board fit. See Platform.TouchContentScale for the full arithmetic.
        //
        // The top margin can go because what it clears -- the type-cycle chart and the settings
        // gear -- are corner-anchored overlays, not rows above the board, so the board only ever
        // needed to start below them, and at this scale it still does.
        layout.AddThemeConstantOverride("margin_top", Platform.TouchTopMargin);

        // The hand band is trimmed to what the fan actually occupies: a hand card is 243 units tall
        // and HandFan shows VisibleCardFraction (0.62) of it plus an 18-unit arc rise, so ~169
        // units are used and the desktop 210 carries ~41 units of slack that a phone cannot spare.
        _hand!.OffsetTop = -Platform.TouchHandBand;

        // D7c: the safe-area inset, fed in at the root for everything inside Layout...
        TouchLayout.InsetMargins(layout);

        // ...and pushed directly into the corner controls, which are anchored to BoardView rather
        // than to Layout and so have nothing between them and the screen edge to inherit it from.
        //
        // ONLY THE TWO RIGHT-EDGE BUTTONS. The type-cycle chart was briefly in this list and should
        // not have been: it is anchored top-LEFT, nowhere near a cut corner, and the inset pushed it
        // 30 units inboard of the hover tooltip directly below it -- two left-edge panels that read
        // as one column looked misaligned. It gets TypeChartLeft instead, matching the tooltip.
        TouchLayout.InsetCornerControls(_settingsButton!, _logButton!);

        // The chart shares the tooltip's left edge (HoverDetailPanel.tscn offset_left = 12), so the
        // two left-edge panels line up as one column. Width is preserved by moving both offsets.
        var chartWidth = _typeCycleChart!.OffsetRight - _typeCycleChart.OffsetLeft;
        _typeCycleChart.OffsetLeft = TypeChartLeft;
        _typeCycleChart.OffsetRight = TypeChartLeft + chartWidth;

        // The rail clears the corner buttons (DESIGN.md D7). Both buttons are anchored to the right
        // edge and, once inset, land at roughly x -44..-88 from it -- squarely on top of the rail's
        // top panel, which is what put the settings gear over the opponent's avatar. Shifting the
        // whole rail left by the button column's width moves it out from under both at once, and
        // the wide phone canvas has the room to give (see CardMetrics' board-fit note).
        //
        // Offsets, not Position: SideRail.Align rewrites Position.Y every time the board resizes and
        // preserves Position.X, so an offset shift survives it while a Position write would be
        // fought. Additive, so the rail keeps its authored 208 width.
        var rail = GetNode<Control>(SideRailPath);
        rail.OffsetLeft -= RailButtonClearance;
        rail.OffsetRight -= RailButtonClearance;

        // D7b: the board scales up to fill the vertical space and centres in what is actually
        // VISIBLE. Both halves matter and the second was the bug.
        //
        // Layout runs to the bottom of the canvas, but the hand band is anchored over its lower
        // ~175 units, so a shrink-centred BoardArea centres on a region far taller than the one the
        // player can see -- which put the board ~78 units too low, cramped against the top margin
        // with a dead gap above the hand. Reserving the hand band as a bottom margin makes the box
        // it centres in the box that is actually visible.
        layout.AddThemeConstantOverride("margin_bottom", Platform.TouchHandBand);

        // Horizontally the same correction, for the rail rather than the hand: reserving the rail's
        // width keeps the board centred on the free space instead of drifting right behind it.
        // Uses the shifted rail edge, so the board follows the rail left.
        layout.AddThemeConstantOverride(
            "margin_right", Mathf.RoundToInt(CardMetrics.SideRailReserve - RailButtonClearance));

        ScaleBoardToFit();
    }

    // Grows the board subtree to fill its region, preserving the 10:9 proportion (DESIGN.md D7).
    //
    // DEFERRED, because it measures. On the frame _Ready runs, BoardArea has not been laid out and
    // reports a zero rect -- the same pre-layout (0,0) read SideRail.Align documents and guards
    // against. Deferring lets the container sort first, so the region measured is the real one.
    private void ScaleBoardToFit() => CallDeferred(nameof(ApplyBoardScale));

    private void ApplyBoardScale()
    {
        var boardArea = GetNodeOrNull<Control>(BoardAreaPath);
        if (boardArea is null)
        {
            return;
        }

        var region = boardArea.GetParent<Control>().Size.Y;
        var scale = CardMetrics.BoardScale(region);

        // Scale about the top-centre of the board, so growth spreads sideways and downward from
        // where the container already placed it rather than pulling the board off its own origin.
        // A Control scales around PivotOffset, which BoardAnimator.Place documents the same trap for.
        boardArea.PivotOffset = new Vector2(boardArea.Size.X / 2f, 0f);
        boardArea.Scale = new Vector2(scale, scale);

        // The animator caches slot rects, and every one of them just moved. Re-collecting is what
        // keeps a played-card cue landing on the slot it belongs to rather than at the unscaled
        // position -- exactly the "animation renders at the wrong slot" class of bug PlayerPanel's
        // own RenderSlots note describes.
        RefreshAnimatorLayout();
    }

    // True while the match log is up -- checked separately from the menu and tutorial for the same
    // reason those two are separate: ESC closes the topmost overlay, so each needs its own answer.
    public bool IsActionLogOpen => _actionLogOverlay?.Visible ?? false;

    public void OpenActionLog() => _actionLogOverlay!.Open(ActionLogSource?.Invoke() ?? []);

    public void CloseActionLog() => _actionLogOverlay!.Close();

    // DESIGN.md D2 items 2/4: shows one action on the recap panel. Called for BOTH seats -- see
    // ActionRecap's header for why that was chosen rather than restricting it to the opponent.
    public void ShowRecap(ActionRecap recap) => _actionRecapPanel!.ShowRecap(recap);

    public void ClearRecap() => _actionRecapPanel?.Clear();

    // True while the pause/game-over overlay is up. GameRoot checks this so ESC cannot reopen a
    // menu that is already showing, and so a finished game's menu cannot be dismissed.
    public bool IsMenuOpen => _menuPanel?.Visible ?? false;

    // True while the Rules/Tutorial overlay is up. Checked separately from IsMenuOpen so GameRoot
    // can make ESC close the topmost thing first: the tutorial opens OVER the pause menu (DESIGN.md
    // 5.C-UI's "Rules" entry), so a bare IsMenuOpen check would leave ESC unable to dismiss it
    // without also punching through to the menu underneath.
    public bool IsTutorialOpen => _tutorialOverlay?.Visible ?? false;

    // True while the Sounds panel is up (DESIGN.md D4). Its own flag for the same reason
    // IsTutorialOpen is separate from IsMenuOpen: it opens OVER the pause menu, so ESC has to be
    // able to dismiss just this without punching through to the menu underneath.
    public bool IsSoundsOpen => _soundsPanel?.Visible ?? false;

    // DESIGN.md 5.C-UI: ESC opens the same panel the game-over screen uses, minus the finality --
    // a paused game keeps its Resume button, a finished one does not.
    public void OpenPauseMenu() => _menuPanel!.Open("Paused", canResume: true);

    // ESC's toggle-closed half: a second ESC press while paused dismisses the menu exactly like
    // pressing Resume would. Never called over a finished game's menu -- GameRoot only reaches
    // for this when the game is still live, so there's no case here where "close" would mean
    // discarding a game-over screen that has nothing to resume to.
    public void ClosePauseMenu() => _menuPanel!.Close();

    // ESC's other job while the Rules overlay is on top: close just the overlay, revealing the
    // pause menu it was opened over rather than falling all the way back to the board.
    public void CloseTutorial() => _tutorialOverlay!.Close();

    // ESC's equivalent for the Sounds panel (DESIGN.md D4) -- closes just this, revealing the pause
    // menu it was opened over.
    public void CloseSounds() => _soundsPanel!.Close();

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

    // Each seat's deck NAME (not the Deck itself -- this view has no business touching GameSession,
    // per DESIGN.md A2's boundary; GameRoot resolves the name once via GameSession.DeckOne/DeckTwo
    // and hands it down, the same split SetAvatars already uses). Stored rather than formatted into
    // "Player N - ..." here, so IdentityOf can reuse it against whichever PlayerId a panel is
    // showing this Render -- the panel/player mapping swaps every turn, the deck name does not.
    private readonly Dictionary<PlayerId, string> _deckNames = [];

    public void SetDeckNames(string one, string two)
    {
        _deckNames[PlayerId.One] = one;
        _deckNames[PlayerId.Two] = two;
    }

    // "Player N - Deck Name", shown between the rail panels and the End Turn button (the request's
    // own wording). 1-based to match ShowGameOver's "Player N wins!" -- the same player-facing
    // numbering everywhere else on this screen.
    private string IdentityOf(PlayerId player)
    {
        var deckName = _deckNames.TryGetValue(player, out var name) ? name : DeckBuilder.DefaultDeckName;

        // DeckBuilder.Default names the engine Deck "default" (lowercase, an id-shaped string
        // meant for logs/reports -- see Deck.Name's own header). Lobby.PopulateDeckPicker never
        // shows that literal string to a player; it hardcodes the friendlier "Default deck" label
        // for the same slot instead, so this mirrors that rather than leaking the internal name.
        var display = deckName == DeckBuilder.DefaultDeckName ? "Default deck" : deckName;
        return $"Player {player.ToIndex() + 1} - {display}";
    }

    // `viewer` is the seat this screen is drawn from -- NOT necessarily the seat whose turn it is
    // (DESIGN.md D1). This method used to compute `self = state.ActivePlayer` itself, which made the
    // board flip sides every turn; that is right for hotseat and wrong against an AI, so the
    // decision moved up to GameRoot's ViewerMode and arrives here as a parameter.
    //
    // Everything below keys off these two locals, so this substitution is the whole perspective
    // change: the panels, the rail, the avatars, the identities and the hand fan all follow.
    // `spentMoves` carries which moves to mark as already used (DESIGN.md D2 item 3). Supplied by
    // GameRoot rather than read off the creatures here, because the engine's own flag clears at the
    // owner's turn end and the marking is meant to persist through the opponent's turn -- see
    // SpentMoveTracker's header.
    public void Render(
        GameState state, CardDatabase cards, IReadOnlyList<GameAction> legalActions, PlayerId viewer,
        SpentMoveTracker spentMoves)
    {
        var self = viewer;
        var other = self.Opponent();

        // Whether the viewer may actually ACT this frame -- distinct from whose board is on the
        // bottom. Under FollowsActive these coincide (the viewer is always the active player) and
        // nothing changes; under Fixed they come apart exactly when the opponent is thinking, which
        // is the case the split exists for: the human keeps their own hand on screen, inert, rather
        // than the board turning around to show the AI's.
        var isViewersTurn = self == state.ActivePlayer;

        // Only the viewer's panel ever draws a hand, so it is the only one given the shared fan --
        // the opponent panel must not hold a reference to it, or its own (hidden-hand) render
        // would clear the cards the self panel just laid out.
        _opponentPanel!.AttachHand(null);
        _selfPanel!.AttachHand(_hand!);

        _opponentPanel.Render(
            state, cards, other, showHand: false, interactive: false, legalActions, spentMoves);
        _selfPanel.Render(
            state, cards, self, showHand: true, interactive: isViewersTurn, legalActions, spentMoves);

        // Counts/resources/health live on the right rail now (DESIGN.md 5.C-UI), not in the removed
        // top status bar. Read straight off GameState, the same way the status bar did.
        var otherState = state[other];
        var selfState = state[self];

        // A seat's health is what is left of the win condition its OPPONENT is racing toward, so
        // each panel is handed the other side's score subtracted from ScoreToWin. Derived from
        // the ruleset rather than hardcoded to 7 so a balance sweep that retunes scoreToWin can
        // not leave this reading a stale maximum.
        // The always-visible type cycle in the top-left corner. Fed from the LIVE ruleset rather
        // than drawn from a fixed cycle, so a balance sweep that retunes what beats what (or the
        // 2x multiplier) cannot leave the diagram confidently contradicting the damage code.
        // SetChart ignores a repeat of the same instance, so this costs nothing per Render.
        _typeCycleChart!.SetChart(state.Rules.TypeChart);

        var scoreToWin = state.Rules.ScoreToWin;
        _opponentSide!.Render(otherState, scoreToWin - selfState.Score);
        _selfSide!.Render(selfState, scoreToWin - otherState.Score);

        // Re-applied per Render because under FollowsActive the two panels change hands every turn
        // -- the seat that was "self" last turn is "opponent" now, so the portraits must follow
        // their players across. Under Fixed the mapping never moves and these are all no-ops:
        // setting the same texture twice is free, since PlayerBadge.Portrait ignores a write that
        // does not change the value, so this only redraws on an actual swap.
        _opponentSide.SetAvatar(AvatarOf(other));
        _selfSide.SetAvatar(AvatarOf(self));

        // Same "re-applied every Render because the panel/player mapping can swap" reasoning as
        // the avatars just above -- whichever seat is "opponent" this frame shows THAT player's
        // name, not a name fixed to the rail position.
        _opponentSide.SetIdentity(IdentityOf(other));
        _selfSide.SetIdentity(IdentityOf(self));

        // RenderSlots rebuilds every SlotView from scratch, which would silently drop
        // targeting highlights applied by BeginTargeting -- reapply them here so a Render
        // triggered mid-targeting (e.g. to refresh the turn label) doesn't blank the board.
        if (_pendingTargetActions is { } pending)
        {
            var targetable = SlotsFor(pending);
            _opponentPanel.SetTargetable(targetable);
            _selfPanel.SetTargetable(targetable);
        }

        // The standalone turn label is gone (DESIGN.md 5.C-UI): whose turn it is already reads off
        // the End Turn button's enabled state and the rail's two panels, so the button carries
        // the turn NUMBER -- the one piece that had nowhere else to live -- and takes over the
        // label's job of announcing a discard/targeting prompt.
        //
        // Under a Fixed viewer the button also has to say whose turn it is OUTRIGHT (DESIGN.md D1).
        // The old design could leave that implicit because the board itself flipped -- the fact
        // that you were looking at your own hand meant it was your turn. Once the view stops
        // moving, a disabled End Turn button is the only thing distinguishing "your turn" from
        // "theirs", which is too subtle to carry the distinction alone.
        var prompt = state.AwaitingDiscard
            ? $"Discard {state.PendingDiscards}"
            : IsTargeting
                ? "Choose a target"
                : $"End Turn {state.TurnNumber}";

        _endTurnButton!.Text = isViewersTurn ? prompt : $"Opponent's turn {state.TurnNumber}";

        // Gated on the viewer's turn as well as legality: `legalActions` is always the ACTIVE
        // player's list (GameSession.LegalActions has no seat parameter), so on the opponent's
        // turn it still contains their EndTurnAction -- without this check the button would sit
        // enabled and let the viewer end a turn that isn't theirs.
        _endTurnButton.Disabled = !isViewersTurn || !legalActions.OfType<EndTurnAction>().Any();

        // The menu is NOT hidden here. It used to be -- the old game-over panel was re-hidden on
        // every Render and re-shown by GameRoot afterwards -- but the pause menu can be open over
        // a live game, and an AI turn resolving behind it triggers a Render that would dismiss it
        // mid-look. Closing is now driven only by Resume, or by leaving the scene entirely.
        RefreshAnimatorLayout();
    }

    // Slot rects for BoardAnimator (DESIGN.md B1d). Deferred by one frame on purpose: RenderSlots
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

    // DESIGN.md D5: a network match's peer connection dropped. Reuses the same modal ShowGameOver
    // does rather than a bespoke dialog -- the player's only real options are identical (go back
    // to the lobby, or quit), and there is nothing to resume to: an interrupted remote match has
    // no local save (GameRoot skips MatchSaveStore for network games) and no peer left to replay
    // actions against, so canResume is false for the same reason it is on a finished game.
    public void ShowDisconnected() => _menuPanel!.Open("Opponent disconnected.", canResume: false);

    // A move or spell needing a chosen target (single-target rule, DESIGN.md A5) highlights the
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
