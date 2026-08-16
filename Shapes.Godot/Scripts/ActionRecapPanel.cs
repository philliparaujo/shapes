using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// PLAN.md D2 items 2 and 4: the last action taken, held briefly on the left edge then faded out.
//
// WHY THIS EXISTS. The board renders state, not events, so an action's only trace is whatever
// lasting change it made -- and a move's is just a health number somewhere. This panel is the
// event made readable for a few seconds after it happens, which is what makes an AI turn (and,
// later, a remote turn) followable rather than a board that silently rearranging itself.
//
// TWO PRESENTATIONS, ONE PER KIND OF ACTION, and no shared header above either. An earlier cut put
// a captioned rectangle above both, which was wrong twice over: it overlapped the card it captioned
// (the card panel is authored at z_index 100 and wins against an ordinary sibling), and it spent
// vertical space that the left edge does not have once the hover tooltip's keyword explainers grow
// up into this band.
//
//   PLAYED CARD -> the card alone, nothing else. The card face already carries its own name, cost,
//   art and move list; a caption above it saying "Played" and repeating the name was pure
//   duplication, and it is obvious from context that a card that just appeared here was played.
//
//   USED MOVE -> a compact strip: the move name over its creature's name, beside that creature's
//   art. A move has no card face of its own, and showing the whole creature card to say "it used
//   one of these two moves" both buries the answer and costs the height the played-card case needs.
//
// The two are sized differently on purpose, and only one is ever visible at a time.
//
// RENDERS THROUGH HoverDetailPanel for the card case rather than building a second card view -- a
// recapped card is the same card the player could hover, so it must look identical. That is the
// same rule C4 and the deckbuilder follow, and the reason this project has exactly one card
// renderer. The move strip is not a card and so is built here.
public partial class ActionRecapPanel : Control
{
    // How long the entry stays fully visible before it starts to fade. Long enough to read a card
    // name and glance at the board for what changed; short enough that it is gone before the next
    // AI action arrives (GameRoot.MoveDelaySeconds, 2.4s) -- otherwise every entry would be
    // replaced mid-life and the fade would never be seen at all.
    private const double HoldSeconds = 1.7;
    private const double FadeSeconds = 0.7;

    private HoverDetailPanel? _card;

    // The move strip and its parts.
    private Panel? _moveStrip;
    private TextureRect? _moveArt;
    private Label? _moveName;
    private Label? _moveCreature;

