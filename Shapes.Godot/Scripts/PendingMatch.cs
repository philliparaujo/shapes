using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Carries the Lobby's chosen MatchConfig across ChangeSceneToFile into GameRoot. Godot has no
// built-in way to pass constructor arguments through a scene change; a static field set
// immediately before the change and consumed once in GameRoot._Ready is the smallest mechanism
// that works for a single-player-process desktop/mobile game with no save-and-resume across
// process restarts yet (that is C6's job, not this one's).
public static class PendingMatch
{
    public static MatchConfig? Config { get; set; }
}
