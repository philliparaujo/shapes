using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Match setup, shown before GameRoot (DESIGN.md C1, with C5's AI-opponent wiring pulled forward
// rather than shipping a seat picker that silently does nothing when an AI kind is chosen).
// Each seat is independently Human or one of three CPU difficulty tiers -- 0/2 human players
// (AI v AI) and 1 human player (the common case) are both just two independent pickers, not a
// separate mode switch.
public partial class Lobby : Control
{
    [Export] public NodePath PlayerOneKindPath { get; set; } = "Play/LocalPanel/PlayerOne/Row/KindPicker";
    [Export] public NodePath PlayerOneDeckPath { get; set; } = "Play/LocalPanel/PlayerOne/Row/DeckPicker";
    [Export] public NodePath PlayerTwoKindPath { get; set; } = "Play/LocalPanel/PlayerTwo/Row/KindPicker";
    [Export] public NodePath PlayerTwoDeckPath { get; set; } = "Play/LocalPanel/PlayerTwo/Row/DeckPicker";
    [Export] public NodePath StartButtonPath { get; set; } = "Play/LocalPanel/StartButton";
    [Export] public NodePath ResumeButtonPath { get; set; } = "Play/LocalPanel/ResumeButton";
    [Export] public NodePath ErrorLabelPath { get; set; } = "Play/LocalPanel/ErrorLabel";
    [Export] public NodePath PlayBackButtonPath { get; set; } = "Play/PlayFooter/PlayBackButton";
    [Export] public NodePath PlayDeckbuilderButtonPath { get; set; } = "Play/PlayFooter/PlayDeckbuilderButton";

    [Export] public NodePath HomePath { get; set; } = "Home";
    [Export] public NodePath PlayPath { get; set; } = "Play";
    [Export] public NodePath PlayButtonPath { get; set; } = "Home/PlayButton";
    [Export] public NodePath DeckbuilderButtonPath { get; set; } = "Home/DeckbuildingButton";
    [Export] public NodePath RulesButtonPath { get; set; } = "Home/RulesSoundsRow/RulesButton";
    [Export] public NodePath SoundsButtonPath { get; set; } = "Home/RulesSoundsRow/SoundsButton";
    [Export] public NodePath ExitButtonPath { get; set; } = "Home/ExitButton";
    [Export] public NodePath TutorialOverlayPath { get; set; } = "TutorialOverlay";
    [Export] public NodePath SoundsPanelPath { get; set; } = "SoundsPanel";
    [Export] public string GameScenePath { get; set; } = "res://Scenes/GameRoot.tscn";
    [Export] public string CardBrowserScenePath { get; set; } = "res://Scenes/CardBrowser.tscn";
    [Export] public string DeckbuilderScenePath { get; set; } = "res://Scenes/Deckbuilder.tscn";

    // DESIGN.md D5: Online panel (Host/Join). RelayUrl is an [Export] rather than a const for the
    // same reason MoveDelaySeconds is (GameRoot's own note) -- a deployment-shaped value someone
    // will want to change without a rebuild, here "which relay to dial" instead of "how fast to
    // watch." Points at the always-on Oracle Cloud relay (Shapes.Relay running as a systemd
    // service on the VM); override to ws://localhost:5080/ws for same-machine testing.
    [Export] public string RelayUrl { get; set; } = "ws://192.9.143.181:5080/ws";

    // Local/Online is now a tab pair INSIDE Play rather than Play/Play Online being two separate
    // Home entry points -- there is only one "start a match" screen, and Local vs Online is a
    // question about who the opponent is, not a different destination. Same Host/Join sub-tab
    // pattern nested one level in.
    [Export] public NodePath LocalTabButtonPath { get; set; } = "Play/ModeTabs/LocalTabButton";
    [Export] public NodePath OnlineTabButtonPath { get; set; } = "Play/ModeTabs/OnlineTabButton";
    [Export] public NodePath LocalPanelPath { get; set; } = "Play/LocalPanel";
    [Export] public NodePath OnlinePanelPath { get; set; } = "Play/OnlinePanel";
    [Export] public NodePath HostTabButtonPath { get; set; } = "Play/OnlinePanel/HostJoinTabs/HostTabButton";
    [Export] public NodePath JoinTabButtonPath { get; set; } = "Play/OnlinePanel/HostJoinTabs/JoinTabButton";
    [Export] public NodePath HostPanelPath { get; set; } = "Play/OnlinePanel/HostPanel";
    [Export] public NodePath JoinPanelPath { get; set; } = "Play/OnlinePanel/JoinPanel";
    [Export] public NodePath OnlineSeatPickerPath { get; set; } =
        "Play/OnlinePanel/HostPanel/Row/SeatColumn/SeatPicker";
    [Export] public NodePath OnlineHostDeckPickerPath { get; set; } =
        "Play/OnlinePanel/HostPanel/Row/DeckColumn/DeckPicker";
    [Export] public NodePath HostButtonPath { get; set; } = "Play/OnlinePanel/HostPanel/HostButton";
    [Export] public NodePath CodeLabelPath { get; set; } = "Play/OnlinePanel/HostPanel/CodeLabel";
    [Export] public NodePath CodeEntryPath { get; set; } = "Play/OnlinePanel/JoinPanel/CodeEntry";
    [Export] public NodePath OnlineJoinDeckPickerPath { get; set; } = "Play/OnlinePanel/JoinPanel/DeckPicker";
    [Export] public NodePath JoinButtonPath { get; set; } = "Play/OnlinePanel/JoinPanel/JoinButton";
    [Export] public NodePath OnlineStatusLabelPath { get; set; } = "Play/OnlinePanel/StatusLabel";
    [Export] public NodePath OnlineErrorLabelPath { get; set; } = "Play/OnlinePanel/ErrorLabel";

    // Index-aligned with the OptionButton items added in _Ready -- see PopulateKindPicker. One
    // dropdown per seat rather than a kind picker plus a separate difficulty picker: the lobby's
    // difficulty knob only ever needs to answer "how strong an opponent," and Easy/Medium/Hard
    // says that directly where "IS-MCTS, 5000 iterations" makes a player choose a number instead.
    // All three tiers are the same IS-MCTS agent at MatchConfig.DifficultyPresets' three budgets --
    // Random/Greedy/the heuristic playout variant exist for the console and the sim's own
    // comparisons, not as a lobby-facing difficulty choice.
    private static readonly (AgentKind Kind, int Iterations)[] KindOrder =
    [
        (AgentKind.Human, 0),
        (AgentKind.IsMcts, MatchConfig.DifficultyPresets[0]),
        (AgentKind.IsMcts, MatchConfig.DifficultyPresets[1]),
        (AgentKind.IsMcts, MatchConfig.DifficultyPresets[2]),
    ];

    private OptionButton? _playerOneKind;
    private OptionButton? _playerOneDeck;
    private OptionButton? _playerTwoKind;
    private OptionButton? _playerTwoDeck;
    private Button? _startButton;
    private Button? _resumeButton;
    private Button? _deckbuilderButton;
    private Button? _rulesButton;
    private Button? _soundsButton;
    private Label? _errorLabel;
    private Button? _exitButton;
    private TutorialOverlay? _tutorialOverlay;
    private SoundsPanel? _soundsPanel;

    // The two panels this scene switches between (DESIGN.md D3 phase 3). One scene rather than two,
    // because everything the Play panel needs -- the loaded CardDatabase, the deck slots, the
    // PendingMatch handoff and the deck-legality check -- is already owned here, and splitting it
    // into its own scene would mean either duplicating that or inventing a way to share it.
    private Control? _home;
    private Control? _play;
    private Button? _playButton;
    private Button? _playBackButton;
    private Button? _playDeckbuilderButton;

    // Local/Online tabs INSIDE Play (see the [Export] block's own note) -- the same toggle-button
    // pair pattern Host/Join already used, just one level higher.
    private Button? _localTabButton;
    private Button? _onlineTabButton;
    private Control? _localPanel;
    private Control? _onlinePanel;

    // The deck slots the two dropdowns offer, loaded once here so both pickers list the same
    // decks in the same order and a selected index means the same slot in each.
    private DeckSlots _decks = DeckSlots.Empty();
    private CardDatabase? _cards;

    // DESIGN.md D5: Online panel (Host/Join), now nested inside Play (see LocalTabButtonPath's note)
    // rather than a third top-level panel alongside Home/Play.
    private Button? _hostTabButton;
    private Button? _joinTabButton;
    private Control? _hostPanel;
    private Control? _joinPanel;
    private OptionButton? _onlineSeatPicker;
    private OptionButton? _onlineHostDeck;
    private Button? _hostButton;
    private Label? _codeLabel;
    private LineEdit? _codeEntry;
    private OptionButton? _onlineJoinDeck;
    private Button? _joinButton;
    private Label? _onlineStatusLabel;
    private Label? _onlineErrorLabel;

    // "Player 1" / "Player 2" -- index-aligned with the seat picker's items, same convention as
    // KindOrder above. Only meaningful when hosting: the joiner's seat is whatever the host
    // didn't take, delivered over the wire in MatchStart rather than chosen locally.
    private static readonly PlayerId[] PlayerChoiceOrder = [PlayerId.One, PlayerId.Two];

    // The relay connection this screen currently owns, from HostButton/JoinButton press until
    // either the handshake completes (control moves to GameRoot, which takes ownership from here)
    // or the player cancels/an error occurs (disposed here). Null whenever the Online panel isn't
    // mid-handshake -- including while it's simply visible and idle, since nothing is opened until
    // a button is actually pressed.
    private RelayMatchTransport? _pendingTransport;
    private CancellationTokenSource? _pendingCts;

    public override void _Ready()
    {
        // DESIGN.md D3 phase 1. The whole subtree inherits, so this one call is what stops the lobby
        // rendering in Godot's stock theme -- it carried six theme_overrides, all of them spacing
        // and font size, and not one colour or panel between them.
        // DESIGN.md D7: content scale, applied before anything measures itself. Idempotent and
        // no-op on desktop -- see Platform.ApplyContentScale.
        Platform.ApplyContentScale(this);

        UiTheme.ApplyTo(this);

        _playerOneKind = GetNode<OptionButton>(PlayerOneKindPath);
        _playerOneDeck = GetNode<OptionButton>(PlayerOneDeckPath);
        _playerTwoKind = GetNode<OptionButton>(PlayerTwoKindPath);
        _playerTwoDeck = GetNode<OptionButton>(PlayerTwoDeckPath);
        _startButton = GetNode<Button>(StartButtonPath);
        _resumeButton = GetNode<Button>(ResumeButtonPath);
        _deckbuilderButton = GetNode<Button>(DeckbuilderButtonPath);
        _rulesButton = GetNode<Button>(RulesButtonPath);
        _soundsButton = GetNode<Button>(SoundsButtonPath);
        _errorLabel = GetNode<Label>(ErrorLabelPath);
        _exitButton = GetNode<Button>(ExitButtonPath);
        _tutorialOverlay = GetNode<TutorialOverlay>(TutorialOverlayPath);
        _soundsPanel = GetNode<SoundsPanel>(SoundsPanelPath);

        _home = GetNode<Control>(HomePath);
        _play = GetNode<Control>(PlayPath);
        _playButton = GetNode<Button>(PlayButtonPath);
        _playBackButton = GetNode<Button>(PlayBackButtonPath);
        _playDeckbuilderButton = GetNode<Button>(PlayDeckbuilderButtonPath);

        _localTabButton = GetNode<Button>(LocalTabButtonPath);
        _onlineTabButton = GetNode<Button>(OnlineTabButtonPath);
        _localPanel = GetNode<Control>(LocalPanelPath);
        _onlinePanel = GetNode<Control>(OnlinePanelPath);

        _hostTabButton = GetNode<Button>(HostTabButtonPath);
        _joinTabButton = GetNode<Button>(JoinTabButtonPath);
        _hostPanel = GetNode<Control>(HostPanelPath);
        _joinPanel = GetNode<Control>(JoinPanelPath);
        _onlineSeatPicker = GetNode<OptionButton>(OnlineSeatPickerPath);
        _onlineHostDeck = GetNode<OptionButton>(OnlineHostDeckPickerPath);
        _hostButton = GetNode<Button>(HostButtonPath);
        _codeLabel = GetNode<Label>(CodeLabelPath);
        _codeEntry = GetNode<LineEdit>(CodeEntryPath);
        _onlineJoinDeck = GetNode<OptionButton>(OnlineJoinDeckPickerPath);
        _joinButton = GetNode<Button>(JoinButtonPath);
        _onlineStatusLabel = GetNode<Label>(OnlineStatusLabelPath);
        _onlineErrorLabel = GetNode<Label>(OnlineErrorLabelPath);

        PopulateKindPicker(_playerOneKind);
        PopulateKindPicker(_playerTwoKind);
        PopulateSeatPicker(_onlineSeatPicker);

        // The card set is loaded once here; the DECK SLOTS are re-read on every entry to the Play
        // panel instead (see ShowPlay), since those are what an excursion to the deckbuilder
        // changes.
        _cards = LoadCards();

        // Default to the common case: player one human, player two a mid-strength AI -- the
        // "start a game against the computer" path needs zero clicks beyond Start.
        _playerTwoKind.Selected = Array.FindIndex(KindOrder, k => k.Kind == AgentKind.IsMcts);

        _startButton.Pressed += OnStartPressed;
        _resumeButton.Pressed += OnResumePressed;
        _deckbuilderButton.Pressed += () => GetTree().ChangeSceneToFile(DeckbuilderScenePath);

        // HOME -> PLAY is a panel swap, not a scene change (DESIGN.md D3 phase 3): the two share this
        // scene's loaded cards and deck slots, and a scene change would reload both to show the
        // same data. It also keeps Back instant, which matters because backing out of match setup
        // is a common, low-commitment action.
        _playButton!.Pressed += () => ShowPlay(true);
        _playBackButton!.Pressed += () => ShowPlay(false);

        // Reached from inside match setup, where "these decks are wrong, let me fix one" is the
        // natural next thought -- so the deckbuilder is one click away rather than back-then-out.
        _playDeckbuilderButton!.Pressed += () => GetTree().ChangeSceneToFile(DeckbuilderScenePath);

        // Local/Online is a tab pair inside Play now (see LocalTabButtonPath's note), the same
        // toggle-button pattern Host/Join already used one level in.
        _localTabButton!.Pressed += () => ShowPlayMode(online: false);
        _onlineTabButton!.Pressed += () => ShowPlayMode(online: true);
        _hostTabButton!.Pressed += () => ShowOnlineTab(hosting: true);
        _joinTabButton!.Pressed += () => ShowOnlineTab(hosting: false);
        _hostButton!.Pressed += OnHostPressed;
        _joinButton!.Pressed += OnJoinPressed;

        // Quitting from the lobby leaves any save alone, so an interrupted match is still there
        // on the next launch -- same reasoning as OnBackToLobbyRequested in GameRoot: only
        // game-over clears the save, walking away never does.
        _exitButton.Pressed += () => GetTree().Quit();

        // DESIGN.md C7: the rules page is reachable from the lobby as well as the in-game pause
        // menu (BoardView.OpenPauseMenu), so a player can read the rules before ever starting a
        // match, not only when stuck mid-game. Same overlay scene both places instantiate --
        // there is exactly one Rules page, not a lobby copy and a board copy that could drift.
        _rulesButton.Pressed += _tutorialOverlay!.Open;
        _tutorialOverlay.CloseRequested += _tutorialOverlay.Close;

        // DESIGN.md D4: the Sounds panel sits directly below Rules on the home screen and is the
        // same single panel scene the in-game pause menu opens (BoardView), for the same reason
        // C7 gives for the rules overlay -- there is exactly one Sounds page, not a lobby copy and
        // a board copy that could drift.
        _soundsButton.Pressed += _soundsPanel.Open;
        _soundsPanel.CloseRequested += _soundsPanel.Close;

        ShowPlay(false);
    }

    // Swaps between the home menu and match setup. Local and Online (DESIGN.md D5) are both inside
    // Play now (see LocalTabButtonPath's note on the [Export] block), so entering Play populates
    // every deck picker across both tabs and always lands back on Local -- Online is a deliberate
    // choice made fresh each visit, not a sticky mode that survives a trip back to Home.
    //
    // The deck pickers are repopulated on the way IN rather than only in _Ready, for the reason
    // _Ready's own note gives: the deckbuilder is the most likely thing to have changed the slots,
    // and here that edit can happen without this scene ever being rebuilt (Play -> Edit Decks ->
    // Back lands on Home, and the next Play must not list the decks as they were).
    private void ShowPlay(bool playing)
    {
        if (playing)
        {
            _decks = DeckStore.Load();
            PopulateDeckPicker(_playerOneDeck!);
            PopulateDeckPicker(_playerTwoDeck!);
            PopulateDeckPicker(_onlineHostDeck!);
            PopulateDeckPicker(_onlineJoinDeck!);
            _errorLabel!.Visible = false;
            ResetOnlineStatus();
            ShowPlayMode(online: false);
            ShowOnlineTab(hosting: true);

            // Re-checked here, not just at _Ready: a game finished since this scene loaded would
            // otherwise leave a Resume button that fails to load. Same reasoning as C6's own check.
            _resumeButton!.Visible = MatchSaveStore.Exists();
        }
        else
        {
            // Backing out abandons any in-flight host/join -- there is no "keep waiting in the
            // background" mode, so the transport this screen was holding must be torn down rather
            // than leaked (an open socket plus its receive loop, per RelayMatchTransport's own
            // DisposeAsync). A no-op if Local was the active tab; CancelPendingAsync itself is a
            // no-op when nothing is pending.
            _ = CancelPendingAsync();
        }

        _home!.Visible = !playing;
        _play!.Visible = playing;
    }

    // Local <-> Online, the tab pair inside Play (DESIGN.md D5, folded in from the old top-level
    // Online panel -- see LocalTabButtonPath's note). Switching away from Online abandons any
    // in-flight host/join for the same reason leaving Play entirely does.
    private void ShowPlayMode(bool online)
    {
        if (!online)
        {
            _ = CancelPendingAsync();
        }

        _localTabButton!.ButtonPressed = !online;
        _onlineTabButton!.ButtonPressed = online;
        _localPanel!.Visible = !online;
        _onlinePanel!.Visible = online;
    }

    private void ShowOnlineTab(bool hosting)
    {
        _hostTabButton!.ButtonPressed = hosting;
        _joinTabButton!.ButtonPressed = !hosting;
        _hostPanel!.Visible = hosting;
        _joinPanel!.Visible = !hosting;
    }

    // ESC backs out one level: the rules overlay first if it is open, then match setup to the
    // home menu. Topmost-first, the same ordering GameRoot's own handler uses for its overlays.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            return;
        }

        // DESIGN.md D4. Tested before the tutorial for the same topmost-first reason the tutorial is
        // tested before the panels: the two never open together from here, so the relative order of
        // these two is arbitrary, but both must precede the panel swaps below or ESC would back out
        // of the whole screen while a modal is still up.
        if (_soundsPanel is { Visible: true })
        {
            _soundsPanel.Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_tutorialOverlay is { Visible: true })
        {
            _tutorialOverlay.Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        // On Home there is nothing left to back out to, so ESC is simply a no-op rather than
        // quitting -- exiting is a deliberate button press, never a stray keystroke.
        if (_play is not { Visible: true })
        {
            return;
        }

        ShowPlay(false);
        GetViewport().SetInputAsHandled();
    }

    // Torn down whenever this screen stops owning an in-flight transport for a reason other than
    // "handed off to GameRoot" -- backing out, a scene change, or the node itself going away.
    // Godot's own _ExitTree can't be async-awaited meaningfully (the node is gone by the time
    // DisposeAsync would complete), so this is deliberately fire-and-forget from every caller;
    // the socket still closes, just not necessarily before the next frame.
    private async Task CancelPendingAsync()
    {
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = null;

        if (_pendingTransport is { } transport)
        {
            _pendingTransport = null;
            await transport.DisposeAsync();
        }
    }

    private static void PopulateKindPicker(OptionButton picker)
    {
        picker.Clear();
        picker.AddItem("Human");
        picker.AddItem("Easy CPU");
        picker.AddItem("Medium CPU");
        picker.AddItem("Hard CPU");
    }

    // Only COMPLETE decks (DeckSize/DeckSize cards) -- a game cannot start on anything less, so
    // an incomplete slot or the >40-card default deck showing up here would just be a choice that
    // fails at Start with an error. No size suffix either: every entry is complete by
    // construction, so "(40)" on every row would say nothing a player doesn't already know from
    // being able to pick it at all.
    private void PopulateDeckPicker(OptionButton picker)
    {
        picker.Clear();
        var deckSize = DeckBuilder.DeckSizeOf(RuleSet.Default);

        for (var i = 0; i < DeckSlots.SlotCount; i++)
        {
            var deck = _decks.Slots[i];
            if (deck.TotalCards != deckSize)
            {
                continue;
            }

            var name = deck.Name.Length > 0 ? deck.Name : $"Deck {i + 1}";

            // The slot index travels as item METADATA rather than as the item's position,
            // because incomplete slots are skipped: item 3 is not slot 3, and reconstructing the
            // mapping by counting would break the moment a slot's completeness changes.
            picker.AddItem(name);
            picker.SetItemMetadata(picker.ItemCount - 1, i);
        }

        picker.Selected = picker.ItemCount > 0 ? 0 : -1;
    }

    private static CardDatabase LoadCards() => ContentLoader.LoadCards();

    private static void PopulateSeatPicker(OptionButton picker)
    {
        picker.Clear();
        picker.AddItem("Player 1");
        picker.AddItem("Player 2");
        picker.Selected = 0;
    }

    private void OnStartPressed()
    {
        var playerOne = ReadSeat(_playerOneKind!);
        var playerTwo = ReadSeat(_playerTwoKind!);
        var seed = (ulong)DateTime.UtcNow.Ticks;

        // WHERE DECK LEGALITY IS ENFORCED (DESIGN.md C2). The deckbuilder deliberately lets a
        // partial deck be saved -- losing an in-progress deck to an interruption is worse than
        // carrying an illegal one on disk -- so this is the point at which a decklist has to be
        // a real 40. ReadDeck routes through SavedDeck.ToDeck -> DeckBuilder.Custom, the same
        // validation the sim's --deck-file path applies, and reports the exception's own message
        // rather than a generic one: "has 37 cards; ruleset requires exactly 40" already says
        // precisely what to go fix.
        Deck? deckOne;
        Deck? deckTwo;
        try
        {
            deckOne = ReadDeck(_playerOneDeck!);
            deckTwo = ReadDeck(_playerTwoDeck!);
        }
        catch (DeckBuildException ex)
        {
            _errorLabel!.Text = ex.Message;
            _errorLabel.Visible = true;
            return;
        }

        _errorLabel!.Visible = false;

        // Starting fresh instead of resuming doesn't clear the old save outright, but the first
        // action of THIS game overwrites it (MatchSaveStore is a single slot, not a per-match
        // list -- see its own header) -- an interrupted game is only actually lost once a new
        // one is both started and played, not merely started.
        PendingMatch.Config = new MatchConfig(playerOne, playerTwo, seed, deckOne, deckTwo);
        GetTree().ChangeSceneToFile(GameScenePath);
    }

    // The Deck this picker's selection plays. PopulateDeckPicker only ever lists complete decks
    // (see its own note), so the one failure mode left is the picker being empty -- no slot has
    // been finished yet -- which is reported the same way an illegal deck used to be, via
    // DeckBuildException, so OnStartPressed's existing catch handles it without a second path.
    private Deck ReadDeck(OptionButton picker)
    {
        if (picker.Selected < 0)
        {
            throw new DeckBuildException(
                "Build a complete 40-card deck in the deckbuilder before starting a match.");
        }

        // Slot index comes from item metadata, not item position -- see PopulateDeckPicker.
        var slotIndex = (int)picker.GetItemMetadata(picker.Selected);
        return _decks.Slots[slotIndex].ToDeck(_cards!, RuleSet.Default);
    }

    // DESIGN.md C6: hands GameRoot nothing but the "resume, not fresh" signal -- GameRoot itself
    // owns loading MatchSaveStore and rebuilding via GameSession.Resume, the same "Lobby decides
    // WHAT to play, GameRoot owns HOW" split OnStartPressed already follows for a new match.
    private void OnResumePressed()
    {
        PendingMatch.Config = null;
        PendingMatch.ResumeRequested = true;
        GetTree().ChangeSceneToFile(GameScenePath);
    }

    private static SeatConfig ReadSeat(OptionButton kindPicker)
    {
        var (kind, iterations) = KindOrder[kindPicker.Selected];
        return kind == AgentKind.Human ? SeatConfig.Human : new SeatConfig(kind, iterations);
    }

    private void ResetOnlineStatus()
    {
        _onlineErrorLabel!.Visible = false;
        _onlineStatusLabel!.Visible = false;
        _codeLabel!.Visible = false;
        _codeLabel.Text = "";
        SetOnlineBusy(false);
    }

    // Disables the two action buttons and both deck/seat pickers for the duration of a
    // host/join attempt -- a second press mid-handshake would open a second socket this screen
    // has no slot to track (_pendingTransport is a single field), and a picker changed after
    // Host/Join was pressed would silently stop matching what was already sent.
    private void SetOnlineBusy(bool busy)
    {
        _hostButton!.Disabled = busy;
        _joinButton!.Disabled = busy;
        _onlineSeatPicker!.Disabled = busy;
        _onlineHostDeck!.Disabled = busy;
        _codeEntry!.Editable = !busy;
        _onlineJoinDeck!.Disabled = busy;
    }

    private void ShowOnlineError(string message)
    {
        _onlineStatusLabel!.Visible = false;
        _onlineErrorLabel!.Text = message;
        _onlineErrorLabel.Visible = true;
        SetOnlineBusy(false);
    }

    private void ShowOnlineStatus(string message)
    {
        _onlineErrorLabel!.Visible = false;
        _onlineStatusLabel!.Text = message;
        _onlineStatusLabel.Visible = true;
    }

    // DESIGN.md D5: opens a relay connection, asks to host, and once a peer joins sends MatchStart
    // (this process is the seed/seat authority -- see RelayMatchTransport's own header) before
    // handing off to GameRoot exactly like OnStartPressed does for a local match. async void is
    // the same shape GameRoot's own event-loop entry points use (RunAiTurns) -- a Button.Pressed
    // handler has no caller to await it.
    private async void OnHostPressed()
    {
        Deck? hostDeck;
        try
        {
            hostDeck = ReadDeck(_onlineHostDeck!);
        }
        catch (DeckBuildException ex)
        {
            ShowOnlineError(ex.Message);
            return;
        }

        ResetOnlineStatus();
        SetOnlineBusy(true);
        ShowOnlineStatus("Connecting...");

        _pendingCts = new CancellationTokenSource();
        var ct = _pendingCts.Token;

        RelayMatchTransport transport;
        try
        {
            transport = await RelayMatchTransport.HostAsync(new Uri(RelayUrl), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowOnlineError($"Could not reach the relay: {ex.Message}");
            return;
        }

        if (ct.IsCancellationRequested)
        {
            await transport.DisposeAsync();
            return;
        }

        _pendingTransport = transport;
        _codeLabel!.Text = transport.Code;
        _codeLabel.Visible = true;
        ShowOnlineStatus("Waiting for opponent...");

        bool joined;
        try
        {
            joined = await transport.Joined.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        if (!joined)
        {
            _pendingTransport = null;
            ShowOnlineError("The connection closed before anyone joined.");
            return;
        }

        var hostSeat = PlayerChoiceOrder[_onlineSeatPicker!.Selected];
        var joinerSeat = hostSeat.Opponent();
        var seed = (ulong)DateTime.UtcNow.Ticks;

        var deckOneList = hostSeat == PlayerId.One && hostDeck is not null ? SavedDeckList.Of(hostDeck).ToDto() : null;
        var deckTwoList = hostSeat == PlayerId.Two && hostDeck is not null ? SavedDeckList.Of(hostDeck).ToDto() : null;

        // Only the HOST's own deck is known here -- MatchStart's DeckOne/DeckTwo carries whichever
        // slot the host landed in and leaves the other null. The joiner never reads its own deck
        // off MatchStart (see OnJoinPressed): it already knows its own choice locally, the same
        // way two local hotseat players each only ever choose their own deck.
        try
        {
            await transport.SendMatchStartAsync(seed, joinerSeat, deckOneList, deckTwoList, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowOnlineError($"Could not reach your opponent: {ex.Message}");
            return;
        }

        var deckOne = hostSeat == PlayerId.One ? hostDeck : null;
        var deckTwo = hostSeat == PlayerId.Two ? hostDeck : null;

        PendingMatch.Transport = transport;
        _pendingTransport = null;
        PendingMatch.Config = new MatchConfig(
            SeatConfig.Human, SeatConfig.Human, seed, deckOne, deckTwo, ViewerOverride: hostSeat);
        GetTree().ChangeSceneToFile(GameScenePath);
    }

    // DESIGN.md D5: opens a relay connection, asks to join a given code, then waits for the host's
    // MatchStart before handing off to GameRoot. Mirrors OnHostPressed's shape; the joiner is
    // never the seed/seat authority (RelayMatchTransport's own header explains why: exactly one
    // side has to be, and the host -- the one who picked "Player 1"/"Player 2" -- is it).
    private async void OnJoinPressed()
    {
        var code = _codeEntry!.Text?.Trim() ?? "";
        if (code.Length == 0)
        {
            ShowOnlineError("Enter the code your host gave you.");
            return;
        }

        Deck? joinDeck;
        try
        {
            joinDeck = ReadDeck(_onlineJoinDeck!);
        }
        catch (DeckBuildException ex)
        {
            ShowOnlineError(ex.Message);
            return;
        }

        ResetOnlineStatus();
        SetOnlineBusy(true);
        ShowOnlineStatus("Connecting...");

        _pendingCts = new CancellationTokenSource();
        var ct = _pendingCts.Token;

        RelayMatchTransport transport;
        try
        {
            transport = await RelayMatchTransport.JoinAsync(new Uri(RelayUrl), code, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowOnlineError($"Could not join '{code}'. Check the code and try again.");
            return;
        }

        if (ct.IsCancellationRequested)
        {
            await transport.DisposeAsync();
            return;
        }

        _pendingTransport = transport;
        ShowOnlineStatus("Waiting for your host to start the match...");

        RelayEnvelope matchStart;
        try
        {
            matchStart = await transport.WaitForMatchStartAsync().WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        var yourSeat = (PlayerId)(matchStart.YourSeat ?? (int)PlayerId.Two);
        var deckOne = yourSeat == PlayerId.One
            ? joinDeck
            : matchStart.DeckOne is { } one ? SavedDeckList.FromDto(one)?.ToDeck() : null;
        var deckTwo = yourSeat == PlayerId.Two
            ? joinDeck
            : matchStart.DeckTwo is { } two ? SavedDeckList.FromDto(two)?.ToDeck() : null;

        PendingMatch.Transport = transport;
        _pendingTransport = null;
        PendingMatch.Config = new MatchConfig(
            SeatConfig.Human, SeatConfig.Human, matchStart.Seed ?? 0, deckOne, deckTwo,
            ViewerOverride: yourSeat);
        GetTree().ChangeSceneToFile(GameScenePath);
    }
}
