using Shapes.Console;

namespace Shapes.Tests.Console;

// Step 4 of Phase 4: --step, parsed the same way every other boolean console flag is (see
// ConsoleOptions.Parse's --quiet/--reveal cases).
public class ConsoleOptionsStepTests
{
    [Fact]
    public void Step_defaults_to_off()
    {
        Assert.False(ConsoleOptions.Parse([]).Step);
    }

    [Fact]
    public void Step_flag_is_parsed()
    {
        Assert.True(ConsoleOptions.Parse(["--step"]).Step);
    }

    [Fact]
    public void Step_is_independent_of_quiet()
    {
        var options = ConsoleOptions.Parse(["--quiet", "--step"]);

        Assert.True(options.Quiet);
        Assert.True(options.Step);
    }
}
