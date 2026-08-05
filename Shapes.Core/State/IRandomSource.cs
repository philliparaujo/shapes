namespace Shapes.Core.State;

// The single source of randomness in the engine.
//
// Nothing in Shapes.Core may call Random.Shared, DateTime.Now, or Guid.NewGuid. Every
// shuffle and every random choice goes through here, which is what makes a game reproducible
// from its seed -- required for debuggable bug reports, comparable balance runs, and MCTS
// determinization.
public interface IRandomSource
{
    // Uniform in [0, exclusiveMax).
    int Next(int exclusiveMax);

    // The seed this source was created with, so a game can record how to reproduce itself.
    ulong Seed { get; }

    // A fresh source with the same seed, positioned back at the start. Lets a caller replay
    // a sequence without knowing how the source is implemented.
    IRandomSource Restart();

    // An independent copy positioned exactly where this one is. Cloning a GameState must
    // clone its randomness too: a shared source would let a search rollout advance the real
    // game's RNG, so identical replays would silently stop being identical.
    IRandomSource Fork();
}

// Deterministic xorshift64* generator.
//
// Hand-rolled rather than System.Random because the algorithm must be stable across .NET
// versions and platforms: a balance run on Windows and the same seed replayed on a phone in
// Phase 5 have to produce identical games. System.Random makes no such guarantee.
public sealed class SeededRandom : IRandomSource
{
    private ulong _state;

    public ulong Seed { get; }

    public SeededRandom(ulong seed)
    {
        // A zero state is a fixed point for xorshift -- it would emit nothing but zeros --
        // so it is remapped rather than rejected, keeping seed 0 usable by callers.
        Seed = seed;
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    // Used by Fork to copy the live position as well as the seed.
    private SeededRandom(ulong seed, ulong state)
    {
        Seed = seed;
        _state = state;
    }

    public int Next(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exclusiveMax, 1);

        // Rejection sampling: taking a plain modulus would bias the low values whenever
        // exclusiveMax does not divide 2^32 evenly. Over millions of MCTS playouts that bias
        // is measurable, so it is worth the occasional retry.
        var bound = (uint)exclusiveMax;
        var limit = uint.MaxValue - (uint.MaxValue % bound);

        uint sample;
        do
        {
            sample = NextUInt32();
        }
        while (sample >= limit);

        return (int)(sample % bound);
    }

    public IRandomSource Restart() => new SeededRandom(Seed);

    public IRandomSource Fork() => new SeededRandom(Seed, _state);

    private uint NextUInt32()
    {
        // xorshift64*
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return (uint)((_state * 0x2545F4914F6CDD1DUL) >> 32);
    }
}
