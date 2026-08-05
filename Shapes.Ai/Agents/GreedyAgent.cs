using System.Threading;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Ai.Agents;

// Scores every legal action with a one-ply heuristic and takes the best one.
//
// The upper half of the plan's step 2.4 yardstick: RandomAgent is the floor an IS-MCTS must
// beat outright, this is the bar it must clear comfortably. It plays the way a beginner does --
// kill what it can reach, fill the board, never pass while something useful is affordable --
// which is exactly the standard a search has to beat to be worth its cost.
//
// HOW STRONG IT IS, IN NUMBERS, IS NOT DEFINED HERE. Agent strength is a batch measurement and
// belongs to Phase 3's Shapes.Sim, which runs it properly (parallel, seeded, both seats, across
// rulesets). What this class owes the comparison is not a score but a set of properties that make
// the comparison meaningful at all: deterministic given a position, independent of the search it
// will be measured against, and stable when Phase 4 reprices cards. Those are the things the
// tests check.
//
// == Why this does NOT simulate ==
//
// The obvious implementation of "one-ply" is: apply each action to a clone, evaluate the
// resulting state, keep the best. That is not available here, and deliberately so. An agent sees
// an ObservedState, which carries no GameState and no path to one (see ObservedState's doc
// comment) -- so applying an action would first require DETERMINIZING, and a determinize-then-
// simulate greedy agent is precisely a one-iteration IS-MCTS.
//
// That would make it a bad yardstick in two specific ways:
//
//   1. IT WOULD MEASURE ITSELF. Comparing IS-MCTS against this baseline is meant to be evidence
//      that search works. If the baseline IS the same search at one iteration, the comparison
//      mostly measures iteration count, and a bug in selection/backprop shared by both would
//      cancel out instead of showing up.
//   2. IT WOULD BE NOISY. Its strength would depend on which world it happened to sample, putting
//      variance into the comparison it exists to make clean.
//
// So this scores actions from static card data and the observed position instead: it reads what a
// move WILL do rather than doing it. That is weaker in principle -- it cannot see an effect it
// has no rule for -- but it is independent of the search, cheap enough to run large batches
// without thinking about it, and deterministic given the position.
//
// == What it understands, and what it does not ==
//
// UNDERSTOOD: flat `damage` (through the type chart, including the merged-target rule and
// next_attack_bonus/attack_buff), lethal as a bonus on top of damage, `heal` on a damaged
// creature, board presence and the scoring value of an unopposed slot, and the fact that ending
// the turn with resources unspent is usually wrong.
//
// NOT UNDERSTOOD, and scored as a small flat "probably does something" value: every scaled op,
// draws, resource gains, status keywords, and control flow. A greedy agent that tried to model
// `damage_scaled` correctly would be re-implementing the effect interpreter against a state it
// cannot see, which is how a baseline turns into a second, drifting definition of the rules. The
// unknown-effect fallback is deliberately positive-but-small: a card whose effect this cannot
// read should still be played over passing, but should lose to a move that visibly kills
// something.
//
// TIE-BREAKING IS RANDOM, through the injected IRandomSource, not first-past-the-post. Two
// actions scoring identically are genuinely indistinguishable to this heuristic, and always
// taking the earliest would bias play toward low slot indices and toward whatever order the
// generator happens to emit -- a systematic bias that would show up in Phase 4's per-card
// statistics as a property of the card list's ordering. Reservoir sampling over the ties keeps
// the choice uniform in one pass.
public sealed class GreedyAgent : IAgent
{
    private readonly IRandomSource _random;

    // -- Score weights ----------------------------------------------------------------------
    //
    // Relative, not absolute: only their ordering matters, and they are tuned to express a few
    // priorities in order -- kill something > damage something > develop the board > do
    // something unread > pass. Constants rather than a config object because this is a fixed
    // yardstick; a baseline whose strength drifts with a settings file cannot be compared across
    // Phase 4 runs.

    // Per point of damage actually landed (after type effectiveness, capped at the target's
    // remaining health -- overkill is not value).
    private const double DamageWeight = 1.0;

    // On top of the damage score when the blow is lethal. Large, because removing a creature
    // also stops its income and frees the facing slot to score, none of which a damage count
    // captures.
    private const double LethalBonus = 4.0;

    // Per point of healing actually applied (capped at missing health).
    private const double HealWeight = 0.5;

    // Playing a creature: board presence is the engine's main currency (it scores and it pays
    // income), so this outranks an unread effect but not a kill.
    private const double PlayCreatureWeight = 2.0;

