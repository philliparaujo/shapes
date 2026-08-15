using Godot;

namespace Shapes.Godot.Scripts;

// Lays out a single child to fill this control, and keeps doing so as this control resizes.
//
// Exists because a Button is not a Container: it never sorts its children, so a child only ever
// occupies whatever rect its own anchors AND offsets describe. Calling SetAnchorsAndOffsetsPreset
// at construction time (the previous fix) computes those offsets against the parent's size *at
// that moment*, which is (0,0) before the node is in the tree and laid out -- so the offsets bake
// in an empty rect, and the child's labels render at zero width forever after. That is the
// "board move buttons show a cost icon and no text" bug, twice.
//
// Tracking Resized instead means the content follows the button's real size whenever layout
// actually settles, with no dependence on when Godot's deferred passes happen to run.
public partial class ButtonContentHost : Control
{
    private Control? _content;

    public ButtonContentHost()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // Full-rect ANCHORS (fractions, not offsets) so this host tracks the Button's size itself.
        // A Button does not sort its children, so nothing else ever assigns this control a size:
        // left at the default zero anchors it stays (0,0) forever, and every layer it hosts is
        // sized from that zero rect. Content built in code hid the bug behind its own
        // CustomMinimumSize (a Label still paints at its minimum), which is why it surfaced only
        // once a row put ART in a host -- a TextureRect has no minimum to fall back to and simply
        // drew nothing.
        //
        // Anchors are safe here in a way the OFFSET preset this class's header warns about is
        // not: anchor fractions are re-resolved against the parent's real size on every layout
        // pass, whereas SetAnchorsAndOffsetsPreset bakes the parent's CURRENT size into fixed
        // offsets -- which, before the node is in the tree, is the (0,0) that trap describes.
        AnchorRight = 1f;
        AnchorBottom = 1f;
    }

    public void SetContent(Control content)
    {
        _content = content;
        AddChild(content);
        Apply();
    }

    public override void _Ready()
    {
        Resized += Apply;
        Apply();
    }

    private void Apply()
    {
        if (_content is null)
        {
            return;
        }

        // Anchors zeroed before the rect is assigned, every pass. Position/Size on a Control whose
        // anchors are not all zero is advisory: Godot recomputes the rect from anchor fractions
        // plus offsets on the next layout pass and the assignment is silently undone. Content
        // built in code here has default (zero) anchors and never noticed -- but content
        // INSTANTIATED FROM A SCENE carries whatever the scene author set, and HoverDetailPanel
        // .tscn is authored as the board's corner-parked tooltip (bottom-left anchors, a
        // hand-placed offset rect). Dropped into a grid cell unchanged, every card face rendered
        // shifted off its own tile. Doing this here rather than at each call site means a caller
        // handing over a scene root does not have to know the trap exists -- and it has to run
        // AFTER the node is in the tree, which a caller resetting the preset itself before
        // AddChild gets wrong (that computes offsets against a zero-size parent, the same
        // anchors-at-construction failure this class's header already documents).
        _content.AnchorLeft = 0f;
        _content.AnchorTop = 0f;
        _content.AnchorRight = 0f;
        _content.AnchorBottom = 0f;

        _content.Position = Vector2.Zero;
        _content.Size = Size;
    }
}
