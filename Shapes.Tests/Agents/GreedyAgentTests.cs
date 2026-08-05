using Shapes.Ai.Agents;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Agents;

// GreedyAgent's heuristic: the decisions it makes, not merely that it makes legal ones.
//
// The IAgent contract clauses (legality, non-mutation, determinism) are covered for this agent
// by AgentContractTests' theories, so this suite is only about play QUALITY -- the properties
// that make it a useful yardstick rather than a differently-shaped RandomAgent. If these pass and
// the contract tests pass, then a later comparison against IS-MCTS is measuring search rather
// than measuring a baseline that quietly does the wrong thing.
//
// Each test states one priority from the class's weight table, positioned so that a single
// action is unambiguously correct. Written against synthetic TestCards, so a Phase 4 rebalance
// of a real card cannot break them.
public class GreedyAgentTests
{
    private static GreedyAgent Agent(ulong seed = 1) => new(new SeededRandom(seed));

    // The shipped card set, for the match-play test at the bottom. Loaded the same way the fuzz
    // harness loads it, so both suites measure the same game.
    private static CardDatabase RealCards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static AgentContext Context(GameState state) =>
        AgentContext.ForActivePlayer(state, TestCards.Database);

    private static GameAction Choose(GameState state, ulong seed = 1) =>
        Agent(seed).Choose(Context(state));

    // -- It attacks -------------------------------------------------------------------------

    [Fact]
    public void It_uses_a_damaging_move_rather_than_ending_the_turn()
    {
        // The most basic failure this rules out: an agent that scores everything at zero and
        // falls through to EndTurn plays exactly like a RandomAgent that got unlucky, and would
        // silently make the Phase 2 exit criteria trivial to meet.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Resources(wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

        var action = Choose(state);

        var move = Assert.IsType<UseMoveAction>(action);
        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
    }

