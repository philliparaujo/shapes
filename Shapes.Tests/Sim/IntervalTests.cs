using Shapes.Sim;

namespace Shapes.Tests.Sim;

// Pins the statistics behind every rate in MetricsReport. These matter more than usual for a
// helper type: a wrong interval doesn't crash or look wrong, it just quietly licenses a balance
// change that the data never supported.
public class IntervalTests
{
    [Fact]
    public void Rate_is_the_plain_proportion()
    {
        var interval = Interval.Wilson(3, 12);

        Assert.Equal(0.25, interval.Rate, precision: 5);
        Assert.Equal(3, interval.Successes);
        Assert.Equal(12, interval.Trials);
    }

    [Fact]
    public void A_small_sample_produces_a_wider_interval_than_a_large_one_at_the_same_rate()
    {
        // The single property the whole feature exists for: 3/16 and 300/1600 are the same rate
        // and must not be equally trusted.
        var small = Interval.Wilson(3, 16);
        var large = Interval.Wilson(300, 1600);

        Assert.Equal(small.Rate, large.Rate, precision: 5);
        Assert.True(small.Margin > large.Margin * 5);
    }

    [Fact]
    public void An_all_or_nothing_result_still_carries_uncertainty()
    {
        // The specific failure of the normal approximation this type exists to avoid: p ± z·
        // sqrt(p(1-p)/n) evaluates to exactly zero width at p=0 and p=1, reporting a 4-trial
        // sample as absolute certainty.
        var none = Interval.Wilson(0, 4);
        var all = Interval.Wilson(4, 4);

        Assert.Equal(0.0, none.Rate, precision: 5);
        Assert.True(none.High > 0.3, $"0/4 upper bound was {none.High}, implausibly tight");

        Assert.Equal(1.0, all.Rate, precision: 5);
        Assert.True(all.Low < 0.7, $"4/4 lower bound was {all.Low}, implausibly tight");
    }

    [Fact]
    public void Bounds_never_escape_the_unit_interval()
    {
        // The other normal-approximation failure: at extreme rates it produces bounds below 0 or
        // above 1, which are not rates at all.
        for (var trials = 1; trials <= 40; trials++)
        {
            for (var successes = 0; successes <= trials; successes++)
            {
                var interval = Interval.Wilson(successes, trials);
                Assert.InRange(interval.Low, 0.0, 1.0);
                Assert.InRange(interval.High, 0.0, 1.0);
                Assert.True(interval.Low <= interval.High);
            }
        }
    }

    [Fact]
    public void Zero_trials_reports_maximal_uncertainty_rather_than_a_rate_of_zero()
    {
        // A card never drawn in a batch has no rate. Reporting 0.0 with a tight interval would
        // make it look like the worst card in the set instead of an unmeasured one.
        var interval = Interval.Wilson(0, 0);

        Assert.Equal(0.0, interval.Low, precision: 5);
        Assert.Equal(1.0, interval.High, precision: 5);
        Assert.False(interval.Excludes(0.5));
    }

    [Fact]
    public void Excludes_answers_whether_a_reference_rate_is_ruled_out()
    {
        // 60% on 20 games cannot rule out an even matchup; 60% on 2000 games can. This is the
        // call Program.cs makes when it reports whether an advantage is real.
        Assert.False(Interval.Wilson(12, 20).Excludes(0.5));
        Assert.True(Interval.Wilson(1200, 2000).Excludes(0.5));
    }

    [Fact]
    public void Successes_may_not_exceed_trials()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Interval.Wilson(5, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interval.Wilson(-1, 4));
    }

    [Fact]
    public void Wilson_matches_a_known_reference_value()
    {
        // Anchors the arithmetic to an externally checkable case rather than only to its own
        // properties: the standard 95% Wilson interval for 8/10 is approximately [0.490, 0.943].
        var interval = Interval.Wilson(8, 10);

        Assert.Equal(0.4901, interval.Low, precision: 3);
        Assert.Equal(0.9433, interval.High, precision: 3);
    }
}

public class MeanEstimateTests
{
    [Fact]
    public void Mean_and_sample_standard_deviation_use_the_bessel_correction()
    {
        // [2, 4, 4, 4, 5, 5, 7, 9] -- mean 5, population sd 2, SAMPLE sd 2.138. Getting this
        // wrong (dividing by n instead of n-1) understates the interval on small batches, which
        // is exactly where the overstated confidence would do damage.
        var estimate = MeanEstimate.From([2, 4, 4, 4, 5, 5, 7, 9]);

        Assert.Equal(5.0, estimate.Mean, precision: 5);
        Assert.Equal(2.1381, estimate.StandardDeviation, precision: 3);
        Assert.Equal(8, estimate.Count);
    }

    [Fact]
    public void A_larger_sample_tightens_the_interval_around_the_same_mean()
    {
        var small = MeanEstimate.From([4.0, 6.0, 4.0, 6.0]);
        var large = MeanEstimate.From(Enumerable.Range(0, 400).Select(i => i % 2 == 0 ? 4.0 : 6.0));

        Assert.Equal(small.Mean, large.Mean, precision: 5);
        Assert.True(large.High - large.Low < small.High - small.Low);
    }

    [Fact]
    public void Excludes_defaults_to_testing_against_zero()
    {
        // The margin question: is this seat advantage real, or is zero still on the table?
        var consistent = MeanEstimate.From(Enumerable.Repeat(3.0, 30).Select((v, i) => v + (i % 2)));
        var noisy = MeanEstimate.From([-10.0, 12.0, -8.0, 9.0]);

        Assert.True(consistent.Excludes());
        Assert.False(noisy.Excludes());
    }

    [Fact]
    public void A_single_sample_reports_its_value_with_no_spread()
    {
        // n-1 would divide by zero. Reporting a zero-width interval at the mean is honest
        // because Count = 1 travels with it.
        var estimate = MeanEstimate.From([5.0]);

        Assert.Equal(5.0, estimate.Mean, precision: 5);
        Assert.Equal(0.0, estimate.StandardDeviation, precision: 5);
        Assert.Equal(1, estimate.Count);
    }

    [Fact]
    public void An_empty_sample_reports_zero_count_rather_than_throwing()
    {
        var estimate = MeanEstimate.From([]);

        Assert.Equal(0, estimate.Count);
        Assert.Equal(0.0, estimate.Mean, precision: 5);
    }
}
