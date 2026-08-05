using Shapes.Core.Effects;
using Shapes.Core.Primitives;

namespace Shapes.Core.Cards;

// Raised when a card is structurally well-formed JSON but breaks a rule of the card schema.
// CardLoader wraps this in a CardLoadException that names the source file.
public sealed class CardValidationException : Exception
{
    public CardValidationException(string message)
        : base(message)
    {
    }
}

// The card schema's rules, in one place.
//
// A typo in card data must fail here, at load, rather than becoming a silent gameplay bug --
// an unknown op that throws mid-game during a Phase 4 sim run costs a whole batch, and an
// unknown op that quietly does nothing is worse, because the run completes and the numbers
// look plausible.
//
// Known-op checking reads EffectRegistry rather than a list maintained here, so the vocabulary
// cannot drift from what the interpreter can actually execute.
public static class CardValidator
{
    public static void Validate(CardDefinition card)
    {
        ArgumentNullException.ThrowIfNull(card);

        ValidateShape(card);

        foreach (var move in card.Moves)
        {
            ValidateMove(card, move);
        }

        ValidateEffects(card.Effects, $"card '{card.Id}'");

        ValidateSingleTargetRule(card);
    }

    private static void ValidateShape(CardDefinition card)
    {
        if (card.IsCreature)
        {
            // A 0-health creature would die the instant it was placed. Almost always a
            // missing "health" key rather than an intent.
            if (card.Health < 1)
            {
                throw new CardValidationException(
                    $"creature must have health of at least 1 (got {card.Health}).");
            }

            if (card.Types.IsEmpty)
            {
                throw new CardValidationException(
                    "creature must declare at least one type -- it determines income and defense.");
            }

            // A creature's defensive type comes from its play cost (see the confirmed
            // ruleset), same as an attacker's type comes from a move's cost. 'types' stays an
            // explicit field rather than being derived outright -- see CardDefinition.Types --
            // but the two must never drift apart, so this cross-checks them at load rather than
            // trusting an author to keep them in sync by hand.
            var costTypes = TypeMask.Of(ResourceTypes.All.Where(t => card.Cost[t] > 0).ToArray());
            if (card.Types != costTypes)
            {
                throw new CardValidationException(
                    $"creature 'types' ({card.Types}) must match the resource type(s) in its cost " +
                    $"({card.Cost}) -- a creature's defensive type comes from what it costs to play.");
            }

            if (card.Effects.Count > 0)
            {
                // No passive or triggered effects exist: all creature damage comes from
                // activated moves. A top-level effect list on a creature would silently never
                // run, so it is an error rather than a no-op.
                throw new CardValidationException(
                    "creature must not declare top-level 'effects' -- creature effects belong to a move. " +
                    "There are no passive or triggered effects.");
            }

            return;
        }

        // Spells resolve and are gone: they never occupy a slot, so board stats are meaningless
        // on them and a declared value means the author expected something that will not happen.
        if (card.Health != 0)
        {
            throw new CardValidationException("spell must not declare 'health'.");
        }

        if (!card.Types.IsEmpty)
        {
            throw new CardValidationException(
                "spell must not declare 'types' -- a spell has no board presence to defend, so " +
                "'types' (a defensive property) is meaningless for it. Its own attacking type is " +
                "derived from its cost instead -- see CardDefinition.AttackType.");
        }

        // A spell's attacking type is read from its cost (see CardDefinition.AttackType), same
        // rule and same reasoning as a move's: a mixed cost has no single answer, and rejecting
        // beats inventing a tie-break nobody asked for.
        if (CostTypeCount(card.Cost) > 1)
        {
            throw new CardValidationException(
                $"spell has a mixed-type cost ({card.Cost}). A spell's attacking type is derived " +
                "from its cost, so the cost must use a single resource type (or be free, for " +
                "typeless damage).");
        }

        if (card.Moves.Count > 0)
        {
            throw new CardValidationException("spell must not declare 'moves'.");
        }

        if (card.Effects.Count == 0)
        {
            throw new CardValidationException("spell must declare at least one effect.");
        }
    }

    private static void ValidateMove(CardDefinition card, MoveDefinition move)
    {
        var at = $"move '{move.Name}'";

        if (move.Effects.Count == 0)
        {
            throw new CardValidationException($"{at} declares no effects.");
        }

        // A move's attacking type is read from its cost (see MoveDefinition.AttackType), so a
        // cost mixing two types has no single answer -- EffectContext.MoveType models one
        // attacking type, deliberately. Rejecting the card is the alternative to inventing a
        // tie-break rule nobody asked for; a mixed-cost attacker can be revisited if a real
        // card ever wants one.
        if (CostTypeCount(move.Cost) > 1)
        {
            throw new CardValidationException(
                $"{at} has a mixed-type cost ({move.Cost}). A move's attacking type is derived from " +
                "its cost, so the cost must use a single resource type (or be free, for typeless damage).");
        }

        if (move.Condition is not null)
        {
            ValidateCondition(move.Condition, $"{at} condition");
        }

        ValidateEffects(move.Effects, at);
    }

    private static int CostTypeCount(ResourcePool cost) =>
        ResourceTypes.All.Count(t => cost[t] > 0);

