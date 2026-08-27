using System.Text.Json;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// DESIGN.md C6: SavedMatch (seed + action log) is the persistence format; GameSession.Resume is
// what proves the log alone is sufficient to reconstruct a game. The real correctness bar here
// is not "does the DTO round-trip" (necessary but not sufficient) -- it's "does a resumed
// session end up in EXACTLY the same state as the live session that produced the log," since
// that is the property an interrupted-game resume actually needs.
public class SavedMatchTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static RuleSet Rules => RuleSet.Default;

    private static GameSession NewSession(ulong seed)
    {
        var session = new GameSession(Rules, Cards, new SeededRandom(seed), PlayerId.One);
        session.Start(Rules.StartingHandSize);
        return session;
    }

    [Fact]
    public void Resume_reproduces_a_full_random_playthrough_exactly()
    {
        var live = NewSession(seed: 99);
        var random = new SeededRandom(12345);
        var actions = new List<GameAction>();

        while (!live.State.IsOver && actions.Count < 5000)
        {
            var legal = live.State.IsOver ? [] : live.LegalActions();
            var choice = legal[random.Next(legal.Count)];
            live.Submit(choice);
            actions.Add(choice);
        }

        Assert.True(live.State.IsOver, "Live playthrough did not terminate.");

        var resumed = GameSession.Resume(
            Rules, Cards, new SeededRandom(99), PlayerId.One, Rules.StartingHandSize, actions);

        Assert.Equal(live.State.IsOver, resumed.State.IsOver);
        Assert.Equal(live.State.Winner, resumed.State.Winner);
        Assert.Equal(live.State.TurnNumber, resumed.State.TurnNumber);
        Assert.Equal(live.State.ActivePlayer, resumed.State.ActivePlayer);
        Assert.Equal(live.State[PlayerId.One].Score, resumed.State[PlayerId.One].Score);
        Assert.Equal(live.State[PlayerId.Two].Score, resumed.State[PlayerId.Two].Score);
        Assert.Equal(live.State[PlayerId.One].Hand, resumed.State[PlayerId.One].Hand);
        Assert.Equal(live.State[PlayerId.Two].Hand, resumed.State[PlayerId.Two].Hand);
    }

    [Fact]
    public void Resume_reproduces_a_partial_game_mid_turn()
    {
        // The realistic C6 case: the app was killed mid-turn, not after a finished game --
        // resume must land in the exact same in-progress position, not just at a terminal one.
        var live = NewSession(seed: 41);
        var random = new SeededRandom(7);
        var actions = new List<GameAction>();

        for (var i = 0; i < 12 && !live.State.IsOver; i++)
        {
            var legal = live.LegalActions();
            var choice = legal[random.Next(legal.Count)];
            live.Submit(choice);
            actions.Add(choice);
        }

        Assert.False(live.State.IsOver, "Test needs a genuinely mid-game position.");

        var resumed = GameSession.Resume(
            Rules, Cards, new SeededRandom(41), PlayerId.One, Rules.StartingHandSize, actions);

        Assert.Equal(live.State.ActivePlayer, resumed.State.ActivePlayer);
        Assert.Equal(live.State.Phase, resumed.State.Phase);
        Assert.Equal(live.State.PendingDiscards, resumed.State.PendingDiscards);
        Assert.Equal(live.LegalActions(), resumed.LegalActions());

        foreach (var slot in SlotIndex.AllFor(PlayerId.One).Concat(SlotIndex.AllFor(PlayerId.Two)))
        {
            var liveCreature = live.State.Board[slot];
            var resumedCreature = resumed.State.Board[slot];
            Assert.Equal(liveCreature is null, resumedCreature is null);
            if (liveCreature is not null && resumedCreature is not null)
            {
                Assert.Equal(liveCreature.CardId, resumedCreature.CardId);
                Assert.Equal(liveCreature.Health, resumedCreature.Health);
                Assert.Equal(liveCreature.MergedFrom, resumedCreature.MergedFrom);
            }
        }
    }

    [Theory]
    [InlineData(ActionKind.PlayCard)]
    [InlineData(ActionKind.UseMove)]
    [InlineData(ActionKind.Merge)]
    [InlineData(ActionKind.Discard)]
    [InlineData(ActionKind.EndTurn)]
    public void Every_action_kind_round_trips_through_the_DTO(ActionKind kind)
    {
        GameAction original = kind switch
        {
            ActionKind.PlayCard => new PlayCardAction(
                PlayerId.One, "basic_circle", new SlotIndex(PlayerId.One, 1),
                new SlotIndex(PlayerId.Two, 2)),
            ActionKind.UseMove => new UseMoveAction(
                PlayerId.Two, new SlotIndex(PlayerId.Two, 0), 3, new SlotIndex(PlayerId.One, 1)),
            ActionKind.Merge => new MergeAction(
                PlayerId.One, new SlotIndex(PlayerId.One, 0), new SlotIndex(PlayerId.One, 1)),
            ActionKind.Discard => new DiscardAction(PlayerId.Two, "basic_square"),
            ActionKind.EndTurn => new EndTurnAction(PlayerId.One),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var dto = ActionDto.FromGameAction(original);
        var restored = ActionDto.ToGameAction(dto);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void PlayCard_with_no_slots_round_trips_as_null_not_a_default_slot()
    {
        // A targetless spell has null TargetSlot/ChosenTarget -- guards against the DTO's
        // int?-pair-per-slot encoding accidentally producing SlotIndex(One, 0) instead of null
        // when both owner and index are unset.
        var original = new PlayCardAction(PlayerId.One, "anchor");

        var restored = ActionDto.ToGameAction(ActionDto.FromGameAction(original));

        Assert.Equal(original, restored);
        Assert.Null(((PlayCardAction)restored).TargetSlot);
        Assert.Null(((PlayCardAction)restored).ChosenTarget);
    }

    [Fact]
    public void SavedMatch_round_trips_through_JSON()
    {
        var actions = new List<GameAction>
        {
            new PlayCardAction(PlayerId.One, "basic_circle", new SlotIndex(PlayerId.One, 0)),
            new EndTurnAction(PlayerId.One),
            new UseMoveAction(PlayerId.Two, new SlotIndex(PlayerId.Two, 0), 0),
        };
        var original = new SavedMatch(
            Seed: 12345,
            PlayerOne: SeatConfig.Human,
            PlayerTwo: new SeatConfig(AgentKind.IsMcts, 1000),
            actions);

        var json = JsonSerializer.Serialize(original.ToDto(), SavedMatchJsonContext.Default.SavedMatchDto);
        var dto = JsonSerializer.Deserialize(json, SavedMatchJsonContext.Default.SavedMatchDto);
        var restored = SavedMatch.FromDto(dto!);

        Assert.Equal(original.Seed, restored.Seed);
        Assert.Equal(original.PlayerOne, restored.PlayerOne);
        Assert.Equal(original.PlayerTwo, restored.PlayerTwo);
        Assert.Equal(original.Actions, restored.Actions);
    }
}
