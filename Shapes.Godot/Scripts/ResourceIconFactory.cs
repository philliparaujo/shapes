using Godot;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Scripts;

// Draws the three resource-type shapes (Spike=triangle, Anvil=square, Wheel=circle) as actual
// geometry rather than reusing ResourceIcons' text glyphs -- a glyph read fine at label size but
// goes thin/font-dependent blown up to placeholder-art size (PLAN.md B1c's temporary stand-in
// for real card art). One factory, three call sites (hand card cost badge, tooltip cost badge,
// move cost badges, in-play card art) so the shape/color mapping lives in exactly one place.
//
// Three fixed sizes rather than an arbitrary pixel parameter: Big (hand/tooltip cost badges,
// plus the in-play placeholder-art panel), Medium (move costs, where a move row needs a
// recognizable but not dominant icon), Small (inline glyphs with no number -- move description
// text, status-adjacent contexts). Matching the three sizes named in the request.
public static class ResourceIconFactory
{
    public enum IconSize
    {
        Small,
        Medium,
        Big,
    }

    private static readonly Color SpikeColor = new(0.85f, 0.25f, 0.2f);
    private static readonly Color AnvilColor = new(0.25f, 0.65f, 0.3f);
    private static readonly Color WheelColor = new(0.25f, 0.45f, 0.9f);
    private static readonly Color NeutralColor = new(0.5f, 0.5f, 0.5f);

    private static Color ColorOf(ResourceType type) => type switch
    {
        ResourceType.Spike => SpikeColor,
        ResourceType.Anvil => AnvilColor,
        ResourceType.Wheel => WheelColor,
        _ => NeutralColor,
    };

    private static float DiameterOf(IconSize size) => size switch
    {
        IconSize.Small => 18f,
        IconSize.Medium => 32f,
        IconSize.Big => 96f,
        _ => 32f,
    };

    // number is null for the no-number small glyph case (move description text); a badge with a
    // number renders it as a bold overlay in the shape's bottom-right corner, high-contrast
    // against the shape's fill so it reads at Medium size, not just Big.
    public static Control Create(ResourceType type, IconSize size, int? number = null)
    {
        var diameter = DiameterOf(size);

        // A CenterContainer sorts its children to fill/center within itself, so the shape and the
        // number both track the icon's real size. A plain Control would not: it never runs a
        // layout pass, and setting an anchors/offsets preset at construction bakes in the parent's
        // size *at that moment* -- (0,0) before the node is in the tree -- which renders the
        // children at zero size forever after. See ButtonContentHost for the same trap on Button.
        var root = new IconStack
        {
            CustomMinimumSize = new Vector2(diameter, diameter),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var shape = new ResourceShape { Type = type, Color = ColorOf(type) };
        shape.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(shape);

        if (number is { } n)
        {
            var label = new Label
            {
                Text = n.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeConstantOverride("outline_size", (int)(diameter * 0.14f));
            label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
            label.AddThemeFontSizeOverride("font_size", (int)(diameter * 0.5f));
            root.AddChild(label);
        }

        return root;
    }

    // A full-panel placeholder for the in-play card's art region (PLAN.md B1c): the same shape
    // drawn edge-to-edge rather than as a small badge, standing in for real art until it exists.
    // No number -- this is "the art," not a cost readout.
    // Every caller parents this into a real Container (a MarginContainer art holder, or the
    // board slot's HBoxContainer for merged split art), which sizes it from the flags below --
    // deliberately no anchors/offsets preset here, since that would bake in a pre-layout (0,0)
    // rect and is what left earlier versions of this drawing at zero size.
    public static Control CreateArtPlaceholder(ResourceType type)
    {
        var shape = new ResourceShape { Type = type, Color = ColorOf(type) };
        shape.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        shape.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        shape.MouseFilter = Control.MouseFilterEnum.Ignore;
        shape.MinFillRatio = 0.92f;
        return shape;
    }
}

// Stretches every child to fill itself, so a shape and the number drawn over it stay aligned and
// correctly sized. A real Container (not a plain Control) specifically so Godot runs a sort pass
// on resize -- see ResourceIconFactory.Create's note on why anchors/offsets set at construction
// silently collapse to a zero rect here.
public partial class IconStack : Container
{
    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren)
        {
            return;
        }

        foreach (var child in GetChildren())
        {
            if (child is Control control)
            {
                FitChildInRect(control, new Rect2(Vector2.Zero, Size));
            }
        }
    }
}

// Draws one resource-type shape filling its control's rect: triangle (Spike), square (Anvil),
// circle (Wheel). A custom _Draw rather than three separate node types (Polygon2D/ColorRect/arc)
// so every caller treats "a resource shape" as one Control regardless of which type it is.
public partial class ResourceShape : Control
{
    public ResourceType Type { get; set; }
    public Color Color { get; set; } = Colors.Gray;

    // Fraction of the control's shorter side the shape actually fills -- badge contexts want a
    // little breathing room around the shape (default), the full-art placeholder wants it
    // to read as "the whole panel is the art" (set closer to 1.0 by CreateArtPlaceholder).
    public float MinFillRatio { get; set; } = 0.85f;

    public override void _Ready()
    {
        // Godot only calls _Draw once at creation time; the control's real Size isn't settled
        // until layout runs a frame later (and can change again on window resize), so without
        // this a shape drawn before its container sizes it would freeze at Size (0,0).
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var size = Size;
        var extent = Mathf.Min(size.X, size.Y) * MinFillRatio;
        var center = size / 2f;

        switch (Type)
        {
            case ResourceType.Spike:
                DrawTriangle(center, extent);
                break;
            case ResourceType.Anvil:
                DrawSquare(center, extent);
                break;
            case ResourceType.Wheel:
                DrawCircle(center, extent / 2f, Color);
                break;
            default:
                DrawCircle(center, extent / 2f, Color);
                break;
        }
    }

    private void DrawTriangle(Vector2 center, float extent)
    {
        var half = extent / 2f;
        var points = new[]
        {
            new Vector2(center.X, center.Y - half),
            new Vector2(center.X + half, center.Y + half),
            new Vector2(center.X - half, center.Y + half),
        };
        DrawColoredPolygon(points, Color);
    }

    private void DrawSquare(Vector2 center, float extent)
    {
        var half = extent / 2f;
        var rect = new Rect2(center.X - half, center.Y - half, extent, extent);
        DrawRect(rect, Color);
    }
}
