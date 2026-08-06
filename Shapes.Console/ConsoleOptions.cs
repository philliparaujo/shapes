namespace Shapes.Console;

// Command-line options for the console client.
//
// Kept deliberately small and hand-parsed: adding a CLI package to get four flags would be the
// first NuGet dependency in the repo, and the console is a debugging tool rather than a shipped
// product (Phase 5's Godot client is the real one). If this grows past a handful of flags, that
// trade changes.
//
// Unknown arguments FAIL rather than being ignored -- a typo'd `--p1 gredy` that silently fell
// back to a human player would look like the game hanging on a prompt, and a mistyped `--sed 7`
// would silently produce an unreproducible game.
public sealed class ConsoleOptions
{
    // "human", "random", "greedy", or "ismcts" per seat. Both default to human, so running with
    // no arguments is exactly the hotseat client that existed before agents did.
    public string PlayerOne { get; private set; } = "human";

    public string PlayerTwo { get; private set; } = "human";

    // Search budget for an `ismcts` seat, in iterations. Applies to both seats: two searches at
    // different budgets are a strength comparison, which is Phase 3's job to run properly rather
    // than something to eyeball from one console game.
    //
    // Iterations rather than milliseconds so a seeded game replays exactly -- a wall-clock budget
    // fits a different number of iterations on a busy machine and reaches a different decision.
    // Defaults low enough that a full AI-v-AI game finishes while you watch it; raise it to see
    // the search play better.
    public int Iterations { get; private set; } = 200;

    // Null prompts for a seed interactively. Supplying one non-interactively is what makes an
    // AI-v-AI game scriptable and exactly replayable.
    public ulong? Seed { get; private set; }

    // Suppresses the board render, leaving one line per action plus a turn header. The form for
    // reading a whole game at once, or piping many games somewhere.
    public bool Quiet { get; private set; }

    // Renders BOTH hands in full, which is what the client did before step 2.5.
    //
    // Default off, so the non-active seat's hand shows only a count. Without that, a human
    // playing the AI reads the AI's hand while the AI cannot read theirs, and "I beat the AI"
    // stops meaning anything. It applies to hotseat too -- two humans sharing a screen genuinely
    // should not see each other's cards -- which is the convenience this flag buys back.
    //
    // Kept rather than deleted because full visibility is the right view for debugging a strange
    // board state or watching an AI-v-AI game, where there is no one to hide information from.
    //
    // Note this is a DISPLAY switch only. What an agent may see is ObservedState's job (step 2.2)
    // and is enforced by test; this flag cannot widen it. The console renders from GameState
    // either way -- coupling the screen to the engine invariant would make full-visibility
    // debugging impossible.
    public bool Reveal { get; private set; }

    // Pauses after every action until Enter is pressed, instead of printing at full speed.
    //
    // A fixed-ms delay was considered and rejected: it fights both a fast-to-skim turn (drawing,
    // ending) and a slow-to-read one (a multi-effect spell) in the same game, since both get the
    // same wait. Stepping one action at a time lets the reader set their own pace, which a timer
    // cannot. Independent of --quiet -- it controls PACING, not verbosity -- but its stated
    // purpose (PLAN.md Phase 4 step 4) is pacing an otherwise-fast --quiet log, so the two are
    // meant to be used together even though nothing enforces that.
    public bool Step { get; private set; }

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

                case "--iterations":
                    var rawIterations = RequireValue(args, ref i);
                    if (!int.TryParse(rawIterations, out var iterations) || iterations < 1)
                    {
                        throw new ArgumentException(
                            $"--iterations expects a positive number, got '{rawIterations}'.");
                    }

                    options.Iterations = iterations;
                    break;

                case "--quiet":
                    options.Quiet = true;
                    break;

                case "--reveal":
                    options.Reveal = true;
                    break;

                case "--step":
                    options.Step = true;
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
        System.Console.WriteLine(
            "  --p1 <agent>   Player one: human (default), random, greedy, ismcts");
        System.Console.WriteLine(
            "  --p2 <agent>   Player two: human (default), random, greedy, ismcts");
        System.Console.WriteLine("  --seed <n>     Seed the game instead of prompting");
        System.Console.WriteLine(
            "  --iterations <n>  Search budget per ismcts decision (default 200)");
        System.Console.WriteLine("  --quiet        One line per action; no board render");
        System.Console.WriteLine(
            "  --reveal       Show both hands in full (default: the waiting seat's is a count)");
        System.Console.WriteLine(
            "  --step         Pause for Enter after each action, instead of printing at full speed");
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
        System.Console.WriteLine();
        System.Console.WriteLine("  dotnet run --project Shapes.Console -- --p2 greedy --reveal");
        System.Console.WriteLine("      Same, but with the AI's hand visible — for debugging only.");
        System.Console.WriteLine();
        System.Console.WriteLine(
            "  dotnet run --project Shapes.Console -- --p1 ismcts --p2 greedy --seed 7 --quiet");
        System.Console.WriteLine("      Watch the search play the greedy baseline.");
        System.Console.WriteLine();
        System.Console.WriteLine(
            "  dotnet run --project Shapes.Console -- --p1 ismcts --p2 greedy --seed 7 --quiet --step");
        System.Console.WriteLine("      Same, but paused for Enter after each action.");
    }
}
