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

using System.Text.Json;
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

// --from-metrics-json skips playing games entirely: read a previously written --metrics-json
// file back in and jump straight to the output-writing block below, so a saved report can be
// turned into --report/--cards-csv/--moves-csv without re-running a batch that may have taken
// minutes (and whose exact seed/agent config may not even be reproducible from memory anymore).
if (options.FromMetricsJson is { } metricsInputPath)
{
    MetricsReport loadedMetrics;
    try
    {
        var text = File.ReadAllText(metricsInputPath);
        loadedMetrics = JsonSerializer.Deserialize<MetricsReport>(text, ResultWriter.JsonOptions)
            ?? throw new InvalidDataException($"'{metricsInputPath}' deserialized to null.");
    }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
    {
        Console.Error.WriteLine($"Failed to read '{metricsInputPath}': {ex.Message}");
        return 1;
    }

    Console.WriteLine($"Loaded metrics from {metricsInputPath} ({loadedMetrics.GameCount} games).");

    if (options.Report is { } loadedReportPath)
    {
        HtmlReportWriter.Write(loadedReportPath, loadedMetrics);
        Console.WriteLine($"Wrote metrics explorer to {loadedReportPath}");
    }

    if (options.CardsCsv is { } loadedCardsCsvPath)
    {
        ResultWriter.WriteCardsCsv(loadedCardsCsvPath, loadedMetrics);
        Console.WriteLine($"Wrote per-card CSV to {loadedCardsCsvPath}");
    }

    if (options.MovesCsv is { } loadedMovesCsvPath)
    {
        ResultWriter.WriteMovesCsv(loadedMovesCsvPath, loadedMetrics);
        Console.WriteLine($"Wrote per-move CSV to {loadedMovesCsvPath}");
    }

    if (options.MetricsJson is { } loadedMetricsJsonPath)
    {
        ResultWriter.WriteMetricsJson(loadedMetricsJsonPath, loadedMetrics);
        Console.WriteLine($"Wrote metrics report to {loadedMetricsJsonPath}");
    }

    return 0;
}

if (options.CompareBaseline is { } comparePathA && options.CompareCandidate is { } comparePathB)
{
    MetricsReport ReadMetrics(string path)
    {
        var text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MetricsReport>(text, ResultWriter.JsonOptions)
            ?? throw new InvalidDataException($"'{path}' deserialized to null.");
    }

    MetricsReport baselineMetrics, candidateMetrics;
    try
    {
        baselineMetrics = ReadMetrics(comparePathA);
        candidateMetrics = ReadMetrics(comparePathB);
    }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
    {
        Console.Error.WriteLine($"Failed to read metrics for comparison: {ex.Message}");
        return 1;
    }

    Console.WriteLine(
        $"Baseline:  {comparePathA} ({baselineMetrics.GameCount} games)");
    Console.WriteLine(
        $"Candidate: {comparePathB} ({candidateMetrics.GameCount} games)");

    CompareReportWriter.Write(options.CompareReport!, baselineMetrics, candidateMetrics);
    Console.WriteLine($"Wrote comparison report to {options.CompareReport}");

    return 0;
}

var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
var cards = CardLoader.FromDirectory(cardsDir);
var rules = RuleSet.Default;

// --calibration adds the six deliberately mispriced spells (PLAN.md step 4.2e) on top of the
// real set, so the metrics detectors can be checked against a known-wrong answer. Loaded from a
// separate directory rather than merged into cards\ on disk, so CardSetHash (which only hashes
// cards\) and BuildSymmetricDeck's card count are unaffected for every non-calibration run.
if (options.Calibration)
{
    var calibrationDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards-calibration");
    var calibrationCards = CardLoader.FromDirectory(calibrationDir);
    cards = new CardDatabase(cards.All.Concat(calibrationCards.All));
    Console.WriteLine($"Calibration: added {calibrationCards.Count} deliberately mispriced spells.");
}

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

var provenance = ProvenanceBuilder.Build(options, cards, rules, cardsDir);
var metrics = MetricsReport.From(result.AllGames, provenance);

Console.WriteLine();
Console.WriteLine("-- Metrics (PLAN.md Phase 4 steps 1/3) -----------------------------------");
Console.WriteLine($"Ruleset / cards    {provenance.RuleSetName}  cards={provenance.CardCount} "
    + $"hash={provenance.CardSetHash}");
Console.WriteLine(
    $"Seat win rate      P1 {metrics.SeatOneWinRate}");
Console.WriteLine(
    $"                   P2 {metrics.SeatTwoWinRate}");

// Margin, not just win rate: at realistic batch sizes the win-rate interval is usually too wide
// to answer "is first-player advantage real," while the margin interval often is not. Saying so
// inline keeps a reader from over-reading a lopsided-looking win rate on few games.
var marginVerdict = metrics.FinalScoreMargin.Excludes()
    ? "REAL seat advantage (interval excludes 0)"
    : "not distinguishable from even";
