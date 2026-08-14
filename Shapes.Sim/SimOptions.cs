namespace Shapes.Sim;

// Which decks a batch plays with. Every game is played with a deck; this chooses which.
public enum DeckSource
{
    // One copy of every card in the set, both seats. The console's deck, and the mode every
    // pre-deck balance run is comparable to -- see Program.cs's note on why this stays the
    // default.
    Default = 0,

    // An explicit decklist loaded from a file, both seats.
    Custom = 1,

    // A freshly generated constrained-random deck PER SEAT PER GAME, so a batch samples the
    // space of legal decks rather than measuring one of them.
    Random = 2,
}

// CLI options for the batch runner. Deliberately narrower than Shapes.Console's ConsoleOptions --
// there is no human seat and no rendering, but there is a matrix of agent pairings and a game
// count, neither of which the console needs.
public sealed record SimOptions
{
    public IReadOnlyList<string> Agents { get; private init; } = ["random", "greedy", "ismcts"];

    public int Games { get; private init; } = 100;

    public ulong Seed { get; private init; } = 1;

    public int Iterations { get; private init; } = 200;

    public int? MaxDegreeOfParallelism { get; private init; }

    public string? OutputCsv { get; private init; }

    public string? OutputJson { get; private init; }

    public string? MetricsJson { get; private init; }

    // When set, Program.cs skips playing games entirely and reads a previously-written
    // --metrics-json file instead -- turning a saved report back into --report/--cards-csv/
    // --moves-csv without re-running the batch that produced it (which may have taken minutes and
    // whose exact agent config may not even be reproducible from memory anymore).
    public string? FromMetricsJson { get; private init; }

    // Compare mode: read two previously-written --metrics-json files (baseline first, candidate
    // second) and write a standalone diff report -- no games played. Mutually exclusive with
    // every other mode; set together with CompareReport.
    public string? CompareBaseline { get; private init; }

    public string? CompareCandidate { get; private init; }

    public string? CompareReport { get; private init; }

    public string? Report { get; private init; }

    public string? CardsCsv { get; private init; }

    public string? MovesCsv { get; private init; }

    // Adds Shapes.Content/cards-calibration's six deliberately mispriced spells (PLAN.md step
    // 4.2e) to the loaded card set, on top of the real ~36. Never combine with a report meant to
    // stand in for a real balance run -- the point is a known-wrong answer to check the
    // detectors against, not a ranking of real cards.
    public bool Calibration { get; private init; }

    // DECKS. Default (one of each) unless asked otherwise -- every balance number in
    // balance/LOG.md was measured against it, so changing the default would silently make new
    // runs incomparable to every recorded one.
    public DeckSource Deck { get; private init; } = DeckSource.Default;

    // Path to a decklist file, required by and only meaningful for --deck custom.
    public string? DeckPath { get; private init; }

    // Constrained-random generation knobs (--deck random). Defaults match
    // DeckBuilder.RandomDeckConstraints: within +/-0.2 of the default deck's mean cost, 10+
    // creatures of each type.
    public double DeckCostTolerance { get; private init; } = 0.2;

    public int DeckMinPerType { get; private init; } = 10;

    public bool ShowHelp { get; private init; }

