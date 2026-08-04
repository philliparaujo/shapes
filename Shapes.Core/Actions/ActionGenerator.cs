using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Actions;

// Enumerates every legal action for the active player.
//
// The single most important API in the codebase: the console renders this as a numbered menu,
// MCTS expands it into tree children, and the Phase 4 UI derives every affordance from it.
// Because all three consume the same list, a rule enforced here is enforced everywhere, and a
// rule enforced ONLY in a consumer is a rule the AI will happily break.
//
// Two invariants this guarantees, both pinned by tests:
//
//   1. SOUNDNESS -- every action returned can be applied without throwing. ActionExecutor is
//      allowed to assume legality and does not re-check it, so an unaffordable or
//      wrongly-targeted action reaching it is a bug here, not there. This is why affordability
//      is checked rather than clamped, matching ResourcePool.Subtract's choice to throw.
//   2. NON-EMPTINESS -- EndTurn is always available during the Actions phase, so a player is
//      never stuck. The fuzz harness's termination property depends on it.
//
// Actions are atomic: one card, one move, or one merge -- never a whole turn. A turn is a
// sequence of these. Enumerating turn-sequences instead would multiply out to a combinatorial
// explosion, which is the single biggest search-cost mistake available here.
public static class ActionGenerator
{
    public static IReadOnlyList<GameAction> Generate(GameState state, CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);

        var actions = new List<GameAction>();

        // A finished game has no legal actions at all -- not even EndTurn. The win check has to
        // come before everything else, or a player could keep acting past the win threshold and
        // a sim run would record actions taken after the result was decided.
        if (state.IsOver || state.Phase != TurnPhase.Actions)
        {
            return actions;
        }

        var player = state.ActivePlayer;

        AddPlayCardActions(state, cards, player, actions);
        AddUseMoveActions(state, cards, player, actions);
        AddMergeActions(state, player, actions);

        actions.Add(new EndTurnAction(player));

