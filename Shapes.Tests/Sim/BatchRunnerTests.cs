using Shapes.Core.Rules;
using Shapes.Sim;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Sim;

// BatchRunner assembles DESIGN.md step 3.1's exit bar directly: every ordered pairing, both seat
// assignments reported separately, run in parallel without games colliding on a shared seed.
public class BatchRunnerTests
{
    private static readonly RuleSet Rules = RuleSet.Default;

    private static SimOptions Options(int games = 3, params string[] agents) => SimOptions.Parse(
        [
            "--agents", string.Join(',', agents.Length > 0 ? agents : ["random", "greedy"]),
            "--games", games.ToString(),
            "--seed", "11",
            "--iterations", "10",
        ]);

    [Fact]
    public void Every_ordered_pairing_including_self_pairings_is_produced()
    {
        var result = BatchRunner.Run(Options(agents: ["random", "greedy"]), TestCards.Database, Rules);

        var pairs = result.Pairings.Select(p => (p.AgentOne, p.AgentTwo)).ToHashSet();
        Assert.Equal(4, pairs.Count);
        Assert.Contains(("random", "random"), pairs);
        Assert.Contains(("random", "greedy"), pairs);
        Assert.Contains(("greedy", "random"), pairs);
        Assert.Contains(("greedy", "greedy"), pairs);
    }

    [Fact]
    public void Mirrored_pairings_are_kept_separate_not_pooled()
    {
        // DESIGN.md is explicit that pooling seats hides first-player advantage -- so (A, B) and
        // (B, A) must remain two distinct PairingSummary entries, each with its own win rate,
        // never merged into one "A vs B" aggregate.
        var result = BatchRunner.Run(Options(agents: ["random", "greedy"]), TestCards.Database, Rules);

        var randomFirst = result.Pairings.Single(p => p.AgentOne == "random" && p.AgentTwo == "greedy");
        var greedyFirst = result.Pairings.Single(p => p.AgentOne == "greedy" && p.AgentTwo == "random");

        Assert.NotSame(randomFirst, greedyFirst);
        Assert.All(randomFirst.Games, g => Assert.Equal("random", g.AgentOne));
        Assert.All(greedyFirst.Games, g => Assert.Equal("greedy", g.AgentOne));
    }

    [Fact]
    public void Each_pairing_runs_the_configured_game_count()
    {
        var result = BatchRunner.Run(Options(games: 5, agents: ["random", "greedy"]), TestCards.Database, Rules);

        Assert.All(result.Pairings, p => Assert.Equal(5, p.GameCount));
        Assert.Equal(5 * 4, result.AllGames.Count);
    }

    [Fact]
    public void No_two_games_in_the_whole_matrix_share_a_seed()
    {
        // Distinct seeds are what make every game in a giant matrix independently reproducible
        // and non-colliding -- two different pairings landing on the same seed would mean one of
        // them wasn't the game the seed number claims to reproduce.
        var result = BatchRunner.Run(Options(games: 4, agents: ["random", "greedy"]), TestCards.Database, Rules);

        var seeds = result.AllGames.Select(g => g.Seed).ToList();
        Assert.Equal(seeds.Count, seeds.Distinct().Count());
    }

    [Fact]
    public void Rerunning_the_same_options_reproduces_the_same_matrix()
    {
        var options = Options(games: 3, agents: ["random", "greedy"]);

        var first = BatchRunner.Run(options, TestCards.Database, Rules);
        var second = BatchRunner.Run(options, TestCards.Database, Rules);

        var firstWins = first.Pairings.OrderBy(p => p.AgentOne).ThenBy(p => p.AgentTwo)
            .Select(p => p.AgentOneWins).ToList();
        var secondWins = second.Pairings.OrderBy(p => p.AgentOne).ThenBy(p => p.AgentTwo)
            .Select(p => p.AgentOneWins).ToList();

        Assert.Equal(firstWins, secondWins);
    }

    [Fact]
    public void Win_rate_matches_wins_divided_by_games()
    {
        var result = BatchRunner.Run(Options(games: 6, agents: ["random", "greedy"]), TestCards.Database, Rules);

        Assert.All(result.Pairings, p =>
            Assert.Equal((double)p.AgentOneWins / p.GameCount, p.AgentOneWinRate, precision: 10));
    }

    [Fact]
    public void Progress_callback_fires_once_per_game_and_ends_at_the_total()
    {
        var options = Options(games: 4, agents: ["random", "greedy"]);
        var totalJobs = options.Games * 4; // every ordered pairing, including self-pairings

        var calls = new System.Collections.Concurrent.ConcurrentBag<(int Completed, int Total)>();
        BatchRunner.Run(options, TestCards.Database, Rules, (completed, total) => calls.Add((completed, total)));

        Assert.Equal(totalJobs, calls.Count);
        Assert.All(calls, c => Assert.Equal(totalJobs, c.Total));
        Assert.Equal(Enumerable.Range(1, totalJobs), calls.Select(c => c.Completed).OrderBy(c => c));
    }
}
