using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Adapter;

// DESIGN.md C6: what a resumable game needs on disk. Seed + action log, not a serialized
// GameState -- picked deliberately over the plan's other option because replaying the log
// through the existing GameSession/ActionExecutor/ActionGenerator pipeline rides on Phase 1's
// determinism guarantee (the same one MCTS and console/Godot parity already depend on) rather
// than needing a new, separately-verified round-trip contract for every GameState field,
// including CreatureInstance's two untyped object? trigger fields (PendingOnNextDamageTaken/
// PendingOnNextRicochet) which have no JSON shape of their own and would otherwise force a
// half-serialized state -- precisely the failure mode this step's own plan text warns against.
// GameAction is already a flat, five-case, fully-value-equal type (Shapes.Core/Actions/
// GameAction.cs), so one DTO with nullable fields per case covers all of it.
//
// The DECKLISTS are saved alongside the seed and the log, and they have to be: replay re-runs the
// seeded stream from the deal onwards, so a game resumed against a different deck than it was
// dealt from desyncs silently (see GameSession.Resume's note). Stored as flat card-id lists --
// the exact order Deck.Cards had, not a re-derived one -- because that pre-shuffle order is an
// input to the seeded shuffle, so preserving the decklist's CONTENT but not its ORDER would
// reproduce a different game just as surely as the wrong deck would.
//
// Null decks (a game played on the default decklist) round-trip as null, so saves written before
// per-seat decks existed still load: a missing field deserializes to null and resumes on the
// default deck, which is exactly what those games were played with.
public sealed record SavedMatch(
    ulong Seed, SeatConfig PlayerOne, SeatConfig PlayerTwo, IReadOnlyList<GameAction> Actions,
    SavedDeckList? DeckOne = null, SavedDeckList? DeckTwo = null)
{
    public static SavedMatch FromDto(SavedMatchDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(dto.PlayerOne);
        ArgumentNullException.ThrowIfNull(dto.PlayerTwo);
        ArgumentNullException.ThrowIfNull(dto.Actions);

        return new SavedMatch(
            dto.Seed,
            SeatDto.ToSeatConfig(dto.PlayerOne),
            SeatDto.ToSeatConfig(dto.PlayerTwo),
            [.. dto.Actions.Select(ActionDto.ToGameAction)],
            SavedDeckList.FromDto(dto.DeckOne),
            SavedDeckList.FromDto(dto.DeckTwo));
    }

    public SavedMatchDto ToDto() => new()
    {
        Seed = Seed,
        PlayerOne = SeatDto.FromSeatConfig(PlayerOne),
        PlayerTwo = SeatDto.FromSeatConfig(PlayerTwo),
        Actions = [.. Actions.Select(ActionDto.FromGameAction)],
        DeckOne = DeckOne?.ToDto(),
        DeckTwo = DeckTwo?.ToDto(),
    };
}

// One seat's decklist as saved: the deck's name and its cards in their exact pre-shuffle order.
//
// Deliberately NOT reusing SavedDeck (the deckbuilder's id->count shape). That type exists to be
// EDITED, so it stores counts and re-derives an order on demand; this one exists to REPRODUCE a
// specific deal, so it stores the flat order verbatim. Collapsing the two would mean a resumed
// game's deck order came from SavedDeck.ToCardIds' sort rather than from what was actually
// shuffled -- identical content, different game.
public sealed record SavedDeckList(string Name, IReadOnlyList<string> Cards)
{
    public static SavedDeckList Of(Deck deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        return new SavedDeckList(deck.Name, [.. deck.Cards]);
    }

    public Deck ToDeck() => new(Name, Cards);

    public static SavedDeckList? FromDto(DeckListDto? dto)
    {
        if (dto?.Cards is null || dto.Cards.Count == 0)
        {
            return null;
        }

        return new SavedDeckList(dto.Name ?? "custom", dto.Cards);
    }

    public DeckListDto ToDto() => new() { Name = Name, Cards = [.. Cards] };
}

public sealed class DeckListDto
{
    public string? Name { get; set; }
    public List<string>? Cards { get; set; }
}

public sealed class SavedMatchDto
{
    public ulong Seed { get; set; }
    public SeatDto? PlayerOne { get; set; }
    public SeatDto? PlayerTwo { get; set; }
    public List<ActionDto>? Actions { get; set; }

    // Absent in saves written before per-seat decks -- see SavedMatch's header on why null is
    // the correct reading of a missing deck rather than a load failure.
    public DeckListDto? DeckOne { get; set; }
    public DeckListDto? DeckTwo { get; set; }
}

// Mirrors SeatConfig (AgentKind + Iterations) exactly -- Human needs no iteration count, but
// carrying it (as 0, SeatConfig.Human's own value) keeps this DTO a flat struct-of-scalars
// rather than needing a discriminated shape for one field.
public sealed class SeatDto
{
    public string? Kind { get; set; }
    public int Iterations { get; set; }

    public static SeatDto FromSeatConfig(SeatConfig seat) => new()
    {
        Kind = seat.Kind.ToString(),
        Iterations = seat.Iterations,
    };

