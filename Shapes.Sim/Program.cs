// Headless batch runner: N seeded games in parallel per agent pairing, emitting a win-rate/
// behaviour-count matrix. Implements DESIGN.md Phase 3, step 1 -- everything after it (playout
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

// --calibration adds the six deliberately mispriced spells (DESIGN.md step 4.2e) on top of the
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

// Decks. Every game is played with one; --deck chooses which. Built before the run so a bad
// decklist file fails immediately rather than thousands of games in.
DeckProvider deckProvider;
try
{
    var customDeck = options.DeckPath is null
        ? null
        : DeckLoader.FromFile(options.DeckPath, cards, rules);

    deckProvider = new DeckProvider(
        options.Deck, cards, rules, customDeck,
        new DeckBuilder.RandomDeckConstraints
        {
            CostTolerance = options.DeckCostTolerance,
            MinPerType = options.DeckMinPerType,
        });
}
catch (Exception ex) when (ex is DeckBuildException or IOException)
{
    Console.Error.WriteLine($"Deck error: {ex.Message}");
    return 1;
}

Console.WriteLine(
    $"Agents: {string.Join(", ", options.Agents)}    Games/pairing: {options.Games}    "
    + $"Seed: {options.Seed}    Iterations: {options.Iterations}");
Console.WriteLine($"Decks: {deckProvider.Describe()}");

// The included-win-rate metric only varies when deck inclusion varies, and under the default
// one-of-each deck it never does -- every deck runs every card. Saying so up front beats having
// a reader discover a column of identical numbers and wonder which part is broken.
if (options.Deck == DeckSource.Default)
{
    Console.WriteLine(
        "       (one-of-each: every deck runs every card, so included win rate is uninformative "
        + "-- use --deck random to vary it)");
}

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

    result = BatchRunner.Run(options, cards, rules, ReportProgress, deckProvider);
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
Console.WriteLine("-- Metrics (DESIGN.md Phase 4 steps 1/3) -----------------------------------");
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

// The distribution, not just the mean: one non-terminating game moves the mean and standard
// deviation far more than the median, so a mean well above p50 is the signal that some games are
// not ending rather than that all games got longer (DESIGN.md step 5b).
Console.WriteLine($"                   {metrics.GameLengthDistribution}");

var fatigueOne = metrics.DeckExhaustionRateSeatOne;
var fatigueTwo = metrics.DeckExhaustionRateSeatTwo;
if (fatigueOne.Successes > 0 || fatigueTwo.Successes > 0)
{
    Console.WriteLine();
    Console.WriteLine("-- Fatigue (empty deck at turn start scores for the opponent) -------------");
    Console.WriteLine(
        $"Decked out         P1 {fatigueOne}   first at turn {metrics.FirstFatigueTurnSeatOne}");
    Console.WriteLine(
        $"                   P2 {fatigueTwo}   first at turn {metrics.FirstFatigueTurnSeatTwo}");
    Console.WriteLine(
        $"Score conceded     P1 {metrics.FatigueScoreConcededSeatOne}  "
        + $"P2 {metrics.FatigueScoreConcededSeatTwo}");

    // The watch item: fatigue is meant to be a backstop that rarely decides anything. A large
    // share here means the timer has become the win condition.
    Console.WriteLine($"Decided by fatigue {metrics.GamesDecidedByFatigue}");
}
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

