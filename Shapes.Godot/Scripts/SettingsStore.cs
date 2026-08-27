using System;
using System.Text.Json;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The Godot-specific half of settings persistence (DESIGN.md D4) -- user:// file I/O for the
// player's audio levels. AudioSettings itself lives in Shapes.Godot.Adapter (pure, testable
// outside the editor); this class is the thin wrapper that reads and writes it, since
// System.IO.File does not resolve a user:// path -- only Godot's FileAccess API does.
//
// Exactly the DeckStore/MatchSaveStore split, for exactly the same reason, and cached for the
// same reason DeckStore is: the settings panel re-reads on every open and AudioDirector reads on
// startup, and re-parsing a two-field document for that is wasted work on a file only this
// process writes. Save writes through the cache, so the cached copy and the file cannot diverge.
//
// C3 (persistence: "decks, settings, progress") named settings as one of the three durable
// categories but shipped only decks and progress, there being no setting to store yet. This is
// that third category arriving with its first real occupant.
public static class SettingsStore
{
    private const string SavePath = "user://settings.json";

    private static AudioSettings? _cached;

    // The player's audio levels, loaded once then served from memory. Never null -- a missing or
    // unreadable file yields defaults (3/3) rather than nothing, so a first launch and a corrupt
    // file both land on a working, audible configuration.
    public static AudioSettings Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        _cached = ReadFromDisk() ?? new AudioSettings();
        return _cached;
    }

    // Persists the whole document. Called on every level change rather than behind an explicit
    // Apply button -- the same "a mobile OS can kill the process with no graceful-shutdown hook"
    // reasoning DeckStore.Save documents, and a volume the player set once and lost is a
    // particularly annoying thing to have to set again.
    public static void Save(AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _cached = settings;

        var json = JsonSerializer.Serialize(settings, AudioSettingsJsonContext.Default.AudioSettings);

        using var file = global::Godot.FileAccess.Open(SavePath, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null)
        {
            // Best-effort, as DeckStore.Save: a failed write means the setting is lost on the next
            // launch, not that the volume slider should stop working now. The in-memory cache
            // keeps this session coherent either way.
            var error = global::Godot.FileAccess.GetOpenError();
            GD.PushWarning($"SettingsStore.Save failed: {error}");
            return;
        }

        file.StoreString(json);
    }

    // Null on any failure to read or parse -- a corrupt file is treated as "no settings yet"
    // rather than as a reason to block audio, matching DeckStore/MatchSaveStore. AudioSettings
    // clamps its own levels on assignment, so even a syntactically valid file with a level of 99
    // lands in range rather than reaching the mixer.
    private static AudioSettings? ReadFromDisk()
    {
        if (!global::Godot.FileAccess.FileExists(SavePath))
        {
            return null;
        }

        using var file = global::Godot.FileAccess.Open(SavePath, global::Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            var error = global::Godot.FileAccess.GetOpenError();
            GD.PushWarning($"SettingsStore.Load failed: {error}");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                file.GetAsText(), AudioSettingsJsonContext.Default.AudioSettings);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            GD.PushWarning($"SettingsStore.Load: saved settings were unreadable ({ex.Message}).");
            return null;
        }
    }
}
