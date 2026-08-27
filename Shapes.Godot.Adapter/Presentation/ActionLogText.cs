using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// How one action reads as a LINE IN THE MATCH LOG (DESIGN.md D2 item 5).
//
// A separate describer from ActionText, which stays as it is. That one exists to render an action
// as an identity -- "Play test_bolt [◯1] (Draw 1.)" -- for the console's action menu, where the id
// and the exact printed cost are the point and where the reader is choosing between candidates. It
// is also pinned by ActionTextTests in both its console and Godot copies, and those assertions are
// about that contract, not this one.
//
// A log line answers a different question: not "which of these may I pick" but "what just
// happened", read as prose, in the past tense, by someone scanning for the shape of a turn. That
// wants card NAMES over ids, slots named the way a player would point at them ("P2's middle slot",
// never "P2:1"), and no cost restated at all -- the effects underneath already say what was spent,
// and repeating it in the headline doubles the noise on the line that should scan fastest.
//
// Resource ICONS are deliberately absent here too. The overlay renders these strings into plain
// Labels; the sentinel form InlineResourceIcons consumes only means anything inside a
// RichTextLabel, so emitting glyphs (△▢◯) would put a second, worse icon vocabulary on screen
// beside the real ones. The effect lines name resources in words for the same reason.
public static class ActionLogText
{
    public static string Describe(GameAction action, GameState state, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);

        return action switch
        {
            PlayCardAction play => DescribePlay(play, cards),
            UseMoveAction useMove => DescribeMove(useMove, state, cards),
            MergeAction merge =>
                $"merges {ActionLogEffects.DescribeSlot(merge.SourceSlot)} "
                + $"into {ActionLogEffects.DescribeSlot(merge.TargetSlot)}",
            DiscardAction discard => $"discards {NameOf(discard.CardId, cards)}",
            EndTurnAction => "ends their turn",
            _ => action.Describe(),
        };
    }

    private static string DescribePlay(PlayCardAction play, CardDatabase cards)
    {
        var name = NameOf(play.CardId, cards);
        var where = play.TargetSlot is { } slot
            ? $" into {ActionLogEffects.DescribeSlot(slot)}"
            : string.Empty;
        var at = play.ChosenTarget is { } chosen
            ? $", targeting {ActionLogEffects.DescribeSlot(chosen)}"
            : string.Empty;

        return $"plays {name}{where}{at}";
    }

    // Names the creature as well as the move: a move name alone ("Overclock") does not say which
    // of up to six creatures on the board produced the damage underneath it -- the same gap item 4
    // fixes for the recap panel.
    private static string DescribeMove(UseMoveAction useMove, GameState state, CardDatabase cards)
    {
        var creature = state.Board[useMove.SourceSlot];
        if (creature is null)
        {
            return $"uses a move from {ActionLogEffects.DescribeSlot(useMove.SourceSlot)}";
        }

        var moves = cards.MovesOf(creature.MergedFrom);
        var moveName = useMove.MoveIndex < moves.Count ? moves[useMove.MoveIndex].Name : "a move";
        var at = useMove.ChosenTarget is { } chosen
            ? $" on {ActionLogEffects.DescribeSlot(chosen)}"
            : string.Empty;

        return $"uses {moveName} ({NameOf(creature.CardId, cards)}){at}";
    }

    private static string NameOf(string cardId, CardDatabase cards) =>
        cards.TryGet(cardId, out var card) && card is not null ? card.Name : cardId;
}