    private double _elapsed;
    private bool _active;
    private bool _needsFit;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        BuildCard();
        BuildMoveStrip();
    }

    private void BuildCard()
    {
        _card = GD.Load<PackedScene>("res://Scenes/HoverDetailPanel.tscn").Instantiate<HoverDetailPanel>();

        // Not a floating tooltip here but an embedded face, so it must not grow keyword explainers
        // above itself -- those are TopLevel and would float free over the board. Same distinction
        // CollectionCardView and CardBrowser make for their grid cells.
        _card.ShowsKeywordExplainers = false;
        AddChild(_card);

        // The scene is authored as the board's corner-parked tooltip (bottom-left anchors, a
        // hand-placed offset rect), so those anchors are cleared before it is placed here -- the
        // exact trap ButtonContentHost.Apply documents, and it has to happen after AddChild.
        _card.AnchorLeft = 0f;
        _card.AnchorTop = 0f;
        _card.AnchorRight = 0f;
        _card.AnchorBottom = 0f;
        _card.Position = Vector2.Zero;

        // A starting height only; FitCardToContent replaces it with the content's own ask on the
        // frame after every Show. Deliberately generous rather than tight -- an under-sized rect is
        // what makes the inner PanelContainer overhang (see FitCardToContent), so the pre-content
        // value errs large.
        _card.Size = new Vector2(PanelWidth, CardHeight);
        _card.Visible = false;
    }

    private void BuildMoveStrip()
    {
        _moveStrip = new Panel
        {
            Position = Vector2.Zero,
            Size = new Vector2(PanelWidth, MoveStripHeight),
            CustomMinimumSize = new Vector2(PanelWidth, MoveStripHeight),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };

        // Same stock/edge/radius as a card, so the strip reads as part of the same UI rather than
        // as a foreign widget -- the reasoning HoverDetailPanel gives for styling itself as a card.
        CardStyle.ApplyToPanel(_moveStrip);
        AddChild(_moveStrip);

        _moveArt = new TextureRect
        {
            Position = new Vector2(StripPadding, StripPadding),
            Size = new Vector2(MoveArtSize, MoveArtSize),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
        };
        _moveStrip.AddChild(_moveArt);

        var textLeft = StripPadding + MoveArtSize + StripPadding;
        var textWidth = PanelWidth - textLeft - StripPadding;

        // Size assigned as well as CustomMinimumSize: a Panel is not a Container and never lays its
        // children out, so a minimum alone leaves the rect at the text's natural width and ClipText
        // has nothing to clip against -- a long move name would simply overrun the strip.
        _moveName = new Label
        {
            Position = new Vector2(textLeft, StripPadding + 4f),
            Size = new Vector2(textWidth, 20f),
            CustomMinimumSize = new Vector2(textWidth, 0f),
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _moveName.AddThemeFontSizeOverride("font_size", 15);
        _moveStrip.AddChild(_moveName);

        _moveCreature = new Label
        {
            Position = new Vector2(textLeft, StripPadding + 26f),
            Size = new Vector2(textWidth, 16f),
            CustomMinimumSize = new Vector2(textWidth, 0f),
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _moveCreature.AddThemeFontSizeOverride("font_size", 12);
        _moveCreature.AddThemeColorOverride("font_color", SubtitleColor);
        _moveStrip.AddChild(_moveCreature);
    }

    // Replaces whatever is showing, restarting the hold. A newer action supersedes an older one
    // outright rather than queueing: a queue would fall progressively further behind the board it
    // is describing, which is the one thing this panel exists to prevent.
    public void ShowRecap(ActionRecap recap)
    {
        ArgumentNullException.ThrowIfNull(recap);

        if (recap.Kind == ActionRecapKind.Move)
        {
            ShowMove(recap);
        }
        else
        {
            ShowCard(recap);
        }

        _elapsed = 0d;
        _active = true;
        Modulate = Colors.White;
        Visible = true;
    }

    private void ShowCard(ActionRecap recap)
    {
        _moveStrip!.Visible = false;

        if (recap.Card is not { } card)
        {
            _card!.Visible = false;
            return;
        }

        _card!.Show(card);

        // Re-fit AFTER the content changes, every time -- a one-line spell and a four-move creature
        // must not paint the same box. Deferred one frame because the move rows were only just
        // added and their heights are not settled until Godot's layout pass has run (the same trap
        // PositionKeywords documents).
        //
        // Flag-and-poll rather than CallDeferred(nameof(...)): that route resolves the name through
        // Godot's own method table, which does not see an ordinary private C# method, so the call
        // was silently never made and the fit never ran.
        _needsFit = true;
    }

    private void ShowMove(ActionRecap recap)
    {
        _card!.Visible = false;

        _moveName!.Text = recap.Title;
        _moveCreature!.Text = recap.Subtitle;

        // The creature's own art, by card id. Null (no art authored) simply leaves the pane empty
        // rather than substituting a placeholder -- the strip is small, and an empty square reads
        // better at this size than a stand-in glyph.
        _moveArt!.Texture = recap.Card is { } card ? CardArt.TextureFor(card.CardId) : null;

        _moveStrip!.Visible = true;
    }

    // Shrinks the card to exactly the height its content wants, so the panel's painted edge always
    // sits just under the last line rather than at a fixed distance that only suits one card shape.
    // GetCombinedMinimumSize is what a parent container WOULD have asked it for -- there is no such
    // parent here (this positions the card by hand), so asking directly does the sizing that has
    // nothing else to do it, the same reasoning PositionKeywords uses for the keyword stack.
    private void FitCardToContent()
    {
        if (_card is null || !_card.Visible)
        {
            return;
        }

        // GIVE THE CARD EXACTLY ITS NATURAL HEIGHT, never less.
        //
        // HoverDetailPanel's inner PanelContainer is anchored full-rect to this control, and a
        // Container will not shrink below its children's combined minimum. Handed a SMALLER rect it
        // does not clip or compress -- it paints at its own preferred height, centred on the rect
        // it was given, overhanging equally above and below. Guardian (art band + 2 move rows +
        // stats) wants 327px; squeezed into 210 it painted 117.5..444.5, i.e. 58px above a control
        // whose own rect measured a perfectly innocent 176..386.
        //
        // That is why three rounds of adjusting offsets and clamps did nothing: every rect this
        // class could read was correct, and the overhang lived entirely in the child. The fix is to
        // stop under-sizing it, after which paint and measurement finally agree.
        // MEASURED ON THE INNER PanelContainer, not on the card root. The root is a plain Control:
        // it has no minimum of its own and does not aggregate its children's, so asking IT returns
        // 0 and every "fit" silently collapsed to MinCardHeight. The PanelContainer beneath it is
        // the node that actually holds the layout and knows what the content needs.
        var panel = _card.GetNodeOrNull<Control>(_card.PanelPath);
        var wanted = panel?.GetCombinedMinimumSize().Y ?? 0f;

        // Clamped at both ends. The upper clamp is safe now that the two agree -- an over-sized
        // rect simply leaves the container room it does not use, which is the harmless direction;
        // it was UNDER-sizing that made it overhang. The ceiling is needed because MoveList carries
        // size_flags_vertical = expand, so the reported minimum runs far past what the card draws.
        var height = Mathf.Clamp(wanted, MinCardHeight, MaxCardHeight);

        _card.Size = new Vector2(PanelWidth, height);
    }

    public void Clear()
    {
        _active = false;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        // One frame after the content changed, which is when the move rows have been laid out and
        // GetCombinedMinimumSize finally reports the truth.
        if (_needsFit)
        {
            _needsFit = false;
            FitCardToContent();
        }

        if (!_active)
        {
            return;
        }

        _elapsed += delta;

        if (_elapsed < HoldSeconds)
        {
            return;
        }

        var fade = (float)((_elapsed - HoldSeconds) / FadeSeconds);
        if (fade >= 1f)
        {
            Clear();
            return;
        }

        Modulate = new Color(1f, 1f, 1f, 1f - fade);
    }

    // Matches HoverDetailPanel.tscn's own authored width (offset_left 12 -> offset_right 252) so a
    // recapped card is laid out at exactly the width it was designed at.
    public const float PanelWidth = 240f;

    // The height before content is measured, and the bounds FitCardToContent clamps into.
    //
    // THE CEILING IS A LAYOUT BUDGET, not a taste call, and the whole left column had to be
    // re-apportioned to make the worst case fit. That column stacks four things: the type-cycle
    // chart, this recap, the hover tooltip, and the tooltip's keyword explainer stack (which grows
    // UPWARD from the tooltip). The worst case is a played-card recap above a hovered Guardian,
    // whose two moves grant reflect and stun -- two explainers, the tallest stack in the set.
    //
    // Settled at: chart 14..154 (shrunk from a 164px square to 140 for this), recap from y=176, and
    // the board's hover tooltip at 645 -- so the recap has 469px before it reaches the tooltip and
    // the tallest card in the set asks ~327. Deleting the old captioned header bought most of that
    // room; shrinking the chart bought the rest.
    private const float CardHeight = 300f;
    private const float MinCardHeight = 210f;

    // The column's real budget: this panel starts at y=176 and the board's hover tooltip begins at
    // 645, so 440 leaves the card ending at 616 with clearance. Measured in a windowed run, not
    // assumed.
    private const float MaxCardHeight = 440f;

    // The move strip: two text lines beside a square of art, and nothing else. Deliberately far
    // shorter than a card -- it exists so a move recap costs almost none of the left edge's height.
    private const float StripPadding = 8f;
    private const float MoveArtSize = 44f;
    private const float MoveStripHeight = MoveArtSize + (StripPadding * 2f);

    private static readonly Color SubtitleColor = new(0.72f, 0.78f, 0.86f);
}
