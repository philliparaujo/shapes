using Shapes.Core.Primitives;

namespace Shapes.Core.Effects;

// Renders an effect tree (a move's or spell's Effects list) into short human-readable rules text,
// e.g. "Deal 1" or "Deal 2 to this. Your right friendly gains +4 max health".
//
// No card ever carries hand-authored description text (see PLAN.md Phase 4 step 4/5) -- the
// card JSON schema has no "text" field, deliberately, so a balance edit to an effect's numbers
// can never drift out of sync with a description someone forgot to update. This is therefore a
// SYNTHESIS from the op vocabulary, not a lookup, and is the one place that vocabulary is turned
// into English. Shared by the console's action log/BoardView and (per step 5) available to
// Shapes.Sim's report, so a phrasing fix only needs to happen once.
//
// Phrasing follows references/card text formatting.txt, a hand-written pass over the whole card
// set. Four conventions carry most of it, and they are why the output reads as rules text rather
// than as a dump of the op vocabulary:
//
//   * Clauses are sentences: capitalized, joined with ". ". A move is a couple of short commands,
//     not one semicolon-chained expression.
//   * The DEFAULT target goes unsaid. Damage-like ops assume "opposing" and heal-like ops assume
//     "this", so "Deal 1" and "Heal 4" carry no target at all; anything else is named explicitly
//     ("Deal 2 to this", "Deal 1 to all enemies"). This is the single biggest source of brevity,
//     and it is why Sel takes the op's own default rather than reading one global default.
//   * The creature reads as "this", never "self", and friendly slots are possessive ("Your left
//     friendly") -- a player reads their own board, not an effect tree's selector names.
//   * Scaling is prose ("equal to health this has"), not a parenthetical.
//
// These rules are general, deliberately: they are applied per op and per selector, never per card
// id. A per-card table would match the reference exactly and then silently mis-word the next card
// someone authors, which is the same drift the no-authored-text design exists to prevent.
public static class EffectText
{
    // How a resource type is written inline. The default spells it in brackets ("[anvil]"), which
    // is what the reference text uses and what any plain-text consumer (console log, Sim report)
    // wants. Godot swaps in a real inline icon by passing its own formatter -- that is the whole
    // reason this is injectable rather than a constant: Shapes.Core must not gain a UI dependency
    // (PLAN.md 3, enforced by CorePurityTests), but it is still the layer that knows WHERE in a
    // sentence the resource belongs.
    public static string DefaultResourceFormat(ResourceType type) =>
        $"[{type.ToString().ToLowerInvariant()}]";

