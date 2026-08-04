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

    public bool BoolOrDefault(string key, bool fallback) =>
        Has(key) ? Convert.ToBoolean(Require(key)) : fallback;

    public TargetSelector Target(string key = "target") => ParseSelector(String(key));

    public static TargetSelector ParseSelector(string raw) => raw switch
    {
        "self" => TargetSelector.Self,
        "opposing" => TargetSelector.Opposing,
        "left_friendly" => TargetSelector.LeftFriendly,
        "right_friendly" => TargetSelector.RightFriendly,
        "all_enemies" => TargetSelector.AllEnemies,
        "all_friendlies" => TargetSelector.AllFriendlies,
        "chosen_enemy" => TargetSelector.ChosenEnemy,
        "chosen_friendly" => TargetSelector.ChosenFriendly,
        _ => throw new ArgumentException($"Unknown target selector '{raw}'."),
    };

    public EffectNode Node(string key) => (EffectNode)Require(key)!;

    public IReadOnlyList<EffectNode> Nodes(string key) => (IReadOnlyList<EffectNode>)Require(key)!;

    // The nodes under `key`, whether it holds one node or a list of them.
    //
    // Control-flow arguments are not uniform: "condition" is a single node while "then",
    // "else", and "effects" are lists. Card validation has to walk every nested effect
    // regardless of which shape it is stored in -- a second chosen_* target hidden inside a
    // conditional's else branch must not load clean -- so it needs one accessor that does not
    // care. Returns empty for a key holding neither, rather than throwing: the validator asks
    // about keys speculatively, and an "amount" that happens to share a name would otherwise
    // be an error rather than simply not an effect list.
    public IReadOnlyList<EffectNode> NodesOrSingle(string key)
    {
        if (!_values.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            EffectNode node => [node],
            IReadOnlyList<EffectNode> nodes => nodes,
            _ => [],
        };
    }

    private object Require(string key)
    {
        if (!_values.TryGetValue(key, out var value) || value is null)
        {
            throw new ArgumentException($"Effect is missing required argument '{key}'.");
        }

        return value;
    }
}
