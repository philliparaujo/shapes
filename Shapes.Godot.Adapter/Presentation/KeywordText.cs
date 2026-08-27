using System.Diagnostics.CodeAnalysis;

namespace Shapes.Godot.Adapter;

// One keyword's name and its reminder text, for the small explainer panels that stack above a
// card's hover tooltip and for the bolding of the keyword where it appears in rules text.
public sealed record KeywordEntry(string Name, string Reminder);

// The four status keywords, their reminder text, and where they occur in a synthesized rules
// string.
//
// The reminder text is transcribed from the Keywords table at the bottom of references/card text
// formatting.txt -- it is the only text in this codebase that is authored rather than synthesized,
// and deliberately so. Everything EffectText produces describes what one card DOES; a keyword
// reminder describes a RULE that card invokes, which lives in the ruleset and not in any card's
// effect tree, so there is nothing to synthesize it from. That is also why it sits here in the
// adapter rather than in Shapes.Core: it is presentation-only reminder text, and Core already
// carries the mechanic itself as KeywordFlags/CombatResolver.
//
// Detection is over the RENDERED text, not the effect tree, for the same reason the tooltip
// renders CardText rather than CardDefinition: the two callers (a hand card's face, a merged
// board creature's tooltip) hold strings that came from several different places -- a card's
// spell effects, each move's description, StatusIcons' synthesized trigger text -- and a
// tree-walking detector would need a separate path for each. Matching the words EffectText
// actually emitted catches all of them with one rule, and cannot report a keyword the player
// can't see: if the phrasing ever changes so the word stops appearing, the highlight and the
// explainer disappear together rather than the panel claiming a keyword the text doesn't mention.
public static class KeywordText
{
    // Ordered as references/card text formatting.txt lists them, which is roughly how often they
    // come up. Find preserves this order, so a card carrying two keywords always stacks its
    // explainers the same way round rather than in whatever order the sentences happened to fall.
    public static IReadOnlyList<KeywordEntry> All { get; } =
    [
        new("Reflect", "The next damage taken is ignored."),
        new("Ricochet", "The next damage taken is dealt to the left/right friendly, if possible."),
        new("Taunt", "All enemy creature attacks target this creature."),
        new("Stun", "No moves can be used on your opponent's next turn."),
    ];

    // Every spelling of each keyword that can appear in synthesized text, longest first.
    //
    // Inflections are listed explicitly rather than matched by a prefix rule: a prefix match on
    // "stun" also fires on a hypothetical "stunt", and EffectText's vocabulary is small and closed
    // enough that enumerating the handful of real forms is both safer and easier to audit against
    // it. The forms below are exactly what EffectText emits today -- "gains ricochet left",
    // "and stun", "next time this ricochets", "Stunned" from StatusIcons' badge tooltips.
    //
    // Longest-first matters because Match takes the first form that fits at a position: with
    // "stun" ahead of "stunned", "Stunned" would bold only its first four letters and leave a
    // bare "ned" behind.
    private static readonly (string Form, KeywordEntry Entry)[] Forms =
    [
        .. All.SelectMany(entry => InflectionsOf(entry.Name).Select(form => (form, entry)))
              .OrderByDescending(pair => pair.form.Length),
    ];

    private static IEnumerable<string> InflectionsOf(string name) => name switch
    {
        "Ricochet" => [name, $"{name}s"],
        "Stun" => [name, $"{name}s", $"{name}ned"],
        "Reflect" => [name, $"{name}s"],
        _ => [name],
    };

    // The keywords `text` mentions, in All's order, without duplicates -- what the tooltip stacks
    // as explainer panels. Null/empty text yields nothing rather than throwing, since a spell's
    // effects string is routinely empty (a creature has none).
    public static IReadOnlyList<KeywordEntry> Find(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < text.Length; i++)
        {
            if (Match(text, i) is { } match)
            {
                found.Add(match.Entry.Name);
                i += match.Length - 1;
            }
        }

        return [.. All.Where(entry => found.Contains(entry.Name))];
    }

    // Find over several strings at once, still deduplicated and still in All's order -- a card's
    // explainer stack covers its spell effects AND every move description, and the same keyword
    // appearing in two of them must not stack two identical panels.
    public static IReadOnlyList<KeywordEntry> FindAll(IEnumerable<string?> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            foreach (var entry in Find(text))
            {
                found.Add(entry.Name);
            }
        }

        return [.. All.Where(entry => found.Contains(entry.Name))];
    }

    // The keyword occurrence starting exactly at `index`, or null if none does. Used by the
    // renderer to bold the word in place, which is why it reports the matched LENGTH rather than
    // just the entry -- the caller has to know how much of the string the bold run covers, and
    // that is the matched inflection's length ("ricochets"), not the keyword name's.
    //
    // Case-insensitive, because the same word is capitalized or not depending on where the
    // sentence-joiner put it ("Gain reflect" vs "Reflect" opening a clause), but WORD-BOUNDED on
    // both sides so a keyword inside a longer word is never highlighted.
    public static KeywordMatch? Match(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (index < 0 || index >= text.Length)
        {
            return null;
        }

        // A keyword must start a word: "reflect" inside "reflection" is not an occurrence.
        if (index > 0 && IsWordCharacter(text[index - 1]))
        {
            return null;
        }

        foreach (var (form, entry) in Forms)
        {
            if (index + form.Length > text.Length
                || string.Compare(text, index, form, 0, form.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            var after = index + form.Length;
            if (after < text.Length && IsWordCharacter(text[after]))
            {
                continue;
            }

            return new KeywordMatch(entry, form.Length);
        }

        return null;
    }

    // Letters only. Digits and the sentinel control characters InlineResourceIcons splices in are
    // deliberately NOT word characters: an inline resource icon can sit flush against a keyword
    // and must not suppress its highlight.
    private static bool IsWordCharacter(char c) => char.IsLetter(c);
}

// One keyword occurrence found in rules text: which keyword, and how many characters of the
// string it spans (the inflected form's length, not the keyword name's).
[SuppressMessage(
    "Naming", "CA1815:Override equals and operator equals on value types",
    Justification = "Record struct; equality is compiler-generated.")]
public readonly record struct KeywordMatch(KeywordEntry Entry, int Length);
