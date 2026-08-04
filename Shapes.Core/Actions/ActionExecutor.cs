using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Actions;

// Applies a legal action to a GameState.
//
// The contract with ActionGenerator: this ASSUMES the action is legal and does not re-validate
// it. Cost is paid via PlayerState.Pay, which throws rather than clamping, so an unaffordable
// action surfaces as a loud failure pointing at the generator rather than a silently free play.
// Two implementations of "legal" -- one generating, one re-checking -- is exactly how a UI and
// an AI come to disagree about the rules, so there is only one.
//
// Structured as apply-only for now, per "build the naive version first". Phase 2's apply/undo
// optimisation returns an undo record from these same methods; the tests pin behaviour rather
// than representation so that swap does not rewrite them.
public static class ActionExecutor
{
    public static void Apply(GameState state, CardDatabase cards, GameAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(action);

        switch (action)
        {
            case PlayCardAction play:
                ApplyPlayCard(state, cards, play);
                break;
            case UseMoveAction move:
                ApplyUseMove(state, cards, move);
                break;
            case MergeAction merge:
                ApplyMerge(state, merge);
                break;
            case EndTurnAction end:
                ApplyEndTurn(state, end);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action), action, $"Unknown action kind {action.Kind}.");
        }
    }

    private static void ApplyPlayCard(GameState state, CardDatabase cards, PlayCardAction action)
    {
        var player = state[action.Player];
        var card = cards.Get(action.CardId);

        player.Pay(card.Cost);

        // Removed from hand before any effect resolves, so an effect that counts hand size
        // (damage_scaled by hand_size, for instance) does not count the card being played. It
        // is in play, not in hand, at the moment its own effects run.
        player.RemoveFromHand(action.CardId);

        if (card.IsCreature)
        {
            var slot = action.TargetSlot
                ?? throw new InvalidOperationException(
                    $"Creature '{card.Id}' was played without a target slot.");

            state.Board.Place(slot, new CreatureInstance(card.Id, card.Health, card.Types));

            // No summoning sickness: the creature may act this turn. Nothing to do here -- its
            // move-usage bitmask starts clear -- but the absence is deliberate, so it is stated.
            ResolveEffects(state, card.Effects, action.Player, slot, action.ChosenTarget, null);
            return;
        }

        // A spell resolves and is gone. It goes to discard AFTER its effects run so a "count
        // your discard" effect cannot see the card that is still resolving.
        ResolveEffects(
            state, card.Effects, action.Player, sourceSlot: null, action.ChosenTarget, null);

        player.SendToDiscard(action.CardId);
    }

    private static void ApplyUseMove(GameState state, CardDatabase cards, UseMoveAction action)
    {
        var creature = state.Board[action.SourceSlot]
            ?? throw new InvalidOperationException($"No creature in {action.SourceSlot} to use a move.");

        var moves = cards.MovesOf(creature.MergedFrom);

        if (action.MoveIndex >= moves.Count)
        {
            throw new InvalidOperationException(
                $"Move index {action.MoveIndex} is out of range for the creature in "
                + $"{action.SourceSlot}, which has {moves.Count} moves.");
        }

        var move = moves[action.MoveIndex];

        state[action.Player].Pay(move.Cost);

        // Marked used BEFORE the effects resolve. An effect that kills this creature would
        // otherwise leave the flag unset, and while a dead creature cannot act again anyway,
        // the ordering also covers the case where an effect somehow returns to this creature
        // mid-list. Marking first means the once-per-turn rule holds regardless.
        creature.MarkMoveUsed(action.MoveIndex);

        ResolveEffects(
            state, move.Effects, action.Player, action.SourceSlot, action.ChosenTarget,
            move.AttackType);
    }

    private static void ApplyMerge(GameState state, MergeAction action)
    {
        var source = state.Board[action.SourceSlot]
            ?? throw new InvalidOperationException($"No creature in {action.SourceSlot} to merge.");

        var target = state.Board[action.TargetSlot]
            ?? throw new InvalidOperationException($"No creature in {action.TargetSlot} to merge into.");

        // Health and max health sum, typings union, move lists concatenate in merge order --
        // CreatureInstance.AbsorbMerge owns all of that. Removing the source is this layer's
        // job, since it owns the board.
        target.AbsorbMerge(source);
        state.Board.Remove(action.SourceSlot);

        // Merging is free and does not consume the turn: no cost paid, no phase change. The
        // ruleset's MergeCostsAction is honoured by the generator's continued offering of
        // actions afterwards, not by anything here.
    }

    private static void ApplyEndTurn(GameState state, EndTurnAction action)
    {
        var player = state[action.Player];

        // Draw, then discard to the hand limit, then pass. Draw first is what makes the hand
        // limit bite: drawing into an over-full hand must force a discard, not be skipped.
        player.Draw(state.Rules.CardsDrawnPerTurn);

        // Deck exhaustion is deliberately not fatal -- PlayerState.Draw returns null and the
        // player simply draws nothing. No damage, no loss; the rule is "you get nothing".
        DiscardToHandLimit(state, player);

        state.EndTurn();
    }

    // Discards from the front of the hand until at the limit.
    //
    // Which card to discard is a player CHOICE the action model does not yet express -- there
    // is no DiscardAction, because nothing has needed one. Taking from the front is a
    // deterministic placeholder rather than a rule: it is reproducible from a seed, which keeps
    // sim runs comparable, and it is deliberately not random, which would consume RNG draws and
    // shift every subsequent shuffle. When hand-limit discards start mattering to play strength
    // this becomes its own action; until then this is the honest minimum.
    private static void DiscardToHandLimit(GameState state, PlayerState player)
    {
        while (player.Hand.Count > state.Rules.HandLimit)
        {
            player.DiscardCardAt(0);
        }
    }

    // Runs an effect list, then sweeps the dead.
    //
    // The sweep happens ONCE, after the whole list, not between effects: an effect list may
    // deliberately reference a creature at 0 health before cleanup (a follow-up heal in the
    // same list, say). The interpreter documents that it does not sweep; this is the layer that
    // does, and doing it per action rather than per effect is what makes that promise true.
    private static void ResolveEffects(
        GameState state, IReadOnlyList<EffectNode> effects, PlayerId player,
        SlotIndex? sourceSlot, SlotIndex? chosenTarget, ResourceType? moveType)
    {
        if (effects.Count == 0)
        {
            return;
        }

        var ctx = new EffectContext(state, player, sourceSlot, chosenTarget, moveType);
        EffectInterpreter.ApplyAll(effects, ctx);

        state.Board.RemoveDead();
    }
}
