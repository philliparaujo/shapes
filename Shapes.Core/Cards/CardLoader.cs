using System.Text.Json;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;

namespace Shapes.Core.Cards;

// Raised when card JSON cannot be turned into a valid CardDefinition. Always names the source
// file and, where it can, the card, move, and effect at fault -- a card set is edited by hand
// throughout Phase 3, so the message is the whole value of failing at load.
public sealed class CardLoadException : Exception
{
    public CardLoadException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

// Reads card JSON into CardDefinitions.
//
// The only place in the engine that knows about System.Text.Json for cards: EffectNode and
// EffectArgs are deliberately JSON-free so the interpreter and synthetic test cards never
// depend on this layer.
//
// Every file goes through CardValidator before it is returned, so an unknown op, an unknown
// selector, a negative amount, or a second chosen_* target fails here rather than mid-game.
public static class CardLoader
{
    // A single card object, or an array of them -- both forms are accepted so the content set
    // can be organised as one-file-per-card or grouped by theme without the loader caring.
    public static IReadOnlyList<CardDefinition> FromJson(string json, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        var where = sourceName is null ? string.Empty : $" in '{sourceName}'";

        List<CardDto> dtos;
        try
        {
            dtos = ParseDtos(json);
        }
        catch (JsonException ex)
        {
            throw new CardLoadException($"Malformed card JSON{where}: {ex.Message}", ex);
        }

        var cards = new List<CardDefinition>(dtos.Count);
        foreach (var dto in dtos)
        {
            cards.Add(ReadCard(dto, where));
        }

        return cards;
    }

    public static IReadOnlyList<CardDefinition> FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new CardLoadException($"Card file not found: '{path}'.");
        }

