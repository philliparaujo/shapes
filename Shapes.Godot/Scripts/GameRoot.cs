using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Shapes.Ai.Agents;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Owns the one GameSession for a match and is the only script that submits GameActions.
// Sub-views never touch GameSession directly -- they raise Godot signals (SlotTapped,
// EndTurnRequested, ...) that GameRoot listens to and turns into GameSession.Submit calls,
// then pushes the resulting StateDiff/legal-action list back down to RefreshAll. This keeps
// every scene a pure view: DESIGN.md A2's "UI only ever submits GameActions and never mutates
// state" boundary, extended down to the scene tree.
//
// DESIGN.md B1a: playing a card is drag-only now -- there is no tap-to-play fallback and no
// card-detail inspect panel. Full card/move detail is available on hover instead (DESIGN.md
// B1a2, HoverDetailPanel) -- BoardView owns showing/hiding it directly since hover never
// submits a GameAction, so GameRoot never sees those events at all.
//
// DESIGN.md C1 (AI-opponent wiring pulled forward from C5, 2026-08-09): either seat, both, or
// neither can be AI-controlled -- Lobby.cs builds the per-seat SeatConfig and GameRoot only
// ever sees "is this seat's IAgent null (human) or not."
//
// DESIGN.md C5 (2026-08-09): AI turns run off the main thread and pace themselves, so an AI-v-AI
// game is actually watchable instead of the loop running to completion in one synchronous frame
// (the earlier C1 cut) and only the final game-over screen ever rendering. RunAiTurns is now
// `async void` -- the correct shape for a Godot event-loop entry point with no caller awaiting
// it (same category as a signal handler) -- and does two things neither Choose nor Submit did
// before: `await Task.Run(...)` moves the actual search off the UI thread, so a multi-second
// IS-MCTS budget no longer freezes input/rendering while it runs; and `await` on a
// `SceneTreeTimer` after every action adds a fixed MoveDelaySeconds pause, so Random/Greedy
// (whose Choose returns near-instantly) don't blur past faster than the board can animate.
// `_aiTurnToken` is cancelled in _ExitTree so a search in flight when the scene is torn down
// does not resume on the main thread afterward and touch freed nodes.
//
// DESIGN.md C6 (interrupted-game persistence): seed + action log, not a serialized GameState --
// see SavedMatch's own header in Shapes.Godot.Adapter for why. `_actionLog` mirrors exactly
// what every Submit call (human or AI) has applied so far, `_matchConfig`/`_seed` are what
// GameSession.Resume needs to rebuild from scratch, and MatchSaveStore.Save is called after
// EVERY action, not just on pause/quit -- a mobile OS can kill this process with no shutdown
// hook, so the only save that survives that is one kept continuously up to date. Cleared on
// game-over (RefreshAll), since a finished game has nothing left to resume.
public partial class GameRoot : Control
{
    [Export] public NodePath BoardViewPath { get; set; } = "BoardView";
    [Export] public ulong Seed { get; set; }

    // The beat between two AI actions. 0.6 -> 1.2 -> 2.4, each step per explicit direction after
    // watching a game: 0.6s read as too fast to follow an AI-v-AI game action by action, and 1.2s
    // was still too fast once D2 gave each action something to READ (the recap panel below holds a
    // card for RecapHoldSeconds, and a beat shorter than that would replace an entry before it had
    // been looked at).
    //
    // An [Export] rather than a const (DESIGN.md D2 item 1): this is a feel value, and the only way
    // to tune feel is to watch it, so it is settable from the editor's inspector without a rebuild.
    // Still applied uniformly across every agent -- see RunAiTurns on why the rhythm of watching
    // must not change with which agent happens to be playing.
    [Export] public double MoveDelaySeconds { get; set; } = 2.4;

    private GameSession? _session;
    private CardDatabase? _cards;
    private Dictionary<PlayerId, IAgent?> _agents = new();
    private readonly CancellationTokenSource _aiTurnToken = new();

    // DESIGN.md D2 item 5. Distinct from `_actionLog` below, which is C6's REPLAY log -- a bare list
    // of GameActions that GameSession.Resume re-applies to rebuild a match. This one is for
    // READING: it pairs each action with the StateDiff it produced, which is the only place the
    // effects (damage, scoring, resource spend) exist in a form anything can render. Keeping them
    // separate rather than widening the replay log matters because SavedMatch serializes that one,
    // and a save file must not grow a rendering concern it has no use for.
    private readonly ActionLog _readableLog = new();

    // DESIGN.md D2 item 3. Keeps a used move marked until that seat's OWN next turn, which the
    // engine's own flag cannot do -- it clears at the owner's turn END, so the marking would
    // disappear precisely while the opponent is acting. See SpentMoveTracker's header.
    private readonly SpentMoveTracker _spentMoves = new();

