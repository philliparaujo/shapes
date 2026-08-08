using Shapes.Core.Cards;
using Shapes.Core.Effects;

namespace Shapes.Godot.Adapter;

// One move's text, for a card face's move list. Name/cost stay separate fields (rather than
// pre-joined into Summary) so the scene can size/style them differently -- a move row is a
// name, a cost badge, and a rules line, not one paragraph.
public sealed record MoveText(string Name, string Cost, string Effects)
{
    public static MoveText Of(MoveDefinition move) => new(
        move.Name,
        ResourceIcons.DescribeCost(move.Cost),
        EffectText.DescribeMove(move.Condition, move.Effects));
}

// Full text for one card face: everything A4 (card rendering via EffectText) needs, gathered
// once per card rather than recomputed per field access. Card JSON carries no hand-authored
// text (see EffectText's own header) -- every string here is synthesized, so a balance edit
// to a card's numbers can never leave a stale description behind.
public sealed record CardText(
    string Name,
    string Cost,
    string TypeIcons,
    bool IsCreature,
    int Health,
    IReadOnlyList<MoveText> Moves,
    string SpellEffects)
{
    public static CardText Of(CardDefinition card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new CardText(
            card.Name,
            ResourceIcons.DescribeCost(card.Cost),
            ResourceIcons.Describe(card.Types),
            card.IsCreature,
            card.Health,
            [.. card.Moves.Select(MoveText.Of)],
            EffectText.Describe(card.Effects));
    }
}
