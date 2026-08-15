using System.Collections.Generic;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The small keyword explainers that stack directly above a card's hover tooltip: one compact panel
// per status keyword the card's text mentions, each carrying the keyword's name and its reminder
// text from references/card text formatting.txt (see KeywordText).
//
// Built in code rather than as a .tscn, unlike HoverDetailPanel: the number of panels is decided
// per card (a card mentions zero, one or two keywords) so there is no fixed node tree for a scene
// to describe, and each panel is three nodes deep. HoverDetailPanel is a scene because it has a
// fixed, deep layout that four different screens instantiate; this has neither property.
//
// Owned by HoverDetailPanel rather than by each of its callers, and grown UPWARD from the
// tooltip's own top edge, for the same reason the tooltip itself is positioned by one rule instead
// of per-source: BoardView, Deckbuilder and CardBrowser each place the tooltip differently (fixed
// corner vs. cursor-following), and a stack that positioned itself would have to know which. By
// hanging off the tooltip's rect it inherits whatever placement its owner chose.
public sealed partial class KeywordTooltip : VBoxContainer
{
    // Narrower than nothing and wider than nothing: matched to the tooltip it sits above so the
    // two read as one column rather than two stacked objects of different widths.
    public const float Width = CardMetrics.TooltipWidth;

    // Gap between the explainer stack and the card tooltip below it, and between the panels
    // themselves. Small enough that the group reads as attached to the card, large enough that
    // each panel still reads as its own note rather than as more of the card's own text.
    public const int Separation = 4;

    private const int NameFontSize = 11;
    private const int ReminderFontSize = 9;
    private const int Padding = 6;

    // Distinct from CardStyle.StockColor: these are notes ABOUT the card, not part of it, so they
    // take a lighter panel than card stock. Same edge and corner radius, so they still read as
    // belonging to the same UI.
    private static readonly Color PanelColor = new("3a424c");
    private static readonly Color ReminderColor = new(0.82f, 0.82f, 0.82f);

    public override void _Ready()
    {
        // Never the target of a mouse-enter/exit -- a hoverable tooltip would fight the hover it is
        // describing, exactly as HoverDetailPanel's own header sets out.
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", Separation);
        Visible = false;
    }

    // Rebuilds the stack for `keywords`, and reports whether anything is showing. An empty list
    // hides the whole container rather than leaving a zero-height node behind, so a card with no
    // keywords costs nothing and leaves no invisible gap above the tooltip.
    public bool Render(IReadOnlyList<KeywordEntry> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);

        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        foreach (var keyword in keywords)
        {
            AddChild(BuildPanel(keyword));
        }

        // Width committed here, before anyone asks for the stack's height. A reminder wraps, so
        // its height is a function of the width it is given -- asking for a combined minimum while
        // the container is still 0 wide yields the height of text wrapped at zero, which is not
        // the height it will actually draw at.
        CustomMinimumSize = new Vector2(Width, 0f);
        Size = new Vector2(Width, GetCombinedMinimumSize().Y);

        Visible = keywords.Count > 0;
        return Visible;
    }

    private static Control BuildPanel(KeywordEntry keyword)
    {
        var panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(Width, 0f),
        };
        panel.AddThemeStyleboxOverride("panel", CardStyle.Box(PanelColor, CardStyle.EdgeColor));

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", Padding);
        }

        panel.AddChild(margin);

        var lines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        lines.AddThemeConstantOverride("separation", 1);
        margin.AddChild(lines);

        // A RichTextLabel rather than a Label, for the same reason the reminder below is one: the
        // heading IS a keyword, so routing it through the shared renderer gives it the same bold
        // and yellow tint the word carries everywhere else in the UI. A plain Label cannot show
        // either, which left the explainer's own heading as the one place in the game where a
        // keyword appeared unformatted -- and it is the place the player looks to learn what the
        // formatting on the card MEANS, so it is the last place that should opt out of it.
        //
        // Sized to its own text (FitContent) so the heading keeps a Label's tight single line and
        // does not reserve a RichTextLabel's default height.
        var name = new RichTextLabel
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.Off,
            FitContent = true,
            ScrollActive = false,
            BbcodeEnabled = false,
        };
        name.AddThemeFontSizeOverride("normal_font_size", NameFontSize);
        lines.AddChild(name);
        InlineResourceIcons.AppendTo(name, keyword.Name, NameFontSize);

        // A RichTextLabel, not a Label, because a reminder can itself name a resource -- none does
        // today, but the reminders are authored text (KeywordText) and routing them through the
        // same renderer as every other rules string is what keeps that from becoming a special
        // case later. It also means the keyword's own name is bolded inside its reminder for free,
        // by the same rule that bolds it on the card.
        var reminder = new RichTextLabel
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FitContent = true,
            ScrollActive = false,
            BbcodeEnabled = false,
        };
        reminder.AddThemeFontSizeOverride("normal_font_size", ReminderFontSize);
        reminder.AddThemeColorOverride("default_color", ReminderColor);
        lines.AddChild(reminder);
        InlineResourceIcons.AppendTo(reminder, keyword.Reminder, ReminderFontSize);

        return panel;
    }
}
