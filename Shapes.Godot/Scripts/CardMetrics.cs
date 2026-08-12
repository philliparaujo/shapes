using Godot;

namespace Shapes.Godot.Scripts;

// The one place card proportions live (PLAN.md B1c, from references/card dimensions.pdf).
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
    // line -- rather than the cropped art thumbnail it used to be (PLAN.md 5.C-UI). That needs
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
}
