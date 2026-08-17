using Godot;

namespace Shapes.Godot.Scripts;

// The room the board sits in (PLAN.md 5.C-UI). Drawn behind everything else in BoardView.
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
    // From Palette (PLAN.md D3 phase 1): these three ARE the app's background, so they belong to
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

    // A vertical gradient as horizontal bands. Godot's immediate-mode API has no gradient-fill
    // primitive, and a GradientTexture2D would mean carrying a resource for two stops -- banding
    // is invisible at this contrast because adjacent bands differ by well under one 8-bit step.
    private void DrawRamp()
    {
        var bandHeight = Size.Y / RampBands;

        for (var i = 0; i < RampBands; i++)
        {
            var t = i / (float)(RampBands - 1);

            // Two-segment ramp so the light pools at FocusY rather than at the midpoint: top
            // colour up to the focus, bottom colour below it.
            var color = t < FocusY
                ? TopColor.Lerp(MidColor, t / FocusY)
                : MidColor.Lerp(BottomColor, (t - FocusY) / (1f - FocusY));

            // Bands overlap by a pixel: exact abutment leaves hairline seams when the height
            // does not divide evenly into RampBands.
            DrawRect(new Rect2(0f, i * bandHeight, Size.X, bandHeight + 1f), color);
        }
    }

    // Darkens the screen's edges while leaving the focus clear.
    //
    // Drawn as concentric ANNULI (thick arc strokes), not filled circles: a filled circle paints
    // its whole interior, so stacking them darkens the centre most -- the opposite of a vignette.
    // An arc stroked at width w only covers the band at that radius, so each successive ring adds
    // shade further out and the focus is never painted at all.
    private void DrawVignette()
    {
        var centre = new Vector2(Size.X / 2f, Size.Y * FocusY);

        // Reaches past the corners so the darkest band is off-screen rather than visible as a
        // ring on the backdrop.
        var maxRadius = Size.Length() * 0.80f;
        var clearRadius = maxRadius * 0.30f;
        var span = maxRadius - clearRadius;
        var bandWidth = span / VignetteRings;

        for (var i = 0; i < VignetteRings; i++)
        {
            var t = (i + 0.5f) / VignetteRings;

            // Radius of this band's centre-line, and a squared ramp so the darkening starts
            // imperceptibly near the focus and deepens toward the corners.
            var radius = clearRadius + span * t;
            var alpha = VignetteColor.A * t * t;

            // Overlap each band slightly (x1.5) so no seam shows between them.
            DrawArc(centre, radius, 0f, Mathf.Tau, 64, new Color(0f, 0f, 0f, alpha), bandWidth * 1.5f);
        }
    }
}
