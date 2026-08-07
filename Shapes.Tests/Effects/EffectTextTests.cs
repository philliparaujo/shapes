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
    public void Plain_damage_names_amount_and_target()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
            [Eff.Node("damage", ("target", "opposing"), ("amount", 1))]);

        Assert.Contains("1", text, StringComparison.Ordinal);
        Assert.Contains("opposing", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_effect_list_describes_as_empty()
    {
        Assert.Equal(string.Empty, Shapes.Core.Effects.EffectText.Describe([]));
    }

    [Fact]
    public void Multiple_effects_are_joined()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node("self_damage", ("amount", 1)),
            Eff.Node("buff_max_health", ("target", "right_friendly"), ("amount", 3)),
        ]);

        Assert.Contains("self-damage", text, StringComparison.Ordinal);
        Assert.Contains("right friendly", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Scaled_damage_names_the_scale_basis()
    {
        var text = Shapes.Core.Effects.EffectText.Describe(
        [
            Eff.Node("damage_scaled", ("target", "opposing"), ("scale", "count"), ("multiplier", 2)),
        ]);

        Assert.Contains("friendly creature count", text, StringComparison.Ordinal);
        Assert.Contains("x2", text, StringComparison.Ordinal);
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

        Assert.Contains("full health", text, StringComparison.Ordinal);
        Assert.Contains("draw", text, StringComparison.Ordinal);
        Assert.Contains("else", text, StringComparison.Ordinal);
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

        Assert.Contains("full health", text, StringComparison.Ordinal);
        Assert.Contains("deal 1 damage to opposing", text, StringComparison.Ordinal);
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

        Assert.Equal("some_future_op", text);
    }
}
