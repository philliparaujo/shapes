using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// The one place allowed to touch GameState directly. Scenes submit GameActions through
// Submit and read the resulting StateDiff; nothing else mutates state. This is PLAN.md's A2
// adapter: "UI only ever submits GameActions and never mutates state; the engine's reply is
// a view-model the scenes bind to."
public sealed class GameSession
{
    private GameState _state;

    public CardDatabase Cards { get; }

    public GameState State => _state;

    public GameSession(RuleSet rules, CardDatabase cards, IRandomSource random, PlayerId firstPlayer)
    {
        Cards = cards;
        _state = new GameState(rules, random, firstPlayer);
    }

    // Deals starting hands, applies the second-seat compensation (Phase 4 step 8), and enters
    // the Actions phase for turn one -- mirroring Shapes.Console's setup (Program.cs) so a
    // seeded Godot game matches the seeded console game, per Milestone A's exit bar.
    // `deck` defaults to DeckBuilder.Default (one of every card) -- the same deck the console
    // plays, so a seeded Godot game still matches the same seed's console result, which is
    // Milestone A's exit bar. The parameter is the seam C2's deckbuilder fills in.
    //
    // startingHandSize is now unused: GameSetup.Deal reads it from the ruleset, which is where it
    // always came from at every other call site. Kept in the signature so existing callers
    // compile unchanged, and asserted against the ruleset rather than silently ignored -- a
    // caller passing a different number is expressing an intent this no longer honours, and it
    // should say so rather than quietly deal a different hand.
    public void Start(int startingHandSize, Deck? deck = null) =>
        Start(startingHandSize, deck, deck);

    // Per-seat decks (PLAN.md C2): each seat is dealt its OWN decklist, which is what the
    // deckbuilder's per-seat lobby dropdowns select. Either may be null, meaning "the default
    // deck" -- so a human-picked deck can face the default without the caller building one.
    //
    // The symmetric overload above delegates here rather than the reverse, so there is exactly
    // one place that deals a game and the two cannot drift on setup order (GameSetup.Deal's own
    // header explains why that order is load-bearing and silent when wrong).
    public void Start(int startingHandSize, Deck? deckOne, Deck? deckTwo)
    {
        if (startingHandSize != _state.Rules.StartingHandSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingHandSize), startingHandSize,
                $"Ruleset '{_state.Rules.Name}' deals {_state.Rules.StartingHandSize} cards; "
                + "the opening hand size is a ruleset property and cannot be overridden here.");
        }

        DeckOne = deckOne ?? DeckBuilder.Default(Cards);
        DeckTwo = deckTwo ?? DeckBuilder.Default(Cards);
        GameSetup.Deal(_state, DeckOne, DeckTwo);
        _state.AdvanceToActions();
    }

    // The decklist each seat was dealt, once Start has run. Null before that -- a session that
    // has not started has no deck, and reading one would be a caller-ordering bug worth surfacing.
    public Deck? DeckOne { get; private set; }

    public Deck? DeckTwo { get; private set; }

    // Seat one's deck, kept as `Deck` for callers that predate per-seat decks and for the
    // symmetric case where both seats share a list. New callers should ask for the seat they
    // mean: an IS-MCTS agent determinizes against its OPPONENT's deck specifically, and under
    // per-seat decks reading the wrong one is exactly the size mismatch AgentFactory.Build warns
    // about -- silent, and fatal to the agent rather than to the game.
    public Deck? Deck => DeckOne;

    // The deck the given seat's OPPONENT is playing -- what AgentFactory.Build wants for that
    // seat's determinizer. Named for the question the caller is actually asking, so a call site
    // cannot quietly pass the agent its own decklist.
    public Deck? OpponentDeckOf(PlayerId seat) => seat == PlayerId.One ? DeckTwo : DeckOne;

    // Legal actions for whoever's turn it is right now. Mirrors ActionGenerator.Generate
    // exactly (including the AwaitingDiscard gate to discard-only) -- callers should not
    // special-case that gate themselves.
    public IReadOnlyList<GameAction> LegalActions() => ActionGenerator.Generate(_state, Cards);

    // Applies one action and returns what changed. Clones before applying because
    // ActionExecutor.Apply mutates GameState in place and there is no other snapshot
    // mechanism (GameState.Clone() IS the undo mechanism -- see PLAN.md A6).
    public StateDiff Submit(GameAction action)
    {
        var before = _state.Clone();
        ActionExecutor.Apply(_state, Cards, action);
        return StateDiff.Between(before, _state);
    }

    // PLAN.md C6: rebuilds a session to exactly the state a SavedMatch's action log left it in,
    // by starting fresh from the same seed and resubmitting every logged action in order --
    // the "replay" half of the seed-plus-action-log persistence choice (see SavedMatch's own
    // header for why that was picked over serializing GameState directly). Sound specifically
    // because Start's setup sequence and every Submit call are already deterministic given the
    // same seed and the same action list (Phase 1's determinism guarantee, the same property
    // MCTS and console/Godot seed parity already depend on) -- replaying is not a weaker
    // approximation of the original game, it reconstructs it exactly.
    //
    // The decks MUST be the ones the original game was dealt from. Replay reconstructs the game
    // by re-running the same seeded stream, and the deal is the first thing in that stream -- so
    // resuming a custom-deck game against the default decklist would shuffle a different deck,
    // deal a different opening hand, and then apply an action log describing cards that are no
    // longer in hand. That desync is silent at the seam and only surfaces as an unrelated-looking
    // failure several actions later, which is why SavedMatch persists both decklists rather than
    // letting them default here.
    public static GameSession Resume(
        RuleSet rules, CardDatabase cards, IRandomSource random, PlayerId firstPlayer,
        int startingHandSize, IReadOnlyList<GameAction> actions,
        Deck? deckOne = null, Deck? deckTwo = null)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var session = new GameSession(rules, cards, random, firstPlayer);
        session.Start(startingHandSize, deckOne, deckTwo);

        foreach (var action in actions)
        {
            session.Submit(action);
        }

        return session;
    }
}
