using Godot;

namespace Shapes.Godot.Scripts;

// The room the board sits in (DESIGN.md 5.C-UI). Drawn behind everything else in BoardView.
//
// Replaces Godot's default flat clear colour, which was the single largest area on screen and
// read as "nothing has been set here" -- the cards, pips and avatars all carry gradients and
// rims, so an untouched fill behind them looked unfinished by comparison rather than neutral.
//
// Two layers, both drawn rather than textured so there is no asset to keep in step with the
// palette: a vertical ramp (light falls from above) and a radial vignette centred on the board
// (the edges of the screen fall away, pulling the eye to the play area).
public partial class TableBackdrop : Control
{
    // Deep desaturated slate. Cool and dark so the warm board and the bright card art both read
    // as sitting ON something rather than blending into it.
    // From Palette (DESIGN.md D3 phase 1): these three ARE the app's background, so they belong to
    // the scheme rather than to this one file.
    private static readonly Color TopColor = Palette.BackdropTop;
    private static readonly Color MidColor = Palette.BackdropMid;
    private static readonly Color BottomColor = Palette.BackdropBottom;

    // How dark the corners get. Subtle on purpose -- a heavy vignette reads as a photo filter
    // rather than as lighting.
    private static readonly Color VignetteColor = new(0f, 0f, 0f, 0.38f);

    // Where the light pools, as a fraction of height. Matches roughly where the board sits, so
    // the brightest part of the room is behind the play area rather than at the screen's middle.
    private const float FocusY = 0.42f;

    private const int RampBands = 48;
    private const int VignetteRings = 20;

    // Angular resolution of each vignette ring. 64 was the old DrawArc point count and is plenty --
    // at this radius the facets are well under a pixel of chord deviation.
    private const int VignetteSegments = 64;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        DrawRamp();
        DrawVignette();
    }

    // A vertical gradient, interpolated PER PIXEL by the renderer via vertex-coloured quads.
    //
    // THIS USED TO BE 48 FLAT BANDS THAT OVERLAPPED BY A PIXEL, and that was fine for as long as
    // this node was opaque: the overlap row was simply painted twice with the same solid colour and
    // nothing showed. It stopped being fine the moment the node was given a modulate alpha so the
    // menu artwork could sit behind it (DESIGN.md D3) -- a translucent band drawn twice over the same
    // row composites twice, so every one of the 48 boundaries became a visibly darker line.
    //
    // The lesson worth keeping: overlap-to-hide-seams is a technique that silently depends on
    // opacity, and this project has now been bitten by it twice. Interpolating instead removes the
    // seams rather than covering them, so it holds at any alpha.
    private void DrawRamp()
    {
        for (var i = 0; i < RampBands; i++)
        {
            var y0 = Size.Y * i / RampBands;
            var y1 = Size.Y * (i + 1) / RampBands;

            var points = new[]
            {
                new Vector2(0f, y0),
                new Vector2(Size.X, y0),
                new Vector2(Size.X, y1),
                new Vector2(0f, y1),
            };

            var top = RampColourAt(y0 / Size.Y);
            var bottom = RampColourAt(y1 / Size.Y);

            DrawPolygon(points, [top, top, bottom, bottom]);
        }
    }

    // Two-segment ramp so the light pools at FocusY rather than at the midpoint: top colour up to
    // the focus, bottom colour below it.
    private static Color RampColourAt(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);

        return t < FocusY
            ? TopColor.Lerp(MidColor, t / FocusY)
            : MidColor.Lerp(BottomColor, (t - FocusY) / (1f - FocusY));
    }

    // Darkens the screen's edges while leaving the focus clear.
    //
    // Built as a RING MESH -- each annulus is a strip of quads whose inner vertices carry one alpha
    // and outer vertices the next -- so the darkening interpolates smoothly across each band and
    // exactly abuts the next.
    //
    // The previous version stroked concentric arcs at 1.5x band width to hide the seams between
    // them. Same trap as DrawRamp above, and worse: these bands are translucent BY DESIGN (each ring
    // adds shade on top of the last), so a 1.5x overlap composited three deep at every boundary and
    // drew a ring at each. Invisible against a flat gradient, obvious the moment artwork sits
    // behind it.
    private void DrawVignette()
    {
        var centre = new Vector2(Size.X / 2f, Size.Y * FocusY);

        // Reaches past the corners so the darkest band is off-screen rather than visible as a
        // ring on the backdrop.
        var maxRadius = Size.Length() * 0.80f;
        var clearRadius = maxRadius * 0.30f;
        var span = maxRadius - clearRadius;

        for (var i = 0; i < VignetteRings; i++)
        {
            var tInner = i / (float)VignetteRings;
            var tOuter = (i + 1) / (float)VignetteRings;

            // Squared ramp so the darkening starts imperceptibly near the focus and deepens toward
            // the corners.
            var inner = new Color(0f, 0f, 0f, VignetteColor.A * tInner * tInner);
            var outer = new Color(0f, 0f, 0f, VignetteColor.A * tOuter * tOuter);

            DrawRing(centre, clearRadius + (span * tInner), clearRadius + (span * tOuter), inner, outer);
        }
    }

    // One annulus as a quad strip, with the inner and outer edges each carrying their own colour.
    private void DrawRing(Vector2 centre, float innerRadius, float outerRadius, Color inner, Color outer)
    {
        for (var s = 0; s < VignetteSegments; s++)
        {
            var a0 = Mathf.Tau * s / VignetteSegments;
            var a1 = Mathf.Tau * (s + 1) / VignetteSegments;

            var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

            var points = new[]
            {
                centre + (d0 * innerRadius),
                centre + (d1 * innerRadius),
                centre + (d1 * outerRadius),
                centre + (d0 * outerRadius),
            };

            DrawPolygon(points, [inner, inner, outer, outer]);
        }
    }
}
