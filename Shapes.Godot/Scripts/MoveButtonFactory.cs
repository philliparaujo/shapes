using System;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Builds one move's button for SlotView (board). Content comes from MoveRowFactory -- the same
// numbered-pip + name-over-description block the tooltip uses (PLAN.md B1c) -- so the two views
// can't drift apart the way they did when each hand-rolled its own row.
//
// Every move renders here, usable or not (PLAN.md B1a): an unusable move (condition unmet, used
// this turn, unaffordable) still tells the player what the creature can do, just not right now, so
// it renders disabled and dimmed rather than being omitted -- omitting it would make "no moves"
// and "one move I can't currently use" look identical.
//
// Sized from CardMetrics, not a local constant: the board card is 10:8 and has to hold a header,
// an art band, and up to 4 moves (RuleSet.MaxMergeDepth 2 x 2 moves per card) inside it, so the
// per-move height is whatever that budget leaves rather than a number picked here and reconciled
// later. Earlier rounds picked 42px here and grew the whole card to fit it, which is how the slot
// drifted to 10:12.
public static class MoveButtonFactory
{
    public static float Width => CardMetrics.SlotMoveWidth;

    public static float Height => CardMetrics.SlotMoveHeight;

    // PLAN.md D2 item 3. `wasUsedThisTurn` splits one of the four reasons a move renders disabled
    // out of the single grey the other three share.
    //
    // Before this, "used already", "condition unmet", "unaffordable" and "not your turn" all
    // produced the identical dimmed button -- so the board could not answer the one question a
    // player asks most while planning a turn: what have I already spent? That is worst on a merged
    // creature, which fields up to four moves at once.
    //
    // The engine already knows (CreatureInstance.HasUsedMove), so this needs no new state and no
    // change to Shapes.Core -- which the milestone requires stay untouched.
    public static Button Create(MoveText text, bool isUsable, Action onChosen, bool wasUsedThisTurn = false)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(Width, Height),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Disabled = !isUsable,
            ClipContents = true,
        };

        // A Button never lays out its children (it is not a Container), so the content is hosted
        // in a control that tracks the button's rect itself -- ButtonContentHost anchors to its
        // parent's full rect, so nothing here has to size it.
        //
        // This used to also assign host.Position/Size from a Resized handler, which was how the
        // host got a size back when it had none of its own. That assignment is now not merely
        // redundant but WRONG: against non-zero anchors, Godot stores an assigned Size as offsets
        // measured from the anchored edges, so writing the button's width there made the host
        // span roughly twice the button. Nothing looked broken on the board except that every
        // move description wrapped to the inflated width -- i.e. stopped wrapping inside the
        // card and ran off its right edge.
        // The scrim goes in BEFORE the content host, so it draws under the row rather than over it
        // -- the marking's other half is the text's own amber, and veiling that with its own wash
        // would undo the recolouring it depends on.
        if (wasUsedThisTurn)
        {
            button.AddChild(new SpentMoveOverlay());
        }

        var host = new ButtonContentHost();
        button.AddChild(host);
        host.SetContent(MoveRowFactory.CreateContent(text, wasUsedThisTurn));

        // SPENT TAKES PRECEDENCE OVER UNAFFORDABLE, deliberately. A move can be both at once, and
        // the two answers are not equally useful: "you already used this" is final until your next
        // turn, while "you cannot afford this" may stop being true a moment later. So the spent
        // marking is applied INSTEAD of the disabled fade rather than on top of it -- fading first
        // would wash the amber toward the same grey every other unusable move already wears, which
        // is exactly the distinction being drawn.
        if (!wasUsedThisTurn && !isUsable)
        {
            button.Modulate = new Color(1f, 1f, 1f, 0.55f);
        }

        button.Pressed += () => onChosen();
        return button;
    }
}
