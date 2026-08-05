namespace Shapes.Sim;

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

        if (options.Games <= 0)
        {
            throw new ArgumentException("--games must be positive.");
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