    private static readonly string[] KnownCreatureStateChecks = ["damaged", "full_health", "unopposed"];

    private static void ValidateCondition(EffectNode condition, string at)
    {
        // Conditions are a separate tiny vocabulary from effect ops -- ConditionEvaluator owns
        // it. Listing every predicate NAME here would create the second list the registry
        // pattern exists to avoid, so this only rejects the confusion an author is actually
        // likely to hit: naming a known EFFECT op where a predicate belongs.
        if (EffectRegistry.IsKnown(condition.Op))
        {
            throw new CardValidationException(
                $"{at} names the effect op '{condition.Op}'. A condition takes a predicate " +
                "(e.g. 'creature_state'), not an effect.");
        }

        if (condition.Op != "creature_state")
        {
            throw new CardValidationException(
                $"{at} uses unknown condition predicate '{condition.Op}'. The only predicate is 'creature_state'.");
        }

        // Parsing the target here, same as ValidateEffect does for an effect's own "target",
        // means a typo'd selector on a CONDITION fails at load rather than the first time the
        // gated move is evaluated.
        if (condition.Args.Has("target"))
        {
            try
            {
                condition.Args.Target();
            }
            catch (ArgumentException ex)
            {
                throw new CardValidationException($"{at}: {ex.Message}");
            }
        }

        var check = condition.Args.String("check");
        if (!KnownCreatureStateChecks.Contains(check, StringComparer.Ordinal)
            && !check.StartsWith("health_at_most:", StringComparison.Ordinal))
        {
            throw new CardValidationException(
                $"{at} uses unknown creature_state check '{check}'. Known checks: " +
                $"{string.Join(", ", KnownCreatureStateChecks)}, or 'health_at_most:<n>'.");
        }
    }

    // Walks the whole effect tree, not just the top level: a card could otherwise hide an
    // unknown op inside a conditional's else branch and load clean. EffectTree owns the descent
    // so this and the single-target walk below cannot disagree about what "every effect on this
    // card" means -- and so ActionGenerator, which finds the chosen target the same way, agrees
    // with both.
    private static void ValidateEffects(IReadOnlyList<EffectNode> effects, string at)
    {
        foreach (var effect in EffectTree.WalkAll(effects))
        {
            ValidateEffect(effect, at);
        }
    }

    private static void ValidateEffect(EffectNode effect, string at)
    {
        if (!EffectRegistry.IsKnown(effect.Op))
        {
            throw new CardValidationException(
                $"{at} uses unknown effect op '{effect.Op}'. Known ops: " +
                $"{string.Join(", ", EffectRegistry.KnownOpNames.OrderBy(n => n, StringComparer.Ordinal))}.");
        }

        // Parsing the selector is the check: EffectArgs.ParseSelector throws on an unknown
        // name, and doing it here means a typo like "opposeing" fails at load rather than the
        // first time that move is used.
        if (effect.Args.Has("target"))
        {
            try
            {
                effect.Args.Target();
            }
            catch (ArgumentException ex)
            {
                throw new CardValidationException($"{at} effect '{effect.Op}': {ex.Message}");
            }
        }

        // Amounts are counts of damage, healing, cards, or resources -- never negative. A
        // negative heal would silently be a damage effect that no reader of the card expects.
        if (effect.Args.Has("amount") && effect.Args.Int("amount") < 0)
        {
            throw new CardValidationException(
                $"{at} effect '{effect.Op}' has a negative amount ({effect.Args.Int("amount")}).");
        }

        if (effect.Args.Has("condition"))
        {
            foreach (var condition in effect.Args.NodesOrSingle("condition"))
            {
                ValidateCondition(condition, $"{at} effect '{effect.Op}' condition");
            }
        }
    }

    // Counts chosen_* selectors across the card's entire effect tree, moves included.
    //
    // The single-target rule (see PLAN.md): at most one player-chosen target per card. This is
    // a schema error rather than a convention precisely so it cannot creep back in during
    // Phase 4 balance edits -- it keeps MCTS branching flat (N actions per move, not N x M),
    // cards readable, and the Phase 5 targeting UI a single state.
    //
    // The count is per CARD, not per move: a move is the unit a player activates, but a card
    // with two differently-targeted moves would still present the same chained-prompt problem
    // to the UI, and no referenced card needs it.
    private static void ValidateSingleTargetRule(CardDefinition card)
    {
        var all = card.Effects.Concat(card.Moves.SelectMany(m => m.Effects)).ToList();

        var chosen = all
            .SelectMany(EffectTree.WalkIncludingConditions)
            .Where(e => e.Args.Has("target"))
            .Select(e => e.Args.String("target"))
            .Where(raw => EffectArgs.ParseSelector(raw).IsChosen());

        // Several effects sharing ONE selector is fine -- "damage chosen_enemy 1, then stun
        // chosen_enemy" is a single decision resolved once by legal-action generation, which
        // is why this counts distinct selectors rather than occurrences.
        var distinct = chosen.Distinct(StringComparer.Ordinal).ToList();

        if (distinct.Count > 1)
        {
            throw new CardValidationException(
                $"declares more than one chosen target ({string.Join(", ", distinct.OrderBy(s => s, StringComparer.Ordinal))}). " +
                "A card may require at most one player-chosen target.");
        }
    }

}
