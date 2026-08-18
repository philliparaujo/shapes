using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Adapter;

// PLAN.md D5's network IMatchTransport: wraps a ClientWebSocket to a Shapes.Relay instance,
// carries a host or join handshake, then forwards GameActions both ways for the rest of the
// match. Plain System.Net.WebSockets + the source-generated RelayProtocolJsonContext -- no Godot
// dependency, matching every other type in this project (PLAN.md A2's project-structure note).
//
// Two ways to obtain one, matching the two roles a Lobby screen offers:
//   HostAsync -- opens the socket, asks the relay to host, and returns once a code is assigned.
//     Await `Joined` afterward for the moment a peer actually connects (or for the connection
//     closing first, which Joined also completes for -- see its own note).
//   JoinAsync -- opens the socket, asks the relay to join a given code, and returns once paired
//     (or throws if the code was unknown/expired/already claimed).
// Both return a transport already listening for match frames; MatchStart is exchanged explicitly
// (SendMatchStartAsync / WaitForMatchStartAsync) rather than folded into the constructors,
// because only the HOST computes it (seed, resolved seat) and only the JOINER waits to receive
// it -- the two roles are asymmetric for exactly one message, this one.
//
// Three message shapes cross this socket, deliberately kept apart rather than unified into one
// envelope: the RELAY's own hello/ack frames ({"type":...}, Shapes.Relay/Program.cs's hand-rolled
// JSON, read here as RelayHello) during the handshake; then, once paired, RelayEnvelope frames
// ({"Kind":...}) between the two GAME CLIENTS, which the relay itself never parses (it is a dumb
// pipe from that point on -- see Shapes.Relay's own header). The host receives exactly one more
// RelayHello after pairing (the relay's "{"type":"joined"}" pairing ack, forwarded from the
// relay itself, not from the peer) before the peer's own RelayEnvelope frames start arriving;
// ReceiveLoopAsync tries RelayHello first and falls back to RelayEnvelope for that reason.
public sealed class RelayMatchTransport : IMatchTransport
{
    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _receiveLoopCts = new();
    private readonly TaskCompletionSource<bool> _joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<RelayEnvelope> _matchStart =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _receiveLoop;
    private bool _disposed;

    public event Action<GameAction>? ActionReceived;
    public event Action? PeerDisconnected;

    private RelayMatchTransport(ClientWebSocket socket)
    {
        _socket = socket;
    }

    // Resolves once the relay has assigned a code (near-instant); does NOT wait for a peer.
    public string Code { get; private set; } = "";

    // HOST side only. Resolves TRUE once a peer has actually paired with this host's code, or
    // FALSE if the connection closed first (the player gave up, or the relay dropped it) without
    // anyone joining -- the caller (Lobby.OnHostPressed) reads the bool rather than needing a
    // separate failure event, since for the host "waiting for a peer" has exactly one other
    // outcome. The JOINER never awaits this; its equivalent wait is WaitForMatchStartAsync.
    public Task<bool> Joined => _joined.Task;

    public static async Task<RelayMatchTransport> HostAsync(Uri relayUri, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(relayUri, ct);
        var transport = new RelayMatchTransport(socket);

        await transport.SendRawAsync("{\"type\":\"host\"}", ct);
        var hostedRaw = await transport.ReceiveRawAsync(ct)
            ?? throw new InvalidOperationException("Relay closed the connection before assigning a code.");
        var hosted = JsonSerializer.Deserialize(hostedRaw, RelayProtocolJsonContext.Default.RelayHello)
            ?? throw new InvalidOperationException("Relay sent an unreadable response.");

        if (hosted.Type != "hosted" || string.IsNullOrEmpty(hosted.Code))
        {
            throw new InvalidOperationException("Relay did not confirm hosting.");
        }

        transport.Code = hosted.Code;
        transport.StartReceiveLoop(isHostAwaitingPeer: true);
        return transport;
    }

