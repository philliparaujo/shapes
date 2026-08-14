using Shapes.Core.Cards;
using Shapes.Core.Rules;

namespace Shapes.Sim;

// One (agentOne, agentTwo) seat assignment played out over N seeded games, aggregated. A
// pairing's mirror (agentTwo, agentOne) is a DIFFERENT PairingSummary, never folded into this
// one -- PLAN.md step 1 requires seats reported separately since pooling them hides first-player
// advantage.
public sealed class PairingSummary
{
    public required string AgentOne { get; init; }

    public required string AgentTwo { get; init; }

    public required IReadOnlyList<GameResult> Games { get; init; }

    public int GameCount => Games.Count;

    public int AgentOneWins => Games.Count(g => g.AgentOneWon);

    public double AgentOneWinRate => (double)AgentOneWins / GameCount;

    public double AverageTurnCount => Games.Average(g => g.TurnCount);

    public double AverageActionCount => Games.Average(g => g.ActionCount);
}

public sealed class BatchResult
{
    public required IReadOnlyList<PairingSummary> Pairings { get; init; }

    public required IReadOnlyList<GameResult> AllGames { get; init; }
}

// Builds the full agent-vs-agent matrix: every ordered pairing (including an agent against
// itself) across both seat assignments, run for the configured game count. "Every pairing, both
// seats reported separately" is PLAN.md step 3.1's exit bar -- this is the one place that
// assembles it.
public static class BatchRunner
{
    // onGameCompleted, if given, is invoked (completed, total) after each game finishes -- from
    // whichever worker thread finished it, so callers must be thread-safe (Program.cs's use just
    // rewrites a console line under a lock). Optional and last so it doesn't disturb existing
    // callers/tests that only care about the result.
    // `decks` supplies each game's two decklists. Defaults to the one-of-each default deck, which
    // is what every pre-deck balance run played -- so an existing caller passing none gets the
    // batch it got before.
    public static BatchResult Run(
        SimOptions options, CardDatabase cards, RuleSet rules,
        Action<int, int>? onGameCompleted = null, DeckProvider? decks = null)
    {
        decks ??= new DeckProvider(DeckSource.Default, cards, rules);

        var pairingSpecs = new List<(string One, string Two)>();
        foreach (var one in options.Agents)
        {
            foreach (var two in options.Agents)
            {
                pairingSpecs.Add((one, two));
            }
        }

        var allGames = new System.Collections.Concurrent.ConcurrentBag<GameResult>();
        var gamesByPairing = pairingSpecs.ToDictionary(
            p => p, _ => new System.Collections.Concurrent.ConcurrentBag<GameResult>());

        var jobs = new List<(string One, string Two, ulong Seed)>();
        foreach (var (one, two) in pairingSpecs)
        {
            for (var i = 0; i < options.Games; i++)
            {
                // Each job gets a distinct seed derived from the base seed, pairing index, and
                // game index, so every game across the whole matrix is reproducible and none
                // collide -- two different pairings never accidentally replay the same game.
                var seed = DeriveSeed(options.Seed, one, two, i);
                jobs.Add((one, two, seed));
            }
        }

        var parallelOptions = new ParallelOptions();
        if (options.MaxDegreeOfParallelism is { } max)
        {
            parallelOptions.MaxDegreeOfParallelism = max;
        }

        var completed = 0;
        Parallel.ForEach(jobs, parallelOptions, job =>
        {
            // Decks are derived from the game's own seed, not drawn from a shared stream, so a
            // game gets the same decks regardless of the order Parallel.ForEach happens to run it
            // in -- see DeckProvider.DecksFor.
            var (deckOne, deckTwo) = decks.DecksFor(job.Seed);
            var result = GameRunner.Play(
                job.One, job.Two, job.Seed, cards, rules, options.Iterations, deckOne, deckTwo);
            allGames.Add(result);
            gamesByPairing[(job.One, job.Two)].Add(result);

            onGameCompleted?.Invoke(Interlocked.Increment(ref completed), jobs.Count);
        });

        var pairings = pairingSpecs
            .Select(p => new PairingSummary
            {
                AgentOne = p.One,
                AgentTwo = p.Two,
                Games = [.. gamesByPairing[p].OrderBy(g => g.Seed)],
            })
            .ToList();

        return new BatchResult
        {
            Pairings = pairings,
            AllGames = [.. allGames.OrderBy(g => g.AgentOne).ThenBy(g => g.AgentTwo).ThenBy(g => g.Seed)],
        };
    }

    private static ulong DeriveSeed(ulong baseSeed, string one, string two, int gameIndex)
    {
        var hash = baseSeed;
        hash = MixIn(hash, one);
        hash = MixIn(hash, two);
        hash = MixIn(hash, (ulong)gameIndex);
        return hash == 0 ? 1 : hash;
    }

    private static ulong MixIn(ulong hash, string value)
    {
        foreach (var c in value)
        {
            hash = MixIn(hash, c);
        }

        return hash;
    }

    // FNV-1a-style mix. Not cryptographic -- only needs to spread distinct (agent, agent, index)
    // tuples across the seed space so games don't collide, not to resist adversarial input.
    private static ulong MixIn(ulong hash, ulong value)
    {
        hash ^= value;
        hash *= 1099511628211UL;
        return hash;
    }
}
