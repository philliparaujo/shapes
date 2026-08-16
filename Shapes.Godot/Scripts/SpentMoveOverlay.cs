using Godot;

namespace Shapes.Godot.Scripts;

// The darkened wash over a move already used (PLAN.md D2 item 3). The other half of the marking is
// the row's own text, recoloured amber by MoveRowFactory -- see SpentTextColor there.
//
// TWO EARLIER CUTS, both dropped. A strike-through hairline read as a rendering artifact: it cut
// through the name and the wrapped, icon-embedded description at whatever height the row happened
// to be, and competed for the same visual channel as the content it crossed. A "USED" chip in the
// corner was legible but noisy -- a second object crowding a row that already holds a cost pip, a
// name and a description, and one that had to be positioned around all three.
//
// Recolouring the text carries the same information using the space the row already spends. Nothing
// is added to the layout, and the whole row changes at once, so it is if anything more visible than
// a corner tag while taking no room.
//
// PRECEDENCE over the other three unusable reasons is preserved, which is the property this needs
// most. Unaffordable / condition-unmet / not-your-turn all express themselves by REMOVING contrast
// (alpha down, colours flattened); this shifts HUE instead, on an axis none of them touch. So a
// spent move still reads as spent no matter how dimmed it is -- and MoveButtonFactory applies the
// spent treatment INSTEAD of the disabled fade rather than on top of it, so the two never stack.
//
// Full-rect ANCHORS, never an offset preset: a Button does not sort its children, so this is sized
// by anchor fractions re-resolved on every layout pass. Setting offsets at construction would bake
// in the parent's pre-layout (0,0) rect -- the trap ButtonContentHost's header documents at length.
public partial class SpentMoveOverlay : Control
{
    // Cool and dark rather than a neutral grey wash: it has to sit visibly ON TOP of the button's
    // own colours instead of reading as more of the same fade. Drawn UNDER the row's content (see
    // MoveButtonFactory, which adds this before the content host) so the amber text stays crisp
    // rather than being veiled by its own scrim.
    private static readonly Color ScrimColor = new(0.06f, 0.08f, 0.12f, 0.55f);

    public SpentMoveOverlay()
    {
        // Never intercepts input: the button underneath is already Disabled, but an overlay that
        // ate events would also block the hover a slot uses to raise its detail tooltip.
        MouseFilter = MouseFilterEnum.Ignore;

        AnchorRight = 1f;
        AnchorBottom = 1f;
    }

    public override void _Ready() => Resized += QueueRedraw;

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), ScrimColor);
}
