using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Tests.Mechanics;

// Ruleset JSON loading. The important behaviours are that overrides actually take effect,
// that omitted keys fall back to the defaults, and that anything malformed fails loudly at
// load rather than silently producing a ruleset nobody intended.
public class RuleSetLoaderTests
{
    [Fact]
    public void Empty_object_yields_the_defaults()
    {
        var rules = RuleSetLoader.FromJson("{}");

        Assert.Equal(RuleSet.Default.ScoreToWin, rules.ScoreToWin);
        Assert.Equal(RuleSet.Default.StartingHandSize, rules.StartingHandSize);
        Assert.Equal(RuleSet.Default.BaseIncome, rules.BaseIncome);
    }

    [Fact]
    public void Scalar_overrides_take_effect()
    {
        var rules = RuleSetLoader.FromJson("""
            { "name": "fast", "scoreToWin": 5, "startingHandSize": 6, "handLimit": 10 }
            """);

        Assert.Equal("fast", rules.Name);
        Assert.Equal(5, rules.ScoreToWin);
        Assert.Equal(6, rules.StartingHandSize);
        Assert.Equal(10, rules.HandLimit);

        // Untouched fields keep their defaults.
        Assert.Equal(RuleSet.Default.CardsDrawnPerTurn, rules.CardsDrawnPerTurn);
    }

    [Fact]
    public void Hand_limit_accepts_the_unlimited_sentinel()
    {
        var rules = RuleSetLoader.FromJson("""{ "handLimit": "unlimited" }""");

        Assert.Equal(RuleSet.NoHandLimit, rules.HandLimit);
    }

