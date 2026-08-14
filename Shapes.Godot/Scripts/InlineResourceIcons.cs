using System;
using System.Collections.Generic;
using Godot;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Scripts;

// Renders the three resource shapes as INLINE icons inside rules text -- the "[anvil]" slots in
// references/card text formatting.txt, drawn rather than spelled.
//
// Two things make this a separate path from ResourceIconFactory rather than another IconSize on
// it:
//
//   * A RichTextLabel embeds TEXTURES, not Controls. ResourceIconFactory hands back live nodes
//     that draw themselves via _Draw, which cannot sit mid-sentence in a text flow. So each shape
//     is rasterized once and cached (see TextureFor) -- also cheaper than a per-instance _Draw
//     when a hand of cards puts a dozen move rows on screen.
//   * At MoveDescriptionFontSize (9px) the badge styling actively hurts. ResourceIconFactory's
//     Small tier is 18px and carries a drop shadow, a 48-wedge gradient fan and a rim stroke --
//     detail tuned for a standalone glyph that turns to mud at half that size, where the whole
//     icon is a handful of pixels across. These are drawn flat with a rim only heavy enough to
//     hold the silhouette, so triangle/square/circle stay tellable apart at text size.
//
// Deliberately NO number baked into the icon: at this scale a digit over the shape is unreadable,
// so quantity always sits in the text beside it ("Gain 3 <icon>"), which is how EffectText already
// words it -- the icon stands for the resource TYPE only.
public static class InlineResourceIcons
{
    // Rasterized at a multiple of the display size and scaled down on draw, so the shape stays
    // smooth rather than aliasing into a blob at ~10px. 4x kills the stair-steps on the triangle's
    // diagonals without making the cached textures large.
    private const int Supersample = 4;

    // Sentinel delimiters marking where an icon belongs. Non-printing control characters, so they
    // cannot collide with anything in card data or be mistaken for text if one ever leaks into a
    // plain Label.
    private const char SentinelStart = '\u0001';
    private const char SentinelEnd = '\u0002';

    private static readonly Dictionary<(ResourceType Type, int Size), ImageTexture> Cache = [];

    // Sized to the font's x-height-ish rather than its em box: an icon at (or above) the full font
    // size is taller than the lowercase text around it, which forces the whole LINE taller and
    // opens a visible gap above any row containing one. Slightly under the nominal size keeps the
    // line height unchanged, and the shape still reads because it is solid, unlike a glyph.
    public static int SizeForFont(int fontSize) => Mathf.Max(5, (int)(fontSize * 0.85f));

    // The EffectText formatter that marks where an icon belongs. Emits a sentinel rather than
    // markup: AppendTo splits on these and pushes real textures, so no BBCode is ever parsed and
    // card data needs no escaping.
    public static string SentinelFormat(ResourceType type) =>
        $"{SentinelStart}{(int)type}{SentinelEnd}";

    // Fills `label` with text produced using SentinelFormat, substituting a real icon at each
    // sentinel.
    //
    // AddImage rather than a BBCode [img] tag: [img] resolves its argument through the resource
    // loader, so runtime-generated textures would each need a registered resource path, and the
    // whole string would then need escaping so a "[" in card data could not be read as markup.
    // Pushing textures directly sidesteps both -- the label never parses markup at all.
    public static void AppendTo(RichTextLabel label, string textWithSentinels, int fontSize)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(textWithSentinels);

        var size = SizeForFont(fontSize);
        label.Clear();

