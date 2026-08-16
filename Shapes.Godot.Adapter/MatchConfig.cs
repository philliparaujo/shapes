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

// Whose seat the screen is drawn from -- deliberately NOT the same question as whose turn it is
// (PLAN.md D1). GameState.ActivePlayer answers turn order and nothing else; this answers "which
// row is the bottom row, and whose hand is legible."
//
// The two were the same expression (`self = state.ActivePlayer`) for as long as hotseat was the
// only mode, where flipping the board every turn IS the correct behaviour -- one screen passed
// between two people. It stopped being correct the moment a seat could be an AI: the board would
// turn around on the AI's turn and fan the AI's hand face-up at the bottom of the screen, so the
// player watched their opponent's turn from inside their opponent's seat.
//
// FollowsActive keeps the hotseat behaviour exactly (and is also the right way to WATCH an
// AI-v-AI game, where there is no human seat to anchor to). Fixed pins the view to one seat
// regardless of turn -- one human against an AI today, and the shape a network client needs
// tomorrow, where "self" is a local fact the remote player's turn cannot move.
public abstract record ViewerMode
{
    private ViewerMode() { }

    // The seat this mode shows for a given turn. The whole type exists to make this one call
    // the only place the question is answered, rather than `state.ActivePlayer` appearing
    // inline at each view that needs a perspective.
    public abstract PlayerId Resolve(PlayerId activePlayer);

    public sealed record FollowsActive : ViewerMode
    {
        public override PlayerId Resolve(PlayerId activePlayer) => activePlayer;
    }

    public sealed record Fixed(PlayerId Seat) : ViewerMode
    {
        public override PlayerId Resolve(PlayerId activePlayer) => Seat;
    }

    public static ViewerMode Hotseat { get; } = new FollowsActive();

    public static ViewerMode At(PlayerId seat) => new Fixed(seat);

    // The mode a given pair of seats implies, so neither the lobby nor a resumed match has to
    // decide it twice (and cannot decide it differently). Exactly one human seat means that human
    // is watching -- pin to them. Two humans is hotseat, the mode that has always flipped. Zero
    // humans is a spectated AI-v-AI game, where following the active player is what makes the
    // game readable: each agent's own hand is shown as it acts, which is the point of watching.
    //
    // Derived rather than offered as a lobby control on purpose: every configuration has exactly
    // one sensible answer, and a control here would be a way to pick the wrong one.
    public static ViewerMode For(SeatConfig playerOne, SeatConfig playerTwo)
    {
        ArgumentNullException.ThrowIfNull(playerOne);
        ArgumentNullException.ThrowIfNull(playerTwo);

        var oneIsHuman = playerOne.Kind == AgentKind.Human;
        var twoIsHuman = playerTwo.Kind == AgentKind.Human;

        return (oneIsHuman, twoIsHuman) switch
        {
            (true, false) => At(PlayerId.One),
            (false, true) => At(PlayerId.Two),
            _ => Hotseat,
        };
    }
}

// What the Lobby scene hands to GameRoot: both seats' player choice, plus the seed a hotseat
// game already needed. A plain data record rather than an autoload singleton -- Godot has no
// built-in way to pass constructor arguments across ChangeSceneToFile, and a static holder set
// immediately before the scene change and read once in GameRoot._Ready is the smallest
// mechanism that works, matching how little state actually needs to cross the boundary.
// `DeckOne`/`DeckTwo` are the decklists the lobby's per-seat deck dropdowns selected (PLAN.md
// C2), null meaning "the default deck" -- the same null-means-default convention GameSession.Start
// already uses, so an AI-vs-AI game started without touching the dropdowns behaves exactly as it
// did before decks existed.
public sealed record MatchConfig(
    SeatConfig PlayerOne, SeatConfig PlayerTwo, ulong Seed, Deck? DeckOne = null, Deck? DeckTwo = null)
{
    // Whose seat the screen shows (PLAN.md D1). Derived from the two seat configs rather than
    // stored by the lobby, for the reason in ViewerMode.For: every seat pairing has exactly one
    // sensible answer, so making this a settable property would only create a way for a caller to
    // supply a different one. It is also therefore correct for a RESUMED match for free --
    // SavedMatch persists both SeatConfigs already, so a resumed vs-AI game re-derives Fixed
    // without the save file needing to have known about viewers when it was written.
    public ViewerMode Viewer => ViewerMode.For(PlayerOne, PlayerTwo);

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
