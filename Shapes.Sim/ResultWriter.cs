using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shapes.Sim;

// Emits the batch result as CSV (one row per pairing, for a quick spreadsheet look) and/or JSON
// (the full per-game detail, for later automated analysis). Neither format existed elsewhere in
// the repo before this -- there was nothing to reuse.
public static class ResultWriter
{
    public static void WriteCsv(string path, BatchResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "agentOne,agentTwo,games,agentOneWins,agentOneWinRate,avgTurns,avgActions");

        foreach (var pairing in result.Pairings)
        {
            sb.Append(CsvField(pairing.AgentOne)).Append(',')
              .Append(CsvField(pairing.AgentTwo)).Append(',')
              .Append(pairing.GameCount.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(pairing.AgentOneWins.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(pairing.AgentOneWinRate.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
              .Append(pairing.AverageTurnCount.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(pairing.AverageActionCount.ToString("F2", CultureInfo.InvariantCulture))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    public static void WriteJson(string path, BatchResult result, MetricsReport metrics)
    {
        var json = JsonSerializer.Serialize(new { result.Pairings, result.AllGames, Metrics = metrics }, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static void WriteMetricsJson(string path, MetricsReport metrics)
    {
        var json = JsonSerializer.Serialize(metrics, JsonOptions);
        File.WriteAllText(path, json);
    }

    // The flat escape hatch for step 2d's explorer -- questions the HTML page did not anticipate
    // are still a spreadsheet away. One row per card/move, opportunity denominators and interval
    // bounds included (not just the point rate) so a spreadsheet formula can recompute margin or
    // re-filter by sample size without round-tripping back through the JSON.
    public static void WriteCardsCsv(string path, MetricsReport metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "cardId,playCount,gamesPlayedIn,offerCount,playTakeRate,playTakeRateLow,playTakeRateHigh,"
            + "winRateWhenPlayed,timesDrawn,gamesDrawnIn,winRateWhenDrawn,blockedByCostCount,"
            + "costPressure,survivalStepsMean,survivalStepsCount,scoredWhileAliveRate");

        foreach (var card in metrics.CardStats)
        {
            sb.Append(CsvField(card.CardId)).Append(',')
              .Append(F(card.PlayCount)).Append(',')
              .Append(F(card.GamesPlayedIn)).Append(',')
              .Append(F(card.OfferCount)).Append(',')
              .Append(F(card.PlayTakeRate.Rate)).Append(',')
              .Append(F(card.PlayTakeRate.Low)).Append(',')
              .Append(F(card.PlayTakeRate.High)).Append(',')
              .Append(F(card.WinRateWhenPlayed.Rate)).Append(',')
              .Append(F(card.TimesDrawn)).Append(',')
              .Append(F(card.GamesDrawnIn)).Append(',')
              .Append(F(card.WinRateWhenDrawn.Rate)).Append(',')
              .Append(F(card.BlockedByCostCount)).Append(',')
              .Append(F(card.CostPressure.Rate)).Append(',')
              .Append(F(card.SurvivalSteps.Mean)).Append(',')
              .Append(F(card.SurvivalSteps.Count)).Append(',')
              .Append(F(card.ScoredWhileAliveRate.Rate))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    public static void WriteMovesCsv(string path, MetricsReport metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "cardId,moveName,useCount,gamesUsedIn,offerCount,useTakeRate,useTakeRateLow,"
            + "useTakeRateHigh,winRateWhenUsed");

        foreach (var move in metrics.MoveStats)
        {
            sb.Append(CsvField(move.CardId)).Append(',')
              .Append(CsvField(move.MoveName)).Append(',')
              .Append(F(move.UseCount)).Append(',')
              .Append(F(move.GamesUsedIn)).Append(',')
              .Append(F(move.OfferCount)).Append(',')
              .Append(F(move.UseTakeRate.Rate)).Append(',')
              .Append(F(move.UseTakeRate.Low)).Append(',')
              .Append(F(move.UseTakeRate.High)).Append(',')
              .Append(F(move.WinRateWhenUsed.Rate))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string F(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    private static string CsvField(string value) =>
        value.Contains(',', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    // Public, not private: --from-metrics-json (Program.cs) deserializes a previously written
    // --metrics-json file and must use the exact options it was serialized with (the enum
    // converter in particular -- without it, EndingType values round-trip as numbers instead of
    // the names the file was written with).
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
