using Godot;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Godot.Scripts;

// The rock-paper-scissors cycle, always visible in the screen's top-left corner
// (references/type charts.jpg, the upper of the two drafts).
//
// Three type shapes on the points of a triangle, joined by curved arrows running CLOCKWISE from
// each attacker to the type it deals 2x to. Beside each attacker sits a smaller shape: the
// DUAL-TYPE case, which is the one rule a player cannot discover from the board -- a wheel deals
// 2x to a plain anvil, and also to an anvil/wheel, but NOT to an anvil/spike. See
// TypeChart.IsDoubled: a multi-type target takes the bonus only when it also carries the
// attacker's own type, which is exactly why each small pair reads "target/attacker".
//
// The cycle is READ FROM THE RULESET rather than drawn from three hardcoded arrows. A chart that
// hardcoded Wheel->Anvil would keep claiming it after a balance sweep retuned the cycle (the
// whole point of TypeChart taking its cycle as data), and a type chart that disagrees with the
// damage code is worse than none at all -- it is confidently wrong in the one place a player
// goes to resolve a doubt.
//
// Drawn in one _Draw rather than composed from Controls: the arrows are curves between shape
// centres, so every arc's geometry depends on where the other two shapes landed. A container
// laying out three icons would own those positions and leave nothing able to draw between them.
// The three type shapes themselves ARE child Controls (ResourceShape), so the diagram's shapes
// are the same geometry, palette and shading as every cost badge on screen rather than a second
// hand-drawn set that could drift from it.
public partial class TypeCycleChart : Control
{
    // Overall footprint, including the enclosing panel's padding. Big enough that the small
    // dual-type shapes stay legible, small enough to sit in the corner without competing with
    // the board.
    //
    // 164 -> 140 to free room in the left column (DESIGN.md D2). That column stacks four things
    // between the top of the screen and the hand: this chart, the action recap, the hover tooltip,
    // and the tooltip's keyword explainer stack -- and the worst case (a played-card recap above a
    // hovered Guardian, whose two moves grant reflect and stun) did not fit. Shrinking the chart is
    // the right giver: it is static reference material a player consults occasionally, whereas the
    // other three are live and change with the game. Verified by measuring all four rects in a
    // windowed run, not by eye.
    private const float ChartSize = 140f;

    // Padding between the panel's edge and the diagram inside it.
    private const float PanelPadding = 10f;

    // Diameter of the three main type shapes, and of the small dual-type ones beside them. Scaled
    // with ChartSize (were 40 and 19 at 164) so the diagram keeps its proportions at the smaller
    // panel rather than the shapes crowding each other.
    private const float ShapeDiameter = 34f;
    private const float SmallShapeDiameter = 16f;

    // Per-type size correction, so the three shapes carry equal VISUAL weight.
    //
    // ResourceShape fills its rect differently per shape: a square spans the full extent, while a
    // circle is inscribed in it and a triangle inscribed again inside that. Handed identical
    // rects, the square therefore covers far more area than the triangle. Correcting here rather
    // than in ResourceShape because that class backs every cost badge on screen, where the shapes
    // sit in separate badges and the disparity never shows; it only matters when all three are
    // side by side, which is this diagram's whole premise.
    //
    // These are NOT the raw area ratios. Equalising area over-corrects the triangle: its wide
    // base and sharp apex make it read bigger than an equal-area circle, so scaling it up to
    // match on area left it visibly dominating the other two. Tuned by eye against the rendered
    // diagram instead, which is why spike sits slightly BELOW parity rather than above it.
    private static float ScaleOf(ResourceType type) => type switch
    {
        ResourceType.Anvil => 0.84f,
        ResourceType.Spike => 0.94f,
        _ => 1f,
    };

    // How far the three main shapes sit from the chart's centre. Scaled with ChartSize (was 50 at
    // 164) so shrinking the panel moves the diagram in rather than clipping it against the edges.
    private const float OrbitRadius = 43f;

    // How far the arrows are pushed outward from the straight line between two shapes. The draft
    // bows them away from the centre, which is also what keeps an arc clear of the dual-type
    // shapes sitting inside the triangle.
    private const float ArcBulge = 0.30f;

