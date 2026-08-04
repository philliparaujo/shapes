using Shapes.Core.Effects.Ops;

namespace Shapes.Core.Effects;

// The single source of truth for "what ops exist".
//
// Both the interpreter's dispatch and the card-schema loader's "unknown op fails loudly"
// validation (step 1.7) query this same registry, rather than each maintaining its own list
// of op names. Two lists that are supposed to agree will eventually disagree; one list cannot.
//
// Adding an op: implement EffectOp, add one line to AllOps below. Removing one: delete the
// line and the class. Nothing else references op names as string literals outside this file
// and the op's own class.
public static class EffectRegistry
{
    private static readonly IReadOnlyList<EffectOp> AllOps =
    [
        new DamageOp(),
        new DamageScaledOp(),

        new HealOp(),
        new HealToFullOp(),
        new HealScaledOp(),
        new SetHealthOp(),
        new BuffMaxHealthOp(),
        new SelfDamageOp(),

        new DrawOp(),
        new DrawScaledOp(),
        new DiscardOp(),
        new DrawUpToOp(),

        new GainResourceOp(),
        new GainResourceScaledOp(),
        new GainNextTurnOp(),

        new DestroyOp(),
        new DestroyRefundCostOp(),
        new SummonOp(),

        new GrantKeywordOp(),
        new StunOp(),
        new OnNextDamageTakenOp(),
        new OnNextRicochetOp(),

        new AttackBuffOp(),
        new NextAttackBonusOp(),
        new NextDamageTakenBonusOp(),

        new ConditionalOp(),
        new ForEachOp(),
    ];

    private static readonly IReadOnlyDictionary<string, EffectOp> ByName =
        AllOps.ToDictionary(op => op.Name);

    public static bool IsKnown(string opName) => ByName.ContainsKey(opName);

    public static IEnumerable<string> KnownOpNames => ByName.Keys;

    public static EffectOp Resolve(string opName)
    {
        if (!ByName.TryGetValue(opName, out var op))
        {
            throw new UnknownEffectOpException(opName);
        }

        return op;
    }
}

// Thrown when an EffectNode names an op the registry doesn't know. Step 1.7's card loader
// catches this at load time so a typo in card JSON fails loudly there, not silently at play
// time deep in a game.
public sealed class UnknownEffectOpException : Exception
{
    public string OpName { get; }

    public UnknownEffectOpException(string opName)
        : base($"Unknown effect op '{opName}'.")
    {
        OpName = opName;
    }
}
