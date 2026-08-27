using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Console;

// Renders a GameState to the console: both boards, hands, resources, score, and turn info.
//
// Deliberately renders from GameState, not ObservedState, and hides by SUPPRESSING output rather
// than by being handed a narrowed state (step 2.5). Two independent switches: what an agent may
// see is a correctness property enforced by test, while what the screen shows is a preference.
// Routing the console through ObservedState would couple the two and make --reveal impossible.
public static class BoardView
{
    // reveal: print both hands in full. Off by default -- the waiting seat's hand shows only a
    // count, so a human never reads the AI's (or the other hotseat player's) cards.
    public static void Render(GameState state, CardDatabase cards, bool reveal = false)
    {
        System.Console.WriteLine(
            $"— Turn {state.TurnNumber} — Player {(int)state.ActivePlayer + 1} to act ({state.Phase}) —");
        System.Console.WriteLine();

        foreach (var playerId in PlayerIds.All)
        {
            RenderPlayer(state, cards, playerId, reveal);
            System.Console.WriteLine();
        }
    }

    private static void RenderPlayer(
        GameState state, CardDatabase cards, PlayerId playerId, bool reveal)
    {
        var player = state[playerId];
        var marker = state.ActivePlayer == playerId ? "*" : " ";
        System.Console.WriteLine(
            $"{marker} Player {(int)playerId + 1} — Score: {player.Score}  " +
            $"Resources: {ResourceIcons.Describe(player.Resources)}");

        System.Console.Write("  Board: ");
        var slots = new List<string>();
        foreach (var slot in SlotIndex.AllFor(playerId))
        {
            slots.Add(DescribeSlot(state, cards, slot));
        }

        System.Console.WriteLine(string.Join("  |  ", slots));

        System.Console.Write("  Hand:  ");
        System.Console.WriteLine(DescribeHand(state, cards, playerId, reveal));
    }

    // The one place step 2.5's hiding happens.
    //
    // The ACTIVE player always sees their own hand -- they are the one being asked to choose from
    // it. Everyone else gets a count, never the contents.
    //
    // A count rather than a blank, because hand size is public information: you can see how many
    // cards someone is holding across a table, and step 2.3's determinizer depends on it being
    // knowable to sample a correctly-sized hand. Hiding it would show the human LESS than the AI
    // is entitled to, which overshoots the fairness this step is for.
    // Public rather than private so the hiding rule can be asserted directly, without a test
    // scraping stdout for a substring. What gets hidden is the property worth pinning; where it
    // lands on the screen is not.
    public static string DescribeHand(
        GameState state, CardDatabase cards, PlayerId playerId, bool reveal)
    {
        var hand = state[playerId].Hand;

        if (!reveal && state.ActivePlayer != playerId)
        {
            return hand.Count == 1 ? "(1 card, hidden)" : $"({hand.Count} cards, hidden)";
        }

        if (hand.Count == 0)
        {
            return "(empty)";
        }

        return string.Join(", ", hand.Select(id => cards.TryGet(id, out var c) ? c!.Name : id));
    }

    // Public for the same reason DescribeHand is: the merged-name property (full MergedFrom,
    // not just the primary card) is worth pinning by test, even though DESIGN.md's coverage note
    // says console layout/wording in general is not.
    public static string DescribeSlot(GameState state, CardDatabase cards, SlotIndex slot)
    {
        var creature = state.Board[slot];
        if (creature is null)
        {
            return $"[{slot}] --";
        }

        var name = DescribeName(cards, creature);
        var badges = DescribeBadges(creature);
        var types = ResourceIcons.Describe(creature.Types);
        return $"[{slot}] {name} {creature.Health}/{creature.MaxHealth} ({types}){badges}";
    }

    // A merged creature's full name, not just its primary card's -- e.g. "Circle Cadet + Medic"
    // rather than just "Circle Cadet", since the merged-in half's identity is otherwise invisible
    // on the board (only ToString()'s debug form hinted at it, via a bare "(merged x2)" count).
    // A token id (never in CardDatabase, see CreatureInstance.IsToken) falls back to its raw id,
    // the same fallback DescribeHand already uses for an unknown card.
    private static string DescribeName(CardDatabase cards, CreatureInstance creature) =>
        string.Join(" + ", creature.MergedFrom.Select(id => cards.TryGet(id, out var c) ? c!.Name : id));

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
}
