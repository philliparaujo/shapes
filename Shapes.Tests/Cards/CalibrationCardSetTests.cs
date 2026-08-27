using Shapes.Core.Cards;

namespace Shapes.Tests.Cards;

// The six deliberately mispriced calibration spells (DESIGN.md Phase 4 step 2e), loaded exactly
// as --calibration loads them: from their own directory, separate from the real content set.
//
// These exist to check that the metrics detectors register a known-wrong card at all, before
// step 3's sweep relies on them. This suite only proves the cards load and are shaped as
// intended -- the actual calibration check is reading a --calibration run through the step 2d
// explorer.
public class CalibrationCardSetTests
{
    private static string CalibrationDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Content", "cards-calibration");

    private static CardDatabase Load() => CardLoader.FromDirectory(CalibrationDirectory);

    [Fact]
    public void The_calibration_directory_is_present_in_the_build_output()
    {
        Assert.True(
            Directory.Exists(CalibrationDirectory),
            $"Expected the calibration card directory at {CalibrationDirectory}. " +
            "Check the Shapes.Content copy pipeline.");
    }

    [Fact]
    public void All_six_calibration_spells_are_present()
    {
        var db = Load();

        Assert.Equal(6, db.Count);
    }

    [Fact]
    public void Every_calibration_spell_loads_without_error()
    {
        var db = Load();

        Assert.All(db.All, card => Assert.False(string.IsNullOrWhiteSpace(card.Id)));
    }

    [Theory]
    [InlineData("spike_op", "Spike OP")]
    [InlineData("spike_up", "Spike UP")]
    [InlineData("anvil_op", "Anvil OP")]
    [InlineData("anvil_up", "Anvil UP")]
    [InlineData("wheel_op", "Wheel OP")]
    [InlineData("wheel_up", "Wheel UP")]
    public void Each_calibration_spell_has_its_expected_id_and_name(string id, string name)
    {
        var db = Load();

        var card = db.Get(id);
        Assert.Equal(name, card.Name);
    }

    [Fact]
    public void Card_names_are_unique_and_distinguishable_by_text_alone()
    {
        // Every name must be tellable apart from every other by its text alone (not just its
        // id) -- a reader scanning the metrics explorer's card table sees names, not ids, so
        // "Spike OP" vs. "Spike UP" has to read unambiguously at a glance.
        var db = Load();

        var names = db.All.Select(c => c.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("spike_op")]
    [InlineData("anvil_op")]
    [InlineData("wheel_op")]
    public void Overpowered_calibration_spells_cost_1_and_are_resource_and_card_positive(string id)
    {
        var db = Load();
        var card = db.Get(id);

        Assert.Equal(1, card.Cost.Spike + card.Cost.Anvil + card.Cost.Wheel);
        Assert.Contains(card.Effects, e => e.Op == "gain_resource");
        Assert.Contains(card.Effects, e => e.Op == "draw");
    }

    [Theory]
    [InlineData("spike_up")]
    [InlineData("anvil_up")]
    [InlineData("wheel_up")]
    public void Underpowered_calibration_spells_cost_3_for_1_damage(string id)
    {
        var db = Load();
        var card = db.Get(id);

        Assert.Equal(3, card.Cost.Spike + card.Cost.Anvil + card.Cost.Wheel);
        Assert.Single(card.Effects);
        Assert.Equal("damage", card.Effects[0].Op);
        Assert.Equal(1, card.Effects[0].Args.Int("amount"));
    }

    [Fact]
    public void Calibration_cards_are_not_part_of_the_real_content_set()
    {
        // The whole point of the separate directory: BuildSymmetricDeck/CardSetHash for a real
        // run must never see these, or every real-run baseline silently shifts.
        var realCards = CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));
        var calibrationIds = Load().All.Select(c => c.Id);

        foreach (var id in calibrationIds)
        {
            Assert.False(realCards.Contains(id), $"Calibration card '{id}' must not also be in the real card set.");
        }

        Assert.Equal(48, realCards.Count);
    }
}
