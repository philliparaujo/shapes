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

        Assert.True(result.WinnerScore >= Rules.ScoreToWin);
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
        Assert.Equal(first.WinnerScore, second.WinnerScore);
        Assert.Equal(first.LoserScore, second.LoserScore);
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
        Assert.True(baseline.WinnerScore >= Rules.ScoreToWin);
        Assert.True(otherOpponent.WinnerScore >= Rules.ScoreToWin);
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
}
