using Godot;

namespace Shapes.Godot.Scripts;

// The full-bleed art behind the home/menu screens (DESIGN.md D3 phase 3), in the shape a game menu
// usually takes -- see references/mc_homescreen.png: one photographic scene covering the whole
// window, with the controls as a narrow column floating over it.
//
// COVER, NOT STRETCH. The art is 1408x768 (1.83:1) and the window is 1600x1000 (1.6:1) and resizable
// besides, so fitting it to the rect would distort a rendered scene -- the one kind of image where
// wrong proportions are immediately obvious. This scales to the larger axis and lets the overflow
// crop, the same rule a CSS `background-size: cover` applies. TextureRect could do this with
// KeepAspectCovered, but only inside a rect it is given; drawing it here means the crop is computed
// against the live window size on every resize with no layout pass involved.
//
// SCRIM ON TOP, and it is what makes the screen usable rather than merely decorated. Menu text over
// bare art is legible only where the art happens to be dark, and this piece runs from near-black
// stone to bright water. A vertical wash plus a soft centre pool buys uniform contrast for the
// button column without flattening the picture -- the same job Minecraft's own panel does, done
// with a gradient instead of a texture.
public partial class MenuBackdrop : Control
{
    // Which file to draw. An [Export] so the three drafts in Art/backgrounds can be swapped from
    // the inspector without a rebuild -- picking between them is a taste call, and taste calls want
    // to be made by looking.
    [Export] public Texture2D? Background { get; set; }

    // Extra darkening applied on top of the scrim, 0 = none. Exported so the same node can serve
    // two screens with very different needs: a menu wants the art readable as art, while the game
    // screen wants it pushed right back so it reads as a room the board sits in and never competes
    // with the cards. Tuning this rather than shipping a second pre-darkened image keeps one source
    // of truth for the artwork.
    [Export] public float ExtraDim { get; set; }

    // Whether the art drifts. Off for the game screen: a board is read closely and for a long time,
    // and motion behind it is a distraction rather than atmosphere -- the opposite of a menu, where
    // the player is idle and the drift is the point.
    [Export] public bool Panning { get; set; } = true;

    // Overall darkening, strongest at the edges. Keeps the frame from competing with the controls
    // and gives the piece a vignette it does not have on its own.
    //
    // The TOP carries the most, which is the opposite of the usual arrangement: this art puts a
    // large bright sky across its upper third, and that is exactly where the title sits. Light text
    // on a pale sky is the one combination the layout cannot afford.
    private static readonly Color ScrimTop = new(0.03f, 0.04f, 0.06f, 0.72f);
    private static readonly Color ScrimMid = new(0.04f, 0.05f, 0.07f, 0.34f);
    private static readonly Color ScrimBottom = new(0.03f, 0.04f, 0.05f, 0.66f);

    // Extra darkening behind the title and button column, as a soft vertical pool. Without it the
    // controls sit over the brightest parts of this image (sky above, ice and lava below), which is
    // exactly where a translucent control loses its edges.
    private static readonly Color CentrePool = new(0.02f, 0.03f, 0.04f, 0.40f);

    // Where the pool sits as a fraction of window height. Starts high enough to cover the title and
    // runs past the last button, so the whole menu block sits in one continuous pocket of shade
    // rather than each control needing its own.
    private const float PoolTopFraction = 0.10f;
    private const float PoolHeightFraction = 0.78f;

    // A horizontal falloff either side of the menu column, so the pool does not read as a band
    // stretching the full width of the screen.
    private const float PoolSideFraction = 0.38f;

    // Mesh resolution. Enough that the smoothstepped weights are sampled finely, few enough that
    // the whole backdrop is a few dozen draw calls -- and unlike the earlier banded ramp, the
    // smoothness does not DEPEND on these being high, because the falloff has zero derivative at
    // both ends either way.
    private const int ScrimRows = 12;
    private const int PoolRows = 10;
    private const int PoolColumns = 10;

    // ---- The idle pan (DESIGN.md D3 phase 3). ---------------------------------------------------
    //
    // How far past a plain cover fit the art is scaled. Deliberately more headroom than the motion strictly needs: the
    // amplitude below spends only part of it, so the art never reaches its own edge even at an
    // aspect ratio this was not tuned against. The 10000px source means even zoomed and capped at
    // 2560 on import, this is still supersampling rather than magnifying.
    private const float PanZoom = 1.18f;

    // Fraction of the available slack actually used. Well under 1 so the crop edge stays off screen
    // with room to spare -- the failure this guards against is the art sliding far enough to reveal
    // the viewport behind it, which is unrecoverable-looking rather than merely wrong.
    private const float PanAmplitude = 0.75f;

