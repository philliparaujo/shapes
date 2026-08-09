using Shapes.Core.Cards;

namespace Shapes.Tests.Godot;

// Art filenames must name real card ids (PLAN.md B1c).
//
// CardArt resolves res://art/cards/{cardId}.png and falls back to the geometric placeholder when
// there is no match -- deliberately, so the set can be filled one card at a time. That fallback
// is also what makes a typo invisible: art named for a card that does not exist renders as a
// placeholder rather than erroring, and looks exactly like a card whose art simply is not drawn
// yet. This test is the loud failure that absence of an error would otherwise cost.
//
// Only the art -> card direction is asserted. The reverse ("every card has art") is deliberately
// NOT checked while the set is incomplete; it would fail for the ~31 cards still on placeholders
// and say nothing that is not already obvious on screen.
public class CardArtTests
{
    // The art lives in Shapes.Godot, which is not a test dependency and copies nothing to the
    // test output -- so this walks up from the test assembly to the repo root rather than
    // reading AppContext.BaseDirectory the way the Content-backed suites do.
    private static string? FindArtDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Shapes.Godot", "art", "cards");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static CardDatabase LoadCards() =>
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    [Fact]
    public void Every_card_art_file_is_named_for_a_real_card_id()
    {
        var artDirectory = FindArtDirectory();
        if (artDirectory is null)
        {
            // No art directory yet is a valid state, not a failure -- B1c fills it incrementally.
            return;
        }

        var cards = LoadCards();
        var unmatched = Directory.GetFiles(artDirectory, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => id is not null && !cards.TryGet(id, out _))
            .ToList();

        Assert.True(
            unmatched.Count == 0,
            $"Card art must be named for a card ID, not a card's JSON filename or display name. "
            + $"No card has these IDs: {string.Join(", ", unmatched)}. "
            + $"Note IDs and filenames can differ -- safeguard.json carries the ID 'patch_up'.");
    }
}
