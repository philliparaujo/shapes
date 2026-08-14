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
    // Builds the icon + (name over description) content. Returned as a Control the caller parents
    // wherever it needs -- MoveButtonFactory drops it inside a Button, the tooltip adds it
    // straight to its move list.
    public static Control CreateContent(MoveText text)
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
        descriptionLabel.AddThemeColorOverride("default_color", new Color(0.82f, 0.82f, 0.82f));
        lines.AddChild(descriptionLabel);
        InlineResourceIcons.AppendTo(
            descriptionLabel, text.Effects, CardMetrics.MoveDescriptionFontSize);

        return row;
    }
}
