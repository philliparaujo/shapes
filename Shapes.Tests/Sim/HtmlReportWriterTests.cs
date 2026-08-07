using Shapes.Core.Rules;
using Shapes.Sim;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Sim;

public class HtmlReportWriterTests
{
    private static BatchResult SmallBatch()
    {
        var options = SimOptions.Parse(
            ["--agents", "random,greedy", "--games", "2", "--seed", "9", "--iterations", "10"]);
        return BatchRunner.Run(options, TestCards.Database, RuleSet.Default);
    }

    [Fact]
    public void Writes_a_self_contained_html_page_with_inlined_metrics()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            HtmlReportWriter.Write(path, metrics);
            var text = File.ReadAllText(path);

            Assert.StartsWith("<!doctype html>", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"cardStats\"", text, StringComparison.Ordinal);
            Assert.Contains("\"moveStats\"", text, StringComparison.Ordinal);
            Assert.Contains("\"scoreMarginByTurn\"", text, StringComparison.Ordinal);
            Assert.Contains("\"handSizeByTurnOne\"", text, StringComparison.Ordinal);
            Assert.Contains("\"handSizeByTurnTwo\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesWinners\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesLosers\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesSeatOne\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesSeatTwo\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesByTurnOne\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesByTurnTwo\"", text, StringComparison.Ordinal);
            Assert.Contains("\"slotsOccupiedByTurnOne\"", text, StringComparison.Ordinal);
            Assert.Contains("\"slotsOccupiedByTurnTwo\"", text, StringComparison.Ordinal);
            Assert.Contains("\"combinedHealthByTurnOne\"", text, StringComparison.Ordinal);
            Assert.Contains("\"combinedHealthByTurnTwo\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"metrics-data\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"margin-chart\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"hand-chart\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"resource-charts\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"resource-table\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"board-presence-charts\"", text, StringComparison.Ordinal);

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
    public void Inlined_json_does_not_break_out_of_its_script_tag()
    {
        // A card id or move name containing "</script>" (or JSON's own '<'/'>'/'&') would end the
        // data block early if the encoder didn't escape it -- this is the property JavaScriptEncoder
        // .Default exists to guarantee, checked directly rather than trusted.
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            HtmlReportWriter.Write(path, metrics);
            var text = File.ReadAllText(path);

            // Exactly four <script> blocks are expected: metrics data, card info, move info, and
            // the page logic. A raw "</script>" leaking out of any JSON payload would add a fifth.
            var closingTagCount = System.Text.RegularExpressions.Regex.Matches(
                text, "</script>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            Assert.Equal(4, closingTagCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Report_inlines_card_and_move_reference_data_when_a_database_is_supplied()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            HtmlReportWriter.Write(path, metrics, TestCards.Database);
            var text = File.ReadAllText(path);

            Assert.Contains("id=\"card-info-data\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"move-info-data\"", text, StringComparison.Ordinal);

            // Exactly four script blocks now: metrics data, card info, move info, page logic.
            var closingTagCount = System.Text.RegularExpressions.Regex.Matches(
                text, "</script>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            Assert.Equal(4, closingTagCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Report_computes_a_composite_power_score_column()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            HtmlReportWriter.Write(path, metrics, TestCards.Database);
            var text = File.ReadAllText(path);

            Assert.Contains("Power score", text, StringComparison.Ordinal);
            Assert.Contains("computeCompositeScores", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Report_carries_provenance_when_the_run_supplies_it()
    {
        var result = SmallBatch();
        var provenance = new RunProvenance
        {
            Agents = ["random", "greedy"],
            GamesPerPairing = 2,
            BaseSeed = 9,
            Iterations = 10,
            RuleSetName = RuleSet.Default.Name,
            CardSetHash = "deadbeef",
            CardCount = TestCards.Database.Count,
            RunAtUtc = DateTimeOffset.UtcNow,
        };

        var path = Path.GetTempFileName();
        try
        {
            HtmlReportWriter.Write(path, MetricsReport.From(result.AllGames, provenance));
            var text = File.ReadAllText(path);

            Assert.Contains("\"cardSetHash\":\"deadbeef\"", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
