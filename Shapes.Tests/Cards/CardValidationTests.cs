using Shapes.Core.Cards;
using Shapes.Core.Effects;

namespace Shapes.Tests.Cards;

// The card schema's guard rails. A typo in card data must fail at load, not become a silent
// gameplay bug -- an op that quietly does nothing would let a whole Phase 3 balance run
// complete while measuring something nobody intended.
public class CardValidationTests
{
    private static CardLoadException Rejects(string json) =>
        Assert.Throws<CardLoadException>(() => CardLoader.FromJson(json));

    [Fact]
    public void An_unknown_op_fails_loudly_at_load()
    {
        var ex = Rejects("""
            { "id": "warp", "name": "Warp", "kind": "spell",
              "effects": [ { "op": "teleport", "target": "self" } ] }
            """);

        Assert.Contains("teleport", ex.Message);
    }

    [Fact]
    public void An_unknown_op_error_lists_the_ops_that_do_exist()
    {
        // The message is the whole point of failing at load: an author who mistyped needs to
        // see the real vocabulary, not just that theirs was wrong.
        var ex = Rejects("""
            { "id": "warp", "name": "Warp", "kind": "spell", "effects": [ { "op": "damge", "amount": 1 } ] }
            """);

        Assert.Contains("damage", ex.Message);
        Assert.Contains("heal", ex.Message);
    }

    [Fact]
    public void The_known_op_check_reads_the_effect_registry()
    {
        // Validation must not keep its own list of op names: two lists that are supposed to
        // agree eventually disagree, and the failure mode is a card that loads but cannot run.
        // Every registered op is therefore accepted by the validator by construction.
        foreach (var op in EffectRegistry.KnownOpNames)
        {
            Assert.True(EffectRegistry.IsKnown(op));
        }
    }

    [Fact]
    public void An_unknown_op_inside_a_conditional_branch_is_still_caught()
    {
        // Validation walks the whole effect tree. A card could otherwise hide a broken op in
        // an else branch and load clean, failing only when that branch first ran.
        var ex = Rejects("""
            { "id": "sneaky", "name": "Sneaky", "kind": "spell",
              "effects": [
                { "op": "conditional",
                  "condition": { "op": "self_at_full_health" },
                  "then": [ { "op": "draw", "amount": 1 } ],
                  "else": [ { "op": "teleport" } ] }
              ] }
            """);

        Assert.Contains("teleport", ex.Message);
    }

    [Fact]
    public void An_unknown_op_inside_a_for_each_is_still_caught()
    {
        var ex = Rejects("""
            { "id": "looper", "name": "Looper", "kind": "spell",
              "effects": [
                { "op": "for_each", "collection": "all_creatures",
                  "effects": [ { "op": "teleport" } ] }
              ] }
            """);

        Assert.Contains("teleport", ex.Message);
    }

    [Fact]
    public void An_unknown_target_selector_fails_at_load()
    {
        var ex = Rejects("""
            { "id": "squint", "name": "Squint", "kind": "spell",
              "effects": [ { "op": "damage", "target": "opposeing", "amount": 1 } ] }
            """);

        Assert.Contains("opposeing", ex.Message);
    }

    [Fact]
    public void A_negative_amount_fails_at_load()
    {
        // A negative heal is a damage effect no reader of the card would expect.
        var ex = Rejects("""
            { "id": "drain", "name": "Drain", "kind": "spell",
              "effects": [ { "op": "heal", "target": "all_friendlies", "amount": -2 } ] }
            """);

        Assert.Contains("-2", ex.Message);
    }

    // --- The single-target rule ------------------------------------------------------------
    //
    // At most one player-chosen target per card. This is a schema error rather than a
    // convention so it cannot creep back in during Phase 3 balance edits: it is what keeps
    // MCTS branching at N actions per move rather than N x M, and the Phase 4 targeting UI a
    // single state.