Console.WriteLine($"Score margin P1-P2 {metrics.FinalScoreMargin}  -> {marginVerdict}");
Console.WriteLine($"Decisiveness |m|   {metrics.AbsoluteScoreMargin}");
Console.WriteLine($"Game length        {metrics.GameLength} turns");
Console.WriteLine(
    $"Move usage         {metrics.MoveUsageCount} of {result.AllGames.Sum(g => g.ActionCount)} "
    + $"actions ({metrics.MoveUsageRate:P1})");
Console.WriteLine(
    $"Merges             {metrics.MergeCount} total, {metrics.MergesPerGame:F2}/game, "
    + $"{metrics.MergesPerCreaturePlayed:P1} of creatures played were merged into something");
Console.WriteLine($"Merge take rate    {metrics.MergeTakeRate} of decisions where a merge was legal");
Console.WriteLine(
    "Endings            " + string.Join(
        "  ", metrics.EndingCounts.Select(kv => $"{kv.Key}={kv.Value}")));

// The scoring rule's own denominator. A low rate means unopposed slots are hard to get and each
// is worth a lot (tune PointsPerUnopposedCreature); a high rate means they come easily and the
// points follow (tune board size, removal, or durability). Same fast game either way, opposite
// fixes -- which is why the raw score cannot choose between them.
Console.WriteLine();
Console.WriteLine("-- Scoring rule (+1 per unopposed creature) -------------------------------");
Console.WriteLine(
    $"Unopposed slots    {metrics.UnopposedSlotRate} of all (scoring step x slot) pairs");
Console.WriteLine(
    $"Per scoring step   {metrics.UnopposedCreaturesPerStep} unopposed creatures held per seat");
Console.WriteLine(
    $"Longest streak     {metrics.LongestUnopposedStreak} consecutive steps  "
    + $"({metrics.GamesWithNoSustainedUnopposed}/{metrics.GameCount} games had none sustained 2+)");

// Cost pressure against the resource profiles: high unspent WITH high pressure means players
// hold the wrong TYPES (a type-chart/cost-distribution problem); high unspent with low pressure
// means income simply exceeds what there is to buy (an income-level problem).
Console.WriteLine();
Console.WriteLine("-- Economy ---------------------------------------------------------------");
Console.WriteLine(
    $"Cost pressure      {metrics.CostPressure} of held-card decisions blocked only by cost");
Console.WriteLine(
    $"Resources/turn     winners  spike {metrics.ResourcesWinners.Spike.Mean,5:F2}  "
    + $"anvil {metrics.ResourcesWinners.Anvil.Mean,5:F2}  wheel {metrics.ResourcesWinners.Wheel.Mean,5:F2}");
Console.WriteLine(
    $"                   losers   spike {metrics.ResourcesLosers.Spike.Mean,5:F2}  "
    + $"anvil {metrics.ResourcesLosers.Anvil.Mean,5:F2}  wheel {metrics.ResourcesLosers.Wheel.Mean,5:F2}");
Console.WriteLine(
    $"Cards drawn/game   winners  {metrics.CardsDrawnWinners}");
Console.WriteLine(
    $"                   losers   {metrics.CardsDrawnLosers}");

var pricedOut = metrics.CardStats
    .Where(c => c.BlockedByCostCount > 0)
    .OrderByDescending(c => c.CostPressure.Rate)
    .Take(5)
    .ToList();

if (pricedOut.Count > 0)
{
    Console.WriteLine("Most priced-out cards (share of held-card decisions blocked by cost):");
    foreach (var card in pricedOut)
    {
        Console.WriteLine(
            $"  {card.CardId,-18} pressure={card.CostPressure.Rate,6:P1}  "
            + $"blocked={card.BlockedByCostCount,5}  offers={card.OfferCount,5}  "
            + $"take={card.PlayTakeRate.Rate,6:P1}");
    }
}

// Survival separates two problems that take rate reports identically: a creature played
// constantly that dies immediately, versus one that sticks. ScoredWhileAlive then separates a
// blocker (holds a contested lane) from a scorer (converts presence into points).
var survivors = metrics.CardStats
    .Where(c => c.SurvivalSteps.Count > 0)
    .OrderBy(c => c.SurvivalSteps.Mean)
    .ToList();

