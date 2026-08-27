using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Carries the Lobby's chosen MatchConfig across ChangeSceneToFile into GameRoot. Godot has no
// built-in way to pass constructor arguments through a scene change; a static field set
// immediately before the change and consumed once in GameRoot._Ready is the smallest mechanism
// that works for a single-player-process desktop/mobile game.
public static class PendingMatch
{
    public static MatchConfig? Config { get; set; }

    // DESIGN.md C6: set by Lobby's Resume button instead of Config -- GameRoot._Ready checks this
    // first and, if true, loads MatchSaveStore's saved match and replays it (GameSession.Resume)
    // instead of building a fresh MatchConfig. A separate flag rather than overloading Config
    // with a sentinel value, since "resume the saved game" and "start this specific new game"
    // are different instructions or GameRoot could not tell "no config, fall back to two-human
    // hotseat" (the pre-C6 default) apart from "no config, but a save exists, resume it."
    public static bool ResumeRequested { get; set; }

    // DESIGN.md D5: the live RelayMatchTransport a Host/Join flow already opened and paired in
    // Lobby, carried across the same ChangeSceneToFile gap Config crosses -- there is no other
    // way to hand a constructed object (let alone one holding an open socket) into GameRoot's
    // _Ready. Null for every local mode (hotseat/vs-AI/resume), which is what tells GameRoot
    // "this is a network match" without a separate boolean that could disagree with Config.
    //
    // The local seat a network match's own process is playing does NOT need a matching carrier
    // here: Lobby already knows it (the host picks/rolls it, the joiner reads it off MatchStart)
    // and bakes it straight into the MatchConfig it builds, as MatchConfig.ViewerOverride -- one
    // fewer static to keep in sync with Config.
    public static IMatchTransport? Transport { get; set; }
}
