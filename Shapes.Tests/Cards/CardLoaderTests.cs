using Shapes.Core.Cards;
using Shapes.Core.Primitives;

namespace Shapes.Tests.Cards;

// Card JSON parsing: that a well-formed card becomes the CardDefinition the author meant, and
// that the effect tree handed to the interpreter matches what the JSON declared. The schema
// rules that reject bad cards live in CardValidationTests.
public class CardLoaderTests
{
    // A synthetic creature exercising every part of the schema at once: a cost, stats, a
    // typing, a conditional move, and a multi-effect move.
    //
    // Deliberately NOT one of the shipped cards, even though the structure mirrors what a real
    // one looks like. These tests assert exact parsed values, so pinning them to real content
    // would mean a Phase 3 rebalance breaks tests that are only about JSON parsing -- and the
    // shipped card would silently stop matching the copy asserted here. Real cards are covered
    // by ContentCardSetTests, which loads them from disk.
    private const string TestCreature = """
        {
          "id": "test_creature",
          "name": "Test Creature",
          "kind": "creature",
          "cost": { "wheel": 1 },
          "health": 2,
          "types": ["wheel"],
          "moves": [
            { "name": "Conditional Move",  "cost": { "wheel": 1 },
              "condition": { "op": "creature_state", "target": "self", "check": "full_health" },
              "effects": [ { "op": "draw", "amount": 1 } ] },
            { "name": "Two Effect Move", "cost": { "wheel": 1 },
              "effects": [ { "op": "damage", "target": "opposing", "amount": 1 },
                           { "op": "heal", "target": "self", "amount": 1 } ] }
          ]
        }
        """;

    private static CardDefinition Load(string json) => CardLoader.FromJson(json).Single();

    [Fact]
    public void A_creature_loads_with_every_field_intact()
    {
        var card = Load(TestCreature);

        Assert.Equal("test_creature", card.Id);
        Assert.Equal("Test Creature", card.Name);
        Assert.Equal(CardKind.Creature, card.Kind);
        Assert.Equal(new ResourcePool(0, 0, 1), card.Cost);
        Assert.Equal(2, card.Health);
        Assert.Equal(TypeMask.Wheel, card.Types);
        Assert.Equal(2, card.Moves.Count);
    }

    [Fact]
    public void Move_effects_are_parsed_in_declared_order()
    {
        // Order is a contract: a multi-effect move applies its effects in sequence, and
        // "damage then heal" is a different card from "heal then damage".
        var move = Load(TestCreature).Moves[1];

        Assert.Equal("damage", move.Effects[0].Op);
        Assert.Equal("heal", move.Effects[1].Op);
        Assert.Equal(1, move.Effects[0].Args.Int("amount"));
        Assert.Equal("opposing", move.Effects[0].Args.String("target"));
    }

    [Fact]
    public void Move_condition_is_parsed_as_a_predicate_node()
    {
        var conditional = Load(TestCreature).Moves[0];

        Assert.NotNull(conditional.Condition);
        Assert.Equal("creature_state", conditional.Condition!.Op);

        // The unconditional move keeps a null condition rather than a vacuously true one, so
        // legal-action generation can skip the evaluation entirely.
        Assert.Null(Load(TestCreature).Moves[1].Condition);
    }

    [Fact]
    public void Move_attack_type_is_derived_from_its_cost()
    {
        // The attacking type is not a declared field -- it comes from what the move costs.
        // A wheel-cost move attacks as Wheel, which is what makes it 2x against Anvil.
        Assert.Equal(ResourceType.Wheel, Load(TestCreature).Moves[1].AttackType);
    }

    [Fact]
    public void A_free_move_has_no_attack_type_and_so_deals_typeless_damage()
    {
        var card = Load("""
            {
              "id": "free", "name": "Free", "kind": "creature",
              "cost": { "spike": 1 }, "health": 1, "types": ["spike"],
              "moves": [ { "name": "Nudge", "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ]
            }
            """);

        Assert.Null(card.Moves[0].AttackType);
        Assert.Equal(ResourcePool.Empty, card.Moves[0].Cost);
    }

    [Fact]
    public void Multi_type_creatures_load_every_declared_type()
    {
        // Cards can ship pre-merged typings; merging is not the only source of multi-type.
        var card = Load("""
            {
              "id": "hybrid", "name": "Hybrid", "kind": "creature",
              "cost": { "spike": 1, "wheel": 1 }, "health": 3, "types": ["spike", "wheel"],
              "moves": [ { "name": "Hit", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ]
            }
            """);

        Assert.Equal(TypeMask.Of(ResourceType.Spike, ResourceType.Wheel), card.Types);
        Assert.Equal(2, card.Types.Count);
    }