    // Whose seat the screen is drawn from (DESIGN.md D1) -- resolved per Render from the two seat
    // configs, never stored as a seat, so it cannot drift from the match it describes. Defaults to
    // hotseat, matching the fallback everywhere else here: a GameRoot.tscn opened directly in the
    // editor has no MatchConfig and is a two-human game, which is the mode that flips.
    private ViewerMode _viewerMode = ViewerMode.Hotseat;

    // Guards against RunAiTurns being entered twice concurrently -- Submit calls it after every
    // human action, and a fast double-submit (or a human action landing while a prior RunAiTurns
    // call is still mid-await) would otherwise start a second loop racing the first over the same
    // GameSession, which owns no locking of its own (DESIGN.md A2: GameSession is the one place
    // allowed to touch GameState, not a place designed for concurrent callers).
    private bool _aiTurnInProgress;

    // DESIGN.md C6: the two things GameSession.Resume needs alongside the action log, kept for
    // the lifetime of the match so every Submit can append-and-save without re-deriving them.
    private ulong _seed;
    private MatchConfig? _matchConfig;
    private readonly List<GameAction> _actionLog = [];

    private BoardView? _boardView;

    // DESIGN.md D5: set only for a network match (PendingMatch.Transport), null for every local
    // mode -- its presence is what tells Submit/RunAiTurns/SaveProgress "this game has a remote
    // peer" without a separate boolean that could disagree with it. Owned here rather than by
    // Lobby because GameRoot is already the one place allowed to drive a match end-to-end (DESIGN.md
    // A2), and a transport's lifetime is exactly the match's lifetime.
    private IMatchTransport? _transport;

    public override async void _ExitTree()
    {
        _aiTurnToken.Cancel();
        _aiTurnToken.Dispose();

        // Disposes the socket (and its receive loop) when the scene is torn down, whether that is
        // a real disconnect, backing out to the lobby, or quitting -- otherwise a network match
        // left mid-game leaks an open ClientWebSocket and its background receive Task.
        if (_transport is not null)
        {
            await _transport.DisposeAsync();
        }
    }

    public override void _Ready()
    {
        GodotTextFormat.Ensure();
        _boardView = GetNode<BoardView>(BoardViewPath);

        _boardView.SlotTapped += OnSlotTapped;
        _boardView.MoveChosen += OnMoveChosen;
        _boardView.CardDroppedOnSlot += OnCardDroppedOnSlot;
        _boardView.CreatureDroppedOnSlot += OnCreatureDroppedOnSlot;
        _boardView.SpellDroppedOnSelfArea += OnSpellDroppedOnSelfArea;
        _boardView.EndTurnRequested += OnEndTurnRequested;
        _boardView.DiscardRequested += OnDiscardRequested;
        _boardView.BackToLobbyRequested += OnBackToLobbyRequested;
        _boardView.ExitRequested += OnExitRequested;

        // Pulled on open rather than pushed on every action (DESIGN.md D2 item 5): the overlay is
        // closed for nearly the whole match, so nothing should be spent keeping its nodes current
        // while nobody is looking at it.
        _boardView.ActionLogSource = () => _readableLog.Entries;

        // DESIGN.md C6: Resume takes priority over a fresh MatchConfig -- Lobby only ever sets one
        // of the two before changing scene (see Lobby.OnResumePressed/OnStartPressed), but
        // checking Resume first means a stray Config left over from a previous run can never
        // silently override an explicit resume request.
        if (PendingMatch.ResumeRequested)
        {
            PendingMatch.ResumeRequested = false;
            PendingMatch.Config = null;

            var saved = MatchSaveStore.Load();
            if (saved is not null)
            {
                ResumeGame(saved);
                return;
            }

            // Load failed (corrupt/missing file racing the Lobby's own Exists check) -- fall
            // through to a fresh two-human hotseat game rather than a blank, stuck screen.
        }

        // Set by Lobby.cs immediately before ChangeSceneToFile; null only if GameRoot.tscn was
        // opened/run directly (e.g. mid-development in the editor) rather than reached through
        // the lobby -- falls back to two-human hotseat, A-milestone's original mode, rather than
        // failing to start.
        var config = PendingMatch.Config;
        PendingMatch.Config = null;
        var seed = config?.Seed ?? (Seed == 0 ? (ulong)DateTime.UtcNow.Ticks : Seed);

        // DESIGN.md D5: a live, already-paired transport means Lobby ran a Host/Join flow -- picked
        // up here the same way Config is, since Godot has no other way to carry a constructed
        // object across ChangeSceneToFile (PendingMatch's own header).
        _transport = PendingMatch.Transport;
        PendingMatch.Transport = null;

        StartNewGame(seed, config);
    }