    // Gap left between a shape's edge and the arrow's tip/tail, so an arrow points AT a shape
    // rather than touching it. Measured from the shape's bounding radius, which for a triangle
    // or circle is larger than the drawn silhouette -- so a gap that looks right against the
    // square looks loose against the other two. Kept small for that reason.
    private const float ArrowGap = 5f;

    private const float ArrowWidth = 3.5f;
    private const float ArrowHeadLength = 13f;
    private const float ArrowHeadWidth = 11f;

    private static readonly Color ArrowColor = new("e8ecf2");
    private static readonly Color LabelColor = new("d6dae0");

    // The same fill/edge SidePanel gives the rail's two blocks, so the chart reads as another
    // piece of the same furniture rather than a differently-styled panel in the corner.
    private static readonly Color PanelFill = new("2b3138");
    private static readonly Color PanelEdge = new("11151a");

    // The ruleset this chart is describing. Set by BoardView from the live GameState, so the
    // arrows always match the rules actually in play.
    private TypeChart _chart = TypeChart.Default;

    private readonly Dictionary<ResourceType, ResourceShape> _shapes = [];
    private readonly Dictionary<ResourceType, ResourceShape> _dualShapes = [];
    private Label? _multiplierLabel;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(ChartSize, ChartSize);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildShapes();
        Resized += QueueRedraw;
    }

    // The multiplier is read off the chart too (not the literal "2x" of the draft): the draft was
    // drawn against the shipping WeaknessMultiplier of 2.0, and a sweep that changes it must not
    // leave a caption claiming the old value.
    public void SetChart(TypeChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        if (ReferenceEquals(_chart, chart))
        {
            return;
        }

        _chart = chart;
        UpdateDualShapes();
        UpdateMultiplierLabel();
        QueueRedraw();
    }

    private void BuildShapes()
    {
        foreach (var type in ResourceTypes.All)
        {
            _shapes[type] = AddShape(type, ShapeDiameter);

            // One small shape per attacker, showing the dual-type target it still doubles. Its
            // TYPE is set in UpdateDualShapes, since which type that is comes from the cycle.
            _dualShapes[type] = AddShape(type, SmallShapeDiameter);
        }

        // Names the relationship once, so the arrows do not have to be interpreted. Without it a
        // player has no way to tell whether an arrow points at what beats me or what I beat --
        // the single highest-value addition to the draft.
        _multiplierLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _multiplierLabel.AddThemeColorOverride("font_color", LabelColor);
        _multiplierLabel.AddThemeFontSizeOverride("font_size", 13);
        _multiplierLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _multiplierLabel.AddThemeConstantOverride("outline_size", 4);
        AddChild(_multiplierLabel);

        UpdateDualShapes();
        UpdateMultiplierLabel();
    }

    private ResourceShape AddShape(ResourceType type, float diameter)
    {
        var shape = new ResourceShape
        {
            Type = type,
            Color = ResourceIconFactory.ColorOf(type),
            MouseFilter = MouseFilterEnum.Ignore,
            Size = new Vector2(diameter, diameter),

            // Near-full fill: these are diagram glyphs, not badges holding a number, so the
            // breathing room a cost badge wants only makes them read smaller than they are.
            MinFillRatio = 0.95f,
        };

        AddChild(shape);
        return shape;
    }

    // The small shape beside each big one is the type that BEATS it -- its predecessor in the
    // cycle, matching the draft (a small triangle beside the circle, since spike beats wheel).
    //
    // Read as the dual-type rule from the perspective of the shape it sits on: a wheel creature
    // takes double from spike, and a WHEEL/SPIKE creature still does. That is TypeChart.IsDoubled
    // -- a multi-type target keeps the weakness only while it also carries the attacker's type --
    // so the pair is "me, plus the type that beats me", which is exactly this small shape.
    //
    // Note this is the inverse of Beats: iterating attackers and asking what each one beats would
    // put a shape beside the WRONG type (the small triangle would land next to the square).
    private void UpdateDualShapes()
    {
        foreach (var attacker in ResourceTypes.All)
        {
            // attacker beats target, so target's small shape is the attacker.
            var target = _chart.Beats(attacker);
            var shape = _dualShapes[target];
            shape.Type = attacker;
            shape.Color = ResourceIconFactory.ColorOf(attacker);
            shape.QueueRedraw();
        }
    }

    private void UpdateMultiplierLabel()
    {
        if (_multiplierLabel is null)
        {
            return;
        }

        // Trimmed so the shipping 2.0 reads "2x" like the draft, while a swept 1.5 still reads
        // correctly rather than being rounded away to the wrong number.
        var multiplier = _chart.WeaknessMultiplier;
        var text = multiplier == Mathf.Round(multiplier)
            ? ((int)multiplier).ToString()
            : multiplier.ToString("0.##");

        _multiplierLabel.Text = $"deals {text}x to";
    }

    // Where each type's main shape sits: the three points of an upright triangle, with the first
    // type at the top. Angles run CLOCKWISE from there, matching the draft's arrow direction --
    // Godot's Y axis points down, so a clockwise-on-screen sweep is an INCREASING angle here.
    private Vector2 CentreOf(ResourceType type)
    {
        var order = CycleOrder();
        var index = order.IndexOf(type);
        var angle = -Mathf.Pi / 2f + index * Mathf.Tau / 3f;
        return DiagramCentre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * OrbitRadius;
    }

    // The diagram's own centre: the middle of the space left once the panel's padding and the
    // caption's band at the bottom are taken out. Centring on the full rect instead would push
    // the triangle down onto the caption.
    private Vector2 DiagramCentre =>
        new(Size.X / 2f, PanelPadding + (Size.Y - PanelPadding * 2f - CaptionBand) / 2f);

    private const float CaptionBand = 18f;

    // The three types walked in cycle order (each beating the next), so the arrows drawn between
    // consecutive entries are exactly the cycle's edges and the layout cannot disagree with the
    // arrows. Derived from the chart rather than from ResourceTypes.All, whose declaration order
    // has nothing to do with what beats what.
    private List<ResourceType> CycleOrder()
    {
        var order = new List<ResourceType>(ResourceTypes.Count) { ResourceTypes.All[0] };
        for (var i = 1; i < ResourceTypes.Count; i++)
        {
            order.Add(_chart.Beats(order[^1]));
        }

        return order;
    }

    public override void _Draw()
    {
        // The enclosing panel, drawn first so everything else lands on top of it. Same rounded
        // box CardStyle gives the rail's panels and every card on screen, so the corner radius
        // and border weight cannot drift from theirs.
        DrawStyleBox(CardStyle.Box(PanelFill, PanelEdge), new Rect2(Vector2.Zero, Size));

        var order = CycleOrder();

        // Arrows next, so a shape always sits on top of the line rather than the line crossing
        // its fill.
        foreach (var attacker in order)
        {
            DrawCycleArrow(CentreOf(attacker), CentreOf(_chart.Beats(attacker)));
        }

        PlaceShapes(order);
    }

    // One arrow from attacker to target, bowed away from the chart's centre.
    private void DrawCycleArrow(Vector2 from, Vector2 to)
    {
        var centre = DiagramCentre;
        var chord = to - from;
        var midpoint = from + chord / 2f;

        // Bow outward: push the curve's control point away from the chart's centre, so all three
        // arcs bulge outward as a set instead of one crossing the middle.
        var outward = (midpoint - centre).Normalized();
        if (outward == Vector2.Zero)
        {
            outward = chord.Orthogonal().Normalized();
        }

        var control = midpoint + outward * chord.Length() * ArcBulge;

        // Trimmed at both ends so the arc starts and stops clear of the two shapes. Solved by
        // walking the curve rather than by shortening the chord: the arc is not straight, so a
        // straight-line inset would leave the tip visibly off the curve.
        //
        // tipT is where the arrow POINTS (just off the target shape); lineEndT is where the drawn
        // line stops, one head-length short of it, so the line does not run out past the head.
        var startT = TrimFromStart(from, control, to, ShapeDiameter / 2f + ArrowGap);
        var tipT = TrimFromEnd(from, control, to, ShapeDiameter / 2f + ArrowGap);
        var lineEndT = TrimFromEnd(from, control, to, ShapeDiameter / 2f + ArrowGap + ArrowHeadLength);

        DrawCurve(from, control, to, startT, lineEndT);

        // Aimed along the curve's own direction of travel at the tip: sampled from a point just
        // BEFORE the tip toward the tip, so the head follows the arc rather than the straight
        // chord. Taking this difference in the wrong order (or against a non-adjacent point) is
        // what previously splayed all three heads outward instead of around the cycle.
        var tip = QuadraticAt(from, control, to, tipT);
        var justBefore = QuadraticAt(from, control, to, Mathf.Max(startT, tipT - 0.06f));
        DrawArrowHead(tip, (tip - justBefore).Normalized());
    }

    // The curve as a polyline. Godot's DrawPolyline takes straight segments, so the arc is
    // flattened into enough of them to read as smooth at this size.
    private void DrawCurve(Vector2 from, Vector2 control, Vector2 to, float startT, float endT)
    {
        const int segments = 24;
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = Mathf.Lerp(startT, endT, i / (float)segments);
            points[i] = QuadraticAt(from, control, to, t);
        }

        DrawPolyline(points, ArrowColor, ArrowWidth, antialiased: true);
    }

    private static Vector2 QuadraticAt(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        var inverse = 1f - t;
        return (inverse * inverse * a) + (2f * inverse * t * b) + (t * t * c);
    }

    // How far along the curve to start/stop, as a t value, so the given distance is cleared from
    // the corresponding endpoint. Walks the curve in small steps -- a quadratic's arc length has
    // no closed form worth solving here, and the sample count is tiny.
    private static float TrimFromStart(Vector2 a, Vector2 b, Vector2 c, float distance)
    {
        const int steps = 40;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            if (QuadraticAt(a, b, c, t).DistanceTo(a) >= distance)
            {
                return t;
            }
        }

        return 0f;
    }

    private static float TrimFromEnd(Vector2 a, Vector2 b, Vector2 c, float distance)
    {
        const int steps = 40;
        for (var i = steps; i >= 0; i--)
        {
            var t = i / (float)steps;
            if (QuadraticAt(a, b, c, t).DistanceTo(c) >= distance)
            {
                return t;
            }
        }

        return 1f;
    }

    private void DrawArrowHead(Vector2 tip, Vector2 direction)
    {
        if (direction == Vector2.Zero)
        {
            return;
        }

        var back = tip - direction * ArrowHeadLength;
        var side = direction.Orthogonal() * (ArrowHeadWidth / 2f);
        DrawColoredPolygon([tip, back + side, back - side], ArrowColor);
    }

    // The shapes are child Controls, so they are POSITIONED here rather than drawn -- _Draw is
    // where the triangle's geometry is known. Centred on their points; the dual-type shape sits
    // just inside the triangle from its attacker, as in the draft.
    private void PlaceShapes(List<ResourceType> order)
    {
        var centre = DiagramCentre;

        foreach (var type in order)
        {
            var point = CentreOf(type);
            var shape = _shapes[type];
            var diameter = ShapeDiameter * ScaleOf(type);
            shape.Size = new Vector2(diameter, diameter);
            shape.Position = point - shape.Size / 2f;

            // The small shape sits INSIDE the triangle from the big one, as in the draft. Its own
            // scale correction is applied too, or a small square would out-weigh a small circle
            // exactly as the big ones did.
            var inward = (centre - point).Normalized();
            var dual = _dualShapes[type];
            var dualDiameter = SmallShapeDiameter * ScaleOf(dual.Type);
            dual.Size = new Vector2(dualDiameter, dualDiameter);
            dual.Position = point
                + inward * (ShapeDiameter / 2f + SmallShapeDiameter / 2f + 4f)
                - dual.Size / 2f;
        }

        // BELOW the diagram, not in its middle: the three dual-type shapes sit inside the
        // triangle and a centred caption lands on top of them. Pinned just inside the panel's
        // bottom padding rather than to the rect's edge, which is what closes the gap that
        // previously left it floating well clear of the shapes above it.
        if (_multiplierLabel is not null)
        {
            var size = _multiplierLabel.GetCombinedMinimumSize();
            _multiplierLabel.Size = size;
            _multiplierLabel.Position = new Vector2(
                centre.X - size.X / 2f,
                Size.Y - PanelPadding - size.Y);
        }
    }
}