    // Extra for playing a creature into a slot whose facing slot is empty -- it starts scoring
    // next turn.
    private const double UnopposedSlotBonus = 1.5;

    // Extra for playing a creature into a slot that FACES an enemy creature which is currently
    // unopposed -- placing here stops it scoring.
    //
    // Worth slightly MORE than taking an unopposed slot, and the ordering is deliberate. Both are
    // worth one point per turn in the scoring race, but they differ in timing and in certainty:
    //
    //   - Blocking denies a point at the opponent's VERY NEXT scoring step, which happens
    //     immediately when this turn ends (ActionExecutor.ApplyEndTurn runs the opponent's
    //     AdvanceToActions, whose first step is ApplyScoring).
    //   - An unopposed placement pays its first point a full turn later, at the start of this
    //     player's next turn -- and only if it is still unopposed by then, which the opponent
    //     gets a turn to prevent.
    //
    // So blocking is the sooner and the surer of the two. Without this the agent scored a
    // blocking placement at exactly zero and would pass up denying a point per turn in favour of
    // a tie-break between two indistinguishable open slots -- measured, not hypothesised.
    private const double BlockingSlotBonus = 1.8;

    // Per point of the creature's health, folded in so a bigger body is preferred when several
    // are affordable.
    private const double HealthWeight = 0.3;

    // An effect this heuristic has no rule for. Positive so an unread card beats passing;
    // small so it loses to anything visible.
    private const double UnknownEffectWeight = 0.4;

    // Merging: mildly positive as a tiebreak, deliberately NOT enthusiastic. The plan flags
    // merge pricing as an open Phase 4 question, and a baseline that merged eagerly would bias
    // the very measurement meant to answer it (PLAN.md step 4.2a asks whether the AI ever
    // DECLINES a merge). Scored below playing a creature, since merging costs a board slot --
    // and with it a scoring body and its income.
    private const double MergeWeight = 0.2;

    // Ending the turn. Negative, so anything scoring above zero is preferred: passing with
    // resources unspent is the single most common way a weak agent throws a game away, and
    // RandomAgent's habit of doing it is most of why it is beatable. Not so negative that the
    // agent takes actively bad actions to avoid it -- an action must still clear zero.
    private const double EndTurnScore = -0.5;

    // Discarding. Only ever offered while a debt stands, when it is the ONLY legal action kind,
    // so this competes against nothing but other discards -- the value is arbitrary and the
    // ordering among discards is what matters. Cheapest card first: see ScoreDiscard.
    private const double DiscardBase = 0.0;

