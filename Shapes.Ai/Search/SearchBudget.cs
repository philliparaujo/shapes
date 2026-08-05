using System.Diagnostics;

namespace Shapes.Ai.Search;

// How long a search is allowed to run: an iteration cap, a wall-clock cap, or both.
//
// Two limits rather than one because the two callers want different things and neither is
// expressible in the other's terms. Phase 3's batch runner wants ITERATIONS -- a reproducible
// number of samples, so that two rulesets are compared at equal search effort rather than at
// equal CPU time on whatever machine happened to run them. Phase 4's client wants MILLISECONDS --
// a turn that comes back before the player gets bored, whatever the device manages in that time.
// PLAN.md step 2.8 names both ("budget by time *or* iteration count"); step 2.6 puts the seam in
// now so neither caller has to be rewritten when tuning arrives.
//
// A time budget makes a search NON-REPRODUCIBLE: the same seed will do a different number of
// iterations on a busy machine and reach a different decision. That is fine for interactive play
// and disqualifying for a balance measurement, which is why Iterations is the default and the one
// the tests use. IsDeterministic states which kind you are holding, so a caller that needs
// replayability can assert on it rather than hoping.
public readonly struct SearchBudget
{
    // Maximum iterations, or null for unlimited (only sensible alongside a time limit).
    public int? Iterations { get; }

    // Maximum wall-clock time, or null for unlimited.
    public TimeSpan? Duration { get; }

    private SearchBudget(int? iterations, TimeSpan? duration)
    {
        Iterations = iterations;
        Duration = duration;
    }

    // A fixed number of iterations: reproducible from a seed, and the form every test uses.
    public static SearchBudget OfIterations(int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        return new SearchBudget(iterations, null);
    }

    // A wall-clock budget, for interactive play. Deliberately still capped by iterations if the
    // caller supplies both -- see OfIterations/Within combinations below.
    public static SearchBudget OfTime(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return new SearchBudget(null, duration);
    }

    // Both limits: stop at whichever is reached first. The safe form for a client -- the
    // iteration cap keeps a fast machine from burning the whole time slice on a trivial position.
    public static SearchBudget Of(int iterations, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return new SearchBudget(iterations, duration);
    }

    // The default for a search built without a stated budget. Small enough that a full game runs
    // in seconds in a test, large enough that the search is visibly better than one iteration.
    // Tuning the real number is PLAN.md step 2.9's job, against measurements this cannot make.
    public static SearchBudget Default => OfIterations(1000);

    // Whether the same seed at the same position must produce the same decision. False the moment
    // a clock is involved -- how many iterations fit in 200ms is a property of the machine.
    public bool IsDeterministic => Duration is null;

    // True while the search may keep going. `elapsed` is only consulted when a duration was set,
    // so a pure-iteration budget never reads the clock at all (a Stopwatch query per iteration is
    // small but not free, and the deterministic path should not pay for a feature it does not
    // use).
    public bool Allows(int completedIterations, Stopwatch clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Iterations is { } cap && completedIterations >= cap)
        {
            return false;
        }

        return Duration is not { } limit || clock.Elapsed < limit;
    }

    public override string ToString() => (Iterations, Duration) switch
    {
        ({ } i, { } d) => $"{i} iterations or {d.TotalMilliseconds:0}ms",
        ({ } i, null) => $"{i} iterations",
        (null, { } d) => $"{d.TotalMilliseconds:0}ms",
        _ => "unlimited",
    };
}