    private void StartNewGame(ulong seed, MatchConfig? config)
    {
        _cards = ContentLoader.LoadCards();

        var rules = RuleSet.Default;
        var random = new SeededRandom(seed);
        _session = new GameSession(rules, _cards, random, PlayerId.One);

        // Per-seat decks from the lobby's deck dropdowns (DESIGN.md C2). Null for either seat means
        // the default deck, so a config from before decks existed -- or one where the player left
        // the dropdowns alone -- deals exactly as it always did.
        _session.Start(rules.StartingHandSize, config?.DeckOne, config?.DeckTwo);

        _seed = seed;
        _matchConfig = config;
        _actionLog.Clear();

        AssignAvatars(seed);
        AssignDeckNames();
        BuildAgents(seed, config);
        WireTransport();

        RefreshAll();
        RunAiTurns();
    }

    // DESIGN.md C6: rebuilds via GameSession.Resume (seed replayed through Start + every logged
    // action, in order) instead of GameSession's plain constructor -- see SavedMatch/
    // GameSession.Resume's own headers for why replay reproduces the exact original state
    // rather than an approximation of it.
    private void ResumeGame(SavedMatch saved)
    {
        _cards = ContentLoader.LoadCards();

        var rules = RuleSet.Default;
        var random = new SeededRandom(saved.Seed);
        // The decks the interrupted game was dealt from -- null for a save written before decks
        // existed, which resumes on the default deck exactly as that game was played. Replay
        // re-runs the deal through the same seeded stream, so passing the wrong decks here
        // desyncs the whole action log; see GameSession.Resume's note.
        var deckOne = saved.DeckOne?.ToDeck();
        var deckTwo = saved.DeckTwo?.ToDeck();

        _session = GameSession.Resume(
            rules, _cards, random, PlayerId.One, rules.StartingHandSize, saved.Actions,
            deckOne, deckTwo);

        _seed = saved.Seed;
        var config = new MatchConfig(
            saved.PlayerOne, saved.PlayerTwo, saved.Seed, deckOne, deckTwo);
        _matchConfig = config;
        _actionLog.Clear();
        _actionLog.AddRange(saved.Actions);

        // The READABLE log (DESIGN.md D2 item 5) starts empty on a resume, and deliberately so.
        // GameSession.Resume replays the saved actions internally, below both submit seams, so no
        // StateDiff is produced for any of them -- and a diff is the only place an action's EFFECTS
        // exist. Reconstructing them would mean replaying the match a second time out here purely
        // to observe it, which is real work to recover a scrollback nobody was reading at the
        // moment they were interrupted. The log fills from the resumed game's next action onward.
        _readableLog.Clear();

        // Same reasoning: the replayed actions never pass the seams that feed this, and a resumed
        // game starts on a fresh turn anyway, so there is nothing legitimate to still be marking.
        _spentMoves.Clear();

        // Same seed as the interrupted game, so it resumes wearing the same two faces -- the
        // avatars are not in the save file, they are re-derived (see AvatarPicker's header).
        AssignAvatars(saved.Seed);
        AssignDeckNames();
        BuildAgents(saved.Seed, config);

        RefreshAll();
        RunAiTurns();
    }

    // Gives each seat a creature's art as its portrait, the two of different resource types
    // (DESIGN.md 5.C-UI) -- see CardArt.AvatarCandidates for why those two filters.
    //
    // Seed-derived rather than random, and picked here rather than in BoardView, for the reasons
    // in AvatarPicker's header -- the short version being that a resumed match must not change
    // faces, and the game's own IRandomSource must not be touched.
    //
    // Called from both StartNewGame and ResumeGame, and before RefreshAll in each, so the very
    // first Render already has the portraits rather than drawing a frame of bare placeholder.
    private void AssignAvatars(ulong seed)
    {
        if (_cards is null || _boardView is null)
        {
            return;
        }

        var (one, two) = AvatarPicker.Pick(seed, CardArt.AvatarCandidates(_cards));
        _boardView.SetAvatars(
            one is null ? null : CardArt.TextureFor(one),
            two is null ? null : CardArt.TextureFor(two));
    }

    // Reads each seat's deck NAME off the live session, not off MatchConfig -- the config's deck
    // is null for "default" (the same null-means-default convention GameSession.Start uses), so
    // its Name would need re-deriving here; GameSession already resolved that null the moment
    // Start/Resume ran (GameSession.DeckOne/DeckTwo fall back to DeckBuilder.Default internally),
    // which is the one place this project wants that fallback decided. Called after _session
    // exists in both StartNewGame and ResumeGame, alongside AssignAvatars, so the very first
    // Render already has both names rather than a blank label for one frame.
    private void AssignDeckNames()
    {
        if (_session is null || _boardView is null)
        {
            return;
        }

        _boardView.SetDeckNames(
            _session.DeckOne?.Name ?? DeckBuilder.DefaultDeckName,
            _session.DeckTwo?.Name ?? DeckBuilder.DefaultDeckName);
    }