    public static async Task<RelayMatchTransport> JoinAsync(Uri relayUri, string code, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(relayUri, ct);
        var transport = new RelayMatchTransport(socket);

        await transport.SendRawAsync($"{{\"type\":\"join\",\"code\":\"{code.Trim().ToUpperInvariant()}\"}}", ct);
        var replyRaw = await transport.ReceiveRawAsync(ct)
            ?? throw new InvalidOperationException("Relay closed the connection before responding.");
        var reply = JsonSerializer.Deserialize(replyRaw, RelayProtocolJsonContext.Default.RelayHello)
            ?? throw new InvalidOperationException("Relay sent an unreadable response.");

        if (reply.Type != "joined")
        {
            throw new InvalidOperationException($"Could not join code '{code}'. It may be wrong or expired.");
        }

        transport.Code = code;
        transport.StartReceiveLoop(isHostAwaitingPeer: false);
        return transport;
    }

    // HOST side only: sends the resolved match parameters to the joiner once Joined has resolved
    // true. The host is the seed/seat authority (PLAN.md's redaction decision keeps the relay
    // itself rules-free), so this is a plain client-to-client message over the now-paired socket,
    // not something the relay computes or inspects.
    public Task SendMatchStartAsync(
        ulong seed, PlayerId yourSeat, DeckListDto? deckOne, DeckListDto? deckTwo, CancellationToken ct)
    {
        var envelope = RelayEnvelope.MatchStartMessage(seed, yourSeat, deckOne, deckTwo);
        return SendEnvelopeAsync(envelope, ct);
    }

    // JOINER side only: waits for the host's MatchStart. `YourSeat` on the returned envelope
    // already means "your own seat" from the joiner's point of view -- the host computed it FOR
    // the peer it is sending to, so no flip is needed on receipt.
    public Task<RelayEnvelope> WaitForMatchStartAsync() => _matchStart.Task;

    public async Task SendAsync(GameAction action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await SendEnvelopeAsync(RelayEnvelope.ActionMessage(action), CancellationToken.None);
    }

    private async Task SendEnvelopeAsync(RelayEnvelope envelope, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(envelope, RelayProtocolJsonContext.Default.RelayEnvelope);
        await SendRawAsync(json, ct);
    }

    private void StartReceiveLoop(bool isHostAwaitingPeer)
    {
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(isHostAwaitingPeer, _receiveLoopCts.Token));
    }

    private async Task ReceiveLoopAsync(bool awaitingPeerHello, CancellationToken ct)
    {
        try
        {
            while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var raw = await ReceiveRawAsync(ct);
                if (raw is null)
                {
                    break;
                }

                // The host's one-time pairing ack (see this type's own header) is a RelayHello,
                // not a RelayEnvelope -- tried first, and only while still awaiting it, so a
                // legitimate RelayEnvelope frame later in the match is never mis-parsed as one
                // (RelayHello's optional fields would silently accept unrelated JSON shapes too).
                if (awaitingPeerHello)
                {
                    awaitingPeerHello = false;
                    var hello = TryParse(raw, RelayProtocolJsonContext.Default.RelayHello);
                    if (hello?.Type == "joined")
                    {
                        _joined.TrySetResult(true);
                        continue;
                    }
                }

                var envelope = TryParse(raw, RelayProtocolJsonContext.Default.RelayEnvelope);
                if (envelope is not null)
                {
                    Handle(envelope);
                }
            }
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                // No-op if Joined already resolved true (a mid-match disconnect after pairing).
                // Resolves it FALSE here for the still-waiting-host case: the socket closed before
                // any peer arrived, which is the one outcome Joined promises to also report.
                _joined.TrySetResult(false);
                PeerDisconnected?.Invoke();
            }
        }
    }

    private static T? TryParse<T>(string json, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Handle(RelayEnvelope envelope)
    {
        switch (envelope.Kind)
        {
            case nameof(RelayMessageKind.MatchStart):
                _matchStart.TrySetResult(envelope);
                break;

            case nameof(RelayMessageKind.Action):
                if (envelope.Action is not null)
                {
                    ActionReceived?.Invoke(ActionDto.ToGameAction(envelope.Action));
                }

                break;
        }
    }

    private async Task SendRawAsync(string text, CancellationToken ct) =>
        await _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

    private async Task<string?> ReceiveRawAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _receiveLoopCts.Cancel();

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (Exception)
            {
                // Already surfaced (or benign) inside ReceiveLoopAsync's own try/catch.
            }
        }

        _socket.Dispose();
        _receiveLoopCts.Dispose();
    }
}
