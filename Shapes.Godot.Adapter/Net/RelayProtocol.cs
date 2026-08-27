using System.Text.Json;
using System.Text.Json.Serialization;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Adapter;

// DESIGN.md D5: the messages exchanged over a RelayMatchTransport, once Shapes.Relay has paired two
// sockets by code. Reuses SavedMatch.cs's ActionDto/SeatDto/DeckListDto rather than inventing a
// second serialization of GameAction/SeatConfig/Deck -- those types already round-trip everything
// this protocol needs to send, for the same reason SavedMatch itself gives (GameAction is flat,
// value-equal, and fully self-describing; a deck's exact pre-shuffle order has to travel intact).
//
// The relay server itself never parses any of this beyond the hello frame's own "type"/"code"
// fields (Shapes.Relay/Program.cs) -- everything below is opaque bytes to it. Both ends of the
// wire are always this same Shapes.Godot.Adapter assembly, so there is no cross-language/
// cross-version concern to design defensively against; unlike SavedMatchDto (which reads files
// written by past app versions), unmapped members are not expected here and are left to the
// default (ignore) behaviour purely for forward-compat within a single running match, not as a
// deliberate looseness.
public enum RelayMessageKind
{
    // Client -> relay -> nobody (server-handled): "I want to host, give me a code."
    Host,

    // Server -> client: "your code is X." Client -> other client: never sent, this is host-only.
    Hosted,

    // Client -> relay -> nobody (server-handled): "pair me with code X."
    Join,

    // Server -> the JOINER only, forwarded by the relay to the host session's own socket object
    // (see Shapes.Relay's PumpAsync) -- the host learns a peer arrived from HostHello arriving,
    // not from a separate signal, since HostHello IS that signal once forwarding starts.
    JoinFailed,

    // Host -> joiner, once both sides are connected: the actual match parameters. The host is the
    // authority that picked the seed and resolved "random" seat order (DESIGN.md's redaction
    // decision: the relay itself stays rules-free, so this can't be the server's job).
    MatchStart,

    // Either side -> the other, for the lifetime of the match: one submitted GameAction.
    Action,
}

// One envelope for every message this protocol sends after the relay's own hello handshake
// (RelayHello below covers that earlier step). A single flat DTO with a Kind discriminator,
// mirroring ActionDto's own shape in the same file family, rather than a type hierarchy --
// there are only a handful of message shapes and most fields are used by exactly one kind.
public sealed class RelayEnvelope
{
    public string? Kind { get; set; }

    // MatchStart fields.
    public ulong? Seed { get; set; }
    public int? YourSeat { get; set; }
    public DeckListDto? DeckOne { get; set; }
    public DeckListDto? DeckTwo { get; set; }

    // Action fields.
    public ActionDto? Action { get; set; }

    public static RelayEnvelope MatchStartMessage(ulong seed, PlayerId yourSeat, DeckListDto? deckOne, DeckListDto? deckTwo) =>
        new()
        {
            Kind = nameof(RelayMessageKind.MatchStart),
            Seed = seed,
            YourSeat = (int)yourSeat,
            DeckOne = deckOne,
            DeckTwo = deckTwo,
        };

    public static RelayEnvelope ActionMessage(Core.Actions.GameAction action) =>
        new() { Kind = nameof(RelayMessageKind.Action), Action = ActionDto.FromGameAction(action) };
}

// The relay's own hello/ack frames (Shapes.Relay/Program.cs's minimal hand-rolled JSON), read
// here on the client side with the same source-generated context as everything else in this file
// so the client never hand-writes JSON either.
public sealed class RelayHello
{
    public string? Type { get; set; }
    public string? Code { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = false)]
[JsonSerializable(typeof(RelayEnvelope))]
[JsonSerializable(typeof(RelayHello))]
public sealed partial class RelayProtocolJsonContext : JsonSerializerContext
{
}
