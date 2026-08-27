using Godot;

namespace Shapes.Godot.Scripts;

// The rectangle framing the six board slots (DESIGN.md 5.C-UI, from references/game screen.png):
// a filled playfield, a thick bevelled border, and a divider separating the two players' rows.
//
// Drawn in _Draw on a mouse-ignoring Control sitting BEHIND the slot rows rather than being a
// StyleBox on a container wrapping them. Two reasons: the divider is a line through the middle
// of the frame, which no StyleBox can express, and a wrapping container would sit between
// PlayerPanel and its Slots row, changing the GlobalPosition BoardAnimator reads via
// CollectSlotRects. As a sibling drawn underneath, it changes nothing about slot layout.
//
// Palette is a warm tan/brown board rather than the dark blue this started as: the game is not
// committing to a dark theme (5.C-UI), and a light playfield is the half of that decision the
// board itself has to make. Everything drawn on top -- slots, cards, text -- reads against a
// mid-tone surface, so neither a light nor a dark UI shell needs the board to change.
public partial class BoardFrame : Control
{
    // Felt-to-wood ramp. The fill is the playing surface; the frame is the rail around it, drawn
    // as three bands (outer shadow, mid body, inner highlight) to fake a bevel -- see _Draw.
    //
    // The surface is a DESATURATED green felt, not the bright tan this had first. The tan was
    // both lighter and more saturated than most of the card art on top of it, so it competed with
    // the cards for attention instead of receding behind them; a table surface should be the
    // quietest thing on screen. The rail stays warm wood, which frames the felt without joining
    // the contest.
    // From Palette (DESIGN.md D3 phase 1) -- the felt and its gold frame are part of the scheme.
    private static readonly Color SurfaceLight = Palette.BoardFelt;
    private static readonly Color SurfaceDark = Palette.BoardFeltDark;
    private static readonly Color FrameBodyColor = Palette.BoardFrameBody;
    private static readonly Color FrameLightColor = Palette.BoardFrameLight;
    private static readonly Color FrameDarkColor = Palette.BoardFrameDark;

    // The divider is a seam in the surface, not a rail: darker than the felt, with a thin
    // highlight under it so it reads as an inset groove rather than a drawn stroke.
    private static readonly Color DividerColor = new("22332a");
    private static readonly Color DividerHighlightColor = new("587a63", 0.8f);

    // Grain and inner shadow -- the two things that stop a flat fill reading as plastic. Both
    // deliberately weak: grain that is visible AS grain looks like noise, and an inner shadow
    // that is visible as a band looks like a second border.
    private static readonly Color InnerShadowColor = new(0f, 0f, 0f, 0.30f);
    private const int GrainDots = 900;
    private const float GrainAlpha = 0.035f;
    private const int InnerShadowBands = 10;
    private const float InnerShadowDepth = 16f;
    private const int SurfaceBands = 40;

    // Thick enough to read as a physical rail. Split across the three bevel bands below.
    private const float FrameWidth = 14f;
    private const float BevelWidth = 3f;
    private const float DividerWidth = 3f;
    private const float CornerRadius = 14f;

    public override void _Ready()
    {
        // Purely decorative. It sits behind the slots, but Godot still hit-tests it, and a Stop
        // filter here would swallow drops meant for SlotView.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var full = new Rect2(Vector2.Zero, Size);

        // Three nested rounded rects make the bevel: the outermost is the frame's shadowed edge,
        // the next its lit body, the innermost the playing surface. Drawing them as filled boxes
        // rather than as borders on one box is what lets the light band sit INSIDE the dark one
        // (a StyleBox border is a single colour on all four sides, which cannot suggest a light
        // source).
        DrawStyleBox(RoundedBox(FrameDarkColor, CornerRadius), full);

        var body = full.Grow(-BevelWidth);
        DrawStyleBox(RoundedBox(FrameBodyColor, CornerRadius - BevelWidth), body);

        // The inner lip catches the light: one band, inset from the body, before the surface.
        var lip = full.Grow(-(FrameWidth - BevelWidth));
        DrawStyleBox(RoundedBox(FrameLightColor, CornerRadius - FrameWidth + BevelWidth), lip);

        var surface = full.Grow(-FrameWidth);
        DrawSurface(surface);
        DrawDivider(surface);
    }