if (survivors.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Creature survival — scoring steps held before dying (shortest first):");
    foreach (var card in survivors.Take(5))
    {
        Console.WriteLine(
            $"  {card.CardId,-18} survived={card.SurvivalSteps.Mean,5:F2} steps  "
            + $"(n={card.SurvivalSteps.Count,3})  scored while alive="
            + $"{card.ScoredWhileAliveRate.Rate,6:P1}");
    }

    if (survivors.Count > 5)
    {
        Console.WriteLine("  ...");
        foreach (var card in survivors.Skip(Math.Max(5, survivors.Count - 3)))
        {
            Console.WriteLine(
                $"  {card.CardId,-18} survived={card.SurvivalSteps.Mean,5:F2} steps  "
                + $"(n={card.SurvivalSteps.Count,3})  scored while alive="
                + $"{card.ScoredWhileAliveRate.Rate,6:P1}");
        }
    }
}

// Sorted by take rate, not play count: play count ranks by how often a card showed up, which is
// mostly a statement about the shuffle. Take rate ranks by how often a strong agent chose it when
// it could have -- the actual balance question. Both extremes are printed because both are step
// 4.5 watch items (auto-include at the top, dead card at the bottom).
Console.WriteLine();
Console.WriteLine("Cards by take rate — chosen / times the play was legal (step 4.5 outliers):");
var byTakeRate = metrics.CardStats
    .Where(c => c.OfferCount > 0)
    .OrderByDescending(c => c.PlayTakeRate.Rate)
    .ToList();

static void PrintCard(CardStat card, IReadOnlyDictionary<string, CardInfo> info)
{
    var costCol = info.TryGetValue(card.CardId, out var i)
        ? $"cost={i.Cost,-7}"
        : "cost=?      ";
    Console.WriteLine(
        $"  {card.CardId,-18} {costCol} take={card.PlayTakeRate.Rate,6:P1} [{card.PlayTakeRate.Low,5:P0},{card.PlayTakeRate.High,5:P0}]  "
        + $"take/turn={card.PlayTakeRatePerTurn.Rate,6:P1}  "
        + $"offers={card.OfferCount,5}  plays={card.PlayCount,4}  "
        + $"win(played)={card.WinRateWhenPlayed.Rate,6:P1}±{card.WinRateWhenPlayed.Margin,5:P0}");
}

var cardInfoLookup = CardInfo.BuildLookup(cards);

foreach (var card in byTakeRate.Take(5))
{
    PrintCard(card, cardInfoLookup);
}

if (byTakeRate.Count > 10)
{
    Console.WriteLine($"  ... {byTakeRate.Count - 10} more ...");
}

foreach (var card in byTakeRate.Skip(Math.Max(5, byTakeRate.Count - 5)))
{
    PrintCard(card, cardInfoLookup);
}

var moveInfoLookup = MoveInfo.BuildLookup(cards);

Console.WriteLine();
Console.WriteLine("Moves by take rate — used / times the move was legal:");
foreach (var move in metrics.MoveStats
    .Where(m => m.OfferCount > 0)
    .OrderByDescending(m => m.UseTakeRate.Rate)
    .Take(10))
{
    moveInfoLookup.TryGetValue((move.CardId, move.MoveName), out var moveInfo);
    Console.WriteLine(
        $"  {move.MoveName,-18} ({move.CardId,-16}) take={move.UseTakeRate.Rate,6:P1}  "
        + $"take/turn={move.UseTakeRatePerTurn.Rate,6:P1}  "
        + $"offers={move.OfferCount,5}  uses={move.UseCount,4}  "
        + $"win={move.WinRateWhenUsed.Rate,6:P1}±{move.WinRateWhenUsed.Margin,5:P0}");
    if (moveInfo is { EffectText.Length: > 0 })
    {
        Console.WriteLine($"      {moveInfo.EffectText}");
    }
}

// A rate whose interval still straddles 0.5 after the whole batch cannot rank anything -- saying
// how many are in that state is the honest headline for a run, and the direct answer to "do I
// have enough games yet?"
var undecided = metrics.CardStats.Count(c => c.GamesPlayedIn > 0 && !c.WinRateWhenPlayed.Excludes());
Console.WriteLine();
Console.WriteLine(
    $"Resolution: {undecided} of {metrics.CardStats.Count} cards have a win-rate interval still "
    + $"straddling 50% — those cannot be ranked at {metrics.GameCount} games.");

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

if (options.Report is { } reportPath)
{
    HtmlReportWriter.Write(reportPath, metrics, cards);
    Console.WriteLine($"Wrote metrics explorer to {reportPath}");
}

if (options.CardsCsv is { } cardsCsvPath)
{
    ResultWriter.WriteCardsCsv(cardsCsvPath, metrics, cards);
    Console.WriteLine($"Wrote per-card CSV to {cardsCsvPath}");
}

if (options.MovesCsv is { } movesCsvPath)
{
    ResultWriter.WriteMovesCsv(movesCsvPath, metrics, cards);
    Console.WriteLine($"Wrote per-move CSV to {movesCsvPath}");
}

return 0;
