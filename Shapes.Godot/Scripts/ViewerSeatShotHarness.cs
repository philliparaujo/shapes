using System.Linq;
using Godot;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// TEMPORARY -- scaffolding for the PLAN.md D1 (viewer seat) check, deleted once verified. Same
// shape as UiShotHarness, but pointed at the one frame D1 is actually about: the middle of the
// OPPONENT's turn, which is when the board used to turn around and fan the AI's hand face-up.
//
// Seat TWO is the human here, deliberately. Seat one is the case a "just check player one"
// derivation gets right by accident; seat two is the mirror that catches it, and it is also the
// harder rendering case, since the human is now the seat that does NOT move first.
//
// Asserts rather than only screenshotting: "is the right hand on screen" is a fact about which
// PlayerId owns the cards in the fan, and reading that off a PNG by eye is exactly the kind of
// check that passes because someone wanted it to. The shots are still written, for the things an
// assertion cannot judge (does the board read correctly, is the inert hand legibly inert).
public partial class ViewerSeatShotHarness : Control
{
    private int _frame;
    private int _opponentTurnFrames;
    private bool _sawOpponentTurn;
    private bool _failed;

    public override void _Ready()
    {
        // Human in seat two against a Greedy seat one: seat one moves first, so the very first
        // thing this harness sees IS an opponent turn -- no setup needed to reach the case.
        PendingMatch.Config = new MatchConfig(
            new SeatConfig(AgentKind.Greedy, 0), SeatConfig.Human, 12345UL);
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

        // The viewer itself, checked directly rather than only through its rendered consequences:
        // a Fixed(Two) mode must resolve to seat two on every frame regardless of turn.
        if (gameRoot.ViewerForTesting != PlayerId.Two)
        {
            GD.PushError(
                $"D1 VIOLATION frame {_frame}: viewer resolved to "
                + $"{gameRoot.ViewerForTesting}, expected seat two (the human).");
            _failed = true;
        }

        var active = session.State.ActivePlayer;
        var humanHand = session.State[PlayerId.Two].Hand;
        var aiHand = session.State[PlayerId.One].Hand;
        var onScreen = fan.GetChildren().OfType<CardFace>().Select(c => c.CardId).ToList();

        // The invariant, checked on EVERY frame rather than at a chosen moment: the fan shows the
        // human's cards, whoever's turn it is. Under the old ActivePlayer-derived perspective this
        // held on the human's turn and inverted on the AI's.
        if (onScreen.Count > 0)
        {
            var showsAiCard = onScreen.Any(id => !humanHand.Contains(id) && aiHand.Contains(id));
            if (showsAiCard)
            {
                GD.PushError(
                    $"D1 VIOLATION frame {_frame}: hand fan is showing seat one's (AI) cards while "
                    + $"the viewer is seat two. active={active} onScreen=[{string.Join(",", onScreen)}]");
                _failed = true;
            }
        }

        // Counted, not just flagged: the assertion above is only meaningful if the AI's turn was
        // actually ON SCREEN for a while (RunAiTurns paces itself at MoveDelaySeconds, so this
        // should be many frames). A pass with a handful of opponent frames would mean the harness
        // blinked past the very case it exists to check.
        if (active == PlayerId.One)
        {
            _opponentTurnFrames++;
            _sawOpponentTurn = true;

            if (_opponentTurnFrames == 1)
            {
                Shoot("d1-opponent-turn.png");
            }
        }

        // Wait for the AI to actually hand back rather than firing on a frame number -- the AI
        // paces its moves, so how many frames its turn takes is a timing detail, not a constant.
        if (_sawOpponentTurn && active == PlayerId.Two)
        {
            Shoot("d1-own-turn.png");

            if (_opponentTurnFrames < 10)
            {
                GD.PushError(
                    $"D1 CHECK INCONCLUSIVE -- only {_opponentTurnFrames} frame(s) of opponent "
                    + "turn were observed, too few to have exercised the case.");
                GetTree().Quit(2);
                return;
            }

            GD.Print(
                _failed
                    ? "D1 CHECK FAILED -- see the violation(s) above."
                    : "D1 CHECK PASSED -- hand fan held seat two's cards for all "
                      + $"{_opponentTurnFrames} frames of seat one's turn, and the viewer never "
                      + "moved off seat two.");
            GetTree().Quit(_failed ? 1 : 0);
            return;
        }

        // Don't hang forever if the AI never hands back.
        if (_frame > 3000)
        {
            GD.PushError("D1 CHECK INCONCLUSIVE -- never reached the human's turn.");
            GetTree().Quit(2);
        }
    }

    // No-op under --headless, which is how this harness actually runs: the dummy rendering driver
    // has no viewport texture, so GetTexture() returns null and the PNG save throws. The assertion
    // above is the part that matters and does not need pixels -- it reads the live GameState and
    // the real CardFace nodes. Kept (rather than deleted) so the same harness can be run windowed
    // for an eyeball check of the two frames.
    private void Shoot(string fileName)
    {
        // Tested by rendering-driver NAME, not by null-checking the texture: under --headless the
        // dummy driver hands back a live Texture2D whose backing texture is null, so GetImage()
        // throws from inside the engine rather than returning null for a guard to catch.
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print($"ViewerSeatShotHarness: headless, skipping {fileName}");
            return;
        }

        var path = ProjectSettings.GlobalizePath($"user://{fileName}");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"ViewerSeatShotHarness wrote {path}");
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
