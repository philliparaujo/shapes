using Shapes.Core.Cards;
using Shapes.Core.Effects;
using Shapes.Core.Primitives;

namespace Shapes.Tests.Fixtures;

// A synthetic card set for action tests.
//
// Deliberately not the real cards: an action test asserting "this move costs 1 wheel" must not
// start failing because a Phase 4 balance pass repriced a real card. These exist only to be
// unremarkable examples of each shape the action model has to handle -- a plain creature, a
// targeted move, a gated move, a spell -- so the tests describe the RULES rather than the
// content.
public static class TestCards
{
    // A 1-wheel 2-health creature whose one move damages the facing slot. The baseline case.
    public const string Striker = "test_striker";

    // Two moves, so "different moves on one creature are independent" is testable, and so a
    // merged creature has a move list long enough for offsets to matter.
    public const string TwoMove = "test_two_move";

    // Its move requires a player-chosen enemy: the expansion case.
    public const string Chooser = "test_chooser";

    // Its move is gated on creature_state(self, full_health): the condition case.
    public const string Gated = "test_gated";

    // A free (zero-cost) move: affordability must not accidentally exclude it.
    public const string FreeMove = "test_free_move";

    // An expensive creature, for the unaffordable-play case.
    public const string Costly = "test_costly";

    // A spell with no target, and one requiring a chosen enemy.
    public const string Bolt = "test_bolt";
    public const string TargetedBolt = "test_targeted_bolt";

    // A spell whose effect owes two chosen discards: the pending-discard case. Two rather than
    // one so the "narrow the choice a card at a time" sequence has more than a single step, and
    // so the clamp has something to clamp when the hand is short.
    public const string DiscardTwo = "test_discard_two";

    // A spell using gain_resource_scaled(hand_composition) -- the Rally shape: ActionExecutor
    // must precompute EffectContext.HandComposition, not just wire the op itself.
    public const string RallyLike = "test_rally_like";

    public static CardDatabase Database { get; } = new(
    [
        Creature(Striker, 1, TypeMask.Wheel, health: 2,
            Move("Strike", Cost(wheel: 1), Eff.Node("damage", ("target", "opposing"), ("amount", 1)))),

        Creature(TwoMove, 1, TypeMask.Spike, health: 3,
            Move("Jab", Cost(spike: 1), Eff.Node("damage", ("target", "opposing"), ("amount", 1))),
            Move("Brace", Cost(spike: 1), Eff.Node("heal", ("target", "self"), ("amount", 1)))),

        Creature(Chooser, 1, TypeMask.Anvil, health: 2,
            Move("Snipe", Cost(anvil: 1),
                Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 1)))),

        Creature(Gated, 1, TypeMask.Wheel, health: 2,
            Move("Scout", Cost(wheel: 1),
                condition: Eff.Node("creature_state", ("target", "self"), ("check", "full_health")),
                effects: [Eff.Node("draw", ("amount", 1))])),

        Creature(FreeMove, 1, TypeMask.Spike, health: 2,
            Move("Focus", ResourcePool.Empty,
                Eff.Node("next_attack_bonus", ("target", "self"), ("amount", 1)))),

        Creature(Costly, 9, TypeMask.Anvil, health: 5,
            Move("Slam", Cost(anvil: 9), Eff.Node("damage", ("target", "opposing"), ("amount", 3)))),

        Spell(Bolt, 1, Eff.Node("draw", ("amount", 1))),

        Spell(TargetedBolt, 1, Eff.Node("damage", ("target", "chosen_enemy"), ("amount", 2))),

        Spell(DiscardTwo, 1, Eff.Node("discard", ("amount", 2))),

        Spell(RallyLike, 1,
            Eff.Node("gain_resource_scaled", ("type", "spike"), ("scale", "hand_composition"), ("multiplier", 2))),
    ]);

    public static ResourcePool Cost(int spike = 0, int anvil = 0, int wheel = 0) =>
        new(spike, anvil, wheel);

    private static CardDefinition Creature(
        string id, int wheelCost, TypeMask types, int health, params MoveDefinition[] moves) =>
        new(id, id, CardKind.Creature, CostFor(types, wheelCost), health, types, moves);

    // Costs the creature in its own type, so income and play cost line up the way a real card's
    // would. Multi-type test creatures are built directly rather than through here.
    private static ResourcePool CostFor(TypeMask types, int amount)
    {
        if (types.Has(ResourceType.Spike))
        {
            return Cost(spike: amount);
        }

        return types.Has(ResourceType.Anvil) ? Cost(anvil: amount) : Cost(wheel: amount);
    }

    private static CardDefinition Spell(string id, int cost, params EffectNode[] effects) =>
        new(id, id, CardKind.Spell, Cost(wheel: cost), 0, TypeMask.None, null, effects);

    private static MoveDefinition Move(string name, ResourcePool cost, params EffectNode[] effects) =>
        new(name, cost, effects);

    private static MoveDefinition Move(
        string name, ResourcePool cost, EffectNode condition, EffectNode[] effects) =>
        new(name, cost, effects, condition);
}
