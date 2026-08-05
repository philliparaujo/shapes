using Shapes.Ai.Search;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Ai;

// PLAN.md step 3.2: the playout-time heuristic that replaces uniform-random rollout selection.
// Mirrors GreedyAgentTests' shape -- one action forced unambiguously correct per test -- but
// against SelectAction directly rather than through an agent, since this policy is search
// machinery, not something IAgent-shaped. Written against synthetic TestCards so a Phase 4
// rebalance of a real card cannot break these.
public class HeuristicPlayoutPolicyTests
{
    private static readonly HeuristicPlayoutPolicy Policy = HeuristicPlayoutPolicy.Instance;

    private static GameAction SelectFrom(GameState state, ulong seed = 1) =>
        Policy.SelectAction(
            state, ActionGenerator.Generate(state, TestCards.Database), TestCards.Database,
            new SeededRandom(seed));

    [Fact]
    public void It_uses_a_damaging_move_rather_than_ending_the_turn()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Resources(wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

        var move = Assert.IsType<UseMoveAction>(SelectFrom(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
    }

    [Fact]
    public void It_prefers_a_lethal_blow_over_a_larger_non_lethal_one()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Resources(spike: 4, wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2, health: 1)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3))
            .Build();

        var move = Assert.IsType<UseMoveAction>(SelectFrom(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
    }

    [Fact]
    public void It_counts_type_effectiveness_when_comparing_targets()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 3)
                .Resources(spike: 4, wheel: 4))
            .P2(p => p
                .Slot(0, TestCards.Striker, TypeMask.Anvil, maxHealth: 4)
                .Slot(1, TestCards.TwoMove, TypeMask.Spike, maxHealth: 4))
            .Build();

        var move = Assert.IsType<UseMoveAction>(SelectFrom(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), move.SourceSlot);
    }

    [Fact]
    public void It_plays_a_creature_rather_than_ending_the_turn()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Hand(TestCards.Striker)
                .Resources(wheel: 4))
            .Build();

        var play = Assert.IsType<PlayCardAction>(SelectFrom(state));

        Assert.Equal(TestCards.Striker, play.CardId);
    }

    [Fact]
    public void It_prefers_an_unopposed_slot_when_placing_a_creature()
    {
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

        var play = Assert.IsType<PlayCardAction>(SelectFrom(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 2), play.TargetSlot);
    }

    [Fact]
    public void It_blocks_an_enemy_creature_that_is_currently_scoring()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Hand(TestCards.Striker)
                .Resources(wheel: 4))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2))
            .Build();

        Assert.Equal(1, state.PendingScore(PlayerId.Two));

        var play = Assert.IsType<PlayCardAction>(SelectFrom(state));

        Assert.Equal(new SlotIndex(PlayerId.One, 0), play.TargetSlot);
    }

    [Fact]
    public void It_kills_a_creature_rather_than_developing_the_board()
    {
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2)
                .Hand(TestCards.Striker)
                .Resources(wheel: 8))
            .P2(p => p.Slot(0, TestCards.Striker, TypeMask.Wheel, maxHealth: 2, health: 1))
            .Build();

        Assert.IsType<UseMoveAction>(SelectFrom(state));
    }

    [Fact]
    public void It_does_not_value_overkill()
    {
        // As GreedyAgentTests: a tie in capped damage must actually tie-break across both
        // options over many seeds, or the cap isn't being applied.
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
            .Select(seed => SelectFrom(state, (ulong)seed))
            .OfType<UseMoveAction>()
            .Select(m => m.SourceSlot)
            .Distinct()
            .ToList();

        Assert.Equal(2, chosen.Count);
    }

    [Fact]
    public void It_ends_the_turn_when_nothing_is_affordable()
    {
        var state = new StateBuilder()
            .P1(p => p.Hand(TestCards.Costly))
            .Build();

        Assert.IsType<EndTurnAction>(SelectFrom(state));
    }

    [Fact]
    public void An_unresolvable_conditional_or_for_each_still_scores_via_its_branches()
    {
        // conditional/for_each are scored by recursing into their nested effects rather than
        // falling to the flat unknown weight -- this only pins that recursing doesn't throw and
        // still prefers acting over passing, using a real card (Gated) that carries a condition.
        var state = new StateBuilder()
            .P1(p => p
                .Slot(0, TestCards.Gated, TypeMask.Wheel, maxHealth: 2)
                .Resources(wheel: 4))
            .Build();

        Assert.IsType<UseMoveAction>(SelectFrom(state));
    }

    [Fact]
    public void It_plays_full_games_on_the_real_card_set_without_faulting()
    {
        // Mirrors GreedyAgentTests' robustness check: the real ~36-card set exercises
        // damage_scaled, for_each, conditional, summon and the rest, so this catches an op this
        // policy has no case for throwing rather than falling through to the unknown weight.
        var cards = CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));
        var rules = RuleSet.Default;

        for (var seed = 1; seed <= 20; seed++)
        {
            var state = new GameState(rules, new SeededRandom((ulong)seed));

            foreach (var player in PlayerIds.All)
            {
                state[player].SetDeck(cards.BuildSymmetricDeck(rules));
                state[player].ShuffleDeck(state.Random);
                state[player].Draw(rules.StartingHandSize);
            }

            state.AdvanceToActions();

            var random = new SeededRandom((ulong)seed * 7919);
            var taken = 0;
            for (; taken < 5000 && !state.IsOver; taken++)
            {
                var legal = ActionGenerator.Generate(state, cards);
                var action = Policy.SelectAction(state, legal, cards, random);
                ActionExecutor.Apply(state, cards, action);
            }

            Assert.True(
                state.IsOver, $"Seed {seed}: no winner after {taken} actions using the heuristic policy.");
        }
    }
}
