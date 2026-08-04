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

public enum TurnEventKind
{
    CreaturePlayed = 0,
    CreatureDestroyed = 1,
}

// One notable thing that happened this turn: a creature entering or leaving the board. Exists
// for cards that count within-turn events (Gravewarden: "draw 1 for each creature destroyed
// this turn") -- a full log rather than a bare counter, so a future card can ask a richer
// question (which slot, which card) without another engine change.
public sealed record TurnEvent(TurnEventKind Kind, PlayerId Player, SlotIndex Slot, string CardId);

// The complete state of a game: board, both players, whose turn it is, and the RNG.
//
// Mutable, with Clone() for the search. The plan's apply/undo optimisation replaces the
// cloning in Phase 2 behind this same surface, which is why the tests pin behaviour rather
// than representation.
public sealed class GameState
{
    private readonly PlayerState[] _players;
    private readonly List<TurnEvent> _turnEvents = [];

    public RuleSet Rules { get; }

    public Board Board { get; }

    public IRandomSource Random { get; }

    public PlayerId ActivePlayer { get; private set; }

    public TurnPhase Phase { get; private set; }

    // Events recorded since the start of the CURRENT turn (cleared by EndTurn, so they read as
    // "this turn" for the whole time the acting player is in their Actions phase, not just the
    // instant a card asks). Not carried into Clone() as a shared reference -- cloned independent
    // of the source's list contents -- see Clone().
    public IReadOnlyList<TurnEvent> TurnEvents => _turnEvents;

    public void RecordTurnEvent(TurnEventKind kind, PlayerId player, SlotIndex slot, string cardId) =>
        _turnEvents.Add(new TurnEvent(kind, player, slot, cardId));

    // Counts full rounds, incrementing when play returns to player one. Starts at 1.
    public int TurnNumber { get; private set; }

    // Starts in Scoring, not Actions: turn one runs the same score -> income -> actions
    // sequence as every later turn (scoring an empty board is simply a no-op). A caller that
    // wants to start playing immediately calls AdvanceToActions() once after construction, the
    // same call it makes after every EndTurn -- one turn-loop entry point rather than turn one
    // being a special case.
    public GameState(RuleSet rules, IRandomSource random, PlayerId startingPlayer = PlayerId.One)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        Rules = rules;
        Random = random;
        Board = new Board();
        _players = [new PlayerState(PlayerId.One), new PlayerState(PlayerId.Two)];
        ActivePlayer = startingPlayer;
        Phase = TurnPhase.Scoring;
        TurnNumber = 1;
    }

    private GameState(
        RuleSet rules, IRandomSource random, Board board, PlayerState[] players,
        PlayerId activePlayer, TurnPhase phase, int turnNumber, IEnumerable<TurnEvent> turnEvents)
    {
        Rules = rules;
        Random = random;
        Board = board;
        _players = players;
        ActivePlayer = activePlayer;
        Phase = phase;
        TurnNumber = turnNumber;
        _turnEvents = [.. turnEvents];
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
        Active.GainResources(Active.ConsumePendingNextTurnResources());
        Phase = TurnPhase.Actions;
    }

    // Runs scoring then income for the active player, in that order, and leaves the state ready
    // for the Actions phase -- unless scoring just won the game, in which case Phase stops at
    // Ended and income never runs. A no-op once the game is already in or past Actions, so
    // callers can invoke it unconditionally after EndTurn (or on a freshly constructed game)
    // without checking Phase themselves first. That "check Phase before calling ApplyScoring"
    // duplication is exactly what step 1.9 folds away -- see ActionExecutor.ApplyEndTurn.
    public void AdvanceToActions()
    {
        if (Phase == TurnPhase.Scoring)
        {
            ApplyScoring();

            if (IsOver)
            {
                Phase = TurnPhase.Ended;
                return;
            }
        }

        if (Phase == TurnPhase.Income)
        {
            ApplyIncome();
        }
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
        _turnEvents.Clear();
    }

    public void SetPhase(TurnPhase phase) => Phase = phase;

    // Debug affordance for the console client's POV swap.
    public void SetActivePlayer(PlayerId player) => ActivePlayer = player;

    // The RNG is forked, not shared: a search rollout on a clone must not advance the real
    // game's randomness, or replaying the same seed would stop producing the same game.
    public GameState Clone() =>
        new(Rules, Random.Fork(), Board.Clone(), [_players[0].Clone(), _players[1].Clone()],
            ActivePlayer, Phase, TurnNumber, _turnEvents);

    public override string ToString() =>
        $"turn {TurnNumber} {Phase} active=P{ActivePlayer.ToIndex() + 1} "
        + $"[{this[PlayerId.One].Score}-{this[PlayerId.Two].Score}]";
}
