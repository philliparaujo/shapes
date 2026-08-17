using Godot;

namespace Shapes.Godot.Scripts;

// The project's default control styling (PLAN.md D3 phase 1): one Theme, built once, applied at
// every screen's root so its whole subtree inherits it.
//
// BUILT IN CODE RATHER THAN AUTHORED AS A .tres. A theme resource would have to restate every
// colour as a literal, which puts it straight back out of sync with Palette the first time a value
// moves -- the same drift MoveRowFactory and CardStyle each exist to prevent for their own corner
// of the UI. Reading from Palette means the scheme has exactly one definition.
//
// WHAT THIS IS FOR. Godot falls back to its built-in theme for any control that overrides nothing,
// which is why the lobby renders as stock grey slabs: it carries six theme_overrides, all spacing
// and font size. A Theme sets the FLOOR, so a screen gets the project's look by default and only
// specifies where it genuinely differs. That is the structural fix for the drift, not another
// per-screen styling pass.
//
// DELIBERATELY NOT A REDESIGN. Every colour comes from Palette, which is itself the values already
// in use. Applying this should leave the already-styled screens (board, browser, deckbuilder)
// looking close to unchanged and bring the lobby up to meet them -- which is exactly what the
// before/after screenshot pass checks.
public static class UiTheme
{
    private static Theme? _cached;

    // One instance shared by every scene. A Theme is a plain Resource, so sharing it is free and
    // means a future runtime tweak reaches every screen at once.
    public static Theme Get() => _cached ??= Build();

    // Applies the shared theme to a scene root. Every control beneath it inherits, so this is the
    // single call a screen needs.
    public static void ApplyTo(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Theme = Get();
    }

    private static Theme Build()
    {
        var theme = new Theme
        {
            DefaultFontSize = 15,
        };

        StyleButton(theme);
        StyleOptionButton(theme);
        StyleLineEdit(theme);
        StylePanels(theme);
        StyleLabels(theme);
        StyleScrollbars(theme);

        return theme;
    }

    // Every state gets a box. Overriding only "normal" is what lets Godot's default theme flash
    // back the moment a cursor crosses the control -- the trap CardStyle.ApplyToButton already
    // documents for its own per-control overrides.
    private static void StyleButton(Theme theme)
    {
        StyleButtonStates(theme, "Button");

        // Hover lifts the label to full white as well as warming the border, so the state reads
        // even for a player who cannot separate the two greens.
        theme.SetColor("font_color", "Button", Palette.InkMuted);
        theme.SetColor("font_hover_color", "Button", Palette.Ink);
        theme.SetColor("font_pressed_color", "Button", Palette.Accent);
        theme.SetColor("font_focus_color", "Button", Palette.Ink);
        theme.SetColor("font_disabled_color", "Button", Palette.InkDisabled);
    }

    // The five states every button-like control needs. Shared by Button and OptionButton, which
    // keep separate stylebox sets -- an OptionButton does NOT inherit Button's, so without this the
    // lobby's dropdowns would stay stock while its buttons were themed.
    private static void StyleButtonStates(Theme theme, string control)
    {
        theme.SetStylebox("normal", control, ControlBox(Palette.Control, Palette.ControlBorder));

        // Gold on hover -- the board's own frame colour, which is what ties a button to the game
        // rather than to Godot's default widget set.
        theme.SetStylebox("hover", control, ControlBox(Palette.ControlHover, Palette.ControlBorderHover));
        theme.SetStylebox("pressed", control, ControlBox(Palette.ControlPressed, Palette.Accent));
        theme.SetStylebox("disabled", control, ControlBox(Palette.ControlDisabled, Palette.ControlBorder));

        // FOCUS DRAWS NOTHING. Godot leaves focus on a button after it is clicked, so any visible
        // focus style becomes a permanent marker on the last thing you pressed -- a stale highlight
        // that reads as "this is selected" when nothing is. Hover already answers "which control is
        // under the cursor", which is the question a mouse player is actually asking.
        //
        // The cost is keyboard/gamepad navigation losing its position indicator. Accepted for now
        // because nothing in this project is keyboard-driven; if that changes, the fix is to show
        // focus only when it arrived from a key rather than a click, not to restore it unconditionally.
        theme.SetStylebox("focus", control, new StyleBoxEmpty());
    }

    // OptionButton is a Button subclass but keeps its own stylebox set, so it needs the same five
    // states again -- inheriting Button's does not happen.
    private static void StyleOptionButton(Theme theme)
    {
        StyleButtonStates(theme, "OptionButton");

        theme.SetColor("font_color", "OptionButton", Palette.InkMuted);
        theme.SetColor("font_hover_color", "OptionButton", Palette.Ink);
        theme.SetColor("font_pressed_color", "OptionButton", Palette.Ink);
        theme.SetColor("font_focus_color", "OptionButton", Palette.Ink);
        theme.SetColor("font_disabled_color", "OptionButton", Palette.InkDisabled);

        // The dropdown list itself, which is a PopupMenu and styled separately from the button that
        // opens it -- an unstyled popup is the most visible way a themed control still looks stock.
        theme.SetStylebox("panel", "PopupMenu", PanelBox(Palette.SurfaceRaised));
        theme.SetColor("font_color", "PopupMenu", Palette.InkMuted);
        theme.SetColor("font_hover_color", "PopupMenu", Palette.Ink);
        theme.SetStylebox("hover", "PopupMenu", FlatBox(Palette.ControlHover));
    }

