using System.Text.Json;
using Shapes.Core.Primitives;

namespace Shapes.Core.Rules;

// Raised when a ruleset file cannot be turned into a valid RuleSet. Always names the file.
public sealed class RuleSetLoadException : Exception
{
    public RuleSetLoadException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

// Reads ruleset JSON.
//
// Unknown properties are rejected rather than ignored. A typo like "scoreToWinn" would
// otherwise silently fall back to the default and a balance run would measure a ruleset
// nobody intended -- the expensive kind of bug, because the numbers still look plausible.
public static class RuleSetLoader
{
    public static RuleSet FromJson(string json, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        var where = sourceName is null ? string.Empty : $" in '{sourceName}'";

        RuleSetDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, RuleSetJsonContext.Default.RuleSetDto);
        }
        catch (JsonException ex)
        {
            throw new RuleSetLoadException($"Malformed ruleset JSON{where}: {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new RuleSetLoadException($"Ruleset JSON{where} was empty.");
        }

        var d = RuleSet.Default;

        try
        {
            return new RuleSet(
                name: dto.Name ?? d.Name,
                startingHandSize: dto.StartingHandSize ?? d.StartingHandSize,
                cardsDrawnPerTurn: dto.CardsDrawnPerTurn ?? d.CardsDrawnPerTurn,
                handLimit: ReadHandLimit(dto.HandLimit, d.HandLimit),
                baseIncome: ReadPool(dto.BaseIncome, d.BaseIncome),
                incomePerCreatureType: dto.IncomePerCreatureType ?? d.IncomePerCreatureType,
                pointsPerUnopposedCreature: dto.PointsPerUnopposedCreature ?? d.PointsPerUnopposedCreature,
                scoreToWin: dto.ScoreToWin ?? d.ScoreToWin,
                mergeEnabled: dto.MergeEnabled ?? d.MergeEnabled,
                mergeRequiresAdjacent: dto.MergeRequiresAdjacent ?? d.MergeRequiresAdjacent,
                mergeCostsAction: dto.MergeCostsAction ?? d.MergeCostsAction,
                maxMergeDepth: dto.MaxMergeDepth ?? d.MaxMergeDepth,
                deckMode: ReadDeckMode(dto.DeckMode, d.DeckMode),
                copiesPerCard: dto.CopiesPerCard ?? d.CopiesPerCard,
                deckSize: dto.DeckSize ?? d.DeckSize,
                maxCopiesPerCard: dto.MaxCopiesPerCard ?? d.MaxCopiesPerCard,
                typeChart: ReadTypeChart(dto.TypeChart, d.TypeChart),
                scoreByCreatureDelta: dto.ScoreByCreatureDelta ?? d.ScoreByCreatureDelta);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            // Turn the constructor's validation into a message that names the file.
            throw new RuleSetLoadException($"Invalid ruleset{where}: {ex.Message}", ex);
        }
    }

    public static RuleSet FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new RuleSetLoadException($"Ruleset file not found: '{path}'.");
        }

        return FromJson(File.ReadAllText(path), Path.GetFileName(path));
    }

    // handLimit is either a JSON number or the string "unlimited" (RuleSet.NoHandLimit) -- see
    // RuleSetDto.HandLimit for why this is untyped rather than plain int?.
    private static int ReadHandLimit(JsonElement? element, int fallback)
    {
        if (element is not { } value)
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var limit))
        {
            return limit;
        }

        if (value.ValueKind == JsonValueKind.String && value.GetString() == "unlimited")
        {
            return RuleSet.NoHandLimit;
        }

        throw new ArgumentException(
            $"handLimit must be a whole number or the string \"unlimited\", got '{value}'.");
    }

    private static ResourcePool ReadPool(ResourcePoolDto? dto, ResourcePool fallback)
    {
        if (dto is null)
        {
            return fallback;
        }

        return new ResourcePool(
            dto.Spike ?? fallback.Spike,
            dto.Anvil ?? fallback.Anvil,
            dto.Wheel ?? fallback.Wheel);
    }

    private static DeckMode ReadDeckMode(string? raw, DeckMode fallback)
    {
        if (raw is null)
        {
            return fallback;
        }

        return raw.ToLowerInvariant() switch
        {
            "symmetric" => DeckMode.Symmetric,
            "custom" => DeckMode.Custom,
            _ => throw new ArgumentException($"Unknown deck mode '{raw}'. Expected 'symmetric' or 'custom'."),
        };
    }

    private static TypeChart ReadTypeChart(TypeChartDto? dto, TypeChart fallback)
    {
        if (dto is null)
        {
            return fallback;
        }

        var multiplier = dto.WeaknessMultiplier ?? fallback.WeaknessMultiplier;

        // Overriding the multiplier alone should not require restating the whole cycle.
        if (dto.Cycle is null)
        {
            return fallback.With(multiplier);
        }

        var cycle = new Dictionary<ResourceType, ResourceType>();
        foreach (var (attacker, target) in dto.Cycle)
        {
            cycle[ParseType(attacker)] = ParseType(target);
        }

        return new TypeChart(cycle, multiplier);
    }

    private static ResourceType ParseType(string raw) => raw.ToLowerInvariant() switch
    {
        "spike" => ResourceType.Spike,
        "anvil" => ResourceType.Anvil,
        "wheel" => ResourceType.Wheel,
        _ => throw new ArgumentException($"Unknown resource type '{raw}'. Expected 'spike', 'anvil' or 'wheel'."),
    };
}
