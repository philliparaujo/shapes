namespace Shapes.Core.Effects;

// Renders an effect tree (a move's or spell's Effects list) into a short human-readable phrase,
// e.g. "deal 1 damage to opposing" or "self-damage 1; buff max health of right_friendly by 3".
//
// No card ever carries hand-authored description text (see PLAN.md Phase 4 step 4/5) -- the
// card JSON schema has no "text" field, deliberately, so a balance edit to an effect's numbers
// can never drift out of sync with a description someone forgot to update. This is therefore a
// SYNTHESIS from the op vocabulary, not a lookup, and is the one place that vocabulary is turned
// into English. Shared by the console's action log/BoardView and (per step 5) available to
// Shapes.Sim's report, so a phrasing fix only needs to happen once.
public static class EffectText
{
    public static string Describe(IReadOnlyList<EffectNode> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return effects.Count == 0
            ? string.Empty
            : string.Join("; ", effects.Select(DescribeNode));
    }

    // A move's gate plus its effects. A move-level `condition` decides whether the move can be
    // used at all (ConditionEvaluator, via legal-action generation) rather than branching inside
    // the effect list, so Describe(move.Effects) alone silently omits it -- and the omission is
    // invisible in a way a missing effect is not: "deal 1 damage to opposing" reads as a complete,
    // and badly underpowered, 1-cost move rather than a conditional one. Same phrasing as the
    // `conditional` op's own rendering, so the two forms read alike wherever they appear.
    public static string DescribeMove(EffectNode? condition, IReadOnlyList<EffectNode> effects)
    {
        var text = Describe(effects);
        if (condition is null)
        {
            return text;
        }

        return text.Length == 0
            ? $"only if {DescribeCondition(condition)}"
            : $"only if {DescribeCondition(condition)}: {text}";
    }

    private static string DescribeNode(EffectNode node)
    {
        var args = node.Args;
        return node.Op switch
        {
            "damage" => $"deal {args.Int("amount")} damage to {Sel(args)}",
            "damage_scaled" => $"deal damage ({Scale(args)}) to {Sel(args)}",

            "heal" => $"heal {Sel(args)} for {args.Int("amount")}",
            "heal_to_full" => $"heal {Sel(args)} to full",
            "heal_scaled" => $"heal {Sel(args)} ({Scale(args)})",
            "set_health" => $"set {Sel(args)}'s health to {args.Int("amount")}",
            "buff_max_health" => $"+{args.Int("amount")} max health to {Sel(args)}",
            "buff_max_health_scaled" => $"+max health ({Scale(args)}) to {Sel(args)}",
            "self_damage" => $"self-damage {args.Int("amount")}",

            "draw" => $"draw {args.Int("amount")}",
            "draw_scaled" => $"draw 1 per creature destroyed this turn"
                + (args.IntOrDefault("multiplier", 1) is var m && m != 1 ? $" x{m}" : string.Empty),
            "discard" => $"discard {args.Int("amount")}",
            "draw_up_to" => $"draw up to {args.Int("amount")} cards",

            "gain_resource" => $"gain {args.Int("amount")} {args.String("type")}",
            "gain_resource_scaled" => $"gain {args.String("type")} ({Scale(args)})",
            "gain_next_turn" => $"gain {args.Int("amount")} {args.String("type")} next turn",

            "destroy" => $"destroy {Sel(args)}",
            "destroy_refund_cost" => $"destroy {Sel(args)}, refunding its cost to its owner",
            "summon" => $"summon a {args.Int("health")}-health {args.String("types")} token into {Sel(args)}",

            "grant_keyword" => DescribeGrantKeyword(args),
            "stun" => $"stun {Sel(args)}",
            "on_next_damage_taken" => $"next time {Sel(args)} takes damage: {DescribeNode(args.Node("effect"))}",
            "on_next_ricochet" => $"next time {Sel(args)} ricochets: {DescribeNode(args.Node("effect"))}",

            "attack_buff" => $"+{args.Int("amount")} attack to {Sel(args)} (persistent)",
            "next_attack_bonus" => $"+{args.Int("amount")} to {Sel(args)}'s next attack",
            "next_damage_taken_bonus" => $"+{args.Int("amount")} to the next damage {Sel(args)} takes",

            "conditional" => DescribeConditional(args),
            "for_each" => DescribeForEach(args),

            _ => node.Op,
        };
    }

    private static string DescribeGrantKeyword(EffectArgs args)
    {
        var keyword = args.String("keyword");
        var target = Sel(args);
        return keyword switch
        {
            "ricochet" => $"grant {target} ricochet ({args.String("direction")})",
            "taunt" when args.BoolOrDefault("until_next_turn", false) =>
                $"grant {target} taunt until next turn",
            _ => $"grant {target} {keyword}",
        };
    }

    private static string DescribeConditional(EffectArgs args)
    {
        var then = args.Has("then") ? Describe(args.Nodes("then")) : string.Empty;
        var text = $"if {DescribeCondition(args.Node("condition"))}: {then}";
        if (args.Has("else"))
        {
            text += $"; else: {Describe(args.Nodes("else"))}";
        }

        return text;
    }

    private static string DescribeCondition(EffectNode condition)
    {
        if (condition.Op != "creature_state")
        {
            return condition.Op;
        }

        var target = Sel(condition.Args);
        var check = condition.Args.String("check");
        return check switch
        {
            "unopposed" => $"{target} is unopposed",
            "damaged" => $"{target} is damaged",
            "full_health" => $"{target} is at full health",
            _ when check.StartsWith("health_at_most:", StringComparison.Ordinal) =>
                $"{target}'s health is at most {check["health_at_most:".Length..]}",
            _ => $"{target} {check}",
        };
    }

    private static string DescribeForEach(EffectArgs args)
    {
        var collection = args.String("collection").Replace('_', ' ');
        var filter = args.StringOrDefault("filter", string.Empty);
        var where = filter.Length == 0 ? string.Empty : $" ({filter.Replace('_', ' ')})";
        var effects = Describe(args.Nodes("effects"));
        return $"for each {collection}{where}: {effects}";
    }

    private static string Scale(EffectArgs args)
    {
        var scale = args.String("scale");
        var multiplier = args.IntOrDefault("multiplier", 1);
        var divisor = args.IntOrDefault("divisor", 1);

        var basis = scale switch
        {
            "health" => "source's health",
            "count" => "friendly creature count",
            "hand_size" => "hand size",
            "selector_health" => $"{Sel(args, "health_source")}'s health",
            "hand_composition" => $"{args.StringOrDefault("resource_type", string.Empty)} cards in hand",
            "resource" => $"{args.StringOrDefault("resource_type", string.Empty)} held",
            _ => scale,
        };

        var scaled = multiplier == 1 ? basis : $"{basis} x{multiplier}";
        return divisor == 1 ? scaled : $"{scaled} / {divisor}";
    }

    private static string Sel(EffectArgs args, string key = "target") =>
        args.Has(key) ? Describe(args.Target(key)) : "self";

    private static string Describe(TargetSelector selector) => selector switch
    {
        TargetSelector.Self => "self",
        TargetSelector.Opposing => "opposing",
        TargetSelector.LeftFriendly => "left friendly",
        TargetSelector.RightFriendly => "right friendly",
        TargetSelector.AllEnemies => "all enemies",
        TargetSelector.AllFriendlies => "all friendlies",
        TargetSelector.ChosenEnemy => "chosen enemy",
        TargetSelector.ChosenFriendly => "chosen friendly",
        _ => selector.ToString(),
    };
}