    [Fact]
    public void Two_different_chosen_selectors_on_one_move_are_rejected()
    {
        var ex = Rejects("""
            { "id": "greedy", "name": "Greedy", "kind": "creature",
              "cost": { "spike": 1 }, "health": 2, "types": ["spike"],
              "moves": [ { "name": "Trade", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "chosen_enemy", "amount": 2 },
                                        { "op": "heal", "target": "chosen_friendly", "amount": 1 } ] } ] }
            """);

        Assert.Contains("chosen_enemy", ex.Message);
        Assert.Contains("chosen_friendly", ex.Message);
    }

    [Fact]
    public void Two_chosen_selectors_split_across_different_moves_are_rejected()
    {
        // The count is per card, not per move: a card with two differently-targeted moves
        // presents the same chained-prompt problem to the UI.
        var ex = Rejects("""
            { "id": "twofaced", "name": "Two-Faced", "kind": "creature",
              "cost": { "spike": 1 }, "health": 2, "types": ["spike"],
              "moves": [ { "name": "Strike", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "chosen_enemy", "amount": 1 } ] },
                         { "name": "Mend", "cost": { "spike": 1 },
                           "effects": [ { "op": "heal", "target": "chosen_friendly", "amount": 1 } ] } ] }
            """);

        Assert.Contains("chosen", ex.Message);
    }

    [Fact]
    public void A_chosen_selector_hidden_in_a_conditional_branch_still_counts()
    {
        var ex = Rejects("""
            { "id": "hidden", "name": "Hidden", "kind": "spell",
              "effects": [
                { "op": "damage", "target": "chosen_enemy", "amount": 1 },
                { "op": "conditional",
                  "condition": { "op": "self_at_full_health" },
                  "then": [ { "op": "heal", "target": "chosen_friendly", "amount": 1 } ] }
              ] }
            """);

        Assert.Contains("chosen_friendly", ex.Message);
    }

    [Fact]
    public void Repeating_the_same_chosen_selector_is_allowed()
    {
        // One decision, resolved once, applied by several effects -- "damage then stun the
        // same target" is a single prompt and must stay legal.
        var card = CardLoader.FromJson("""
            { "id": "focus", "name": "Focus", "kind": "spell",
              "effects": [ { "op": "damage", "target": "chosen_enemy", "amount": 2 },
                           { "op": "stun", "target": "chosen_enemy" } ] }
            """).Single();

        Assert.Equal(2, card.Effects.Count);
    }

    [Fact]
    public void One_chosen_selector_may_combine_freely_with_automatic_ones()
    {
        // The restriction is on player choices only. self/opposing/all_* are automatic.
        var card = CardLoader.FromJson("""
            { "id": "mixed", "name": "Mixed", "kind": "creature",
              "cost": { "spike": 1 }, "health": 2, "types": ["spike"],
              "moves": [ { "name": "Volley", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "chosen_enemy", "amount": 2 },
                                        { "op": "heal", "target": "self", "amount": 1 },
                                        { "op": "damage", "target": "all_enemies", "amount": 1 } ] } ] }
            """).Single();

        Assert.Single(card.Moves);
    }

    // --- Move cost typing ------------------------------------------------------------------

    [Fact]
    public void A_mixed_type_move_cost_is_rejected()
    {
        // A move's attacking type comes from its cost, so a two-type cost has no single
        // answer. Rejecting is the alternative to inventing a tie-break rule nobody asked for.
        var ex = Rejects("""
            { "id": "confused", "name": "Confused", "kind": "creature",
              "cost": { "spike": 1 }, "health": 2, "types": ["spike"],
              "moves": [ { "name": "Muddle", "cost": { "spike": 1, "wheel": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ] }
            """);

        Assert.Contains("Muddle", ex.Message);
        Assert.Contains("single resource type", ex.Message);
    }

    [Fact]
    public void A_single_type_move_cost_of_more_than_one_pip_is_fine()
    {
        // "Single-type" is about which types appear, not how many pips of it.
        var card = CardLoader.FromJson("""
            { "id": "heavy", "name": "Heavy", "kind": "creature",
              "cost": { "anvil": 2 }, "health": 4, "types": ["anvil"],
              "moves": [ { "name": "Slam", "cost": { "anvil": 3 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 3 } ] } ] }
            """).Single();

        Assert.Equal(Core.Primitives.ResourceType.Anvil, card.Moves[0].AttackType);
    }

