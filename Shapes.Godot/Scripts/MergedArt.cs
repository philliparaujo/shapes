using Godot;

namespace Shapes.Godot.Scripts;

// How a merged creature's two source arts are combined into one band (DESIGN.md 5.C-UI).
public enum MergedArtStyle
{
    // Two equal halves split by an angled, soft-edged seam.
    AngledSoft,

    // As above, but the absorbing card takes the larger share -- the split communicates which
    // creature the merge actually is.
    AngledAsymmetric,

    // The primary art fills the whole band; the secondary sits inset as a framed medallion.
    Layered,
}

// Composites a merged creature's two arts into a single image.
//
// Replaces two side-by-side TextureRects in an HBoxContainer. That arrangement produced a hard
// vertical seam exactly down the middle, which read as a UI divider rather than as one creature:
// both halves got equal billing, the seam maximised the clash between two arts with unrelated
// palettes, and -- because every card's art has its subject centred and KeepAspectCovered
// centre-crops -- the split cut through the interesting part of BOTH images.
//
// Drawn rather than composed from nodes: an angled, softened seam needs per-pixel blending
// between two textures, which no arrangement of Controls can express. DrawTextureRectRegion
// handles the crop; the seam is a polygon mask feathered by drawing the boundary as a stack of
// thin translucent bands.
public partial class MergedArt : Control
{
    // Degrees off vertical. A tilted seam reads as a composed image; a perfectly vertical one
    // reads as a partition, which is the core of the "two arts in a box" complaint.
    private const float SeamAngleDegrees = 14f;

    // Width of the crossfade, as a fraction of the band's width. The two arts blend across this
    // rather than abutting.
    private const float SeamFeather = 0.075f;
    private const int FeatherBands = 22;

    // Where the seam crosses the horizontal midline, as a fraction of width.
    private const float EvenSplit = 0.5f;
    private const float AsymmetricSplit = 0.64f;

    // Layered mode: the inset medallion's size and margin, as fractions of the band.
    private const float InsetWidth = 0.34f;
    private const float InsetMargin = 0.05f;
    private const float InsetBorderWidth = 2f;
    private static readonly Color InsetBorderColor = new("d8c39a");

    private Texture2D? _primary;
    private Texture2D? _secondary;
    private MergedArtStyle _style = MergedArtStyle.AngledSoft;

    // The rect the secondary art is cover-fitted into -- its own half of the band, computed in
    // DrawSplit and read by every DrawSeamBand call in that pass. A field rather than a parameter
    // because the band loop calls DrawSeamBand many times per draw and every call needs the same
    // framing; recomputing it per band risks the two drifting apart.
    private Rect2 _secondaryRect;

    public void SetArt(Texture2D? primary, Texture2D? secondary, MergedArtStyle style)
    {
        _primary = primary;
        _secondary = secondary;
        _style = style;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        if (_primary is null && _secondary is null)
        {
            return;
        }

        // A merge that lost one of its arts degrades to the single art filling the band, rather
        // than drawing half a picture against empty space.
        if (_primary is null || _secondary is null)
        {
            DrawCover(_primary ?? _secondary!, new Rect2(Vector2.Zero, Size));
            return;
        }

        if (_style == MergedArtStyle.Layered)
        {
            DrawLayered();
            return;
        }

        DrawSplit(_style == MergedArtStyle.AngledAsymmetric ? AsymmetricSplit : EvenSplit);
    }

    // Both arts fill the whole band; the second is then painted over the first through an angled
    // mask, so each side shows an UNCROPPED cover-fit of its own art rather than a half-width
    // slice. That is what stops the split cutting through both subjects.
    private void DrawSplit(float split)
    {
        // Each art is cover-fitted to ITS OWN HALF, not to the whole band.
        //
        // Fitting both to the full band (the first version) centred both subjects on the band's
        // midpoint -- which is exactly where the seam runs, so each subject ended up half-hidden
        // behind the other and the visible part of each side was mostly background. Fitting to the
        // half means the subject is centred in the region that actually shows.
        //
        // The rects are grown by the feather so each art still has pixels to supply throughout the
        // crossfade, and are still drawn full-height: only the horizontal framing changes.
        var feather = Size.X * SeamFeather;
        var seamX = Size.X * split;

        var primaryRect = new Rect2(0f, 0f, seamX + feather, Size.Y);
        _secondaryRect = new Rect2(
            seamX - feather, 0f, Size.X - seamX + feather, Size.Y);

        DrawCover(_primary!, primaryRect);

        var bandStep = feather / FeatherBands;

        // The secondary is drawn as a stack of thin vertical strips, each clipped to a polygon
        // whose edge advances across the feather zone. Strips nearer the primary side get lower
        // alpha, which is the crossfade.
        for (var i = 0; i < FeatherBands; i++)
        {
            var t = i / (float)(FeatherBands - 1);

            // Offset this band's seam line, walking from the primary side to the secondary side.
            var offset = -feather / 2f + bandStep * i;
            var alpha = t;

            DrawSeamBand(split, offset, alpha, bandStep);
        }

        // The fully-opaque remainder past the feather zone.
        DrawSeamBand(split, feather / 2f, 1f, Size.X);
    }

