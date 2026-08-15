using System.Linq;
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

    private static readonly Color SpikeColor = new(0.8f, 0.2f, 0.15f);
    private static readonly Color AnvilColor = new(0.25f, 0.65f, 0.3f);
    private static readonly Color WheelColor = new(0.25f, 0.45f, 0.9f);
    private static readonly Color NeutralColor = new(0.5f, 0.5f, 0.5f);

    // Public because the type palette is not this factory's private business: CostCurveChart
    // stacks its bar segments in these exact colors, and a second copy of the mapping there is
    // how a wheel segment ends up a different blue from a wheel pip on the card beside it.
    public static Color ColorOf(ResourceType type) => type switch
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

    // The number's tint when a free-moves spell has discounted this move's type for the turn --
    // a light yellow against the usual white, so a 0 that is temporary reads as different from a
    // move that simply prints no cost. Chosen light rather than saturated because it sits on top
    // of three different shape fills (red/green/blue) and has to stay legible on all of them.
    //
    // Public because keyword highlighting in rules text takes the SAME yellow (see
    // InlineResourceIcons.KeywordColor): both mark "this word/number is not the ordinary case,
    // look here," and two hand-tuned yellows a shade apart would read as an inconsistency rather
    // than as one highlight convention.
    public static readonly Color DiscountedNumberColor = new(0.9f, 0.85f, 0.35f);

    // number is null for the no-number small glyph case (move description text); a badge with a
    // number renders it as a bold overlay in the shape's bottom-right corner, high-contrast
    // against the shape's fill so it reads at Medium size, not just Big.
    //
    // `discounted` recolors that number without changing anything else about the badge: the SHAPE
    // still shows which resource the move is paid in (that never changes), only the amount owed
    // right now does. Callers pass the already-effective number -- this does not compute the
    // discount, it only styles it. See MoveText.Of.
    public static Control Create(
        ResourceType type, IconSize size, int? number = null, bool discounted = false)
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
            label.AddThemeColorOverride(
                "font_color", discounted ? DiscountedNumberColor : Colors.White);
            label.AddThemeConstantOverride("outline_size", (int)(diameter * 0.14f));
            label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
            label.AddThemeFontSizeOverride("font_size", (int)(diameter * 0.5f));

            // A triangle's visual mass sits well below its bounding box's vertical center (a
            // triangle's centroid is 1/3 up from its base, not half), so a label centered in the
            // full IconStack rect reads as sitting too high specifically on Spike -- square and
            // circle have no such offset between box-center and shape-center. IconStack fits
            // every child to the SAME full rect every layout pass regardless of size flags, so a
            // one-shot Position nudge would just get overwritten (or double up if applied inside
            // a Resized handler instead) -- VerticalOffset is IconStack's own per-instance knob
            // for exactly this, applied fresh inside FitChildInRect every time it runs.
            if (type == ResourceType.Spike)
            {
                root.LabelVerticalOffset = diameter * 0.08f;
            }

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
    // Pixels to push the Label child down after fitting -- 0 for every shape except Spike (see
    // Create's note on why a triangle's centered number needs this and the others don't). Applied
    // to the Label specifically, not the ResourceShape, since the shape itself is correctly
    // centered in the full rect; only the number needs to sit lower.
    public float LabelVerticalOffset { get; set; }

    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren)
        {
            return;
        }

        foreach (var child in GetChildren())
        {
            if (child is not Control control)
            {
                continue;
            }

            var rect = new Rect2(Vector2.Zero, Size);
            if (control is Label && LabelVerticalOffset != 0f)
            {
                rect.Position += new Vector2(0f, LabelVerticalOffset);
            }
            FitChildInRect(control, rect);
        }
    }
}

