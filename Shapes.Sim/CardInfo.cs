using Shapes.Core.Cards;
using Shapes.Core.Primitives;

namespace Shapes.Sim;

// Card/move reference data joined onto a report at write time -- cost, health, resource type,
// and synthesized effect text -- so reading an outlier in the HTML explorer, a CSV, or the
// console doesn't require tabbing over to Shapes.Content/cards/ to remember what a card id does
// (DESIGN.md Phase 4 step 5). Deliberately NOT part of MetricsReport/CardStat/MoveStat: those types
// are pure aggregation over GameResult and stay constructible without a CardDatabase (every
// MetricsReportTests fixture does exactly that), matching how HtmlReportWriter/ResultWriter
// already keep the reporting/formatting concern out of Shapes.Core.
public sealed class CardInfo
{
    public required string CardId { get; init; }

    public required string Name { get; init; }

    public required CardKind Kind { get; init; }

    public required ResourcePool Cost { get; init; }

    public required ResourceType? AttackType { get; init; }

    public required int Health { get; init; }

    public required string EffectText { get; init; }

    public static IReadOnlyDictionary<string, CardInfo> BuildLookup(CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var lookup = new Dictionary<string, CardInfo>(cards.Count, StringComparer.Ordinal);
        foreach (var card in cards.All)
        {
            lookup[card.Id] = new CardInfo
            {
                CardId = card.Id,
                Name = card.Name,
                Kind = card.Kind,
                Cost = card.Cost,
                AttackType = card.AttackType,
                Health = card.Health,
                EffectText = card.IsCreature
                    ? string.Empty
                    : Shapes.Core.Effects.EffectText.Describe(card.Effects),
            };
        }

        return lookup;
    }
}

// Move reference data, keyed the same way MoveStat is -- (CardId, MoveName). A move's cost and
// effect text live on MoveDefinition, not CardDefinition, so this is a separate lookup rather
// than a field on CardInfo.
public sealed class MoveInfo
{
    public required string CardId { get; init; }

    public required string MoveName { get; init; }

    public required ResourcePool Cost { get; init; }

    public required ResourceType? AttackType { get; init; }

    public required string EffectText { get; init; }

    public static IReadOnlyDictionary<(string CardId, string MoveName), MoveInfo> BuildLookup(
        CardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var lookup = new Dictionary<(string, string), MoveInfo>();
        foreach (var card in cards.All)
        {
            foreach (var move in card.Moves)
            {
                lookup[(card.Id, move.Name)] = new MoveInfo
                {
                    CardId = card.Id,
                    MoveName = move.Name,
                    Cost = move.Cost,
                    AttackType = move.AttackType,
                    EffectText = Shapes.Core.Effects.EffectText.DescribeMove(
                        move.Condition, move.Effects),
                };
            }
        }

        return lookup;
    }
}