    // Derived per-seat streams, not one shared source -- two agents drawing from a single
    // IRandomSource would interleave their draws, so changing one seat's agent would change
    // the other's decisions too (same reasoning as Shapes.Console's BuildAgent call site).
    // Shared between StartNewGame and ResumeGame so the two paths cannot drift on how an
    // agent's own RNG stream is derived from the match seed.
    private void BuildAgents(ulong seed, MatchConfig? config)
    {
        // Perspective is decided from the same config, in the same place, as the agents (DESIGN.md
        // D1) -- the two answer one question between them ("which seats are human, and which of
        // them is looking at this screen"), and deriving them apart is how they would drift. A
        // null config is the direct-from-editor case: two humans, hotseat, board flips.
        //
        // Routed through config.Viewer (not ViewerMode.For directly) so a network match's
        // ViewerOverride (DESIGN.md D5) actually takes effect here -- MatchConfig.Viewer is the one
        // place that decision was meant to live; reading ViewerMode.For directly would silently
        // bypass it for exactly the case it exists to handle.
        _viewerMode = config?.Viewer ?? ViewerMode.Hotseat;

        // The deck each agent's OPPONENT is playing -- asked for PER SEAT, because the two seats
        // can now be playing different decklists (DESIGN.md C2). It must be passed, because an
        // IS-MCTS agent without it determinizes against the ruleset's SYMMETRIC decklist instead,
        // which is CopiesPerCard (2) of every card and therefore twice the size of the deck
        // actually in play. See AgentFactory.Build's note: the size disagreement makes
        // Determinizer.RestoreOpponent throw, and RunAiTurns being `async void` turns that into
        // "the AI silently stops taking turns" rather than a visible error.
        //
        // Passing the agent its OWN deck instead would be the same class of failure whenever the
        // decks differ in composition -- the determinizer would rule out cards the opponent can
        // actually hold -- which is why GameSession exposes OpponentDeckOf rather than leaving
        // each call site to pick a side. Called after _session is assigned in both StartNewGame
        // and ResumeGame, so both decks are set.
        _agents = new Dictionary<PlayerId, IAgent?>
        {
            [PlayerId.One] = config is null
                ? null
                : AgentFactory.Build(
                    config.PlayerOne, seed * 7919, _cards!, _session?.OpponentDeckOf(PlayerId.One)),
            [PlayerId.Two] = config is null
                ? null
                : AgentFactory.Build(
                    config.PlayerTwo, seed * 104729, _cards!, _session?.OpponentDeckOf(PlayerId.Two)),
        };
    }

    // DESIGN.md C6: called after every action lands (Submit, RunAiTurns) so the on-disk save is
    // never more than one action stale -- the only save that actually survives a mobile OS
    // killing this process with no shutdown hook. A human-only or PlayerOne-null MatchConfig
    // still saves fine: SeatConfig.Human round-trips through SavedMatch/ActionDto exactly like
    // an AI seat, and Resume only needs it to rebuild the agent dictionary, not to decide
    // whether to save at all.
    [Export] public string LobbyScenePath { get; set; } = "res://Scenes/Lobby.tscn";

