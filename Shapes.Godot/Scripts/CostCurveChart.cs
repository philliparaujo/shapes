using System.Collections.Generic;
using Godot;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Scripts;

// The deck's mana curve, as a stacked bar per cost (PLAN.md C2's deckbuilding tab).
//
// The one deck statistic that is a SHAPE rather than a number. Deckbuilder's stats line already
// prints the same totals this chart sums (Spike/Anvil/Wheel counts, mean cost), and printed
// totals answer "how much spike am I running"; they cannot answer "is my curve top-heavy," which
// is the question a player actually re-asks after every single card edit. A bar per cost answers
// it at a glance and answers it in the same place the edit happened.
//
// STACKED BY RESOURCE TYPE, not one flat bar per cost, because this game's curve has a second
// axis a Hearthstone curve does not: a deck can be perfectly curved and still unplayable if all
// of its two-drops demand spike while the deck's engine is wheel. Segmenting each bar by the
// type its cards are paid in makes that visible without a second chart -- the cost axis reads
// left to right, the type mix reads bottom to top within each column.
//
// Custom _Draw rather than a row of ColorRect/Panel nodes: the whole chart is a dozen
// rectangles and three text runs that all rescale together whenever the deck changes, and node
// churn on every keystroke-fast card edit (Deckbuilder.CommitEdit rebuilds everything) is the
// cost DeckRowView's own header already weighs. A redraw is one Godot call; twelve nodes is not.
//
// Colors come from ResourceIconFactory, not a local palette -- a wheel segment here is the same
// blue as a wheel pip on a card, which is the whole reason the stack is readable without a
// legend beyond the axis labels.
public partial class CostCurveChart : Control
{
    // Costs 1..6, with anything above folded into the last column as "6+". Real cards top out at
    // 5 today, so the 6+ bucket is nearly always empty -- it exists so a future high-cost card
    // has a column to land in rather than silently vanishing from a chart that claims to show
    // the whole deck.
    public const int MinCost = 1;
    public const int MaxCost = 6;
    private const int Buckets = MaxCost - MinCost + 1;

    private const float LabelBandHeight = 14f;
    private const float CountBandHeight = 12f;
    private const int AxisFontSize = 10;
    private const int CountFontSize = 10;
    private const float BarSeparation = 6f;
    private const float BarCorner = 2f;

    // The floor a non-empty column draws at, so a one-card bucket is a visible sliver rather
    // than a sub-pixel line indistinguishable from an empty column.
    private const float MinBarHeight = 3f;

    private static readonly Color AxisColor = new("8a94a2");
    private static readonly Color TrackColor = new("272d35");
    private static readonly Color CountColor = new("d8dee6");

    // counts[bucket][type]. Rebuilt wholesale by SetCounts -- there is no incremental path,
    // matching the full-rebuild policy the rest of the deckbuilder edits under.
    private readonly int[,] _counts = new int[Buckets, 3];
    private int _peak;

    public override void _Ready() => Resized += QueueRedraw;

    // `cardCosts` is one entry per CARD IN THE DECK (copies included, not distinct names) -- the
    // curve is about what you will actually draw, so three copies of a two-drop are three cards
    // tall, not one. A null type (a hypothetical typeless card) is counted into the bucket's
    // height but drawn in no type's color; see DrawBar.
    public void SetCounts(IEnumerable<(int Cost, ResourceType? Type)> cardCosts)
    {
        System.Array.Clear(_counts);
        _peak = 0;

        foreach (var (cost, type) in cardCosts)
        {
            if (type is not { } t)
            {
                continue;
            }

            var bucket = Mathf.Clamp(cost, MinCost, MaxCost) - MinCost;
            _counts[bucket, (int)t]++;
        }

        for (var bucket = 0; bucket < Buckets; bucket++)
        {
            _peak = Mathf.Max(_peak, TotalIn(bucket));
        }

        QueueRedraw();
    }

    private int TotalIn(int bucket)
    {
        var total = 0;
        foreach (var type in ResourceTypes.All)
        {
            total += _counts[bucket, (int)type];
        }

        return total;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var chartTop = CountBandHeight;
        var chartHeight = Mathf.Max(1f, Size.Y - LabelBandHeight - CountBandHeight);
        var slot = (Size.X + BarSeparation) / Buckets;
        var barWidth = Mathf.Max(1f, slot - BarSeparation);

        // Every column scales against the TALLEST column, not against the deck size: a 40-card
        // deck spread over five costs never fills a bar scaled to 40, and the differences
        // between columns -- which is the whole point of a curve -- would compress to nothing.
        var peak = Mathf.Max(1, _peak);

        for (var bucket = 0; bucket < Buckets; bucket++)
        {
            var x = bucket * slot;
            DrawBar(bucket, x, barWidth, chartTop, chartHeight, peak, font);
            DrawAxisLabel(bucket, x, barWidth, font);
        }
    }

    private void DrawBar(
        int bucket, float x, float width, float top, float height, int peak, Font font)
    {
        // The empty track behind every column, drawn whether or not the column has cards: it is
        // what makes an EMPTY cost read as a deliberate gap in the curve rather than as a
        // missing column the chart forgot to draw.
        DrawRect(new Rect2(x, top, width, height), TrackColor);

        var total = TotalIn(bucket);
        if (total == 0)
        {
            return;
        }

        var barHeight = Mathf.Max(MinBarHeight, height * total / peak);
        var y = top + height - barHeight;

        // Segments stack from the bottom up in a FIXED type order (Spike, Anvil, Wheel -- the
        // ResourceTypes.All order every other view lists them in), never sorted by size: a
        // segment that moved between columns because it happened to be the largest there would
        // make the three colors impossible to track across the chart.
        var drawn = 0;
        foreach (var type in ResourceTypes.All)
        {
            var count = _counts[bucket, (int)type];
            if (count == 0)
            {
                continue;
            }

            var segmentTop = y + barHeight * (total - drawn - count) / total;
            var segmentBottom = y + barHeight * (total - drawn) / total;
            drawn += count;

            var box = new StyleBoxFlat { BgColor = ResourceIconFactory.ColorOf(type) };
            box.SetCornerRadiusAll((int)BarCorner);
            DrawStyleBox(box, new Rect2(x, segmentTop, width, segmentBottom - segmentTop));
        }

        // The column's total above the bar. A stacked bar shows proportion well and absolute
        // count badly -- "is this four cards or six" is exactly the question a curve gets asked
        // while trimming a deck to 40, and reading it off a bar's height against no gridlines is
        // guesswork.
        DrawString(
            font, new Vector2(x, y - 2f), total.ToString(), HorizontalAlignment.Center,
            width, CountFontSize, CountColor);
    }

    private void DrawAxisLabel(int bucket, float x, float width, Font font)
    {
        var cost = bucket + MinCost;
        var text = cost == MaxCost ? $"{cost}+" : cost.ToString();

        DrawString(
            font, new Vector2(x, Size.Y - 3f), text, HorizontalAlignment.Center,
            width, AxisFontSize, AxisColor);
    }
}
