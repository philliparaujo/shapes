using System;
using Godot;
using Godot.Collections;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// A card as it looks IN A DECKLIST (PLAN.md C2): a short, wide band whose art fills the whole
// rectangle, with the cost pip, name and copy count laid over it.
//
// THE FOURTH CARD VIEW, alongside in-hand, in-play and tooltip -- and the only one whose job is
// to be scanned forty at a time rather than read one at a time. That is what drives every choice
// here: a fixed row height so a decklist is a regular column, and the name on the same line as
// the cost rather than beneath it. The other three views all answer "what does this card do";
// this one answers "what is in this deck," and rendering a full CardFace forty times over would
// answer the wrong question at forty times the node cost (the same cost CardBrowser's own header
// measures and paginates around).
//
// FULL-BLEED ART, not the art thumbnail this row used to place in its own fixed-width column.
// The thumbnail was a hedge against a narrow column: a 2:1 sliver beside a name label reads fine
// at any width, whereas art stretched across a very wide row is a letterboxed strip of one
// card's background with nothing recognizable in it. Now that the decklist column is the NARROW
// one (the collection took the width, since a full card face needs it and a decklist entry does
// not), the row is close enough to the art's authored 2:1 that the subject actually reads -- so
// the art can be the row's background rather than a stamp on it, which is the Hearthstone
// treatment and is far faster to scan by silhouette than a column of identical grey bands.
//
// The text stays legible over arbitrary art via a horizontal scrim (see DeckRowScrim) rather
// than by dimming the whole texture: art that is uniformly darkened to guarantee contrast is
// art that no longer reads as anything, which defeats the point of showing it at full bleed.
//
// Built in code rather than as a .tscn, matching MoveRowFactory/ResourceIconFactory: the whole
// view is one HBox of four children, and a scene file for it would add an editor round trip to
// every change without making the structure any clearer.
//
// A Button, not a Panel, so the row is clickable -- the deckbuilder uses a left click to add a
// copy and a right click to remove one, and hovering shows the full card tooltip through the
// same HoverDetailPanel every other view uses (so the deckbuilder never needs a second
// card-detail renderer of its own).
public partial class DeckRowView : Button
{
    // Taller than the 34px band this replaced: the art is now the row's full background rather
    // than a 68px thumbnail in it, and at 34px a 2:1 crop of a card's centered subject is a
    // letterboxed strip too thin to recognize. 44 is the shortest height at which the real art
    // reads while a forty-card list still fits a screen without excessive scrolling.
    public const float RowHeight = 44f;
    private const int CostBadgeSize = 22;
    private const int NameFontSize = 15;
    private const int CountFontSize = 15;

    // Width of the cost-pip column and the count column. The scrim's opaque band is sized off
    // the former (see BuildScrim) so the pip always sits on solid backing rather than on art.
    private const float CostColumnWidth = CostBadgeSize + 12f;
    private const float CountColumnWidth = 40f;

    // Dimmer than CardStyle.StockColor: forty rows of full card stock reads as a wall, and these
    // sit inside a panel that is itself card stock. Same hue, lifted slightly so a row separates
    // from the list behind it without becoming its own card. Now only visible where the art does
    // not reach (a card with no art yet, and the row's rounded corners), so it is the FLOOR the
    // art sits on rather than the row's own surface.
    private static readonly Color RowFill = new("343b44");
    private static readonly Color RowEdge = new("1b2026");
    private static readonly Color CountColor = new("f0f3f7");
    private static readonly Color NameColor = new("f4f7fb");

    // Text drawn over art needs an outline, not just a scrim: the scrim guarantees contrast
    // against the row's LEFT portion where the name starts, but a long name runs right into the
    // art's bright middle, and an outline is what keeps its tail legible there.
    private static readonly Color TextOutline = new(0f, 0f, 0f, 0.85f);
    private const int TextOutlineSize = 4;

    // Fired with this row's card id. Add/Remove rather than a generic Pressed so the deckbuilder
    // binds intent, not mouse buttons -- which is also what lets the collection list (left adds)
    // and the decklist (left adds, right removes) share one row type.
    [Signal] public delegate void AddRequestedEventHandler(string cardId);
    [Signal] public delegate void RemoveRequestedEventHandler(string cardId);
    [Signal] public delegate void HoverStartedEventHandler(string cardId);
    [Signal] public delegate void HoverEndedEventHandler();

