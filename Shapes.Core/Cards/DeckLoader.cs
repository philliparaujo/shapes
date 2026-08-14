using Shapes.Core.Rules;

namespace Shapes.Core.Cards;

// Reads a decklist from a text file.
//
// FORMAT -- one card per line, `cardId count` or bare `cardId` (implying 1):
//
//     # a comment
//     shieldbearer 3
//     basic_circle 2
//     bubbles
//
// Plain text rather than JSON deliberately: a decklist is hand-edited far more often than it is
// machine-generated, and the whole file is a two-column list. JSON would add quoting and braces
// to every line of the format's only real use case. Blank lines and `#` comments are allowed so a
// decklist can be annotated with why a card is in it, which is the note a balance experiment
// wants to keep next to the list rather than in a separate file.
//
// Validation is the caller's: this parses, then hands the result to DeckBuilder.Custom, which
// applies the ruleset's size/copy limits. Splitting it that way means a decklist can be READ
// (for inspection or reporting) without a ruleset in hand.
public static class DeckLoader
{
    public static Deck FromFile(string path, CardDatabase cards, RuleSet rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rules);

        if (!File.Exists(path))
        {
            throw new DeckBuildException($"Decklist file not found: {path}");
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return DeckBuilder.Custom(name, ParseLines(File.ReadAllLines(path), path), cards, rules);
    }

    public static Deck FromText(string name, string text, CardDatabase cards, RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(text);

        return DeckBuilder.Custom(
            name, ParseLines(text.Split('\n'), "<text>"), cards, rules);
    }

    // Expands `cardId count` lines into the flat card list a Deck holds. Line numbers are carried
    // into every error message: a 40-card decklist with one typo is otherwise a hunt, and the
    // parse failure is the last point where the line number still exists.
    private static List<string> ParseLines(IReadOnlyList<string> lines, string source)
    {
        var cardIds = new List<string>();

        for (var lineNumber = 1; lineNumber <= lines.Count; lineNumber++)
        {
            var line = lines[lineNumber - 1];

            // Strip comments before trimming so a trailing `# note` on a card line works, not
            // only whole-line comments.
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var count = 1;
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1], out count) || count < 1)
                {
                    throw new DeckBuildException(
                        $"{source} line {lineNumber}: expected a positive count, got '{parts[1]}'.");
                }
            }
            else if (parts.Length != 1)
            {
                throw new DeckBuildException(
                    $"{source} line {lineNumber}: expected 'cardId' or 'cardId count', got '{line}'.");
            }

            for (var i = 0; i < count; i++)
            {
                cardIds.Add(parts[0]);
            }
        }

        if (cardIds.Count == 0)
        {
            throw new DeckBuildException($"{source} contains no cards.");
        }

        return cardIds;
    }
}