    public static SimOptions Parse(string[] args)
    {
        var options = new SimOptions();
        var agents = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--agents":
                    agents.AddRange(RequireValue(args, ref i, "--agents").Split(
                        ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--games":
                    options = options with { Games = ParseInt(args, ref i, "--games") };
                    break;
                case "--seed":
                    options = options with { Seed = ParseULong(args, ref i, "--seed") };
                    break;
                case "--iterations":
                    options = options with { Iterations = ParseInt(args, ref i, "--iterations") };
                    break;
                case "--parallelism":
                    options = options with
                    {
                        MaxDegreeOfParallelism = ParseInt(args, ref i, "--parallelism"),
                    };
                    break;
                case "--csv":
                    options = options with { OutputCsv = RequireValue(args, ref i, "--csv") };
                    break;
                case "--json":
                    options = options with { OutputJson = RequireValue(args, ref i, "--json") };
                    break;
                case "--metrics-json":
                    options = options with
                    {
                        MetricsJson = RequireValue(args, ref i, "--metrics-json"),
                    };
                    break;
                case "--from-metrics-json":
                    options = options with
                    {
                        FromMetricsJson = RequireValue(args, ref i, "--from-metrics-json"),
                    };
                    break;
                case "--compare":
                    var pair = RequireValue(args, ref i, "--compare").Split(
                        ',', StringSplitOptions.TrimEntries);
                    if (pair.Length != 2 || pair[0].Length == 0 || pair[1].Length == 0)
                    {
                        throw new ArgumentException(
                            "--compare expects BASELINE.json,CANDIDATE.json.");
                    }

                    options = options with { CompareBaseline = pair[0], CompareCandidate = pair[1] };
                    break;
                case "--compare-report":
                    options = options with
                    {
                        CompareReport = RequireValue(args, ref i, "--compare-report"),
                    };
                    break;
                case "--report":
                    options = options with { Report = RequireValue(args, ref i, "--report") };
                    break;
                case "--cards-csv":
                    options = options with { CardsCsv = RequireValue(args, ref i, "--cards-csv") };
                    break;
                case "--moves-csv":
                    options = options with { MovesCsv = RequireValue(args, ref i, "--moves-csv") };
                    break;
                case "--calibration":
                    options = options with { Calibration = true };
                    break;
                case "--deck":
                    options = options with { Deck = ParseDeckSource(args, ref i) };
                    break;
                case "--deck-file":
                    options = options with { DeckPath = RequireValue(args, ref i, "--deck-file") };
                    break;
                case "--deck-cost-tolerance":
                    options = options with
                    {
                        DeckCostTolerance = ParseDouble(args, ref i, "--deck-cost-tolerance"),
                    };
                    break;
                case "--deck-min-per-type":
                    options = options with
                    {
                        DeckMinPerType = ParseInt(args, ref i, "--deck-min-per-type"),
                    };
                    break;
                case "--help" or "-h":
                    options = options with { ShowHelp = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        if (agents.Count > 0)
        {
            options = options with { Agents = agents };
        }

        if (options.CompareBaseline is not null)
        {
            if (options.CompareReport is null)
            {
                throw new ArgumentException("--compare requires --compare-report PATH.html.");
            }
        }
        else if (options.FromMetricsJson is null && options.Games <= 0)
        {
            throw new ArgumentException("--games must be positive.");
        }

        // A custom deck needs a file to read, and a file is meaningless without --deck custom.
        // Both directions are checked: silently ignoring a --deck-file the user passed would run
        // a whole batch against the wrong deck and report it as if nothing were wrong.
        if (options.Deck == DeckSource.Custom && options.DeckPath is null)
        {
            throw new ArgumentException("--deck custom requires --deck-file PATH.");
        }

        if (options.Deck != DeckSource.Custom && options.DeckPath is not null)
        {
            throw new ArgumentException("--deck-file is only valid with --deck custom.");
        }

        if (options.DeckCostTolerance < 0)
        {
            throw new ArgumentException("--deck-cost-tolerance must not be negative.");
        }

        if (options.DeckMinPerType < 0)
        {
            throw new ArgumentException("--deck-min-per-type must not be negative.");
        }

        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project Shapes.Sim -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --agents a,b,c     Agent pool to round-robin pair (default: random,greedy,ismcts)");
        Console.WriteLine("  --games N          Games per pairing per seat assignment (default: 100)");
        Console.WriteLine("  --seed N           Base seed; each game gets a distinct derived seed (default: 1)");
        Console.WriteLine("  --iterations N     IS-MCTS search budget in iterations (default: 200)");
        Console.WriteLine("  --parallelism N    Max concurrent games (default: Environment.ProcessorCount)");
        Console.WriteLine("  --csv PATH         Write per-pairing summary rows to a CSV file");
        Console.WriteLine("  --json PATH        Write the full result set (summary + per-game rows) as JSON");
        Console.WriteLine("  --metrics-json PATH  Write just the aggregated metrics report (PLAN.md step 4.1) as JSON");
        Console.WriteLine("  --from-metrics-json PATH  Skip playing games; read a saved metrics report instead");
        Console.WriteLine("                     (combine with --report/--cards-csv/--moves-csv to re-derive them)");
        Console.WriteLine("  --compare BASE.json,CANDIDATE.json  Skip playing games; diff two saved metrics reports");
        Console.WriteLine("  --compare-report PATH.html  Write the standalone A/B diff report (required with --compare)");
        Console.WriteLine("  --report PATH.html Write a self-contained metrics explorer (PLAN.md step 4.2d)");
        Console.WriteLine("  --cards-csv PATH   Write per-card metrics as a flat CSV");
        Console.WriteLine("  --moves-csv PATH   Write per-move metrics as a flat CSV");
        Console.WriteLine("  --calibration      Add the 6 deliberately mispriced calibration spells (PLAN.md step 4.2e)");
        Console.WriteLine("  --deck MODE        default (one of each card, the default), custom, or random");
        Console.WriteLine("  --deck-file PATH   Decklist to play; required with --deck custom.");
        Console.WriteLine("                     One 'cardId count' or 'cardId' per line; # comments allowed");
        Console.WriteLine("  --deck-cost-tolerance N  --deck random: max mean-cost gap from the default deck (default: 0.2)");
        Console.WriteLine("  --deck-min-per-type N    --deck random: min creatures of each type (default: 10)");
        Console.WriteLine("  --help             Show this message");
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{flag} requires a value.");
        }

        return args[++i];
    }

    private static DeckSource ParseDeckSource(string[] args, ref int i)
    {
        var value = RequireValue(args, ref i, "--deck");
        return value.ToLowerInvariant() switch
        {
            "default" => DeckSource.Default,
            "custom" => DeckSource.Custom,
            "random" => DeckSource.Random,
            _ => throw new ArgumentException(
                $"--deck expects default, custom, or random, got '{value}'."),
        };
    }

    private static double ParseDouble(string[] args, ref int i, string flag)
    {
        var value = RequireValue(args, ref i, flag);
        if (!double.TryParse(
                value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{flag} expects a number, got '{value}'.");
        }

        return parsed;
    }

    private static int ParseInt(string[] args, ref int i, string flag)
    {
        var value = RequireValue(args, ref i, flag);
        if (!int.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"{flag} expects an integer, got '{value}'.");
        }

        return parsed;
    }

    private static ulong ParseULong(string[] args, ref int i, string flag)
    {
        var value = RequireValue(args, ref i, flag);
        if (!ulong.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"{flag} expects a non-negative integer, got '{value}'.");
        }

        return parsed;
    }
}
