using System;
using System.IO;
using System.Linq;
using Godot;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Owns the one GameSession for a hotseat game and is the only script that submits
// GameActions. Sub-views never touch GameSession directly -- they raise Godot signals
// (SlotTapped, EndTurnRequested, ...) that GameRoot listens to and turns into
// GameSession.Submit calls, then pushes the resulting StateDiff/legal-action list back down
// to RefreshAll. This keeps every scene a pure view: PLAN.md A2's "UI only ever submits
// GameActions and never mutates state" boundary, extended down to the scene tree.
//
// PLAN.md B1a: playing a card is drag-only now -- there is no tap-to-play fallback and no
// card-detail inspect panel. Full card/move detail is available on hover instead (PLAN.md
// B1a2, HoverDetailPanel) -- BoardView owns showing/hiding it directly since hover never
// submits a GameAction, so GameRoot never sees those events at all.
public partial class GameRoot : Control
{
    [Export] public NodePath BoardViewPath { get; set; } = "BoardView";
    [Export] public ulong Seed { get; set; }

    private GameSession? _session;
    private CardDatabase? _cards;

    private BoardView? _boardView;

    public override void _Ready()
    {
        _boardView = GetNode<BoardView>(BoardViewPath);

        _boardView.SlotTapped += OnSlotTapped;
        _boardView.MoveChosen += OnMoveChosen;
        _boardView.CardDroppedOnSlot += OnCardDroppedOnSlot;
        _boardView.CreatureDroppedOnSlot += OnCreatureDroppedOnSlot;
        _boardView.SpellDroppedOnSelfArea += OnSpellDroppedOnSelfArea;
        _boardView.EndTurnRequested += OnEndTurnRequested;
        _boardView.DiscardRequested += OnDiscardRequested;

        StartNewGame(Seed == 0 ? (ulong)DateTime.UtcNow.Ticks : Seed);
    }

    private void StartNewGame(ulong seed)
    {
        var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
        _cards = CardLoader.FromDirectory(cardsDir);

        var rules = RuleSet.Default;
        var random = new SeededRandom(seed);
        _session = new GameSession(rules, _cards, random, PlayerId.One);
        _session.Start(rules.StartingHandSize);

        RefreshAll();
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
        _boardView!.ClearSelection();
        RefreshAll();
        _boardView.PlayAnimation(diff, _session.State.ActivePlayer);
    }
}
