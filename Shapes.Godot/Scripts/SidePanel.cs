using Godot;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Scripts;

// One seat's block on the right rail (DESIGN.md 5.C-UI, from references/game screen.png): a
// rectangle holding the hand/deck counts and the three resource chips, with a PlayerBadge
// floating over its left edge.
//
// Built in code rather than as a .tscn because the whole point of the layout is that one child
// (the badge) is NOT laid out by the container -- it is anchored so it overhangs. Expressing that
// in a scene means a Control whose position fights its parent's sort every time the rail
// resizes; here the overhang is one anchor set once, next to the code that depends on it.
//
// Rendered twice by BoardView, once per seat, the same "pass the PlayerId in, don't bake it into
// the scene" split PlayerPanel already uses.
public partial class SidePanel : Control
{
    public const float PanelWidth = 208f;

    private const int PanelPadding = 10;

    // The badge sits ABOVE the opponent's panel and BELOW the player's, floating clear of the
    // rectangle rather than overhanging its left edge (it used to, but the circles were small and
    // cramped against the counts). It is still drawn outside this control's rect, so every
    // ancestor up to the window must leave clipping off -- nothing on the rail sets
    // clip_contents, and this note is here so it stays that way.
    // Distance between the panel's edge and the nearest DRAWN circle. The badge's rect is bigger
    // than the circles inside it, and by a different amount at the top than the bottom (the
    // satellites hang below the avatar), so PlaceBadge subtracts the relevant inset rather than
    // aligning the rect itself -- see PlayerBadge.TopInset/BottomInset.
    private const float BadgeGap = 10f;

    private static readonly Color PanelFill = new("2b3138");
    private static readonly Color PanelEdge = new("11151a");
    private static readonly Color CountBoxFill = new("3a424c");
    private static readonly Color LabelColor = new("d6dae0");

    private PlayerBadge? _badge;
    private Label? _handCount;
    private Label? _deckCount;
    private HBoxContainer? _resourceRow;
    private Label? _nameLabel;

    // Which side the badge hangs off is fixed, but the vertical order of the two rows mirrors
    // between seats in the reference: the opponent's counts sit above its resources, the
    // player's below, so both push toward the End Turn button between them.
    private bool _resourcesFirst;

    public void Build(bool resourcesFirst)
    {
        _resourcesFirst = resourcesFirst;

        // Width only -- the HEIGHT must keep whatever the scene set. Assigning a fresh Vector2
        // with a zero Y here clobbered the scene's 104px, which collapsed both panels to nothing
        // and stacked all three rail sections on top of each other.
        CustomMinimumSize = new Vector2(PanelWidth, CustomMinimumSize.Y);

        // Fills this control's rect exactly. Both parts matter: without the offsets the container
        // sizes to its own content, and without this control adopting the container's minimum
        // (see _GetMinimumSize) the container overflows a too-small slot instead.
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", CardStyle.Box(PanelFill, PanelEdge));
        AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", PanelPadding);
        margin.AddThemeConstantOverride("margin_top", PanelPadding);
        margin.AddThemeConstantOverride("margin_right", PanelPadding);
        margin.AddThemeConstantOverride("margin_bottom", PanelPadding);
        panel.AddChild(margin);

        // Two rows: one of resource chips, one of card counts. Which comes first mirrors per seat
        // so both panels push their card row toward the outside of the rail.
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        _resourceRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        _resourceRow.AddThemeConstantOverride("separation", 8);

        var counts = BuildCountsRow();

        // "Player N - Deck Name" (SetIdentity), positioned nearest the End Turn button -- one
        // slot further in than the resources row, which was already the row nearest the button
        // for both seats (see the comment above on why resources/counts mirror). Between the
        // panels and the button is exactly what was asked for, and it is the row every seat swap
        // relabels: BoardView.Render assigns this panel to a different PlayerId each turn, so the
        // text changes with it rather than being fixed to a rail position.
        _nameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 12);
        _nameLabel.AddThemeColorOverride("font_color", LabelColor);

        if (_resourcesFirst)
        {
            column.AddChild(_nameLabel);
            column.AddChild(_resourceRow);
            column.AddChild(counts);
        }
        else
        {
            column.AddChild(counts);
            column.AddChild(_resourceRow);
            column.AddChild(_nameLabel);
        }

        // The badge is added AFTER the panel so it draws on top, and anchored rather than added
        // to the column so the container never positions it -- that is what lets it overhang.
        // ZIndex, not just child order: the badge overhangs the panel's left edge and must draw
        // ON TOP of it. Sibling order alone was not enough -- the PanelContainer's own background
        // painted over the overhanging half, which read as the circle being clipped.
        _badge = new PlayerBadge { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 2 };
        AddChild(_badge);

        _panel = panel;

