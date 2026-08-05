// Headless batch runner: N seeded games in parallel per agent pairing, emitting a win-rate/
// behaviour-count matrix. Implements PLAN.md Phase 3, step 1 -- everything after it (playout
// policy, tuning, performance work) is gated on this existing, since an unmeasured optimization
// is a guess.
//
// Usage:
//   dotnet run --project Shapes.Sim --                                     random,greedy,ismcts matrix, 100 games/pairing
//   dotnet run --project Shapes.Sim -- --agents greedy,ismcts --games 200  just those two, 200 games/pairing
//   dotnet run --project Shapes.Sim -- --csv out.csv --json out.json       write results to disk
//
// Every pairing is run with BOTH seat assignments and reported separately (agentOne/agentTwo
// columns), never pooled -- pooling hides first-player advantage, which is one of the things
// Phase 4 needs to watch for.

using Shapes.Core.Cards;
using Shapes.Core.Rules;
using Shapes.Sim;

SimOptions options;
try
{
    options = SimOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    SimOptions.PrintUsage();
    return 1;
}

if (options.ShowHelp)
{
    SimOptions.PrintUsage();
    return 0;
}

Console.WriteLine("Shapes — simulation runner");
Console.WriteLine();

var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
var cards = CardLoader.FromDirectory(cardsDir);
var rules = RuleSet.Default;

Console.WriteLine(
    $"Agents: {string.Join(", ", options.Agents)}    Games/pairing: {options.Games}    "
    + $"Seed: {options.Seed}    Iterations: {options.Iterations}");
Console.WriteLine();

BatchResult result;
try
{
    // Redrawn in place (\r, no newline) rather than one line per game -- a 30-games/pairing
    // matrix across a handful of agents is thousands of games, and printing each would flood the
    // terminal. Throttled to a fixed cadence, not every completion, since Parallel.ForEach can
    // finish games faster than the console can usefully redraw; still shows the LAST completion
    // exactly (guarded write below) so the line never gets stuck short of 100%. Only drawn to a
    // real console -- redirected output (CI logs, `> file`) skips it so the log isn't full of
    // carriage-return noise.
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var lastDrawnAt = TimeSpan.Zero;
    var progressLock = new object();

    void ReportProgress(int completedCount, int total)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        lock (progressLock)
        {
            var elapsed = sw.Elapsed;
            if (completedCount < total && elapsed - lastDrawnAt < TimeSpan.FromMilliseconds(200))
            {
                return;
            }

            lastDrawnAt = elapsed;
            var rate = completedCount / Math.Max(elapsed.TotalSeconds, 0.001);
            var line =
                $"  {completedCount}/{total} games ({(double)completedCount / total:P0})  "
                + $"{rate:F1} games/s  {elapsed.TotalSeconds:F0}s elapsed";
            Console.Write($"\r{line,-70}");

            if (completedCount == total)
            {
                Console.WriteLine();
            }
        }
    }

    result = BatchRunner.Run(options, cards, rules, ReportProgress);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

foreach (var pairing in result.Pairings)
{
    Console.WriteLine(
        $"{pairing.AgentOne,-8} vs {pairing.AgentTwo,-8}  "
        + $"P1 win rate {pairing.AgentOneWinRate,6:P1}  "
        + $"avg turns {pairing.AverageTurnCount,5:F1}  "
        + $"avg actions {pairing.AverageActionCount,5:F1}  "
        + $"({pairing.GameCount} games)");
}

Console.WriteLine();
Console.WriteLine(
    $"{result.AllGames.Count} games total in "
    + $"{result.AllGames.Sum(g => g.Elapsed.TotalSeconds):F1}s of playout time.");

var metrics = MetricsReport.From(result.AllGames);

Console.WriteLine();
Console.WriteLine("-- Metrics (PLAN.md Phase 4 step 1) --------------------------------------");
Console.WriteLine(
    $"Seat win rate      P1 {metrics.SeatOneWinRate,6:P1}   P2 {metrics.SeatTwoWinRate,6:P1}");
Console.WriteLine($"Avg game length    {metrics.AverageGameLength,6:F1} turns");
Console.WriteLine(
    $"Move usage         {metrics.MoveUsageCount} of {result.AllGames.Sum(g => g.ActionCount)} "
    + $"actions ({metrics.MoveUsageRate:P1})");
Console.WriteLine(
    $"Merges             {metrics.MergeCount} total, {metrics.MergesPerGame:F2}/game, "
    + $"{metrics.MergesPerCreaturePlayed:P1} of creatures played were merged into something");
Console.WriteLine(
    $"Unspent at end     spike {metrics.AverageUnspentSpike:F2}  anvil {metrics.AverageUnspentAnvil:F2}  "
    + $"wheel {metrics.AverageUnspentWheel:F2}");
Console.WriteLine(
    "Endings            " + string.Join(
        "  ", metrics.EndingCounts.Select(kv => $"{kv.Key}={kv.Value}")));

Console.WriteLine();
Console.WriteLine("Top played cards (plays in N games, win rate when played / when drawn):");
foreach (var card in metrics.CardStats.Take(10))
{
    Console.WriteLine(
        $"  {card.CardId,-20} plays={card.PlayCount,4}  games={card.GamesPlayedIn,4}  "
        + $"winRate(played)={card.WinRateWhenPlayed,6:P1}  winRate(drawn)={card.WinRateWhenDrawn,6:P1}");
}

Console.WriteLine();
Console.WriteLine("Top used moves (uses in N games, win rate when used):");
foreach (var move in metrics.MoveStats.Take(10))
{
    Console.WriteLine(
        $"  {move.MoveName,-18} ({move.CardId,-16}) uses={move.UseCount,4}  games={move.GamesUsedIn,4}  "
        + $"winRate={move.WinRateWhenUsed,6:P1}");
}

if (options.OutputCsv is { } csvPath)
{
    ResultWriter.WriteCsv(csvPath, result);
    Console.WriteLine();
    Console.WriteLine($"Wrote pairing summary to {csvPath}");
}

if (options.OutputJson is { } jsonPath)
{
    ResultWriter.WriteJson(jsonPath, result, metrics);
    Console.WriteLine($"Wrote full results to {jsonPath}");
}

if (options.MetricsJson is { } metricsPath)
{
    ResultWriter.WriteMetricsJson(metricsPath, metrics);
    Console.WriteLine($"Wrote metrics report to {metricsPath}");
}

return 0;
