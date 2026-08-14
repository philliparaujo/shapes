using Shapes.Core.Primitives;
using Shapes.Core.Rules;

namespace Shapes.Core.State;

// The phase of a turn. Score and income resolve automatically at the start of a turn; the
// player only makes choices during Actions.
// Draw sits between Income and Actions: a card drawn at turn start is playable that same turn.
// (It used to happen at turn END, which meant a drawn card sat unusable until the next turn and
// the hand limit was enforced on the way out rather than on the way in.)
public enum TurnPhase
{
    Scoring = 0,
    Income = 1,
    Draw = 2,
    Actions = 3,
    Ended = 4,
}

public enum TurnEventKind
{
    CreaturePlayed = 0,
    CreatureDestroyed = 1,

    // A card drawn into an already-full hand and burned. Logged so the Phase 4 balance run can
    // measure how often the hand limit actually costs a player a card -- "resource flooding" in
    // the plan's metrics list -- rather than inferring it from hand sizes.
    CardBurned = 2,

    // A card drawn and actually kept (the CardBurned counterpart). Lets a batch runner build a
    // per-card "drawn this game" signal -- e.g. draw win-rate correlation -- without re-deriving
    // it from hand/deck diffs.
    CardDrawn = 3,

    // A player started their turn with an empty deck, handing their opponent fatigue score
    // (RuleSet.FatigueScorePerTurn). Player is the FATIGUED seat -- the one who ran out of cards
    // -- not the one who scored, so "how often did this seat deck out" reads directly off the log.
    // Logged rather than counted on a field because the event list already survives Clone() and is
    // already harvested per turn by the batch runner; a new mutable counter would have to be
    // threaded through the search's clone path for a number only the runner reads.
    Fatigued = 4,
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

    // Sends a destroyed creature's card(s) to its owner's discard pile and logs the destruction.
    //
    // THE one place a creature leaves the board by dying, called by every destruction path (the
    // post-action RemoveDead sweep and the `destroy`/`destroy_refund_cost` ops). Both halves --
    // discard and turn event -- belong together: a caller that logged the event but skipped the
    // discard would make the card physically vanish, which is the bug this method exists to
    // prevent.
    //
    // A card that was played has to BE somewhere. Before this existed, a destroyed creature was
    // simply dropped: not on the board, not in a hand, not in a deck, not in a discard. That is
    // invisible in ordinary play -- nothing reads a discard pile except the odd effect -- but it
    // breaks the card-conservation identity the determinizer's accounting rests on
    // (hand + deck + discard + board == the starting deck). Without it, subtracting a player's
    // visible cards from their known decklist over-counts what remains, and the determinizer
    // samples opponent hands containing cards that are actually dead. See
    // Shapes.Ai/Search/Determinizer.cs and Shapes.Tests/Mechanics/CardConservationTests.cs.
    //
    // MERGED creatures discard EVERY card folded into them, not just the surviving instance's
    // CardId: a merge of two cards is two physical cards occupying one slot, and both die at
    // once. MergedFrom is exactly that list (it holds the creature's own id even unmerged), which
    // is why this iterates it rather than discarding CardId.
    //
    // TOKENS discard nothing -- they were never cards. See CreatureInstance.IsToken.
    public void DestroyCreature(SlotIndex slot, CreatureInstance creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        RecordTurnEvent(TurnEventKind.CreatureDestroyed, slot.Owner, slot, creature.CardId);

        if (creature.IsToken)
        {
            return;
        }

        var owner = this[slot.Owner];
        foreach (var cardId in creature.MergedFrom)
        {
            owner.SendToDiscard(cardId);
        }
    }

    // Counts full rounds, incrementing when play returns to player one. Starts at 1.
    public int TurnNumber { get; private set; }

    // How many cards the active player still owes to a card-driven "discard N" effect.
    //
    // While this is above zero the action generator offers ONLY DiscardActions, so the turn
    // cannot proceed until the debt is paid. That is what turns discard into a player choice
    // without letting an effect ask a question mid-resolution: the op records the debt, resolution
    // finishes, and the choice is then expressed as ordinary legal actions the console renders and
    // the search expands like any other.
    //
    // Deliberately a bare count rather than a queue of pending effects: multiple discard effects
    // in one turn simply add up, and paying them one card at a time (PLAN.md's turn structure) is
    // indistinguishable from paying them per-effect. Overdraw does NOT come through here -- an
    // overdrawn card is burned automatically and is never a choice.
    public int PendingDiscards { get; private set; }

