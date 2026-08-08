using System;
using Godot;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// One board slot: empty, or a creature summary (name, health, type icons, merge/keyword
// marks). The button itself IS the hit target -- sized to the >=44px minimum in the .tscn's
// custom_minimum_size, satisfying A3's touch-target floor without a separate overlay.
public partial class SlotView : Control
{
    public event Action? Tapped;

    [Export] public NodePath ButtonPath { get; set; } = "Button";
    [Export] public NodePath NameLabelPath { get; set; } = "Button/Layout/NameLabel";
    [Export] public NodePath HealthLabelPath { get; set; } = "Button/Layout/HealthLabel";
    [Export] public NodePath TypeLabelPath { get; set; } = "Button/Layout/TypeLabel";

    private Button? _button;
    private Label? _nameLabel;
    private Label? _healthLabel;
    private Label? _typeLabel;

    public override void _Ready()
    {
        _button = GetNode<Button>(ButtonPath);
        _nameLabel = GetNode<Label>(NameLabelPath);
        _healthLabel = GetNode<Label>(HealthLabelPath);
        _typeLabel = GetNode<Label>(TypeLabelPath);
        _button.Pressed += () => Tapped?.Invoke();
    }

    public void Render(SlotIndex slot, CreatureInstance? creature, CardDatabase cards, bool isTappable)
    {
        if (creature is null)
        {
            _nameLabel!.Text = "—";
            _healthLabel!.Text = string.Empty;
            _typeLabel!.Text = string.Empty;
            _button!.Disabled = false; // empty slots stay tappable for merge/placement targeting
            _button.TooltipText = string.Empty;
            return;
        }

        var name = cards.TryGet(creature.CardId, out var card) ? card!.Name : creature.CardId;
        _nameLabel!.Text = creature.IsMerged ? $"{name}+" : name;
        _healthLabel!.Text = $"{creature.Health}/{creature.MaxHealth}";
        _typeLabel!.Text = ResourceIcons.Describe(creature.Types);
        _button!.Disabled = false;
    }

    public void SetHighlighted(bool highlighted)
    {
        Modulate = highlighted ? new Color(1f, 0.85f, 0.4f) : Colors.White;
    }
}
