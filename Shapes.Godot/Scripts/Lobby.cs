using System;
using System.IO;
using Godot;
using Shapes.Core.Cards;
using Shapes.Core.Rules;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Match setup, shown before GameRoot (PLAN.md C1, with C5's AI-opponent wiring pulled forward
// rather than shipping a seat picker that silently does nothing when an AI kind is chosen).
// Each seat is independently Human or one of the console's four agent kinds -- 0/2 human
// players (AI v AI) and 1 human player (the common case) are both just two independent
// pickers, not a separate mode switch.
public partial class Lobby : Control
{
    [Export] public NodePath PlayerOneKindPath { get; set; } = "Play/PlayerOne/KindPicker";
    [Export] public NodePath PlayerOneDifficultyPath { get; set; } = "Play/PlayerOne/DifficultyPicker";
    [Export] public NodePath PlayerOneDeckPath { get; set; } = "Play/PlayerOne/DeckPicker";
    [Export] public NodePath PlayerTwoKindPath { get; set; } = "Play/PlayerTwo/KindPicker";
    [Export] public NodePath PlayerTwoDifficultyPath { get; set; } = "Play/PlayerTwo/DifficultyPicker";
    [Export] public NodePath PlayerTwoDeckPath { get; set; } = "Play/PlayerTwo/DeckPicker";
    [Export] public NodePath StartButtonPath { get; set; } = "Play/StartButton";
    [Export] public NodePath ResumeButtonPath { get; set; } = "Play/ResumeButton";
    [Export] public NodePath ErrorLabelPath { get; set; } = "Play/ErrorLabel";
    [Export] public NodePath PlayBackButtonPath { get; set; } = "Play/PlayFooter/PlayBackButton";
    [Export] public NodePath PlayDeckbuilderButtonPath { get; set; } = "Play/PlayFooter/PlayDeckbuilderButton";

    [Export] public NodePath HomePath { get; set; } = "Home";
    [Export] public NodePath PlayPath { get; set; } = "Play";
    [Export] public NodePath PlayButtonPath { get; set; } = "Home/PlayButton";
    [Export] public NodePath DeckbuilderButtonPath { get; set; } = "Home/DeckbuildingButton";
    [Export] public NodePath RulesButtonPath { get; set; } = "Home/RulesButton";
    [Export] public NodePath ExitButtonPath { get; set; } = "Home/ExitButton";
    [Export] public NodePath TutorialOverlayPath { get; set; } = "TutorialOverlay";
    [Export] public string GameScenePath { get; set; } = "res://Scenes/GameRoot.tscn";
    [Export] public string CardBrowserScenePath { get; set; } = "res://Scenes/CardBrowser.tscn";
    [Export] public string DeckbuilderScenePath { get; set; } = "res://Scenes/Deckbuilder.tscn";

    // Index-aligned with the OptionButton items added in _Ready -- see PopulateKindPicker.
    private static readonly AgentKind[] KindOrder =
    [
        AgentKind.Human, AgentKind.Random, AgentKind.Greedy, AgentKind.IsMcts, AgentKind.IsMctsHeuristic,
    ];

    private OptionButton? _playerOneKind;
    private OptionButton? _playerOneDifficulty;
    private OptionButton? _playerOneDeck;
    private OptionButton? _playerTwoKind;
    private OptionButton? _playerTwoDifficulty;
    private OptionButton? _playerTwoDeck;
    private Button? _startButton;
    private Button? _resumeButton;
    private Button? _deckbuilderButton;
    private Button? _rulesButton;
    private Label? _errorLabel;
    private Button? _exitButton;
    private TutorialOverlay? _tutorialOverlay;

    // The two panels this scene switches between (PLAN.md D3 phase 3). One scene rather than two,
    // because everything the Play panel needs -- the loaded CardDatabase, the deck slots, the
    // PendingMatch handoff and the deck-legality check -- is already owned here, and splitting it
    // into its own scene would mean either duplicating that or inventing a way to share it.
    private Control? _home;
    private Control? _play;
    private Button? _playButton;
    private Button? _playBackButton;
    private Button? _playDeckbuilderButton;

    // The deck slots the two dropdowns offer, loaded once here so both pickers list the same
    // decks in the same order and a selected index means the same slot in each.
    private DeckSlots _decks = DeckSlots.Empty();
    private CardDatabase? _cards;

    // Item 0 in both deck pickers is the default deck (one of every card), not a slot -- so a
    // player who has never opened the deckbuilder still gets a working game, which is what the
    // lobby did before decks existed. Slot N is therefore at item index N+1.
    private const int DefaultDeckItemIndex = 0;

    public override void _Ready()
    {
        // PLAN.md D3 phase 1. The whole subtree inherits, so this one call is what stops the lobby
        // rendering in Godot's stock theme -- it carried six theme_overrides, all of them spacing
        // and font size, and not one colour or panel between them.
        UiTheme.ApplyTo(this);

        _playerOneKind = GetNode<OptionButton>(PlayerOneKindPath);
        _playerOneDifficulty = GetNode<OptionButton>(PlayerOneDifficultyPath);
        _playerOneDeck = GetNode<OptionButton>(PlayerOneDeckPath);
        _playerTwoKind = GetNode<OptionButton>(PlayerTwoKindPath);
        _playerTwoDifficulty = GetNode<OptionButton>(PlayerTwoDifficultyPath);
        _playerTwoDeck = GetNode<OptionButton>(PlayerTwoDeckPath);
        _startButton = GetNode<Button>(StartButtonPath);
        _resumeButton = GetNode<Button>(ResumeButtonPath);
        _deckbuilderButton = GetNode<Button>(DeckbuilderButtonPath);
        _rulesButton = GetNode<Button>(RulesButtonPath);
        _errorLabel = GetNode<Label>(ErrorLabelPath);
        _exitButton = GetNode<Button>(ExitButtonPath);
        _tutorialOverlay = GetNode<TutorialOverlay>(TutorialOverlayPath);

        _home = GetNode<Control>(HomePath);
        _play = GetNode<Control>(PlayPath);
        _playButton = GetNode<Button>(PlayButtonPath);
        _playBackButton = GetNode<Button>(PlayBackButtonPath);
        _playDeckbuilderButton = GetNode<Button>(PlayDeckbuilderButtonPath);

        PopulateKindPicker(_playerOneKind);
        PopulateKindPicker(_playerTwoKind);
        PopulateDifficultyPicker(_playerOneDifficulty);
        PopulateDifficultyPicker(_playerTwoDifficulty);

        // The card set is loaded once here; the DECK SLOTS are re-read on every entry to the Play
        // panel instead (see ShowPlay), since those are what an excursion to the deckbuilder
        // changes.
        _cards = LoadCards();

        // Default to the common case: player one human, player two a mid-strength AI -- the
        // "start a game against the computer" path needs zero clicks beyond Start.
        _playerTwoKind.Selected = Array.IndexOf(KindOrder, AgentKind.IsMcts);

        _playerOneKind.ItemSelected += _ => UpdateDifficultyVisibility();
        _playerTwoKind.ItemSelected += _ => UpdateDifficultyVisibility();
        UpdateDifficultyVisibility();

        _startButton.Pressed += OnStartPressed;
        _resumeButton.Pressed += OnResumePressed;
        _deckbuilderButton.Pressed += () => GetTree().ChangeSceneToFile(DeckbuilderScenePath);

        // HOME -> PLAY is a panel swap, not a scene change (PLAN.md D3 phase 3): the two share this
        // scene's loaded cards and deck slots, and a scene change would reload both to show the
        // same data. It also keeps Back instant, which matters because backing out of match setup
        // is a common, low-commitment action.
        _playButton!.Pressed += () => ShowPlay(true);
        _playBackButton!.Pressed += () => ShowPlay(false);

        // Reached from inside match setup, where "these decks are wrong, let me fix one" is the
        // natural next thought -- so the deckbuilder is one click away rather than back-then-out.
        _playDeckbuilderButton!.Pressed += () => GetTree().ChangeSceneToFile(DeckbuilderScenePath);

        // Quitting from the lobby leaves any save alone, so an interrupted match is still there
        // on the next launch -- same reasoning as OnBackToLobbyRequested in GameRoot: only
        // game-over clears the save, walking away never does.
        _exitButton.Pressed += () => GetTree().Quit();

        // PLAN.md C7: the rules page is reachable from the lobby as well as the in-game pause
        // menu (BoardView.OpenPauseMenu), so a player can read the rules before ever starting a
        // match, not only when stuck mid-game. Same overlay scene both places instantiate --
        // there is exactly one Rules page, not a lobby copy and a board copy that could drift.
        _rulesButton.Pressed += _tutorialOverlay!.Open;
        _tutorialOverlay.CloseRequested += _tutorialOverlay.Close;

        ShowPlay(false);
    }

    // Swaps between the home menu and match setup.
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
            _errorLabel!.Visible = false;

            // Re-checked here, not just at _Ready: a game finished since this scene loaded would
            // otherwise leave a Resume button that fails to load. Same reasoning as C6's own check.
            _resumeButton!.Visible = MatchSaveStore.Exists();
        }

        _home!.Visible = !playing;
        _play!.Visible = playing;
    }

    // ESC backs out one level: the rules overlay first if it is open, then match setup to the home
    // menu. Topmost-first, the same ordering GameRoot's own handler uses for its three overlays.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
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

    private static void PopulateKindPicker(OptionButton picker)
    {
        picker.Clear();
        picker.AddItem("Human");
        picker.AddItem("Random");
        picker.AddItem("Greedy");
        picker.AddItem("IS-MCTS");
        picker.AddItem("IS-MCTS (heuristic)");
    }

    // The default deck first, then every non-empty slot, labelled with its size so an illegal
    // deck is visible as a choice rather than only as an error after pressing Start. Empty slots
    // are omitted entirely -- there is nothing to play -- so this list is usually short.
    private void PopulateDeckPicker(OptionButton picker)
    {
        picker.Clear();
        picker.AddItem("Default deck");

        for (var i = 0; i < DeckSlots.SlotCount; i++)
        {
            var deck = _decks.Slots[i];
            if (deck.IsEmpty)
            {
                continue;
            }

            var name = deck.Name.Length > 0 ? deck.Name : $"Deck {i + 1}";

            // The slot index travels as item METADATA rather than as the item's position,
            // because empty slots are skipped: item 3 is not slot 3, and reconstructing the
            // mapping by counting would break the moment a slot in the middle is emptied.
            picker.AddItem($"{name} ({deck.TotalCards})");
            picker.SetItemMetadata(picker.ItemCount - 1, i);
        }

        picker.Selected = DefaultDeckItemIndex;
    }

    private static CardDatabase LoadCards()
    {
        var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
        return CardLoader.FromDirectory(cardsDir);
    }

    private static void PopulateDifficultyPicker(OptionButton picker)
    {
        picker.Clear();
        foreach (var iterations in MatchConfig.DifficultyPresets)
        {
            picker.AddItem($"{iterations} iterations");
        }

        // Middle preset by default -- matches SearchBudget.Default's own "visibly better than
        // one iteration, still fast enough to feel interactive" balance.
        picker.Selected = MatchConfig.DifficultyPresets.Length / 2;
    }

    // The difficulty picker only means anything for the two IS-MCTS kinds -- Random/Greedy have
    // no search to budget, same as the console's BuildAgent never reading --iterations for them.
    private void UpdateDifficultyVisibility()
    {
        _playerOneDifficulty!.Visible = IsSearchBased(KindOrder[_playerOneKind!.Selected]);
        _playerTwoDifficulty!.Visible = IsSearchBased(KindOrder[_playerTwoKind!.Selected]);
    }

    private static bool IsSearchBased(AgentKind kind) =>
        kind is AgentKind.IsMcts or AgentKind.IsMctsHeuristic;

    private void OnStartPressed()
    {
        var playerOne = ReadSeat(_playerOneKind!, _playerOneDifficulty!);
        var playerTwo = ReadSeat(_playerTwoKind!, _playerTwoDifficulty!);
        var seed = (ulong)DateTime.UtcNow.Ticks;

        // WHERE DECK LEGALITY IS ENFORCED (PLAN.md C2). The deckbuilder deliberately lets a
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

    // The Deck this picker's selection plays, or null for the default deck -- the same
    // null-means-default convention MatchConfig and GameSession.Start both use.
    private Deck? ReadDeck(OptionButton picker)
    {
        if (picker.Selected <= DefaultDeckItemIndex)
        {
            return null;
        }

        // Slot index comes from item metadata, not item position -- see PopulateDeckPicker.
        var slotIndex = (int)picker.GetItemMetadata(picker.Selected);
        return _decks.Slots[slotIndex].ToDeck(_cards!, RuleSet.Default);
    }

    // PLAN.md C6: hands GameRoot nothing but the "resume, not fresh" signal -- GameRoot itself
    // owns loading MatchSaveStore and rebuilding via GameSession.Resume, the same "Lobby decides
    // WHAT to play, GameRoot owns HOW" split OnStartPressed already follows for a new match.
    private void OnResumePressed()
    {
        PendingMatch.Config = null;
        PendingMatch.ResumeRequested = true;
        GetTree().ChangeSceneToFile(GameScenePath);
    }

    private static SeatConfig ReadSeat(OptionButton kindPicker, OptionButton difficultyPicker)
    {
        var kind = KindOrder[kindPicker.Selected];
        return kind == AgentKind.Human
            ? SeatConfig.Human
            : new SeatConfig(kind, MatchConfig.DifficultyPresets[difficultyPicker.Selected]);
    }
}