    private string _cardId = "";

    // The card this row stands for. Exposed so a caller holding the row can act on it without
    // re-deriving the id from the label text.
    public string CardId => _cardId;

    // `count` is the copies-in-deck badge; pass null in the collection list, where a row is a
    // card that COULD be added rather than one already in the deck.
    public void Render(CardText card, int? count)
    {
        ArgumentNullException.ThrowIfNull(card);

        _cardId = card.CardId;

        CustomMinimumSize = new Vector2(0f, RowHeight);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        TooltipText = "";
        CardStyle.ApplyToButton(this, RowFill, RowEdge);

        // A Button never sorts its children, so the row's content goes through ButtonContentHost
        // -- see its header for why anchors-at-construction renders at zero size instead.
        // Three stacked layers, back to front: art across the whole row, the scrim that keeps
        // text readable over it, then the text itself. Each is its own ButtonContentHost rather
        // than one host holding a container -- Godot draws siblings in tree order, and a
        // container cannot overlap its own children, which is exactly what this layout is.
        AddLayer(BuildArt(card));
        AddLayer(new DeckRowScrim { MouseFilter = MouseFilterEnum.Ignore });

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        AddLayer(row);

        row.AddChild(BuildCostBadge(card));
        row.AddChild(BuildName(card));

        if (count is { } copies)
        {
            row.AddChild(BuildCount(copies));
        }

        MouseEntered += () => EmitSignal(SignalName.HoverStarted, _cardId);
        MouseExited += () => EmitSignal(SignalName.HoverEnded);

        // Left click adds, right click removes. Pressed only reports "a click happened," so the
        // button-specific split has to come from the raw event -- and MouseButtonMask must include
        // Right for a Button to report right clicks at all.
        ButtonMask = MouseButtonMask.Left | MouseButtonMask.Right;
        GuiInput += OnGuiInput;
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } click)
        {
            return;
        }

        if (click.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(SignalName.AddRequested, _cardId);
        }
        else if (click.ButtonIndex == MouseButton.Right)
        {
            EmitSignal(SignalName.RemoveRequested, _cardId);
        }
    }

    // One overlapping layer, sized to the button by its own host. See Render on why three hosts
    // rather than one container.
    private void AddLayer(Control layer)
    {
        var host = new ButtonContentHost();
        AddChild(host);
        host.SetContent(layer);
    }

    // The cost pip, at the badge size the row height allows -- the same ResourceIconFactory shape
    // every other view uses, so a card's cost reads identically here and on the board.
    private static Control BuildCostBadge(CardText card)
    {
        var holder = new CenterContainer
        {
            CustomMinimumSize = new Vector2(CostColumnWidth, RowHeight),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        // A typeless (free) card has no pip to draw; the gap keeps the name column aligned.
        if (card.PrimaryType is { } type)
        {
            holder.AddChild(ResourceIconFactory.Create(
                type, ResourceIconFactory.IconSize.Small, card.CostAmount));
        }

        return holder;
    }

    // The row's background layer, filling it edge to edge -- the opposite of what this method used
    // to do, and the point of the change: CardArt.For already returns a TextureRect flagged to
    // expand (it is authored for a full card face, where the art SHOULD take the space), and this
    // row now wants exactly that rather than the fixed 68px thumbnail column it used to pin the
    // art into.
    //
    // Returned BARE, with no MarginContainer around it, so ButtonContentHost sizes the art
    // control itself. Wrapping it in a container instead (the first cut of this) rendered every
    // row's art invisible: a container sorts its children from its OWN size, the host sets that
    // size imperatively rather than through a layout pass, and KeepAspectCovered computing its
    // crop against a still-zero rect scales the texture to nothing. One less node in the way is
    // also one less thing to size, forty rows over.
    private static Control BuildArt(CardText card)
    {
        var art = CardArt.For(card.CardId, card.PrimaryType ?? ResourceType.Spike);
        art.ClipContents = true;
        return art;
    }

    private static Control BuildName(CardText card)
    {
        // A minimum width is load-bearing, not cosmetic. This label lives in an HBox inside a
        // ButtonContentHost, which sizes the box imperatively from the Button's rect; a Label with
        // no minimum can settle at zero width in that pass, and TrimEllipsis then paints NOTHING
        // rather than a clipped name -- which is exactly how the first cut of this row rendered
        // every card nameless while its cost pip, art and count all drew correctly.
        var label = new Label
        {
            Text = card.Name,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(100f, RowHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        label.AddThemeFontSizeOverride("font_size", NameFontSize);
        ApplyOverText(label, NameColor);
        return label;
    }

    // Shared by the name and the count: both now sit on art rather than on flat stock, and both
    // need the same outline treatment for it (see the class header on why an outline rather than
    // a heavier scrim).
    private static void ApplyOverText(Label label, Color color)
    {
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeConstantOverride("outline_size", TextOutlineSize);
        label.AddThemeColorOverride("font_outline_color", TextOutline);
    }

    // "x3" rather than a bare number: a lone digit beside a name reads as part of the card's
    // stats (the in-play view puts health exactly there), and copies-in-deck is a different
    // quantity entirely.
    private static Control BuildCount(int copies)
    {
        var label = new Label
        {
            Text = $"x{copies}",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(CountColumnWidth, RowHeight),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        label.AddThemeFontSizeOverride("font_size", CountFontSize);
        ApplyOverText(label, CountColor);
        return label;
    }
}

// The gradient that keeps a deck row's text readable over its art.
//
// Opaque behind the cost pip on the left, fading to clear across the row's middle, then darkening
// again under the copy count on the right -- so both text columns sit on solid backing while the
// art's centre, which is where its subject actually is, stays untouched. That asymmetry is why
// this is a custom _Draw and not a StyleBoxFlat: Godot's style boxes take flat colors, and the
// two-ended fade cannot be expressed as one.
//
// Drawn as a run of thin vertical bands rather than a true gradient for the same reason
// CardStyle.DrawCardShadow fakes its blur in bands -- immediate-mode drawing has no gradient
// primitive, and at this width the banding is invisible.
public partial class DeckRowScrim : Control
{
    // How far across the row each end's scrim reaches, as a fraction of row width. The left is
    // wide enough to cover the pip plus the first part of a name; the right only needs the count.
    private const float LeftExtent = 0.55f;
    private const float RightExtent = 0.22f;

    private const int Bands = 24;

    // Near-opaque at the left edge. Tuned against the brightest art in the set (Rally's lit red
    // desert, Enrage's white beam), which is what a name has to stay legible over -- an alpha
    // that reads as plenty over a dark card is transparent over those.
    private const float LeftAlpha = 0.96f;
    private const float RightAlpha = 0.82f;

    // Flat, fully-opaque backing under the cost pip before the fade even starts, as a fraction of
    // row width. Without it the pip sits on whatever the art happens to be, and a colored pip on
    // colored art is the one element here that cannot rely on an outline to separate.
    private const float SolidLeftExtent = 0.1f;

    // A neutral near-black rather than the row fill: this is a shadow cast over the art, and
    // tinting it toward the row's blue-grey stock made the art look colour-shifted rather than
    // shaded, the same failure ResourceShape's header records for lightened fills.
    private static readonly Color ScrimColor = new(0.02f, 0.03f, 0.04f);

    public override void _Ready() => Resized += QueueRedraw;

    public override void _Draw()
    {
        var solid = Size.X * SolidLeftExtent;
        DrawRect(
            new Rect2(0f, 0f, solid, Size.Y),
            new Color(ScrimColor.R, ScrimColor.G, ScrimColor.B, LeftAlpha));

        DrawFade(solid, Size.X * LeftExtent, LeftAlpha, fromLeft: true);
        DrawFade(0f, Size.X * RightExtent, RightAlpha, fromLeft: false);
    }

    // `start` is where the fade begins, measured in from that edge -- the left fade starts where
    // the solid band ends so the two meet without a step.
    private void DrawFade(float start, float extent, float peakAlpha, bool fromLeft)
    {
        if (extent <= 0f)
        {
            return;
        }

        var bandWidth = extent / Bands;
        for (var i = 0; i < Bands; i++)
        {
            // Squared falloff, not linear: a linear fade still reads as a visible straight edge
            // where it meets the art, because perceived brightness does not track alpha linearly.
            var t = 1f - (i / (float)Bands);
            var alpha = peakAlpha * t * t;

            var x = fromLeft ? start + i * bandWidth : Size.X - start - (i + 1) * bandWidth;
            DrawRect(
                new Rect2(x, 0f, bandWidth + 1f, Size.Y),
                new Color(ScrimColor.R, ScrimColor.G, ScrimColor.B, alpha));
        }
    }
}
