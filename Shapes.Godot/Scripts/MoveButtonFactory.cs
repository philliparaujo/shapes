using System;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Builds one move's button for SlotView (board). CardFace (hand) deliberately does NOT use this
// -- a hand card only has room for a compact name+cost line (see its own Render), full effect
// text is board-only. Every move renders here, usable or not (PLAN.md B1a): an unusable move
// (condition unmet, used this turn, unaffordable) still tells the player what the creature can
// do, just not right now, so it renders disabled and dimmed rather than being omitted --
// omitting it would make "no moves" and "one move I can't currently use" look identical.
//
// Sized for the real worst case, not an arbitrary guess: RuleSet.MaxMergeDepth is 2, so a
// creature has at most 2 source cards, and every real card has exactly 2 moves -- 4 moves is the
// hard ceiling, never more. That's why SlotView can budget a fixed total height for its move
// list instead of needing scrolling (the user's explicit call) to survive an unbounded case that
// can't actually occur.
public static class MoveButtonFactory
{
    // Fixed width, not just a height floor: Godot sizes a container to fit its widest child's
    // *unwrapped* minimum size unless something caps it, so leaving width at 0 (auto) meant the
    // button -- and everything containing it, all the way up to the board row -- grew to fit the
    // single longest effect-text line instead of actually wrapping. This is the cap that makes
    // AutowrapMode do anything.
    public const float Width = 240f;

    // Fixed for the same reason -- a height of 0 (auto) sizes to fit the longest wrapped line's
    // full text, which is how the previous version blew out its container's height and caused
    // OpponentPanel/SelfPanel to overlap. This is a real, non-negotiable cap, not a floor:
    // ClipText backstops it below, so even the rare effect text too long for 3 lines at the
    // reduced font clips with an ellipsis instead of growing the button (and by extension the
    // whole board) taller. Sized for 2 wrapped lines at FontSize, not 3 -- SlotView's own fixed
    // budget only has room for that at up to 4 moves (MaxMergeDepth's real worst case).
    public const float Height = 42f;

    // Smaller than the theme default so more characters fit per wrapped line, meaning fewer
    // real cards need the ClipText backstop at all.
    private const int FontSize = 11;

    public static Button Create(MoveText text, bool isUsable, Action onChosen)
    {
        var button = new Button
        {
            Text = $"{text.Name} [{text.Cost}] {text.Effects}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
            CustomMinimumSize = new Vector2(Width, Height),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            Disabled = !isUsable,
        };
        button.AddThemeFontSizeOverride("font_size", FontSize);

        if (!isUsable)
        {
            button.Modulate = new Color(1f, 1f, 1f, 0.55f);
        }

        button.Pressed += () => onChosen();
        return button;
    }
}
