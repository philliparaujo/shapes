namespace Shapes.Console;

// Command-line options for the console client.
//
// Kept deliberately small and hand-parsed: adding a CLI package to get four flags would be the
// first NuGet dependency in the repo, and the console is a debugging tool rather than a shipped
// product (Phase 4's Godot client is the real one). If this grows past a handful of flags, that
// trade changes.
//
// Unknown arguments FAIL rather than being ignored -- a typo'd `--p1 gredy` that silently fell
// back to a human player would look like the game hanging on a prompt, and a mistyped `--sed 7`
// would silently produce an unreproducible game.
public sealed class ConsoleOptions
{
    // "human", "random", or "greedy" per seat. Both default to human, so running with no
    // arguments is exactly the hotseat client that existed before agents did.
    public string PlayerOne { get; private set; } = "human";

    public string PlayerTwo { get; private set; } = "human";

    // Null prompts for a seed interactively. Supplying one non-interactively is what makes an
    // AI-v-AI game scriptable and exactly replayable.
    public ulong? Seed { get; private set; }

    // Suppresses the board render, leaving one line per action plus a turn header. The form for
    // reading a whole game at once, or piping many games somewhere.
    public bool Quiet { get; private set; }

    public bool ShowHelp { get; private set; }

    public static ConsoleOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var options = new ConsoleOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--p1":
                    options.PlayerOne = RequireValue(args, ref i);
                    break;

                case "--p2":
                    options.PlayerTwo = RequireValue(args, ref i);
                    break;

                case "--seed":
                    var raw = RequireValue(args, ref i);
                    if (!ulong.TryParse(raw, out var seed))
                    {
                        throw new ArgumentException($"--seed expects a number, got '{raw}'.");
                    }

                    options.Seed = seed;
                    break;

                case "--quiet":
                    options.Quiet = true;
                    break;

                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown argument '{args[i]}'. Run with --help for usage.");
            }
        }

        return options;
    }

    private static string RequireValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{args[i]} expects a value.");
        }

        return args[++i];
    }

    public static void PrintUsage()
    {
        System.Console.WriteLine("Shapes — console client");
        System.Console.WriteLine();
        System.Console.WriteLine("  --p1 <agent>   Player one: human (default), random, greedy");
        System.Console.WriteLine("  --p2 <agent>   Player two: human (default), random, greedy");
        System.Console.WriteLine("  --seed <n>     Seed the game instead of prompting");
        System.Console.WriteLine("  --quiet        One line per action; no board render");
        System.Console.WriteLine("  --help         This message");
        System.Console.WriteLine();
        System.Console.WriteLine("Examples:");
        System.Console.WriteLine("  dotnet run --project Shapes.Console");
        System.Console.WriteLine("      Hotseat, two humans (prompts for a seed).");
        System.Console.WriteLine();
        System.Console.WriteLine("  dotnet run --project Shapes.Console -- --p2 greedy --seed 7");
        System.Console.WriteLine("      Play against Greedy, with a board render each turn.");
        System.Console.WriteLine();
        System.Console.WriteLine(
            "  dotnet run --project Shapes.Console -- --p1 greedy --p2 random --seed 7 --quiet");
        System.Console.WriteLine("      Watch a full AI-v-AI game as a turn-by-turn action log.");
    }
}
