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

        _content.Position = Vector2.Zero;
        _content.Size = Size;
    }
}
