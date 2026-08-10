using System;
using Godot;
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
    [Export] public NodePath PlayerTwoKindPath { get; set; } = "Layout/PlayerTwo/KindPicker";
    [Export] public NodePath PlayerTwoDifficultyPath { get; set; } = "Layout/PlayerTwo/DifficultyPicker";
    [Export] public NodePath StartButtonPath { get; set; } = "Layout/StartButton";
    [Export] public NodePath CardBrowserButtonPath { get; set; } = "Layout/CardBrowserButton";
    [Export] public string GameScenePath { get; set; } = "res://Scenes/GameRoot.tscn";
    [Export] public string CardBrowserScenePath { get; set; } = "res://Scenes/CardBrowser.tscn";

    // Index-aligned with the OptionButton items added in _Ready -- see PopulateKindPicker.
    private static readonly AgentKind[] KindOrder =
    [
        AgentKind.Human, AgentKind.Random, AgentKind.Greedy, AgentKind.IsMcts, AgentKind.IsMctsHeuristic,
    ];

    private OptionButton? _playerOneKind;
    private OptionButton? _playerOneDifficulty;
    private OptionButton? _playerTwoKind;
    private OptionButton? _playerTwoDifficulty;
    private Button? _startButton;
    private Button? _cardBrowserButton;

    public override void _Ready()
    {
        _playerOneKind = GetNode<OptionButton>(PlayerOneKindPath);
        _playerOneDifficulty = GetNode<OptionButton>(PlayerOneDifficultyPath);
        _playerTwoKind = GetNode<OptionButton>(PlayerTwoKindPath);
        _playerTwoDifficulty = GetNode<OptionButton>(PlayerTwoDifficultyPath);
        _startButton = GetNode<Button>(StartButtonPath);
        _cardBrowserButton = GetNode<Button>(CardBrowserButtonPath);

        PopulateKindPicker(_playerOneKind);
        PopulateKindPicker(_playerTwoKind);
        PopulateDifficultyPicker(_playerOneDifficulty);
        PopulateDifficultyPicker(_playerTwoDifficulty);

        // Default to the common case: player one human, player two a mid-strength AI -- the
        // "start a game against the computer" path needs zero clicks beyond Start.
        _playerTwoKind.Selected = Array.IndexOf(KindOrder, AgentKind.IsMcts);

        _playerOneKind.ItemSelected += _ => UpdateDifficultyVisibility();
        _playerTwoKind.ItemSelected += _ => UpdateDifficultyVisibility();
        UpdateDifficultyVisibility();

        _startButton.Pressed += OnStartPressed;
        _cardBrowserButton.Pressed += () => GetTree().ChangeSceneToFile(CardBrowserScenePath);
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

        PendingMatch.Config = new MatchConfig(playerOne, playerTwo, seed);
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