    [Fact]
    public void A_creatures_cost_must_match_its_declared_types()
    {
        // A creature's defensive type comes from its play cost, so the two must agree -- a
        // three-type cost with a single declared type is exactly the drift CardValidator now
        // rejects at load, rather than trusting an author to keep them in sync by hand.
        var ex = Assert.Throws<CardLoadException>(() => Load("""
            {
              "id": "hybrid", "name": "Hybrid", "kind": "creature",
              "cost": { "spike": 2, "anvil": 1, "wheel": 3 }, "health": 3, "types": ["spike"],
              "moves": [ { "name": "Hit", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ]
            }
            """));

        Assert.Contains("must match the resource type(s) in its cost", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Spells_load_with_a_top_level_effect_list_and_no_board_stats()
    {
        var card = Load("""
            {
              "id": "siphon", "name": "Siphon", "kind": "spell",
              "cost": { "anvil": 2 },
              "effects": [ { "op": "damage", "target": "all_enemies", "amount": 1 } ]
            }
            """);

        Assert.Equal(CardKind.Spell, card.Kind);
        Assert.False(card.IsCreature);
        Assert.Single(card.Effects);
        Assert.Empty(card.Moves);
        Assert.Equal(0, card.Health);
        Assert.True(card.Types.IsEmpty);
    }

    [Fact]
    public void Name_falls_back_to_the_id_when_omitted()
    {
        // Synthetic and token cards rarely want a display name; a real card always has one.
        var card = Load("""
            {
              "id": "token_spike", "kind": "creature", "cost": { "spike": 1 }, "health": 1,
              "types": ["spike"],
              "moves": [ { "name": "Poke", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ]
            }
            """);

        Assert.Equal("token_spike", card.Name);
    }

    [Fact]
    public void An_omitted_cost_is_free_rather_than_an_error()
    {
        var card = Load("""
            {
              "id": "freebie", "name": "Freebie", "kind": "spell",
              "effects": [ { "op": "draw", "amount": 1 } ]
            }
            """);

        Assert.Equal(ResourcePool.Empty, card.Cost);
    }

    [Fact]
    public void A_json_array_loads_several_cards_from_one_file()
    {
        // Both file shapes are supported so content can be grouped by theme or split per card.
        var cards = CardLoader.FromJson("""
            [
              { "id": "a", "name": "A", "kind": "spell", "effects": [ { "op": "draw", "amount": 1 } ] },
              { "id": "b", "name": "B", "kind": "spell", "effects": [ { "op": "draw", "amount": 2 } ] }
            ]
            """);

        Assert.Equal(2, cards.Count);
        Assert.Equal(["a", "b"], cards.Select(c => c.Id));
    }

    [Fact]
    public void Nested_control_flow_effects_are_parsed_into_a_tree()
    {
        // conditional/for_each carry effect lists inside their arguments. If these arrive as
        // raw JSON rather than EffectNodes, the interpreter fails at play time -- and card
        // validation cannot see inside them either.
        var card = Load("""
            {
              "id": "thinker", "name": "Thinker", "kind": "spell",
              "effects": [
                { "op": "conditional",
                  "condition": { "op": "creature_state", "target": "self", "check": "full_health" },
                  "then": [ { "op": "draw", "amount": 2 } ],
                  "else": [ { "op": "damage", "target": "all_enemies", "amount": 1 } ] }
              ]
            }
            """);

        var conditional = card.Effects[0];
        Assert.Equal("conditional", conditional.Op);
        Assert.Equal("creature_state", conditional.Args.Node("condition").Op);
        Assert.Equal("draw", conditional.Args.Nodes("then").Single().Op);
        Assert.Equal("damage", conditional.Args.Nodes("else").Single().Op);
    }

    [Fact]
    public void For_each_effect_lists_are_parsed_as_nodes()
    {
        var card = Load("""
            {
              "id": "sweeper", "name": "Sweeper", "kind": "spell",
              "effects": [
                { "op": "for_each", "collection": "enemy_creatures", "filter": "damaged",
                  "effects": [ { "op": "damage", "target": "all_enemies", "amount": 1 } ] }
              ]
            }
            """);

        var forEach = card.Effects[0];
        Assert.Equal("enemy_creatures", forEach.Args.String("collection"));
        Assert.Equal("damaged", forEach.Args.String("filter"));
        Assert.Equal("damage", forEach.Args.Nodes("effects").Single().Op);
    }

    [Fact]
    public void A_string_array_argument_is_joined_rather_than_read_as_an_effect_list()
    {
        // summon's "types" is an array of strings, not of effects. Telling the two apart by
        // element kind is what lets one converter handle both.
        var card = Load("""
            {
              "id": "summoner", "name": "Summoner", "kind": "spell",
              "effects": [
                { "op": "summon", "target": "all_friendlies", "card_id": "token",
                  "health": 1, "types": ["spike", "wheel"] }
              ]
            }
            """);

        Assert.Equal("spike,wheel", card.Effects[0].Args.String("types"));
    }

    [Fact]
    public void Resource_and_kind_names_are_case_insensitive()
    {
        var card = Load("""
            {
              "id": "shout", "name": "Shout", "kind": "CREATURE",
              "cost": { "spike": 1 }, "health": 1, "types": ["Spike"],
              "moves": [ { "name": "Yell", "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ]
            }
            """);

        Assert.Equal(CardKind.Creature, card.Kind);
        Assert.Equal(TypeMask.Spike, card.Types);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        // Card files are hand-edited throughout Phase 3 balance work, exactly like rulesets.
        var card = Load("""
            {
              // a test card
              "id": "commented", "name": "Commented", "kind": "spell",
              "effects": [ { "op": "draw", "amount": 1 }, ],
            }
            """);

        Assert.Equal("commented", card.Id);
    }

    [Fact]
    public void Malformed_json_is_rejected_with_the_source_named()
    {
        var ex = Assert.Throws<CardLoadException>(
            () => CardLoader.FromJson("{ not json", "broken.json"));

        Assert.Contains("broken.json", ex.Message);
    }

    [Fact]
    public void An_unknown_property_is_rejected()
    {
        // "helth: 3" would otherwise produce a 0-health creature that fails far from the typo.
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "id": "typo", "name": "Typo", "kind": "creature", "helth": 3, "types": ["spike"] }
            """));

        Assert.Contains("helth", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_id_is_reported_clearly()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "name": "Nameless", "kind": "spell", "effects": [ { "op": "draw", "amount": 1 } ] }
            """));

