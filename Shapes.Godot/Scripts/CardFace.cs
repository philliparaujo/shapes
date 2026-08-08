using System;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// A hand card's face: name, cost, and (for creatures) health -- enough to recognize the card
// at a glance. Full rules text lives in CardDetailPanel, reached by tapping this (A3's "no
// hover-dependent information": nothing here is available only on hover).
public partial class CardFace : Control
{
    public event Action? Tapped;

    [Export] public NodePath ButtonPath { get; set; } = "Button";
    [Export] public NodePath NameLabelPath { get; set; } = "Button/Layout/NameLabel";
    [Export] public NodePath CostLabelPath { get; set; } = "Button/Layout/CostLabel";
    [Export] public NodePath StatLabelPath { get; set; } = "Button/Layout/StatLabel";

    private Button? _button;
    private Label? _nameLabel;
    private Label? _costLabel;
    private Label? _statLabel;

    public override void _Ready()
    {
        _button = GetNode<Button>(ButtonPath);
        _nameLabel = GetNode<Label>(NameLabelPath);
        _costLabel = GetNode<Label>(CostLabelPath);
        _statLabel = GetNode<Label>(StatLabelPath);
        _button.Pressed += () => Tapped?.Invoke();
    }

    public void Render(CardText text, bool isActionable)
    {
        _nameLabel!.Text = text.Name;
        _costLabel!.Text = text.Cost;
        _statLabel!.Text = text.IsCreature ? $"{text.Health} HP  {text.TypeIcons}" : "Spell";
        Modulate = isActionable ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
    }
}
