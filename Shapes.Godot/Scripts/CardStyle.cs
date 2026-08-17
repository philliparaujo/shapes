using Godot;

namespace Shapes.Godot.Scripts;

// The one place a card's outer shape lives: rounded corners, border weight, and the card-stock
// palette shared by the three views that draw a card (PLAN.md 5.C-UI).
//
// A hand card, a creature in play, and the hover tooltip are three separate scenes that each used
// to bring their own panel -- Godot's default Button/PanelContainer styling in two of them, and a
// hand-rolled StyleBoxFlat in the third. That is the same drift MoveRowFactory exists to prevent
// for move rows: change the corner radius in one view and the other two silently disagree.
//
// Empty board slots are the deliberate exception (see SlotView): a hole in the board is not a
// card, so it takes the recessed tan treatment rather than card stock.
public static class CardStyle
{
    // Card stock and its edge. Dark because every card's art and text are authored against a dark
    // face -- the tan board changed what sits AROUND a card, not the card itself.
    //
    // Aliases onto Palette (PLAN.md D3 phase 1) rather than holding its own literals, so a card and
    // a themed panel cannot disagree about what "card stock" is. Kept as names here because these
    // two are what the card-drawing code has always called them.
    public static readonly Color StockColor = Palette.Surface;
    public static readonly Color EdgeColor = Palette.SurfaceEdge;

    // A recess cut into the board's surface rather than a card lying on it. Darker than the felt
    // it sits in, so an empty slot reads as a hole rather than as a lighter tile placed on top.
    public static readonly Color EmptySlotFill = new("26382d");
    public static readonly Color EmptySlotBorder = new("1b2a21");

    // Drop shadow beneath a card. Cards used to sit flush on the board, which made them look
    // printed onto it; a shadow is what puts them physically above the surface.
    //
    // Faked as nested rounded outlines at decreasing alpha rather than a real blur -- Godot's
    // immediate-mode drawing has no blur primitive, and StyleBoxFlat's shadow_size draws a hard
    // edge. Same layered-band trick BoardFrame's inner shadow and CardBackVignette use.
    public static readonly Color ShadowColor = new(0f, 0f, 0f, 0.45f);
    public const int ShadowBands = 6;
    public const float ShadowSpread = 7f;
    public const float ShadowDropY = 4f;

    // Draws the shadow for a rect of `size` at the local origin of `canvas`. Callers draw this
    // BEFORE their own body so the shadow lands underneath it.
    public static void DrawCardShadow(CanvasItem canvas, Vector2 size)
    {
        for (var i = ShadowBands; i >= 1; i--)
        {
            var t = i / (float)ShadowBands;
            var grow = ShadowSpread * t;

            var box = new StyleBoxFlat
            {
                BgColor = new Color(
                    ShadowColor.R,
                    ShadowColor.G,
                    ShadowColor.B,
                    ShadowColor.A / ShadowBands * (1f - t + 0.35f)),
            };
            box.SetCornerRadiusAll(CornerRadius + (int)grow);

            var rect = new Rect2(new Vector2(0f, ShadowDropY), size).Grow(grow);
            canvas.DrawStyleBox(box, rect);
        }
    }

    public const int BorderWidth = Palette.BorderWidth;
    public const int CornerRadius = Palette.CornerRadius;

    // Every Button state. Godot picks a different StyleBox per state, so overriding only "normal"
    // lets the default theme flash back the moment the cursor crosses the control.
    private static readonly string[] ButtonStates =
        ["normal", "hover", "pressed", "focus", "disabled"];

    public static StyleBoxFlat Box(Color fill, Color border)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(BorderWidth);
        box.SetCornerRadiusAll(CornerRadius);
        return box;
    }

    public static StyleBoxFlat CardBox() => Box(StockColor, EdgeColor);

    // Applies a card-stock panel to a Button across all its states (CardFace, SlotView).
    public static void ApplyToButton(Control control, Color fill, Color border)
    {
        foreach (var state in ButtonStates)
        {
            control.AddThemeStyleboxOverride(state, Box(fill, border));
        }
    }

    // Applies to a PanelContainer, which has the single "panel" style (HoverDetailPanel).
    public static void ApplyToPanel(Control panel) =>
        panel.AddThemeStyleboxOverride("panel", CardBox());
}
