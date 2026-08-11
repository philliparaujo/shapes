using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Carries the Lobby's chosen MatchConfig across ChangeSceneToFile into GameRoot. Godot has no
// built-in way to pass constructor arguments through a scene change; a static field set
// immediately before the change and consumed once in GameRoot._Ready is the smallest mechanism
// that works for a single-player-process desktop/mobile game.
public static class PendingMatch
{
    public static MatchConfig? Config { get; set; }

    // PLAN.md C6: set by Lobby's Resume button instead of Config -- GameRoot._Ready checks this
    // first and, if true, loads MatchSaveStore's saved match and replays it (GameSession.Resume)
    // instead of building a fresh MatchConfig. A separate flag rather than overloading Config
    // with a sentinel value, since "resume the saved game" and "start this specific new game"
    // are different instructions or GameRoot could not tell "no config, fall back to two-human
    // hotseat" (the pre-C6 default) apart from "no config, but a save exists, resume it."
    public static bool ResumeRequested { get; set; }
}
