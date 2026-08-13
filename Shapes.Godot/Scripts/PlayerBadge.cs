using Godot;

namespace Shapes.Godot.Scripts;

// The floating avatar cluster on the right rail (PLAN.md 5.C-UI, from references/game screen.png):
// a circular portrait with a ring, a small passive circle at its bottom-LEFT, and a health circle
// at its bottom-RIGHT.
//
// Drawn rather than composed from Controls, and deliberately NOT a child of the rail's panel: in
// the reference the cluster floats clear of the panel rather than sitting inside it. A Control
// laid out by the panel's container could not do that, so SidePanel anchors this above (opponent)
// or below (player) the rectangle and lets it draw outside.
//
// The satellites use the same shaded-sphere recipe as ResourceIconFactory's circle -- radial
// gradient, drop shadow, darker rim, white text with a heavy outline -- so a health pip and a
// resource chip read as the same family of object. Avatar art does not exist yet; a flat fill
// plus a ring stands in, and swapping it for a real portrait is a texture draw in _Draw.
public partial class PlayerBadge : Control
{
    // Diameter of the portrait, and of the two satellite circles that hang off it.
    public const float AvatarDiameter = 112f;
    public const float SatelliteDiameter = 34f;

    // How far the satellites' centres sit from the avatar's, as a fraction of the avatar radius.
    // Just past 1.0 so they straddle the ring rather than floating free of it.
    private const float SatelliteOffset = 0.84f;

    private const float RingWidth = 4f;

    private static readonly Color AvatarFill = new("4a5668");
    private static readonly Color RingColor = new("c9a677");
    private static readonly Color PassiveColor = new("2f6fd0");
    private static readonly Color HealthColor = new("c0392b");
    private static readonly Color DropShadowColor = new(0f, 0f, 0f, 0.35f);

    // Light comes from the upper left, matching ResourceIconFactory's own accent direction so
    // every shaded pip on screen is lit from the same place.
    private static readonly Vector2 LightDirection = new Vector2(-0.55f, -0.83f).Normalized();

    private int _health;
    private Label? _healthLabel;

    // Null until the first SetValues, so the opening deal never animates as "health just
    // changed" -- the same guard SidePanel uses for its resource totals.
    private int? _lastHealth;

    // How far the health pip swells (a gain) or shrinks (a loss), and for how long. Losing is
    // drawn as the opposite gesture rather than the same one in another colour, matching the
    // resource chips' spend animation.
    private const float HealthPulseSeconds = 0.42f;
    private const float HealthGainScale = 1.4f;
    private const float HealthLossScale = 0.68f;

    // Drawn scale for the health satellite, driven by the pulse tween below. A plain float rather
    // than a Control property because the satellite is drawn in _Draw, not composed from nodes --
    // there is no child to tween, so the tween drives this and _Draw reads it.
    private float _healthScale = 1f;

    // The reference shows a "passive" here; this game has no passive mechanic, so the circle is
    // drawn empty to hold the layout until something earns the slot.
    public void SetValues(int health)
    {
        var previous = _lastHealth;
        _health = health;
        _lastHealth = health;

        EnsureLabel();
        _healthLabel!.Text = health.ToString();
        QueueRedraw();

        if (previous is { } prev && prev != health)
        {
            PulseHealth(gained: health > prev);
        }
    }

    // Scale bounce on the pip itself. Deliberately no floating number here: BoardAnimator already
    // flies a "-N" to this badge for every point of health lost (see SidePanel.HealthCueRect), so
    // adding a second number would double-report the same event.
    private void PulseHealth(bool gained)
    {
        var peak = gained ? HealthGainScale : HealthLossScale;

        var tween = CreateTween();
        tween.TweenMethod(
                Callable.From<float>(SetHealthScale), 1f, peak, HealthPulseSeconds * 0.35f)
            .SetEase(Tween.EaseType.Out);
        tween.TweenMethod(
                Callable.From<float>(SetHealthScale), peak, 1f, HealthPulseSeconds * 0.65f)
            .SetEase(Tween.EaseType.In);
    }

    private void SetHealthScale(float scale)
    {
        _healthScale = scale;
        QueueRedraw();
    }

    // A real Label rather than DrawString: it picks up the same font, outline and centring the
    // resource chips use, instead of this file hand-measuring a baseline.
    private void EnsureLabel()
    {
        if (_healthLabel is not null)
        {
            return;
        }

        _healthLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _healthLabel.AddThemeColorOverride("font_color", Colors.White);
        _healthLabel.AddThemeConstantOverride("outline_size", (int)(SatelliteDiameter * 0.14f));
        _healthLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _healthLabel.AddThemeFontSizeOverride("font_size", (int)(SatelliteDiameter * 0.5f));
        AddChild(_healthLabel);
    }

    // Sized so the satellites, which hang past the avatar on both sides, are inside our rect --
    // otherwise the parent could clip them.
    public override Vector2 _GetMinimumSize()
    {
        var span = AvatarDiameter + SatelliteDiameter;
        return new Vector2(span, span);
    }

