using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects.Ops;

// { "op": "destroy", "target": "opposing" }
internal sealed class DestroyOp : EffectOp
{
    public override string Name => "destroy";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            RemoveAndRecord(ctx, slot);
        }
    }

    // Shared with DestroyRefundCostOp: removes a creature immediately (as opposed to the
    // once-per-action RemoveDead sweep, which only catches 0-health deaths) and logs it to the
    // turn's event list, so Gravewarden's "destroyed this turn" count sees direct destroys too.
    internal static CreatureInstance? RemoveAndRecord(EffectContext ctx, SlotIndex slot)
    {
        var creature = ctx.State.Board[slot];
        if (creature is null)
        {
            return null;
        }

        ctx.State.Board.Remove(slot);
        ctx.State.RecordTurnEvent(TurnEventKind.CreatureDestroyed, slot.Owner, slot, creature.CardId);
        return creature;
    }
}

// { "op": "destroy_refund_cost", "target": "chosen_enemy" } -- destroys the target and grants
// its OWN controller (the caster's opponent) resources equal to what they originally paid to
// play it. Suffocate's drawback: you remove their creature, but they get compensated for it.
// Reads CreatureInstance.PlayCost rather than a card lookup, since Effects has no dependency
// on Cards.
internal sealed class DestroyRefundCostOp : EffectOp
{
    public override string Name => "destroy_refund_cost";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        foreach (var slot in TargetResolver.Resolve(ctx, args.Target()))
        {
            var creature = DestroyOp.RemoveAndRecord(ctx, slot);
            if (creature is not null)
            {
                ctx.State[slot.Owner].GainResources(creature.PlayCost);
            }
        }
    }
}

// { "op": "summon", "target": "left_friendly", "card_id": "token_spike", "health": 1,
//   "types": ["spike"] }
//
// Stats are carried directly in the effect args rather than looked up from a card database:
// Shapes.Core.Effects has no dependency on Shapes.Core.Cards (step 1.7), and card loading is
// exactly the layer that will resolve a referenced card's stats into these same args when it
// parses a "summon" effect from JSON -- this op does not need to change when that lands.
//
// Target selection here means "which empty slot to summon into", not an existing creature:
// only self/left_friendly/all_friendlies/chosen_friendly make sense (an empty slot has no
// creature to be "opposing" or an "enemy"), and only the first resolved slot is used. Summon
// into a full board (no empty slot resolves) is a no-op.
internal sealed class SummonOp : EffectOp
{
    public override string Name => "summon";

    public override void Apply(EffectContext ctx, EffectArgs args)
    {
        var cardId = args.String("card_id");
        var health = args.Int("health");
        var types = ParseTypes(args.String("types"));

        var slot = FindEmptySlot(ctx, args.Target());
        if (slot is null)
        {
            return;
        }

        ctx.State.Board.Place(slot.Value, new CreatureInstance(cardId, health, types));
    }

    private static SlotIndex? FindEmptySlot(EffectContext ctx, TargetSelector selector)
    {
        foreach (var slot in TargetResolver.Resolve(ctx, selector))
        {
            if (ctx.State.Board.IsEmpty(slot))
            {
                return slot;
            }
        }

        // Selectors like left_friendly resolve only to occupied slots (per TargetResolver),
        // so for summon, fall back to any empty slot on the controller's side when the
        // selector itself found nothing to anchor on.
        var emptySlots = ctx.State.Board.EmptySlotsOf(ctx.ControllingPlayer);
        return emptySlots.Cast<SlotIndex?>().FirstOrDefault();
    }

    private static TypeMask ParseTypes(string raw) =>
        TypeMask.Of(raw.Split(',').Select(GainResourceOp.ParseResourceType).ToArray());
}