    // ESC opens the pause menu (DESIGN.md 5.C-UI). Handled here rather than in BoardView because
    // this node owns the match and the save; BoardView only owns the panel that displays it.
    //
    // _UnhandledInput, not _Input: a control with focus (the End Turn button, a card) gets first
    // refusal, so ESC cannot steal a key another widget is legitimately using.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            return;
        }

        if (_session is null)
        {
            return;
        }

        // The Rules overlay opens OVER the pause menu, so ESC closes just the overlay first --
        // otherwise a press meant to back out of the tutorial would fall straight through to
        // whatever OpenPauseMenu's re-entry guard below decides, punching past the menu beneath it.
        if (_boardView!.IsTutorialOpen)
        {
            _boardView.CloseTutorial();
            GetViewport().SetInputAsHandled();
            return;
        }

        // DESIGN.md D4: the Sounds panel opens over the pause menu exactly as the tutorial does, so
        // it needs the same treatment and for the same reason. The two never open together (both
        // are reached from the menu beneath them), so their relative order here is arbitrary --
        // what matters is that both precede the menu itself.
        if (_boardView.IsSoundsOpen)
        {
            _boardView.CloseSounds();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Same topmost-first rule for the match log (DESIGN.md D2 item 5). Unlike the tutorial it
        // opens straight from the board rather than over the pause menu, so closing it reveals the
        // board -- but it still has to be tested BEFORE the menu, or ESC would open the pause menu
        // behind an already-open log.
        if (_boardView.IsActionLogOpen)
        {
            _boardView.CloseActionLog();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Never over a finished game: its menu has no Resume, so a second ESC has nothing to
        // toggle back to and must stay a no-op rather than dismissing the game-over screen.
        if (_session.State.IsOver)
        {
            return;
        }

        // A second ESC while paused closes the menu, same as pressing Resume -- ESC toggles
        // rather than only ever opening.
        if (_boardView.IsMenuOpen)
        {
            _boardView.ClosePauseMenu();
            GetViewport().SetInputAsHandled();
            return;
        }

        _boardView.OpenPauseMenu();
        GetViewport().SetInputAsHandled();
    }

    // DESIGN.md C6: the save is deliberately LEFT behind. Walking away mid-match is exactly the
    // interruption the action log exists to survive, so the Lobby's Resume button picks this game
    // back up. Only game-over clears it (see RefreshAll), because a finished game has nothing to
    // return to.
    private void OnBackToLobbyRequested()
    {
        _aiTurnToken.Cancel();
        GetTree().ChangeSceneToFile(LobbyScenePath);
    }

    private void OnExitRequested()
    {
        _aiTurnToken.Cancel();
        GetTree().Quit();
    }

    private void SaveProgress()
    {
        if (_session is null || _session.State.IsOver)
        {
            return;
        }

        // DESIGN.md D5: no local save for a network match. C6's resume replays the action log
        // against a peer that (for this minimal cut) no longer exists once the connection drops --
        // there is nothing to resume TO, only a game to end (see ShowDisconnected). Saving anyway
        // would surface a phantom "Resume" button in the lobby for a match this process can never
        // actually continue.
        if (_transport is not null)
        {
            return;
        }

        var config = _matchConfig ?? new MatchConfig(SeatConfig.Human, SeatConfig.Human, _seed);

        // Saved from the SESSION, not from the config: the config's decks are null for a default
        // deck, while the session holds the resolved Deck it actually shuffled and dealt. Saving
        // the resolved list means a resume reproduces this game even if the default deck itself
        // changes between launches (a card added to the set changes DeckBuilder.Default), which a
        // saved null would silently fail to do -- see SavedMatch's header on why the exact
        // pre-shuffle order is what has to survive.
        var saved = new SavedMatch(
            _seed, config.PlayerOne, config.PlayerTwo, [.. _actionLog],
            _session?.DeckOne is { } one ? SavedDeckList.Of(one) : null,
            _session?.DeckTwo is { } two ? SavedDeckList.Of(two) : null);

        MatchSaveStore.Save(saved);
    }

    // The seat the screen is currently drawn from (DESIGN.md D1). Resolved on each read rather than
    // cached, because under FollowsActive the answer changes with the turn -- caching it would
    // reintroduce the stale-perspective bug in a new place.
    private PlayerId Viewer =>
        _viewerMode.Resolve(_session?.State.ActivePlayer ?? PlayerId.One);

    // Read-only access for the in-editor harnesses (ViewerSeatShotHarness), which need to compare
    // what is drawn against what the engine actually holds. Deliberately not a general seam: this
    // node stays the only thing that submits actions, and nothing here lets a caller do that.
    public GameSession? SessionForTesting => _session;

    public PlayerId ViewerForTesting => Viewer;

    private void RefreshAll()
    {
        if (_session is null || _cards is null || _boardView is null)
        {
            return;
        }

        var legalActions = _session.LegalActions();
        // Drops markings for slots whose creature is gone (killed, merged away, replaced) -- done
        // here on the live state rather than modelled through destruction events, since a board
        // read covers all three routes at once.
        _spentMoves.ForgetEmptySlots(_session.State);

        _boardView.Render(_session.State, _cards, legalActions, Viewer, _spentMoves);

        if (_session.State.IsOver)
        {
            _boardView.ShowGameOver(_session.State.Winner);

            // DESIGN.md C6: a finished game has nothing left to resume -- leaving the save behind
            // would resurrect a dead game the next time the app launches into the lobby.
            MatchSaveStore.Clear();
        }
    }

    // Keeps calling the active seat's agent until control returns to a human seat or the game
    // ends -- one Choose() call per ACTION, not per turn, the same granularity
    // Shapes.Console's main loop uses, so a turn that needs several actions (e.g. discard down
    // to the hand limit, then play) plays out exactly as it would against a human at that seat.
    // Called after every state-changing event (StartNewGame, Submit) so an AI-v-AI game runs to
    // completion without any UI input, and a human-v-AI game hands back to the human the moment
    // it's their turn.
    //
    // async void, not async Task: this is an event-loop entry point (called from _Ready and from
    // Submit, neither of which awaits it), the same shape Godot signal handlers already take.
    // Each iteration awaits twice -- Task.Run for the search itself, then a SceneTreeTimer for
    // the pacing delay -- so between any two AI actions control fully returns to Godot's own
    // frame loop and BoardView actually redraws, instead of the whole game resolving inside one
    // synchronous call as it did when this was a plain while loop (DESIGN.md C1's cut).
    private async void RunAiTurns()
    {
        if (_session is null || _cards is null || _boardView is null || _aiTurnInProgress)
        {
            return;
        }

        _aiTurnInProgress = true;
        try
        {
            while (!_session.State.IsOver && _agents.TryGetValue(_session.State.ActivePlayer, out var agent)
                   && agent is not null)
            {
                var context = AgentContext.ForActivePlayer(_session.State, _cards);
                var token = _aiTurnToken.Token;

                // The search itself is the only unbounded-time part of a turn (Random/Greedy
                // return instantly regardless) and the only part that benefits from a thread --
                // it is a tight CPU loop with no I/O, so Task.Run's thread-pool hop is what
                // actually keeps Godot's own thread free to render/accept input while it runs.
                var action = await Task.Run(() => agent.Choose(context, token), token);

                if (!IsInstanceValid(this) || token.IsCancellationRequested)
                {
                    // The scene was torn down (or a new game started) while the search ran --
                    // _session/_boardView may already be disposed, so this action is simply
                    // dropped rather than applied to state nothing here still owns.
                    return;
                }

                // Cloned BEFORE Submit (DESIGN.md D2 items 2/4/5): both the recap and the log name
                // things the action may destroy -- a lethal move's own creature, a played card
                // that has left the hand -- so describing against the post-action state would
                // silently lose exactly the names worth showing. Same before/after reasoning
                // StateDiff itself is built on.
                var before = _session.State.Clone();

                var diff = _session.Submit(action);
                _actionLog.Add(action);
                _readableLog.Add(action, diff, before, _session.State, _cards);
                _spentMoves.Observe(action, before, _session.State);
                ShowRecapFor(action, before);
                SaveProgress();
                RefreshAll();

                // Oriented to the VIEWER, not the active player (DESIGN.md D1): BoardAnimator mirrors
                // its cues about the seat it is given, so passing the acting seat here would play
                // the AI's attack travelling up the screen -- away from the human it is aimed at --
                // now that the board no longer turns around to match.
                _boardView.PlayAnimation(diff, Viewer);
                PlaySoundsFor(action, diff);

                // Paces every agent uniformly, not just the fast ones -- an IS-MCTS search that
                // already took visible time still gets the same beat before the next action, so
                // the rhythm of watching a game doesn't change with which agent is playing.
                var timer = GetTree().CreateTimer(MoveDelaySeconds);
                await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);

                if (!IsInstanceValid(this))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A new game (or a teardown) cancelled the search mid-flight -- expected, not a fault.
        }
        catch (Exception ex)
        {
            // `async void` has no caller to observe a fault, so without this an agent throwing
            // (a determinizer mismatch, a bad action, anything) just ends this loop and the AI
            // silently never moves again -- the game looks frozen with no error anywhere. Godot's
            // own handler does print the exception, but only because the framework catches it at
            // the synchronization-context boundary; logging it HERE keeps the message attached to
            // the thing that actually broke, and the loop stays exited either way.
            GD.PushError($"AI turn failed, agent seats will stop acting: {ex}");
        }
        finally
        {
            _aiTurnInProgress = false;
        }
    }

    // A hand card dropped directly on a board slot (DESIGN.md B1a) -- the drag already supplied
    // the slot a tap-then-tap flow would otherwise need a separate step to collect.
    private void OnCardDroppedOnSlot(string cardId, SlotIndex slot)
    {
        if (_session is null)
        {
            return;
        }

        var legalPlays = _session.LegalActions().OfType<PlayCardAction>()
            .Where(a => a.CardId == cardId)
            .ToList();

        var placement = legalPlays.Where(a => a.TargetSlot == slot).ToList();

        var direct = placement.FirstOrDefault(a => a.ChosenTarget is null);
        if (direct is not null)
        {
            Submit(direct);
            return;
        }

        var placementWithTargets = placement.Where(a => a.ChosenTarget is not null).ToList();
        if (placementWithTargets.Count > 0)
        {
            // The creature's placement slot is now fixed by the drop; still needs a chosen_*
            // target (e.g. a play-effect on an enemy) before the play resolves -- falls back to
            // A5's tap-to-target UI for that last step, since a single drop can't supply both a
            // placement slot and a separate chosen target at once.
            _boardView!.BeginTargeting(placementWithTargets.Cast<GameAction>().ToList());
            RefreshAll();
            return;
        }

        // Not a creature placement here -- a spell that targets this exact slot (dropped
        // straight onto the enemy creature it targets, the single most natural drag gesture for
        // that card) resolves immediately rather than requiring a drop-then-tap-target sequence.
        var spellTargeting = legalPlays.FirstOrDefault(a => a.TargetSlot is null && a.ChosenTarget == slot);
        if (spellTargeting is not null)
        {
            Submit(spellTargeting);
            return;
        }

        // No placement or direct target uses this slot -- SlotView accepts any card drop so a
        // targetless spell dropped on top of a creature (not just empty board space) still
        // works, and a targeted spell dropped on the wrong slot still resolves via targeting
        // mode below rather than silently failing.
        var spellWithTargets = legalPlays.Where(a => a.TargetSlot is null && a.ChosenTarget is not null).ToList();
        if (spellWithTargets.Count > 0)
        {
            _boardView!.BeginTargeting(spellWithTargets.Cast<GameAction>().ToList());
            RefreshAll();
            return;
        }

        OnSpellDroppedOnSelfArea(cardId);
    }

    // A friendly creature dropped onto another friendly slot (DESIGN.md B1a) -- replaces the old
    // tap-slot-then-pick-"Merge into X"-from-a-menu path with one drag gesture.
    private void OnCreatureDroppedOnSlot(SlotIndex source, SlotIndex target)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<MergeAction>()
            .FirstOrDefault(a => a.SourceSlot == source && a.TargetSlot == target);

        if (action is not null)
        {
            Submit(action);
        }
    }

    // A targetless spell dropped anywhere on the self panel's background (DESIGN.md B1a) rather
    // than a specific slot, since it never occupies the board.
    private void OnSpellDroppedOnSelfArea(string cardId)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<PlayCardAction>()
            .FirstOrDefault(a => a.CardId == cardId && a.TargetSlot is null && a.ChosenTarget is null);

        if (action is not null)
        {
            Submit(action);
        }
    }

    private void OnSlotTapped(SlotIndex slot)
    {
        if (_session is null)
        {
            return;
        }

        if (_boardView!.IsTargeting)
        {
            var targeted = _boardView.TryResolveTarget(slot);
            if (targeted is not null)
            {
                Submit(targeted);
            }

            // A tap that misses every highlighted slot is ignored, not a cancel -- targeting
            // mode has its own explicit cancel (BoardView's "Cancel Targeting" button, wired
            // to ClearSelection); a stray miss-tap shouldn't silently drop the choice.
            return;
        }

        // A slot tap outside targeting is otherwise a no-op (DESIGN.md B1a): playing a card is
        // drag-only (OnCardDroppedOnSlot), using a move is a tap on that move's own
        // always-visible button (OnMoveChosen), and merge is a drag (OnCreatureDroppedOnSlot),
        // so a bare tap on a slot has nothing left to do.
    }

    // A move's own always-visible board button was tapped (DESIGN.md B1a, replacing the old
    // tap-slot-then-MoveMenu-popup flow).
    private void OnMoveChosen(SlotIndex source, int moveIndex)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<UseMoveAction>()
            .FirstOrDefault(a => a.SourceSlot == source && a.MoveIndex == moveIndex && a.ChosenTarget is null);

        if (action is not null)
        {
            Submit(action);
            return;
        }

        // Needs a chosen target (A5's territory) -- ask BoardView to enter targeting mode
        // for the legal ChosenTarget options this move offers.
        var withTargets = _session.LegalActions()
            .OfType<UseMoveAction>()
            .Where(a => a.SourceSlot == source && a.MoveIndex == moveIndex && a.ChosenTarget is not null)
            .ToList();
        if (withTargets.Count > 0)
        {
            _boardView!.BeginTargeting(withTargets.Cast<GameAction>().ToList());
            RefreshAll();
        }
    }

    private void OnEndTurnRequested()
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions().OfType<EndTurnAction>().FirstOrDefault();
        if (action is not null)
        {
            Submit(action);
        }
    }

    private void OnDiscardRequested(string cardId)
    {
        if (_session is null)
        {
            return;
        }

        var action = _session.LegalActions()
            .OfType<DiscardAction>()
            .FirstOrDefault(a => a.CardId == cardId);

        if (action is not null)
        {
            Submit(action);
        }
    }

    // DESIGN.md D4: the audio half of PlayAnimation, called from all three apply paths (local
    // Submit, RunAiTurns, DrainRemoteActions) so a card played by a human, an AI, or a remote peer
    // sounds identical. Kept as its own method rather than folded into the animation call because
    // the two answer different questions -- PlayAnimation needs the VIEWER's seat to orient a
    // cue's direction, while a sound has no direction to orient and so needs no seat at all.
    //
    // Sounds BOTH seats deliberately, matching the recap panel's own decision (ActionRecap's
    // header): hearing what the opponent just did is the point, not noise to be filtered out.
    private void PlaySoundsFor(GameAction action, StateDiff diff)
    {
        foreach (var cue in SoundScript.From(diff, action))
        {
            SoundFx.Play(cue);
        }
    }

    // DESIGN.md D2 items 2/4. Not every action earns a recap -- ActionRecap.For returns null for
    // EndTurn/Discard, which are either already obvious from the board or not worth interrupting
    // the panel for. Shown for both seats; see ActionRecap's header for that decision.
    private void ShowRecapFor(GameAction action, GameState before)
    {
        if (_cards is null || _boardView is null)
        {
            return;
        }

        if (ActionRecap.For(action, before, _cards) is { } recap)
        {
            _boardView.ShowRecap(recap);
        }
    }

    private void Submit(GameAction action)
    {
        if (_session is null)
        {
            return;
        }

        // Nothing may be submitted on a seat the viewer is not sitting in (DESIGN.md D1). Every
        // handler that reaches here already filtered against `LegalActions()`, but that list
        // belongs to the ACTIVE player, so during an AI turn a stray click could match a legal
        // action and submit it on the AI's behalf. Under FollowsActive the viewer IS the active
        // player and this never fires; under Fixed it is the whole of "wait your turn."
        //
        // This is also D4's generalization of `_aiTurnInProgress` to "not my turn," arriving early:
        // an action from a remote seat will be applied by a different path than local input, and
        // this guard is what keeps local input out of it.
        if (Viewer != _session.State.ActivePlayer)
        {
            return;
        }

        // The StateDiff A2 built this whole adapter to produce, finally consumed (DESIGN.md B1d).
        // Captured BEFORE RefreshAll, because it describes the transition into the state that
        // RefreshAll is about to draw -- and played after, so the cues land over the new board.
        //
        // The pre-action clone serves the recap and the readable log -- see RunAiTurns' copy of
        // this for why they cannot be built from the state Submit leaves behind.
        var before = _session.State.Clone();

        var diff = _session.Submit(action);
        _actionLog.Add(action);
        _readableLog.Add(action, diff, before, _session.State, _cards!);
        _spentMoves.Observe(action, before, _session.State);
        ShowRecapFor(action, before);
        SaveProgress();
        _boardView!.ClearSelection();
        RefreshAll();

        // The viewer's own seat, same as RunAiTurns -- see its note on why the animation is
        // oriented to who is WATCHING rather than to who acted.
        _boardView.PlayAnimation(diff, Viewer);
        PlaySoundsFor(action, diff);

        // DESIGN.md D5: tell the peer what just happened, once it has already been applied locally.
        // Fire-and-forget from this method's point of view -- a send failure surfaces later as
        // PeerDisconnected (raised by the transport's own receive loop noticing the socket died),
        // not as an exception here, because the local action is already committed and there is
        // nothing to roll back.
        if (_transport is not null)
        {
            _ = _transport.SendAsync(action);
        }

        // A human action can hand the turn straight to an AI seat (or all the way back to the
        // same human, if the action didn't end the turn) -- RunAiTurns is the one place that
        // decides whose move it is next, so every path that changes state runs through it rather
        // than each UI handler re-deciding "was that seat human." A no-op for a network match:
        // _agents is empty (both seats are SeatConfig.Human), so its while-loop condition never
        // matches -- the same "no agent for this seat" fallthrough hotseat already relies on.
        RunAiTurns();
    }

    // DESIGN.md D5: hooks a live transport into the same apply path Submit uses, minus the "is this
    // my seat" guard -- an action arriving here already passed the PEER's copy of that guard (its
    // own GameSession.LegalActions), the same trust RunAiTurns places in agent.Choose's output.
    // Called once per match from StartNewGame; a no-op when there is no transport (every local
    // mode), so this method's own body never runs for hotseat/vs-AI/resume.
    private void WireTransport()
    {
        if (_transport is null)
        {
            return;
        }

        _transport.ActionReceived += ApplyRemoteAction;
        _transport.PeerDisconnected += OnPeerDisconnected;
    }

    // Queued rather than applied inline: ActionReceived fires from the transport's background
    // receive loop, not Godot's main thread, and GameSession/BoardView are only safe to touch
    // from the main thread. CallDeferred can't carry an arbitrary C# object as an argument
    // (BoardView.PlayPendingAnimation's own note on this same constraint), so the action is
    // stashed in a field first and CallDeferred takes no arguments, same pattern.
    private readonly Queue<GameAction> _pendingRemoteActions = new();

    private void ApplyRemoteAction(GameAction action)
    {
        lock (_pendingRemoteActions)
        {
            _pendingRemoteActions.Enqueue(action);
        }

        CallDeferred(nameof(DrainRemoteActions));
    }

    private void DrainRemoteActions()
    {
        if (_session is null || _cards is null || _boardView is null)
        {
            return;
        }

        while (true)
        {
            GameAction action;
            lock (_pendingRemoteActions)
            {
                if (_pendingRemoteActions.Count == 0)
                {
                    return;
                }

                action = _pendingRemoteActions.Dequeue();
            }

            var before = _session.State.Clone();
            var diff = _session.Submit(action);
            _actionLog.Add(action);
            _readableLog.Add(action, diff, before, _session.State, _cards);
            _spentMoves.Observe(action, before, _session.State);
            ShowRecapFor(action, before);
            RefreshAll();
            _boardView.PlayAnimation(diff, Viewer);
            PlaySoundsFor(action, diff);
        }
    }

    private void OnPeerDisconnected()
    {
        CallDeferred(nameof(ShowDisconnectedIfStillPlaying));
    }

    private void ShowDisconnectedIfStillPlaying()
    {
        if (_session is null || _session.State.IsOver || _boardView is null)
        {
            return;
        }

        _boardView.ShowDisconnected();
    }
}