    private static void StyleLineEdit(Theme theme)
    {
        theme.SetStylebox("normal", "LineEdit", ControlBox(Palette.SurfaceInset));
        theme.SetStylebox("focus", "LineEdit", ControlBox(Palette.SurfaceInset, Palette.Accent));
        theme.SetColor("font_color", "LineEdit", Palette.Ink);
        theme.SetColor("font_placeholder_color", "LineEdit", Palette.InkDim);
        theme.SetColor("caret_color", "LineEdit", Palette.Accent);
    }

    private static void StylePanels(Theme theme)
    {
        theme.SetStylebox("panel", "PanelContainer", PanelBox(Palette.Surface));
        theme.SetStylebox("panel", "Panel", PanelBox(Palette.Surface));
    }

    private static void StyleLabels(Theme theme)
    {
        theme.SetColor("font_color", "Label", Palette.InkMuted);
        theme.SetColor("default_color", "RichTextLabel", Palette.InkMuted);
    }

    // The default scrollbar is a bright slab against a dark UI. Both the browser and the log lean
    // on scrolling, so it is worth the few lines.
    private static void StyleScrollbars(Theme theme)
    {
        // WIDTH COMES FROM THE STYLEBOX, and this is the trap: a ScrollBar has no minimum size of
        // its own, so it is exactly as wide as its stylebox's content margins make it. Godot's
        // default boxes carry those margins; a bare StyleBoxFlat does not, so styling the bar
        // without restoring them collapses it to a hairline -- which reads as "the scrollbar
        // disappeared", the symptom this fixes.
        foreach (var axis in new[] { "VScrollBar", "HScrollBar" })
        {
            var vertical = axis == "VScrollBar";

            // The trough is inset on the long axis only, so the bar reads as a channel cut into
            // the panel rather than as a floating stripe.
            theme.SetStylebox("scroll", axis, ScrollBox(Palette.SurfaceInset, vertical, TroughInset));
            theme.SetStylebox("grabber", axis, ScrollBox(Palette.SurfaceRaised, vertical, GrabberInset));
            theme.SetStylebox(
                "grabber_highlight", axis, ScrollBox(Palette.ControlHover, vertical, GrabberInset));
            theme.SetStylebox(
                "grabber_pressed", axis, ScrollBox(Palette.AccentDim, vertical, GrabberInset));
        }
    }

    // Total bar thickness, and how much of it each part gives up as padding. The grabber is inset
    // further than the trough so it sits INSIDE the channel with a visible gutter either side.
    private const int ScrollThickness = 12;
    private const int TroughInset = 3;
    private const int GrabberInset = 2;

    // A scrollbar part. `inset` pads the two edges across the bar's short axis, which is what gives
    // the control its thickness -- see StyleScrollbars on why that cannot be left to a default.
    private static StyleBoxFlat ScrollBox(Color fill, bool vertical, int inset)
    {
        var box = new StyleBoxFlat { BgColor = fill };
        box.SetCornerRadiusAll(ScrollThickness / 2);

        if (vertical)
        {
            box.ContentMarginLeft = inset;
            box.ContentMarginRight = ScrollThickness - inset;
        }
        else
        {
            box.ContentMarginTop = inset;
            box.ContentMarginBottom = ScrollThickness - inset;
        }

        return box;
    }

    // A control: tighter corner than a panel (see Palette.ControlCornerRadius) and enough padding
    // that text is not jammed against the border.
    private static StyleBoxFlat ControlBox(Color fill, Color? border = null)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border ?? Palette.SurfaceEdge,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        box.SetBorderWidthAll(border is null ? 1 : Palette.BorderWidth);
        box.SetCornerRadiusAll(Palette.ControlCornerRadius);
        return box;
    }

    // A panel: the card-stock treatment, matching CardStyle so a themed panel and a hand-styled
    // card agree on radius and border weight.
    private static StyleBoxFlat PanelBox(Color fill)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = Palette.SurfaceEdge };
        box.SetBorderWidthAll(Palette.BorderWidth);
        box.SetCornerRadiusAll(Palette.CornerRadius);
        return box;
    }

    private static StyleBoxFlat FlatBox(Color fill)
    {
        var box = new StyleBoxFlat { BgColor = fill };
        box.SetCornerRadiusAll(3);
        return box;
    }
}
