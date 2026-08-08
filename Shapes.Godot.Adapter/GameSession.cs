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
    public void Start(int startingHandSize)
    {
        foreach (var playerId in PlayerIds.All)
        {
            var player = _state[playerId];
            player.SetDeck(Cards.BuildSymmetricDeck(_state.Rules));
            player.ShuffleDeck(_state.Random);
            player.Draw(startingHandSize);
        }

        _state.ApplySecondSeatCompensation();
        _state.AdvanceToActions();
    }

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
}
