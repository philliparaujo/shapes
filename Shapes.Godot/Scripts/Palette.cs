using Godot;

namespace Shapes.Godot.Scripts;

// The one place a colour is named (DESIGN.md D3 phase 1).
//
// WHY THIS EXISTS. Before it, ~80 colour literals lived across 19 scripts and 117 theme_overrides
// across the scenes, with no shared source -- so "what colour is a panel" had as many answers as
// there were files, and a new screen either re-specified the whole look or inherited Godot's
// defaults. The lobby is what that costs: it carries six theme_overrides, all of them spacing and
// font size, not one colour or panel, which is exactly why it renders as stock grey next to a board
// screen that had a full styling pass.
//
// NOT A NEW SCHEME. Every value below is one already in use, moved rather than invented -- the card
// stock from CardStyle, the felt and gold frame from BoardFrame, the backdrop ramp from
// TableBackdrop, the damage/heal/score cues from BoardAnimator. Naming them is the whole change;
// re-picking them would have made this a redesign and thrown away the tuning already done against
// real art.
//
// The scale is deliberately shallow. Surface/Ink/Accent/Semantic is enough to describe every screen
// in this project, and a deeper token set (surface-1..9, on-surface-variant, and so on) would be
// more vocabulary than there are decisions.
public static class Palette
{
    // ---- Surfaces: the ramp everything sits on, darkest to lightest. --------------------------

    // The window behind everything (TableBackdrop's own gradient interpolates between these).
    public static readonly Color BackdropTop = new("23272e");
    public static readonly Color BackdropMid = new("2e343d");
    public static readonly Color BackdropBottom = new("191c21");

    // Card stock, and the edge every card/panel is outlined with.
    public static readonly Color Surface = new("2b3138");
    public static readonly Color SurfaceEdge = new("11151a");

    // One step up from card stock: rails, tooltips, and the note panels that sit ON cards.
    public static readonly Color SurfaceRaised = new("3a424c");

    // One step down: inset wells, rows inside a list, the deckbuilder's cells.
    public static readonly Color SurfaceInset = new("232830");

    // Interactive surfaces -- a button at rest, hovered, and pressed.
    //
    // FELT AND GOLD, not neutral grey. The board is the game's signature surface (green felt inside
    // a gold frame) and a grey button beside it reads as generic UI bolted onto a themed game. These
    // are the felt darkened toward the app's own backdrop so a button still recedes against a card,
    // with the gold arriving on the border and on hover -- which is where the board puts it too.
    // Lifted a step from the first felt values (2f4438 / 3c5748 / 243528): against a disabled
    // control they were close enough that "can I press this" needed a second look. The enabled
    // resting state now sits clearly above ControlDisabled, and hover above that again.
    public static readonly Color Control = new("4d7757");
    public static readonly Color ControlHover = new("5d8d69");
    public static readonly Color ControlPressed = new("3c6244");

    // Clearly BELOW the resting surface, not a near-match to it. A disabled control has to be
    // separable from an enabled one at a glance -- the rules overlay puts a disabled "< Prev" right
    // beside an enabled "Next >", and at only a shade apart the pair read as the same control
    // twice. Paired with InkDisabled, which does most of the work.
    //
    // Desaturated out of the felt as well as darkened: a disabled control should not keep the green
    // that says "this is live", or it reads as merely dim rather than as unavailable.
    public static readonly Color ControlDisabled = new("242a28");

    // The button border at rest and the gold it takes on hover/focus. A thin gold edge is what ties
    // a button to the board's frame without flooding the screen with the accent.
    public static readonly Color ControlBorder = new("1c2a20");
    public static readonly Color ControlBorderHover = new("9b7f52");

    // ---- Ink: text, in three weights. ---------------------------------------------------------

    // Titles and card names.
    public static readonly Color Ink = new("f4f7fb");

    // Body text and labels.
    public static readonly Color InkMuted = new("d6dae0");

    // Secondary text: descriptions, hints, the quieter half of a two-line row.
    public static readonly Color InkDim = new("8a94a2");

    // Text on a disabled control. Dim enough to read as unavailable without vanishing -- a disabled
    // control still has to be legible, since its label is often what explains why it is disabled.
    public static readonly Color InkDisabled = new("5b636e");

    // ---- Accent: the game's own gold, used for emphasis and focus. ----------------------------

    public static readonly Color Accent = new("f0c674");
    public static readonly Color AccentDim = new("c9a677");

    // The board's felt and its gold frame. Only BoardFrame draws these, but they belong to the
    // scheme rather than to that one file.
    public static readonly Color BoardFelt = new("3f5a48");
    public static readonly Color BoardFeltDark = new("2b4033");
    public static readonly Color BoardFrameBody = new("6b5335");
    public static readonly Color BoardFrameLight = new("9b7f52");
    public static readonly Color BoardFrameDark = new("38281a");

    // ---- Semantic: what a thing MEANS, not what it looks like. --------------------------------
    //
    // Kept separate from the surface ramp because these must stay legible against any of it, and
    // because a balance change or a new cue should reach for "damage" rather than for a hex value.

    public static readonly Color Damage = new("ff5a5a");
    public static readonly Color Heal = new("5ad17a");
    public static readonly Color Score = new("ffd479");
    public static readonly Color Merge = new("ffc94a");
    public static readonly Color Warning = new("d68f8f");

    // A move already used this turn (DESIGN.md D2 item 3) -- amber, on an axis none of the "cannot
    // use this" fades touch, so it survives any amount of dimming.
    public static readonly Color Spent = new(0.65f, 0.42f, 0.10f);
    public static readonly Color SpentDim = new(0.52f, 0.37f, 0.08f);

    // ---- Shape: the geometry that goes with the colours. ---------------------------------------

    public const int BorderWidth = 2;
    public const int CornerRadius = 8;

    // Smaller radius for controls inside a panel, so a button in a card does not echo the card's
    // own corner and blur the nesting.
    public const int ControlCornerRadius = 5;
}