        return actions;
    }

    // Whether this exact action is currently legal. Implemented as a membership test against
    // Generate rather than as a parallel set of checks: a second implementation of "legal" is
    // the classic way for a UI to permit something the AI thinks is illegal (or vice versa).
    // Callers on the hot path should use Generate directly rather than calling this per action.
    public static bool IsLegal(GameState state, CardDatabase cards, GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Generate(state, cards).Contains(action);
    }

    // Playing a card: pay its cost, and for a creature choose an empty slot.
    //
    // Duplicate hand entries are collapsed -- two copies of the same card in hand produce one
    // action per slot, not two identical ones per slot. They are genuinely the same choice
    // (cards are static data with no per-copy identity), and leaving both in would give the
    // search two identical edges, splitting its statistics across them and biasing selection
    // toward whichever it happened to visit first.
    private static void AddPlayCardActions(
        GameState state, CardDatabase cards, PlayerId player, List<GameAction> actions)
    {
        var hand = state[player].Hand;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cardId in hand)
        {
            if (!seen.Add(cardId))
            {
                continue;
            }

            var card = cards.Get(cardId);

            if (!state[player].CanAfford(card.Cost))
            {
                continue;
            }

            if (card.IsCreature)
            {
                AddCreaturePlays(state, cards, player, card, actions);
            }
            else
            {
                AddSpellPlays(state, player, card, actions);
            }
        }
    }

    private static void AddCreaturePlays(
        GameState state, CardDatabase cards, PlayerId player, CardDefinition card,
        List<GameAction> actions)
    {
        // A creature's chosen target is resolved from the slot it will occupy, since a creature
        // card's own effects (should any exist) would act from there. Creatures declare no
        // top-level effects today -- CardValidator rejects them, as there are no passive or
        // triggered effects -- so this expands to exactly one action per empty slot. The
        // structure is here so a future card kind that does target on entry needs no new
        // branching logic.
        var chosen = card.ChosenSelector;

        foreach (var slot in state.Board.EmptySlotsOf(player))
        {
            if (chosen is null)
            {
                actions.Add(new PlayCardAction(player, card.Id, slot));
                continue;
            }

            var ctx = new EffectContext(state, player, slot, chosenTarget: null);
            foreach (var target in TargetResolver.ChosenCandidates(ctx, chosen.Value))
            {
                actions.Add(new PlayCardAction(player, card.Id, slot, target));
            }
        }
    }

    private static void AddSpellPlays(
        GameState state, PlayerId player, CardDefinition card, List<GameAction> actions)
    {
        var chosen = card.ChosenSelector;

        if (chosen is null)
        {
            actions.Add(new PlayCardAction(player, card.Id));
            return;
        }

        // A spell has no source slot: its damage is typeless, and taunt does not restrict it
        // (there is no creature to be taunted away from). Passing sourceSlot: null is what
        // communicates that to TargetResolver.
        var ctx = new EffectContext(state, player, sourceSlot: null, chosenTarget: null);
        var candidates = TargetResolver.ChosenCandidates(ctx, chosen.Value);

        // A targeted spell with no legal target is not playable. Generating it anyway would let
        // a player burn a card and its cost for nothing -- and worse, would give the search an
        // edge whose only effect is losing tempo, which it would have to learn to avoid rather
        // than simply never seeing.
        foreach (var target in candidates)
        {
            actions.Add(new PlayCardAction(player, card.Id, targetSlot: null, chosenTarget: target));
        }
    }

    // Using a move: the creature must be un-stunned, the move unused this turn and affordable,
    // and its condition (if any) met.
    private static void AddUseMoveActions(
        GameState state, CardDatabase cards, PlayerId player, List<GameAction> actions)
    {
        foreach (var (slot, creature) in state.Board.CreaturesOf(player))
        {
            // Stun gates the whole creature, not individual moves. Checked once here rather
            // than per move so the intent stays visible.
            if (creature.IsStunned)
            {
                continue;
            }

            // The concatenated move list -- source cards in merge order, each card's moves in
            // declaration order. The index into THIS list is what UseMoveAction carries and
            // what the once-per-turn bitmask uses, so both agree by construction.
            var moves = cards.MovesOf(creature.MergedFrom);

            for (var i = 0; i < moves.Count; i++)
            {
                AddMoveActions(state, player, slot, creature, moves[i], i, actions);
            }
        }
    }

    private static void AddMoveActions(
        GameState state, PlayerId player, SlotIndex slot, CreatureInstance creature,
        MoveDefinition move, int moveIndex, List<GameAction> actions)
    {
        // Each move once per turn; different moves on one creature are independent.
        if (creature.HasUsedMove(moveIndex))
        {
            return;
        }

        if (!state[player].CanAfford(move.Cost))
        {
            return;
        }

        var ctx = new EffectContext(state, player, slot, chosenTarget: null, move.AttackType);

        // An unmet condition makes the move illegal outright, rather than a legal action that
        // resolves to nothing -- see ConditionEvaluator for why that distinction matters.
        if (move.Condition is not null && !ConditionEvaluator.Evaluate(ctx, move.Condition))
        {
            return;
        }

        var chosen = move.ChosenSelector;

        if (chosen is null)
        {
            actions.Add(new UseMoveAction(player, slot, moveIndex));
            return;
        }

        // One action per valid target: this is the expansion the plan's single-target rule
        // keeps to N rather than N x M. Zero candidates means zero actions -- a targeted move
        // with nothing to target is simply not offered.
        foreach (var target in TargetResolver.ChosenCandidates(ctx, chosen.Value))
        {
            actions.Add(new UseMoveAction(player, slot, moveIndex, target));
        }
    }

    // Merging: free, between two adjacent un-merged friendly creatures.
    //
    // Both directions are generated for each eligible pair. The result differs by which slot it
    // occupies, which changes what it faces for scoring -- so these are two real choices, not a
    // duplicate.
    private static void AddMergeActions(GameState state, PlayerId player, List<GameAction> actions)
    {
        if (!state.Rules.MergeEnabled)
        {
            return;
        }

        var creatures = state.Board.CreaturesOf(player).ToList();

        foreach (var (sourceSlot, source) in creatures)
        {
            foreach (var (targetSlot, target) in creatures)
            {
                if (sourceSlot == targetSlot)
                {
                    continue;
                }

                if (state.Rules.MergeRequiresAdjacent && !sourceSlot.IsAdjacentTo(targetSlot))
                {
                    continue;
                }

                // Depth is checked on the COMBINED result, not on each operand: merging is
                // capped by how many cards end up folded together. With the default
                // MaxMergeDepth of 2 this reduces to "neither may already be merged", but
                // stating it as the sum is what makes a ruleset raising the cap behave sensibly
                // rather than silently still forbidding everything.
                if (source.MergeDepth + target.MergeDepth > state.Rules.MaxMergeDepth)
                {
                    continue;
                }

                actions.Add(new MergeAction(player, sourceSlot, targetSlot));
            }
        }
    }
}
