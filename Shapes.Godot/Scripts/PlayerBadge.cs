using Godot;

namespace Shapes.Godot.Scripts;

// The floating avatar cluster on the right rail (DESIGN.md 5.C-UI, from references/game screen.png):
// a circular portrait with a ring and a health circle at its bottom-RIGHT.
//
// Drawn rather than composed from Controls, and deliberately NOT a child of the rail's panel: in
// the reference the cluster floats clear of the panel rather than sitting inside it. A Control
// laid out by the panel's container could not do that, so SidePanel anchors this above (opponent)
// or below (player) the rectangle and lets it draw outside.
//
// The health pip uses the same shaded-sphere recipe as ResourceIconFactory's circle -- radial
// gradient, drop shadow, darker rim, white text with a heavy outline -- so a health pip and a
// resource chip read as the same family of object.
//
// The reference also showed a second, blue "passive" satellite at the bottom-LEFT. That mechanic
// is not being implemented, so the circle is gone rather than drawn empty: an always-blank pip
// reads as a value that failed to load. Only the health satellite remains, which is why the
// geometry below speaks of one satellite rather than a symmetric pair.
public partial class PlayerBadge : Control
{
    // Diameter of the portrait, and of the health satellite that hangs off it.
    public const float AvatarDiameter = 112f;
    public const float SatelliteDiameter = 34f;

    // How far the satellite's centre sits from the avatar's, as a fraction of the avatar radius.
    // Just under 1.0 so it straddles the ring rather than floating free of it.
    private const float SatelliteOffset = 0.84f;

    private const float RingWidth = 4f;

    private static readonly Color AvatarFill = new("4a5668");
    private static readonly Color RingColor = new("c9a677");
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

    // The portrait art, or null to fall back to the flat fill. Assigned once per match by
    // BoardView (see AvatarPicker) rather than per Render: Render runs on every hover and
    // targeting refresh, and re-picking there would reshuffle the face mid-game.
    public Texture2D? Portrait
    {
        get => _portrait;
        set
        {
            if (_portrait == value)
            {
                return;
            }

            _portrait = value;
            QueueRedraw();
        }
    }

    private Texture2D? _portrait;

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

    // Sized so the health satellite, which hangs past the avatar's edge, is inside our rect --
    // otherwise the parent could clip it. Kept symmetric (the same span on both axes) even though
    // only the bottom-right pip survives the passive circle's removal: SidePanel.PlaceBadge
    // centres this rect horizontally over the panel, so a rect that hugged the pip's side would
    // shift the AVATAR off-centre to make room for it.
    public override Vector2 _GetMinimumSize()
    {
        var span = AvatarDiameter + SatelliteDiameter;
        return new Vector2(span, span);
    }

    // Where the drawn circles actually start and end inside the rect, as distances from the
    // rect's top and bottom edges.
    //
    // The cluster is NOT vertically symmetric: the avatar is centred in the rect, but the health
    // satellite hangs below it and may reach lower than the avatar does. Both insets are derived
    // from the SAME geometry _Draw uses rather than being written out separately -- deriving them
    // independently is what previously left the bottom badge sitting 11px inside its panel while
    // the top one cleared its own by 3px.
    private static float RectHalf => (AvatarDiameter + SatelliteDiameter) / 2f;

    // How far below the rect's centre the satellite's lowest point falls. It sits at 45 degrees,
    // so its centre drops by reach/sqrt(2) and its own radius extends past that.
    private static float SatelliteBottom =>
        AvatarDiameter / 2f * SatelliteOffset * (Mathf.Sqrt(2f) / 2f) + SatelliteDiameter / 2f;

    // The visible cluster's top is always the avatar (nothing is drawn above it); its bottom is
    // whichever reaches lower, the avatar or the satellite.
    public static float TopInset => RectHalf - AvatarDiameter / 2f;

    public static float BottomInset =>
        RectHalf - Mathf.Max(AvatarDiameter / 2f, SatelliteBottom);

    public override void _Draw()
    {
        var avatarRadius = AvatarDiameter / 2f;
        var satelliteRadius = SatelliteDiameter / 2f;
        var centre = Size / 2f;

        // The fill is drawn even when there is art, so a portrait with transparent corners sits on
        // the same base the placeholder uses rather than on whatever is behind the badge.
        DrawCircle(centre, avatarRadius, AvatarFill);
        DrawPortrait(centre, avatarRadius);
        DrawArc(centre, avatarRadius, 0f, Mathf.Tau, 64, RingColor, RingWidth, antialiased: true);

        // 45 degrees below-right of centre, on the ring.
        var reach = avatarRadius * SatelliteOffset;
        var diagonal = Mathf.Sqrt(2f) / 2f;
        var healthCentre = centre + new Vector2(reach * diagonal, reach * diagonal);

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

    // The portrait, cropped to a circle.
    //
    // Godot's immediate-mode drawing has no circular clip, and a Control's ClipContents clips to
    // the RECT, not a shape -- so the mask is built the same way DrawSphere builds its gradient:
    // a triangle fan around the circle, with UVs computed per vertex so the texture is sampled
    // only inside it. DrawTextureRect plus a clip would have been simpler but would square off
    // the portrait's corners over the ring.
    //
    // The source art is authored 2:1 with the subject in the centred square (see CardArt's note
    // on KeepAspectCovered), so the fan samples that centred square rather than the full width --
    // sampling the whole 2:1 frame into a circle would squash every face horizontally.
    private void DrawPortrait(Vector2 centre, float radius)
    {
        if (_portrait is null)
        {
            return;
        }

        var size = _portrait.GetSize();
        if (size.X <= 0f || size.Y <= 0f)
        {
            return;
        }

        // The centred square, in UV (0..1) terms: the shorter side is used whole, the longer one
        // is centred and cropped. Written for both axes rather than assuming landscape so a
        // square or portrait source is handled too.
        var side = Mathf.Min(size.X, size.Y);
        var halfU = side / size.X / 2f;
        var halfV = side / size.Y / 2f;

        // The rim OUTLINE only -- no centre vertex, and no repeated vertex to close the loop.
        // DrawPolygon triangulates the outline it is given rather than reading it as a triangle
        // fan (which is what DrawSphere above builds, one DrawPolygon call per triangle), so a
        // centre point would be an interior vertex and a duplicated seam point (angle 0 and Tau
        // are the same place) would be a zero-area edge. Godot rejects the whole polygon for
        // either -- "Invalid polygon data, triangulation failed" -- and draws nothing.
        const int segments = 64;
        var points = new Vector2[segments];
        var uvs = new Vector2[segments];

        for (var i = 0; i < segments; i++)
        {
            var angle = i / (float)segments * Mathf.Tau;
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            points[i] = centre + offset * radius;

            // The unit offset doubles as the UV offset: a point on the circle's rim maps to the
            // rim of the cropped square, so the square's inscribed circle is what shows.
            uvs[i] = new Vector2(0.5f, 0.5f) + new Vector2(offset.X * halfU, offset.Y * halfV);
        }

        // Explicit arrays rather than collection expressions: DrawPolygon has both an array and a
        // ReadOnlySpan overload, and a target-typed `[...]` cannot choose between them.
        DrawPolygon(points, new[] { Colors.White }, uvs, _portrait);
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
