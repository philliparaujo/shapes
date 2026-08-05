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
    result = BatchRunner.Run(options, cards, rules);
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

if (options.OutputCsv is { } csvPath)
{
    ResultWriter.WriteCsv(csvPath, result);
    Console.WriteLine($"Wrote pairing summary to {csvPath}");
}

if (options.OutputJson is { } jsonPath)
{
    ResultWriter.WriteJson(jsonPath, result);
    Console.WriteLine($"Wrote full results to {jsonPath}");
}

return 0;