    public static SeatConfig ToSeatConfig(SeatDto dto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.Kind);
        var kind = Enum.Parse<AgentKind>(dto.Kind);
        return new SeatConfig(kind, dto.Iterations);
    }
}

// One GameAction, in the flat shape every concrete subtype already has (Shapes.Core/Actions/
// GameAction.cs) -- Kind picks which of the nullable fields apply, the same discriminator
// GameAction.Kind itself already exposes, so this DTO adds no vocabulary the domain type
// didn't already have.
public sealed class ActionDto
{
    public string? Kind { get; set; }
    public int Player { get; set; }
    public string? CardId { get; set; }
    public int? SourceSlotOwner { get; set; }
    public int? SourceSlot { get; set; }
    public int? TargetSlotOwner { get; set; }
    public int? TargetSlot { get; set; }
    public int? ChosenTargetOwner { get; set; }
    public int? ChosenTarget { get; set; }
    public int? MoveIndex { get; set; }

    public static ActionDto FromGameAction(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dto = new ActionDto
        {
            Kind = action.Kind.ToString(),
            Player = (int)action.Player,
        };

        switch (action)
        {
            case PlayCardAction play:
                dto.CardId = play.CardId;
                SetSlot(v => (dto.TargetSlotOwner, dto.TargetSlot) = v, play.TargetSlot);
                SetSlot(v => (dto.ChosenTargetOwner, dto.ChosenTarget) = v, play.ChosenTarget);
                break;
            case UseMoveAction move:
                SetSlot(v => (dto.SourceSlotOwner, dto.SourceSlot) = v, move.SourceSlot);
                dto.MoveIndex = move.MoveIndex;
                SetSlot(v => (dto.ChosenTargetOwner, dto.ChosenTarget) = v, move.ChosenTarget);
                break;
            case MergeAction merge:
                SetSlot(v => (dto.SourceSlotOwner, dto.SourceSlot) = v, merge.SourceSlot);
                SetSlot(v => (dto.TargetSlotOwner, dto.TargetSlot) = v, merge.TargetSlot);
                break;
            case DiscardAction discard:
                dto.CardId = discard.CardId;
                break;
            case EndTurnAction:
                break;
        }

        return dto;
    }

    public static GameAction ToGameAction(ActionDto dto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.Kind);
        var kind = Enum.Parse<ActionKind>(dto.Kind);
        var player = (PlayerId)dto.Player;

        return kind switch
        {
            ActionKind.PlayCard => new PlayCardAction(
                player,
                dto.CardId ?? throw new InvalidDataException("PlayCard action missing CardId."),
                GetSlot(dto.TargetSlotOwner, dto.TargetSlot),
                GetSlot(dto.ChosenTargetOwner, dto.ChosenTarget)),
            ActionKind.UseMove => new UseMoveAction(
                player,
                GetSlot(dto.SourceSlotOwner, dto.SourceSlot)
                    ?? throw new InvalidDataException("UseMove action missing SourceSlot."),
                dto.MoveIndex ?? throw new InvalidDataException("UseMove action missing MoveIndex."),
                GetSlot(dto.ChosenTargetOwner, dto.ChosenTarget)),
            ActionKind.Merge => new MergeAction(
                player,
                GetSlot(dto.SourceSlotOwner, dto.SourceSlot)
                    ?? throw new InvalidDataException("Merge action missing SourceSlot."),
                GetSlot(dto.TargetSlotOwner, dto.TargetSlot)
                    ?? throw new InvalidDataException("Merge action missing TargetSlot.")),
            ActionKind.Discard => new DiscardAction(
                player, dto.CardId ?? throw new InvalidDataException("Discard action missing CardId.")),
            ActionKind.EndTurn => new EndTurnAction(player),
            _ => throw new InvalidDataException($"Unknown action kind '{dto.Kind}'."),
        };
    }

    private static void SetSlot(Action<(int?, int?)> assign, SlotIndex? slot) =>
        assign(slot is { } s ? ((int?)s.Owner, (int?)s.Slot) : (null, null));

    private static SlotIndex? GetSlot(int? owner, int? slot) =>
        owner is { } o && slot is { } s ? new SlotIndex((PlayerId)o, s) : null;
}

// Source-generated serialization context -- required for the iOS AOT export, same as
// CardJsonContext/RuleSetJsonContext. UnmappedMemberHandling is not set to Disallow here,
// unlike card data: a save file is machine-written by THIS client, not hand-authored, so an
// unknown key (e.g. from a future app version) should be ignored on load rather than fail the
// whole resume -- forward compatibility matters more than catching a typo nothing types by hand.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = false)]
[JsonSerializable(typeof(SavedMatchDto))]
[JsonSerializable(typeof(SeatDto))]
[JsonSerializable(typeof(ActionDto))]
[JsonSerializable(typeof(List<ActionDto>))]
[JsonSerializable(typeof(DeckListDto))]
[JsonSerializable(typeof(List<string>))]
public sealed partial class SavedMatchJsonContext : JsonSerializerContext
{
}
