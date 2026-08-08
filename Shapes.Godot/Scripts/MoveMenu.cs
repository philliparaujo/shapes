using System;
using System.Collections.Generic;
using Godot;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The tap-triggered menu for "what can this creature do right now": its usable moves plus
// any adjacent merge it can perform, positioned at the tapped slot. Replaces what a desktop
// game might show as a hover context menu -- opened only by a tap, per A3.
public partial class MoveMenu : Control
{
    public event Action<int>? MoveChosen;
    public event Action<SlotIndex>? MergeChosen;
    public event Action? Cancelled;

    [Export] public NodePath ListPath { get; set; } = "Panel/Margin/Layout/List";
    [Export] public NodePath CancelButtonPath { get; set; } = "Panel/Margin/Layout/CancelButton";

    private VBoxContainer? _list;
    private Button? _cancelButton;

    public override void _Ready()
    {
        _list = GetNode<VBoxContainer>(ListPath);
        _cancelButton = GetNode<Button>(CancelButtonPath);
        _cancelButton.Pressed += () => { Close(); Cancelled?.Invoke(); };
        Visible = false;
    }

    public void Open(Vector2 globalPosition, IReadOnlyList<(int Index, MoveText Text)> moves, IReadOnlyList<SlotIndex> mergeTargets)
    {
        foreach (var child in _list!.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var (index, text) in moves)
        {
            var button = new Button
            {
                Text = $"{text.Name} [{text.Cost}] — {text.Effects}",
                CustomMinimumSize = new Vector2(0, 44),
            };
            button.Pressed += () => { MoveChosen?.Invoke(index); Close(); };
            _list.AddChild(button);
        }

        foreach (var target in mergeTargets)
        {
            var button = new Button
            {
                Text = $"Merge into {target}",
                CustomMinimumSize = new Vector2(0, 44),
            };
            button.Pressed += () => { MergeChosen?.Invoke(target); Close(); };
            _list.AddChild(button);
        }

        GlobalPosition = globalPosition;
        Visible = true;
    }

    // Closes the menu without notifying listeners. BoardView.ClearSelection calls this (and
    // is itself Cancelled's handler) -- if Close fired Cancelled unconditionally, that would
    // recurse forever: ClearSelection -> Close -> Cancelled -> ClearSelection -> ...
    public void Close()
    {
        Visible = false;
    }
}