        return FromJson(File.ReadAllText(path), Path.GetFileName(path));
    }

    // Loads every *.json under a directory, recursively. This is how the shipped content set
    // is read; ids must be unique across the whole directory, which CardDatabase enforces.
    public static CardDatabase FromDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!Directory.Exists(path))
        {
            throw new CardLoadException($"Card directory not found: '{path}'.");
        }

        var cards = new List<CardDefinition>();

        // Ordered so a duplicate-id error names the same pair of files on every machine --
        // the filesystem's own enumeration order is not guaranteed to be stable.
        foreach (var file in Directory.GetFiles(path, "*.json", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            cards.AddRange(FromFile(file));
        }

        return new CardDatabase(cards);
    }

    private static List<CardDto> ParseDtos(string json)
    {
        // Peek at the first token rather than try-deserialize-and-catch: an array parsed as an
        // object throws, and catching that to retry would also swallow genuinely malformed
        // JSON and report it as the wrong shape.
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json), new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!reader.Read())
        {
            throw new CardLoadException("Card JSON was empty.");
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize(json, CardJsonContext.Default.ListCardDto)
                   ?? throw new CardLoadException("Card JSON array was empty.");
        }

        var single = JsonSerializer.Deserialize(json, CardJsonContext.Default.CardDto)
                     ?? throw new CardLoadException("Card JSON was empty.");

        return [single];
    }

    private static CardDefinition ReadCard(CardDto dto, string where)
    {
        var id = dto.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new CardLoadException($"A card{where} is missing its 'id'.");
        }

        // From here on the id is known, so every message can name the card.
        var at = $"Card '{id}'{where}";

        try
        {
            var kind = ReadKind(dto.Kind);

            var card = new CardDefinition(
                id: id,
                name: dto.Name ?? id,
                kind: kind,
                cost: ReadCost(dto.Cost),
                health: dto.Health ?? 0,
                types: ReadTypes(dto.Types),
                moves: ReadMoves(dto.Moves),
                effects: ReadEffects(dto.Effects));

            CardValidator.Validate(card);
            return card;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException
                                       or UnknownEffectOpException or CardValidationException)
        {
            throw new CardLoadException($"{at}: {ex.Message}", ex);
        }
    }

    private static CardKind ReadKind(string? raw) => raw?.ToLowerInvariant() switch
    {
        null => throw new ArgumentException("Missing 'kind'. Expected 'creature' or 'spell'."),
        "creature" => CardKind.Creature,
        "spell" => CardKind.Spell,
        _ => throw new ArgumentException($"Unknown card kind '{raw}'. Expected 'creature' or 'spell'."),
    };

    private static ResourcePool ReadCost(CostDto? dto)
    {
        if (dto is null)
        {
            return ResourcePool.Empty;
        }

        // ResourcePool's constructor rejects negatives, which is exactly the "costs are
        // non-negative" schema rule -- no separate check needed.
        return new ResourcePool(dto.Spike ?? 0, dto.Anvil ?? 0, dto.Wheel ?? 0);
    }

    private static TypeMask ReadTypes(List<string>? raw)
    {
        if (raw is null || raw.Count == 0)
        {
            return TypeMask.None;
        }

        var mask = TypeMask.None;
        foreach (var name in raw)
        {
            mask = mask.Union(TypeMask.Of(ParseResourceType(name)));
        }

        return mask;
    }

    internal static ResourceType ParseResourceType(string raw) => raw.ToLowerInvariant() switch
    {
        "spike" => ResourceType.Spike,
        "anvil" => ResourceType.Anvil,
        "wheel" => ResourceType.Wheel,
        _ => throw new ArgumentException($"Unknown resource type '{raw}'. Expected 'spike', 'anvil' or 'wheel'."),
    };

    private static IReadOnlyList<MoveDefinition> ReadMoves(List<MoveDto>? dtos)
    {
        if (dtos is null || dtos.Count == 0)
        {
            return [];
        }

        var moves = new List<MoveDefinition>(dtos.Count);
        foreach (var dto in dtos)
        {
            var name = dto.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A move is missing its 'name'.");
            }

            try
            {
                moves.Add(new MoveDefinition(
                    name: name,
                    cost: ReadCost(dto.Cost),
                    effects: ReadEffects(dto.Effects),
                    condition: dto.Condition is null ? null : ReadEffect(dto.Condition)));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new ArgumentException($"move '{name}': {ex.Message}", ex);
            }
        }

        return moves;
    }

    private static IReadOnlyList<EffectNode> ReadEffects(List<EffectDto>? dtos)
    {
        if (dtos is null || dtos.Count == 0)
        {
            return [];
        }

        var nodes = new List<EffectNode>(dtos.Count);
        foreach (var dto in dtos)
        {
            nodes.Add(ReadEffect(dto));
        }

        return nodes;
    }

    private static EffectNode ReadEffect(EffectDto dto)
    {
        var op = dto.Op;
        if (string.IsNullOrWhiteSpace(op))
        {
            throw new ArgumentException("An effect is missing its 'op'.");
        }

        var args = new Dictionary<string, object?>();
        foreach (var (key, value) in dto.Args ?? [])
        {
            args[key] = ReadArgValue(op, key, value);
        }

        return new EffectNode(op, new EffectArgs(args));
    }

    // Converts one raw JSON argument into the loosely-typed value EffectArgs expects.
    //
    // Arrays are the interesting case: an array of objects is a nested effect list (the
    // conditional/for_each control-flow ops), while an array of strings is a plain value --
    // "types": ["spike", "wheel"] on a summon. The two are told apart by element kind rather
    // than by argument name, so a future control-flow op needs no change here.
    private static object? ReadArgValue(string op, string key, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Number => value.TryGetInt32(out var i) ? i : value.GetDouble(),
        JsonValueKind.Object => ReadNestedNode(op, key, value),
        JsonValueKind.Array => ReadArray(op, key, value),
        _ => throw new ArgumentException($"effect '{op}' argument '{key}' has an unsupported value."),
    };

    private static object ReadArray(string op, string key, JsonElement array)
    {
        var isEffectList = array.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object);

        if (isEffectList)
        {
            var nodes = new List<EffectNode>();
            foreach (var element in array.EnumerateArray())
            {
                nodes.Add(ReadNestedNode(op, key, element));
            }

            return nodes;
        }

        // SummonOp's ParseTypes takes "spike,wheel", so a string array is joined rather than
        // kept as a list -- EffectArgs has no list-of-strings accessor and adding one for a
        // single op would widen the interface for no gain.
        return string.Join(",", array.EnumerateArray().Select(e => e.ToString()));
    }

    private static EffectNode ReadNestedNode(string op, string key, JsonElement element)
    {
        var dto = element.Deserialize(CardJsonContext.Default.EffectDto);
        if (dto is null)
        {
            throw new ArgumentException($"effect '{op}' argument '{key}' is not a valid nested effect.");
        }

        return ReadEffect(dto);
    }
}
