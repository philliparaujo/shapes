using System;
using Godot;
using Godot.Collections;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// A card as it looks IN A DECKLIST (PLAN.md C2): a short, wide band showing cost, art and name
// in one line, plus the copy count.
//
// THE FOURTH CARD VIEW, alongside in-hand, in-play and tooltip -- and the only one whose job is
// to be scanned forty at a time rather than read one at a time. That is what drives every choice
// here: a fixed row height so a decklist is a regular column, the name on the same line as the
// cost rather than beneath it, and a cropped art sliver instead of the full 7:5 pane. The other
// three views all answer "what does this card do"; this one answers "what is in this deck," and
// rendering a full CardFace forty times over would answer the wrong question at forty times the
// node cost (the same cost CardBrowser's own header measures and paginates around).
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
    // 2:1 art, per CardMetrics' authored ratio -- the same sliver the in-play view crops, so the
    // subject stays centered (see CardArt's KeepAspectCovered note).
    public const float RowHeight = 34f;
    private const float ArtWidth = RowHeight * 2f;
    private const int CostBadgeSize = 22;
    private const int NameFontSize = 15;
    private const int CountFontSize = 15;

    // Dimmer than CardStyle.StockColor: forty rows of full card stock reads as a wall, and these
    // sit inside a panel that is itself card stock. Same hue, lifted slightly so a row separates
    // from the list behind it without becoming its own card.
    private static readonly Color RowFill = new("343b44");
    private static readonly Color RowEdge = new("1b2026");
    private static readonly Color CountColor = new("d8dee6");

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
        var host = new ButtonContentHost();
        AddChild(host);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        host.SetContent(row);

        row.AddChild(BuildCostBadge(card));
        row.AddChild(BuildArt(card));
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

    // The cost pip, at the badge size the row height allows -- the same ResourceIconFactory shape
    // every other view uses, so a card's cost reads identically here and on the board.
    private static Control BuildCostBadge(CardText card)
    {
        var holder = new CenterContainer
        {
            CustomMinimumSize = new Vector2(CostBadgeSize + 8f, RowHeight),
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

    // ShrinkBegin, not the default: CardArt.For returns a TextureRect flagged ExpandFill (it is
    // authored for a full card face, where the art SHOULD take the space), and an expanding child
    // in this row grows the art holder until the name label is squeezed to zero width -- which,
    // with TrimEllipsis, renders as nothing at all rather than as a visibly clipped name. Pinning
    // the holder to a fixed width is what keeps the art a thumbnail and leaves the row's slack to
    // the name, which is the column that actually needs it.
    private static Control BuildArt(CardText card)
    {
        var holder = new MarginContainer
        {
            CustomMinimumSize = new Vector2(ArtWidth, RowHeight),
            ClipContents = true,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        holder.AddChild(CardArt.For(card.CardId, card.PrimaryType ?? ResourceType.Spike));
        return holder;
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
            CustomMinimumSize = new Vector2(120f, RowHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        label.AddThemeFontSizeOverride("font_size", NameFontSize);
        return label;
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
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(32f, RowHeight),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        label.AddThemeFontSizeOverride("font_size", CountFontSize);
        label.AddThemeColorOverride("font_color", CountColor);
        return label;
    }
}
