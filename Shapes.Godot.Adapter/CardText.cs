using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Adapter;

// How rules text names a resource, for every CardText/MoveText this assembly builds.
//
// Set once at startup by the Godot layer to emit inline-icon markers (InlineResourceIcons);
// left at EffectText's bracketed default everywhere else, including tests. A single mutable
// default rather than a parameter on all ~8 call sites specifically so none can be MISSED --
// a forgotten argument would render a literal "[anvil]" in one view and a drawn icon in the
// next, which is exactly the per-view drift MoveRowFactory's own header describes fixing.
public static class CardTextFormat
{
    public static Func<ResourceType, string> Resource { get; set; } = EffectText.DefaultResourceFormat;
}

// One move's text, for a card face's move list. Name/cost stay separate fields (rather than
// pre-joined into Summary) so the scene can size/style them differently -- a move row is a
// name, a cost badge, and a rules line, not one paragraph. PrimaryType is the same derivation
// as MoveDefinition.AttackType (single-type cost, or null for free/mixed) -- Godot's move-cost
// badge (PLAN.md B1c) needs a ResourceType to pick a shape/color, not just the pre-formatted
// glyph string ResourceIcons.DescribeCost already gives it; CostAmount is that type's pip count,
// for the badge's number overlay (0 / meaningless when PrimaryType is null).
//
// CostAmount is the EFFECTIVE cost, not necessarily the printed one: a free-moves spell zeroes
// every move of its type for the turn, and a badge still showing the printed pips while the move
// costs nothing is the one thing a player cannot discover from the board. IsDiscounted says the
// zero is that temporary state rather than a move that simply prints no cost, which is what lets
// the badge tint it (see ResourceIconFactory.Create) -- the two look identical otherwise.
public sealed record MoveText(
    string Name, string Cost, string Effects, ResourceType? PrimaryType, int CostAmount,
    bool IsDiscounted = false)
{
    // The card-data-only form: no game in progress (the card browser, a deck list), so nothing
    // can be discounted and the printed cost IS the effective cost.
    public static MoveText Of(MoveDefinition move) => Of(move, move.Cost);

    // `effectiveCost` is what the move costs right now -- GameState.CostOfMove, which is the same
    // answer ActionGenerator used to decide the move was legal and ActionExecutor will charge.
    // Passed in rather than computed here so this assembly stays free of GameState, and so there
    // is still exactly one implementation of the discount rule.
    public static MoveText Of(MoveDefinition move, ResourcePool effectiveCost)
    {
        var type = move.AttackType;
        return new MoveText(
            move.Name,
            ResourceIcons.DescribeCost(effectiveCost),
            EffectText.DescribeMove(move.Condition, move.Effects, CardTextFormat.Resource),
            type,
            type is { } t ? effectiveCost[t] : 0,
            IsDiscounted: effectiveCost != move.Cost);
    }
}

// Full text for one card face: everything A4 (card rendering via EffectText) needs, gathered
// once per card rather than recomputed per field access. Card JSON carries no hand-authored
// text (see EffectText's own header) -- every string here is synthesized, so a balance edit
// to a card's numbers can never leave a stale description behind.
//
// PrimaryType (PLAN.md B1c): the single resource type Godot's hand/tooltip/in-play views use to
// pick a placeholder-art shape/color and cost-badge number. "Type comes from resource cost,
// always" (PLAN.md 0. Confirmed ruleset) -- a creature's defensive type IS its play cost's type.
// CardDefinition.SingleCostType is the authoritative derivation but is internal to Shapes.Core
// (no InternalsVisibleTo to this project), so SinglePipType below is a second copy of the same
// few lines -- same accepted duplication ResourceIcons' own header already documents for this
// project, rather than widening Shapes.Core's public surface for a Godot-only convenience.
// Public (not internal) because Shapes.Godot's SlotView also needs it, for a merged creature's
// per-source-card art pane (PLAN.md B1c) -- a second cross-assembly copy inside Shapes.Godot
// itself would triple the duplication instead of keeping it at the one accepted layer.
// CardId travels with the text (PLAN.md B1c) so a view can look up that card's art. Every art
// site is reached through a CardText already -- the hand card's face, its hover tooltip, and a
// board creature's hover all carry one -- so carrying the id here is what lets art resolve
// without threading a second parameter through three event signatures. It is the card's stable
// id, not its JSON filename; see CardArt on why that distinction is load-bearing.
public sealed record CardText(
    string CardId,
    string Name,
    string Cost,
    string TypeIcons,
    bool IsCreature,
    int Health,
    IReadOnlyList<MoveText> Moves,
    string SpellEffects,
    ResourceType? PrimaryType,
    int CostAmount)
{
    public static CardText Of(CardDefinition card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var primaryType = SinglePipType(card.Cost);
        var costAmount = primaryType is { } t ? card.Cost[t] : 0;

        return new CardText(
            card.Id,
            card.Name,
            ResourceIcons.DescribeCost(card.Cost),
            ResourceIcons.Describe(card.Types),
            card.IsCreature,
            card.Health,
            [.. card.Moves.Select(MoveText.Of)],
            EffectText.Describe(card.Effects, CardTextFormat.Resource),
            primaryType,
            costAmount);
    }

    public static ResourceType? SinglePipType(ResourcePool cost)
    {
        ResourceType? found = null;
        foreach (var type in ResourceTypes.All)
        {
            if (cost[type] <= 0)
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = type;
        }

        return found;
    }
}
