using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// PLAN.md D2 items 2 and 4: what the recap panel should show for one action, or null when that
// action is not worth showing at all.
//
// THE PROBLEM THIS SOLVES. The client renders state, not events -- every action resolves instantly
// into a new board, so anything that is not a lasting state change is gone the moment its animation
// ends. A card play is at least partly self-evident (a card leaves the hand, a creature appears);
// a MOVE firing is the least legible action in the game, whose only trace is a health number
// changing somewhere on the board. Item 4 is therefore the one that earns item 2's panel.
//
// The decision of WHAT to show lives here, in the adapter, so it is testable without a scene; the
// panel in Shapes.Godot owns only how long it lingers and how it fades.
//
// SHOWN FOR BOTH SEATS, decided rather than assumed (PLAN.md D2 item 2). The uniform rule is
// simpler to reason about and to test, doubles as confirmation feedback for your own plays, and is
// the only variant that behaves correctly for an AI-vs-AI spectator -- where neither seat is
// "yours" and a self/opponent split would show nothing at all.
// Which of the two presentations the panel should use. Carried as data rather than inferred from
// whether Title reads like a move name -- the panel showing a whole card face or a compact strip is
// a real fork, and deriving it by string-sniffing would be the kind of implicit coupling that
// breaks the first time a card is named after a move.
public enum ActionRecapKind
{
    Card,
    Move,
}

public sealed record ActionRecap(
    string Title,
    string Subtitle,
    PlayerId Player,
    CardText? Card,
    ActionRecapKind Kind)
{
    // The recap for one action, or null if it should not raise one.
    //
    // `before` is the state the action applied TO: a move's own creature may not survive it (and a
    // played card has left the hand by the after-state), so the names this needs are only reliably
    // present beforehand -- the same reason ActionLog.Add takes the before-state.
    public static ActionRecap? For(GameAction action, GameState before, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(cards);

        return action switch
        {
            PlayCardAction play => ForPlay(play, cards),
            UseMoveAction useMove => ForMove(useMove, before, cards),
            _ => null,
        };
    }

    // A played card shows the card itself, so the recap is the same full card face the player would
    // get by hovering it -- reusing HoverDetailPanel's renderer rather than adding a second one,
    // the same rule C4/the deckbuilder follow.
    private static ActionRecap? ForPlay(PlayCardAction play, CardDatabase cards)
    {
        if (!cards.TryGet(play.CardId, out var card) || card is null)
        {
            return null;
        }

        // Title/Subtitle go unused by the card presentation -- the card face carries its own name --
        // but are still filled in so the record is meaningful to anything else reading it (tests,
        // and any future surface that wants a one-line form).
        return new ActionRecap(
            "Played", card.Name, play.Player, CardText.Of(card), ActionRecapKind.Card);
    }

    // A move shows the move NAME plus the creature that used it (PLAN.md D2 item 4) -- "which
    // creature just did that" is the question a move raises and the board cannot answer, since the
    // move button that fired is identical to the one that did not.
    //
    // The card shown is the creature's, not the move's: a move has no art or card face of its own,
    // and the creature is what the player is looking for on the board.
    private static ActionRecap? ForMove(UseMoveAction useMove, GameState before, CardDatabase cards)
    {
        var creature = before.Board[useMove.SourceSlot];
        if (creature is null)
        {
            return null;
        }

        var moves = cards.MovesOf(creature.MergedFrom);
        if (useMove.MoveIndex >= moves.Count)
        {
            return null;
        }

        var moveName = moves[useMove.MoveIndex].Name;
        var creatureName = cards.TryGet(creature.CardId, out var card) && card is not null
            ? card.Name
            : creature.CardId;

        // The creature's CardText travels so the panel can pull its art for the compact strip; the
        // strip never renders it as a card face. A merged creature reports its base card, the same
        // choice PlayerPanel makes for its hover cards.
        var text = card is null ? null : CardText.Of(card);

        return new ActionRecap(
            moveName, creatureName, useMove.Player, text, ActionRecapKind.Move);
    }
}
