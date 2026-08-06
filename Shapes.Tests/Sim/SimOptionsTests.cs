using Shapes.Sim;

namespace Shapes.Tests.Sim;

public class SimOptionsTests
{
    [Fact]
    public void Defaults_cover_all_three_agents()
    {
        var options = SimOptions.Parse([]);

        Assert.Equal(["random", "greedy", "ismcts"], options.Agents);
        Assert.Equal(100, options.Games);
    }

    [Fact]
    public void Agents_flag_overrides_the_default_pool()
    {
        var options = SimOptions.Parse(["--agents", "greedy,ismcts"]);

        Assert.Equal(["greedy", "ismcts"], options.Agents);
    }

    [Fact]
    public void Games_seed_and_iterations_are_parsed()
    {
        var options = SimOptions.Parse(["--games", "50", "--seed", "42", "--iterations", "500"]);

        Assert.Equal(50, options.Games);
        Assert.Equal(42UL, options.Seed);
        Assert.Equal(500, options.Iterations);
    }

    [Fact]
    public void Csv_and_json_paths_are_parsed()
    {
        var options = SimOptions.Parse(
            ["--csv", "out.csv", "--json", "out.json", "--metrics-json", "metrics.json"]);

        Assert.Equal("out.csv", options.OutputCsv);
        Assert.Equal("out.json", options.OutputJson);
        Assert.Equal("metrics.json", options.MetricsJson);
    }

    [Fact]
    public void Report_and_csv_export_paths_are_parsed()
    {
        var options = SimOptions.Parse(
            ["--report", "out.html", "--cards-csv", "cards.csv", "--moves-csv", "moves.csv"]);

        Assert.Equal("out.html", options.Report);
        Assert.Equal("cards.csv", options.CardsCsv);
        Assert.Equal("moves.csv", options.MovesCsv);
    }

    [Fact]
    public void From_metrics_json_path_is_parsed_and_bypasses_the_positive_games_check()
    {
        // --games keeps its default of 100 here since no batch will run; the point is that
        // --from-metrics-json alone (with no --games override) must not trip the "--games must
        // be positive" validation, which only makes sense when a batch is actually being played.
        var options = SimOptions.Parse(["--from-metrics-json", "metrics.json"]);

        Assert.Equal("metrics.json", options.FromMetricsJson);
    }

    [Fact]
    public void Unknown_flag_throws()
    {
        Assert.Throws<ArgumentException>(() => SimOptions.Parse(["--bogus"]));
    }

    [Fact]
    public void Zero_or_negative_games_throws()
    {
        Assert.Throws<ArgumentException>(() => SimOptions.Parse(["--games", "0"]));
    }

    [Fact]
    public void Missing_value_throws()
    {
        Assert.Throws<ArgumentException>(() => SimOptions.Parse(["--games"]));
    }

    [Fact]
    public void Help_flag_is_recognized()
    {
        Assert.True(SimOptions.Parse(["--help"]).ShowHelp);
        Assert.False(SimOptions.Parse([]).ShowHelp);
    }
}
