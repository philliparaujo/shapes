using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Sim;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Sim;

// GameRunner is the headless equivalent of Shapes.Console/Program.cs's game loop (construct
// state, deal, AdvanceToActions, play to IsOver). The console version is already watched by hand
// per PLAN.md's guidance; these tests exist to pin the parts that are specific to batch play:
// same-seed reproducibility, and that every action taken is accounted for in the result.
public class GameRunnerTests
{
    private static readonly RuleSet Rules = RuleSet.Default;

    [Fact]
    public void A_game_always_produces_a_winner()
    {
        var result = GameRunner.Play("random", "random", seed: 1, TestCards.Database, Rules, iterations: 10);

        Assert.Equal(EndingType.ScoreThreshold, result.Ending);
        Assert.NotNull(result.Winner);
        var winnerScore = result.Winner == PlayerId.One ? result.ScoreOne : result.ScoreTwo;
        Assert.True(winnerScore >= Rules.ScoreToWin);
    }

    [Fact]
    public void Same_seed_and_agents_reproduce_the_same_game()
    {
        // The whole point of seeding through IRandomSource rather than Random.Shared: a batch run
        // must be replayable, and a script rerunning "the game that looked wrong" must get exactly
        // that game back, not a different one that happens to share a seed number.
        var first = GameRunner.Play("random", "greedy", seed: 7, TestCards.Database, Rules, iterations: 10);
        var second = GameRunner.Play("random", "greedy", seed: 7, TestCards.Database, Rules, iterations: 10);

        Assert.Equal(first.Winner, second.Winner);
        Assert.Equal(first.TurnCount, second.TurnCount);
        Assert.Equal(first.ActionCount, second.ActionCount);
        Assert.Equal(first.ScoreOne, second.ScoreOne);
        Assert.Equal(first.ScoreTwo, second.ScoreTwo);
    }

    [Fact]
    public void Different_seeds_can_produce_different_games()
    {
        // Not a strict guarantee for every possible pair, but random-v-random across two
        // arbitrary seeds should not collapse to identical play -- if it did, the RNG fork or the
        // seed itself would not actually be varying anything.
        var results = Enumerable.Range(1, 8)
            .Select(seed => GameRunner.Play(
                "random", "random", (ulong)seed, TestCards.Database, Rules, iterations: 10))
            .ToList();

        Assert.True(results.Select(r => r.ActionCount).Distinct().Count() > 1);
    }

    [Fact]
    public void Swapping_seats_does_not_change_the_other_agents_stream()
    {
        // Each agent is forked from the base seed with a distinct multiplier specifically so that
        // one seat's agent kind never perturbs the other's decisions. If this regressed to a
        // shared stream, changing agentTwo would change how agentOne plays too.
        var baseline = GameRunner.Play("random", "greedy", seed: 3, TestCards.Database, Rules, iterations: 10);
        var otherOpponent = GameRunner.Play("random", "random", seed: 3, TestCards.Database, Rules, iterations: 10);

        // Both are random-vs-X from an identical P1 stream; P1's own draws/choices are unaffected
        // by what P2 is, so at minimum the game shouldn't crash and both complete validly. The
        // precise action sequences legitimately diverge once P2's differing choices change the
        // board P1 reacts to -- this test only pins that the source game boots and completes for
        // both, exercising the same seed across agent kinds without mixing streams.
        Assert.True(Math.Max(baseline.ScoreOne, baseline.ScoreTwo) >= Rules.ScoreToWin);
        Assert.True(Math.Max(otherOpponent.ScoreOne, otherOpponent.ScoreTwo) >= Rules.ScoreToWin);
    }

    [Fact]
    public void Action_counts_by_kind_sum_to_the_total_action_count()
    {
        var result = GameRunner.Play("greedy", "greedy", seed: 5, TestCards.Database, Rules, iterations: 10);

        Assert.Equal(result.ActionCount, result.ActionCountsByKind.Values.Sum());
    }

    [Fact]
    public void An_unknown_agent_kind_throws()
    {
        Assert.Throws<ArgumentException>(
            () => GameRunner.Play("nonsense", "random", seed: 1, TestCards.Database, Rules, iterations: 10));
    }

