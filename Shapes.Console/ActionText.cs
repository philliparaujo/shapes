using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.State;

namespace Shapes.Console;

// Turns a GameAction into the line the console prints for it: the real card/move name plus its
// effect text, rather than the raw ids/indices GameAction.Describe() emits on its own.
//
// GameAction.Describe() is deliberately id/index-only -- it has no access to CardDatabase or
// GameState (see its own doc comment), so resolving a real name has to happen here, at a call
// site that has both. A public class (like BoardView) rather than a Program.cs local function,
// so the move-name/effect-text behaviour can be pinned by test the same way BoardView's
// hidden-hand behaviour is.
public static class ActionText
{
    public static string Describe(GameAction action, GameState state, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);

        if (action is PlayCardAction play && cards.TryGet(play.CardId, out var playedCard))
        {
            var named = action.Describe().Replace(play.CardId, playedCard!.Name, StringComparison.Ordinal);
            var text = $"{named} [{ResourceIcons.DescribeCost(playedCard!.Cost)}]";
            var effects = EffectText.Describe(playedCard!.Effects);
            return effects.Length == 0 ? text : $"{text} ({effects})";
        }

        if (action is DiscardAction discard && cards.TryGet(discard.CardId, out var discardedCard))
        {
            return action.Describe().Replace(discard.CardId, discardedCard!.Name, StringComparison.Ordinal);
        }

        if (action is UseMoveAction useMove)
        {
            var creature = state.Board[useMove.SourceSlot];
            if (creature is not null)
            {
                var moves = cards.MovesOf(creature.MergedFrom);
                if (useMove.MoveIndex < moves.Count)
                {
                    var move = moves[useMove.MoveIndex];
                    var at = useMove.ChosenTarget is { } chosen ? $" targeting {chosen}" : string.Empty;
                    var effects = EffectText.Describe(move.Effects);
                    var text =
                        $"{move.Name} [{ResourceIcons.DescribeCost(move.Cost)}] from {useMove.SourceSlot}{at}";
                    return effects.Length == 0 ? text : $"{text} ({effects})";
                }
            }
        }

        return action.Describe();
    }
}