        // _GetMinimumSize reads _panel, which only exists now -- tell Godot the answer changed,
        // or the slot keeps the pre-Build value it already cached.
        UpdateMinimumSize();

        Resized += PlaceBadge;
        PlaceBadge();
    }

    private Control? _panel;

    // Where a health-loss number should appear for this seat: the empty space on the far side of
    // the avatar from the panel -- above the opponent's badge, below the player's. Returned in
    // global coordinates for BoardAnimator, whose overlay draws in screen space.
    //
    // Sits outside the avatar rather than over it so the number never covers the health pip it
    // is describing, which is the one thing the player is checking when it fires.
    public Rect2 HealthCueRect
    {
        get
        {
            if (_badge is null)
            {
                return new Rect2(GlobalPosition, Size);
            }

            var badge = new Rect2(_badge.GlobalPosition, _badge.Size);
            var height = PlayerBadge.SatelliteDiameter;

            // _resourcesFirst marks the player's seat, whose badge hangs BELOW its panel -- so
            // its free space is below the badge; the opponent's is above.
            var y = _resourcesFirst ? badge.End.Y - height : badge.Position.Y;
            return new Rect2(new Vector2(badge.Position.X, y), new Vector2(badge.Size.X, height));
        }
    }

    // This control is a plain Control, so Godot does NOT propagate its children's minimums to it
    // -- it reported only the scene's 104px while the PanelContainer inside needed 122, and the
    // container simply overflowed by 18px. The overflow ran downward from y=0 in both seats, so
    // it ate into the button gap above the player's panel and away from it below the opponent's:
    // one 18px overflow, mirrored, producing the 29px/48px asymmetry.
    //
    // Adopting the panel's minimum makes the slot match what is actually painted, which is what
    // keeps the two gaps equal. The badge is deliberately NOT consulted -- it is positioned by
    // hand outside this rect and must never inflate the slot.
    public override Vector2 _GetMinimumSize()
    {
        var content = _panel?.GetCombinedMinimumSize().Y ?? 0f;
        return new Vector2(PanelWidth, Mathf.Max(CustomMinimumSize.Y, content));
    }

    // Hand and deck side by side on one row. The hand pip is an upright card; the deck pip is the
    // SAME art rotated 90 degrees, so the two are distinguishable at a glance without a caption
    // -- a deck reads as a stack seen edge-on, a hand as a card held face-on.
    private Control BuildCountsRow()
    {
        var row = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        row.AddThemeConstantOverride("separation", 14);

        _handCount = BuildCountPip(row, "Cards in hand", rotated: false);
        _deckCount = BuildCountPip(row, "Cards left in deck", rotated: true);
        return row;
    }

    // Card-shaped and card-proportioned (the source art is 214x288, close to 3:4).
    private const float PipWidth = 42f;
    private const float PipHeight = 56f;

    private static Label BuildCountPip(Node parent, string tooltip, bool rotated)
    {
        // Rotating a Control does not change the space its parent reserves for it, so the
        // horizontal pip declares the SWAPPED extents and the art inside it is turned to match.
        var pip = new Control
        {
            CustomMinimumSize = rotated
                ? new Vector2(PipHeight, PipWidth)
                : new Vector2(PipWidth, PipHeight),
            TooltipText = tooltip,
        };
        parent.AddChild(pip);

        if (CardBackTexture is { } texture)
        {
            var art = new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
                Size = new Vector2(PipWidth, PipHeight),

                // Darkened so the white count reads against the art, which is a light silver-blue
                // in the middle -- exactly where the number sits. Scaling RGB with alpha left at 1
                // keeps the card opaque rather than letting the panel show through.
                Modulate = CardBackTint,
            };

            if (rotated)
            {
                // Rotate about the card's own centre, then shift that centre to the middle of the
                // (swapped) pip rect -- rotating about the top-left corner would swing the art out
                // of the pip entirely.
                art.PivotOffset = new Vector2(PipWidth / 2f, PipHeight / 2f);
                art.RotationDegrees = 90f;
                art.Position = new Vector2((PipHeight - PipWidth) / 2f, (PipWidth - PipHeight) / 2f);
            }

            pip.AddChild(art);

            // A soft radial shade under the number: the card back's centre is its brightest
            // region, so a flat tint over the whole card either leaves the middle too light or
            // crushes the edges. A ColorRect (tried first) reads as an obvious dark rectangle
            // pasted on the art -- this is drawn as concentric fading rings instead, so it has no
            // defined edge, and it is clipped to the pip so it cannot spill past the card.
            pip.AddChild(new CardBackVignette { MouseFilter = MouseFilterEnum.Ignore });
        }
        else
        {
            // Same flat box the counts used before, for when the art is missing.
            var fallback = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1f, AnchorBottom = 1f };
            fallback.AddThemeStyleboxOverride("panel", CardStyle.Box(CountBoxFill, PanelEdge));
            pip.AddChild(fallback);
        }

        var label = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeFontSizeOverride("font_size", 17);

        // Outlined so the number stays readable over the busy card-back art -- same treatment
        // ResourceIconFactory gives its own pip numbers.
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("outline_size", 5);
        pip.AddChild(label);
        return label;
    }

    private static readonly Color CardBackTint = new(0.62f, 0.66f, 0.72f, 1f);

    // Loaded once. Null when the file is missing, which BuildCountPip falls back from rather than
    // erroring -- the same "missing art degrades to a placeholder" rule CardArt follows.
    private static readonly Texture2D? CardBackTexture =
        ResourceLoader.Exists(CardBackPath) ? GD.Load<Texture2D>(CardBackPath) : null;

    private const string CardBackPath = "res://Art/ui/cardback.png";

    // Re-placed on resize rather than in a sort notification: this is a Control, not a Container,
    // so nothing sorts its children -- which is exactly why the badge can hang outside the panel.
    private void PlaceBadge()
    {
        if (_badge is null)
        {
            return;
        }

        // Floats clear of the rectangle: ABOVE it for the opponent (whose panel is at the top of
        // the rail) and BELOW it for the player, so in both cases the circles sit on the outer
        // side, away from the End Turn button between the two panels.
        //
        // _resourcesFirst doubles as "this is the player's seat" -- the same flag that decides
        // row order also decides which way the badge hangs, since both follow from which end of
        // the rail this panel occupies.
        var size = _badge.GetCombinedMinimumSize();
        _badge.Size = size;

        // Player's badge hangs below the panel, so its TOP circle edge is what must clear the
        // panel; the opponent's hangs above, so its BOTTOM edge does. Subtracting the matching
        // inset makes BadgeGap the real gap in both cases -- which is what makes the two panels
        // look symmetric instead of one circle sitting closer than the other.
        var y = _resourcesFirst
            ? Size.Y + BadgeGap - PlayerBadge.TopInset
            : -size.Y - BadgeGap + PlayerBadge.BottomInset;

        _badge.Position = new Vector2((Size.X - size.X) / 2f, y);
    }

    // This seat's avatar art. Set once per match by BoardView rather than passed through Render,
    // which runs on every hover and targeting refresh -- see PlayerBadge.Portrait.
    public void SetAvatar(Texture2D? portrait) => _badge!.Portrait = portrait;

    // "Player N - Deck Name". Called every Render, same as SetAvatar just below it in BoardView --
    // which PLAYER this text names is fixed for the whole match, but which SEAT (opponent/self
    // panel) shows it swaps every turn, so the assignment has to be re-applied each time even
    // though the string itself rarely changes.
    public void SetIdentity(string text) => _nameLabel!.Text = text;

    // health is passed in rather than derived here: it is scoreToWin minus the OPPONENT's score,
    // which is a rule this view has no business knowing (see BoardView.Render).
    public void Render(PlayerState player, int health)
    {
        _handCount!.Text = player.Hand.Count.ToString();
        _deckCount!.Text = player.Deck.Count.ToString();
        _badge!.SetValues(health);

        foreach (var child in _resourceRow!.GetChildren())
        {
            _resourceRow.RemoveChild(child);
            child.QueueFree();
        }

        // Same chips as the board and the cards' cost badges, so a resource looks identical
        // everywhere it appears.
        var resources = player.Resources;
        AddResource(ResourceType.Wheel, resources.Wheel, _lastResources?.Wheel);
        AddResource(ResourceType.Anvil, resources.Anvil, _lastResources?.Anvil);
        AddResource(ResourceType.Spike, resources.Spike, _lastResources?.Spike);
        _lastResources = resources;
    }

    // Last totals this panel actually drew, compared per-type on the next Render so a gain reads
    // as a pulse rather than the number silently switching. Render happens far more often than a
    // resource changes (targeting refreshes, hovers), so animating unconditionally would be
    // near-constant motion; comparing against the last DRAWN total limits it to real changes.
    // Null until the first Render, so game start never reads as "everything just increased."
    private ResourcePool? _lastResources;

    private void AddResource(ResourceType type, int count, int? previousCount)
    {
        var icon = ResourceIconFactory.Create(type, ResourceIconFactory.IconSize.Medium, count);
        _resourceRow!.AddChild(icon);

        if (previousCount is { } prev && count != prev)
        {
            // Deferred one frame: the icon just joined the tree, and GlobalPosition/Size are not
            // settled until Godot's layout pass runs -- reading them synchronously yields a
            // pre-layout (0,0) rect.
            CallDeferred(nameof(PulseResourceIcon), icon, count - prev);
        }
    }

    private const float ResourcePulseSeconds = 0.32f;
    private const float ResourceFloatSeconds = 0.6f;
    private const float ResourceFloatRisePixels = 22f;
    private static readonly Color ResourceGainColor = new("8affa0");
    private static readonly Color ResourceSpendColor = new("ff8a6a");

    // Scale-bounce on the icon plus a floating number -- the same recipe BoardAnimator.FloatText
    // uses for damage/heal/score. Moved here with the resource row when the top status bar was
    // removed (DESIGN.md 5.C-UI); it stays local rather than routing through BoardAnimator because
    // a rail total has no StateDiff cue of its own.
    //
    // delta's SIGN drives everything. Spending is not just a gain played backwards: the icon
    // shrinks instead of swelling and the number sinks instead of rising, so paying a cost reads
    // as the opposite gesture to earning income rather than as a differently-coloured version of
    // the same one.
    private void PulseResourceIcon(Control icon, int delta)
    {
        if (!IsInstanceValid(icon) || delta == 0)
        {
            return;
        }

        var gained = delta > 0;

        icon.PivotOffset = icon.Size / 2f;
        var peak = gained ? new Vector2(1.35f, 1.35f) : new Vector2(0.74f, 0.74f);
        var pulse = CreateTween();
        pulse.TweenProperty(icon, "scale", peak, ResourcePulseSeconds * 0.4f)
            .SetEase(Tween.EaseType.Out);
        pulse.TweenProperty(icon, "scale", Vector2.One, ResourcePulseSeconds * 0.6f)
            .SetEase(Tween.EaseType.In);

        var label = new Label
        {
            Text = gained ? $"+{delta}" : delta.ToString(),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1,
        };
        label.AddThemeColorOverride("font_color", gained ? ResourceGainColor : ResourceSpendColor);
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 4);

        AddChild(label);

        // A gain starts above the icon and rises clear of it; a spend starts below and sinks, so
        // the two never occupy the same space and can be told apart at a glance.
        var iconCentre = icon.GlobalPosition - GlobalPosition + icon.Size / 2f;
        var startOffset = icon.Size.Y * (gained ? -0.6f : 0.6f);
        label.Position = iconCentre - label.Size / 2f + new Vector2(0f, startOffset);

        var travel = gained ? -ResourceFloatRisePixels : ResourceFloatRisePixels;
        var floatTween = CreateTween().SetParallel();
        floatTween.TweenProperty(
                label, "position:y", label.Position.Y + travel, ResourceFloatSeconds)
            .SetEase(Tween.EaseType.Out);
        floatTween.TweenProperty(label, "modulate:a", 0f, ResourceFloatSeconds).SetEase(Tween.EaseType.In);
        floatTween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}

