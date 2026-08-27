using System;
using System.Collections.Generic;
using Godot;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Plays A2's StateDiff as visible feedback (DESIGN.md B1d).
//
// ARCHITECTURE -- the choice B1d called "the step":
// This is an OVERLAY, not a reconciliation of the existing card nodes. PlayerPanel.RenderSlots
// and RenderHand QueueFree and re-instantiate every SlotView/CardFace on each render, so those
// nodes have no identity across frames and cannot themselves be tweened. The two options were
// (a) rewrite both render paths to reuse nodes and update them in place, or (b) leave rendering
// exactly as it is and draw transient effects in a layer above it. This is (b), because:
//
//   * (a) touches every event-rewiring path in PlayerPanel -- the code that produced A3's three
//     playtest-only bugs and A5's dropped-highlight bug, all of them lifetime/rewiring mistakes.
//     A rewrite there risks the same class of bug in the part of the codebase that has already
//     proven most able to hide it from the type checker.
//   * (b) needs no change to how cards render at all. The overlay reads the diff, spawns its own
//     short-lived nodes at slot positions, and frees them. Nothing it does can outlive an
//     action, so nothing it does can corrupt board state the way a stale reused node could.
//
// The cost of (b) is honest: this animates AT slots rather than animating the cards themselves,
// so a moving creature is a ghost travelling between two positions, not the card sliding. That
// is a real ceiling on polish, and if the ceiling ever binds, (a) is still available -- the cue
// list this consumes (AnimationScript) is unchanged by that decision, since it is expressed in
// slots and cues rather than in nodes.
//
// INPUT POLICY: animations never block input. B1d flagged this as a design call; the answer
// follows from A6's stance that every action is a committed decision -- the game state has
// ALREADY changed by the time anything here draws, so making the player wait would be showing
// them a lie about what is interactive. A player who acts fast simply sees the next action's
// cues start over the tail of the previous one, which is why every effect here is self-contained
// and self-freeing rather than a queue that must drain in order.
public partial class BoardAnimator : Control
{
    // Kept in step with the cue set rather than tuned per effect: the whole sequence for one
    // action has to stay under the time it takes a player to act again, or feedback for action N
    // is still on screen when N+1 lands. Fast enough to read, short enough not to accumulate.
    private const float FlashSeconds = 0.28f;
    private const float FloatSeconds = 0.55f;
    private const float FloatRisePixels = 34f;
    private const float GhostSeconds = 0.34f;

    private static readonly Color DamageColor = new("ff5a5a");
    private static readonly Color HealColor = new("5ad17a");
    private static readonly Color PlayColor = new("cfd8ff");
    private static readonly Color MergeColor = new("ffc94a");
    private static readonly Color DestroyColor = new("ff3b3b");
    // Score now reads as the opponent LOSING health, so it shares the damage red rather than
    // keeping its own yellow (DESIGN.md 5.C-UI). ScoreColor survives as the highlight on the
    // creature that earned it -- that one really is a gain, and gold reads as such.
    private static readonly Color ScoreColor = new("ffd479");
    private static readonly Color HealthLossColor = new("ff5a5a");

    // Supplied by BoardView each render: where each slot currently is on screen. Rects rather
    // than nodes on purpose -- a SlotView reference would dangle the moment the next render
    // QueueFrees it, which is the exact failure this overlay exists to avoid.
    private readonly Dictionary<SlotIndex, Rect2> _slotRects = new();
    private Rect2 _selfScoreRect;
    private Rect2 _opponentScoreRect;

