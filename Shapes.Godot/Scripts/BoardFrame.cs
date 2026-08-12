using Godot;

namespace Shapes.Godot.Scripts;

// The rectangle framing the six board slots (PLAN.md 5.C-UI, from references/game screen.png):
// a filled playfield, a thick bevelled border, and a divider separating the two players' rows.
//
// Drawn in _Draw on a mouse-ignoring Control sitting BEHIND the slot rows rather than being a
// StyleBox on a container wrapping them. Two reasons: the divider is a line through the middle
// of the frame, which no StyleBox can express, and a wrapping container would sit between
// PlayerPanel and its Slots row, changing the GlobalPosition BoardAnimator reads via
// CollectSlotRects. As a sibling drawn underneath, it changes nothing about slot layout.
//
// Palette is a warm tan/brown board rather than the dark blue this started as: the game is not
// committing to a dark theme (5.C-UI), and a light playfield is the half of that decision the
// board itself has to make. Everything drawn on top -- slots, cards, text -- reads against a
// mid-tone surface, so neither a light nor a dark UI shell needs the board to change.
public partial class BoardFrame : Control
{
    // Felt-to-wood ramp. The fill is the playing surface; the frame is the rail around it, drawn
    // as three bands (outer shadow, mid body, inner highlight) to fake a bevel -- see _Draw.
    private static readonly Color FillColor = new("d9c4a3");
    private static readonly Color FrameBodyColor = new("8a6a44");
    private static readonly Color FrameLightColor = new("c9a677");
    private static readonly Color FrameDarkColor = new("4f3a24");

    // The divider is a seam in the surface, not a rail: darker than the fill, lighter than the
    // frame, with a thin highlight under it so it reads as an inset groove rather than a stroke.
    private static readonly Color DividerColor = new("a68a63");
    private static readonly Color DividerHighlightColor = new("e8d8bb", 0.7f);

    // Thick enough to read as a physical rail. Split across the three bevel bands below.
    private const float FrameWidth = 14f;
    private const float BevelWidth = 3f;
    private const float DividerWidth = 3f;
    private const float CornerRadius = 14f;

    public override void _Ready()
    {
        // Purely decorative. It sits behind the slots, but Godot still hit-tests it, and a Stop
        // filter here would swallow drops meant for SlotView.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var full = new Rect2(Vector2.Zero, Size);

        // Three nested rounded rects make the bevel: the outermost is the frame's shadowed edge,
        // the next its lit body, the innermost the playing surface. Drawing them as filled boxes
        // rather than as borders on one box is what lets the light band sit INSIDE the dark one
        // (a StyleBox border is a single colour on all four sides, which cannot suggest a light
        // source).
        DrawStyleBox(RoundedBox(FrameDarkColor, CornerRadius), full);

        var body = full.Grow(-BevelWidth);
        DrawStyleBox(RoundedBox(FrameBodyColor, CornerRadius - BevelWidth), body);

        // The inner lip catches the light: one band, inset from the body, before the surface.
        var lip = full.Grow(-(FrameWidth - BevelWidth));
        DrawStyleBox(RoundedBox(FrameLightColor, CornerRadius - FrameWidth + BevelWidth), lip);

        var surface = full.Grow(-FrameWidth);
        DrawStyleBox(RoundedBox(FillColor, Mathf.Max(2f, CornerRadius - FrameWidth)), surface);

        DrawDivider(surface);
    }

    // Divider at the true vertical centre: the two slot rows are positioned symmetrically about
    // it by BoardView's layout, so this reads as the line between the two sides. Inset to the
    // surface rect so it stops at the rail rather than running under it.
    private void DrawDivider(Rect2 surface)
    {
        var y = Size.Y / 2f;
        var left = new Vector2(surface.Position.X, y);
        var right = new Vector2(surface.End.X, y);

        DrawLine(left, right, DividerColor, DividerWidth);
        DrawLine(
            left + new Vector2(0f, DividerWidth),
            right + new Vector2(0f, DividerWidth),
            DividerHighlightColor,
            1f);
    }

    private static StyleBoxFlat RoundedBox(Color color, float radius)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll((int)Mathf.Max(0f, radius));
        return box;
    }
}
