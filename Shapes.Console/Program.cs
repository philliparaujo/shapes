// Text client for Shapes: hotseat human v human play.
// Implements PLAN.md Phase 1, step 11.

using Shapes.Console;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

System.Console.WriteLine("Shapes — console client");
System.Console.WriteLine();

var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
var cards = CardLoader.FromDirectory(cardsDir);
var rules = RuleSet.Default;

var seed = PromptSeed();
var random = new SeededRandom(seed);
System.Console.WriteLine($"Seed: {random.Seed}");

var state = new GameState(rules, random, PlayerId.One);
foreach (var playerId in PlayerIds.All)
{
    var player = state[playerId];
    player.SetDeck(cards.BuildSymmetricDeck(rules));
    player.ShuffleDeck(random);
    player.Draw(rules.StartingHandSize);
}

state.AdvanceToActions();

while (!state.IsOver)
{
    BoardView.Render(state, cards);
    System.Console.WriteLine();

    var actions = ActionGenerator.Generate(state, cards);
    var choice = PromptAction(actions, cards);
    ActionExecutor.Apply(state, cards, choice);
    System.Console.WriteLine();
}

BoardView.Render(state, cards);
System.Console.WriteLine();
System.Console.WriteLine($"Player {(int)state.Winner! + 1} wins with {state[state.Winner!.Value].Score} points!");

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
    if (action is PlayCardAction play && cards.TryGet(play.CardId, out var card))
    {
        return action.Describe().Replace(play.CardId, card!.Name, StringComparison.Ordinal);
    }

    return action.Describe();
}