    public override void _Ready()
    {
        // The overlay covers the whole board and must never intercept a click or a drag: every
        // gesture in B1a passes through this layer to the cards underneath.
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    // Called by BoardView after each render, since a re-render can move slots (a hand growing
    // shifts the panel split, and any window resize moves everything).
    public void UpdateLayout(
        IReadOnlyDictionary<SlotIndex, Rect2> slotRects, Rect2 selfScoreRect, Rect2 opponentScoreRect)
    {
        ArgumentNullException.ThrowIfNull(slotRects);

        _slotRects.Clear();
        foreach (var (slot, rect) in slotRects)
        {
            _slotRects[slot] = rect;
        }

        _selfScoreRect = selfScoreRect;
        _opponentScoreRect = opponentScoreRect;
    }

    public void Play(StateDiff diff, PlayerId selfSeat)
    {
        ArgumentNullException.ThrowIfNull(diff);

        foreach (var step in AnimationScript.From(diff))
        {
            PlayStep(step, selfSeat);
        }
    }

    private void PlayStep(AnimationStep step, PlayerId selfSeat)
    {
        if (step.Cue == AnimationCue.Score)
        {
            // Shown as the OPPONENT losing health rather than the scorer gaining points: health
            // is ScoreToWin minus the other side's score (see BoardView.Render), so a point
            // scored IS a point of health off the other player. The number therefore lands on
            // the player who lost it, in damage red with a minus, not on the scorer in yellow.
            var victim = step.Player == selfSeat ? _opponentScoreRect : _selfScoreRect;
            FloatText($"-{step.Amount}", victim, HealthLossColor);
            return;
        }

        if (step.Slot is not { } slot || !_slotRects.TryGetValue(slot, out var rect))
        {
            // A slot with no known rect (layout hasn't run yet, or the board was re-laid out
            // between submit and draw) is skipped rather than drawn at the origin -- a cue in
            // the wrong place is worse than a missing one.
            return;
        }

        switch (step.Cue)
        {
            case AnimationCue.Damage:
                Flash(rect, DamageColor);
                FloatText($"-{step.Amount}", rect, DamageColor);
                break;

            case AnimationCue.Heal:
                Flash(rect, HealColor);
                FloatText($"+{step.Amount}", rect, HealColor);
                break;

            case AnimationCue.Play:
                Ghost(rect, PlayColor, fadeIn: true);
                break;

            case AnimationCue.Move:
                Ghost(rect, PlayColor, fadeIn: true);
                break;

            case AnimationCue.Merge:
                Flash(rect, MergeColor);
                break;

            case AnimationCue.Destroy:
                Ghost(rect, DestroyColor, fadeIn: false);
                break;

            case AnimationCue.Scoring:
                ScorePulse(rect);
                break;
        }
    }

    private const float ScorePulseSeconds = 0.75f;

    // Marks a creature that just earned a point. Deliberately NOT the plain Flash used for
    // damage/merge: those are instant hits, while scoring is the payoff at the top of a turn and
    // wants to read as a swell rather than a blink. A gold border that brightens and fades, plus
    // a soft inner wash, held roughly twice as long as a flash so the eye can find every scoring
    // creature before the total lands on the avatar.
    private void ScorePulse(Rect2 rect)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(ScoreColor, 0.16f),
            BorderColor = ScoreColor,
        };
        style.SetBorderWidthAll(4);
        style.SetCornerRadiusAll(CardStyle.CornerRadius);