    public bool AwaitingDiscard => PendingDiscards > 0;

    // Called by the discard op. Adds to any debt already outstanding rather than replacing it.
    public void AddPendingDiscards(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        PendingDiscards += count;
    }

    // Called by the executor once a DiscardAction has actually removed a card.
    public void ConsumePendingDiscard()
    {
        if (PendingDiscards == 0)
        {
            throw new InvalidOperationException("No discard is pending.");
        }

        PendingDiscards--;
    }

    // Clears an unpayable debt: a "discard 2" with one card in hand discards that one and forgets
    // the rest. The alternative -- carrying the debt until cards arrive -- would let an effect
    // silently tax a future turn, and would deadlock the generator when the hand is empty, since
    // it offers nothing but DiscardActions while a debt is outstanding.
    public void ClearPendingDiscards() => PendingDiscards = 0;

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

    // Applies the second seat's starting compensation (PLAN.md step 4.8): extra resources, extra
    // cards, and extra score, all zero under the shipping ruleset.
    //
    // Lives here rather than in each caller's deal loop because there are three callers that set a
    // game up (Shapes.Sim's GameRunner, the console client, and the tests' fixtures) and a rule
    // that seat two is compensated is not one any of them should be able to forget -- a balance run
    // that silently skipped it would report a seat asymmetry that the ruleset had already fixed,
    // and the numbers would look entirely plausible.
    //
    // Called AFTER the caller has dealt opening hands and BEFORE the first AdvanceToActions(), so
    // the extra cards come off an already-shuffled deck and the extra resources are not overwritten
    // by turn one's income (income adds, so ordering is not strictly load-bearing for resources --
    // but the extra draw genuinely must follow SetDeck/ShuffleDeck). Returns the extra cards drawn
    // so a caller tracking draws for metrics can account for them the same way it accounts for the
    // opening hand; the console, which tracks nothing, ignores the return.
    //
    // Score is added directly rather than through the scoring step: this is a starting condition,
    // not something seat two earned by holding a slot, and routing it through ApplyScoring would
    // make it look like a scoring event to every metric that counts those.
    public IReadOnlyList<string> ApplySecondSeatCompensation()
    {
        var seatTwo = this[PlayerId.Two];

        seatTwo.GainResources(Rules.SecondSeatStartingResources);

        if (Rules.SecondSeatStartingScore > 0)
        {
            seatTwo.AddScore(Rules.SecondSeatStartingScore);
        }

        // Goes through DrawWithBurn rather than PlayerState.Draw so the extra cards respect the
        // hand limit exactly as every other draw in the game does -- the whole reason that method
        // is the single choke point (see DrawWithBurn). Under an unlimited hand limit, which the
        // shipping ruleset uses, the two are equivalent.
        return DrawWithBurn(PlayerId.Two, Rules.SecondSeatStartingCards);
    }

