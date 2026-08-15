using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// Keyword highlighting/explainers: KeywordText must find exactly the status keywords a rendered
// rules string actually names, in the reference's order, and must report each occurrence's span so
// the renderer can bold the word in place.
//
// The strings asserted here are real EffectText output shapes (see EffectTextTests for where each
// comes from), not invented phrasings -- detection runs over rendered text rather than the effect
// tree, so a test against hand-written prose would pass while the real pipeline silently missed
// every keyword.
public class KeywordTextTests
{
    [Fact]
    public void All_four_reference_keywords_are_defined_with_reminder_text()
    {
        Assert.Equal(
            ["Reflect", "Ricochet", "Taunt", "Stun"],
            KeywordText.All.Select(k => k.Name));

        Assert.All(KeywordText.All, k => Assert.NotEmpty(k.Reminder));
    }

    [Fact]
    public void Finds_a_keyword_in_a_grant_clause()
    {
        var found = Assert.Single(KeywordText.Find("Gain reflect."));
        Assert.Equal("Reflect", found.Name);
    }

    [Fact]
    public void Finds_a_keyword_regardless_of_case()
    {
        // The sentence-joiner capitalizes a clause that opens a move, so the same word arrives
        // both ways depending on where it fell.
        Assert.Equal("Taunt", Assert.Single(KeywordText.Find("Taunt until your next turn.")).Name);
        Assert.Equal("Taunt", Assert.Single(KeywordText.Find("Gain taunt until your next turn.")).Name);
    }

    [Fact]
    public void Finds_the_rider_form_of_stun()
    {
        var found = Assert.Single(KeywordText.Find("Deal 3 and stun."));
        Assert.Equal("Stun", found.Name);
    }

    [Fact]
    public void Finds_inflected_forms()
    {
        // "next time this ricochets" (on_next_ricochet) and StatusIcons' "Stunned" badge tooltip.
        Assert.Equal("Ricochet", Assert.Single(KeywordText.Find("Gain 3 next time this ricochets.")).Name);
        Assert.Equal("Stun", Assert.Single(KeywordText.Find("Stunned")).Name);
    }

    [Fact]
    public void Reports_each_keyword_once_however_often_it_appears()
    {
        var found = Assert.Single(
            KeywordText.Find("Gain reflect. Your left friendly gains reflect."));

        Assert.Equal("Reflect", found.Name);
    }

    [Fact]
    public void Reports_several_keywords_in_reference_order()
    {
        // Circle Captain's Wardance grants both; the reference lists Reflect before Ricochet, and
        // the stack must follow that rather than order of appearance in the sentence.
        Assert.Equal(
            ["Reflect", "Ricochet"],
            KeywordText.Find("Gain ricochet left. Your left friendly gains reflect.")
                .Select(k => k.Name));
    }

    [Fact]
    public void Ignores_a_keyword_embedded_in_a_longer_word()
    {
        Assert.Empty(KeywordText.Find("Reflection stunt taunted-ish"));
    }

    [Fact]
    public void Text_with_no_keywords_finds_nothing()
    {
        Assert.Empty(KeywordText.Find("Deal 2 to this. Draw 1."));
        Assert.Empty(KeywordText.Find(string.Empty));
        Assert.Empty(KeywordText.Find(null));
    }

    [Fact]
    public void FindAll_merges_across_strings_without_duplicating()
    {
        // A card's explainer stack covers its spell effects and every move description at once.
        var found = KeywordText.FindAll(
            [string.Empty, "Gain reflect.", "Deal 3 and stun.", "Gain reflect."]);

        Assert.Equal(["Reflect", "Stun"], found.Select(k => k.Name));
    }

    [Fact]
    public void Match_reports_the_span_of_the_inflected_form()
    {
        // The renderer bolds Length characters starting at the index, so an inflection must report
        // its OWN length -- "ricochets" is 9, not the keyword name's 8. A short span would leave a
        // stray unbolded "s" behind.
        const string text = "Gain 3 next time this ricochets.";
        var index = text.IndexOf("ricochets", StringComparison.Ordinal);

        var match = Assert.NotNull(KeywordText.Match(text, index));
        Assert.Equal("Ricochet", match.Entry.Name);
        Assert.Equal("ricochets".Length, match.Length);
    }

    [Fact]
    public void Match_only_fires_at_the_start_of_the_word()
    {
        const string text = "Gain reflect.";
        var index = text.IndexOf("reflect", StringComparison.Ordinal);

        Assert.NotNull(KeywordText.Match(text, index));

        // One character in is mid-word, so nothing starts there.
        Assert.Null(KeywordText.Match(text, index + 1));
    }

    [Fact]
    public void Match_is_null_outside_the_string()
    {
        Assert.Null(KeywordText.Match("Gain reflect.", -1));
        Assert.Null(KeywordText.Match("Gain reflect.", 99));
    }
}
