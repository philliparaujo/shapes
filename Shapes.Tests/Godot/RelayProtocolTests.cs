using System.Text.Json;
using Shapes.Core.Actions;
using Shapes.Core.Primitives;
using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// DESIGN.md D5: RelayEnvelope is the wire shape RelayMatchTransport exchanges once a relay has
// paired two clients. The correctness bar mirrors SavedMatchTests' own note -- round-tripping the
// DTO is necessary but not sufficient; what actually matters is that a GameAction/PlayerId/deck
// survives the exact JSON serialization RelayMatchTransport uses (RelayProtocolJsonContext), the
// same source-generated context both send and receive paths share.
public class RelayProtocolTests
{
    private static string Roundtrip(RelayEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope, RelayProtocolJsonContext.Default.RelayEnvelope);
        return json;
    }

    private static RelayEnvelope Parse(string json) =>
        JsonSerializer.Deserialize(json, RelayProtocolJsonContext.Default.RelayEnvelope)
        ?? throw new InvalidOperationException("Deserialized to null.");

    [Fact]
    public void MatchStart_survives_the_wire_with_the_recipients_own_seat()
    {
        var deckOne = new DeckListDto { Name = "aggro", Cards = ["basic_square", "basic_square"] };
        var sent = RelayEnvelope.MatchStartMessage(seed: 123456789, yourSeat: PlayerId.Two, deckOne, deckTwo: null);

        var received = Parse(Roundtrip(sent));

        Assert.Equal(nameof(RelayMessageKind.MatchStart), received.Kind);
        Assert.Equal(123456789ul, received.Seed);
        Assert.Equal((int)PlayerId.Two, received.YourSeat);
        Assert.Equal("aggro", received.DeckOne?.Name);
        Assert.Equal(["basic_square", "basic_square"], received.DeckOne?.Cards);
        Assert.Null(received.DeckTwo);
    }

    [Theory]
    [InlineData(PlayerId.One)]
    [InlineData(PlayerId.Two)]
    public void An_EndTurn_action_survives_the_wire(PlayerId player)
    {
        var sent = RelayEnvelope.ActionMessage(new EndTurnAction(player));

        var received = Parse(Roundtrip(sent));

        Assert.Equal(nameof(RelayMessageKind.Action), received.Kind);
        Assert.NotNull(received.Action);
        var restored = ActionDto.ToGameAction(received.Action);
        Assert.Equal(new EndTurnAction(player), restored);
    }

    [Fact]
    public void A_targeted_move_action_survives_the_wire()
    {
        // UseMoveAction with a chosen target exercises every slot-carrying field ActionDto has --
        // the same shape SavedMatch's own action-log round trip already depends on, exercised here
        // against THIS project's separate JSON context to catch a context that forgot to register
        // a type (RelayProtocolJsonContext is hand-maintained, not shared with SavedMatchJsonContext).
        var action = new UseMoveAction(
            PlayerId.One,
            new SlotIndex(PlayerId.One, 0),
            moveIndex: 1,
            chosenTarget: new SlotIndex(PlayerId.Two, 2));

        var sent = RelayEnvelope.ActionMessage(action);
        var received = Parse(Roundtrip(sent));

        var restored = ActionDto.ToGameAction(received.Action!);
        Assert.Equal(action, restored);
    }

    [Fact]
    public void RelayHello_reads_the_relays_own_hosted_frame()
    {
        // Shapes.Relay/Program.cs hand-writes this frame directly (it doesn't reference this
        // assembly -- see that project's own header), so this pins the CLIENT side reads exactly
        // the shape the relay actually sends, not just what RelayHello itself can produce.
        var hello = JsonSerializer.Deserialize(
            "{\"type\":\"hosted\",\"code\":\"ABC123\"}", RelayProtocolJsonContext.Default.RelayHello);

        Assert.NotNull(hello);
        Assert.Equal("hosted", hello!.Type);
        Assert.Equal("ABC123", hello.Code);
    }
}
