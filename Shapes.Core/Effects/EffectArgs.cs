namespace Shapes.Core.Effects;

// Loosely-typed argument bag for one effect op, e.g. { "op": "damage", "target": "opposing",
// "amount": 1 }. Deliberately not a strongly-typed class per op: with ~20 ops that would mean
// 20 near-identical DTOs, and the card JSON loader (step 1.7) is the layer that should own
// parsing/validation, not the interpreter.
//
// Values are stored as object (int, double, string, bool, or nested EffectNode/EffectNode[]
// for control-flow ops) so this same bag works for every op. Accessors throw a clear message
// on a missing or wrong-typed key rather than a raw InvalidCastException -- card data errors
// should be loud, per the "fail loudly on load" principle, though most of that validation
// happens earlier, at card-load time.
public sealed class EffectArgs
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    public static readonly EffectArgs Empty = new(new Dictionary<string, object?>());

    public EffectArgs(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public int Int(string key) => Convert.ToInt32(Require(key));

    public int IntOrDefault(string key, int fallback) => Has(key) ? Int(key) : fallback;

    public string String(string key) => (string)Require(key)!;

    public string StringOrDefault(string key, string fallback) =>
        Has(key) ? String(key) : fallback;

    public TargetSelector Target(string key = "target") => ParseSelector(String(key));

    public static TargetSelector ParseSelector(string raw) => raw switch
    {
        "self" => TargetSelector.Self,
        "opposing" => TargetSelector.Opposing,
        "left_friendly" => TargetSelector.LeftFriendly,
        "all_enemies" => TargetSelector.AllEnemies,
        "all_friendlies" => TargetSelector.AllFriendlies,
        "chosen_enemy" => TargetSelector.ChosenEnemy,
        "chosen_friendly" => TargetSelector.ChosenFriendly,
        _ => throw new ArgumentException($"Unknown target selector '{raw}'."),
    };

    public EffectNode Node(string key) => (EffectNode)Require(key)!;

    public IReadOnlyList<EffectNode> Nodes(string key) => (IReadOnlyList<EffectNode>)Require(key)!;

    private object Require(string key)
    {
        if (!_values.TryGetValue(key, out var value) || value is null)
        {
            throw new ArgumentException($"Effect is missing required argument '{key}'.");
        }

        return value;
    }
}
