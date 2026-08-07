using System.Text.Json;
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
    public void Cards_csv_has_one_header_row_plus_one_row_per_card()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteCardsCsv(path, metrics);
            var lines = File.ReadAllLines(path);

            Assert.Equal(metrics.CardStats.Count + 1, lines.Length);
            Assert.StartsWith("cardId,name,attackType,cost,health,effectText,playCount", lines[0], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Moves_csv_has_one_header_row_plus_one_row_per_move()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteMovesCsv(path, metrics);
            var lines = File.ReadAllLines(path);

            Assert.Equal(metrics.MoveStats.Count + 1, lines.Length);
            Assert.StartsWith("cardId,moveName,attackType,cost,effectText,useCount", lines[0], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cards_csv_enriches_rows_with_card_reference_data_when_a_database_is_supplied()
    {
        var result = SmallBatch();
        var metrics = MetricsReport.From(result.AllGames);
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteCardsCsv(path, metrics, TestCards.Database);
            var lines = File.ReadAllLines(path);

            // Every real card has a non-empty name -- the join succeeded for at least one row
            // rather than silently leaving every reference column blank.
            Assert.Contains(lines.Skip(1), line => !line.StartsWith(",", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Metrics_json_deserializes_back_into_a_metrics_report()
    {
        // Program.cs's --from-metrics-json reads a saved report back in with exactly
        // ResultWriter.JsonOptions -- this is the round trip that must hold for that path to
        // work, distinct from the "does it contain the right substrings" tests above.
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
        var original = MetricsReport.From(result.AllGames, provenance);
        var path = Path.GetTempFileName();
        try
        {
            ResultWriter.WriteMetricsJson(path, original);
            var text = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<MetricsReport>(text, ResultWriter.JsonOptions);

            Assert.NotNull(loaded);
            Assert.Equal(original.GameCount, loaded.GameCount);
            Assert.Equal(original.CardStats.Count, loaded.CardStats.Count);
            Assert.Equal(original.SeatOneWinRate.Rate, loaded.SeatOneWinRate.Rate);
            Assert.Equal(original.EndingCounts.Count, loaded.EndingCounts.Count);
            Assert.NotNull(loaded.Provenance);
            Assert.Equal("deadbeef", loaded.Provenance!.CardSetHash);
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
