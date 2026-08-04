namespace Shapes.Core.Effects;

// One parsed effect: an op name plus its arguments, e.g.
// { "op": "damage", "target": "opposing", "amount": 1 } becomes
// new EffectNode("damage", args-with-target-and-amount).
//
// This is the interpreter's input shape -- deliberately independent of JSON, so the card
// loader (step 1.7) is the only place that knows about System.Text.Json, and synthetic test
// cards can build a move's effect list by hand without going through a JSON string.
public sealed class EffectNode
{
    public string Op { get; }

    public EffectArgs Args { get; }

    public EffectNode(string op, EffectArgs? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(op);
        Op = op;
        Args = args ?? EffectArgs.Empty;
    }
}
