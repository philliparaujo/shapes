using Shapes.Core.Rules;
using Shapes.Sim;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Sim;

public class ResultWriterTests
{
    private static BatchResult SmallBatch()
    {
        var options = SimOptions.Parse(
            ["--agents", "random,greedy", "--games", "2", "--seed", "9", "--iterations", "10"]);
        return BatchRunner.Run(options, TestCards.Database, RuleSet.Default);
    }

    [Fact]
    public void Csv_has_one_header_row_plus_one_row_per_pairing()
    {
        var result = SmallBatch();
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteCsv(path, result);
            var lines = File.ReadAllLines(path);

            Assert.Equal(result.Pairings.Count + 1, lines.Length);
            Assert.StartsWith("agentOne,agentTwo,games", lines[0], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Json_round_trips_the_game_count()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteJson(path, result, metrics);
            var text = File.ReadAllText(path);

            Assert.Contains("\"AllGames\"", text, StringComparison.Ordinal);
            Assert.Contains("\"Pairings\"", text, StringComparison.Ordinal);
            Assert.Contains("\"Metrics\"", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Metrics_json_round_trips_seat_win_rate()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteMetricsJson(path, metrics);
            var text = File.ReadAllText(path);

            Assert.Contains("\"SeatOneWinRate\"", text, StringComparison.Ordinal);
            Assert.Contains("\"CardStats\"", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