    public GreedyAgent(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public string Name => "Greedy";

    public GameAction Choose(AgentContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // No cancellation check: scoring is linear in the action list with no search inside it,
        // so this completes in microseconds. The token is accepted to satisfy the interface and
        // observed by agents that can actually run long.
        var best = context.LegalActions[0];
        var bestScore = double.NegativeInfinity;
        var ties = 0;

        foreach (var action in context.LegalActions)
        {
            var score = Score(context, action);

            if (score > bestScore)
            {
                best = action;
                bestScore = score;
                ties = 1;
                continue;
            }

            // Reservoir sampling across equal scores: the nth tie replaces the incumbent with
            // probability 1/n, which leaves every tied action equally likely in a single pass
            // without materialising the tied set.
            if (score == bestScore && _random.Next(++ties) == 0)
            {
                best = action;
            }
        }

        return best;
    }

    // What one action is worth, in the weights above. Higher is better; only the ordering
    // within a single call matters, never the absolute value.
    private double Score(AgentContext context, GameAction action) => action switch
    {
        UseMoveAction move => ScoreMove(context, move),
        PlayCardAction play => ScorePlayCard(context, play),
        MergeAction => MergeWeight,
        DiscardAction discard => ScoreDiscard(context, discard),
        EndTurnAction => EndTurnScore,
        _ => 0.0,
    };

    // A move is worth what its effect list does, with the acting creature as the source.
    private double ScoreMove(AgentContext context, UseMoveAction action)
    {
        var source = context.State.Board[action.SourceSlot];

        // Unreachable via a generated action -- the generator only offers moves for creatures
        // that are on the board -- but scoring is a read-only pass and should not throw on a
        // hand-built context.
        if (source is null)
        {
            return 0.0;
        }

        var moves = context.Cards.MovesOf(source.MergedFrom);

        if (action.MoveIndex >= moves.Count)
        {
            return 0.0;
        }

        var move = moves[action.MoveIndex];

        return ScoreEffects(
            context, move.Effects, source, action.SourceSlot, action.ChosenTarget, move.AttackType);
    }

    // A creature is worth its presence on the board; a spell is worth what its effects do.
    private double ScorePlayCard(AgentContext context, PlayCardAction action)
    {
        var card = context.Cards.Get(action.CardId);

        if (!card.IsCreature)
        {
            // A spell has no creature source, so effects reading "self" score nothing -- which
            // is correct, since a spell's self-targeted effects have no creature to act on
            // either.
            return ScoreEffects(
                context, card.Effects, source: null, sourceSlot: null, action.ChosenTarget,
                card.AttackType);
        }

        var score = PlayCreatureWeight + (card.Health * HealthWeight);

        // Which slot a creature goes into is a scoring decision, and there are two ways it pays.
        // TargetSlot is non-null for a creature play; the pattern keeps this total rather than
        // assuming the generator's shape.
        if (action.TargetSlot is { } slot)
        {
            var facing = context.State.Board.Opposing(slot);

            if (facing is null)
            {
                // Nothing across from it: it begins scoring at the start of this player's next
                // turn. The win condition, so it is worth reaching for.
                score += UnopposedSlotBonus;
            }
            else if (context.State.Board.IsUnopposed(slot.Opposing()))
            {
                // An enemy creature is there AND it is currently unopposed, meaning it scores a
                // point every turn. Placing here opposes it and stops that, starting with the
                // opponent's scoring step the moment this turn ends. See BlockingSlotBonus for
                // why this outranks taking an open slot.
                score += BlockingSlotBonus;
            }
        }

        return score;
    }

    // Which card to pitch to a pending "discard N".
    //
    // Cheapest first, by total pip cost. A crude proxy for "least valuable", but it is the one
    // ordering available without an evaluation of cards in hand that this agent does not have --
    // and it is at least not arbitrary, which is what matters for a reproducible baseline.
    // Negated so a cheaper card scores HIGHER and is therefore chosen.
    private double ScoreDiscard(AgentContext context, DiscardAction action)
    {
        var cost = context.Cards.Get(action.CardId).Cost;
        return DiscardBase - cost.Total;
    }

    // Sums the value of an effect list. The heart of the heuristic.
    //
    // Reads static card data against the OBSERVED board -- it does not apply anything. Effects
    // are scored independently and summed, which ignores interactions between them (a heal after
    // a self-damage in the same list, say). That is a real limitation and an accepted one: the
    // alternative is a second implementation of the effect interpreter.
    private double ScoreEffects(
        AgentContext context, IReadOnlyList<EffectNode> effects, CreatureInstance? source,
        SlotIndex? sourceSlot, SlotIndex? chosenTarget, ResourceType? attackType)
    {
        var total = 0.0;

        foreach (var effect in effects)
        {
            total += ScoreEffect(context, effect, source, sourceSlot, chosenTarget, attackType);
        }

        return total;
    }

    private double ScoreEffect(
        AgentContext context, EffectNode effect, CreatureInstance? source, SlotIndex? sourceSlot,
        SlotIndex? chosenTarget, ResourceType? attackType)
    {
        switch (effect.Op)
        {
            case "damage":
                return ScoreDamage(context, effect, source, sourceSlot, chosenTarget, attackType);

            case "heal":
                return ScoreHeal(context, effect, sourceSlot, chosenTarget);

            // Control flow is scored by its branches rather than as an unknown: a conditional
            // wrapping a big damage effect is worth roughly that damage, and treating it as an
            // opaque unknown would make every gated card look identical to a cantrip. The
            // condition itself is not evaluated -- doing so needs the real ConditionEvaluator
            // against a GameState -- so this optimistically assumes the branch is taken, which
            // matches how the generator already filtered out moves whose top-level condition
            // fails.
            case "conditional":
                return ScoreEffects(
                    context, effect.Args.NodesOrSingle("then"), source, sourceSlot, chosenTarget,
                    attackType);

            case "for_each":
                return ScoreEffects(
                    context, effect.Args.NodesOrSingle("effects"), source, sourceSlot, chosenTarget,
                    attackType);

            // Everything else -- every scaled op, draws, resource gains, keywords, summons,
            // destroys, modifiers. Worth something, since a card that does nothing would not
            // have been printed, but not something this can price. See the class doc comment.
            default:
                return UnknownEffectWeight;
        }
    }

    // Damage: what it would actually land, summed over every creature the selector hits.
    //
    // Capped at the target's remaining health, so overkill is not counted as value -- a 5-damage
    // move into a 1-health creature is worth the same kill as a 1-damage move, and the agent
    // should prefer to spend the small one. Then the lethal bonus, once per creature killed.
    private double ScoreDamage(
        AgentContext context, EffectNode effect, CreatureInstance? source, SlotIndex? sourceSlot,
        SlotIndex? chosenTarget, ResourceType? attackType)
    {
        var amount = effect.Args.IntOrDefault("amount", 0);

        if (amount <= 0)
        {
            return 0.0;
        }

        // The flat modifiers the real damage path applies before the type multiplier. Read, not
        // consumed -- this is a scoring pass over a state it must not mutate.
        var flat = amount + (source?.AttackBuff ?? 0) + (source?.NextAttackBonus ?? 0);

        var total = 0.0;

        foreach (var (_, target) in TargetsOf(context, effect, sourceSlot, chosenTarget))
        {
            var multiplier = attackType is { } type
                ? context.State.Rules.TypeChart.MultiplierAgainst(type, target.Types)
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

    // Healing is worth what it would actually restore -- nothing on an undamaged creature, which
    // is what stops the agent spending a turn healing a full-health body.
    private double ScoreHeal(
        AgentContext context, EffectNode effect, SlotIndex? sourceSlot, SlotIndex? chosenTarget)
    {
        var amount = effect.Args.IntOrDefault("amount", 0);

        if (amount <= 0)
        {
            return 0.0;
        }

        var total = 0.0;

        foreach (var (_, target) in TargetsOf(context, effect, sourceSlot, chosenTarget))
        {
            total += Math.Min(amount, target.MaxHealth - target.Health) * HealWeight;
        }

        return total;
    }

    // Which creatures an effect's selector resolves to, on the observed board.
    //
    // A deliberately simplified re-reading of TargetResolver: it ignores taunt (which only
    // narrows chosen_* candidates, and the generator has already applied that narrowing to the
    // actions being scored) and it resolves chosen_* to the action's own carried target rather
    // than enumerating candidates. Both are safe here because this scores an action the
    // generator already produced, so its target is known-legal.
    //
    // It reads the board off ObservedState, which is public information for both players -- no
    // hidden state enters the heuristic.
    private static IEnumerable<(SlotIndex Slot, CreatureInstance Creature)> TargetsOf(
        AgentContext context, EffectNode effect, SlotIndex? sourceSlot, SlotIndex? chosenTarget)
    {
        if (!effect.Args.Has("target"))
        {
            yield break;
        }

        var board = context.State.Board;
        var player = context.Player;
        var selector = effect.Args.Target();

        switch (selector)
        {
            case TargetSelector.Self:
                if (sourceSlot is { } self && board[self] is { } selfCreature)
                {
                    yield return (self, selfCreature);
                }

                break;

            case TargetSelector.Opposing:
                if (sourceSlot is { } from)
                {
                    var opposing = from.Opposing();
                    if (board[opposing] is { } opposed)
                    {
                        yield return (opposing, opposed);
                    }
                }

                break;

            case TargetSelector.LeftFriendly:
            case TargetSelector.RightFriendly:
                if (sourceSlot is { } origin)
                {
                    var offset = selector == TargetSelector.LeftFriendly ? -1 : 1;
                    var neighbour = origin.Slot + offset;

                    if (neighbour >= 0 && neighbour < SlotIndex.SlotsPerPlayer)
                    {
                        var slot = new SlotIndex(origin.Owner, neighbour);
                        if (board[slot] is { } creature)
                        {
                            yield return (slot, creature);
                        }
                    }
                }

                break;

            case TargetSelector.AllEnemies:
                foreach (var entry in board.CreaturesOf(player.Opponent()))
                {
                    yield return entry;
                }

                break;

            case TargetSelector.AllFriendlies:
                foreach (var entry in board.CreaturesOf(player))
                {
                    yield return entry;
                }

                break;

            case TargetSelector.ChosenEnemy:
            case TargetSelector.ChosenFriendly:
                // The action already carries the resolved choice -- that is what picking THIS
                // action out of the legal list meant.
                if (chosenTarget is { } chosen && board[chosen] is { } chosenCreature)
                {
                    yield return (chosen, chosenCreature);
                }

                break;
        }
    }
}
