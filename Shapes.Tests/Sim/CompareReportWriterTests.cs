using Shapes.Core.Rules;
using Shapes.Sim;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Sim;

public class CompareReportWriterTests
{
    private static BatchResult SmallBatch(ulong seed) =>
        BatchRunner.Run(
            SimOptions.Parse(["--agents", "random,greedy", "--games", "2", "--seed", seed.ToString(), "--iterations", "10"]),
            TestCards.Database, RuleSet.Default);

    [Fact]
    public void Writes_a_self_contained_html_page_with_both_reports_inlined()
    {
        var baseline = MetricsReport.From(SmallBatch(1).AllGames);
        var candidate = MetricsReport.From(SmallBatch(2).AllGames);
        var path = Path.GetTempFileName();
        try
        {
            CompareReportWriter.Write(path, baseline, candidate);
            var text = File.ReadAllText(path);

            Assert.StartsWith("<!doctype html>", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"baseline\"", text, StringComparison.Ordinal);
            Assert.Contains("\"candidate\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"compare-data\"", text, StringComparison.Ordinal);

            // No external references -- no CDN script/link, no server needed to view it.
            Assert.DoesNotContain("http://", text, StringComparison.Ordinal);
            Assert.DoesNotContain("https://", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Report_computes_a_composite_power_score_delta_column_without_a_card_database()
    {
        // CompareReportWriter.Write takes no CardDatabase (the --compare CLI path reads two
        // --metrics-json files and never plays a game or loads Shapes.Content/cards/), so this
        // pins that the power score column still renders -- the creature/spell distinction it
        // needs is inferred from moveStats presence, not a CardDatabase lookup.
        var baseline = MetricsReport.From(SmallBatch(1).AllGames);
        var candidate = MetricsReport.From(SmallBatch(2).AllGames);
        var path = Path.GetTempFileName();
        try
        {
            CompareReportWriter.Write(path, baseline, candidate);
            var text = File.ReadAllText(path);

            Assert.Contains("computeCompositeScores", text, StringComparison.Ordinal);
            Assert.Contains("Δ power score", text, StringComparison.Ordinal);
            Assert.Contains("powerScoreDelta", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
