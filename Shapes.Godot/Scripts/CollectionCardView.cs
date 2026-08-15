using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// A card as it looks IN THE COLLECTION (PLAN.md C2): the full card face, clickable, with a
// copies-in-deck badge in the corner.
//
// The collection and the decklist deliberately no longer share one row type. They are asking two
// different questions and the earlier shared DeckRowView only ever answered the second one well:
// the decklist answers "what is in this deck" (forty entries, scanned as a column, where a
// cropped art sliver and a name is exactly right -- see DeckRowView's header), while the
// collection answers "what could I put in it," which is a question about what cards DO, and a
// name plus a sliver cannot answer it. Making the two sides look different is also what stops
// them reading as one undifferentiated wall of near-identical bands, which is the state this
// replaces.
//
// NOT a fourth card renderer. The face here is a real HoverDetailPanel -- the same scene the
// board's hover, the hand's tooltip and CardBrowser's grid cells all instantiate -- so a card
// looks IDENTICAL in the collection and everywhere else, and a change to card layout still
// lands in exactly one place. This class contributes only what the collection adds on top: the
// click target and the copies badge.
//
// A Button wrapping the panel rather than a panel with input handling bolted on, for the same
// reason DeckRowView is a Button: left click adds a copy, right click removes one, and that has
// to be the same gesture on both sides of the screen. HoverDetailPanel sets its own MouseFilter
// to Ignore (see its header -- a tooltip that could be hovered would fight the gesture it
// describes), which is precisely what lets it sit inside a Button without swallowing the click.
public partial class CollectionCardView : Button
{
    // The face is a fixed-size scene, so the cell is too -- the grid column count is computed
    // against this width (Deckbuilder.CollectionColumns).
    public static Vector2 CellSize => CardMetrics.TooltipSize;

    private const int BadgeFontSize = 15;
    private const float BadgeSize = 30f;
    private const float BadgeInset = 4f;

    // Card stock, but a shade lighter than CardStyle.StockColor -- the panel inside this button
    // already paints real card stock, so this outer rect is the MOUNT the card sits on and has to
    // separate from it. Selected (in-deck) cards take the lifted fill, which is what makes "what
    // have I already got" answerable by scanning for the lighter tiles.
    private static readonly Color CellFill = new("232830");
    private static readonly Color CellEdge = new("11151a");
    private static readonly Color InDeckFill = new("2f3a46");
    private static readonly Color InDeckEdge = new("6f8aa8");

    // The copies badge, which is not the same object as a cost pip and must not read as one --
    // hence a plain rounded chip in neutral grey rather than a resource shape.
    private static readonly Color BadgeFill = new(0.05f, 0.07f, 0.09f, 0.88f);
    private static readonly Color BadgeText = new("e8eef6");
    private static readonly Color MaxedText = new("f0c674");

    [Signal] public delegate void AddRequestedEventHandler(string cardId);
    [Signal] public delegate void RemoveRequestedEventHandler(string cardId);

    private string _cardId = "";

    public string CardId => _cardId;

    // `copies` is how many of this card the CURRENT deck runs, and `maxCopies` the per-card
    // limit -- passed rather than read from a ruleset here so this view stays a view. At the
    // limit the badge recolors instead of the cell going disabled: a maxed card is still a
    // legitimate right-click target for removing one, so greying it out would take away the only
    // gesture that can un-max it.
    public void Render(CardText card, int copies, int maxCopies, PackedScene faceScene)
    {
        System.ArgumentNullException.ThrowIfNull(card);
        System.ArgumentNullException.ThrowIfNull(faceScene);

        _cardId = card.CardId;

        CustomMinimumSize = CellSize;
        ClipContents = true;
        TooltipText = "";

        var inDeck = copies > 0;
        CardStyle.ApplyToButton(this, inDeck ? InDeckFill : CellFill, inDeck ? InDeckEdge : CellEdge);

        // Same trap ButtonContentHost exists for: a Button never sorts its children, so the face
        // has to be positioned by something that tracks this button's real size.
        var host = new ButtonContentHost();
        AddChild(host);

        // The face is a scene ROOT, which carries HoverDetailPanel.tscn's own bottom-left anchors
        // -- right for the board's corner-parked tooltip, wrong for a grid cell. ButtonContentHost
        // zeroes those on every layout pass; see its Apply for why that has to happen there and
        // not here.
        var face = faceScene.Instantiate<HoverDetailPanel>();

        // The same scene also carries z_index 100, so that the board's tooltip floats over every
        // card and slot it might overlap. Inside a cell that is actively wrong: z-index beats
        // tree order, so the face painted over the copies badge that is meant to sit on top of it
        // -- a badge correct in a rect dump and invisible on screen. Reset to 0 so this cell's
        // layers stack in the order they were added.
        face.ZIndex = 0;

        host.SetContent(face);

        // Show only works once the panel's _Ready has resolved its child references, which
        // happens on entering the tree -- so the AddChild above has to land before this call
        // (the same ordering CardBrowser.BuildOriginalCell documents for its own cells).
        face.Show(card);

        if (inDeck)
        {
            AddChild(BuildBadge(copies, copies >= maxCopies));
        }

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

    // Top-right, over the card face rather than beside it -- the cell is already exactly one
    // card wide and a badge given its own column would shrink the face it annotates. Anchored
    // (not laid out by a container) because it deliberately overlaps its sibling, which is the
    // one thing a container cannot express.
    //
    // Anchors pinned to the right edge with offsets measured back from it, all four set
    // explicitly: a PanelContainer with a CustomMinimumSize but a half-specified rect gets its
    // size from the anchor span rather than the minimum, which is how an earlier cut of this drew
    // the chip at zero width -- present in the tree, correct in a rect dump, invisible on screen.
    private static Control BuildBadge(int copies, bool maxed)
    {
        var chip = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 1f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 0f,
            OffsetLeft = -(BadgeSize + BadgeInset),
            OffsetTop = BadgeInset,
            OffsetRight = -BadgeInset,
            OffsetBottom = BadgeSize + BadgeInset,
        };

        var box = new StyleBoxFlat { BgColor = BadgeFill };
        box.SetCornerRadiusAll((int)(BadgeSize / 2f));
        chip.AddThemeStyleboxOverride("panel", box);

        var label = new Label
        {
            Text = $"x{copies}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", BadgeFontSize);
        label.AddThemeColorOverride("font_color", maxed ? MaxedText : BadgeText);

        chip.AddChild(label);
        return chip;
    }
}
