using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Core.State;

// The phase of a turn. Score and income resolve automatically at the start of a turn; the
// player only makes choices during Actions.
public enum TurnPhase
{
    Scoring = 0,
    Income = 1,
    Actions = 2,
    Ended = 3,
}

// The complete state of a game: board, both players, whose turn it is, and the RNG.
//
// Mutable, with Clone() for the search. The plan's apply/undo optimisation replaces the
// cloning in Phase 2 behind this same surface, which is why the tests pin behaviour rather
// than representation.
public sealed class GameState
{
    private readonly PlayerState[] _players;

    public RuleSet Rules { get; }

    public Board Board { get; }

    public IRandomSource Random { get; }

    public PlayerId ActivePlayer { get; private set; }

    public TurnPhase Phase { get; private set; }

    // Counts full rounds, incrementing when play returns to player one. Starts at 1.
    public int TurnNumber { get; private set; }

    public GameState(RuleSet rules, IRandomSource random, PlayerId startingPlayer = PlayerId.One)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        Rules = rules;
        Random = random;
        Board = new Board();
        _players = [new PlayerState(PlayerId.One), new PlayerState(PlayerId.Two)];
        ActivePlayer = startingPlayer;
        Phase = TurnPhase.Actions;
        TurnNumber = 1;
    }

    private GameState(
        RuleSet rules, IRandomSource random, Board board, PlayerState[] players,
        PlayerId activePlayer, TurnPhase phase, int turnNumber)
    {
        Rules = rules;
        Random = random;
        Board = board;
        _players = players;
        ActivePlayer = activePlayer;
        Phase = phase;
        TurnNumber = turnNumber;
    }

    public PlayerState this[PlayerId player] => _players[player.ToIndex()];

    public PlayerState Active => this[ActivePlayer];

    public PlayerState Inactive => this[ActivePlayer.Opponent()];

    public bool IsOver => Winner is not null;

    // The first player to reach the win threshold. Both reaching it simultaneously is not
    // reachable through normal play, since scoring resolves for one player at a time.
    public PlayerId? Winner
    {
        get
        {
            foreach (var player in PlayerIds.All)
            {
                if (this[player].Score >= Rules.ScoreToWin)
                {
                    return player;
                }
            }

            return null;
        }
    }

    // Points a player would score right now: one per unopposed creature. Pure -- callers use
    // it to preview scoring without applying it.
    public int PendingScore(PlayerId player) =>
        SlotIndex.AllFor(player).Count(Board.IsUnopposed) * Rules.PointsPerUnopposedCreature;

    // Income a player would receive right now: the flat base plus one per type per creature.
    // A Spike/Wheel creature contributes to both spike and wheel, which is why merged
    // creatures pay more.
    public ResourcePool PendingIncome(PlayerId player)
    {
        var income = Rules.BaseIncome;

        foreach (var (_, creature) in Board.CreaturesOf(player))
        {
            foreach (var type in creature.Types.ToArray())
            {
                income = income.Add(type, Rules.IncomePerCreatureType);
            }
        }

        return income;
    }

    public void ApplyScoring()
    {
        Active.AddScore(PendingScore(ActivePlayer));
        Phase = TurnPhase.Income;
    }

    public void ApplyIncome()
    {
        Active.GainResources(PendingIncome(ActivePlayer));
        Phase = TurnPhase.Actions;
    }

    // Hands the turn to the opponent and resets their per-turn creature state. Does not run
    // scoring or income -- the turn loop owns phase sequencing.
    public void EndTurn()
    {
        foreach (var (_, creature) in Board.CreaturesOf(ActivePlayer))
        {
            creature.ResetMovesForNewTurn();
        }

        ActivePlayer = ActivePlayer.Opponent();

        if (ActivePlayer == PlayerId.One)
        {
            TurnNumber++;
        }

        Phase = TurnPhase.Scoring;
    }

    public void SetPhase(TurnPhase phase) => Phase = phase;

    // Debug affordance for the console client's POV swap.
    public void SetActivePlayer(PlayerId player) => ActivePlayer = player;

    // The RNG is forked, not shared: a search rollout on a clone must not advance the real
    // game's randomness, or replaying the same seed would stop producing the same game.
    public GameState Clone() =>
        new(Rules, Random.Fork(), Board.Clone(), [_players[0].Clone(), _players[1].Clone()],
            ActivePlayer, Phase, TurnNumber);

    public override string ToString() =>
        $"turn {TurnNumber} {Phase} active=P{ActivePlayer.ToIndex() + 1} "
        + $"[{this[PlayerId.One].Score}-{this[PlayerId.Two].Score}]";
}
