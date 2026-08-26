using System;
using System.Text.Json;
using Godot;
using Shapes.Core.Cards;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The Godot-specific half of deck persistence (PLAN.md C2) -- user:// file I/O for the ten deck
// slots. SavedDeck/DeckSlots/DeckSlotsJsonContext live in Shapes.Godot.Adapter (pure, testable
// outside the editor); this class is the thin wrapper that actually reads and writes them, since
// System.IO.File does not resolve a user:// path -- only Godot's FileAccess API does. Exactly the
// MatchSaveStore split, for exactly the same reason.
//
// ONE document holding all ten slots, not ten files: the deckbuilder and the lobby both want
// "every deck I have" (a slot picker, a seat dropdown) far more often than they want one deck by
// name, so a single read is the common case and a per-file layout would just turn it into ten.
//
// CACHED after the first load. The lobby rebuilds its deck dropdowns every time it is shown and
// the deckbuilder re-reads on each slot switch; re-parsing the file for each of those is wasted
// work on a document that only this process ever writes. Save writes through the cache, so the
// cached copy and the file cannot diverge.
public static class DeckStore
{
    private const string SavePath = "user://decks.json";

    private static DeckSlots? _cached;

    // The ten slots, loaded once then served from memory. Never null and always exactly
    // DeckSlots.SlotCount entries (Normalize guarantees it), so callers index straight in.
    public static DeckSlots Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var slots = ReadFromDisk() ?? DeckSlots.Empty();
        EnsureStarterDeck(slots);
        _cached = slots;
        return _cached;
    }

    // Seeds slot 0 with a legal, ready-to-play "Default" deck (DeckBuilder.Starter) whenever that
    // slot is still untouched, so a new player opens the deckbuilder tab to a real deck rather
    // than ten empty slots -- the same starting point every player gets, since Starter's seed is
    // fixed. Only fires on an EMPTY slot 0: once a player has named or built something there, that
    // is their deck, and re-seeding over it on a later load would silently discard their work.
    private static void EnsureStarterDeck(DeckSlots slots)
    {
        var slot = slots.Slots[0];
        if (!slot.IsEmpty)
        {
            return;
        }

        var cards = ContentLoader.LoadCards();
        var starter = DeckBuilder.Starter(cards);

        slot.Name = starter.Name;
        foreach (var (cardId, count) in starter.CountsById())
        {
            slot.SetCopies(cardId, count);
        }

        Save(slots);
    }

    // Persists the whole document. Called on every deckbuilder edit that changes a deck (card
    // added/removed, renamed, deleted) rather than behind an explicit Save button -- the same
    // "a mobile OS can kill the process with no graceful-shutdown hook" reasoning MatchSaveStore
    // documents, and a deckbuilder is exactly where a player would lose the most work to it.
    public static void Save(DeckSlots slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        slots.Normalize();
        _cached = slots;

        var json = JsonSerializer.Serialize(slots, DeckSlotsJsonContext.Default.DeckSlots);

        using var file = global::Godot.FileAccess.Open(SavePath, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null)
        {
            // Best-effort, as MatchSaveStore.Save: a failed write means the edit is lost on the
            // next launch, not that the deckbuilder should stop working now. The in-memory cache
            // keeps this session coherent either way.
            var error = global::Godot.FileAccess.GetOpenError();
            GD.PushWarning($"DeckStore.Save failed: {error}");
            return;
        }

        file.StoreString(json);
    }

    // Null on any failure to read or parse -- a corrupt file is treated as "no decks yet" rather
    // than as a reason to block the deckbuilder, matching MatchSaveStore.Load. The alternative
    // (throwing) would leave a player with an unreadable file unable to open the tab at all,
    // with no way to fix it from inside the game.
    private static DeckSlots? ReadFromDisk()
    {
        if (!global::Godot.FileAccess.FileExists(SavePath))
        {
            return null;
        }

        using var file = global::Godot.FileAccess.Open(SavePath, global::Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            var error = global::Godot.FileAccess.GetOpenError();
            GD.PushWarning($"DeckStore.Load failed: {error}");
            return null;
        }

        try
        {
            var slots = JsonSerializer.Deserialize(file.GetAsText(), DeckSlotsJsonContext.Default.DeckSlots);
            slots?.Normalize();
            return slots;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            GD.PushWarning($"DeckStore.Load: saved decks were unreadable ({ex.Message}).");
            return null;
        }
    }
}
