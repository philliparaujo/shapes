using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shapes.Core.Rules;

// Wire format for a ruleset file. Kept separate from RuleSet so the domain type stays
// immutable and validated while this mirrors the JSON exactly.
//
// Every field is nullable: that is how a missing key is told apart from an explicit zero.
// A missing key falls back to the default ruleset; an explicit wrong value is an error.
internal sealed class RuleSetDto
{
    public string? Name { get; set; }

    public int? StartingHandSize { get; set; }
    public int? CardsDrawnPerTurn { get; set; }
    public int? HandLimit { get; set; }

    public ResourcePoolDto? BaseIncome { get; set; }
    public int? IncomePerCreatureType { get; set; }

    public int? PointsPerUnopposedCreature { get; set; }
    public int? ScoreToWin { get; set; }

    public bool? MergeEnabled { get; set; }
    public bool? MergeRequiresAdjacent { get; set; }
    public bool? MergeCostsAction { get; set; }
    public int? MaxMergeDepth { get; set; }

    public string? DeckMode { get; set; }
    public int? CopiesPerCard { get; set; }
    public int? DeckSize { get; set; }
    public int? MaxCopiesPerCard { get; set; }

    public TypeChartDto? TypeChart { get; set; }
}

internal sealed class ResourcePoolDto
{
    public int? Spike { get; set; }
    public int? Anvil { get; set; }
    public int? Wheel { get; set; }
}

internal sealed class TypeChartDto
{
    public double? WeaknessMultiplier { get; set; }
    public Dictionary<string, string>? Cycle { get; set; }
}

// Source-generated serialization context.
//
// iOS export requires AOT compilation, which rules out the reflection-based serializer.
// Declaring the types here makes System.Text.Json generate the binding at compile time, so
// no reflection is needed at runtime and the trim analyzer stays quiet.
// UnmappedMemberHandling.Disallow must be declared here rather than on a separately built
// JsonSerializerOptions: deserializing through a source-generated JsonTypeInfo uses the
// context's own options, so settings passed alongside it are silently ignored.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RuleSetDto))]
internal sealed partial class RuleSetJsonContext : JsonSerializerContext
{
}