// The soft shade behind a count pip's number (DESIGN.md 5.C-UI). Darkest at the centre, fading to
// nothing before the card's edge, so the white count reads against the card back's bright middle
// without a visible rectangle sitting on the art.
//
// Drawn as a stack of concentric ellipses at decreasing alpha rather than as one shape: Godot's
// immediate-mode drawing has no radial-gradient primitive, and a single DrawRect/ColorRect (what
// this replaces) reads as an obvious dark box. The same "layered rings fake a blur" trick
// ResourceIconFactory already uses for its square drop shadow.
public partial class CardBackVignette : Control
{
    // How dark the very centre gets, and how far out the shade reaches as a fraction of the
    // pip's half-extent. Kept under 1 so the falloff completes INSIDE the card -- at 1.0 the
    // outermost ring would touch the edge and reintroduce a visible boundary.
    private const float CentreAlpha = 0.42f;
    private const float Reach = 0.92f;

    private const int Rings = 14;

    public override void _Ready()
    {
        // Anchors rather than a size: the pip owns the rect, and the two pip orientations
        // (upright hand, rotated deck) have different extents.
        AnchorRight = 1f;
        AnchorBottom = 1f;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var centre = Size / 2f;

        // Outermost (faintest) first so each smaller ring layers over it, accumulating toward the
        // centre. Alpha per ring is low; the buildup is what makes the gradient smooth.
        for (var i = Rings; i >= 1; i--)
        {
            var t = i / (float)Rings;
            var radius = centre * Reach * t;

            // Squared falloff: alpha drops off faster than radius, which reads as a soft pool of
            // shadow rather than a linear cone.
            var alpha = CentreAlpha / Rings * (1f - t) * 2f;
            DrawEllipse(centre, radius, new Color(0f, 0f, 0f, alpha));
        }
    }

    // Godot has DrawCircle but no ellipse, and a pip is not square -- a circle would leave the
    // long axis unshaded. Drawn as a polygon fan scaled per axis.
    private void DrawEllipse(Vector2 centre, Vector2 radius, Color color)
    {
        const int segments = 28;
        var points = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var angle = i / (float)segments * Mathf.Tau;
            points[i] = centre + new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y);
        }

        DrawColoredPolygon(points, color);
    }
}
