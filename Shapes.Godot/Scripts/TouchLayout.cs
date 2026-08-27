using Godot;

namespace Shapes.Godot.Scripts;

// Positioning fixes the shared Theme cannot make (PLAN.md D7c): pushing corner-anchored
// controls clear of the physical screen edge.
//
// DELIBERATELY NOT PART OF UiTheme. D7c is explicit that the inset is a POSITIONING concern, not a
// styling one, and UiTheme's cached Theme is shared by every screen -- folding the inset in there
// would carry it to desktop. Keeping it here means the desktop gate is a single early return in a
// file desktop never enters.
//
// EVERY METHOD IS A NO-OP ON DESKTOP, by an early return rather than by arithmetic that happens to
// come out even (see Platform's header). That makes "the .tscn offsets are genuinely untouched" a
// property that can be asserted, not eyeballed.
public static class TouchLayout
{
    // Applies the safe-area inset to a screen's root MarginContainer, additively.
    //
    // ADDITIVE, NOT RECOMPUTED (D7c, belt and braces). The margins already in the .tscn were tuned
    // against the desktop window; adding to them preserves that tuning and means no rounding
    // difference can shift a desktop layout at a non-default window size.
    public static void InsetMargins(MarginContainer layout)
    {
        if (!Platform.IsTouch || layout is null)
        {
            return;
        }

        var inset = Platform.SafeAreaInset();

        AddMargin(layout, "margin_left", inset.X);
        AddMargin(layout, "margin_top", inset.Y);
        AddMargin(layout, "margin_right", inset.Z);
        AddMargin(layout, "margin_bottom", inset.W);
    }

    private static void AddMargin(MarginContainer layout, string constant, float extra)
    {
        if (extra <= 0f)
        {
            return;
        }

        // GetThemeConstant, not the override: the .tscn sets only some of the four, and reading the
        // override for an unset one returns nothing. The resolved theme value is the number the
        // container is actually laying out against, which is what we mean to add to.
        var current = layout.GetThemeConstant(constant);
        layout.AddThemeConstantOverride(constant, current + Mathf.RoundToInt(extra));
    }

    // The same inset for a screen whose root is a full-rect ANCHORED control rather than a
    // MarginContainer -- Deckbuilder and CardBrowser both anchor their "Layout" to all four edges
    // with offsets, so there are no margin constants to add to and the offsets themselves move.
    //
    // Additive for the same reason InsetMargins is: the .tscn offsets carry the desktop tuning, and
    // adding to them preserves it. Each edge moves INWARD, which for the right and bottom offsets
    // (measured negatively from their anchored edge) means subtracting.
    public static void InsetOffsets(Control layout)
    {
        if (!Platform.IsTouch || layout is null)
        {
            return;
        }

        var inset = Platform.SafeAreaInset();

        layout.OffsetLeft += inset.X;
        layout.OffsetTop += inset.Y;
        layout.OffsetRight -= inset.Z;
        layout.OffsetBottom -= inset.W;
    }

    // Pushes corner-anchored controls in from the screen edge (PLAN.md D7c).
    //
    // These need the inset pushed in DIRECTLY rather than inheriting it: they are anchored to
    // BoardView, not to the Layout MarginContainer, so nothing between them and the screen edge
    // carries a margin they could pick up.
    //
    // SIZE IS NOT TOUCHED HERE ANY MORE. An earlier cut also grew these to a 48dp floor while
    // their glyph stayed at its .tscn font size, which is the "text does not scale within the
    // button" symptom. Content scale now grows the button and its glyph together (see Platform),
    // so this is purely a positioning fix -- which is all D7c ever described it as.
    //
    // Applied once from _Ready with NO Resized hook, deliberately (D7c iii): the safe area does not
    // change on a landscape-locked app, and a resize handler would introduce runtime code mutating
    // positions that are currently purely static -- risking the pre-layout (0,0) read that
    // SideRail.Align already documents.
    public static void InsetCornerControls(params Control[] controls)
    {
        if (!Platform.IsTouch || controls is null)
        {
            return;
        }

        var inset = Platform.SafeAreaInset();

        foreach (var control in controls)
        {
            if (control is not null)
            {
                InsetOne(control, inset);
            }
        }
    }

    private static void InsetOne(Control control, Vector4 inset)
    {
        // Which edges a control is pinned to decides which inset applies. Read off the anchors
        // rather than hardcoded per button, so this stays correct if a corner control is ever
        // re-anchored -- and so one helper serves both buttons.
        var pinnedRight = control.AnchorLeft >= 1f;
        var pinnedBottom = control.AnchorTop >= 1f;

        // STRICTLY ADDITIVE (D7c ii): each offset moves by the inset for the edge it is measured
        // from, so the control keeps its .tscn size and only its position changes.
        if (pinnedRight)
        {
            control.OffsetLeft -= inset.Z;
            control.OffsetRight -= inset.Z;
        }
        else
        {
            control.OffsetLeft += inset.X;
            control.OffsetRight += inset.X;
        }

        if (pinnedBottom)
        {
            control.OffsetTop -= inset.W;
            control.OffsetBottom -= inset.W;
        }
        else
        {
            control.OffsetTop += inset.Y;
            control.OffsetBottom += inset.Y;
        }
    }
}
