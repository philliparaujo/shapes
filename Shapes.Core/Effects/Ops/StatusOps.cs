using Shapes.Core.Primitives;

namespace Shapes.Core.Effects.Ops;

// { "op": "grant_keyword", "target": "self", "keyword": "taunt" }
// { "op": "grant_keyword", "target": "self", "keyword": "ricochet", "direction": "left" }
//
// Ricochet requires a "direction" arg (left/right); taunt and reflect ignore it.
internal sealed class GrantKeywordOp : EffectOp
{
    public override string Name => "grant_keyword";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var keyword = args.String("keyword");

        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            var creature = ctx.State.Board[slot];
            if (creature is null)
            {
                continue;
            }

            switch (keyword)
            {
                case "taunt":
                    creature.GrantKeyword(KeywordFlags.Taunt);
                    break;
                case "reflect":
                    creature.GrantKeyword(KeywordFlags.Reflect);
                    break;
                case "ricochet":
                    creature.GrantRicochet(ParseDirection(args.String("direction")));
                    break;
                default:
                    throw new ArgumentException($"Unknown keyword '{keyword}'.");
            }
        }
    }

    private static RicochetDirection ParseDirection(string raw) => raw switch
    {
        "left" => RicochetDirection.Left,
        "right" => RicochetDirection.Right,
        _ => throw new ArgumentException($"Unknown ricochet direction '{raw}'."),
    };
}

// { "op": "stun", "target": "opposing" } -- prevents the target from using moves until the
// start of its controller's next turn (cleared alongside ResetMovesForNewTurn).
internal sealed class StunOp : EffectOp
{
    public override string Name => "stun";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.Stun();
        }
    }
}
