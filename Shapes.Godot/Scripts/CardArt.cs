using Godot;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Scripts;

// Real card art, keyed on the stable card id (PLAN.md B1c).
//
// res://art/cards/{cardId}.png -- the CARD ID, never the JSON filename. Every card's id and
// filename agree today (safeguard.json carried id "patch_up" until its art silently fell back to
// the placeholder, at which point the id was renamed to match), but the rule still holds: art
// keyed to filenames detaches the next time a card is renamed for readability, and it detaches
// SILENTLY -- a missing texture renders as the placeholder rather than erroring, which is exactly
// how the safeguard mismatch survived unnoticed.
//
// Falls back to ResourceIconFactory's geometric placeholder whenever a card has no art file yet,
// so the set can be filled in one card at a time instead of needing all 36 before anything shows.
public static class CardArt
{
    private const string ArtDirectory = "res://art/cards";

    // Textures are cached by path: PlayerPanel.RenderSlots/RenderHand rebuild every SlotView and
    // CardFace from scratch on each render (see their own notes), so an uncached GD.Load would
    // re-hit the filesystem for every card, every frame a render happens. GD.Load does its own
    // resource caching, but ResourceLoader.Exists does not, and that check is the per-frame cost
    // here -- a null entry records "no art for this id" so a missing file is probed once.
    private static readonly Dictionary<string, Texture2D?> Cache = [];

    // The art region for one card, or the geometric placeholder when it has no art yet.
    // fallbackType is the card's PrimaryType -- what the placeholder keys its shape/color off.
    public static Control For(string cardId, ResourceType fallbackType)
    {
        var texture = Load(cardId);
        if (texture is null)
        {
            return ResourceIconFactory.CreateArtPlaceholder(fallbackType);
        }

        // KeepAspectCovered, not KeepAspect: the art is authored 2:1 but is drawn into three
        // different aspect ratios (7:5 in hand and tooltip, 2:1 in play, ~1:1 per pane when
        // merged -- CardMetrics' reference ratios). Covered crops the overflow instead of
        // letterboxing it, so a card face never shows bars against the panel behind it. The
        // authored safe area is the centered square, so a centered crop keeps the subject.
        var rect = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,

            // Same reason CreateArtPlaceholder sets it: the art sits inside a Button (CardFace
            // and SlotView both ARE Buttons) and must never absorb the click/drag gesture that
            // the card itself is listening for.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        // IgnoreSize above lets the rect shrink below the texture's native size; without a zero
        // minimum, a 1774x887 source would impose a 1774px floor on the card's width and blow
        // out the layout of every view it appears in.
        rect.CustomMinimumSize = Vector2.Zero;
        rect.ClipContents = true;
        return rect;
    }

    // True when this card has real art -- lets a caller vary layout for art vs placeholder.
    public static bool Has(string cardId) => Load(cardId) is not null;

    // The raw texture, for callers that composite art themselves rather than parenting a
    // TextureRect -- MergedArt draws two of these into one band with a blended seam, which no
    // arrangement of TextureRects can express.
    public static Texture2D? TextureFor(string cardId) => Load(cardId);

    private static Texture2D? Load(string cardId)
    {
        if (Cache.TryGetValue(cardId, out var cached))
        {
            return cached;
        }

        var path = $"{ArtDirectory}/{cardId}.png";

        // ResourceLoader.Exists rather than a FileAccess check: in an exported build the art is
        // inside the .pck and .png files are imported to .ctex, so neither the original extension
        // nor a filesystem path is necessarily present. Exists asks the resource system, which is
        // what GD.Load will consult anyway.
        var texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        Cache[cardId] = texture;
        return texture;
    }
}