    private GameState(
        RuleSet rules, IRandomSource random, Board board, PlayerState[] players,
        PlayerId activePlayer, TurnPhase phase, int turnNumber, IEnumerable<TurnEvent> turnEvents,
        int pendingDiscards)
    {
        Rules = rules;
        Random = random;
        Board = board;
        _players = players;
        ActivePlayer = activePlayer;
        Phase = phase;
        TurnNumber = turnNumber;
        _turnEvents = [.. turnEvents];
        PendingDiscards = pendingDiscards;
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

    // Points a player would score right now: one per unopposed creature, or -- under
    // Rules.ScoreByCreatureDelta -- a pure board-presence race, max(0, own creatures - opponent's
    // creatures), which ignores per-slot opposition entirely. Pure -- callers use it to preview
    // scoring without applying it.
    public int PendingScore(PlayerId player) =>
        (Rules.ScoreByCreatureDelta ? CreatureDeltaScore(player) : UnopposedScore(player))
        * Rules.PointsPerUnopposedCreature;

    private int UnopposedScore(PlayerId player) =>
        SlotIndex.AllFor(player).Count(Board.IsUnopposed);

    private int CreatureDeltaScore(PlayerId player)
    {
        var own = SlotIndex.AllFor(player).Count(Board.IsOccupied);
        var opponent = SlotIndex.AllFor(player.Opponent()).Count(Board.IsOccupied);
        return Math.Max(0, own - opponent);
    }

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

    // What `move` actually costs `player` right now: its printed cost, or nothing when a
    // free-moves spell has discounted that move's type this turn.
    //
    // THE one place the discount is applied. ActionGenerator asks this to decide whether a move is
    // affordable and ActionExecutor asks it to charge -- two implementations of "what does this
    // cost" is exactly how a UI and an AI come to disagree about the rules, the same reasoning
    // that keeps legality in one place (see ActionExecutor's contract note).
    //
    // A move's type comes from its cost (MoveDefinition.AttackType), so a zero-cost move has no
    // type and is unaffected -- it was already free.
    public ResourcePool CostOfMove(PlayerId player, ResourcePool printedCost)
    {
        return printedCost.SingleType() is { } t && this[player].FreeMoveTypes.Has(t)
            ? ResourcePool.Empty
            : printedCost;
    }

    // True when the active player's empty deck is about to hand their opponent fatigue score.
    // Pure, so a caller can preview it the same way PendingScore/PendingIncome allow.
    public bool IsFatigued(PlayerId player) =>
        Rules.FatigueScorePerTurn > 0 && this[player].DeckIsEmpty;

    public void ApplyScoring()
    {
        Active.AddScore(PendingScore(ActivePlayer));

        // Fatigue resolves in the score step because that is the step it awards score in, and it
        // is checked BEFORE the draw that would have emptied the deck this turn -- "you started
        // your turn with no deck," not "you failed to draw." The opponent scores, not the active
        // player, so running out of cards is a cost rather than a reward.
        //
        // This is the game's only score source that does not require winning a slot, which is the
        // whole point: a board where neither side can kill anything stops scoring forever
        // otherwise (see RuleSet.FatigueScorePerTurn).
        if (IsFatigued(ActivePlayer))
        {
            this[ActivePlayer.Opponent()].AddScore(Rules.FatigueScorePerTurn);
            RecordTurnEvent(TurnEventKind.Fatigued, ActivePlayer, default, string.Empty);
        }

        Phase = TurnPhase.Income;
    }

    public void ApplyIncome()
    {
        Active.GainResources(PendingIncome(ActivePlayer));
        Active.GainResources(Active.ConsumePendingNextTurnResources());

        // Turn-start creature effects (Titan's scheduled heal) land here, alongside the
        // resource counterpart just above -- this is the first phase of the receiving player's
        // own turn, whereas ResetMovesForNewTurn ran as their PREVIOUS turn ended.
        foreach (var (_, creature) in Board.CreaturesOf(ActivePlayer))
        {
            creature.OnControllerTurnStart();
        }

        Phase = TurnPhase.Draw;
    }

    // Draws the turn's cards for the active player, burning any drawn over the hand limit.
    //
    // Draw is at turn START (after income, before actions) rather than at turn end: a card drawn
    // at the start is playable this turn, which is what makes the draw a live decision rather
    // than a deposit for next turn. Hearthstone's sequencing, and the reason the hand limit can
    // be enforced here rather than needing a separate end-of-turn step.
    //
    // Overdraw BURNS: a card drawn into a full hand goes straight to the discard pile and is
    // never a choice. That is deliberate and differs from the card-driven `discard` op, which IS
    // a choice (see PendingDiscards). The asymmetry is the Hearthstone/Slay-the-Spire rule --
    // you cannot save an overdrawn card by pitching a worse one, so a full hand is a real cost.
    // It also keeps the common path free of a pending state: overdraw happens constantly, and
    // routing it through the action generator would put a discard prompt in front of the player
    // most turns.
    public void ApplyDraw()
    {
        DrawWithBurn(ActivePlayer, Rules.CardsDrawnPerTurn);
        Phase = TurnPhase.Actions;
    }

    // Draws `count` cards for a player, burning any that arrive into a full hand.
    //
    // THE one place cards enter a hand from a deck. Every draw goes through here -- the turn
    // draw and every card effect that draws -- because the hand limit is a property of drawing,
    // not of the turn step: a card reading "draw 3" into a full hand burns all three, exactly
    // as the turn draw would. PlayerState.Draw cannot enforce this itself, since the limit lives
    // on RuleSet and PlayerState has no access to it; putting the rule here rather than at each
    // call site is what stops a future draw effect quietly bypassing it.
    //
    // Returns the cards that were KEPT, so a caller can tell a real draw from a burned one.
    public IReadOnlyList<string> DrawWithBurn(PlayerId player, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var kept = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var drawn = this[player].Draw();

            // Deck exhaustion is not fatal: the player simply draws nothing. Stopping rather
            // than continuing avoids counting a failed draw against the burn logic below.
            if (drawn is null)
            {
                break;
            }

            // Over the limit, so the card just drawn is the one burned -- not an older card.
            // Discarding the drawn card specifically is what makes this a burn rather than a
            // hand-limit discard the player might have preferred to direct.
            if (this[player].Hand.Count > Rules.HandLimit)
            {
                this[player].DiscardCard(drawn);
                RecordTurnEvent(TurnEventKind.CardBurned, player, default, drawn);
                continue;
            }

            kept.Add(drawn);
            RecordTurnEvent(TurnEventKind.CardDrawn, player, default, drawn);
        }

        return kept;
    }

