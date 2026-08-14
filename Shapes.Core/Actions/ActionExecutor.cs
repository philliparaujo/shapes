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
                ApplyMerge(state, cards, merge);
                break;
            case DiscardAction discard:
                ApplyDiscard(state, discard);
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

            // PlayCost is captured here, at the moment the card leaves hand and becomes a
            // creature, so destroy_refund_cost can read it later without a CardDatabase lookup.
            state.Board.Place(
                slot, new CreatureInstance(card.Id, card.Health, card.Types, playCost: card.Cost));
            state.RecordTurnEvent(TurnEventKind.CreaturePlayed, action.Player, slot, card.Id);

            // No summoning sickness: the creature may act this turn. Nothing to do here -- its
            // move-usage bitmask starts clear -- but the absence is deliberate, so it is stated.
            ResolveEffects(state, cards, card.Effects, action.Player, slot, action.ChosenTarget, null);
            return;
        }

        // A spell resolves and is gone. It goes to discard AFTER its effects run so a "count
        // your discard" effect cannot see the card that is still resolving. Its attacking type
        // comes from its own cost (CardDefinition.AttackType), exactly like a move's -- a spell
        // has no creature source, but it is not typeless merely for that reason.
        ResolveEffects(
            state, cards, card.Effects, action.Player, sourceSlot: null, action.ChosenTarget,
            card.AttackType);

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

        // Not move.Cost directly: a free-moves spell may have discounted this move's type for the
        // turn. GameState.CostOfMove is the same answer ActionGenerator used to decide this action
        // was affordable in the first place.
        state[action.Player].Pay(state.CostOfMove(action.Player, move.Cost));

        // Marked used BEFORE the effects resolve. An effect that kills this creature would
        // otherwise leave the flag unset, and while a dead creature cannot act again anyway,
        // the ordering also covers the case where an effect somehow returns to this creature
        // mid-list. Marking first means the once-per-turn rule holds regardless.
        creature.MarkMoveUsed(action.MoveIndex);

        ResolveEffects(
            state, cards, move.Effects, action.Player, action.SourceSlot, action.ChosenTarget,
            move.AttackType);
    }

    private static void ApplyMerge(GameState state, CardDatabase cards, MergeAction action)
    {
        var source = state.Board[action.SourceSlot]
            ?? throw new InvalidOperationException($"No creature in {action.SourceSlot} to merge.");

        var target = state.Board[action.TargetSlot]
            ?? throw new InvalidOperationException($"No creature in {action.TargetSlot} to merge into.");

        // Health and max health sum, typings union, move lists concatenate in merge order --
        // CreatureInstance.AbsorbMerge owns all of that. Removing the source is this layer's
        // job, since it owns the board. The move-count lookup is what lets AbsorbMerge shift the
        // source's spent-move bits into their new indices in the concatenated list.
        target.AbsorbMerge(source, cards.MoveCountOf);
        state.Board.Remove(action.SourceSlot);

        // Merging is free and does not consume the turn: no cost paid, no phase change. The
        // ruleset's MergeCostsAction is honoured by the generator's continued offering of
        // actions afterwards, not by anything here.
    }

    // Pays one point of a pending "discard N" debt with a chosen card.
    //
    // Both halves must happen together: removing the card without consuming the debt would loop
    // forever, and consuming without removing would let a player discharge a discard for free.
    private static void ApplyDiscard(GameState state, DiscardAction action)
    {
        var player = state[action.Player];

        if (!player.DiscardCard(action.CardId))
        {
            throw new InvalidOperationException(
                $"Cannot discard '{action.CardId}': it is not in player {action.Player}'s hand.");
        }

        state.ConsumePendingDiscard();

        // The debt may now exceed what is left in hand -- "discard 2" with two cards, one of
        // which was just discarded by something else. Clamping here as well as at the point the
        // debt is incurred covers a hand that shrank in between.
        ClampPendingDiscards(state, player);
    }

    private static void ApplyEndTurn(GameState state, EndTurnAction action)
    {
        // No draw here: drawing moved to turn START (GameState.ApplyDraw, run by
        // AdvanceToActions below for the player receiving the turn), so a drawn card is playable
        // the turn it arrives. The hand limit is enforced there too, by burning the overdrawn
        // card -- which is why this method no longer discards anything.
        state.EndTurn();

        // Runs the new active player's scoring and income immediately, so a caller never has
        // to check Phase and call ApplyScoring/ApplyIncome itself after EndTurn -- the one thing
        // step 1.9 exists to fold into a single turn-loop entry point. A no-op if scoring just
        // won the game.
        state.AdvanceToActions();
    }

    // Caps an outstanding discard debt at the number of cards actually held.
    //
    // "Discard 3" with two cards in hand discards both and forgets the third; the debt does not
    // follow the player into a future turn. Two reasons: a debt outliving its turn would let a
    // card silently tax a later one, and -- more urgently -- the generator offers NOTHING but
    // discards while a debt stands, so an unpayable one would produce an empty legal-action list
    // and deadlock the game. Clamping at the moment the debt is incurred is what keeps
    // ActionGenerator's non-emptiness invariant true without it having to mutate state.
    private static void ClampPendingDiscards(GameState state, PlayerState player)
    {
        if (state.PendingDiscards > player.Hand.Count)
        {
            state.ClearPendingDiscards();
            state.AddPendingDiscards(player.Hand.Count);
        }
    }

    // Runs an effect list, then sweeps the dead.
    //
    // The sweep happens ONCE, after the whole list, not between effects: an effect list may
    // deliberately reference a creature at 0 health before cleanup (a follow-up heal in the
    // same list, say). The interpreter documents that it does not sweep; this is the layer that
    // does, and doing it per action rather than per effect is what makes that promise true.
    private static void ResolveEffects(
        GameState state, CardDatabase cards, IReadOnlyList<EffectNode> effects, PlayerId player,
        SlotIndex? sourceSlot, SlotIndex? chosenTarget, ResourceType? moveType)
    {
        if (effects.Count == 0)
        {
            return;
        }

        var ctx = new EffectContext(
            state, player, sourceSlot, chosenTarget, moveType, HandComposition(state, cards, player));
        EffectInterpreter.ApplyAll(effects, ctx);

        foreach (var (slot, creature) in state.Board.RemoveDead())
        {
            state.DestroyCreature(slot, creature);
        }

        // A `discard` op in that list may have recorded a debt larger than the hand can pay.
        // Clamped here, once the whole list has resolved, rather than inside the op: a later
        // effect in the same list may draw cards, and clamping mid-list would forget a debt the
        // player turns out to be able to afford.
        ClampPendingDiscards(state, state[player]);
    }

    // How many cards in `player`'s hand cost each resource type -- see EffectContext.HandComposition.
    // Counts CARDS, not resource amounts: a single card costing 2 spike counts once toward
    // Spike, not twice. A mixed-cost card (a play cost may mix types freely; only a move's cost
    // must be single-type) counts toward every type it costs.
    internal static ResourcePool HandComposition(GameState state, CardDatabase cards, PlayerId player)
    {
        var spike = 0;
        var anvil = 0;
        var wheel = 0;

        foreach (var cardId in state[player].Hand)
        {
            var cost = cards.Get(cardId).Cost;
            if (cost.Spike > 0) spike++;
            if (cost.Anvil > 0) anvil++;
            if (cost.Wheel > 0) wheel++;
        }

        return new ResourcePool(spike, anvil, wheel);
    }
}