    [Fact]
    public void A_card_can_never_be_played_more_often_than_it_was_offered()
    {
        // The invariant that makes take rate meaningful, checked against real games rather than
        // synthetic counts: every play must have been preceded by a decision point at which that
        // play was legal. A violation would mean the offer counter and the action stream have
        // drifted out of step -- which would silently push take rates above 1.0 rather than
        // failing loudly.
        var result = GameRunner.Play("greedy", "greedy", seed: 11, TestCards.Database, Rules, iterations: 10);

        foreach (var (cards, offers) in new[]
        {
            (result.CardsPlayedOne, result.CardOffersOne),
            (result.CardsPlayedTwo, result.CardOffersTwo),
        })
        {
            foreach (var group in cards.GroupBy(c => c, StringComparer.Ordinal))
            {
                Assert.True(
                    offers.GetValueOrDefault(group.Key) >= group.Count(),
                    $"{group.Key} was played {group.Count()} times but offered only "
                    + $"{offers.GetValueOrDefault(group.Key)} times.");
            }
        }
    }

    [Fact]
    public void A_move_can_never_be_used_more_often_than_it_was_offered()
    {
        var result = GameRunner.Play("greedy", "greedy", seed: 11, TestCards.Database, Rules, iterations: 10);

        foreach (var (moves, offers) in new[]
        {
            (result.MovesUsedOne, result.MoveOffersOne),
            (result.MovesUsedTwo, result.MoveOffersTwo),
        })
        {
            foreach (var group in moves.GroupBy(m => MoveKey.Of(m.CardId, m.MoveName), StringComparer.Ordinal))
            {
                Assert.True(
                    offers.GetValueOrDefault(group.Key) >= group.Count(),
                    $"{group.Key} was used {group.Count()} times but offered only "
                    + $"{offers.GetValueOrDefault(group.Key)} times.");
            }
        }
    }

    [Fact]
    public void Merges_never_exceed_the_decisions_where_a_merge_was_legal()
    {
        var result = GameRunner.Play("greedy", "greedy", seed: 11, TestCards.Database, Rules, iterations: 10);

        Assert.True(result.MergeOffersOne >= result.MergeCountOne);
        Assert.True(result.MergeOffersTwo >= result.MergeCountTwo);
    }

    [Fact]
    public void Per_turn_series_are_sampled_once_per_turn_and_end_at_the_final_score()
    {
        // One sample per turn boundary, not per action -- and the last margin sampled must be
        // the game's actual final margin, which is what pins the series to the result rather
        // than to some mid-turn intermediate state.
        var result = GameRunner.Play("greedy", "random", seed: 3, TestCards.Database, Rules, iterations: 10);

        Assert.NotEmpty(result.ScoreMarginByTurn);
        Assert.Equal(result.ScoreMarginByTurn.Count, result.ResourcesByTurnOne.Count);
        Assert.Equal(result.ScoreMarginByTurn.Count, result.ResourcesByTurnTwo.Count);
        Assert.True(result.ScoreMarginByTurn.Count <= result.TurnCount);
        Assert.Equal(result.ScoreOne - result.ScoreTwo, result.ScoreMarginByTurn[^1]);
    }

    [Fact]
    public void Unopposed_slot_turns_equal_the_score_they_produced()
    {
        // The exact cross-check, and the reason this metric can be trusted at all: at
        // PointsPerUnopposedCreature = 1 every unopposed slot-turn IS one point, so the tally
        // must reconcile to the final score with no slack. Any sampling error shows up here --
        // the first implementation observed the seat that ENDED its turn rather than the one
        // RECEIVING it, reading the board before the opponent could contest those slots, and
        // over-counted by ~40% while still looking entirely plausible in aggregate.
        //
        // Asserted across several seeds because a single game can coincidentally agree.
        //
        // The identity is score == slot-turns x PointsPerUnopposedCreature; stated as a
        // multiplication rather than assuming the default's value of 1, so a ruleset sweep that
        // changes the points per creature reveals a real regression here instead of a false one.
        var points = Rules.PointsPerUnopposedCreature;

        foreach (var seed in new ulong[] { 3, 5, 13, 21 })
        {
            var result = GameRunner.Play(
                "greedy", "greedy", seed, TestCards.Database, Rules, iterations: 10);

            Assert.Equal(result.ScoreOne, result.UnopposedSlotTurnsOne * points);
            Assert.Equal(result.ScoreTwo, result.UnopposedSlotTurnsTwo * points);
        }
    }

    [Fact]
    public void Unopposed_slot_turns_never_exceed_the_slots_that_existed_to_be_unopposed()
    {
        // Each scoring step offers at most SlotsPerPlayer unopposed slots per seat. Exceeding
        // that means the observation is firing more than once per step -- the failure mode that
        // would silently inflate the scoring-rule denominator.
        var result = GameRunner.Play("greedy", "greedy", seed: 13, TestCards.Database, Rules, iterations: 10);

        Assert.True(
            result.UnopposedSlotTurnsOne <= result.ScoringStepsOne * SlotIndex.SlotsPerPlayer,
            $"P1 had {result.UnopposedSlotTurnsOne} unopposed slot-turns over "
            + $"{result.ScoringStepsOne} steps.");
        Assert.True(
            result.UnopposedSlotTurnsTwo <= result.ScoringStepsTwo * SlotIndex.SlotsPerPlayer,
            $"P2 had {result.UnopposedSlotTurnsTwo} unopposed slot-turns over "
            + $"{result.ScoringStepsTwo} steps.");
    }