    // Runs scoring, income, then draw for the active player, in that order, and leaves the state
    // ready for the Actions phase -- unless scoring just won the game, in which case Phase stops
    // at Ended and neither income nor draw runs. A no-op once the game is already in or past
    // Actions, so callers can invoke it unconditionally after EndTurn (or on a freshly
    // constructed game) without checking Phase themselves first. That "check Phase before calling
    // ApplyScoring" duplication is exactly what step 1.9 folds away -- see
    // ActionExecutor.ApplyEndTurn.
    //
    // Note the starting player draws on turn one, on top of the opening hand a caller dealt with
    // StartingHandSize -- the same as every later turn, and the same as Hearthstone's first
    // player. Turn one is not a special case here, which is the property step 1.9 was after.
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

        if (Phase == TurnPhase.Draw)
        {
            ApplyDraw();
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

        // "This turn, all [type] moves are free" expires with the turn that bought it. Cleared
        // here, alongside the per-turn creature reset, rather than at the next turn's start: this
        // is the same boundary, and clearing on the way out keeps a discount from ever being
        // visible to the opponent's turn in between.
        Active.ClearFreeMoves();

        ActivePlayer = ActivePlayer.Opponent();

        if (ActivePlayer == PlayerId.One)
        {
            TurnNumber++;
        }

        Phase = TurnPhase.Scoring;
        _turnEvents.Clear();

        // A discard debt does not survive the turn that created it. It should already be zero --
        // the generator will not offer EndTurn while one is outstanding -- but clearing here
        // means a state hand-built by a test or a replay cannot leak a debt onto the opponent,
        // who would then be gated into discarding for someone else's card.
        PendingDiscards = 0;
    }

    public void SetPhase(TurnPhase phase) => Phase = phase;

    // Restores the turn counter when a state is reconstructed rather than played -- the step 2.3
    // determinizer building a sampled world, and tests positioning a game mid-match.
    //
    // A direct setter rather than replaying EndTurn to advance the count: EndTurn also flips the
    // active player and calls ResetMovesForNewTurn on the board, which would erase the
    // once-per-turn move usage a reconstructed state is trying to reproduce.
    public void SetTurnNumber(int turnNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(turnNumber, 1);
        TurnNumber = turnNumber;
    }

    // Debug affordance for the console client's POV swap.
    public void SetActivePlayer(PlayerId player) => ActivePlayer = player;

    // The RNG is forked, not shared: a search rollout on a clone must not advance the real
    // game's randomness, or replaying the same seed would stop producing the same game.
    public GameState Clone() =>
        new(Rules, Random.Fork(), Board.Clone(), [_players[0].Clone(), _players[1].Clone()],
            ActivePlayer, Phase, TurnNumber, _turnEvents, PendingDiscards);

    public override string ToString() =>
        $"turn {TurnNumber} {Phase} active=P{ActivePlayer.ToIndex() + 1} "
        + $"[{this[PlayerId.One].Score}-{this[PlayerId.Two].Score}]";
}
