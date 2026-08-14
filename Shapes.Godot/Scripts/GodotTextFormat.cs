using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Points the adapter's rules-text formatting at Godot's inline resource icons, once per process.
//
// Every scene that renders card text (GameRoot's board, the hand, the tooltip, CardBrowser, the
// shot harnesses) has to run through this before building a CardText, and each of them can be the
// FIRST scene loaded -- CardBrowser and the harnesses are opened directly, not only via GameRoot.
// A one-line call in each _Ready would therefore be five chances to forget, and forgetting shows
// up as a literal "[anvil]" in one view and a drawn icon in the next.
//
// So it is an idempotent Ensure() that those entry points call, rather than a static constructor
// on CardTextFormat itself: the adapter assembly must not depend on Shapes.Godot (the dependency
// runs the other way), so the assignment has to live here, on the Godot side.
public static class GodotTextFormat
{
    private static bool _applied;

    public static void Ensure()
    {
        if (_applied)
        {
            return;
        }

        _applied = true;
        CardTextFormat.Resource = InlineResourceIcons.SentinelFormat;
    }
}
