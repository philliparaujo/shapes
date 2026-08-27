using Godot;

namespace Shapes.Godot.Scripts;

// The one place card proportions live (DESIGN.md B1c, from references/card dimensions.pdf).
//
// Every previous round of this work sized each view with its own hand-computed pixel budget, and
// every round some view's real content overflowed a budget that had drifted out of step with the
// other two. The PDF specifies *ratios*, so these derive heights from a single width per view
// instead: change the width and every band moves with it, and no two views can disagree about
// what "7:6" meant.
//
// Reference ratios, read off the PDF's grid:
//
//   In hand   7:6 overall,  7:5 art,   5:1 title,  2:1 cost badge (bleeds off the top-left)
//   Tooltip   7:10 overall, 7:5 art,   5:2 move,   2:2 HP
//   In play   10:8 body + 10:1 status bar (10:9 together), 10:5 full art / 5:3 merge art,
//             2:1 resource type + 8:1 title, 5:2 move
public static class CardMetrics
{
    // --- In hand -----------------------------------------------------------------------------
    // A hand card is now a FULL card face -- cost badge, name, art, effects, moves and a stat
    // line -- rather than the cropped art thumbnail it used to be (DESIGN.md 5.C-UI). That needs
    // the tooltip's taller 7:10 proportion, not the old 7:6: at 7:6 the move rows had nowhere to
    // go and were clipped away, which is exactly the "cropped card" this replaced.
    public const float HandWidth = 170f;
    public const float HandHeight = HandWidth * 10f / 7f;         // 7:10, as the tooltip
    public const float HandTitleHeight = HandWidth / 5f;          // 5:1
    public const float HandArtHeight = HandWidth * 5f / 7f;       // 7:5
    public const float HandCostBadge = HandWidth * 2f / 7f / 2f;  // 2:1 pip, half-width so it
                                                                  // reads as a corner badge

    // --- Tooltip -----------------------------------------------------------------------------
    public const float TooltipWidth = 240f;
    public const float TooltipHeight = TooltipWidth * 10f / 7f;   // 7:10
    public const float TooltipArtHeight = TooltipWidth * 5f / 7f; // 7:5
    public const float TooltipHpHeight = TooltipWidth * 2f / 7f;  // 2:2-ish band for the HP line
    public const float TooltipCostBadge = 32f;

    // 5:2 means the move BLOCK is two-fifths as tall as it is wide -- a row spanning the card's
    // full width is 240 wide, so 96px tall would be enormous and is what made the tooltip's move
    // list swallow the card. The PDF's 5:2 move sits in a column beside the pip, so the height
    // that matters is the text block's: name line + wrapped description at the shared font sizes.
    public const float TooltipMoveHeight = 34f;

    // --- In play -----------------------------------------------------------------------------
    // Widened from 260: at that size a 2-column move grid gave ~124px cells, which truncated the
    // longest real move name ("Unopposed Growth", ~105px at MoveNameFontSize, plus the cost pip
    // and separations) and left descriptions too short to wrap. The card scales, the text does
    // not -- font sizes are shared with the tooltip and stay put.
    public const float SlotWidth = 330f;
    public const float SlotBodyHeight = SlotWidth * 8f / 10f;     // 10:8
    public const float SlotStatusHeight = SlotWidth / 10f;        // 10:1
    public const float SlotHeight = SlotBodyHeight + SlotStatusHeight; // 10:9 together
    public const float SlotHeaderHeight = SlotWidth / 10f;        // 8:1 title / 2:1 type badge
    public const float SlotTypeBadge = 18f;

    // A creature can show at most 4 moves (RuleSet.MaxMergeDepth 2 x 2 moves per card), laid out
    // as a 2x2 grid rather than a single column -- which is what makes the PDF's numbers fit. In
    // one column, four 5:2 blocks at full card width need ~128px of a 208px body and leave no
    // room for a 10:5 art band; in two columns the same four moves occupy two rows, so the move
    // area is half as tall and the art band keeps its share.
    public const int MoveColumns = 2;
    public const int MaxMovesOnBoard = 4;
    public const int MaxMoveRows = MaxMovesOnBoard / MoveColumns;

    // Each cell is half the card width (less the grid separation), and 5:2 on that width.
    public const float SlotMoveWidth = (SlotWidth - 12f) / MoveColumns;
    public const float SlotMoveHeight = SlotMoveWidth * 2f / 5f;
    public const float SlotArtHeightMin = 80f;

    // Inner margin applied to the card body and the tooltip alike, so text never sits flush
    // against a card edge (HP, spell effects and move descriptions all did before).
    public const int CardPadding = 8;