    // Seconds for a full there-and-back cycle. Long: this is the screen a player leaves sitting
    // open, and motion that completes quickly enough to notice becomes motion that repeats often
    // enough to irritate. At 120s the drift is under 3px/second, which reads as the scene breathing
    // rather than as the background moving.
    private const double PanCycleSeconds = 120.0;

    // Ceiling on one-way travel, as a fraction of viewport width -- see DrawCover on why the
    // proportional rule alone is not enough at portrait aspect ratios. 5% of width keeps the drift
    // at a few pixels per second on every display this is likely to meet.
    private const float PanMaxTravelFraction = 0.05f;

    private double _elapsed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _Process(double delta)
    {
        if (Background is null || !Panning || !IsVisibleInTree())
        {
            return;
        }

        // Wrapped rather than left to grow: this runs for as long as the menu is open, and an
        // ever-increasing float eventually loses the precision that keeps the motion smooth.
        _elapsed = (_elapsed + delta) % PanCycleSeconds;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Background is null)
        {
            return;
        }

        DrawCover(Background);
        DrawScrim();

        // Last, over everything including the scrim, so it dims the composed result rather than
        // being one more layer the scrim then lightens back up.
        if (ExtraDim > 0f)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.03f, 0.04f, ExtraDim));
        }
    }

    // Scales to cover and centres, cropping whatever overflows. Centring rather than anchoring a
    // corner keeps the art's focal point (here the vortex where the three regions meet) on screen at
    // any aspect ratio.
    private void DrawCover(Texture2D texture)
    {
        var art = texture.GetSize();
        if (art.X <= 0f || art.Y <= 0f)
        {
            return;
        }

        // PanZoom is what MAKES the pan possible, not a stylistic choice. A plain cover fit leaves
        // only whatever the aspect mismatch happens to give: this art is 1.834:1, so at 16:9 that is
        // ~60px horizontally and ZERO vertically. Zooming past cover buys the slack to move within.
        var scale = Mathf.Max(Size.X / art.X, Size.Y / art.Y) * PanZoom;
        var drawn = art * scale;
        var centred = (Size - drawn) / 2f;

        // HORIZONTAL ONLY. Vertical slack is zero at every 16:9 resolution before the zoom and stays
        // the smaller axis after it, and this composition reads left-to-right anyway (ruins ->
        // volcanic -> vortex -> ice), so lateral drift reveals that progression while vertical drift
        // would only bob.
        //
        // Amplitude as a FRACTION of available slack rather than a pixel count: portrait mobile has
        // ~3300px of slack against a desktop 16:9's ~175px, so a fixed offset would be imperceptible
        // on one and violent on the other.
        // Capped in absolute terms as well as proportionally. A pure fraction-of-slack rule breaks
        // down at extreme aspect ratios: portrait mobile leaves ~3900px of slack, and 55% of that is
        // a 1000px sweep -- around 48px/second, which is a camera move rather than an idle drift.
        // The cap is expressed against viewport WIDTH so it scales with the display rather than
        // being a resolution-dependent pixel count.
        var slack = Mathf.Max(0f, drawn.X - Size.X);
        var travel = Mathf.Min(slack * 0.5f * PanAmplitude, Size.X * PanMaxTravelFraction);
        var offset = travel * PanPhase();

        DrawTextureRect(texture, new Rect2(centred + new Vector2(offset, 0f), drawn), false);
    }

    // Position in the drift, in [-1, 1]. A sine rather than a triangle wave: the turnaround is where
    // a pan gives itself away, and a linear ramp reversing direction reads as the image being yanked
    // back. A sine eases to a stop and back out, so there is no moment the motion calls attention to
    // itself -- which is the whole requirement for something the player stares at while idle.
    private float PanPhase() =>
        Panning ? Mathf.Sin((float)(_elapsed / PanCycleSeconds) * Mathf.Tau) : 0f;

    private void DrawScrim()
    {
        // Two-stop ramp through the midpoint, so the top and bottom darken at different rates --
        // the bottom carries more, since that is where the art is busiest.
        //
        // Drawn as a stack of strips whose colours are sampled from a SMOOTHSTEPPED curve, not as
        // two linear ramps meeting at the midpoint: two ramps have different slopes either side of
        // the join, and that slope change is visible as a line even though the colour is continuous.
        // See DrawCentrePool for the same reasoning at more length.
        for (var i = 0; i < ScrimRows; i++)
        {
            var y0 = Size.Y * i / ScrimRows;
            var y1 = Size.Y * (i + 1) / ScrimRows;

            DrawVerticalRamp(y0, y1 - y0, ScrimColourAt(y0 / Size.Y), ScrimColourAt(y1 / Size.Y));
        }

        // The centre pool: darkening behind the title and button column, fading out on all four
        // sides so it has no visible edge. A hard-edged panel would read as a UI element sitting on
        // the art rather than as part of the lighting.
        DrawCentrePool();
    }

    // A soft pool of shade around the menu column, faded on all four sides.
    //
    // WHY A MESH RATHER THAN A FEW QUADS. Vertex colours interpolate LINEARLY across a polygon, so
    // any corner-coloured quad has a constant gradient inside it and a sudden change in slope at its
    // edge. A human eye reads that slope discontinuity as a line (Mach banding) even though the
    // colour itself is continuous -- which is why the three-strip version still showed faint vertical
    // seams where the side fades met the solid middle, despite no colour step being present.
    //
    // Subdividing into a grid and giving every vertex a SMOOTHSTEP weight makes the slope continuous
    // too, so there is no edge for the eye to find. The grid is cheap (one DrawPolygon per cell, and
    // the counts below are small) and, unlike the earlier banded ramp, its smoothness does not
    // depend on the cells being small enough to hide.
    private void DrawCentrePool()
    {
        var top = Size.Y * PoolTopFraction;
        var height = Size.Y * PoolHeightFraction;

        if (height <= 0f || Size.X <= 0f)
        {
            return;
        }

        for (var row = 0; row < PoolRows; row++)
        {
            var y0 = top + (height * row / PoolRows);
            var y1 = top + (height * (row + 1) / PoolRows);

            for (var col = 0; col < PoolColumns; col++)
            {
                var x0 = Size.X * col / PoolColumns;
                var x1 = Size.X * (col + 1) / PoolColumns;

                var points = new[]
                {
                    new Vector2(x0, y0),
                    new Vector2(x1, y0),
                    new Vector2(x1, y1),
                    new Vector2(x0, y1),
                };

                DrawPolygon(points,
                [
                    PoolColourAt(x0, y0, top, height),
                    PoolColourAt(x1, y0, top, height),
                    PoolColourAt(x1, y1, top, height),
                    PoolColourAt(x0, y1, top, height),
                ]);
            }
        }
    }

    // The overall scrim's colour at a vertical position in [0,1]: dark at the top, lightest across
    // the middle, dark again at the bottom, eased at both transitions.
    private static Color ScrimColourAt(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);

        return t < 0.5f
            ? ScrimTop.Lerp(ScrimMid, Smoothstep(t / 0.5f))
            : ScrimMid.Lerp(ScrimBottom, Smoothstep((t - 0.5f) / 0.5f));
    }

    private static float Smoothstep(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    // The pool's alpha at one point: the product of a vertical and a horizontal falloff, each
    // smoothstepped so the ramp eases in and out rather than arriving at a constant slope.
    private Color PoolColourAt(float x, float y, float top, float height)
    {
        var vertical = Falloff((y - top) / height);
        var horizontal = Falloff(x / Size.X, PoolSideFraction);

        return CentrePool with { A = CentrePool.A * vertical * horizontal };
    }

    // 0 at both ends of [0,1], 1 across the middle, eased between. `edge` is how much of each side
    // is spent on the ramp.
    private static float Falloff(float t, float edge = 0.5f)
    {
        t = Mathf.Clamp(t, 0f, 1f);

        var d = Mathf.Min(t, 1f - t);
        var w = Mathf.Clamp(d / edge, 0f, 1f);

        // Smoothstep: zero derivative at both ends, which is what removes the visible slope change.
        return w * w * (3f - (2f * w));
    }

    // One vertical gradient band, interpolated PER PIXEL by the renderer.
    //
    // WHY NOT STACKED RECTS. The first cut drew ~24 translucent strips per ramp, each a flat colour.
    // Over the old backdrop that passed; over a photograph with a large smooth sky it quantised the
    // gradient into visible horizontal rules, because each strip steps the alpha by a fixed amount
    // and a smooth source has nothing to hide the step. Rounding the strips to whole pixels fixed
    // the SEAMS between them but not the stepping itself -- they are two different defects, and only
    // this fixes the second.
    //
    // DrawPolygon with per-vertex colours hands the interpolation to the GPU, so the ramp is exact
    // at any height and costs one draw call instead of two dozen.
    private void DrawVerticalRamp(float top, float height, Color from, Color to)
    {
        if (height <= 0f)
        {
            return;
        }

        var bottom = top + height;

        // Clockwise from top-left; the colour array is index-matched to the points, so the two top
        // corners carry `from` and the two bottom corners `to`.
        var points = new[]
        {
            new Vector2(0f, top),
            new Vector2(Size.X, top),
            new Vector2(Size.X, bottom),
            new Vector2(0f, bottom),
        };

        DrawPolygon(points, [from, from, to, to]);
    }
}
