using Godot;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Scripts;

// End-of-game overlay. Reads GameState.Winner via GameRoot rather than deriving anything
// itself -- IsOver/Winner are already resolved by GameState, so this only formats the result.
public partial class GameOverPanel : Control
{
    [Export] public NodePath LabelPath { get; set; } = "Panel/Label";

    private Label? _label;

    public override void _Ready()
    {
        _label = GetNode<Label>(LabelPath);
        Visible = false;
    }

    public void Show(PlayerId? winner)
    {
        _label!.Text = winner is { } player ? $"Player {player.ToIndex() + 1} wins!" : "Game over.";
        Visible = true;
    }
}