    // One band of the secondary art, clipped to the region right of an angled line.
    private void DrawSeamBand(float split, float offset, float alpha, float bandWidth)
    {
        var tilt = Mathf.Tan(Mathf.DegToRad(SeamAngleDegrees)) * Size.Y / 2f;
        var centreX = Size.X * split + offset;

        // The seam runs from top to bottom, leaning by `tilt` -- top edge shifted one way, bottom
        // the other, so it crosses the midline at centreX.
        var topX = centreX + tilt;
        var bottomX = centreX - tilt;

        // Quad covering everything right of the seam, out to the band's right limit.
        var right = Mathf.Min(Size.X, centreX + bandWidth + Mathf.Abs(tilt) + 2f);
        Vector2[] clip =
        [
            new(topX, 0f),
            new(right, 0f),
            new(right, Size.Y),
            new(bottomX, Size.Y),
        ];

        // Godot has no clip-to-polygon for textures, so the mask is applied by drawing the
        // texture INTO the polygon via DrawColoredPolygon's UV mapping.
        //
        // UVs are NORMALISED (0..1 across the texture), not pixel coordinates -- passing pixels
        // sampled far outside the texture and every band came out as a flat colour block.
        //
        // Mapped through _secondaryRect (the art's own half) rather than the full band, so the
        // secondary's subject centres in the region that shows rather than under the seam.
        var target = _secondaryRect;
        var source = CoverRegion(_secondary!, target);
        var textureSize = _secondary!.GetSize();
        var uvs = new Vector2[clip.Length];
        for (var i = 0; i < clip.Length; i++)
        {
            var px = source.Position.X + source.Size.X * ((clip[i].X - target.Position.X) / target.Size.X);
            var py = source.Position.Y + source.Size.Y * ((clip[i].Y - target.Position.Y) / target.Size.Y);
            uvs[i] = new Vector2(px / textureSize.X, py / textureSize.Y);
        }

        DrawColoredPolygon(clip, new Color(1f, 1f, 1f, alpha), uvs, _secondary);
    }

    // Primary fills the band; secondary sits in a bordered inset in the lower-right. Reads as
    // "this creature has absorbed that one" rather than as two equal partners.
    private void DrawLayered()
    {
        var full = new Rect2(Vector2.Zero, Size);
        DrawCover(_primary!, full);

        var insetW = Size.X * InsetWidth;
        var insetH = insetW * (Size.Y / Size.X) * 1.25f;
        var margin = Size.X * InsetMargin;
        var inset = new Rect2(
            Size.X - insetW - margin,
            Size.Y - insetH - margin,
            insetW,
            insetH);

        // A shadow under the medallion lifts it off the art behind it.
        DrawRect(inset.Grow(3f), new Color(0f, 0f, 0f, 0.5f));
        DrawCover(_secondary!, inset);
        DrawRect(inset, InsetBorderColor, filled: false, width: InsetBorderWidth);
    }

    // Draws a texture cover-fitted into a rect: scaled to fill, overflow cropped, subject
    // centred. Matches CardArt's KeepAspectCovered so a merged pane and a single pane frame
    // their art identically.
    private void DrawCover(Texture2D texture, Rect2 target) =>
        DrawTextureRectRegion(texture, target, CoverRegion(texture, target));

    // The source-texture region that, drawn into `target`, fills it without distortion.
    private static Rect2 CoverRegion(Texture2D texture, Rect2 target)
    {
        var size = texture.GetSize();
        if (size.X <= 0f || size.Y <= 0f || target.Size.Y <= 0f)
        {
            return new Rect2(Vector2.Zero, size);
        }

        var textureAspect = size.X / size.Y;
        var targetAspect = target.Size.X / target.Size.Y;

        if (textureAspect > targetAspect)
        {
            // Source is wider: take a full-height, centred slice.
            var width = size.Y * targetAspect;
            return new Rect2((size.X - width) / 2f, 0f, width, size.Y);
        }

        // Source is taller: take a full-width, centred slice.
        var height = size.X / targetAspect;
        return new Rect2(0f, (size.Y - height) / 2f, size.X, height);
    }
}
