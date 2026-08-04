using Shapes.Core.Effects;

namespace Shapes.Tests.Fixtures;

// Terse EffectNode/EffectArgs construction for tests, so a test reads close to the card JSON
// it stands in for: Eff.Node("damage", ("target", "opposing"), ("amount", 1)).
public static class Eff
{
    public static EffectNode Node(string op, params (string Key, object? Value)[] args) =>
        new(op, new EffectArgs(args.ToDictionary(a => a.Key, a => a.Value)));

    public static EffectNode Node(string op, EffectArgs args) => new(op, args);

    public static EffectArgs Args(params (string Key, object? Value)[] args) =>
        new(args.ToDictionary(a => a.Key, a => a.Value));
}
