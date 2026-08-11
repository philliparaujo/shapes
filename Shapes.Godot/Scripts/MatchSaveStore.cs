using System;
using System.Text.Json;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// PLAN.md C6: the Godot-specific half of interrupted-game persistence -- user:// file I/O.
// SavedMatch/SavedMatchDto/SavedMatchJsonContext live in Shapes.Godot.Adapter (pure, testable
// outside the editor, same reasoning as GameSession itself); this class is the thin wrapper
// that actually reads/writes them, since System.IO.File does not resolve a user:// path --
// only Godot's own FileAccess API does.
//
// One slot, not a save-per-match list: this client has exactly one game in flight at a time
// (no multiplayer, no save browser), so "the current match" is the whole feature C6 asked for.
public static class MatchSaveStore
{
    private const string SavePath = "user://match_save.json";

    public static bool Exists() => global::Godot.FileAccess.FileExists(SavePath);

    // Called after every submitted action (GameRoot.Submit/RunAiTurns), not just on pause/quit
    // -- a mobile OS can kill the process without ever running a graceful-shutdown hook, so the
    // only save that survives that is one written continuously as the game is played.
    public static void Save(SavedMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var json = JsonSerializer.Serialize(match.ToDto(), SavedMatchJsonContext.Default.SavedMatchDto);

        using var file = global::Godot.FileAccess.Open(SavePath, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null)
        {
            // Best-effort: a failed save means a worse resume next launch, not a crash now --
            // GetOpenError() is logged for whoever's debugging a real device's storage state,
            // never surfaced to the player as a blocking error.
            var error = global::Godot.FileAccess.GetOpenError();
            GD.PushWarning($"MatchSaveStore.Save failed: {error}");
            return;
        }

        file.StoreString(json);
    }

    // Null on any failure to load or parse -- a corrupt or missing save is treated as "nothing
    // to resume," never as a reason to block starting a new game.
    public static SavedMatch? Load()
    {
        if (!Exists())
        {
            return null;
        }

        using var file = global::Godot.FileAccess.Open(SavePath, global::Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            var error = global::Godot.FileAccess.GetOpenError();
            GD.PushWarning($"MatchSaveStore.Load failed: {error}");
            return null;
        }

        var json = file.GetAsText();

        try
        {
            var dto = JsonSerializer.Deserialize(json, SavedMatchJsonContext.Default.SavedMatchDto);
            return dto is null ? null : SavedMatch.FromDto(dto);
        }
        catch (Exception ex) when (ex is JsonException or System.IO.InvalidDataException or ArgumentException)
        {
            GD.PushWarning($"MatchSaveStore.Load: saved match was unreadable ({ex.Message}).");
            return null;
        }
    }

    // Called once a match reaches game-over -- a finished game has nothing left to resume, and
    // leaving the file behind would resurrect a dead game the next time the app launches.
    public static void Clear()
    {
        if (Exists())
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        }
    }
}
