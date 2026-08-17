using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// One move, rendered the way references/card dimensions.pdf draws it: a numbered cost pip on the
// left, then the move name on its own line with the effect description in smaller text beneath.
// Roughly a 5:2 block (see CardMetrics).
//
// Shared by the tooltip (plain rows) and the board (the same content inside a Button, see
// MoveButtonFactory) because the previous version hand-rolled a different row in each of three
// places -- which is how the cost number ended up missing from the tooltip's icons while the
// board's had one, and how name and description ended up concatenated into a single label at a
// single font size in one view but not another.
public static class MoveRowFactory
{
    // The amber a spent move's text takes (PLAN.md D2 item 3). Unused anywhere else on a move row --
    // costs are type-coloured and text is otherwise near-white -- so the hue shift alone says
    // "spent" without adding anything to the layout. Paired with SpentMoveOverlay's scrim.
    public static readonly Color SpentTextColor = Palette.Spent;

    // Slightly deeper for the description, mirroring the normal row's own name-brighter-than-
    // description relationship so a spent row keeps its internal hierarchy instead of flattening
    // into one block of colour.
    private static readonly Color SpentDescriptionColor = Palette.SpentDim;

    private static readonly Color DescriptionColor = new(0.82f, 0.82f, 0.82f);

    // Builds the icon + (name over description) content. Returned as a Control the caller parents
    // wherever it needs -- MoveButtonFactory drops it inside a Button, the tooltip adds it
    // straight to its move list.
    //
    // `isSpent` recolours the text rather than adding a marker beside it; see SpentMoveOverlay's
    // header for why an added element was tried twice and dropped both times.
    public static Control CreateContent(MoveText text, bool isSpent = false)
    {
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Begin,
            ClipContents = true,
        };
        row.AddThemeConstantOverride("separation", 6);

        // The cost pip always carries its number here. The tooltip's old rows passed no number at
        // all, so a move's cost was invisible in the one view with room to show it.
        if (text.PrimaryType is { } type)
        {
            var icon = ResourceIconFactory.Create(
                type, ResourceIconFactory.IconSize.Medium, text.CostAmount, text.IsDiscounted);
            icon.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            row.AddChild(icon);
        }

        var lines = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        lines.AddThemeConstantOverride("separation", 0);
        row.AddChild(lines);

        var nameLabel = new Label
        {
            Text = text.Name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipText = true,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", CardMetrics.MoveNameFontSize);
        if (isSpent)
        {
            nameLabel.AddThemeColorOverride("font_color", SpentTextColor);
        }

        lines.AddChild(nameLabel);

        // A RichTextLabel, not a Label, because the description embeds real resource icons where
        // the rules text names a resource (see InlineResourceIcons) -- a plain Label can only show
        // the bracketed fallback text. FitContent so it still sizes to its wrapped text the way
        // the Label did, rather than demanding an explicit height.
        var descriptionLabel = new RichTextLabel
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FitContent = true,
            ScrollActive = false,
            BbcodeEnabled = false,
        };
        descriptionLabel.AddThemeFontSizeOverride("normal_font_size", CardMetrics.MoveDescriptionFontSize);
        descriptionLabel.AddThemeColorOverride(
            "default_color", isSpent ? SpentDescriptionColor : DescriptionColor);
        lines.AddChild(descriptionLabel);
        InlineResourceIcons.AppendTo(
            descriptionLabel, text.Effects, CardMetrics.MoveDescriptionFontSize);

        return row;
    }
}
