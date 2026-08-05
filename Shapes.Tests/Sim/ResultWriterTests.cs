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

    [Fact]
    public void Every_offer_dictionary_key_is_a_type_json_can_write()
    {
        // Regression: MoveOffers* was first keyed by the same (CardId, MoveName) tuple that
        // MovesUsed* uses, which System.Text.Json refuses as an object key -- so --json threw at
        // write time while everything else about the run looked fine. Keyed on MoveKey's
        // delimited string instead. Named explicitly because the natural "just use the tuple,
        // it's the same identity" edit reintroduces it.
        var result = SmallBatch();
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteJson(path, result, MetricsReport.From(result.AllGames));
            var text = File.ReadAllText(path);

            Assert.Contains("\"MoveOffersOne\"", text, StringComparison.Ordinal);
            Assert.Contains("\"CardOffersOne\"", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Metrics_json_carries_provenance_when_the_run_supplies_it()
    {
        // A balance/ directory of anonymous reports cannot be diffed -- provenance is what makes
        // step 4.4's "edit JSON, rerun, compare" loop possible at all.
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
            ResultWriter.WriteMetricsJson(path, MetricsReport.From(result.AllGames, provenance));
            var text = File.ReadAllText(path);

            Assert.Contains("\"Provenance\"", text, StringComparison.Ordinal);
            Assert.Contains("\"CardSetHash\": \"deadbeef\"", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
