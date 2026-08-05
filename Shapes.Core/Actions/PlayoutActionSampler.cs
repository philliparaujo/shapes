using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Actions;

// PLAN.md step 3.3b: a cheap playout-only path to ONE uniformly-random legal action, without
// materializing Generate's List<GameAction>/HashSet machinery for a caller that only needs a
// single pick.
//
// Profiling (step 3.3) found playout's ActionGenerator.Generate + ActionExecutor.Apply calls are
// 86.4% of one search iteration's cost, at ~98 calls per playout. Generate builds a List, one or
// two HashSets, and an EffectContext per hand card/move considered -- but MEASURING this sampler
// (step 3.3b's own before/after) found the List/HashSet allocations are the SMALLER share of that
// cost: this still has to consider every candidate, and therefore still builds every
// EffectContext and calls TargetResolver exactly as Generate does, to reservoir-sample correctly
// over them. The measured win is a real but modest ~1.07x (~6.6%) per-decision speedup, not a
// re-run of step 3.3a's 2.1x -- see PLAN.md step 3.3b for the numbers and the profiling
// re-reading that explains the gap.
//
// This walks the exact same traversal Generate does -- same order, same legality checks, same
// target expansion -- but instead of appending to a list, it reservoir-samples: keep the current
// pick with probability 1/n as the n-th candidate is produced. One pass, O(1) extra space. The
// result is uniform over the same legal set Generate(state, cards) would have produced -- proven
// as SET MEMBERSHIP plus a standalone distribution check in PlayoutActionSamplerTests, not as
// exact per-call agreement with `legal[random.Next(legal.Count)]` (that test's header explains
// why draw-for-draw agreement is the wrong property to check). This file must never drift from
// Generate's traversal, since a silent divergence would mean the playout is sampling from a
// different (and wrong) legal set.
//
// Additive only: ActionGenerator.Generate itself, and every other caller (console, tree
// expansion, tests), is untouched.
public static class PlayoutActionSampler
{
    // Uniformly samples one legal action for `player` to take in `state`, or null if none exist
    // (mirrors Generate's non-emptiness invariant not holding only when Generate itself would
    // also return empty -- a finished game or a phase outside Actions).
    public static GameAction? SampleOne(GameState state, CardDatabase cards, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(random);

        if (state.IsOver || state.Phase != TurnPhase.Actions)
        {
            return null;
        }

        var player = state.ActivePlayer;
        var reservoir = new Reservoir(random);

        if (state.AwaitingDiscard)
        {
            SampleDiscardActions(state, player, ref reservoir);
            return reservoir.Picked;
        }

        SamplePlayCardActions(state, cards, player, ref reservoir);
        SampleUseMoveActions(state, cards, player, ref reservoir);
        SampleMergeActions(state, player, ref reservoir);

        reservoir.Consider(new EndTurnAction(player));

        return reservoir.Picked;
    }

    // Reservoir sampling of size 1: the n-th candidate replaces the current pick with probability
    // 1/n, which -- by induction -- leaves every candidate seen so far with probability 1/count
    // once the pass completes, identical to `legal[random.Next(legal.Count)]`.
    private ref struct Reservoir(IRandomSource random)
    {
        private readonly IRandomSource _random = random;
        private int _count;

        public GameAction? Picked { get; private set; }

        public void Consider(GameAction action)
        {
            _count++;
            if (_random.Next(_count) == 0)
            {
                Picked = action;
            }
        }
    }

    private static void SamplePlayCardActions(
        GameState state, CardDatabase cards, PlayerId player, ref Reservoir reservoir)
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
                SampleCreaturePlays(state, player, card, ref reservoir);
            }
            else
            {
                SampleSpellPlays(state, player, card, ref reservoir);
            }
        }
    }

    private static void SampleCreaturePlays(
        GameState state, PlayerId player, CardDefinition card, ref Reservoir reservoir)
    {
        var chosen = card.ChosenSelector;

        foreach (var slot in state.Board.EmptySlotsOf(player))
        {
            if (chosen is null)
            {
                reservoir.Consider(new PlayCardAction(player, card.Id, slot));
                continue;
            }

            var ctx = new EffectContext(state, player, slot, chosenTarget: null);
            foreach (var target in TargetResolver.ChosenCandidates(ctx, chosen.Value))
            {
                reservoir.Consider(new PlayCardAction(player, card.Id, slot, target));
            }
        }
    }

    private static void SampleSpellPlays(
        GameState state, PlayerId player, CardDefinition card, ref Reservoir reservoir)
    {
        var chosen = card.ChosenSelector;

        if (chosen is null)
        {
            reservoir.Consider(new PlayCardAction(player, card.Id));
            return;
        }

        var ctx = new EffectContext(state, player, sourceSlot: null, chosenTarget: null, card.AttackType);
        var candidates = TargetResolver.ChosenCandidates(ctx, chosen.Value);

        foreach (var target in candidates)
        {
            reservoir.Consider(new PlayCardAction(player, card.Id, targetSlot: null, chosenTarget: target));
        }
    }

    private static void SampleUseMoveActions(
        GameState state, CardDatabase cards, PlayerId player, ref Reservoir reservoir)
    {
        foreach (var (slot, creature) in state.Board.CreaturesOf(player))
        {
            if (creature.IsStunned)
            {
                continue;
            }

            var moves = cards.MovesOf(creature.MergedFrom);

            for (var i = 0; i < moves.Count; i++)
            {
                SampleMoveActions(state, player, slot, creature, moves[i], i, ref reservoir);
            }
        }
    }

    private static void SampleMoveActions(
        GameState state, PlayerId player, SlotIndex slot, CreatureInstance creature,
        MoveDefinition move, int moveIndex, ref Reservoir reservoir)
    {
        if (creature.HasUsedMove(moveIndex))
        {
            return;
        }

        if (!state[player].CanAfford(move.Cost))
        {
            return;
        }

        var ctx = new EffectContext(state, player, slot, chosenTarget: null, move.AttackType);

        if (move.Condition is not null && !ConditionEvaluator.Evaluate(ctx, move.Condition))
        {
            return;
        }

        var chosen = move.ChosenSelector;

        if (chosen is null)
        {
            reservoir.Consider(new UseMoveAction(player, slot, moveIndex));
            return;
        }

        foreach (var target in TargetResolver.ChosenCandidates(ctx, chosen.Value))
        {
            reservoir.Consider(new UseMoveAction(player, slot, moveIndex, target));
        }
    }

    private static void SampleDiscardActions(GameState state, PlayerId player, ref Reservoir reservoir)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cardId in state[player].Hand)
        {
            if (seen.Add(cardId))
            {
                reservoir.Consider(new DiscardAction(player, cardId));
            }
        }
    }

    private static void SampleMergeActions(GameState state, PlayerId player, ref Reservoir reservoir)
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

                if (source.MergeDepth + target.MergeDepth > state.Rules.MaxMergeDepth)
                {
                    continue;
                }

                reservoir.Consider(new MergeAction(player, sourceSlot, targetSlot));
            }
        }
    }
}
