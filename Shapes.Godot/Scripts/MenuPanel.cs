using System;
using Godot;

namespace Shapes.Godot.Scripts;

// The modal overlay shown at game over and on ESC (PLAN.md 5.C-UI).
//
// One scene serving both cases rather than two near-identical panels: they differ only in their
// heading and in whether the game underneath is still playable, which is a parameter, not a
// reason to duplicate the layout, the styling and the two buttons.
//
// Raises events instead of changing scenes itself. GameRoot owns the save file and the scene
// transition (see its header on C6's action-log persistence), so it decides what leaving means;
// this only reports which button was pressed -- the same "report, don't decide" split every
// other view in this project uses.
public partial class MenuPanel : Control
{
    public event Action? BackToLobbyRequested;
    public event Action? ExitRequested;
    public event Action? ResumeRequested;

    [Export] public NodePath TitleLabelPath { get; set; } = "Backdrop/Panel/Margin/Layout/TitleLabel";
    [Export] public NodePath ResumeButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/ResumeButton";
    [Export] public NodePath LobbyButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/LobbyButton";
    [Export] public NodePath ExitButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/ExitButton";

    private Label? _title;
    private Button? _resumeButton;
    private Button? _lobbyButton;
    private Button? _exitButton;

    public override void _Ready()
    {
        _title = GetNode<Label>(TitleLabelPath);
        _resumeButton = GetNode<Button>(ResumeButtonPath);
        _lobbyButton = GetNode<Button>(LobbyButtonPath);
        _exitButton = GetNode<Button>(ExitButtonPath);

        _resumeButton.Pressed += () => ResumeRequested?.Invoke();
        _lobbyButton.Pressed += () => BackToLobbyRequested?.Invoke();
        _exitButton.Pressed += () => ExitRequested?.Invoke();

        Visible = false;
    }

    // canResume separates the two cases: a paused game can be returned to, a finished one cannot.
    public void Open(string title, bool canResume)
    {
        _title!.Text = title;
        _resumeButton!.Visible = canResume;
        Visible = true;

        // Focus the first ACTIONABLE button so keyboard/gamepad has somewhere to land, and so a
        // second ESC press has a sensible default. Deferred because a control cannot take focus
        // in the same frame it becomes visible.
        var first = canResume ? _resumeButton : _lobbyButton!;
        first.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => Visible = false;
}
