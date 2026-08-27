using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Ai.Search;

// DESIGN.md step 3.2: a lightly heuristic playout, replacing IsMctsAgent's uniform-random rollout.
// Uniform playouts waste iterations on lines no real player would take -- passing with
// resources unspent, ignoring a lethal move -- so a playout biased toward damage/scoring should
// make the search's statistics converge on better play with the same iteration budget. Whether
// it actually does is Phase 3's to measure with Shapes.Sim, not to assume; UniformPlayoutPolicy
// stays selectable as the control specifically so that comparison is possible.
//
// DELIBERATELY NOT GreedyAgent's heuristic reused wholesale. GreedyAgent scores from an
// AgentContext/ObservedState because that is all a real decision has access to; a playout step
// runs on this iteration's own determinized GameState, so it can resolve real targets through
// EffectContext/TargetResolver instead of re-deriving a simplified target reader. It is also
// deliberately CHEAPER than GreedyAgent's: a playout runs this once per remaining ply of every
// iteration, not once per Choose, so scoring must stay to the same handful of ops GreedyAgent
// treats as "understood" (damage, its lethal bonus, and play-a-creature's board-presence value)
// and must not walk into a full effect-interpreter reimplementation. Everything else this cannot
// price is treated as a small flat positive, exactly like GreedyAgent's unknown-effect fallback,
// so an unread card still beats passing without being mistaken for something stronger.
public sealed class HeuristicPlayoutPolicy : IPlayoutPolicy
{
    public static readonly HeuristicPlayoutPolicy Instance = new();

    private const double DamageWeight = 1.0;
    private const double LethalBonus = 4.0;
    private const double PlayCreatureWeight = 2.0;
    private const double UnopposedSlotBonus = 1.5;
    private const double BlockingSlotBonus = 1.8;
    private const double UnknownEffectWeight = 0.4;
    private const double EndTurnScore = -0.5;

    public GameAction SelectAction(
        GameState state, IReadOnlyList<GameAction> legal, CardDatabase cards, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legal);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(random);

        var best = legal[0];
        var bestScore = double.NegativeInfinity;
        var ties = 0;

        foreach (var action in legal)
        {
            var score = Score(state, cards, action);

            if (score > bestScore)
            {
                best = action;
                bestScore = score;
                ties = 1;
                continue;
            }

            // Reservoir sampling across ties, exactly as GreedyAgent does -- indistinguishable
            // actions must stay uniformly likely, or a playout would systematically favor
            // whatever order the generator happens to emit.
            if (score == bestScore && random.Next(++ties) == 0)
            {
                best = action;
            }
        }

        return best;
    }

    private double Score(GameState state, CardDatabase cards, GameAction action) => action switch
    {
        UseMoveAction move => ScoreMove(state, cards, move),
        PlayCardAction play => ScorePlayCard(state, cards, play),
        MergeAction => 0.0,
        EndTurnAction => EndTurnScore,
        _ => 0.0,
    };

    private double ScoreMove(GameState state, CardDatabase cards, UseMoveAction action)
    {
        var source = state.Board[action.SourceSlot];
        if (source is null)
        {
            return 0.0;
        }

        var moves = cards.MovesOf(source.MergedFrom);
        if (action.MoveIndex >= moves.Count)
        {
            return 0.0;
        }

        var move = moves[action.MoveIndex];
        var ctx = new EffectContext(
            state, action.Player, action.SourceSlot, action.ChosenTarget, move.AttackType);

        return ScoreEffects(ctx, move.Effects);
    }

    private double ScorePlayCard(GameState state, CardDatabase cards, PlayCardAction action)
    {
        var card = cards.Get(action.CardId);

        if (!card.IsCreature)
        {
            var ctx = new EffectContext(
                state, action.Player, sourceSlot: null, action.ChosenTarget, card.AttackType);
            return ScoreEffects(ctx, card.Effects);
        }

        var score = PlayCreatureWeight;

        if (action.TargetSlot is { } slot)
        {
            var facing = state.Board.Opposing(slot);

            if (facing is null)
            {
                score += UnopposedSlotBonus;
            }
            else if (state.Board.IsUnopposed(slot.Opposing()))
            {
                score += BlockingSlotBonus;
            }
        }

        return score;
    }

    // Only damage is priced by resolving real targets -- the one op whose value swings hard
    // enough (a kill, or not) to be worth the EffectContext/TargetResolver cost during a
    // playout. Everything else this doesn't have a case for, including heal, falls through to
    // the flat unknown weight: still positive so it beats passing, small so it never outranks a
    // visible kill.
    private double ScoreEffects(EffectContext ctx, IReadOnlyList<EffectNode> effects)
    {
        var total = 0.0;

        foreach (var effect in effects)
        {
            total += effect.Op switch
            {
                "damage" => ScoreDamage(ctx, effect),
                "conditional" => ScoreEffects(ctx, effect.Args.NodesOrSingle("then")),
                "for_each" => ScoreEffects(ctx, effect.Args.NodesOrSingle("effects")),
                _ => UnknownEffectWeight,
            };
        }

        return total;
    }

    private double ScoreDamage(EffectContext ctx, EffectNode effect)
    {
        var amount = effect.Args.IntOrDefault("amount", 0);
        if (amount <= 0)
        {
            return 0.0;
        }

        var source = ctx.SourceCreature;
        var flat = amount + (source?.AttackBuff ?? 0) + (source?.NextAttackBonus ?? 0);

        var total = 0.0;

        foreach (var slot in TargetResolver.Resolve(ctx, effect.Args.Target()))
        {
            var target = ctx.State.Board[slot];
            if (target is null)
            {
                continue;
            }

            var multiplier = ctx.MoveType is { } type
                ? ctx.State.Rules.TypeChart.MultiplierAgainst(type, target.Types)
                : 1.0;

            var dealt = (int)(flat * multiplier);
            var landed = Math.Min(dealt, target.Health);

            total += landed * DamageWeight;

            if (dealt >= target.Health)
            {
                total += LethalBonus;
            }
        }

        return total;
    }
}
