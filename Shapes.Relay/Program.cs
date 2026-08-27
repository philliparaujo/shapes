// Shapes relay: pairs two WebSocket clients by a short code and forwards frames between them.
// DESIGN.md D5 -- the piece that lets two installs find each other across different home networks.
// A router/NAT blocks an unsolicited INBOUND connection, which is what plain host-prints-their-IP
// direct play would need; a relay works because both clients only ever connect OUT to it, which
// no home network blocks.
//
// Usage:
//   dotnet run --project Shapes.Relay                    listens on port 5080
//   dotnet run --project Shapes.Relay -- --port 6000      listens on a different port
//
// Deployment note: this is an ordinary dotnet-run-able ASP.NET Core app with no assumptions about
// where it runs. Today: your own machine, for same-machine/LAN testing (point Godot clients at
// ws://localhost:5080/ws). Later: any always-on VM (DESIGN.md's own D5 section names Oracle
// Cloud's Always Free tier) -- moving it is "run this binary there, open the one port, change the
// client's ws:// URL." Nothing here is Oracle-specific.
//
// Deliberately dumb: this process never loads Shapes.Core, never validates a GameAction, and
// never looks at what it is forwarding once two sockets are paired. Per DESIGN.md's redaction
// decision, a friends-only relay doesn't need to referee rules -- the two clients already trust
// each other, and giving a public-facing process the full engine buys nothing at this scope.

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var port = 5080;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] != "--port")
    {
        System.Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: --port <n>");
        return 1;
    }

    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out port) || port is < 1 or > 65535)
    {
        System.Console.Error.WriteLine("--port expects a number between 1 and 65535.");
        return 1;
    }

    i++;
}

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();
app.UseWebSockets();

var matches = new RelayMatchTable();

app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await RelaySession.RunAsync(socket, matches, context.RequestAborted);
});

System.Console.WriteLine($"Shapes relay listening on ws://0.0.0.0:{port}/ws");
app.Run();
return 0;

// One pending "host" socket waiting for a joiner, keyed by its code. A host registers a code and
// then awaits `Joined` -- the TaskCompletionSource IS the handoff: TakeHost's caller (the joiner's
// session) completes it with its own socket, which wakes the host's session task directly with no
// polling. Removed from the table the moment a joiner claims it, on expiry, or if the host's own
// socket closes first (RelaySession's finally block calls CancelHost).
internal sealed class RelayMatchTable
{
    // Six chars, uppercase letters + digits minus visually-ambiguous 0/O/1/I -- short enough to
    // read aloud to a friend, long enough that a random guess isn't a real risk at this scope
    // (friends-only, not a public matchmaking pool).
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, PendingHost> _pending = new();

    public (string Code, Task<WebSocket?> Joined) RegisterHost()
    {
        ExpireStale();

        var joined = new TaskCompletionSource<WebSocket?>(TaskCreationOptions.RunContinuationsAsynchronously);
        string code;
        do
        {
            code = GenerateCode();
        }
        while (!_pending.TryAdd(code, new PendingHost(joined, DateTime.UtcNow)));

        return (code, joined.Task);
    }

    // Called by the JOINING session with its own socket. Returns false (and leaves the joiner
    // with nothing to do but report failure) if the code is unknown, expired, or already claimed.
    public bool TryPairJoiner(string code, WebSocket joinerSocket)
    {
        if (!_pending.TryRemove(code, out var pending))
        {
            return false;
        }

        return pending.Joined.TrySetResult(joinerSocket);
    }

    public void CancelHost(string code)
    {
        if (_pending.TryRemove(code, out var pending))
        {
            pending.Joined.TrySetResult(null);
        }
    }

    private void ExpireStale()
    {
        var cutoff = DateTime.UtcNow - CodeLifetime;
        foreach (var (code, pending) in _pending)
        {
            if (pending.RegisteredAt < cutoff)
            {
                CancelHost(code);
            }
        }
    }

    private static string GenerateCode() =>
        string.Create(6, Random.Shared, static (span, rng) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = CodeAlphabet[rng.Next(CodeAlphabet.Length)];
            }
        });

    private readonly record struct PendingHost(TaskCompletionSource<WebSocket?> Joined, DateTime RegisteredAt);
}

