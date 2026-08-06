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
            Assert.Contains("\"resourcesWinners\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesLosers\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesSeatOne\"", text, StringComparison.Ordinal);
            Assert.Contains("\"resourcesSeatTwo\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"metrics-data\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"margin-chart\"", text, StringComparison.Ordinal);
            Assert.Contains("id=\"resource-table\"", text, StringComparison.Ordinal);

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

            // Exactly two <script> blocks are expected: the inlined JSON data and the page logic.
            // A raw "</script>" leaking out of the JSON payload would produce a third.
            var closingTagCount = System.Text.RegularExpressions.Regex.Matches(
                text, "</script>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            Assert.Equal(2, closingTagCount);
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
