using Godot;
using Shapes.Core.Cards;

namespace Shapes.Godot.Scripts;

// Reads the shipped card set through res://, the one loading path that works identically on
// every export target.
//
// Every non-test call site used to build a real OS path with
// Path.Combine(AppContext.BaseDirectory, "Content", "cards") and hand it to
// CardLoader.FromDirectory. That is correct on desktop, where Shapes.Content's
// CopyToOutputDirectory puts a real folder next to the built DLLs -- but on Android, .NET
// assemblies publish into the APK's own zip, so AppContext.BaseDirectory does not point at a
// populated directory and Directory.Exists(cardsDir) is false. CardLoader.FromDirectory threw
// CardLoadException before returning, and every one of these call sites ran that load BEFORE
// wiring its screen's buttons (Lobby.cs's Home screen is the case that shipped this: a thrown
// exception mid-_Ready aborts the rest of the method silently in an exported release build, so
// every Pressed handler after the throw was simply never connected -- indistinguishable from
// "touch doesn't work" with no error surfaced anywhere).
//
// Getting this into res:// takes two changes, not one: Shapes.Godot.csproj's
// MirrorContentIntoResourceTree target copies Shapes.Content's cards/rulesets/cards-calibration
// JSON into this project's own Content\ folder at build time so it sits in the resource tree
// alongside Art\ and Audio\, and export_presets.cfg's include_filter now names it explicitly --
// Godot does not treat .json as an importable resource type, and "Export all resources" only
// covers files the import system recognizes, so a plain directory copy alone still ships an empty
// PCK entry for Content\.
//
// DirAccess.GetFilesAt, not ResourceLoader.ListDirectory: the latter only lists recognized
// Resources and silently omits raw included files like these .json ones (this is why
// AudioDirector's music fix used ListDirectory but this can't -- the two loaders are reading
// different categories of file). GetFilesAt is a one-level listing, which is sufficient because
// the card set has no subdirectories; if that ever changes, this needs either a per-subfolder
// call or a build-time-generated manifest, since recursive res:// listing has no reliable runtime
// API once a project is exported.
public static class ContentLoader
{
    private const string CardsDirectory = "res://Content/cards";

    // Mirrors CardLoader.FromDirectory's own contract (throws CardLoadException on a missing
    // directory or bad JSON) so every call site's existing error handling -- there isn't any,
    // deliberately; a missing card set is a packaging bug worth crashing loudly on -- stays
    // correct without each one needing to know this reads through Godot rather than System.IO.
    public static CardDatabase LoadCards()
    {
        var names = DirAccess.GetFilesAt(CardsDirectory);
        if (names.Length == 0)
        {
            // GetFilesAt returns an empty array both when the directory is missing and when it
            // exists but is empty -- the second case is exactly what an export_presets.cfg
            // include_filter regression looks like, so name that cause rather than leaving a
            // reader to rediscover it the way this bug was first found.
            throw new CardLoadException(
                $"No card files found under '{CardsDirectory}'. If this is an exported build, " +
                "check export_presets.cfg's include_filter covers Content/*.json.");
        }

        var cards = new List<CardDefinition>();

        // Ordered so a duplicate-id error names the same pair of files on every machine, the same
        // reasoning CardLoader.FromDirectory itself gives for sorting before parsing.
        foreach (var name in names.Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            var path = $"{CardsDirectory}/{name}";
            using var file = global::Godot.FileAccess.Open(path, global::Godot.FileAccess.ModeFlags.Read);
            if (file is null)
            {
                var error = global::Godot.FileAccess.GetOpenError();
                throw new CardLoadException($"Card file not found: '{path}' ({error}).");
            }

            cards.AddRange(CardLoader.FromJson(file.GetAsText(), path));
        }

        return new CardDatabase(cards);
    }
}