    [Fact]
    public void It_prefers_a_lethal_blow_over_a_larger_non_lethal_one()
    {
        // The lethal bonus doing its job. Slot 0's Striker faces a 1-health creature it can
        // kill; slot 1's TwoMove faces a 3-health creature it can only chip. Raw damage is
        // equal (1 each), so only the kill bonus can separate them -- an agent scoring damage
        // alone would tie and pick randomly.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Resources(spike: 4, wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2, health: 1)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3))
            .Build();

        var move = Assert.IsType<UseMoveAction>(Choose(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
    }

    [Fact]
    public void It_counts_type_effectiveness_when_comparing_targets()
    {
        // Both attackers deal a base 1. P1 slot 0's Striker is a WHEEL creature, so its move
        // costs wheel and attacks as Wheel -- which is 2x against Anvil. Slot 1's TwoMove
        // attacks as Spike into a Spike creature, for 1x.
        //
        // Wheel->Anvil is a real cycle edge (see TypeChart.Default), so the wheel attack lands
        // 2 and the spike attack lands 1. An agent ignoring the type chart would see both as 1
        // and tie.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Resources(spike: 4, wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Anvil, maxHealth: 4)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 4))
            .Build();

        var move = Assert.IsType<UseMoveAction>(Choose(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
    }

    [Fact]
    public void It_does_not_value_overkill()
    {
        // Damage is capped at the target's remaining health, so a 1-health target is worth the
        // same kill to either attacker -- and the tie is then broken randomly rather than by
        // whichever move happens to hit harder on paper.
        //
        // Stated as a property over seeds rather than a single assertion: the point is that BOTH
        // choices occur, which is what proves the scores are equal. An agent counting raw damage
        // would always pick the same one.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Resources(spike: 4, wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Spike, maxHealth: 4, health: 1)
                .Slot(1, TestCards.Striker, TypeMask.Spike, maxHealth: 4, health: 1))
            .Build();

        var chosen = Enumerable.Range(1, 40)
            .Select(seed => Choose(state, (ulong)seed))
            .OfType<UseMoveAction>()
            .Select(m => m.SourceSlot)
            .Distinct()
            .ToList();

        Assert.Equal(2, chosen.Count);
    }

    // -- It develops the board --------------------------------------------------------------

    [Fact]
    public void It_plays_a_creature_rather_than_ending_the_turn()
    {
        // Board presence is the engine's main currency -- it both scores and pays income -- so
        // an idle board with an affordable creature in hand is never a reason to pass.
        var state = new StateBuilder()
            .P1(p => p
                .Hand(TestCards.Striker)
                .Resources(wheel: 4))
            .Build();

        var play = Assert.IsType<PlayCardAction>(Choose(state));

        Assert.Equal(TestCards.Striker, play.CardId);
    }

    [Fact]
    public void It_prefers_an_unopposed_slot_when_placing_a_creature()
    {
        // A creature facing an empty enemy slot starts scoring next turn, which is the win
        // condition. Slot 2 is the only one whose facing enemy slot is empty.
        //
        // P2's creatures in slots 0 and 1 are ALREADY opposed by P1's own, so neither of those
        // slots is available and there is no blocking play to compete with -- which is the point
        // of setting it up this way. An earlier version of this test left P1's board empty, which
        // made P2's creatures unopposed and turned P1 slots 0 and 1 into blocking plays that
        // correctly outrank slot 2. That version was pinning the pre-blocking-rule behaviour.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Striker)
                .Resources(wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

        // Nothing for P2 to score, so nothing to deny: the only value on offer is the open slot.
        Assert.Equal(0, state.PendingScore(PlayerId.Two));

        var play = Assert.IsType<PlayCardAction>(Choose(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 2), play.TargetSlot);
    }

    [Fact]
    public void It_blocks_an_enemy_creature_that_is_currently_scoring()
    {
        // The scoring race has two halves, and this is the one the first version of the
        // heuristic missed entirely.
        //
        // P2's slot-0 creature is unopposed, so it scores a point at the start of every P2 turn.
        // Playing into P1 slot 0 opposes it and stops that -- and stops it IMMEDIATELY, since
        // ending this turn runs P2's scoring step (ActionExecutor.ApplyEndTurn ->
        // AdvanceToActions -> ApplyScoring). Slots 1 and 2 are open, so the earlier version
        // scored all three placements on the unopposed bonus alone, rated the blocking slot at
        // zero, and tie-broke randomly between the two open ones -- passing up a point per turn.
        var state = new StateBuilder()
            .P1(p => p
                .Hand(TestCards.Striker)
                .Resources(wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

        // The point being denied, stated as an engine fact rather than an assumption: P2 scores
        // 1 right now, and would score 0 with P1 slot 0 occupied.
        Assert.Equal(1, state.PendingScore(PlayerId.Two));

        var play = Assert.IsType<PlayCardAction>(Choose(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), play.TargetSlot);
    }

    [Fact]
    public void It_prefers_blocking_a_scoring_creature_over_taking_an_open_slot()
    {
        // The two halves of the scoring race put in direct competition, which is the only place
        // the ORDER of the two bonuses is observable.
        //
        // P2's slot-0 creature is unopposed and scoring; P1 slot 2 is open and unopposed. Both
        // are worth a point per turn, so an agent weighting them equally would tie-break randomly
        // between them. Blocking wins because it pays sooner and surer: it denies a point at P2's
        // very next scoring step (which happens the instant this turn ends), whereas the open
        // slot pays a full turn later and only if P2 does not contest it in between.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(1, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Striker)
                .Resources(wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

        // Exactly one P2 creature is scoring -- the slot-0 one. Its slot-1 creature is opposed.
        Assert.Equal(1, state.PendingScore(PlayerId.Two));

        var play = Assert.IsType<PlayCardAction>(Choose(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), play.TargetSlot);
    }

    [Fact]
    public void It_kills_a_creature_rather_than_developing_the_board()
    {
        // The priority ordering the weight table encodes: removing a body outranks adding one,
        // because a kill both stops the opponent's income and frees the facing slot to score.
        // Both actions are affordable here, so only the weights decide.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Striker)
                .Resources(wheel: 8))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2, health: 1))
            .Build();

        Assert.IsType<UseMoveAction>(Choose(state));
    }

    // -- It does not waste actions ------------------------------------------------------------

    [Fact]
    public void It_does_not_heal_a_creature_at_full_health()
    {
        // TwoMove has Jab (damage the facing slot) and Brace (heal self). With nothing opposing
        // it, Jab hits nothing and scores zero -- but Brace on a full-health creature must ALSO
        // score zero, or the agent would spend a turn healing for no reason.
        //
        // Both being worthless, the agent should end the turn rather than take either: EndTurn
        // scores negative but a zero-value action is not preferred to passing... it is. So this
        // asserts the weaker, correct thing -- that Brace specifically is not chosen.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Resources(spike: 4))
            .Build();

        var action = Choose(state);

        // Brace is move index 1 on TwoMove. Jab (index 0) is a legal but pointless action here;
        // what matters is that a no-op heal is not preferred over it or over passing.
        if (action is UseMoveAction move)
        {
            Assert.NotEqual(1, move.MoveIndex);
        }
    }

    [Fact]
    public void It_heals_a_damaged_creature_when_there_is_nothing_to_attack()
    {
        // The other half: healing IS worth something when there is damage to undo. Nothing
        // opposes slot 1, so Jab scores zero and Brace scores the healing -- which must be
        // enough to beat ending the turn.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3, health: 1)
                .Resources(spike: 4))
            .Build();

        var move = Assert.IsType<UseMoveAction>(Choose(state));

        Assert.Equal(1, move.MoveIndex);
    }

    [Fact]
    public void It_ends_the_turn_when_nothing_is_affordable()
    {
        // The floor case. With no resources and an empty board there is nothing but EndTurn in
        // the legal list, so this mostly pins that a negative EndTurn score does not make the
        // agent throw or loop when it is the only option.
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Costly))
            .Build();

        Assert.IsType<EndTurnAction>(Choose(state));
    }

    // -- Discards -----------------------------------------------------------------------------

    [Fact]
    public void It_discards_its_cheapest_card()
    {
        // While a discard debt stands the generator offers nothing else, so this competes only
        // against other discards. Cheapest-first is a crude proxy for least-valuable, but a
        // reproducible one -- the alternative is an arbitrary pick that would vary with hand
        // order.
        //
        // Striker costs 1 wheel; Costly costs 9 anvil.
        var state = new StateBuilder()
            .P1(p => p
                .Hand(TestCards.DiscardTwo, TestCards.Striker, TestCards.Costly)
                .Resources(wheel: 4))
            .Build();

        // Play the spell to incur the debt, rather than hand-building a pending state -- this
        // way the test exercises the same path the game does.
        ActionExecutor.Apply(
            state, TestCards.Database,
            new PlayCardAction(PlayerId.One, TestCards.DiscardTwo));

        Assert.True(state.AwaitingDiscard);

        var discard = Assert.IsType<DiscardAction>(Choose(state));

        Assert.Equal(TestCards.Striker, discard.CardId);
    }

    // -- It survives the real card set ----------------------------------------------------------

    [Fact]
    public void It_plays_full_games_on_the_real_card_set_without_faulting()
    {
        // The only test that runs GreedyAgent against the REAL ~36-card set -- everything above
        // uses synthetic TestCards, which cover rule SHAPES but not the actual effect vocabulary.
        // Real cards mean damage_scaled, for_each, conditional, summon, health_source and the
        // rest, so this is what catches a heuristic that throws on an op it has no branch for, or
        // returns an action the executor then rejects.
        //
        // Deliberately NOT a strength assertion. How often one agent beats another is a balance
        // question, and balance questions belong to Phase 4's balance work, which runs batches
        // properly (parallel, seeded, CSV) and compares numbers across rulesets. A threshold here
        // would be a number picked by rounding down whatever the agent currently scores -- it
        // would pass until an unrelated Phase 4 rebalance moved it, then fail while indicating no
        // defect, which is worse than not testing it. What this asserts instead is robustness, a
        // property of the agent alone that cannot drift when cards are repriced.
        //
        // Both seats are played across the sample, so a seat-specific fault cannot hide.
        var rules = RuleSet.Default;

        for (var seed = 1; seed <= 60; seed++)
        {
            var greedySeat = seed % 2 == 0 ? PlayerId.One : PlayerId.Two;

            var (winner, actions) = PlayGame(RealCards, rules, (ulong)seed, greedySeat);

            // Termination, not victory. A game that hit the cap means an agent stopped making
            // progress -- a defect an aggregate score would surface only as an odd number, if at
            // all, whereas here it names the seed that stalled.
            Assert.True(
                winner is not null,
                $"Seed {seed}: no winner after {actions} actions. An agent is not progressing.");
        }
    }

    // Plays one full game between a GreedyAgent and a RandomAgent, returning the winner (null if
    // the game hit the safety cap without one) and how many actions it took.
    //
    // The action count is returned so a cap-out reports how far it got rather than just "no
    // winner" -- a game that stalled at 5000 actions and one that ended at 30 are different
    // failures, and the message should say which.
    private static (PlayerId? Winner, int Actions) PlayGame(
        CardDatabase cards, RuleSet rules, ulong seed, PlayerId greedySeat)
    {
        var state = new GameState(rules, new SeededRandom(seed));

        foreach (var player in PlayerIds.All)
        {
            state[player].SetDeck(cards.BuildSymmetricDeck(rules));
            state[player].ShuffleDeck(state.Random);
            state[player].Draw(rules.StartingHandSize);
        }

        var agents = new Dictionary<PlayerId, IAgent>
        {
            [greedySeat] = new GreedyAgent(new SeededRandom(seed * 7919)),
            [greedySeat.Opponent()] = new RandomAgent(new SeededRandom(seed * 104729)),
        };

        state.AdvanceToActions();

        // Caps the game rather than trusting termination: a heuristic bug that made an agent
        // loop would otherwise hang the suite instead of failing it. The fuzz harness already
        // pins that random play terminates, so a cap hit here points at the agent.
        var taken = 0;
        for (; taken < 5000 && !state.IsOver; taken++)
        {
            var context = AgentContext.ForActivePlayer(state, cards);
            ActionExecutor.Apply(state, cards, agents[state.ActivePlayer].Choose(context));
        }

        return (state.Winner, taken);
    }
}