    public static string Describe(
        IReadOnlyList<EffectNode> effects, Func<ResourceType, string>? resourceFormat = null)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return Describe(effects, resourceFormat ?? DefaultResourceFormat, pronoun: null);
    }

    // `pronoun`, when set, is a selector already named earlier in the sentence -- rendered as
    // "it" instead of being spelled out again. See DescribeConditional, its only caller.
    private static string Describe(
        IReadOnlyList<EffectNode> effects, Func<ResourceType, string> format, TargetSelector? pronoun)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return effects.Count == 0
            ? string.Empty
            : Join(effects.Select(node => DescribeNode(node, format, pronoun)));
    }

    // A move's gate plus its effects. A move-level `condition` decides whether the move can be
    // used at all (ConditionEvaluator, via legal-action generation) rather than branching inside
    // the effect list, so Describe(move.Effects) alone silently omits it -- and the omission is
    // invisible in a way a missing effect is not: "Deal 1" reads as a complete, and badly
    // underpowered, 1-cost move rather than a conditional one.
    //
    // The gate is a trailing clause ("Deal 2 if this is damaged"), not a leading one, and it
    // attaches to the LAST sentence rather than the whole list -- "Discard 1. Heal 4 if this is
    // damaged" is how the reference writes a gated multi-effect move. Both readings are
    // technically wrong for a move-level gate (which governs every effect, not just the last),
    // but a leading "Only if X:" reads as a disclaimer rather than a rule, and the gate is
    // near-always on a single-effect move in practice.
    public static string DescribeMove(
        EffectNode? condition,
        IReadOnlyList<EffectNode> effects,
        Func<ResourceType, string>? resourceFormat = null)
    {
        var format = resourceFormat ?? DefaultResourceFormat;
        var text = Describe(effects, format);
        if (condition is null)
        {
            return text;
        }

        var gate = $"if {DescribeCondition(condition, format)}";
        return text.Length == 0 ? Capitalize(gate) : $"{TrimEnd(text)} {gate}.";
    }

    // Sentence-joins already-capitalized clauses. Each clause arrives without its terminating
    // period so a trailing gate can be appended to the last one (see DescribeMove).
    //
    // A clause starting with "and " continues the previous sentence instead of opening a new one
    // -- "Deal 3 and stun", not "Deal 3. And stun." Stun is the only op that produces one today
    // (see DescribeStun), and it is always authored as a rider on the damage that delivers it.
    private static string Join(IEnumerable<string> clauses)
    {
        var text = string.Empty;
        foreach (var clause in clauses.Where(c => c.Length > 0))
        {
            if (clause.StartsWith("and ", StringComparison.Ordinal) && text.Length > 0)
            {
                text = $"{TrimEnd(text)} {clause}.";
                continue;
            }

            text = text.Length == 0
                ? $"{Capitalize(clause)}."
                : $"{text} {Capitalize(clause)}.";
        }

        return text;
    }

    private static string TrimEnd(string text) =>
        text.EndsWith('.') ? text[..^1] : text;

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // `pronoun` rides in an ambient field rather than as a parameter on all ~25 op cases and every
    // selector helper below. Nothing here is reentrant or threaded -- Describe is a pure
    // synthesis over an immutable tree, called on one stack -- and the alternative was
    // threading one rarely-set argument through the entire call graph purely so DescribeConditional
    // could reach Describe(TargetSelector). Set and restored around that one call site.
    [ThreadStatic]
    private static TargetSelector? _pronoun;

    private static string DescribeNode(
        EffectNode node, Func<ResourceType, string> fmt, TargetSelector? pronoun)
    {
        var previous = _pronoun;
        _pronoun = pronoun;
        try
        {
            return DescribeNode(node, fmt);
        }
        finally
        {
            _pronoun = previous;
        }
    }

    private static string DescribeNode(EffectNode node, Func<ResourceType, string> fmt)
    {
        var args = node.Args;
        return node.Op switch
        {
            // Damage defaults to "opposing" and drops the word "damage" -- "Deal 1", not "deal 1
            // damage to opposing". self_damage is the same sentence with an explicit target
            // rather than its own vocabulary ("Deal 2 to this"), so the two ops stop reading as
            // unrelated mechanics.
            "damage" => $"deal {args.Int("amount")}{To(args, TargetSelector.Opposing)}",
            "self_damage" => $"deal {args.Int("amount")} to this",
            "damage_scaled" =>
                $"deal {ScaledAmount(args, fmt)}{To(args, TargetSelector.Opposing)}",

            "heal" => $"heal {args.Int("amount")}{To(args, TargetSelector.Self)}",
            "heal_to_full" => $"heal{To(args, TargetSelector.Self)} to full",
            "heal_scaled" => $"heal {Scale(args, fmt)}{To(args, TargetSelector.Self)}",
            "set_health" => $"set {Possessive(args)} health to {args.Int("amount")}",

            // "gains" reads from the target's side, so a non-default target becomes the subject
            // ("Your right friendly gains +4 max health") rather than a trailing "to X".
            "buff_max_health" => Gains(args, $"+{args.Int("amount")} max health"),
            "buff_max_health_scaled" => GainsScaled(args, "max health", fmt),
            "attack_buff" => Gains(args, $"+{args.Int("amount")} attack"),
            "attack_buff_scaled" => GainsScaled(args, "attack", fmt),
            "next_attack_bonus" => Gains(args, $"+{args.Int("amount")} to its next attack"),
            "next_damage_taken_bonus" =>
                Gains(args, $"+{args.Int("amount")} to the next damage it takes"),

            "draw" => $"draw {args.Int("amount")}",
            "draw_scaled" => $"draw {Multiplied(args, "1")} for each creature destroyed this turn",
            "discard" => $"discard {args.Int("amount")}",
            "draw_up_to" => $"draw up to {args.Int("amount")} cards",

            "gain_resource" => $"gain {args.Int("amount")} {Resource(args, fmt)}",
            "gain_resource_scaled" => $"gain {Resource(args, fmt)} {Scale(args, fmt)}",
            "gain_next_turn" =>
                $"gain {args.Int("amount")} {Resource(args, fmt)} next turn",

            "destroy" => $"destroy {Target(args, TargetSelector.Self)}",
            "destroy_refund_cost" =>
                $"destroy {Target(args, TargetSelector.ChosenEnemy)}. Refund its cost to your opponent",
            "summon" =>
                $"summon a {args.Int("health")}-health {args.String("types")} token{To(args, TargetSelector.Self)}",

            "grant_keyword" => DescribeGrantKeyword(args),
            "stun" => DescribeStun(args),
            "on_next_damage_taken" =>
                $"{DescribeNode(args.Node("effect"), fmt)} next time {Subject(args)} takes damage",
            "on_next_ricochet" =>
                $"{DescribeNode(args.Node("effect"), fmt)} next time {Subject(args)} ricochets",

            "conditional" => DescribeConditional(args, fmt),
            "for_each" => DescribeForEach(args, fmt),

            _ => node.Op,
        };
    }

    // Stun rides along on the attack that delivers it ("Deal 3 and stun"), which is how every
    // stun in the set is authored -- as the second effect of a damage move against the same
    // target. Written standalone when it targets anything else, so an unattached stun still says
    // who it hits.
    private static string DescribeStun(EffectArgs args) =>
        Sel(args, TargetSelector.Opposing) == TargetSelector.Opposing
            ? "and stun"
            : $"stun {Target(args, TargetSelector.Opposing)}";

    private static string DescribeGrantKeyword(EffectArgs args)
    {
        var keyword = args.String("keyword");
        var text = keyword switch
        {
            "ricochet" => $"ricochet {args.String("direction")}",
            "taunt" when args.BoolOrDefault("until_next_turn", false) =>
                "taunt until your next turn",
            _ => keyword,
        };

        return Gains(args, text);
    }

    // "Deal 4 to it if an enemy is damaged. Otherwise deal 2 to it."
    //
    // The branches refer back to the creature the CONDITION just named rather than naming it
    // again: a chosen target spelled out three times ("deal 4 to an enemy if an enemy is damaged,
    // otherwise deal 2 to an enemy") reads as three different creatures. Only applies when the
    // branches target the same selector the condition tests, which is the shape every conditional
    // in the set uses -- a branch hitting some other creature still names it.
    private static string DescribeConditional(EffectArgs args, Func<ResourceType, string> fmt)
    {
        var conditionNode = args.Node("condition");
        var subject = conditionNode.Op == "creature_state" && conditionNode.Args.Has("target")
            ? conditionNode.Args.Target()
            : (TargetSelector?)null;

        var then = args.Has("then") ? Describe(args.Nodes("then"), fmt, subject) : string.Empty;
        var condition = DescribeCondition(conditionNode, fmt);
        var text = then.Length == 0 ? $"if {condition}" : $"{TrimEnd(then)} if {condition}";

        if (args.Has("else"))
        {
            var otherwise = TrimEnd(Lower(Describe(args.Nodes("else"), fmt, subject)));
            text += $". Otherwise {otherwise}";
        }

        return text;
    }

    private static string Lower(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    // Subject-first, matching how a player would say it out loud: "this has at least 4 health",
    // not "self's health is at least 4".
    private static string DescribeCondition(EffectNode condition, Func<ResourceType, string> fmt)
    {
        if (condition.Op != "creature_state")
        {
            return condition.Op;
        }

        var args = condition.Args;
        var subject = Subject(args, TargetSelector.Self);
        var check = args.String("check");
        return check switch
        {
            "unopposed" => $"{subject} is unopposed",
            "damaged" => $"{subject} is damaged",
            "full_health" => $"{subject} is at full health",
            _ when check.StartsWith("health_at_most:", StringComparison.Ordinal) =>
                $"{subject} has at most {check["health_at_most:".Length..]} health",
            _ when check.StartsWith("health_at_least:", StringComparison.Ordinal) =>
                $"{subject} has at least {check["health_at_least:".Length..]} health",
            _ => $"{subject} {check}",
        };
    }

    // "Deal 2 damage for each friendly creature" -- the count folds into the noun rather than
    // trailing as a multiplier, which is the one place the reference restructures a scaled op
    // instead of just rewording it.
    private static string DescribeForEach(EffectArgs args, Func<ResourceType, string> fmt)
    {
        var collection = args.String("collection").Replace('_', ' ');
        var filter = args.StringOrDefault("filter", string.Empty);
        var noun = filter.Length == 0
            ? collection
            : $"{filter.Replace('_', ' ')} {Singularize(collection)}";
        var effects = TrimEnd(Lower(Describe(args.Nodes("effects"), fmt)));
        return $"{effects} for each {noun}";
    }

    private static string Singularize(string collection) => collection switch
    {
        "all creatures" => "creature",
        "all friendlies" => "friendly",
        "all enemies" => "enemy",
        _ => collection,
    };

    // The amount phrase for a scaled damage op. The friendly-creature count is restructured
    // rather than reworded: "Deal 2 damage for each friendly creature" reads as rules text, while
    // the literal "damage equal to twice the friendly creature count" is a formula. The
    // multiplier becomes the per-creature amount, which only works because that is exactly what
    // it means here -- so this stays a `count`-only shape rather than a general rewrite.
    private static string ScaledAmount(EffectArgs args, Func<ResourceType, string> fmt)
    {
        if (args.String("scale") != "count")
        {
            return $"damage {Scale(args, fmt)}";
        }

        var per = args.IntOrDefault("multiplier", 1);
        return $"{per} damage for each friendly creature";
    }

    // Scaling reads as an "equal to ..." phrase rather than a parenthetical, with the common
    // multiplier/divisor cases spelled as English quantities ("twice the", "half the") since
    // "x2" and "/ 2" are notation, not rules text.
    private static string Scale(EffectArgs args, Func<ResourceType, string> fmt)
    {
        var scale = args.String("scale");
        var multiplier = args.IntOrDefault("multiplier", 1);
        var divisor = args.IntOrDefault("divisor", 1);

        var basis = scale switch
        {
            "health" => "health this has",
            "hand_size" => "your hand size",
            "selector_health" => $"{Possessive(args, "health_source")} health",
            "hand_composition" =>
                $"{ResourceOrEmpty(args, fmt)} cards in your hand",
            "resource" => $"your {ResourceOrEmpty(args, fmt)}",
            "missing_health" => "missing health",
            "selector_missing_health" => $"{Possessive(args, "health_source")} missing health",
            "destroyed_this_turn" => "creatures destroyed this turn",
            _ => scale,
        };

        return $"equal to {Quantified(basis, multiplier, divisor)}";
    }

    // "twice the health this has" / "half the health this has" -- the quantity word goes in
    // front of the basis, which is why this cannot just append "x2" to Scale's result.
    private static string Quantified(string basis, int multiplier, int divisor)
    {
        if (divisor == 2 && multiplier == 1)
        {
            return $"half the {basis}";
        }

        if (multiplier == 2 && divisor == 1)
        {
            return $"twice the {basis}";
        }

        var text = basis;
        if (multiplier != 1)
        {
            text = $"{multiplier} times the {text}";
        }

        if (divisor != 1)
        {
            text = $"{text} divided by {divisor}";
        }

        return text;
    }

    private static string Multiplied(EffectArgs args, string one)
    {
        var multiplier = args.IntOrDefault("multiplier", 1);
        return multiplier == 1 ? one : multiplier.ToString();
    }

    private static string Resource(EffectArgs args, Func<ResourceType, string> fmt) =>
        fmt(ParseResource(args.String("type")));

    private static string ResourceOrEmpty(EffectArgs args, Func<ResourceType, string> fmt)
    {
        var raw = args.StringOrDefault("resource_type", string.Empty);
        return raw.Length == 0 ? string.Empty : fmt(ParseResource(raw));
    }

    private static ResourceType ParseResource(string raw) => raw switch
    {
        "spike" => ResourceType.Spike,
        "anvil" => ResourceType.Anvil,
        "wheel" => ResourceType.Wheel,
        _ => throw new ArgumentException($"Unknown resource type '{raw}'."),
    };

    // The op's own default target, omitted from the text entirely when it matches. Damage assumes
    // the creature across from you; heals and buffs assume the creature using the move.
    private static TargetSelector Sel(EffectArgs args, TargetSelector fallback) =>
        args.Has("target") ? args.Target() : fallback;

    private static string To(EffectArgs args, TargetSelector fallback)
    {
        var selector = Sel(args, fallback);
        return selector == fallback ? string.Empty : $" to {Describe(selector)}";
    }

    private static string Target(EffectArgs args, TargetSelector fallback) =>
        Describe(Sel(args, fallback));

    // A gain whose amount scales off the GAINING creature's own stat: "A friendly gains attack
    // equal to its missing health", not "...equal to a friendly's missing health", which reads as
    // two different creatures. The subject is named once, then referred back to -- the same
    // problem DescribeConditional solves, and it reuses the same pronoun mechanism.
    private static string GainsScaled(
        EffectArgs args, string stat, Func<ResourceType, string> fmt)
    {
        var subject = Sel(args, TargetSelector.Self);
        var previous = _pronoun;
        _pronoun = subject;
        try
        {
            return Gains(args, $"{stat} {Scale(args, fmt)}");
        }
        finally
        {
            _pronoun = previous;
        }
    }

    // "X gains Y", with the default (the creature acting) dropping the subject entirely:
    // "Gain reflect" vs "Your left friendly gains reflect".
    private static string Gains(EffectArgs args, string what)
    {
        var selector = Sel(args, TargetSelector.Self);

        // Spell, not Describe: this IS the first mention, so it names the creature even when
        // GainsScaled has already registered it as the pronoun for the rest of the clause.
        return selector == TargetSelector.Self
            ? $"gain {what}"
            : $"{Spell(selector)} gains {what}";
    }

    private static string Subject(EffectArgs args, TargetSelector fallback = TargetSelector.Self) =>
        Describe(Sel(args, fallback));

    // "this" and "it" are already possessive-shaped in these sentences ("health this has",
    // "equal to its missing health"), so only a spelled-out selector takes an apostrophe-s.
    private static string Possessive(EffectArgs args, string key = "target")
    {
        var selector = args.Has(key) ? args.Target(key) : TargetSelector.Self;
        var text = Describe(selector);
        return text switch
        {
            "this" => "this",
            "it" => "its",
            _ => $"{text}'s",
        };
    }

    // A selector already named earlier in the same sentence renders as "it" -- see
    // DescribeConditional on why repeating "an enemy" three times reads as three creatures.
    private static string Describe(TargetSelector selector)
    {
        if (_pronoun == selector)
        {
            return "it";
        }

        return Spell(selector);
    }

    private static string Spell(TargetSelector selector) => selector switch
    {
        TargetSelector.Self => "this",
        TargetSelector.Opposing => "the opposing creature",
        TargetSelector.LeftFriendly => "your left friendly",
        TargetSelector.RightFriendly => "your right friendly",
        TargetSelector.AllEnemies => "all enemies",
        TargetSelector.AllFriendlies => "each friendly",
        TargetSelector.AllCreatures => "all creatures",
        TargetSelector.ChosenEnemy => "an enemy",
        TargetSelector.ChosenFriendly => "a friendly",
        _ => selector.ToString(),
    };
}
