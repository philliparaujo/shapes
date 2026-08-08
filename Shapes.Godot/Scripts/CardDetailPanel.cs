using System;
using Godot;
using Shapes.Core.Cards;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The tap target for "what does this card do" -- A3's replacement for a desktop tooltip.
// Shows full EffectText-synthesized rules text (A4) and, when the tapped card is legally
// playable right now, a Play button that fires the same submission GameRoot would otherwise
// wire from a direct hand tap.
public partial class CardDetailPanel : Control
{
    public event Action? Closed;

    [Export] public NodePath NameLabelPath { get; set; } = "Panel/Layout/NameLabel";
    [Export] public NodePath CostLabelPath { get; set; } = "Panel/Layout/CostLabel";
    [Export] public NodePath StatLabelPath { get; set; } = "Panel/Layout/StatLabel";
    [Export] public NodePath EffectsLabelPath { get; set; } = "Panel/Layout/EffectsLabel";
    [Export] public NodePath MoveListPath { get; set; } = "Panel/Layout/MoveList";
    [Export] public NodePath PlayButtonPath { get; set; } = "Panel/Layout/PlayButton";
    [Export] public NodePath CloseButtonPath { get; set; } = "Panel/Layout/CloseButton";

    private Label? _nameLabel;
    private Label? _costLabel;
    private Label? _statLabel;
    private Label? _effectsLabel;
    private VBoxContainer? _moveList;
    private Button? _playButton;
    private Button? _closeButton;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>(NameLabelPath);
        _costLabel = GetNode<Label>(CostLabelPath);
        _statLabel = GetNode<Label>(StatLabelPath);
        _effectsLabel = GetNode<Label>(EffectsLabelPath);
        _moveList = GetNode<VBoxContainer>(MoveListPath);
        _playButton = GetNode<Button>(PlayButtonPath);
        _closeButton = GetNode<Button>(CloseButtonPath);

        _closeButton.Pressed += Hide;
        Visible = false;
    }

    public void ShowCard(CardDefinition card, bool canPlay, Action onPlay)
    {
        var text = CardText.Of(card);

        _nameLabel!.Text = text.Name;
        _costLabel!.Text = text.Cost;
        _statLabel!.Text = text.IsCreature ? $"{text.Health} HP  {text.TypeIcons}" : "Spell";
        _effectsLabel!.Text = text.SpellEffects;
        _effectsLabel.Visible = text.SpellEffects.Length > 0;

        foreach (var child in _moveList!.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var move in text.Moves)
        {
            var label = new Label
            {
                Text = $"{move.Name} [{move.Cost}]: {move.Effects}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _moveList.AddChild(label);
        }

        _playButton!.Visible = canPlay;
        _playButton.Disabled = !canPlay;

        if (_lastPlayHandler is not null)
        {
            _playButton.Pressed -= _lastPlayHandler;
        }

        // Dismiss WITHOUT firing Closed: onPlay() may only have started placement (a creature
        // needing a board slot), not finished the play, and Closed means "the user backed out
        // -- discard any in-progress selection." Firing it here would immediately wipe the
        // BoardView.PendingPlacementCardId that onPlay() just set, in the same call stack, so
        // the very next slot tap would find nothing pending. Firing Closed unconditionally
        // once did exactly that.
        _lastPlayHandler = () => { onPlay(); Visible = false; };
        _playButton.Pressed += _lastPlayHandler;

        Visible = true;
    }

    private Action? _lastPlayHandler;

    public new void Hide()
    {
        Visible = false;
        Closed?.Invoke();
    }
}
