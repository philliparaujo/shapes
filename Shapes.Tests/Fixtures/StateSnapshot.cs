using System.Text;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Tests.Fixtures;

// A full-fidelity structural snapshot of a GameState, as a string.
//
// GameState/PlayerState/Board/CreatureInstance deliberately carry no value-equality members --
// they are mutable identity types everywhere else in the engine -- so "byte-identical" for the
// determinism and apply/undo properties needs its own comparer. A string built from every field
// that matters to a replay (score, resources, hand/deck contents and ORDER, board occupancy,
// and every piece of creature state effects can mutate) is that comparer: two snapshots are
// equal iff xUnit's string equality says so, and a failure message shows exactly where two
// "identical" games diverged instead of just asserting `false`.
internal static class StateSnapshot
{
    public static string Of(GameState state)
    {
        var sb = new StringBuilder();
        sb.Append("turn=").Append(state.TurnNumber);
        sb.Append(" phase=").Append(state.Phase);
        sb.Append(" active=").Append(state.ActivePlayer);
        sb.Append(" winner=").Append(state.Winner);

        // Part of the replayable state, not a derived value: a pending discard debt changes
        // which actions are legal, so two states differing only here are genuinely different
        // positions and the determinism/apply-undo properties must be able to tell them apart.
        sb.Append(" pendingDiscards=").Append(state.PendingDiscards);

        foreach (var playerId in PlayerIds.All)
        {
            AppendPlayer(sb, state[playerId]);
        }

        foreach (var slot in AllSlots())
        {
            sb.Append(" | ").Append(slot).Append(": ");
            AppendCreature(sb, state.Board[slot]);
        }

        return sb.ToString();
    }

    private static void AppendPlayer(StringBuilder sb, PlayerState player)
    {
        sb.Append(" | ").Append(player.Id)
          .Append(" score=").Append(player.Score)
          .Append(" res=").Append(player.Resources)
          .Append(" pendingRes=").Append(player.PendingNextTurnResources)
          .Append(" hand=[").Append(string.Join(',', player.Hand)).Append(']')
          .Append(" deck=[").Append(string.Join(',', player.Deck)).Append(']')
          .Append(" discard=[").Append(string.Join(',', player.Discard)).Append(']');
    }

    private static void AppendCreature(StringBuilder sb, CreatureInstance? creature)
    {
        if (creature is null)
        {
            sb.Append("empty");
            return;
        }

        sb.Append(creature.CardId)
          .Append(" hp=").Append(creature.Health).Append('/').Append(creature.MaxHealth)
          .Append(" types=").Append(creature.Types)
          .Append(" mergedFrom=[").Append(string.Join(',', creature.MergedFrom)).Append(']')
          .Append(" keywords=").Append(creature.Keywords)
          .Append(" ricochetDir=").Append(creature.RicochetDirection)
          .Append(" stunned=").Append(creature.IsStunned)
          .Append(" nextAtk=").Append(creature.NextAttackBonus)
          .Append(" nextDmgTaken=").Append(creature.NextDamageTakenBonus)
          .Append(" atkBuff=").Append(creature.AttackBuff)
          .Append(" playCost=").Append(creature.PlayCost)
          .Append(" movesUsed=");

        for (var i = 0; i < 32; i++)
        {
            sb.Append(creature.HasUsedMove(i) ? '1' : '0');
        }
    }

    private static IEnumerable<SlotIndex> AllSlots() =>
        PlayerIds.All.SelectMany(SlotIndex.AllFor);
}
