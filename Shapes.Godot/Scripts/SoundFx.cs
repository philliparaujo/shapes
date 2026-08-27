using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The one call every sound goes through (DESIGN.md D4).
//
// A facade over AudioDirector.Instance, and it earns its file by removing a null check from every
// call site. The autoload is null in exactly two situations -- a scene run directly from the editor
// without the project's autoloads, and the in-editor shot harnesses -- and both are cases where the
// right behaviour is silence rather than a crash. Putting that judgement here means no caller has
// to remember it, and no caller can get it wrong.
public static class SoundFx
{
    public static void Play(SoundCue cue) => AudioDirector.Instance?.PlayCue(cue);

    // Marks a Button as having its own sound, so the automatic click below leaves it alone.
    private static readonly StringName SilentMeta = "shapes_silent_button";

    // Marks a Button this class has ALREADY wired, so it cannot be wired twice.
    //
    // Needed because there are now two paths onto the same button: the node_added hook and the
    // catch-up walk in AttachClicksGlobally. A node entering the tree while a walk is in progress
    // -- or any future second call to attach -- would otherwise take both and click twice on one
    // press, which is the same double-trigger the Silent exemption exists to prevent, arriving by
    // a different route.
    private static readonly StringName WiredMeta = "shapes_click_wired";

    // Gives EVERY Button in the game its click sound, including ones that do not exist yet.
    //
    // WHY A TREE HOOK RATHER THAN A WALK. The first cut of this walked a screen's tree once from
    // its _Ready. That covers the lobby, whose buttons are all authored in the .tscn, and silently
    // misses everything built in code -- the card browser's grid, the deckbuilder's collection and
    // decklist rows, the board's move buttons (MoveButtonFactory), and the pagination controls that
    // are rebuilt on every page change. Those are the majority of the buttons a player actually
    // presses, and the failure is invisible: a missed button is simply quiet, with nothing to
    // notice it. Re-walking after each rebuild would work only for as long as every future rebuild
    // site remembers to call it, which is the same drift in slower motion.
    //
    // `node_added` fires for every node entering the tree for the whole process lifetime, so a
    // button is wired by the fact of existing rather than by anyone remembering it. Connected once
    // from AudioDirector._Ready.
    //
    // AND THEN A CATCH-UP WALK, which is not redundant. `node_added` only reports nodes that enter
    // AFTER the connection, and the main scene is already in the tree by the time an autoload's
    // _Ready runs -- autoloads are added first, but the main scene is instantiated during the same
    // startup step and Godot does not emit node_added retroactively for it. So the very first
    // screen a player sees (the lobby) had silent buttons until they navigated away and back,
    // at which point the re-entering scene finally passed through the hook. Reported from real
    // play as exactly that: "no click sounds on the home screen until I load another scene."
    //
    // Walking the existing tree here closes that window. Every later scene is still covered by the
    // hook alone, so this runs once and never again.
    public static void AttachClicksGlobally(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        tree.NodeAdded += OnNodeAdded;

        if (tree.Root is { } root)
        {
            AttachExisting(root);
        }
    }

    private static void AttachExisting(Node node)
    {
        OnNodeAdded(node);

        foreach (var child in node.GetChildren())
        {
            AttachExisting(child);
        }
    }

    // Disconnects the hook. Called from AudioDirector._ExitTree so the delegate stops firing while
    // the tree tears down -- otherwise it keeps wiring click handlers onto nodes on their way out.
    public static void DetachClicksGlobally(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        tree.NodeAdded -= OnNodeAdded;
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is not Button button || IsSilent(button) || button.HasMeta(WiredMeta))
        {
            return;
        }

        button.SetMeta(WiredMeta, true);
        button.Pressed += () => Play(SoundCue.ButtonClick);
    }

    // Buttons that already have a sound of their own, and so must NOT also click.
    //
    // A card is a Button (CardFace) and playing it sounds CardPlay; a board slot is a Button
    // (SlotView) and acting on it sounds whatever the action was; a move button submits a UseMove.
    // Without this exemption each of those would click AND thump on the same press, which reads as
    // a double-trigger rather than as feedback.
    //
    // TYPES ARE CHECKED DIRECTLY, not only via the meta flag, because CardFace and SlotView are
    // instantiated from .tscn files -- they are already in the tree by the time any caller could
    // mark them, so a flag alone would always lose the race. The meta flag stays for buttons built
    // in code (MoveButtonFactory), where marking before AddChild is straightforward.
    private static bool IsSilent(Button button) =>
        button is CardFace or SlotView || button.HasMeta(SilentMeta);

    // Opts one code-built button out of the automatic click. Must be called before the node enters
    // the tree -- the hook above reads the flag at that moment. Every current caller constructs the
    // button and marks it in the same breath, which is the only ordering that is obviously correct.
    public static void Silence(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetMeta(SilentMeta, true);
    }
}
