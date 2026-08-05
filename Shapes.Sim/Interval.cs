namespace Shapes.Sim;

// A proportion reported with the uncertainty that comes with its sample size.
//
// Phase 4 exists to rank cards and rules against each other, and a bare rate cannot be ranked: at
// 20 games a 3/16 win rate and a 300/1600 win rate both print as "0.19" while meaning entirely
// different things. Every rate in MetricsReport therefore carries its own interval, so a sweep
// can tell "this card is an outlier" from "this card has not been played enough to say."
//
// Wilson score interval, not the textbook normal approximation (p ± z·sqrt(p(1-p)/n)), which is
// wrong in exactly the cases balance work runs into: it produces bounds outside [0,1] for
// near-0/near-1 rates, and collapses to a zero-width interval at p=0 or p=1 -- so a card played
// 4 times and losing all 4 would report "0% ± 0", the most confident-looking number in the file
// and the least justified. Wilson stays inside [0,1] and keeps a wide interval at the extremes.
public readonly record struct Interval
{
    // 1.959964 = the standard normal quantile for a two-sided 95% interval. Hard-coded rather
    // than parameterized: one confidence level across the whole report keeps intervals
    // comparable between metrics, which is the entire point of attaching them.
    private const double Z = 1.959964;

    public required double Rate { get; init; }

    public required double Low { get; init; }

    public required double High { get; init; }

    public required int Successes { get; init; }

    public required int Trials { get; init; }

    // Half the interval's width -- the "± this much" a reader wants when scanning for outliers.
    // Wilson intervals are asymmetric about Rate, so this is a summary of the width, not a value
    // you can add to and subtract from Rate to recover Low/High. Read Low/High for that.
    public double Margin => (High - Low) / 2.0;

    // Whether the interval excludes a reference rate -- the "is this actually an outlier"
    // question, asked directly instead of eyeballed. Balance work almost always compares against
    // 0.5 (an even matchup), so that is the default.
    public bool Excludes(double reference = 0.5) => reference < Low || reference > High;

    // Zero trials is not an error -- a card that was never drawn in a batch legitimately has no
    // rate -- so it reports the maximally uninformative interval [0,1] rather than throwing or
    // pretending to a rate of 0. A reader (or a sweep's outlier filter) sees a full-width
    // interval and correctly concludes nothing can be said.
    public static Interval Wilson(int successes, int trials)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfNegative(trials);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successes, trials);

        if (trials == 0)
        {
            return new Interval { Rate = 0.0, Low = 0.0, High = 1.0, Successes = 0, Trials = 0 };
        }

        var n = (double)trials;
        var p = successes / n;
        var z2 = Z * Z;

        var denominator = 1.0 + (z2 / n);
        var centre = (p + (z2 / (2.0 * n))) / denominator;
        var spread = Z / denominator * Math.Sqrt((p * (1.0 - p) / n) + (z2 / (4.0 * n * n)));

        return new Interval
        {
            Rate = p,
            Low = Math.Max(0.0, centre - spread),
            High = Math.Min(1.0, centre + spread),
            Successes = successes,
            Trials = trials,
        };
    }

    public override string ToString() =>
        Trials == 0 ? "n/a (0 trials)" : $"{Rate:P1} [{Low:P1}, {High:P1}] n={Trials}";
}

// A mean reported with its uncertainty, the continuous counterpart of Interval -- for score
// margin, game length, and per-turn resource levels, where the quantity is not a proportion.
//
// Normal-approximation interval on the mean (mean ± z·sd/sqrt(n)), which is appropriate here in
// a way it is not for proportions: there is no [0,1] boundary to overshoot, and the quantities
// involved (turn counts, score margins) are sums of many per-turn contributions, so the central
// limit theorem is doing honest work rather than being invoked as a formality.
public readonly record struct MeanEstimate
{
    private const double Z = 1.959964;

    public required double Mean { get; init; }

    // Sample standard deviation (Bessel-corrected, n-1). Reported alongside the interval because
    // it answers a different question: the interval says how well the mean is pinned down, the
    // standard deviation says how much individual games actually vary. A balance change can move
    // one without the other -- e.g. tightening variance while leaving the average untouched.
    public required double StandardDeviation { get; init; }

    public required double Low { get; init; }

    public required double High { get; init; }

    public required int Count { get; init; }

    public static MeanEstimate From(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var samples = values as IReadOnlyList<double> ?? values.ToList();

        if (samples.Count == 0)
        {
            return new MeanEstimate
            {
                Mean = 0.0, StandardDeviation = 0.0, Low = 0.0, High = 0.0, Count = 0,
            };
        }

        var mean = samples.Average();

        // A single sample has a mean but no spread to estimate (n-1 would divide by zero), so it
        // reports a zero-width interval AT the mean. That is honest rather than misleading: the
        // Count = 1 alongside it is the signal not to trust it.
        if (samples.Count == 1)
        {
            return new MeanEstimate
            {
                Mean = mean, StandardDeviation = 0.0, Low = mean, High = mean, Count = 1,
            };
        }

        var variance = samples.Sum(v => (v - mean) * (v - mean)) / (samples.Count - 1);
        var sd = Math.Sqrt(variance);
        var standardError = sd / Math.Sqrt(samples.Count);

        return new MeanEstimate
        {
            Mean = mean,
            StandardDeviation = sd,
            Low = mean - (Z * standardError),
            High = mean + (Z * standardError),
            Count = samples.Count,
        };
    }

    // Whether the interval excludes a reference value. Defaults to zero, the reference that
    // matters most here: a score margin whose interval excludes 0 is a real seat advantage, not
    // noise.
    public bool Excludes(double reference = 0.0) => reference < Low || reference > High;

    public override string ToString() =>
        Count == 0 ? "n/a (0 samples)" : $"{Mean:F2} [{Low:F2}, {High:F2}] sd={StandardDeviation:F2} n={Count}";
}
