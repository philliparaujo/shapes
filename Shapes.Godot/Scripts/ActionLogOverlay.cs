using System;
using System.Collections.Generic;
using Godot;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// DESIGN.md D2 item 5: the full match log, as a scrollable overlay.
//
// Same dimmed-backdrop-plus-centered-panel pattern MenuPanel and TutorialOverlay already use,
// rather than a third visual language for a modal -- and raises CloseRequested instead of closing
// itself, because BoardView owns which overlay is on top and what ESC dismisses ("report, don't
// decide", the split every view in this project follows).
//
// SEPARATE FROM THE RECAP PANEL, not an expansion of it. The recap auto-fades, so making it the
// click target would mean the affordance vanishes exactly when a player wants it -- "I missed
// that, what happened?" is asked AFTER the thing has gone. They also serve different needs: the
// recap is glanceable and transient, this is deliberate and scrollable.
//
// Renders ActionLogEntry values built in the adapter (ActionLog), so everything about WHAT a line
// says is testable outside the editor and this file only decides how it looks.
public partial class ActionLogOverlay : Control
{
    public event Action? CloseRequested;

    [Export] public NodePath TitleLabelPath { get; set; } = "Backdrop/Panel/Margin/Layout/Header/TitleLabel";
    [Export] public NodePath CloseButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/Header/CloseButton";
    [Export] public NodePath EntryListPath { get; set; } = "Backdrop/Panel/Margin/Layout/BodyScroll/EntryList";
    [Export] public NodePath BodyScrollPath { get; set; } = "Backdrop/Panel/Margin/Layout/BodyScroll";

    private Label? _title;
    private Button? _closeButton;
    private VBoxContainer? _entryList;
    private ScrollContainer? _bodyScroll;

    public override void _Ready()
    {
        _title = GetNode<Label>(TitleLabelPath);
        _closeButton = GetNode<Button>(CloseButtonPath);
        _entryList = GetNode<VBoxContainer>(EntryListPath);
        _bodyScroll = GetNode<ScrollContainer>(BodyScrollPath);

        _closeButton.Pressed += () => CloseRequested?.Invoke();
        Visible = false;
    }

    // Rebuilt on open rather than kept in sync per action: the overlay is closed for almost the
    // whole match, so maintaining a live node per entry would pay the cost of every line while
    // nobody is looking at it. Reading the list once, on demand, is the cheaper shape.
    public void Open(IReadOnlyList<ActionLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var child in _entryList!.GetChildren())
        {
            child.QueueFree();
        }

        if (entries.Count == 0)
        {
            _entryList.AddChild(MutedLabel("Nothing has happened yet."));
        }

        var lastTurn = -1;
        foreach (var entry in entries)
        {
            // One heading per turn, so the log reads as turns containing actions rather than as a
            // flat wall -- "what happened last turn" is the question it is usually opened for.
            if (entry.TurnNumber != lastTurn)
            {
                _entryList.AddChild(TurnHeading(entry.TurnNumber));
                lastTurn = entry.TurnNumber;
            }

            _entryList.AddChild(EntryBlock(entry));
        }

        Visible = true;
        _closeButton!.CallDeferred(Control.MethodName.GrabFocus);

        // Deferred twice over: the entries were only just added, so neither their own heights nor
        // the scroll container's content range are settled until Godot's layout pass has run.
        CallDeferred(nameof(ScrollToBottom));
    }

    public void Close() => Visible = false;

    // Opens on the most recent action, not the first. The log grows all match and the interesting
    // end is the one just added -- scrolling from turn 1 every time would make it useless by the
    // midgame.
    private void ScrollToBottom()
    {
        if (_bodyScroll is null)
        {
            return;
        }

        _bodyScroll.ScrollVertical = (int)_bodyScroll.GetVScrollBar().MaxValue;
    }

    private static Label TurnHeading(int turn)
    {
        var label = new Label { Text = $"Turn {turn}" };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", TurnHeadingColor);
        return label;
    }

    // One action and its effects as an indented block: the action reads as the cause and its
    // effects as consequences underneath, which is the relationship the log is recording.
    private static Control EntryBlock(ActionLogEntry entry)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);

        // "Player 1 plays Snowball into P2's left slot" -- one sentence, rather than a "P1" tag
        // prefixed to a fragment. The seat is already carried by colour; spelling it out here is
        // what lets the rest of the line be ordinary prose instead of a log format.
        var isTurnStart = entry.Description == ActionLog.TurnStartDescription;
        var action = new Label
        {
            Text = isTurnStart
                ? $"  {SeatName(entry.Player)}'s turn begins"
                : $"  {SeatName(entry.Player)} {entry.Description}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        action.AddThemeFontSizeOverride("font_size", 14);

        // Coloured per seat, so scanning for "what did the opponent do" is a colour scan rather
        // than a read of every line. A turn opening is not something a player DID, so it renders
        // in the muted effect colour rather than claiming a seat's action colour.
        action.AddThemeColorOverride(
            "font_color",
            isTurnStart ? EffectColor : entry.Player == PlayerId.One ? SeatOneColor : SeatTwoColor);
        column.AddChild(action);

        foreach (var effect in entry.Effects)
        {
            var line = new Label
            {
                Text = $"        {effect}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            line.AddThemeFontSizeOverride("font_size", 12);
            line.AddThemeColorOverride("font_color", EffectColor);
            column.AddChild(line);
        }

        return column;
    }

    private static Label MutedLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", EffectColor);
        return label;
    }

    private static string SeatName(PlayerId player) => $"Player {player.ToIndex() + 1}";

    private static readonly Color TurnHeadingColor = new(0.94f, 0.86f, 0.55f);
    private static readonly Color SeatOneColor = new(0.62f, 0.82f, 0.98f);
    private static readonly Color SeatTwoColor = new(0.98f, 0.72f, 0.62f);
    private static readonly Color EffectColor = new(0.72f, 0.78f, 0.86f);
}
