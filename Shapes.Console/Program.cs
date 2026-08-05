// Text client for Shapes: hotseat human v human, human v AI, and AI v AI play.
// Implements PLAN.md Phase 1, step 11, plus the agent-driven modes added with step 2.4.
//
// Usage:
//   dotnet run --project Shapes.Console                         hotseat (default)
//   dotnet run --project Shapes.Console -- --p1 greedy           human v Greedy
//   dotnet run --project Shapes.Console -- --p1 greedy --p2 random --seed 7 --quiet
//
// The AI-v-AI mode is how a full game gets WATCHED rather than merely asserted about. A passing
// test tells you the agent's decisions were legal; it does not tell you what it did with its
// turns, and the action mix that revealed step 2.4's blocking-slot bug was only visible by
// looking. --quiet is the form for that: one line per action, no board redraw, so a whole game
// fits on a screen and a hundred fit in a pipe.

using Shapes.Ai.Agents;
using Shapes.Console;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

ConsoleOptions options;
try
{
    options = ConsoleOptions.Parse(args);
}
catch (ArgumentException ex)
{
    // A mistyped flag is a user error, not a crash. Printing the message and the usage beats a
    // stack trace that buries the one line explaining what to type instead.
    System.Console.Error.WriteLine(ex.Message);
    System.Console.Error.WriteLine();
    ConsoleOptions.PrintUsage();
    return 1;
}

if (options.ShowHelp)
{
    ConsoleOptions.PrintUsage();
    return 0;
}

System.Console.WriteLine("Shapes — console client");
System.Console.WriteLine();

var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
var cards = CardLoader.FromDirectory(cardsDir);
var rules = RuleSet.Default;

// A seed on the command line skips the prompt -- which is what makes an AI-v-AI game
// reproducible from a script, and what lets a suspicious game be replayed exactly.
var seed = options.Seed ?? PromptSeed();
var random = new SeededRandom(seed);
System.Console.WriteLine($"Seed: {random.Seed}");

// Agents are built from the same seed so a replay is exact. Each gets its own derived stream
// rather than sharing one: two agents drawing from a single source would interleave their draws,
// so changing one player's agent would change the other's decisions too.
Dictionary<PlayerId, IAgent?> agents;
try
{
    agents = new Dictionary<PlayerId, IAgent?>
    {
        [PlayerId.One] = BuildAgent(options.PlayerOne, seed * 7919),
        [PlayerId.Two] = BuildAgent(options.PlayerTwo, seed * 104729),
    };
}
catch (ArgumentException ex)
{
    System.Console.Error.WriteLine(ex.Message);
    return 1;
}

System.Console.WriteLine(
    $"P1: {agents[PlayerId.One]?.Name ?? "Human"}    P2: {agents[PlayerId.Two]?.Name ?? "Human"}");
System.Console.WriteLine();

var state = new GameState(rules, random, PlayerId.One);
foreach (var playerId in PlayerIds.All)
{
    var player = state[playerId];
    player.SetDeck(cards.BuildSymmetricDeck(rules));
    player.ShuffleDeck(random);
    player.Draw(rules.StartingHandSize);
}

state.AdvanceToActions();

var turnNumber = 0;
var actionCount = 0;

while (!state.IsOver)
{
    var agent = agents[state.ActivePlayer];

    // A turn header in quiet mode, so a log of actions can still be read as a game rather than
    // as an undifferentiated stream. Printed on the transition, not per action.
    if (options.Quiet && state.TurnNumber != turnNumber)
    {
        turnNumber = state.TurnNumber;
        System.Console.WriteLine(
            $"--- turn {turnNumber}   P1 {state[PlayerId.One].Score} - {state[PlayerId.Two].Score} P2");
    }

    if (!options.Quiet)
    {
        BoardView.Render(state, cards);
        System.Console.WriteLine();
    }

    GameAction choice;
    if (agent is null)
    {
        var actions = ActionGenerator.Generate(state, cards);
        choice = PromptAction(actions, cards);
    }
    else
    {
        choice = agent.Choose(AgentContext.ForActivePlayer(state, cards));

        // Every AI decision is echoed. Without this an AI-v-AI game is a black box that prints
        // only its winner -- which is precisely the gap that made the blocking-slot bug invisible
        // until it was probed for directly.
        System.Console.WriteLine(
            $"  P{state.ActivePlayer.ToIndex() + 1} ({agent.Name}): {Describe(choice, cards)}");
    }

    ActionExecutor.Apply(state, cards, choice);
    actionCount++;

    if (!options.Quiet)
    {
        System.Console.WriteLine();
    }
}

if (!options.Quiet)
{
    BoardView.Render(state, cards);
}

System.Console.WriteLine();
System.Console.WriteLine(
    $"Player {(int)state.Winner! + 1} wins with {state[state.Winner!.Value].Score} points "
    + $"after {actionCount} actions ({state.TurnNumber} turns).");

return 0;

// Null means a human plays this seat -- the console prompts instead of asking an agent.
static IAgent? BuildAgent(string kind, ulong seed) => kind.ToLowerInvariant() switch
{
    "human" => null,
    "random" => new RandomAgent(new SeededRandom(seed)),
    "greedy" => new GreedyAgent(new SeededRandom(seed)),
    _ => throw new ArgumentException(
        $"Unknown agent '{kind}'. Expected human, random, or greedy."),
};

static ulong PromptSeed()
{
    System.Console.Write("Seed (blank for random): ");
    var input = System.Console.ReadLine();
    if (ulong.TryParse(input, out var seed))
    {
        return seed;
    }

    return (ulong)Random.Shared.NextInt64();
}

static GameAction PromptAction(IReadOnlyList<GameAction> actions, CardDatabase cards)
{
    while (true)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            System.Console.WriteLine($"  {i + 1}. {Describe(actions[i], cards)}");
        }

        System.Console.Write("> ");
        var input = System.Console.ReadLine();
        if (int.TryParse(input, out var choice) && choice >= 1 && choice <= actions.Count)
        {
            return actions[choice - 1];
        }

        System.Console.WriteLine($"Enter a number from 1 to {actions.Count}.");
    }
}

static string Describe(GameAction action, CardDatabase cards)
{
    // Swap the card id for its display name. Both PlayCard and Discard name a card, and a
    // discard menu reading "Discard t_juggler" rather than "Discard T Juggler" is exactly the
    // moment the player has to pick one, so it is worth getting right.
    var cardId = action switch
    {
        PlayCardAction play => play.CardId,
        DiscardAction discard => discard.CardId,
        _ => null,
    };

    if (cardId is not null && cards.TryGet(cardId, out var card))
    {
        return action.Describe().Replace(cardId, card!.Name, StringComparison.Ordinal);
    }

    return action.Describe();
}
