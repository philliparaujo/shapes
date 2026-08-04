using Shapes.Core.Primitives;

namespace Shapes.Core.Effects.Ops;

// { "op": "grant_keyword", "target": "self", "keyword": "taunt" }
// { "op": "grant_keyword", "target": "self", "keyword": "taunt", "until_next_turn": true }
// { "op": "grant_keyword", "target": "self", "keyword": "ricochet", "direction": "left" }
//
// Ricochet requires a "direction" arg (left/right); taunt and reflect ignore it.
// "until_next_turn" is taunt-only (Columns: "taunt until next turn") -- see
// CreatureInstance.GrantKeyword/ResetMovesForNewTurn for the expiry itself. Omitted or false
// means permanent, matching every other keyword grant.
internal sealed class GrantKeywordOp : EffectOp
{
    public override string Name => "grant_keyword";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var keyword = args.String("keyword");
        var untilNextTurn = args.BoolOrDefault("until_next_turn", false);

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
                    creature.GrantKeyword(KeywordFlags.Taunt, untilNextTurn);
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

// { "op": "on_next_damage_taken", "target": "self", "effect": { "op": "gain_resource", ... } }
//
// Arms a reactive trigger: the next time this creature takes damage from a creature-sourced
// attack, `effect` runs once (with "self" rebound to this creature), then the trigger clears.
// See CombatResolver.ApplyToTarget for the fire point -- placed after the damage itself lands,
// so a lethal hit still triggers (Zealot/Shieldbearer punish the attacker regardless of whether
// their own creature survives). Never fires from spell damage or from reflect/ricochet
// redirection landing on a DIFFERENT creature -- only a hit actually taken by the armed
// creature counts.
internal sealed class OnNextDamageTakenOp : EffectOp
{
    public override string Name => "on_next_damage_taken";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var effect = args.Node("effect");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.SetPendingOnNextDamageTaken(effect);
        }
    }
}

// { "op": "on_next_ricochet", "target": "self", "effect": { "op": "gain_resource", ... } }
//
// As on_next_damage_taken, but fires the next time an attack against this creature actually
// redirects via its OWN Ricochet keyword (Circle Bender: "when next attack ricochets, gain 3
// wheel"). Only fires when the redirect really happens -- if Ricochet's target side has no
// friendly neighbor, CombatResolver falls through to a normal hit and this does not fire or
// clear, staying armed for a future attack that does redirect.
internal sealed class OnNextRicochetOp : EffectOp
{
    public override string Name => "on_next_ricochet";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var effect = args.Node("effect");
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            ctx.State.Board[slot]?.SetPendingOnNextRicochet(effect);
        }
    }
}