    // Where the drawn circles actually start and end inside the rect, as distances from the
    // rect's top and bottom edges.
    //
    // The cluster is NOT vertically symmetric: the avatar is centred in the rect, but the two
    // satellites hang below it and may reach lower than the avatar does. Both insets are derived
    // from the SAME geometry _Draw uses rather than being written out separately -- deriving them
    // independently is what previously left the bottom badge sitting 11px inside its panel while
    // the top one cleared its own by 3px.
    private static float RectHalf => (AvatarDiameter + SatelliteDiameter) / 2f;

    // How far below the rect's centre the satellites' lowest point falls. They sit at 45 degrees,
    // so their centre drops by reach/sqrt(2) and their own radius extends past that.
    private static float SatelliteBottom =>
        AvatarDiameter / 2f * SatelliteOffset * (Mathf.Sqrt(2f) / 2f) + SatelliteDiameter / 2f;

    // The visible cluster's top is always the avatar (nothing is drawn above it); its bottom is
    // whichever reaches lower, the avatar or the satellites.
    public static float TopInset => RectHalf - AvatarDiameter / 2f;

    public static float BottomInset =>
        RectHalf - Mathf.Max(AvatarDiameter / 2f, SatelliteBottom);

    public override void _Draw()
    {
        var avatarRadius = AvatarDiameter / 2f;
        var satelliteRadius = SatelliteDiameter / 2f;
        var centre = Size / 2f;

        DrawCircle(centre, avatarRadius, AvatarFill);
        DrawArc(centre, avatarRadius, 0f, Mathf.Tau, 64, RingColor, RingWidth, antialiased: true);

        // 45 degrees below-left and below-right of centre, on the ring.
        var reach = avatarRadius * SatelliteOffset;
        var diagonal = Mathf.Sqrt(2f) / 2f;
        var passiveCentre = centre + new Vector2(-reach * diagonal, reach * diagonal);
        var healthCentre = centre + new Vector2(reach * diagonal, reach * diagonal);

        DrawSphere(passiveCentre, satelliteRadius, PassiveColor);

        // Only the health pip scales -- the pulse is about health changing, so swelling the whole
        // cluster would blur which value moved.
        DrawSphere(healthCentre, satelliteRadius * _healthScale, HealthColor);

        // The label is a child Control, so it is positioned rather than drawn -- centred on the
        // health sphere here because _Draw is where that sphere's centre is known. Scaled with
        // the pip so the number rides the pulse instead of floating at a fixed size over a
        // shrinking circle.
        if (_healthLabel is not null)
        {
            var size = _healthLabel.GetCombinedMinimumSize();
            _healthLabel.Size = size;
            _healthLabel.PivotOffset = size / 2f;
            _healthLabel.Scale = new Vector2(_healthScale, _healthScale);
            _healthLabel.Position = healthCentre - size / 2f;
        }
    }

    // ResourceIconFactory.DrawCircle3D's recipe: offset drop shadow, a triangle-fan radial
    // gradient (DrawCircle takes only one flat colour, so a gradient needs the fan), then a
    // darker rim. Duplicated rather than shared because that method is a private member of a
    // Control subclass built per icon -- extracting it would mean reshaping that whole file for
    // one caller, and this variant has no number-offset or size-flag machinery to carry.
    private void DrawSphere(Vector2 centre, float radius, Color color)
    {
        DrawCircle(centre - LightDirection * radius * 0.16f, radius, DropShadowColor);

        var light = PerceptualDarken(color, 0.12f);
        var dark = PerceptualDarken(color, 0.55f);
        var rim = PerceptualDarken(color, 0.70f);

        const int segments = 40;
        var ring = new Vector2[segments + 1];
        var ringColors = new Color[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = i / (float)segments * Mathf.Tau;
            var p = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            ring[i] = p;
            ringColors[i] = GradientAt(p, centre, radius * 2f, light, dark);
        }

        var centreColor = GradientAt(centre, centre, radius * 2f, light, dark);
        for (var i = 0; i < segments; i++)
        {
            DrawPolygon(
                [centre, ring[i], ring[i + 1]],
                [centreColor, ringColors[i], ringColors[i + 1]]);
        }

        DrawArc(centre, radius, 0f, Mathf.Tau, 40, rim, Mathf.Max(1f, radius * 0.06f), antialiased: true);
    }

    private static Color GradientAt(Vector2 p, Vector2 centre, float extent, Color light, Color dark)
    {
        var t = Mathf.Clamp((p - centre).Dot(LightDirection) / extent + 0.5f, 0f, 1f);
        return light.Lerp(dark, t);
    }

    // Reduces HSV value rather than scaling RGB, so a saturated blue darkens by the same
    // PERCEIVED amount as a red -- see ResourceIconFactory.PerceptualDarken's note.
    private static Color PerceptualDarken(Color color, float amount) =>
        Color.FromHsv(color.H, color.S, color.V * (1f - amount), color.A);
}