    // --- Shared ------------------------------------------------------------------------------
    public const int MoveNameFontSize = 11;
    public const int MoveDescriptionFontSize = 9;
    public const int TitleFontSize = 12;
    public const int HpFontSize = 12;

    public static Vector2 HandCardSize => new(HandWidth, HandHeight);

    public static Vector2 TooltipSize => new(TooltipWidth, TooltipHeight);

    public static Vector2 SlotSize => new(SlotWidth, SlotHeight);

    // --- Board fit (DESIGN.md D7b) ---------------------------------------------------------------
    //
    // THE MEASUREMENT THAT DECIDED THIS, and it inverts D7b's stated premise. D7b assumed the
    // phone's 2.17 aspect makes the binding constraint vertical, and proposed deriving the slot
    // size from available height. Measured against the actual stretch settings, that is not what
    // happens.
    //
    // Under stretch/mode="canvas_items" with aspect="expand", Godot scales by min(w/1600, h/1000).
    // Every landscape phone is wider than the 1.6 design aspect, so HEIGHT is always the limiting
    // axis and the canvas is always exactly 1000 units tall -- on a 2340x1080 phone, a 2400x1080
    // phone and the 1600x1000 desktop window alike. The aspect change shows up purely as extra
    // canvas WIDTH (1600 -> ~2167).
    //
    // Two consequences, and together they answer D7b's open number ("compute what slot size a
    // 2340x1080 landscape phone actually affords"):
    //
    //   1. The vertical budget is IDENTICAL on both platforms, and it is already spent. Between the
    //      96-unit top margin and the 210-unit hand band, two rows plus their margins afford a
    //      334x301 slot against today's 330x297 -- four pixels. Scaling the slot up is therefore
    //      not worth breaking the desktop-identical constraint for, and scaling it from height
    //      would compute the SAME number on desktop and phone, i.e. change nothing at all. That is
    //      exactly the "compiles clean while being visibly wrong" failure D7 warns about twice.
    //
    //   2. The slack a phone actually gains is horizontal -- and the visible bug is not that the
    //      slots are too small but that the board is centred in the WRONG BOX. BoardArea
    //      shrink-centres within Layout, which spans the full canvas width, while the side rail is
    //      anchored over the right end of that same span. At 1600 the board lands 41 units clear of
    //      the rail, which is the tuned desktop look. At 2167 the same centring puts it 324 units
    //      clear on the right and 548 units of dead space on the left: the board visibly drifts
    //      right and the screen reads as half-empty. That is D6's "fixed centred block with large
    //      empty margins", and it is a centring bug, not a sizing one.
    //
    // So the fix is to centre the board in the space LEFT OF THE RAIL rather than in the whole
    // canvas, and to leave the slot size alone. The slot stays a compile-time const, every ratio in
    // this file is untouched, and SlotView.tscn keeps its 330x297 -- which also means D7a's
    // move-button sizing and the clipped-move-row bug this file documents from the 7:6 era are
    // both left undisturbed.
    //
    // How much the whole board subtree is scaled up on touch, and the region it is centred in
    // (DESIGN.md D7). Desktop never calls these -- BoardView gates on Platform.IsTouch.
    //
    // A UNIFORM SCALE ON THE SUBTREE, not a new slot width. SlotView.tscn pins 330x297 on its root
    // and four more fixed band heights inside it (264 body, 26 header, 80 art, 33 status), so
    // widening only the root would stretch the frame around bands that stayed put. Scaling the
    // subtree grows the slots, their art, their move rows and their fonts together, in the exact
    // 10:9 proportion the card was drawn to -- which is what "scale up the entire board with the
    // same aspect ratio" means, and it keeps every ratio in this file untouched.
    //
    // FLOORED AT 1.0, never below. The design dimensions are the minimum: this file holds font
    // sizes constant while cards scale, so shrinking re-creates the clipped-move-row bug documented
    // from the 7:6 era. If a device's board region is smaller than the desktop budget, the board
    // keeps its authored size and simply uses the space it has.
    public static float BoardScale(float regionHeight)
    {
        var natural = BoardContentHeight;
        return natural <= 0f ? 1f : Mathf.Max(1f, regionHeight / natural);
    }

    // The board's own laid-out height at the design size: two slot rows, the gap between them, and
    // the margin around the pair. The number BoardScale measures the available region against.
    public static float BoardContentHeight =>
        (2f * SlotHeight) + RowSeparation + (BoardInnerMargin * 2f);

    // The rail's authored width, less the distance BoardView shifts it left on touch to clear the
    // corner buttons. This is the width the board must stay out of when it centres horizontally.
    public static float SideRailReserve => 224f;

    private const float RowSeparation = 40f;    // BoardView.tscn, Rows separation
    private const float BoardInnerMargin = 26f; // BoardView.tscn, RowsMargin
}