        var glow = new Panel
        {
            Size = rect.Size,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0f),
        };
        glow.AddThemeStyleboxOverride("panel", style);

        AddChild(glow);
        Place(glow, rect.Position);

        // Swell in, hold, fade -- three legs on one sequential tween so the hold is explicit
        // rather than being an artifact of easing.
        var tween = CreateTween();
        tween.TweenProperty(glow, "modulate:a", 1f, ScorePulseSeconds * 0.25f).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(ScorePulseSeconds * 0.35f);
        tween.TweenProperty(glow, "modulate:a", 0f, ScorePulseSeconds * 0.4f).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(glow.QueueFree));
    }

    // Applies any scale, then positions a spawned effect.
    //
    // ORDER IS LOAD-BEARING, and getting it wrong is what put every cue to the right of (and
    // below) its slot: a Control's Scale is applied around PivotOffset, not around its top-left
    // corner. Setting Position first and Scale second leaves Position describing the UNSCALED
    // top-left, but shrinking around a centered PivotOffset afterward drags that corner inward --
    // right and down -- by pivot * (1 - scale). For a 330x297 slot frame at scale 0.82 that is
    // (29.7, 26.7), exactly the displacement observed. Setting Scale first means Position is the
    // last write and lands the already-scaled node's top-left exactly where asked.
    //
    // Position (not GlobalPosition) is correct here because BoardAnimator is a full-rect overlay
    // anchored at the viewport origin, so its children's local space IS screen space -- confirmed
    // rather than assumed: the animator reports global=(0,0) with size equal to the viewport.
    private static void Place(Control node, Vector2 position, Vector2? scale = null)
    {
        if (scale is { } s)
        {
            node.Scale = s;
        }

        node.Position = position;
    }

    // A colored wash over the slot that fades out. ColorRect rather than modulating the
    // SlotView: the SlotView is about to be replaced by the next render, and a modulate applied
    // to a node that gets freed mid-tween leaves the replacement at the wrong color.
    private void Flash(Rect2 rect, Color color)
    {
        var wash = new ColorRect
        {
            Color = new Color(color, 0.42f),
            Size = rect.Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        AddChild(wash);
        Place(wash, rect.Position);

        var tween = CreateTween();
        tween.TweenProperty(wash, "modulate:a", 0f, FlashSeconds).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(wash.QueueFree));
    }

    // A number that rises and fades -- damage, healing, score.
    private void FloatText(string text, Rect2 rect, Color color)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1,
        };

        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 5);

        AddChild(label);

        // Centred on the rect rather than offset from its top-left corner. Place positions a
        // node's TOP-LEFT, so the old "+50% width" put the label's left edge at the midpoint --
        // visibly right-of-centre, and badly off on the narrow avatar cue rect that health-loss
        // numbers now use. Measured after AddChild so the label has a real size to halve.
        var textSize = label.GetCombinedMinimumSize();
        Place(label, rect.Position + (rect.Size - textSize) / 2f);

        // Parallel so the rise and the fade run together rather than one after the other. The
        // rise target is read from Position AFTER placement -- tweening "position:y" animates the
        // LOCAL property, so its start and end must both be in local space.
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(label, "position:y", label.Position.Y - FloatRisePixels, FloatSeconds)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0f, FloatSeconds).SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    // An outline that scales in (arrival) or out (departure). Stands in for the card itself
    // moving -- see the class header on why this overlay draws at slots rather than moving cards.
    private void Ghost(Rect2 rect, Color color, bool fadeIn)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(color, 0.10f),
            BorderColor = color,
            BorderWidthTop = 3,
            BorderWidthBottom = 3,
            BorderWidthLeft = 3,
            BorderWidthRight = 3,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        };

        var frame = new Panel
        {
            Size = rect.Size,
            MouseFilter = MouseFilterEnum.Ignore,

            // PivotOffset centers the scale, so the frame grows/shrinks from the middle of the
            // slot instead of from its top-left corner.
            PivotOffset = rect.Size * 0.5f,
        };

        frame.AddThemeStyleboxOverride("panel", style);
        frame.Modulate = new Color(1f, 1f, 1f, fadeIn ? 0f : 1f);

        AddChild(frame);

        // Scale first, position second -- see Place. This is the effect the ordering bug was
        // found on: it is the only spawned node that starts at a non-identity scale.
        Place(frame, rect.Position, fadeIn ? new Vector2(0.82f, 0.82f) : Vector2.One);

        var tween = CreateTween().SetParallel();
        tween.TweenProperty(frame, "scale", fadeIn ? Vector2.One : new Vector2(1.14f, 1.14f), GhostSeconds)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(frame, "modulate:a", fadeIn ? 1f : 0f, GhostSeconds)
            .SetEase(fadeIn ? Tween.EaseType.Out : Tween.EaseType.In);

        // An arrival fades IN, so it would otherwise be left sitting at full opacity over the
        // slot forever. Both directions end invisible: the frame is a cue, not a decoration.
        if (fadeIn)
        {
            tween.Chain()
                .TweenProperty(frame, "modulate:a", 0f, GhostSeconds)
                .SetEase(Tween.EaseType.In);
        }

        tween.Chain().TweenCallback(Callable.From(frame.QueueFree));
    }
}
