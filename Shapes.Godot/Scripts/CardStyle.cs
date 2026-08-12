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
    public static readonly Color StockColor = new("2b3138");
    public static readonly Color EdgeColor = new("11151a");

    // A recess cut into the board's surface rather than a card lying on it.
    public static readonly Color EmptySlotFill = new("c3ab8a");
    public static readonly Color EmptySlotBorder = new("9c8259");

    public const int BorderWidth = 2;
    public const int CornerRadius = 8;

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
