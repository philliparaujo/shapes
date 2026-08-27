using Shapes.Core.Actions;

namespace Shapes.Godot.Adapter;

// DESIGN.md D5: "define IMatchTransport (queue / send action / receive action / disconnected) with
// a LocalTransport covering today's hotseat and AI games. Every item above is then buildable and
// testable with no server running at all." Today's hotseat/vs-AI modes need no implementation of
// this at all -- GameRoot simply has no transport wired for them, which already IS the local
// behaviour the plan names; only a network match constructs one (RelayMatchTransport).
//
// One action in, one action out, one disconnect notification -- nothing here knows about seats,
// legality, or turn order. GameRoot already has everything else a remote seat needs: D1's
// ViewerMode.Fixed plus the `Viewer != state.ActivePlayer` guard in Submit is "wait your turn",
// and GameSession.Submit/StateDiff is the same apply path a local action already takes. A
// transport's whole job is getting a GameAction from one process to the other.
public interface IMatchTransport : IAsyncDisposable
{
    // Raised when the peer's chosen action arrives. GameRoot applies it exactly like a local
    // Submit (clone-before, GameSession.Submit, log, recap, RefreshAll, PlayAnimation) but skips
    // the "is this my seat" guard -- the action arrived BECAUSE it was the peer's legal turn; the
    // peer's own GameSession already validated it before sending, the same trust RunAiTurns
    // already places in whatever agent.Choose returns.
    event Action<GameAction>? ActionReceived;

    // Raised once, when the peer's connection is lost for any reason (closed, network error).
    // Not raised for a disposal this side initiated itself.
    event Action? PeerDisconnected;

    // Sends one locally-submitted action to the peer. Fire-and-forget from GameRoot's point of
    // view (awaited, but a failure surfaces as PeerDisconnected rather than an exception GameRoot
    // has to handle inline -- the local action has already been applied to this side's GameSession
    // by the time SendAsync runs, so there is nothing to roll back on a send failure).
    Task SendAsync(GameAction action);
}
