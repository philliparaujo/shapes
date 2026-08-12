using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// TEMPORARY -- scaffolding for the 5.C-UI visual check, deleted once the screenshots are taken.
// Boots straight into a match (bypassing the Lobby) with seat one HUMAN, so the turn stops there
// with a real hand to fan, and screenshots it. Takes two shots: the opening hand, and a later one
// after several End Turn presses have grown it, which is the case the fan's overlap and the
// per-card height consistency actually need checking against.
public partial class UiShotHarness : Control
{
    private const int FramesPerTurn = 40;
    private const int TurnsToEnd = 6;

    private int _frame;
    private int _turnsEnded;
    private bool _openingShotTaken;
    private Button? _endTurnButton;

    public override void _Ready()
    {
        PendingMatch.Config = new MatchConfig(SeatConfig.Human, new SeatConfig(AgentKind.Greedy, 0), 12345UL);
        PendingMatch.ResumeRequested = false;

        var scene = GD.Load<PackedScene>("res://Scenes/GameRoot.tscn");
        AddChild(scene.Instantiate<GameRoot>());
    }

    public override void _Process(double delta)
    {
        _frame++;

        if (_frame == 30 && !_openingShotTaken)
        {
            Shoot("ui-shot-open.png");
            _openingShotTaken = true;
            return;
        }

        // Presses the real End Turn button, so this drives the actual turn loop rather than
        // needing a test-only hook. Skipped while disabled (an AI turn, or a pending discard).
        if (_openingShotTaken && _turnsEnded < TurnsToEnd && _frame % FramesPerTurn == 0)
        {
            _endTurnButton ??= FindEndTurnButton(GetTree().Root);
            if (_endTurnButton is { Disabled: false })
            {
                _endTurnButton.EmitSignal(BaseButton.SignalName.Pressed);
                _turnsEnded++;
            }

            return;
        }

        // Force the tooltip open on a real card so the shot covers all three card surfaces at
        // once -- hand, in play, and hover -- which is the whole point of sharing CardStyle.
        if (_turnsEnded >= TurnsToEnd && _frame % FramesPerTurn == 10)
        {
            ShowATooltip();
            return;
        }

        if (_turnsEnded >= TurnsToEnd && _frame % FramesPerTurn == 20)
        {
            Shoot("ui-shot-full.png");
            GetTree().Quit();
        }
    }

    private void ShowATooltip()
    {
        var fan = FindByType<HandFan>(GetTree().Root);
        var panel = FindByType<HoverDetailPanel>(GetTree().Root);
        if (fan is null || panel is null || fan.GetChildCount() == 0)
        {
            return;
        }

        // Raise the same event a real mouse-enter raises, so nothing here bypasses the normal path.
        if (fan.GetChild(fan.GetChildCount() - 1) is CardFace card)
        {
            card.EmitSignal(BaseButton.SignalName.MouseEntered);
        }
    }

    private static T? FindByType<T>(Node node) where T : Node
    {
        if (node is T match)
        {
            return match;
        }

        foreach (var child in node.GetChildren())
        {
            if (FindByType<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void Shoot(string fileName)
    {
        var image = GetViewport().GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath($"user://{fileName}");
        image.SavePng(path);
        GD.Print($"UiShotHarness wrote {path}");
    }

    // Walks by type/name: FindChild respects scene ownership, which misses a node instantiated at
    // runtime from another scene.
    private static Button? FindEndTurnButton(Node node)
    {
        if (node is Button button && button.Name == "EndTurnButton")
        {
            return button;
        }

        foreach (var child in node.GetChildren())
        {
            if (FindEndTurnButton(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
