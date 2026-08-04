using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shapes.Core.Cards;

// Wire format for a card file. Mirrors the JSON exactly; CardDefinition is the validated
// domain type. Same split, and the same reasons, as RuleSetDto vs RuleSet.
//
// Nullable everywhere so a missing key is distinguishable from an explicit zero -- but unlike
// a ruleset, a card has no "defaults" to fall back on: a creature with no health is a data
// error, not an inherited value. CardValidator reports which key is missing.
internal sealed class CardDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; }

    public CostDto? Cost { get; set; }

    public int? Health { get; set; }
    public List<string>? Types { get; set; }

    public List<MoveDto>? Moves { get; set; }

    // Spells only: the effect list resolved on play.
    public List<EffectDto>? Effects { get; set; }
}

internal sealed class CostDto
{
    public int? Spike { get; set; }
    public int? Anvil { get; set; }
    public int? Wheel { get; set; }
}

internal sealed class MoveDto
{
    public string? Name { get; set; }
    public CostDto? Cost { get; set; }
    public EffectDto? Condition { get; set; }
    public List<EffectDto>? Effects { get; set; }
}

// One effect node. "op" names the effect; every other key is an argument whose meaning depends
// on that op, so the rest of the object is captured as raw JSON via [JsonExtensionData] rather
// than modelled per-op.
//
// This is the one place the "reject unknown properties" rule cannot apply, because for an
// effect there is no fixed property set to compare against -- "amount" is unknown to the
// schema but meaningful to the op. The guard that replaces it is CardValidator's check that
// the op itself is known to EffectRegistry, plus each op's own accessors throwing on a missing
// required argument. A misspelled ARGUMENT (e.g. "amnount") is therefore caught by the op, not
// here -- which is why the per-card smoke test in step 1.10 (every move actually usable) is
// load-bearing rather than a nicety.
internal sealed class EffectDto
{
    public string? Op { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Args { get; set; }

    // Nested effect lists for control-flow ops (conditional's then/else, for_each's effects)
    // arrive inside Args as raw JSON arrays and are converted by CardLoader.
}

// Source-generated serialization context -- required for the iOS AOT export, same as
// RuleSetJsonContext. See that file for why UnmappedMemberHandling must be declared on the
// attribute rather than on a separately built JsonSerializerOptions.
//
// Cards are hand-authored data, so unknown properties are rejected: "helth: 3" would otherwise
// silently produce a 0-health creature that fails much later and much less clearly.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CardDto))]
[JsonSerializable(typeof(List<CardDto>))]
// Declared so nested control-flow effect lists can be deserialized element-by-element from a
// JsonElement without falling back to the reflection-based serializer, which iOS AOT forbids.
[JsonSerializable(typeof(EffectDto))]
internal sealed partial class CardJsonContext : JsonSerializerContext
{
}
