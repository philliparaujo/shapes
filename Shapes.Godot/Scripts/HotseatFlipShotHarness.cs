using System.Linq;
using Godot;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// TEMPORARY -- the other half of the DESIGN.md D1 check, deleted alongside ViewerSeatShotHarness.
// That harness proves the view STOPS following the active player when one seat is an AI; this one
// proves it still DOES follow when both seats are human, which is D1's compatibility bar: local
// two-player hotseat is one screen passed between two people, and there flipping is correct.
//
// Worth its own harness rather than a flag on the other one because reaching the second seat's
// turn needs a real End Turn press (there is no AI to hand over on its own), which is a different
// script even though the assertion is the mirror image.
public partial class HotseatFlipShotHarness : Control
{
    private int _frame;
    private bool _endTurnPressed;
    private bool _sawSeatOne;
    private bool _sawSeatTwo;
    private bool _failed;

    public override void _Ready()
    {
        PendingMatch.Config = new MatchConfig(SeatConfig.Human, SeatConfig.Human, 12345UL);
        PendingMatch.ResumeRequested = false;

        var scene = GD.Load<PackedScene>("res://Scenes/GameRoot.tscn");
        AddChild(scene.Instantiate<GameRoot>());
    }

    public override void _Process(double delta)
    {
        _frame++;

        if (_frame < 20)
        {
            return;
        }

        var root = GetTree().Root;
        var gameRoot = FindByType<GameRoot>(root);
        var session = gameRoot?.SessionForTesting;
        var fan = FindByType<HandFan>(root);
        if (gameRoot is null || session is null || fan is null)
        {
            return;
        }

        var active = session.State.ActivePlayer;

        // The hotseat invariant, and the exact opposite of the vs-AI one: the viewer tracks the
        // active player, so the screen always belongs to whoever is about to move.
        if (gameRoot.ViewerForTesting != active)
        {
            GD.PushError(
                $"HOTSEAT VIOLATION frame {_frame}: active={active} but viewer="
                + $"{gameRoot.ViewerForTesting}; two-human play must follow the active seat.");
            _failed = true;
        }

        // And the hand on screen is the active seat's, which is what makes passing the device work.
        var onScreen = fan.GetChildren().OfType<CardFace>().Select(c => c.CardId).ToList();
        var activeHand = session.State[active].Hand;
        if (onScreen.Count > 0 && onScreen.Any(id => !activeHand.Contains(id)))
        {
            GD.PushError(
                $"HOTSEAT VIOLATION frame {_frame}: fan holds a card the active seat ({active}) "
                + $"does not own. onScreen=[{string.Join(",", onScreen)}]");
            _failed = true;
        }

        if (active == PlayerId.One)
        {
            _sawSeatOne = true;
        }
        else
        {
            _sawSeatTwo = true;
        }

        // Press End Turn once, via the real button, to get the handover this harness is about.
        if (!_endTurnPressed && _frame > 40)
        {
            var button = FindEndTurnButton(root);
            if (button is { Disabled: false })
            {
                button.EmitSignal(BaseButton.SignalName.Pressed);
                _endTurnPressed = true;
            }
        }

        // Both seats observed as the viewer -- the flip happened and held on both sides.
        if (_sawSeatOne && _sawSeatTwo)
        {
            GD.Print(
                _failed
                    ? "HOTSEAT CHECK FAILED -- see the violation(s) above."
                    : "HOTSEAT CHECK PASSED -- view followed the active seat across a handover, "
                      + "and the fan always held the active seat's hand.");
            GetTree().Quit(_failed ? 1 : 0);
            return;
        }

        if (_frame > 3000)
        {
            GD.PushError(
                $"HOTSEAT CHECK INCONCLUSIVE -- sawSeatOne={_sawSeatOne} sawSeatTwo={_sawSeatTwo}; "
                + "never observed both seats.");
            GetTree().Quit(2);
        }
    }

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
}