// One accepted WebSocket's lifetime: read its first "hello" frame (host or join), then either
// wait to be paired (host) or pair immediately (join), then forward every subsequent frame
// verbatim to the peer until either side closes. The message SHAPE is Shapes.Godot.Adapter's
// RelayProtocol -- this project deliberately doesn't reference that assembly (see the csproj
// header), so it treats every frame after the hello as an opaque byte blob to forward, and only
// parses enough JSON itself (the hello's own "type"/"code" fields) to make the routing decision.
internal static class RelaySession
{
    public static async Task RunAsync(WebSocket socket, RelayMatchTable matches, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        string? hostedCode = null;

        try
        {
            var hello = await ReceiveTextAsync(socket, buffer, ct);
            if (hello is null)
            {
                return;
            }

            using var helloDoc = JsonDocument.Parse(hello);
            var type = helloDoc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "host":
                {
                    var (code, joined) = matches.RegisterHost();
                    hostedCode = code;
                    await SendTextAsync(socket, $"{{\"type\":\"hosted\",\"code\":\"{code}\"}}", ct);

                    // Waits for a joiner while also noticing if this host socket closes first (the
                    // player gave up). Polls WebSocketState -- the SAME reason the joiner's own
                    // wait (WaitForSocketToCloseAsync below) polls instead of calling ReceiveAsync:
                    // a receive left in flight when the joiner arrives would have to be cancelled,
                    // and cancelling ReceiveAsync aborts the WHOLE WebSocket (ManagedWebSocket
                    // transitions to the Aborted state on a cancelled receive, not just that one
                    // call), poisoning the connection PumpAsync is about to start using. Polling
                    // never touches the read stream, so there is nothing to cancel or abort.
                    var joinerSocket = await WaitForJoinerAsync(socket, joined, ct);
                    if (joinerSocket is null)
                    {
                        matches.CancelHost(code);
                        return;
                    }

                    hostedCode = null; // Claimed by the joiner; no longer this session's to cancel.
                    await SendTextAsync(socket, "{\"type\":\"joined\"}", ct);
                    await PumpAsync(socket, joinerSocket, ct);
                    return;
                }

                case "join":
                {
                    var code = helloDoc.RootElement.GetProperty("code").GetString()?.Trim().ToUpperInvariant();
                    if (string.IsNullOrEmpty(code) || !matches.TryPairJoiner(code, socket))
                    {
                        await SendTextAsync(socket, "{\"type\":\"join-failed\"}", ct);
                        return;
                    }

                    // The JOINER gets its own "joined" ack here -- RelayMatchTransport.JoinAsync
                    // blocks on reading exactly this frame before returning (mirroring how
                    // HostAsync blocks on "hosted"). The HOST's session (the "host" case above)
                    // sends its OWN separate "joined" ack on its own socket once `joined` resolves
                    // -- two acks, one per socket, since each session only ever reads from the
                    // socket it was handed and PumpAsync (below) must not start on either side
                    // until the ack that unblocks its caller has already been written.
                    await SendTextAsync(socket, "{\"type\":\"joined\"}", ct);

                    // Handing `socket` to the host's session above is the pairing: THAT task now
                    // owns reading/writing it via PumpAsync. This task's only remaining job is to
                    // stay alive until the connection closes, so ASP.NET Core doesn't dispose the
                    // socket out from under the host's pump -- it must not also read from `socket`
                    // (that would race PumpAsync's own ReceiveAsync on the same object).
                    await WaitForSocketToCloseAsync(socket, ct);
                    return;
                }

                default:
                    return;
            }
        }
        catch (WebSocketException)
        {
            // Peer dropped mid-handshake -- nothing to clean up beyond the finally block below.
        }
        catch (OperationCanceledException)
        {
            // Host process shutting down, or the request was aborted.
        }
        finally
        {
            if (hostedCode is not null)
            {
                matches.CancelHost(hostedCode);
            }
        }
    }

    // Polls rather than a blocking receive -- see the "host" case's own note on why: cancelling a
    // ReceiveAsync aborts the whole WebSocket (ManagedWebSocket's Aborted state is socket-wide,
    // not call-scoped), which would poison the connection PumpAsync is about to hand off to. A
    // pending host socket sends nothing while waiting, so state alone is enough to tell "gave up
    // or errored" apart from "still waiting" without ever touching the read stream.
    private static async Task<WebSocket?> WaitForJoinerAsync(WebSocket hostSocket, Task<WebSocket?> joined, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (hostSocket.State != WebSocketState.Open)
            {
                return null;
            }

            var completed = await Task.WhenAny(joined, Task.Delay(500, ct));
            if (completed == joined)
            {
                return await joined;
            }
        }

        return null;
    }

    private static async Task PumpAsync(WebSocket a, WebSocket b, CancellationToken ct)
    {
        var bufferA = new byte[16 * 1024];
        var bufferB = new byte[16 * 1024];
        var forwardAtoB = ForwardAsync(a, b, bufferA, ct);
        var forwardBtoA = ForwardAsync(b, a, bufferB, ct);
        await Task.WhenAny(forwardAtoB, forwardBtoA);

        await CloseQuietly(a);
        await CloseQuietly(b);
    }

    // See the "join" case's note: this task must NOT read from `socket` itself once handed off --
    // the host's PumpAsync owns that. Polls WebSocketState rather than a blocking receive for
    // exactly that reason.
    private static async Task WaitForSocketToCloseAsync(WebSocket socket, CancellationToken ct)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task ForwardAsync(WebSocket from, WebSocket to, byte[] buffer, CancellationToken ct)
    {
        try
        {
            while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
            {
                var result = await from.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                await to.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, ct);
            }
        }
        catch (WebSocketException)
        {
            // Either side dropped -- Task.WhenAny above ends the pump either way.
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task CloseQuietly(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "peer disconnected", CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        var result = await socket.ReceiveAsync(buffer, ct);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static Task SendTextAsync(WebSocket socket, string text, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
}