        Assert.Contains("id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_kind_is_reported_clearly()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "id": "shapeless", "name": "Shapeless", "health": 1, "types": ["spike"] }
            """));

        Assert.Contains("kind", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_kind_is_rejected()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "id": "artifact", "name": "Artifact", "kind": "enchantment" }
            """));

        Assert.Contains("enchantment", ex.Message);
    }

    [Fact]
    public void An_unknown_resource_name_is_rejected()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "id": "odd", "name": "Odd", "kind": "creature", "health": 1, "types": ["sphere"] }
            """));

        Assert.Contains("sphere", ex.Message);
    }

    [Fact]
    public void A_negative_cost_is_rejected()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "id": "refund", "name": "Refund", "kind": "spell", "cost": { "spike": -1 },
              "effects": [ { "op": "draw", "amount": 1 } ] }
            """));

        Assert.Contains("refund", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_move_without_a_name_is_rejected()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "id": "mute", "name": "Mute", "kind": "creature", "health": 1, "types": ["spike"],
              "moves": [ { "cost": { "spike": 1 },
                           "effects": [ { "op": "damage", "target": "opposing", "amount": 1 } ] } ] }
            """));

        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_effect_without_an_op_is_rejected()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            { "id": "empty", "name": "Empty", "kind": "spell",
              "effects": [ { "amount": 1 } ] }
            """));

        Assert.Contains("op", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_file_is_reported_clearly()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromFile("does-not-exist.json"));

        Assert.Contains("does-not-exist.json", ex.Message);
    }

    [Fact]
    public void A_missing_directory_is_reported_clearly()
    {
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromDirectory("no-such-dir"));

        Assert.Contains("no-such-dir", ex.Message);
    }

    [Fact]
    public void The_failing_card_is_named_even_when_it_is_one_of_many()
    {
        // A directory load reports which card is broken, not merely that something is.
        var ex = Assert.Throws<CardLoadException>(() => CardLoader.FromJson("""
            [
              { "id": "fine", "name": "Fine", "kind": "spell", "effects": [ { "op": "draw", "amount": 1 } ] },
              { "id": "broken", "name": "Broken", "kind": "spell", "effects": [ { "op": "teleport" } ] }
            ]
            """, "set.json"));

        Assert.Contains("broken", ex.Message);
        Assert.Contains("set.json", ex.Message);
    }
}
