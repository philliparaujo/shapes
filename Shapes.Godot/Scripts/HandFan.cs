using System;
using Godot;

namespace Shapes.Godot.Scripts;

// The player's hand, laid out as an arc rather than a row (PLAN.md 5.C-UI, from
// references/game screen.png).
//
// This is a manual-layout Control, not a container, for three reasons an HBoxContainer cannot
// satisfy: cards must OVERLAP (a container always allocates disjoint rects), each card must be
// ROTATED to sit tangent to the arc (containers own their children's rotation and reset it on
// every sort), and the whole fan must CAP its width regardless of hand size (a container grows
// without bound, which is what forced the old ScrollContainer).
//
// Deliberately a Control, not a Container: Container's own sort would fight the manual placement
// (it re-lays-out children and clears their rotation on every sort pass). The cost is having to
// re-run the layout by hand, which Relayout below does -- called on Resized as well as after a
// deal, so the fan survives a window resize rather than baking in the size it was first dealt at
// (the same trap ResourceIconFactory's header documents for anchors/offsets set at construction).
public partial class HandFan : Control
{
    // The fan never grows past this, no matter how many cards are held: past the point where
    // MaxSpread/count drops below a card width the cards simply overlap further. Sized to leave
    // HoverDetailPanel's fixed bottom-left box clear (see PlayerPanel.RenderHand). Widened along
    // with the card itself (140 -> 170) so a large hand overlaps no harder than it used to.
    public const float MaxSpread = 880f;

    // Total sweep of the arc, and how far the middle of the fan rises above its ends. Both are
    // deliberately gentle: the reference art fans ~5 degrees per card, and a steeper arc pushes
    // the outer cards' top corners out of the panel's vertical budget.
    private const float MaxTotalArcDegrees = 26f;
    private const float PerCardArcDegrees = 5f;
    private const float ArcRisePixels = 18f;

    // Cards may overlap down to this fraction of their width still showing. Below it a card's
    // cost badge and name -- the only parts readable at a glance -- start to disappear under its
    // neighbour, so the fan stops tightening and simply gets no wider.
    private const float MinVisibleFraction = 0.34f;

    public override void _Ready()
    {
        Resized += LayoutFan;
    }

    // Re-runs the arc placement. Called by PlayerPanel once it has finished adding this hand's
    // CardFaces, since a plain AddChild raises nothing a Control listens for.
    public void Relayout() => LayoutFan();

    // How much of a card stays on screen. The rest runs off the bottom edge, which is the point:
    // a hand should look like it is held at the screen's lip rather than floating in a reserved
    // band above it.
    //
    // This node is anchored to the window's bottom edge (see BoardView.tscn) rather than flowing
    // in the Layout VBox -- a VBox distributes leftover space among its children, which parked
    // the fan mid-screen no matter what minimum size it reported. Anchored, its rect IS the
    // bottom band, and the overhang below simply falls outside the window.
    public const float VisibleCardFraction = 0.62f;

    // A rotated rectangle is taller than its unrotated self by w*sin(a) at the extremes; half the
    // total sweep is the largest angle any one card takes.
    private static float RotationHeadroom =>
        CardMetrics.HandWidth * Mathf.Sin(Mathf.DegToRad(MaxTotalArcDegrees / 2f));

    private void LayoutFan()
    {
        var cards = GetChildren();
        var count = cards.Count;
        if (count == 0)
        {
            return;
        }

        var cardSize = CardMetrics.HandCardSize;

        // Step between card centres: the natural side-by-side step, tightened until the whole fan
        // fits MaxSpread, but never so tight that less than MinVisibleFraction of a card shows.
        var naturalStep = cardSize.X + CardGap;
        var step = naturalStep;
        if (count > 1)
        {
            var fittedStep = (Mathf.Min(MaxSpread, Size.X) - cardSize.X) / (count - 1);
            step = Mathf.Clamp(fittedStep, cardSize.X * MinVisibleFraction, naturalStep);
        }

        var totalArc = Mathf.Min(MaxTotalArcDegrees, PerCardArcDegrees * (count - 1));
        var centreX = Size.X / 2f;

        // Cards are placed at FULL height but hang below this Control's rect, so the screen edge
        // crops them rather than the card being drawn short. Our rect covers only the visible
        // band (see VisibleCardFraction), so anchoring the card's visible portion to the bottom
        // of that band puts the remainder off-screen.
        var baselineY = Size.Y - cardSize.Y * VisibleCardFraction;

        for (var i = 0; i < count; i++)
        {
            if (cards[i] is not Control card)
            {
                continue;
            }

            // -1 (left end) .. 0 (middle) .. +1 (right end); a single card sits dead centre.
            var t = count == 1 ? 0f : (i / (float)(count - 1)) * 2f - 1f;

            var angle = totalArc / 2f * t;

            // Parabolic rise: highest in the middle, zero at both ends. Cheaper than a true
            // circular arc and visually indistinguishable at this sweep.
            var rise = ArcRisePixels * (1f - t * t);

            // Pin BOTH the minimum and the actual size. Size alone was not enough: a CardFace is
            // a Button whose VBox content (a wrapped title, a longer move list) can demand more
            // than the fan allots, and Godot grows the control to its content's minimum on the
            // next layout pass -- which left taller cards sitting higher than their neighbours
            // even though every card was given the same rect here.
            card.CustomMinimumSize = cardSize;
            card.Size = cardSize;

            // Rotate about the bottom centre -- a card in a real fan pivots where the hand holds
            // it, not around its top-left corner, which is what Godot's default pivot would do
            // and which visibly slides the outer cards sideways as they tilt.
            card.PivotOffset = new Vector2(cardSize.X / 2f, cardSize.Y);
            card.RotationDegrees = angle;

            // t spans -1..+1 across the fan, so half the total span (step * (count-1)) is the
            // distance from the middle to either end.
            var spanFromCentre = step * (count - 1) / 2f;
            card.Position = new Vector2(
                centreX + spanFromCentre * t - cardSize.X / 2f,
                baselineY - rise);

            // Later cards draw over earlier ones, so the fan reads left-to-right with each card
            // tucked behind its right-hand neighbour.
            card.ZIndex = i;
        }
    }

    // Breathing room between cards when the hand is small enough not to need overlapping at all.
    private const float CardGap = 12f;
}
