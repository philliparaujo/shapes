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
public sealed record MoveText(string Name, string Cost, string Effects, ResourceType? PrimaryType, int CostAmount)
{
    public static MoveText Of(MoveDefinition move) => new(
        move.Name,
        ResourceIcons.DescribeCost(move.Cost),
        EffectText.DescribeMove(move.Condition, move.Effects, CardTextFormat.Resource),
        move.AttackType,
        move.AttackType is { } t ? move.Cost[t] : 0);
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
