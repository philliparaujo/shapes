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
    [Export] public NodePath PlayerOneKindPath { get; set; } = "Layout/PlayerOne/KindPicker";
    [Export] public NodePath PlayerOneDifficultyPath { get; set; } = "Layout/PlayerOne/DifficultyPicker";
    [Export] public NodePath PlayerOneDeckPath { get; set; } = "Layout/PlayerOne/DeckPicker";
    [Export] public NodePath PlayerTwoKindPath { get; set; } = "Layout/PlayerTwo/KindPicker";
    [Export] public NodePath PlayerTwoDifficultyPath { get; set; } = "Layout/PlayerTwo/DifficultyPicker";
    [Export] public NodePath PlayerTwoDeckPath { get; set; } = "Layout/PlayerTwo/DeckPicker";
    [Export] public NodePath StartButtonPath { get; set; } = "Layout/StartButton";
    [Export] public NodePath ResumeButtonPath { get; set; } = "Layout/ResumeButton";
    [Export] public NodePath CardBrowserButtonPath { get; set; } = "Layout/CardBrowserButton";
    [Export] public NodePath DeckbuilderButtonPath { get; set; } = "Layout/DeckbuilderButton";
    [Export] public NodePath ErrorLabelPath { get; set; } = "Layout/ErrorLabel";
    [Export] public NodePath ExitButtonPath { get; set; } = "Layout/ExitButton";
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
    private Button? _cardBrowserButton;
    private Button? _deckbuilderButton;
    private Label? _errorLabel;
    private Button? _exitButton;

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
        _playerOneKind = GetNode<OptionButton>(PlayerOneKindPath);
        _playerOneDifficulty = GetNode<OptionButton>(PlayerOneDifficultyPath);
        _playerOneDeck = GetNode<OptionButton>(PlayerOneDeckPath);
        _playerTwoKind = GetNode<OptionButton>(PlayerTwoKindPath);
        _playerTwoDifficulty = GetNode<OptionButton>(PlayerTwoDifficultyPath);
        _playerTwoDeck = GetNode<OptionButton>(PlayerTwoDeckPath);
        _startButton = GetNode<Button>(StartButtonPath);
        _resumeButton = GetNode<Button>(ResumeButtonPath);
        _cardBrowserButton = GetNode<Button>(CardBrowserButtonPath);
        _deckbuilderButton = GetNode<Button>(DeckbuilderButtonPath);
        _errorLabel = GetNode<Label>(ErrorLabelPath);
        _exitButton = GetNode<Button>(ExitButtonPath);

        PopulateKindPicker(_playerOneKind);
        PopulateKindPicker(_playerTwoKind);
        PopulateDifficultyPicker(_playerOneDifficulty);
        PopulateDifficultyPicker(_playerTwoDifficulty);

        // Re-read every time the lobby is shown, not cached across scenes: returning here from
        // the deckbuilder is the single most likely moment for the slots to have changed, and a
        // dropdown still listing the decks as they were before that edit is exactly the stale
        // read the Resume button's own re-check exists to avoid.
        _cards = LoadCards();
        _decks = DeckStore.Load();
        PopulateDeckPicker(_playerOneDeck);
        PopulateDeckPicker(_playerTwoDeck);
        _errorLabel.Visible = false;

        // Default to the common case: player one human, player two a mid-strength AI -- the
        // "start a game against the computer" path needs zero clicks beyond Start.
        _playerTwoKind.Selected = Array.IndexOf(KindOrder, AgentKind.IsMcts);

        _playerOneKind.ItemSelected += _ => UpdateDifficultyVisibility();
        _playerTwoKind.ItemSelected += _ => UpdateDifficultyVisibility();
        UpdateDifficultyVisibility();

        // PLAN.md C6: only offered when a save actually exists -- re-checked every time the
        // lobby is shown (not just once at process start) so returning here after a game ends
        // normally (which clears the save) hides the button again rather than leaving a stale
        // "Resume" that would fail to load.
        _resumeButton.Visible = MatchSaveStore.Exists();

        _startButton.Pressed += OnStartPressed;
        _resumeButton.Pressed += OnResumePressed;
        _cardBrowserButton.Pressed += () => GetTree().ChangeSceneToFile(CardBrowserScenePath);
        _deckbuilderButton.Pressed += () => GetTree().ChangeSceneToFile(DeckbuilderScenePath);

        // Quitting from the lobby leaves any save alone, so an interrupted match is still there
        // on the next launch -- same reasoning as OnBackToLobbyRequested in GameRoot: only
        // game-over clears the save, walking away never does.
        _exitButton.Pressed += () => GetTree().Quit();
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