    // The playing surface: a vertical gradient, a dusting of grain, and an inner shadow where the
    // felt meets the rail. Together these are what make it read as a material rather than as a
    // filled rectangle -- the gradient gives it a light direction, the grain gives it a texture at
    // rest, and the inner shadow sets it INSIDE the frame instead of painted onto it.
    private void DrawSurface(Rect2 surface)
    {
        var radius = Mathf.Max(2f, CornerRadius - FrameWidth);

        // Base pass fills the rounded rect (including the corners the band pass cannot round),
        // then the bands paint the gradient over its straight interior.
        DrawStyleBox(RoundedBox(SurfaceDark, radius), surface);

        var bandHeight = surface.Size.Y / SurfaceBands;
        for (var i = 0; i < SurfaceBands; i++)
        {
            var t = i / (float)(SurfaceBands - 1);
            var color = SurfaceLight.Lerp(SurfaceDark, t);

            // Inset horizontally by the corner radius so square bands never overwrite the
            // rounded corners drawn above.
            DrawRect(
                new Rect2(
                    surface.Position.X + radius,
                    surface.Position.Y + i * bandHeight,
                    surface.Size.X - radius * 2f,
                    bandHeight + 1f),
                color);
        }

        DrawGrain(surface);
        DrawInnerShadow(surface, radius);
    }

    // Fixed-seed speckle. A deterministic seed rather than a random one so the grain does not
    // crawl every time the board re-draws (a resize, a card played) -- static grain reads as
    // texture, grain that changes reads as television static.
    private void DrawGrain(Rect2 surface)
    {
        var random = new RandomNumberGenerator { Seed = 0xB0A4D };

        for (var i = 0; i < GrainDots; i++)
        {
            var p = new Vector2(
                random.RandfRange(surface.Position.X, surface.End.X),
                random.RandfRange(surface.Position.Y, surface.End.Y));

            // Half the dots lighten and half darken, so the grain reads as fibre rather than as
            // dirt on the surface.
            var lighten = random.Randf() > 0.5f;
            var shade = lighten ? Colors.White : Colors.Black;
            DrawRect(new Rect2(p, new Vector2(2f, 2f)), new Color(shade, GrainAlpha));
        }
    }

    // A soft darkening just inside the rail, as nested rounded outlines at decreasing alpha --
    // the same layered-band trick the vignette and the card pips use. Sells the felt as recessed
    // into the frame rather than flush with it.
    private void DrawInnerShadow(Rect2 surface, float radius)
    {
        for (var i = 0; i < InnerShadowBands; i++)
        {
            var t = i / (float)InnerShadowBands;
            var inset = InnerShadowDepth * t;

            var box = new StyleBoxFlat
            {
                BgColor = Colors.Transparent,
                BorderColor = new Color(
                    InnerShadowColor.R,
                    InnerShadowColor.G,
                    InnerShadowColor.B,
                    InnerShadowColor.A / InnerShadowBands * (1f - t)),
                DrawCenter = false,
            };
            box.SetBorderWidthAll(2);
            box.SetCornerRadiusAll((int)Mathf.Max(0f, radius - inset));
            DrawStyleBox(box, surface.Grow(-inset));
        }
    }

    // Divider at the true vertical centre: the two slot rows are positioned symmetrically about
    // it by BoardView's layout, so this reads as the line between the two sides. Inset to the
    // surface rect so it stops at the rail rather than running under it.
    private void DrawDivider(Rect2 surface)
    {
        var y = Size.Y / 2f;
        var left = new Vector2(surface.Position.X, y);
        var right = new Vector2(surface.End.X, y);

        DrawLine(left, right, DividerColor, DividerWidth);
        DrawLine(
            left + new Vector2(0f, DividerWidth),
            right + new Vector2(0f, DividerWidth),
            DividerHighlightColor,
            1f);
    }

    private static StyleBoxFlat RoundedBox(Color color, float radius)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll((int)Mathf.Max(0f, radius));
        return box;
    }
}