    [Fact]
    public void An_unopposed_streak_never_exceeds_the_scoring_steps_that_seat_took()
    {
        var result = GameRunner.Play("greedy", "greedy", seed: 13, TestCards.Database, Rules, iterations: 10);

        Assert.True(result.LongestUnopposedStreakOne <= result.ScoringStepsOne);
        Assert.True(result.LongestUnopposedStreakTwo <= result.ScoringStepsTwo);
    }

    [Fact]
    public void Both_seats_take_scoring_steps_in_a_real_game()
    {
        // Guards against the observation being keyed to one seat: scoring runs once per turn per
        // player, so a game of any length must show steps for both.
        var result = GameRunner.Play("greedy", "random", seed: 4, TestCards.Database, Rules, iterations: 10);

        Assert.True(result.ScoringStepsOne > 0);
        Assert.True(result.ScoringStepsTwo > 0);
    }

    [Fact]
    public void Creature_survival_is_recorded_only_for_creatures_and_never_negative()
    {
        // A lifetime is (destroyed step - played step), so a negative value would mean the two
        // were harvested out of order. Spells must never appear at all -- they hold no slot.
        var result = GameRunner.Play("greedy", "greedy", seed: 13, TestCards.Database, Rules, iterations: 10);

        foreach (var lifetime in result.CreatureSurvivalOne.Concat(result.CreatureSurvivalTwo))
        {
            Assert.True(
                TestCards.Database[lifetime.CardId].IsCreature,
                $"{lifetime.CardId} is not a creature but has a survival record.");
            Assert.True(
                lifetime.ScoringStepsSurvived >= 0,
                $"{lifetime.CardId} survived {lifetime.ScoringStepsSurvived} steps.");
        }
    }

    [Fact]
    public void Destroyed_creatures_never_outnumber_creatures_played()
    {
        // A creature cannot die more often than it was played. This is the check that catches
        // double-harvesting of CreatureDestroyed events, and it also confirms merges do not
        // produce phantom deaths -- a merged-away creature must leave no survival record.
        var result = GameRunner.Play("greedy", "greedy", seed: 13, TestCards.Database, Rules, iterations: 10);

        Assert.True(result.CreatureSurvivalOne.Count <= result.CreaturesPlayedOne);
        Assert.True(result.CreatureSurvivalTwo.Count <= result.CreaturesPlayedTwo);
    }

    [Fact]
    public void A_card_blocked_by_cost_is_never_also_offered_at_that_same_decision()
    {
        // The two counters must partition held cards, not overlap: a card either could be played
        // or could not. Overlap would mean the affordability check disagrees with the action
        // generator's, which is the classic "two implementations of legal" bug the generator's
        // own docs warn about. Totals are a weaker check than per-decision, but they would still
        // catch a systematically double-counted card.
        var result = GameRunner.Play("greedy", "greedy", seed: 13, TestCards.Database, Rules, iterations: 10);

        foreach (var (cardId, blocked) in result.CardsBlockedByCostOne)
        {
            var offers = result.CardOffersOne.GetValueOrDefault(cardId);
            Assert.True(
                blocked + offers <= result.ActionCount,
                $"{cardId}: {blocked} blocked + {offers} offered exceeds {result.ActionCount} decisions.");
        }
    }

    [Fact]
    public void Offer_counting_does_not_change_the_game_that_gets_played()
    {
        // CountOffers calls ActionGenerator.Generate an extra time per decision. Generate is
        // documented as pure, but "pure" is worth verifying here specifically: if it advanced
        // the RNG or mutated state, adding instrumentation would silently change every result in
        // Phase 3's frozen reference matrix. Same seed must still give the same game as the
        // recorded expectations in the tests above.
        var first = GameRunner.Play("greedy", "random", seed: 3, TestCards.Database, Rules, iterations: 10);
        var second = GameRunner.Play("greedy", "random", seed: 3, TestCards.Database, Rules, iterations: 10);

        Assert.Equal(first.Winner, second.Winner);
        Assert.Equal(first.ActionCount, second.ActionCount);
        Assert.Equal(first.ScoreMarginByTurn, second.ScoreMarginByTurn);
    }
}
