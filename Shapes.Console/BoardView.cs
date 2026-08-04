using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Console;

// Renders a GameState to the console: both boards, hands, resources, score, and turn info.
internal static class BoardView
{
    public static void Render(GameState state, CardDatabase cards)
    {
        System.Console.WriteLine(
            $"— Turn {state.TurnNumber} — Player {(int)state.ActivePlayer + 1} to act ({state.Phase}) —");
        System.Console.WriteLine();

        foreach (var playerId in PlayerIds.All)
        {
            RenderPlayer(state, cards, playerId);
            System.Console.WriteLine();
        }
    }

    private static void RenderPlayer(GameState state, CardDatabase cards, PlayerId playerId)
    {
        var player = state[playerId];
        var marker = state.ActivePlayer == playerId ? "*" : " ";
        System.Console.WriteLine(
            $"{marker} Player {(int)playerId + 1} — Score: {player.Score}  " +
            $"Resources: {Describe(player.Resources)}");

        System.Console.Write("  Board: ");
        var slots = new List<string>();
        foreach (var slot in SlotIndex.AllFor(playerId))
        {
            slots.Add(DescribeSlot(state, cards, slot));
        }

        System.Console.WriteLine(string.Join("  |  ", slots));

        System.Console.Write("  Hand:  ");
        if (player.Hand.Count == 0)
        {
            System.Console.WriteLine("(empty)");
        }
        else
        {
            var handNames = player.Hand.Select(id => cards.TryGet(id, out var c) ? c!.Name : id);
            System.Console.WriteLine(string.Join(", ", handNames));
        }
    }

    private static string DescribeSlot(GameState state, CardDatabase cards, SlotIndex slot)
    {
        var creature = state.Board[slot];
        if (creature is null)
        {
            return $"[{slot}] --";
        }

        var name = cards.TryGet(creature.CardId, out var c) ? c!.Name : creature.CardId;
        var badges = DescribeBadges(creature);
        return $"[{slot}] {name} {creature.Health}/{creature.MaxHealth} ({creature.Types}){badges}";
    }

    private static string DescribeBadges(CreatureInstance creature)
    {
        var badges = new List<string>();
        if (creature.HasKeyword(KeywordFlags.Taunt))
        {
            badges.Add("Taunt");
        }

        if (creature.HasKeyword(KeywordFlags.Reflect))
        {
            badges.Add("Reflect");
        }

        if (creature.HasKeyword(KeywordFlags.Ricochet))
        {
            badges.Add("Ricochet");
        }

        if (creature.IsStunned)
        {
            badges.Add("Stunned");
        }

        if (creature.AttackBuff != 0)
        {
            badges.Add($"Atk+{creature.AttackBuff}");
        }

        return badges.Count == 0 ? string.Empty : $" [{string.Join(",", badges)}]";
    }

    private static string Describe(ResourcePool pool) =>
        $"△{pool.Spike} ▢{pool.Anvil} ◯{pool.Wheel}";
}
