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

    public static void WriteJson(string path, BatchResult result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static string CsvField(string value) =>
        value.Contains(',', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