    // --- Card shape ------------------------------------------------------------------------

    [Fact]
    public void A_creature_without_health_is_rejected()
    {
        var ex = Rejects("""
            { "id": "ghost", "name": "Ghost", "kind": "creature", "types": ["spike"],
              "moves": [ { "name": "Poke", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ] }
            """);

        Assert.Contains("health", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_creature_without_a_type_is_rejected()
    {
        // Typing drives both income and defense; an untyped creature would generate nothing
        // and have an undefined damage profile.
        var ex = Rejects("""
            { "id": "blank", "name": "Blank", "kind": "creature", "health": 2,
              "moves": [ { "name": "Poke", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ] }
            """);

        Assert.Contains("type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_creature_with_top_level_effects_is_rejected()
    {
        // There are no passive or triggered effects: all creature damage comes from activated
        // moves. A top-level list would silently never run.
        var ex = Rejects("""
            { "id": "passive", "name": "Passive", "kind": "creature",
              "health": 2, "types": ["spike"],
              "effects": [ { "op": "damage", "target": "all_enemies", "amount": 1 } ] }
            """);

        Assert.Contains("passive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_spell_with_health_or_types_is_rejected()
    {
        // Spells never occupy a slot, so board stats on one mean the author expected
        // something that will not happen.
        Assert.Contains("health", Rejects("""
            { "id": "solid", "name": "Solid", "kind": "spell", "health": 3,
              "effects": [ { "op": "draw", "amount": 1 } ] }
            """).Message, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("types", Rejects("""
            { "id": "typed", "name": "Typed", "kind": "spell", "types": ["spike"],
              "effects": [ { "op": "draw", "amount": 1 } ] }
            """).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_spell_with_moves_is_rejected()
    {
        var ex = Rejects("""
            { "id": "active", "name": "Active", "kind": "spell",
              "effects": [ { "op": "draw", "amount": 1 } ],
              "moves": [ { "name": "Go", "cost": { "spike": 1 },
                           "effects": [ { "op": "draw", "amount": 1 } ] } ] }
            """);

        Assert.Contains("moves", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_spell_with_no_effects_is_rejected()
    {
        var ex = Rejects("""{ "id": "dud", "name": "Dud", "kind": "spell" }""");

        Assert.Contains("effect", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_move_with_no_effects_is_rejected()
    {
        var ex = Rejects("""
            { "id": "idle", "name": "Idle", "kind": "creature", "health": 2, "types": ["spike"],
              "moves": [ { "name": "Wait", "cost": { "spike": 1 }, "effects": [] } ] }
            """);

        Assert.Contains("Wait", ex.Message);
    }

    [Fact]
    public void A_condition_naming_an_effect_op_is_rejected()
    {
        // Conditions take a predicate, not an effect. Confusing the two is a likely authoring
        // mistake and would otherwise throw at play time inside ConditionEvaluator.
        var ex = Rejects("""
            { "id": "muddled", "name": "Muddled", "kind": "creature",
              "health": 2, "types": ["spike"],
              "moves": [ { "name": "Try", "cost": { "spike": 1 },
                           "condition": { "op": "draw" },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ] }
            """);

        Assert.Contains("predicate", ex.Message);
    }

    [Fact]
    public void A_creature_may_legitimately_have_no_moves()
    {
        // A pure body -- it still scores and pays income. Nothing in the rules requires a
        // creature to be able to act, so this must load.
        var card = CardLoader.FromJson("""
            { "id": "wall", "name": "Wall", "kind": "creature",
              "cost": { "anvil": 2 }, "health": 5, "types": ["anvil"] }
            """).Single();

        Assert.Empty(card.Moves);
        Assert.Equal(5, card.Health);
    }
}