    [Fact]
    public void Hand_limit_rejects_a_non_unlimited_string()
    {
        var ex = Assert.Throws<RuleSetLoadException>(
            () => RuleSetLoader.FromJson("""{ "handLimit": "lots" }"""));

        Assert.Contains("handLimit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_zero_is_honoured_rather_than_treated_as_missing()
    {
        // The DTO uses nullable fields precisely so "0" and "absent" are distinguishable.
        // Getting this wrong would silently ignore a deliberate Phase 4 sweep value.
        var rules = RuleSetLoader.FromJson("""{ "incomePerCreatureType": 0 }""");

        Assert.Equal(0, rules.IncomePerCreatureType);
    }

    [Fact]
    public void Base_income_is_read_per_type()
    {
        var rules = RuleSetLoader.FromJson("""
            { "baseIncome": { "spike": 3, "anvil": 0, "wheel": 2 } }
            """);

        Assert.Equal(new ResourcePool(3, 0, 2), rules.BaseIncome);
    }

    [Fact]
    public void Partial_base_income_falls_back_per_field()
    {
        var rules = RuleSetLoader.FromJson("""{ "baseIncome": { "spike": 5 } }""");

        Assert.Equal(5, rules.BaseIncome.Spike);
        Assert.Equal(RuleSet.Default.BaseIncome.Anvil, rules.BaseIncome.Anvil);
        Assert.Equal(RuleSet.Default.BaseIncome.Wheel, rules.BaseIncome.Wheel);
    }

    [Theory]
    [InlineData("symmetric", DeckMode.Symmetric)]
    [InlineData("custom", DeckMode.Custom)]
    [InlineData("SYMMETRIC", DeckMode.Symmetric)]
    public void Deck_mode_is_parsed_case_insensitively(string raw, DeckMode expected)
    {
        var json = expected == DeckMode.Custom
            ? $$"""{ "deckMode": "{{raw}}", "deckSize": 30, "maxCopiesPerCard": 2 }"""
            : $$"""{ "deckMode": "{{raw}}" }""";

        Assert.Equal(expected, RuleSetLoader.FromJson(json).DeckMode);
    }

    [Fact]
    public void Type_chart_overrides_take_effect()
    {
        var rules = RuleSetLoader.FromJson("""
            {
              "typeChart": {
                "weaknessMultiplier": 3.0,
                "cycle": { "spike": "anvil", "anvil": "wheel", "wheel": "spike" }
              }
            }
            """);

        Assert.Equal(3.0, rules.TypeChart.WeaknessMultiplier);

        // Cycle reversed relative to the default, so the multiplier lands on Anvil.
        Assert.Equal(3.0, rules.TypeChart.MultiplierAgainst(ResourceType.Spike, TypeMask.Anvil));
        Assert.Equal(1.0, rules.TypeChart.MultiplierAgainst(ResourceType.Spike, TypeMask.Wheel));
    }

    [Fact]
    public void Type_chart_multiplier_can_be_changed_without_restating_the_cycle()
    {
        var rules = RuleSetLoader.FromJson("""{ "typeChart": { "weaknessMultiplier": 1.0 } }""");

        Assert.Equal(1.0, rules.TypeChart.WeaknessMultiplier);
        Assert.Equal(ResourceType.Wheel, rules.TypeChart.Beats(ResourceType.Spike));
    }

    [Fact]
    public void Unknown_property_is_rejected()
    {
        // A typo like "scoreToWinn" would otherwise fall back to the default and a balance
        // run would measure the wrong ruleset while looking entirely plausible.
        var ex = Assert.Throws<RuleSetLoadException>(
            () => RuleSetLoader.FromJson("""{ "scoreToWinn": 5 }"""));

        Assert.Contains("scoreToWinn", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        Assert.Throws<RuleSetLoadException>(() => RuleSetLoader.FromJson("{ not json"));
    }

    [Fact]
    public void Unknown_deck_mode_is_rejected()
    {
        var ex = Assert.Throws<RuleSetLoadException>(
            () => RuleSetLoader.FromJson("""{ "deckMode": "freeform" }"""));

        Assert.Contains("freeform", ex.Message);
    }

    [Fact]
    public void Unknown_resource_name_in_the_cycle_is_rejected()
    {
        var ex = Assert.Throws<RuleSetLoadException>(() => RuleSetLoader.FromJson("""
            { "typeChart": { "cycle": { "sphere": "wheel", "wheel": "anvil", "anvil": "sphere" } } }
            """));

        Assert.Contains("sphere", ex.Message);
    }

    [Fact]
    public void Malformed_cycle_is_rejected_at_load()
    {
        Assert.Throws<RuleSetLoadException>(() => RuleSetLoader.FromJson("""
            { "typeChart": { "cycle": { "spike": "wheel", "anvil": "wheel", "wheel": "anvil" } } }
            """));
    }

    [Fact]
    public void Invalid_values_fail_at_load_with_the_source_named()
    {
        var ex = Assert.Throws<RuleSetLoadException>(
            () => RuleSetLoader.FromJson("""{ "scoreToWin": 0 }""", "broken.json"));

        Assert.Contains("broken.json", ex.Message);
    }

    [Fact]
    public void Missing_file_is_reported_clearly()
    {
        var ex = Assert.Throws<RuleSetLoadException>(
            () => RuleSetLoader.FromFile("does-not-exist.json"));

        Assert.Contains("does-not-exist.json", ex.Message);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        // Ruleset files are hand-edited during balance work; a stray trailing comma should
        // not cost anyone a debugging session.
        var rules = RuleSetLoader.FromJson("""
            {
              // a balance experiment
              "scoreToWin": 7,
            }
            """);

        Assert.Equal(7, rules.ScoreToWin);
    }

    [Fact]
    public void Shipped_default_json_matches_RuleSet_Default()
    {
        // RuleSet.Default and Shapes.Content/rulesets/default.json are two statements of the
        // same rules -- one for code, one for data. This is the test that keeps them honest.
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "default.json");
        Assert.True(File.Exists(path), $"Expected shipped ruleset at {path}.");

        var loaded = RuleSetLoader.FromFile(path);
        var d = RuleSet.Default;

        Assert.Equal(d.Name, loaded.Name);
        Assert.Equal(d.StartingHandSize, loaded.StartingHandSize);
        Assert.Equal(d.CardsDrawnPerTurn, loaded.CardsDrawnPerTurn);
        Assert.Equal(d.HandLimit, loaded.HandLimit);
        Assert.Equal(d.BaseIncome, loaded.BaseIncome);
        Assert.Equal(d.IncomePerCreatureType, loaded.IncomePerCreatureType);
        Assert.Equal(d.PointsPerUnopposedCreature, loaded.PointsPerUnopposedCreature);
        Assert.Equal(d.ScoreToWin, loaded.ScoreToWin);
        Assert.Equal(d.MergeEnabled, loaded.MergeEnabled);
        Assert.Equal(d.MergeRequiresAdjacent, loaded.MergeRequiresAdjacent);
        Assert.Equal(d.MergeCostsAction, loaded.MergeCostsAction);
        Assert.Equal(d.MaxMergeDepth, loaded.MaxMergeDepth);
        Assert.Equal(d.DeckMode, loaded.DeckMode);
        Assert.Equal(d.CopiesPerCard, loaded.CopiesPerCard);
        Assert.Equal(d.TypeChart.WeaknessMultiplier, loaded.TypeChart.WeaknessMultiplier);

        foreach (var t in ResourceTypes.All)
        {
            Assert.Equal(d.TypeChart.Beats(t), loaded.TypeChart.Beats(t));
        }
    }
}
