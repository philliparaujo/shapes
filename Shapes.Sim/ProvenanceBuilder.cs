using System.Security.Cryptography;
using System.Text;
using Shapes.Core.Cards;
using Shapes.Core.Rules;

namespace Shapes.Sim;

// Builds the RunProvenance stamped onto every metrics report written to disk.
//
// Exists as its own type rather than a MetricsReport method because the card-set fingerprint
// needs the card JSON on disk, and MetricsReport is deliberately a pure aggregation over
// GameResults with no I/O.
public static class ProvenanceBuilder
{
    public static RunProvenance Build(SimOptions options, CardDatabase cards, RuleSet rules, string cardsDir)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rules);

        return new RunProvenance
        {
            Agents = options.Agents,
            GamesPerPairing = options.Games,
            BaseSeed = options.Seed,
            Iterations = options.Iterations,
            RuleSetName = rules.Name,
            CardSetHash = HashCardSet(cardsDir),
            CardCount = cards.Count,
            RunAtUtc = DateTimeOffset.UtcNow,
        };
    }

    // SHA-256 over every card file's CONTENT, in filename order, with each file's bytes preceded
    // by its name. Hashing content rather than the loaded object graph means any edit to any card
    // changes the hash without CardDefinition needing to implement structural equality; ordering
    // by name (not by directory-enumeration order, which is filesystem-dependent) makes the same
    // content hash the same on any machine.
    //
    // Whitespace-sensitive, so a purely cosmetic reformat of a card file registers as a content
    // change. That is the safe direction to err: a spurious "content differs" prompts a rerun,
    // whereas a missed one silently invalidates a paired comparison.
    private static string HashCardSet(string cardsDir)
    {
        if (!Directory.Exists(cardsDir))
        {
            return "unknown";
        }

        var files = Directory.GetFiles(cardsDir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);

        using var sha = SHA256.Create();
        using var stream = new MemoryStream();

        foreach (var file in files)
        {
            var nameBytes = Encoding.UTF8.GetBytes(Path.GetFileName(file));
            stream.Write(nameBytes);
            stream.Write(File.ReadAllBytes(file));
        }

        stream.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(stream))[..16].ToLowerInvariant();
    }
}