// Draws one resource-type shape filling its control's rect: triangle (Spike), square (Anvil),
// circle (Wheel). A custom _Draw rather than three separate node types (Polygon2D/ColorRect/arc)
// so every caller treats "a resource shape" as one Control regardless of which type it is.
//
// "Slightly 3D" (per-icon HD art request, no image-gen tool available this session -- see
// ResourceIconFactory's header) is faked in vector layers rather than a texture: a soft offset
// drop shadow, a real per-vertex color gradient across the shape's fill, and a thin, darker-still
// rim for edge definition. Accent axis fixed at the BOTTOM corner (not top, the usual highlight
// convention) purely so the gradient's darkest point sits away from Create's centered white cost
// number, keeping the number's background reliably dark.
//
// The gradient is a genuine fill gradient, not a smaller shape layered on top -- Godot's
// draw_colored_polygon takes one flat color for the whole polygon, but the plural DrawPolygon
// overload takes a Color[] matched 1:1 to its vertices and interpolates the fill between them, the
// same mechanism a Polygon2D's vertex colors use. An earlier version faked a gradient by drawing a
// second, smaller darkened shape in one corner on top of a flat base fill; pulled back far enough
// to not swallow the white number it instead read as an odd floating patch rather than shading, is
// what prompted this rewrite -- a real interpolated fill has no seam to look like a patch.
//
// Every tone is a DARKENING of the base Color, never a lightening (explicit standing request from
// an earlier round of this same file: a lightened patch read as flat/washed-out once it covered
// any real fraction of the shape, because lightening a saturated color desaturates it toward
// white). GradientDark sits at the accent corner, GradientLight is only a lighter DARKENING of the
// same base -- not the base color itself and never a lightening -- so the whole shape stays
// visibly shaded rather than having one flat "true color" region. RimColor is darker again, for
// edge definition against whatever sits behind the icon. Darkening goes through PerceptualDarken
// (HSV value, not raw RGB) rather than Color.Darkened, since a linear RGB darken crushes blue
// toward black well before red/green reach the same PERCEIVED darkness at the same fraction --
// see PerceptualDarken's own note. Same technique for all three shapes so they read as one
// consistent set. Scales with the control's own Size, so it stays crisp at every IconSize this
// factory defines instead of being a fixed-resolution raster.
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
        // Accent axis fixed at the bottom corner across every shape/size, so a row of mixed
        // icons (e.g. a move's cost badge next to a resource chip) reads as shaded consistently
        // rather than each shape picking its own direction -- see this class's header on why
        // bottom rather than the usual top-left highlight convention. Drop shadow falls opposite
        // this axis; the shade accent patch sits opposite it again (see DrawTriangle3D etc.).
        var lightDir = new Vector2(0.6f, 0.75f).Normalized();
        var center = size / 2f;

        switch (Type)
        {
            case ResourceType.Spike:
                DrawTriangle3D(center, extent, lightDir);
                break;
            case ResourceType.Anvil:
                DrawSquare3D(center, extent, lightDir);
                break;
            case ResourceType.Wheel:
                DrawCircle3D(center, extent / 2f, lightDir);
                break;
            default:
                DrawCircle3D(center, extent / 2f, lightDir);
                break;
        }
    }

    // Lightest / darkest stops of the gradient, both DARKENED off the base Color (never
    // lightened -- see this class's header) plus the rim tone, darker again. GradientLight sits
    // at the corner OPPOSITE the accent axis (away from Create's cost number), GradientDark at
    // the accent corner itself.
    //
    // PerceptualDarken, not Color.Darkened: Color.Darkened scales raw R/G/B linearly toward
    // black, which darkens by an equal RAW amount but not an equal PERCEIVED amount -- blue
    // contributes far less to perceived brightness than red or green at the same channel
    // magnitude (roughly the standard luma weighting, R*0.3 + G*0.59 + B*0.11), so WheelColor
    // (a saturated blue) started noticeably darker than SpikeColor/AnvilColor even before any
    // darken was applied, and the same 0.55/0.70 fractions that read as a subtle shade on
    // spike/anvil crushed wheel toward black -- visually indistinguishable from the drop shadow
    // sitting behind it. PerceptualDarken instead reduces HSV's V (value/brightness) channel,
    // which keeps hue and saturation intact and darkens by an amount that reads consistently
    // across every base color, including blue, rather than being tuned per-shape.
    private static Color PerceptualDarken(Color color, float amount)
    {
        var hsv = color;
        return Color.FromHsv(hsv.H, hsv.S, hsv.V * (1f - amount), hsv.A);
    }

    private Color GradientLight => PerceptualDarken(Color, 0.12f);
    private Color GradientDark => PerceptualDarken(Color, 0.55f);
    private Color RimColor => PerceptualDarken(Color, 0.70f);
    private static readonly Color DropShadowColor = new(0f, 0f, 0f, 0.35f);

    // Interpolates GradientLight -> GradientDark along lightDir, keyed off how far point p sits
    // from center in the accent direction relative to extent -- the single source both the
    // triangle/square vertex colors and the circle fan's per-vertex colors read from, so all
    // three shapes derive their gradient the same way instead of three hand-tuned versions.
    private Color GradientAt(Vector2 p, Vector2 center, Vector2 lightDir, float extent)
    {
        var t = Mathf.Clamp((p - center).Dot(lightDir) / extent + 0.5f, 0f, 1f);
        return GradientLight.Lerp(GradientDark, t);
    }

    private void DrawTriangle3D(Vector2 center, float extent, Vector2 lightDir)
    {
        var half = extent / 2f;
        Vector2[] Points(Vector2 c) =>
        [
            new Vector2(c.X, c.Y - half),
            new Vector2(c.X + half, c.Y + half),
            new Vector2(c.X - half, c.Y + half),
        ];

        var shadowOffset = -lightDir * extent * 0.08f;
        DrawColoredPolygon(Points(center + shadowOffset), DropShadowColor);

        var points = Points(center);
        var colors = points.Select(p => GradientAt(p, center, lightDir, extent)).ToArray();
        DrawPolygon(points, colors);

        DrawPolyline([.. points, points[0]], RimColor, Mathf.Max(1f, extent * 0.03f), antialiased: true);
    }

    private void DrawSquare3D(Vector2 center, float extent, Vector2 lightDir)
    {
        var half = extent / 2f;

        // A single hard-edged offset DrawRect (the original version of this method) reads as a
        // visible second rectangle, not a soft shadow -- unlike the triangle/circle, a square's
        // axis-aligned edges have no rotation or curvature to blur the duplicate silhouette
        // against, so the offset sliver on two sides shows as a crisp corner rather than
        // ambient shading. Faked here as a few progressively smaller, lower-alpha rects at
        // decreasing offsets instead of one flat one -- same "layered rings fake a blur" trick
        // already used for the specular highlight in an earlier version of this file, just
        // applied to the shadow instead. Reads as a soft falloff rather than a stacked outline.
        for (var i = 3; i >= 1; i--)
        {
            var t = i / 3f;
            var offset = -lightDir * extent * 0.08f * t;
            var inset = extent * 0.015f * (3 - i);
            var shadowRect = new Rect2(
                center.X - half + offset.X + inset, center.Y - half + offset.Y + inset,
                extent - inset * 2f, extent - inset * 2f);
            DrawRect(shadowRect, new Color(DropShadowColor.R, DropShadowColor.G, DropShadowColor.B, DropShadowColor.A * (0.5f + 0.5f * t) / 3f));
        }

        Vector2[] corners =
        [
            new Vector2(center.X - half, center.Y - half),
            new Vector2(center.X + half, center.Y - half),
            new Vector2(center.X + half, center.Y + half),
            new Vector2(center.X - half, center.Y + half),
        ];
        var colors = corners.Select(p => GradientAt(p, center, lightDir, extent)).ToArray();
        DrawPolygon(corners, colors);

        // Rim drawn slightly thicker than the triangle/circle's shared extent * 0.03 stroke --
        // a square's straight edges make a thin rim read as barely-there compared to the
        // triangle/circle, so it gets its own heavier weight rather than sharing the constant.
        var baseRect = new Rect2(center.X - half, center.Y - half, extent, extent);
        DrawRect(baseRect, RimColor, filled: false, width: Mathf.Max(1f, extent * 0.045f));
    }

    private void DrawCircle3D(Vector2 center, float radius, Vector2 lightDir)
    {
        var shadowOffset = -lightDir * radius * 0.16f;
        DrawCircle(center + shadowOffset, radius, DropShadowColor);

        // DrawCircle only takes one flat color, so a circle's gradient needs a manual triangle
        // fan -- but DrawPolygon triangulates its Vector2[] as the OUTLINE of one simple polygon,
        // not as fan topology. Handing it [center, ring0, ring1, ..., ringN] as a single call (an
        // earlier version of this code) makes it treat that as a 50-sided star-shaped boundary
        // walking out to the rim and back through every ring point in sequence, which
        // self-intersects and triangulates into overlapping garbage -- the "looks solid black"
        // bug this replaces. The fix is one DrawPolygon call PER TRIANGLE (center, ring[i],
        // ring[i+1]), each a real 3-vertex simple polygon DrawPolygon triangulates correctly, with
        // its own 3 gradient-sampled colors so the fan still reads as one continuous gradient
        // across all 48 wedges.
        const int segments = 48;
        var ring = new Vector2[segments + 1];
        var ringColors = new Color[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = i / (float)segments * Mathf.Tau;
            var p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            ring[i] = p;
            ringColors[i] = GradientAt(p, center, lightDir, radius * 2f);
        }
        var centerColor = GradientAt(center, center, lightDir, radius * 2f);
        for (var i = 0; i < segments; i++)
        {
            DrawPolygon(
                [center, ring[i], ring[i + 1]],
                [centerColor, ringColors[i], ringColors[i + 1]]);
        }

        DrawArc(center, radius, 0f, Mathf.Tau, 48, RimColor, Mathf.Max(1f, radius * 0.06f), antialiased: true);
    }
}