        var index = 0;
        while (index < textWithSentinels.Length)
        {
            var start = textWithSentinels.IndexOf(SentinelStart, index);
            if (start < 0)
            {
                label.AddText(textWithSentinels[index..]);
                return;
            }

            if (start > index)
            {
                label.AddText(textWithSentinels[index..start]);
            }

            var end = textWithSentinels.IndexOf(SentinelEnd, start);
            if (end < 0)
            {
                // Unterminated sentinel: emit the remainder as text rather than dropping it, so a
                // malformed string degrades to visible characters instead of silently vanishing.
                label.AddText(textWithSentinels[start..]);
                return;
            }

            if (int.TryParse(textWithSentinels[(start + 1)..end], out var raw)
                && Enum.IsDefined(typeof(ResourceType), raw))
            {
                // Center-aligned to the text's own center, so the icon sits ON the line rather
                // than hanging from its top edge (the default) or dropping below its baseline --
                // both of which push the line's height past the font's and open a visible gap
                // above the row.
                label.AddImage(
                    TextureFor((ResourceType)raw, size), size, size,
                    new Color(1f, 1f, 1f), InlineAlignment.CenterTo | InlineAlignment.ToCenter);
            }

            index = end + 1;

            // EffectText puts a space after the resource ("gain 3 [anvil] next turn"), which is
            // right in text but leaves a visible gap before a following period once the bracketed
            // word becomes a fixed-width image. Swallow exactly that one space when punctuation
            // follows it, so the icon reads as attached to the sentence rather than floating.
            if (index < textWithSentinels.Length - 1
                && textWithSentinels[index] == ' '
                && textWithSentinels[index + 1] is '.' or ',')
            {
                index++;
            }
        }
    }

    private static ImageTexture TextureFor(ResourceType type, int size)
    {
        if (Cache.TryGetValue((type, size), out var cached) && GodotObject.IsInstanceValid(cached))
        {
            return cached;
        }

        var texture = ImageTexture.CreateFromImage(Draw(type, size * Supersample));
        Cache[(type, size)] = texture;
        return texture;
    }

    // Drawn straight into an Image rather than through a Control's _Draw: this runs once per
    // (type, size), has no node to attach to, and needs a texture out the other end. A per-pixel
    // coverage test at 4x supersampling gives clean enough edges at icon size and avoids standing
    // up a SubViewport just to screenshot three shapes.
    private static Image Draw(ResourceType type, int pixels)
    {
        var image = Image.CreateEmpty(pixels, pixels, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        var fill = ColorOf(type);
        var rim = PerceptualDarken(fill, 0.45f);
        var center = new Vector2(pixels / 2f, pixels / 2f);
        var extent = pixels * 0.92f;
        var rimWidth = Mathf.Max(pixels * 0.06f, 1f);

        for (var y = 0; y < pixels; y++)
        {
            for (var x = 0; x < pixels; x++)
            {
                var distance = SignedDistance(type, new Vector2(x + 0.5f, y + 0.5f), center, extent);
                if (distance > 0f)
                {
                    continue;
                }

                // Inside the shape: the outer band takes the darker rim so the silhouette stays
                // defined against whatever the text sits on, light or dark.
                image.SetPixel(x, y, -distance < rimWidth ? rim : fill);
            }
        }

        return image;
    }

    // Negative inside the shape, positive outside -- one signed-distance test per shape, so the
    // fill and the rim band both fall out of the same number instead of being two draw passes.
    private static float SignedDistance(ResourceType type, Vector2 p, Vector2 center, float extent)
    {
        var half = extent / 2f;
        var d = p - center;

        return type switch
        {
            ResourceType.Wheel => d.Length() - half,
            ResourceType.Anvil => Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Y)) - half,
            _ => TriangleDistance(d, half),
        };
    }

    // Upward triangle, apex at -half and base at +half. Each edge's distance is normalized, so the
    // rim band reads as an even thickness on all three sides rather than thinning on the diagonals.
    private static float TriangleDistance(Vector2 d, float half)
    {
        var apex = new Vector2(0f, -half);
        var left = new Vector2(-half, half);
        var right = new Vector2(half, half);

        return Mathf.Max(
            Mathf.Max(EdgeDistance(d, apex, right), EdgeDistance(d, right, left)),
            EdgeDistance(d, left, apex));
    }

    // Signed distance from p to the line through a->b, positive outside the edge given the
    // triangle's clockwise winding.
    private static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var edge = b - a;
        var normal = new Vector2(edge.Y, -edge.X).Normalized();
        return (p - a).Dot(normal);
    }

    // Same palette as ResourceIconFactory -- kept in step deliberately, so an inline icon and the
    // cost badge for the same resource read as the same thing.
    private static Color ColorOf(ResourceType type) => type switch
    {
        ResourceType.Spike => new Color(0.8f, 0.2f, 0.15f),
        ResourceType.Anvil => new Color(0.25f, 0.65f, 0.3f),
        ResourceType.Wheel => new Color(0.25f, 0.45f, 0.9f),
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    // HSV-value darkening, matching ResourceShape.PerceptualDarken's reasoning: a linear RGB
    // darken crushes the blue Wheel toward black long before red/green reach the same perceived
    // darkness at the same fraction.
    private static Color PerceptualDarken(Color color, float amount) =>
        Color.FromHsv(color.H, color.S, color.V * (1f - amount), color.A);
}
