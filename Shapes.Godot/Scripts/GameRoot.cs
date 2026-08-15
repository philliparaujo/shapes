using System;
using System.Collections.Generic;
using System.IO;
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
// every scene a pure view: PLAN.md A2's "UI only ever submits GameActions and never mutates
// state" boundary, extended down to the scene tree.
//
// PLAN.md B1a: playing a card is drag-only now -- there is no tap-to-play fallback and no
// card-detail inspect panel. Full card/move detail is available on hover instead (PLAN.md
// B1a2, HoverDetailPanel) -- BoardView owns showing/hiding it directly since hover never
// submits a GameAction, so GameRoot never sees those events at all.
//
// PLAN.md C1 (AI-opponent wiring pulled forward from C5, 2026-08-09): either seat, both, or
// neither can be AI-controlled -- Lobby.cs builds the per-seat SeatConfig and GameRoot only
// ever sees "is this seat's IAgent null (human) or not."
//
// PLAN.md C5 (2026-08-09): AI turns run off the main thread and pace themselves, so an AI-v-AI
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
// PLAN.md C6 (interrupted-game persistence): seed + action log, not a serialized GameState --
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

    // Doubled from the original 0.6s (still comfortably past BoardAnimator's longest cue,
    // FloatSeconds at 0.55s) per explicit direction -- 0.6s read as too fast to actually follow
    // an AI-v-AI game action by action.
    private const double MoveDelaySeconds = 1.2;

    private GameSession? _session;
    private CardDatabase? _cards;
    private Dictionary<PlayerId, IAgent?> _agents = new();
    private readonly CancellationTokenSource _aiTurnToken = new();

    // Guards against RunAiTurns being entered twice concurrently -- Submit calls it after every
    // human action, and a fast double-submit (or a human action landing while a prior RunAiTurns
    // call is still mid-await) would otherwise start a second loop racing the first over the same
    // GameSession, which owns no locking of its own (PLAN.md A2: GameSession is the one place
    // allowed to touch GameState, not a place designed for concurrent callers).
    private bool _aiTurnInProgress;

    // PLAN.md C6: the two things GameSession.Resume needs alongside the action log, kept for
    // the lifetime of the match so every Submit can append-and-save without re-deriving them.
    private ulong _seed;
    private MatchConfig? _matchConfig;
    private readonly List<GameAction> _actionLog = [];

    private BoardView? _boardView;

    public override void _ExitTree()
    {
        _aiTurnToken.Cancel();
        _aiTurnToken.Dispose();
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

        // PLAN.md C6: Resume takes priority over a fresh MatchConfig -- Lobby only ever sets one
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

        StartNewGame(seed, config);
    }

    private void StartNewGame(ulong seed, MatchConfig? config)
    {
        var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
        _cards = CardLoader.FromDirectory(cardsDir);

        var rules = RuleSet.Default;
        var random = new SeededRandom(seed);
        _session = new GameSession(rules, _cards, random, PlayerId.One);

        // Per-seat decks from the lobby's deck dropdowns (PLAN.md C2). Null for either seat means
        // the default deck, so a config from before decks existed -- or one where the player left
        // the dropdowns alone -- deals exactly as it always did.
        _session.Start(rules.StartingHandSize, config?.DeckOne, config?.DeckTwo);

        _seed = seed;
        _matchConfig = config;
        _actionLog.Clear();

        AssignAvatars(seed);
        BuildAgents(seed, config);

        RefreshAll();
        RunAiTurns();
    }

    // PLAN.md C6: rebuilds via GameSession.Resume (seed replayed through Start + every logged
    // action, in order) instead of GameSession's plain constructor -- see SavedMatch/
    // GameSession.Resume's own headers for why replay reproduces the exact original state
    // rather than an approximation of it.
    private void ResumeGame(SavedMatch saved)
    {
        var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
        _cards = CardLoader.FromDirectory(cardsDir);

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

        // Same seed as the interrupted game, so it resumes wearing the same two faces -- the
        // avatars are not in the save file, they are re-derived (see AvatarPicker's header).
        AssignAvatars(saved.Seed);
        BuildAgents(saved.Seed, config);

        RefreshAll();
        RunAiTurns();
    }

    // Gives each seat a creature's art as its portrait, the two of different resource types
    // (PLAN.md 5.C-UI) -- see CardArt.AvatarCandidates for why those two filters.
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

    // Derived per-seat streams, not one shared source -- two agents drawing from a single
    // IRandomSource would interleave their draws, so changing one seat's agent would change
    // the other's decisions too (same reasoning as Shapes.Console's BuildAgent call site).
    // Shared between StartNewGame and ResumeGame so the two paths cannot drift on how an
    // agent's own RNG stream is derived from the match seed.
    private void BuildAgents(ulong seed, MatchConfig? config)
    {
        // The deck each agent's OPPONENT is playing -- asked for PER SEAT, because the two seats
        // can now be playing different decklists (PLAN.md C2). It must be passed, because an
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

    // PLAN.md C6: called after every action lands (Submit, RunAiTurns) so the on-disk save is
    // never more than one action stale -- the only save that actually survives a mobile OS
    // killing this process with no shutdown hook. A human-only or PlayerOne-null MatchConfig
    // still saves fine: SeatConfig.Human round-trips through SavedMatch/ActionDto exactly like
    // an AI seat, and Resume only needs it to rebuild the agent dictionary, not to decide
    // whether to save at all.
    [Export] public string LobbyScenePath { get; set; } = "res://Scenes/Lobby.tscn";

    // ESC opens the pause menu (PLAN.md 5.C-UI). Handled here rather than in BoardView because
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

        // Never over a finished game: its menu has no Resume, so re-opening would be a no-op that
        // still swallowed the keypress.
        if (_session is null || _session.State.IsOver || _boardView!.IsMenuOpen)
        {
            return;
        }

        _boardView.OpenPauseMenu();
        GetViewport().SetInputAsHandled();
    }

    // PLAN.md C6: the save is deliberately LEFT behind. Walking away mid-match is exactly the
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

    private void RefreshAll()
    {
        if (_session is null || _cards is null || _boardView is null)
        {
            return;
        }

        var legalActions = _session.LegalActions();
        _boardView.Render(_session.State, _cards, legalActions);

        if (_session.State.IsOver)
        {
            _boardView.ShowGameOver(_session.State.Winner);

            // PLAN.md C6: a finished game has nothing left to resume -- leaving the save behind
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
    // synchronous call as it did when this was a plain while loop (PLAN.md C1's cut).
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

                var diff = _session.Submit(action);
                _actionLog.Add(action);
                SaveProgress();
                RefreshAll();
                _boardView.PlayAnimation(diff, _session.State.ActivePlayer);

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

    // A hand card dropped directly on a board slot (PLAN.md B1a) -- the drag already supplied
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

    // A friendly creature dropped onto another friendly slot (PLAN.md B1a) -- replaces the old
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

    // A targetless spell dropped anywhere on the self panel's background (PLAN.md B1a) rather
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

        // A slot tap outside targeting is otherwise a no-op (PLAN.md B1a): playing a card is
        // drag-only (OnCardDroppedOnSlot), using a move is a tap on that move's own
        // always-visible button (OnMoveChosen), and merge is a drag (OnCreatureDroppedOnSlot),
        // so a bare tap on a slot has nothing left to do.
    }

    // A move's own always-visible board button was tapped (PLAN.md B1a, replacing the old
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

    private void Submit(GameAction action)
    {
        if (_session is null)
        {
            return;
        }

        // The StateDiff A2 built this whole adapter to produce, finally consumed (PLAN.md B1d).
        // Captured BEFORE RefreshAll, because it describes the transition into the state that
        // RefreshAll is about to draw -- and played after, so the cues land over the new board.
        var diff = _session.Submit(action);
        _actionLog.Add(action);
        SaveProgress();
        _boardView!.ClearSelection();
        RefreshAll();
        _boardView.PlayAnimation(diff, _session.State.ActivePlayer);

        // A human action can hand the turn straight to an AI seat (or all the way back to the
        // same human, if the action didn't end the turn) -- RunAiTurns is the one place that
        // decides whose move it is next, so every path that changes state runs through it rather
        // than each UI handler re-deciding "was that seat human."
        RunAiTurns();
    }
}