// INCLUDED WIN RATE -- of the decks that ran a card, how often that seat won. Printed only when
// inclusion actually VARIED: under the default one-of-each deck every deck runs every card, so
// every row would carry the identical pooled seat win rate, and a table of 36 identical numbers
// invites the reader to find meaning that is not there. The check is on the data rather than on
// the --deck flag so a custom-deck batch that happens to vary gets the table too.
var included = metrics.CardStats.Where(c => c.DecksIncludedIn > 0).ToList();
if (included.Select(c => c.DecksIncludedIn).Distinct().Count() > 1)
{
    Console.WriteLine();
    Console.WriteLine(
        "Cards by included win rate — of the decks running the card, how often that seat won:");
    Console.WriteLine(
        "  (one deck = one trial regardless of copies; the 1x/2x/3x split is the copy-count trend)");

    static void PrintIncluded(CardStat card)
    {
        // Buckets are printed as bare rates with their deck counts, so a 3x rate resting on four
        // decks is visibly thin rather than looking like the 1x rate beside it.
        static string Bucket(CardStat c, int copies) =>
            c.ByCopyCount.TryGetValue(copies, out var b)
                ? $"{b.WinRate.Rate,5:P0}/{b.Decks,-4}"
                : "    -/-   ";

        Console.WriteLine(
            $"  {card.CardId,-18} incl={card.IncludedWinRate.Rate,6:P1} "
            + $"[{card.IncludedWinRate.Low,5:P0},{card.IncludedWinRate.High,5:P0}]  "
            + $"decks={card.DecksIncludedIn,5}   "
            + $"1x {Bucket(card, 1)}  2x {Bucket(card, 2)}  3x {Bucket(card, 3)}");
    }

    var byIncluded = included.OrderByDescending(c => c.IncludedWinRate.Rate).ToList();

    foreach (var card in byIncluded.Take(5))
    {
        PrintIncluded(card);
    }

    if (byIncluded.Count > 10)
    {
        Console.WriteLine($"  ... {byIncluded.Count - 10} more ...");
    }

    foreach (var card in byIncluded.Skip(Math.Max(5, byIncluded.Count - 5)))
    {
        PrintIncluded(card);
    }

    // Same honesty check the play-rate resolution line below makes, for this metric: an interval
    // straddling 50% cannot rank anything, and inclusion samples are thinner than play samples
    // (one per deck, not one per decision), so this is usually the tighter constraint on games.
    var undecidedIncluded = included.Count(c => !c.IncludedWinRate.Excludes());
    Console.WriteLine(
        $"  {undecidedIncluded} of {included.Count} cards have an included-win-rate interval "
        + "still straddling 50%.");
}

// DECK STATS -- win rate by the deck's own properties, asking "what kind of deck wins" rather
// than "which card wins". Empty under --deck default (every deck identical, nothing to group by),
// so the whole section disappears rather than printing single-bucket rows.
if (metrics.DeckStats.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Deck stats — win rate by deck composition (one deck played by one seat = one trial):");

    foreach (var stat in metrics.DeckStats)
    {
        // Buckets with no decks in them are printed as gaps rather than skipped: "no deck ran
        // 14-16 spike creatures" is information, and hiding it would make the surrounding buckets
        // look adjacent when they are not.
        var live = stat.Buckets.Where(b => b.Decks > 0).ToList();
        if (live.Count < 2)
        {
            continue;
        }

        // The separation flag is the honest headline: without it a monotone-looking climb is just
        // as likely to be noise, and this is the number that says which.
        var verdict = stat.HasSeparatedBuckets
            ? "buckets separate"
            : "no bucket separates — not distinguishable";
        Console.WriteLine();
        Console.WriteLine($"  {stat.Name}  (n={stat.TotalDecks} decks, {verdict})");

        foreach (var bucket in stat.Buckets)
        {
            if (bucket.Decks == 0)
            {
                Console.WriteLine($"    {bucket.Label(stat.Decimals),-14}      —  (no decks)");
                continue;
            }

            // A bar makes the shape readable at a glance; the interval beside it is what says
            // whether the shape means anything.
            // Width 7, not 6: "100.0 %" is seven characters and overflows a 6-wide field, which
            // misaligns exactly the rows a thin bucket produces.
            var bar = new string('#', (int)Math.Round(bucket.WinRate.Rate * 20));
            Console.WriteLine(
                $"    {bucket.Label(stat.Decimals),-14} {bucket.WinRate.Rate,7:P1} "
                + $"[{bucket.WinRate.Low,5:P0},{bucket.WinRate.High,5:P0}]  n={bucket.Decks,4}  {bar}");
        }
    }
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
