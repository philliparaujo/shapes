using Shapes.Ai.Agents;
using Shapes.Ai.Search;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// Which agent a seat uses, or a human. Mirrors Shapes.Console's --p1/--p2 vocabulary (its
// BuildAgent switch) so the lobby offers exactly what the console already proves out, rather
// than inventing a second selection surface (PLAN.md C1, pulled forward to include C5's
// AI-opponent wiring rather than leaving picking an AI seat non-functional).
public enum AgentKind
{
    Human,
    Random,
    Greedy,
    IsMcts,
    IsMctsHeuristic,
}

// One seat's chosen player: Human, or an AgentKind plus a search budget (only meaningful for
// the two IS-MCTS kinds -- Random/Greedy have no difficulty knob, same as the console).
public sealed record SeatConfig(AgentKind Kind, int Iterations)
{
    public static SeatConfig Human { get; } = new(AgentKind.Human, 0);
}

// What the Lobby scene hands to GameRoot: both seats' player choice, plus the seed a hotseat
// game already needed. A plain data record rather than an autoload singleton -- Godot has no
// built-in way to pass constructor arguments across ChangeSceneToFile, and a static holder set
// immediately before the scene change and read once in GameRoot._Ready is the smallest
// mechanism that works, matching how little state actually needs to cross the boundary.
public sealed record MatchConfig(SeatConfig PlayerOne, SeatConfig PlayerTwo, ulong Seed)
{
    // Iteration budget, not a time budget, even here -- SearchBudget's own header: a wall-clock
    // budget makes the same seed play a different game on a different machine, and a hotseat
    // hobby project's whole reason to carry a seed is a replayable game. C5 owns deciding
    // whether interactive play should trade that away for responsiveness.
    public static readonly int[] DifficultyPresets = [200, 1000, 5000];
}

// Builds an IAgent from a SeatConfig, or null for a human seat -- the same null-means-human
// convention Shapes.Console's BuildAgent uses, so GameRoot's turn loop can reuse the console's
// exact "agent is null -> wait for UI" branch.
public static class AgentFactory
{
    // `opponentDeck` is the decklist the OTHER seat is playing -- what an IS-MCTS agent's
    // determinizer subtracts from to work out which cards the opponent could still be holding.
    //
    // It must be passed whenever the game is dealt from anything other than the ruleset's
    // symmetric decklist, and the Godot game always is: GameSession.Start deals from
    // DeckBuilder.Default (ONE of every card), while the determinizer's no-deck fallback rebuilds
    // CardDatabase.BuildSymmetricDeck (CopiesPerCard, currently TWO of every card). Determinizing
    // against a decklist twice the real size makes the "unseen cards" count disagree with the
    // opponent's observed hand+deck size, and Determinizer.RestoreOpponent throws on that
    // mismatch by design rather than papering over it -- which surfaced as the AI simply never
    // moving, since GameRoot.RunAiTurns is `async void` and swallows the exception.
    //
    // Optional (rather than required) only so the human-vs-human and Random/Greedy paths, which
    // never determinize, keep working without a deck on hand. See Shapes.Sim's own AgentFactory,
    // which has always threaded the opposing deck through for the same reason.
    public static IAgent? Build(SeatConfig seat, ulong seed, CardDatabase cards, Deck? opponentDeck = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(cards);

        var random = new SeededRandom(seed);
        return seat.Kind switch
        {
            AgentKind.Human => null,
            AgentKind.Random => new RandomAgent(random),
            AgentKind.Greedy => new GreedyAgent(random),
            AgentKind.IsMcts => new IsMctsAgent(
                cards, random, SearchBudget.OfIterations(seat.Iterations),
                opponentDeck: opponentDeck),
            AgentKind.IsMctsHeuristic => new IsMctsAgent(
                cards, random, SearchBudget.OfIterations(seat.Iterations),
                playoutPolicy: HeuristicPlayoutPolicy.Instance,
                opponentDeck: opponentDeck),
            _ => throw new ArgumentOutOfRangeException(nameof(seat), seat.Kind, "Unknown agent kind."),
        };
    }
}
