using Godot;

namespace Shapes.Godot.Scripts;

// The right-hand rail (DESIGN.md 5.C-UI, from references/game screen.png): opponent panel, End Turn
// button, player panel, stacked top to bottom.
//
// Its only job beyond stacking is vertical alignment: the End Turn button must sit level with the
// board's centre divider, and the two panels must be equidistant from it. Anchoring the rail to
// the window's midpoint does NOT achieve that -- the board is not centred on the window (it hangs
// below a top margin, above the hand), so the divider sits well above the window's middle. This
// aligns to the divider itself and lets the rail fall where it must.
public partial class SideRail : VBoxContainer
{
    [Export] public NodePath BoardFramePath { get; set; } = "../Layout/BoardArea/BoardFrame";

    private Control? _boardFrame;

    public override void _Ready()
    {
        _boardFrame = GetNodeOrNull<Control>(BoardFramePath);

        // Both this rail and the board resize independently, and either moving invalidates the
        // alignment -- so track both rather than positioning once at startup.
        Resized += QueueAlign;
        if (_boardFrame is not null)
        {
            _boardFrame.Resized += QueueAlign;
        }

        QueueAlign();
    }

    // Deferred: on the frame this runs, the board's own rect may not have been laid out yet, and
    // reading a pre-layout (0,0) size would park the rail at the top of the screen. Same trap
    // BoardView.RefreshAnimatorLayout documents for slot rects.
    private void QueueAlign() => CallDeferred(nameof(Align));

    private void Align()
    {
        if (_boardFrame is null || GetParent() is not Control parent)
        {
            return;
        }

        // The divider is drawn at the frame's vertical midpoint (BoardFrame.DrawDivider), so that
        // is the line to match. Converted out of global space because our own Position is
        // relative to the parent.
        var dividerY = _boardFrame.GlobalPosition.Y + _boardFrame.Size.Y / 2f;
        var localDividerY = dividerY - parent.GlobalPosition.Y;

        // Align the BUTTON to the divider, not the rail's own midpoint. Those are not the same
        // point: centring the rail assumes its middle child sits at its centre, which held only
        // while every child measured exactly its minimum. A SidePanel is a plain Control whose
        // badge is drawn outside its rect, so the laid-out rail can differ from the sum of its
        // minimums -- and the button ended up 19px off the divider as a result.
        //
        // Positioning off the button's measured offset within the rail sidesteps that entirely:
        // wherever the button actually lands, the rail shifts so that point meets the divider.
        var height = Mathf.Max(Size.Y, GetCombinedMinimumSize().Y);
        Size = new Vector2(Size.X, height);

        var button = GetNodeOrNull<Control>("MiddleColumn");
        var buttonCentreInRail = button is null
            ? height / 2f
            : button.Position.Y + button.Size.Y / 2f;

        Position = new Vector2(Position.X, localDividerY - buttonCentreInRail);
    }
}
