using Shapes.Core.State;

namespace Shapes.Tests.Mechanics;

// Determinism is the property the whole debugging and balance story rests on: a game must
// replay identically from its seed, on any machine, on any .NET version.
public class RandomSourceTests
{
    [Fact]
    public void Same_seed_produces_the_same_sequence()
    {
        var a = new SeededRandom(12345);
        var b = new SeededRandom(12345);

        for (var i = 0; i < 200; i++)
        {
            Assert.Equal(a.Next(1000), b.Next(1000));
        }
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var a = new SeededRandom(1);
        var b = new SeededRandom(2);

        var sequenceA = Enumerable.Range(0, 50).Select(_ => a.Next(1000)).ToList();
        var sequenceB = Enumerable.Range(0, 50).Select(_ => b.Next(1000)).ToList();

        Assert.NotEqual(sequenceA, sequenceB);
    }

    [Fact]
    public void Seed_zero_is_usable()
    {
        // Zero is a fixed point for xorshift and would emit nothing but zeros if passed
        // through unmapped. Callers should not have to know that.
        var random = new SeededRandom(0);
        var values = Enumerable.Range(0, 50).Select(_ => random.Next(100)).ToList();

        Assert.True(values.Distinct().Count() > 1, "Seed 0 produced a constant sequence.");
    }

    [Fact]
    public void Restart_replays_from_the_beginning()
    {
        var random = new SeededRandom(99);
        var first = Enumerable.Range(0, 20).Select(_ => random.Next(500)).ToList();

        var restarted = random.Restart();
        var second = Enumerable.Range(0, 20).Select(_ => restarted.Next(500)).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fork_continues_from_the_current_position_independently()
    {
        // This is what makes GameState.Clone safe: a search rollout on the clone must not
        // advance the original's stream.
        var original = new SeededRandom(7);
        _ = original.Next(100);
        _ = original.Next(100);

        var fork = original.Fork();

        var fromOriginal = Enumerable.Range(0, 20).Select(_ => original.Next(1000)).ToList();
        var fromFork = Enumerable.Range(0, 20).Select(_ => fork.Next(1000)).ToList();

        Assert.Equal(fromOriginal, fromFork);
    }

    [Fact]
    public void Fork_does_not_advance_the_original()
    {
        var original = new SeededRandom(7);
        var fork = original.Fork();

        // Burn the fork hard; the original must be untouched.
        for (var i = 0; i < 100; i++)
        {
            _ = fork.Next(1000);
        }

        var expected = new SeededRandom(7);
        Assert.Equal(expected.Next(1000), original.Next(1000));
    }

    [Fact]
    public void Next_stays_in_range()
    {
        var random = new SeededRandom(4242);

        for (var i = 0; i < 5000; i++)
        {
            var value = random.Next(10);
            Assert.InRange(value, 0, 9);
        }
    }

    [Fact]
    public void Next_of_one_is_always_zero()
    {
        var random = new SeededRandom(1);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(0, random.Next(1));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Next_rejects_a_non_positive_bound(int bound)
    {
        var random = new SeededRandom(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(bound));
    }

    [Fact]
    public void Distribution_is_roughly_uniform()
    {
        // Guards the rejection sampling. A plain modulus biases low values whenever the bound
        // does not divide the range evenly; over millions of MCTS playouts that would skew
        // every result drawn from it.
        var random = new SeededRandom(2024);
        var counts = new int[7];
        const int samples = 70_000;

        for (var i = 0; i < samples; i++)
        {
            counts[random.Next(7)]++;
        }

        var expected = samples / 7.0;
        foreach (var count in counts)
        {
            Assert.InRange(count, expected * 0.9, expected * 1.1);
        }
    }

    [Fact]
    public void Seed_is_recorded()
    {
        Assert.Equal(31337UL, new SeededRandom(31337).Seed);
        Assert.Equal(31337UL, new SeededRandom(31337).Fork().Seed);
    }
}
