using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Effects;

// Step 4 of Phase 4: effect text synthesized from the op vocabulary, since no card carries
// hand-authored description text (see EffectText's header). Not exhaustive over every op --
// that would just restate the switch -- but covers one of each shape (plain, scaled, nested,
// control-flow) so a rewrite of the synthesizer has to keep each shape working, not just the
// ops a specific card happens to use.
public class EffectTextTests
{
    [Fact]
    public void Plain_damage_against_the_default_target_names_only_the_amount()
    {
        // "opposing" is the assumed target of a damage op, so naming it would be noise on every
        // attack in the game -- see EffectText's header on the default-target convention.
        var text = Shapes.Core.Effects.EffectText.Describe(
            [Eff.Node("damage", ("target", "opposing"), ("amount", 1))]);

        Assert.Equal("Deal 1.", text);
    }

    [Fact]
    public void Plain_damage_against_a_non_default_target_names_it()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
            [Eff.Node("damage", ("target", "all_enemies"), ("amount", 1))]);

        Assert.Equal("Deal 1 to all enemies.", text);
    }

    [Fact]
    public void Empty_effect_list_describes_as_empty()
    {
        Assert.Equal(string.Empty, Shapes.Core.Effects.EffectText.Describe([]));
    }

    [Fact]
    public void All_creatures_target_renders_distinctly_from_all_enemies()
    {
        // A missing selector case degrades silently -- the console would render a symmetric
        // sweep as if it only hit the opponent, which is exactly the misreading this avoids.
        var text = Shapes.Core.Effects.EffectText.Describe(
            [Eff.Node("damage", ("target", "all_creatures"), ("amount", 2))]);

        Assert.Contains("all creatures", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Scaled_attack_buff_names_its_scale_basis()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
            [Eff.Node("attack_buff_scaled", ("target", "self"), ("scale", "missing_health"))]);

        Assert.Contains("attack", text, StringComparison.Ordinal);
        Assert.Contains("missing health", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_effects_are_joined()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node("self_damage", ("amount", 1)),
            Eff.Node("buff_max_health", ("target", "right_friendly"), ("amount", 3)),
        ]);

        // self_damage shares the "deal N to X" sentence rather than having its own vocabulary,
        // and a buff on someone else reads from THEIR side ("... gains ...").
        Assert.Equal("Deal 1 to this. Your right friendly gains +3 max health.", text);
    }

    [Fact]
    public void Count_scaled_damage_folds_the_multiplier_into_a_per_creature_amount()
    {
        // The one scaled op that is RESTRUCTURED rather than reworded: the multiplier is the
        // per-creature damage, so it reads as an amount instead of a trailing "x2" on a count.
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "count"), ("multiplier", 2)),
        ]);

        Assert.Equal("Deal 2 damage for each friendly creature.", text);
    }

    [Fact]
    public void Scaled_damage_states_its_basis_as_prose_not_a_parenthetical()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "health"), ("multiplier", 2)),
        ]);

        Assert.Equal("Deal damage equal to twice the health this has.", text);
    }

    [Fact]
    public void A_divisor_reads_as_half_rather_than_as_a_division()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "health"), ("divisor", 2)),
        ]);

        Assert.Equal("Deal damage equal to half the health this has.", text);
    }

    [Fact]
    public void Grant_ricochet_names_the_direction()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node("grant_keyword", ("target", "self"), ("keyword", "ricochet"), ("direction", "left")),
        ]);

        Assert.Contains("ricochet", text, StringComparison.Ordinal);
        Assert.Contains("left", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Conditional_describes_both_branches()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node(
                "conditional",
                ("condition", Eff.Node("creature_state", ("target", "self"), ("check", "full_health"))),
                ("then", new[] { Eff.Node("draw", ("amount", 1)) }),
                ("else", new[] { Eff.Node("damage", ("target", "self"), ("amount", 1)) })),
        ]);

        // "it", not "this", in the else branch: the condition already named the creature, so the
        // branch refers back to it rather than re-identifying it.
        Assert.Equal("Draw 1 if this is at full health. Otherwise deal 1 to it.", text);
    }

    [Fact]
    public void Conditional_branches_refer_back_to_the_creature_the_condition_named()
    {
        // Execute: spelling "an enemy" out in the condition AND both branches reads as three
        // different creatures rather than one.
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node(
                "conditional",
                ("condition", Eff.Node("creature_state", ("target", "chosen_enemy"), ("check", "damaged"))),
                ("then", new[] { Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 4)) }),
                ("else", new[] { Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 2)) })),
        ]);

        Assert.Equal("Deal 4 to it if an enemy is damaged. Otherwise deal 2 to it.", text);
    }

    [Fact]
    public void Move_text_states_the_gate_a_move_level_condition_imposes()
    {
        // Circle Priest's Focus Strike: "deal 1 damage to opposing" alone reads as a complete and
        // badly overpriced 1-cost move. The condition is what makes it a real card, and it lives
        // on the move rather than in the effect list, so Describe(effects) cannot see it.
        var text = Shapes.Core.Effects.EffectText.DescribeMove(
            Eff.Node("creature_state", ("target", "self"), ("check", "full_health")),
            [Eff.Node("damage", ("target", "opposing"), ("amount", 1))]);

        // Trailing clause, not a leading "only if" -- see DescribeMove on why the gate reads as
        // part of the rule rather than as a disclaimer in front of it.
        Assert.Equal("Deal 1 if this is at full health.", text);
    }

    [Fact]
    public void Move_text_without_a_condition_is_unchanged()
    {
        var effects = new[] { Eff.Node("damage", ("target", "opposing"), ("amount", 2)) };

        Assert.Equal(
            Shapes.Core.Effects.EffectText.Describe(effects),
            Shapes.Core.Effects.EffectText.DescribeMove(null, effects));
    }

    [Fact]
    public void Unknown_op_falls_back_to_its_raw_name_rather_than_throwing()
    {
        // A new op landing without a synthesizer case should degrade to something readable, not
        // crash the console mid-game -- the same "fail loud at load time, not at play time"
        // spirit as the rest of the engine, but this is a DISPLAY path, not a load-time check.
        var text = Shapes.Core.Effects.EffectText.Describe([Eff.Node("some_future_op")]);

        Assert.Equal("Some_future_op.", text);
    }

    [Fact]
    public void Resources_render_through_the_supplied_formatter()
    {
        // The hook Godot uses to swap bracketed text for a real inline icon (InlineResourceIcons)
        // without Shapes.Core gaining any knowledge of how a resource is DRAWN.
        var text = Shapes.Core.Effects.EffectText.Describe(
            [Eff.Node("gain_resource", ("type", "anvil"), ("amount", 3))],
            type => $"<{type}>");

        Assert.Equal("Gain 3 <Anvil>.", text);
    }

    [Fact]
    public void Resources_default_to_bracketed_names_when_no_formatter_is_given()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
            [Eff.Node("gain_resource", ("type", "anvil"), ("amount", 3))]);

        Assert.Equal("Gain 3 [anvil].", text);
    }
}
