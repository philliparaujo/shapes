using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects;

// Everything an effect op needs to resolve, beyond its own arguments.
//
// SourceSlot is null for spell effects (no creature source -- untouched by taunt/reflect/
// ricochet, which all require one, per the confirmed ruleset). A spell's attacking type is a
// separate question from whether it has a creature source -- see MoveType. ChosenTarget is the
// slot a player picked for this effect's chosen_* selector, resolved by legal-action generation
// (step 1.8) before the op ever runs; the interpreter itself never asks the player anything.
public sealed class EffectContext
{
    public GameState State { get; }

    public PlayerId ControllingPlayer { get; }

    public SlotIndex? SourceSlot { get; }

    public SlotIndex? ChosenTarget { get; }

    // The attacking resource type for damage dealt by this effect, when it has one. Resolved by
    // the caller from the acting card's cost -- a move's cost from MoveDefinition.AttackType, a
    // spell's from CardDefinition.AttackType -- both of which CardValidator enforces are
    // single-type or free, so the interpreter never has to disambiguate a mixed cost itself.
    // Null only for a genuinely free move or spell, whose damage is typeless and always 1x.
    public ResourceType? MoveType { get; }

    // How many cards in the controller's hand cost each resource type -- e.g. a hand of two
    // spike-cost cards and one wheel-cost card is (2, 0, 1). A mixed-cost card counts toward
    // every type it costs. Precomputed by the caller (ActionExecutor/ActionGenerator, which
    // already reference CardDatabase) rather than looked up here, the same reason MoveType is
    // resolved by the caller: Shapes.Core.Effects itself must stay free of any Cards
    // dependency, so op implementations only ever see plain data, never a card lookup. Used by
    // `gain_resource_scaled`'s hand_composition scale (Rally).
    public ResourcePool HandComposition { get; }

    public EffectContext(
        GameState state, PlayerId controllingPlayer, SlotIndex? sourceSlot, SlotIndex? chosenTarget,
        ResourceType? moveType = null, ResourcePool handComposition = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        ControllingPlayer = controllingPlayer;
        SourceSlot = sourceSlot;
        ChosenTarget = chosenTarget;
        MoveType = moveType;
        HandComposition = handComposition;
    }

    public CreatureInstance? SourceCreature => SourceSlot is { } slot ? State.Board[slot] : null;

    // Whether taunt restrictions apply: only to creature moves, never to spells (no creature
    // source to be taunted away from).
    public bool HasCreatureSource => SourceSlot is not null;

    public EffectContext WithChosenTarget(SlotIndex? target) =>
        new(State, ControllingPlayer, SourceSlot, target, MoveType, HandComposition);

    // Rebinds "self" to a different slot -- used by for_each so nested effects targeting
    // "self" apply to the creature currently being iterated, not the original move's source.
    // MoveType does not carry over: type effectiveness inside a for_each loop is deliberately
    // not modeled yet, since nothing in the vocabulary needs a looped attack. ControllingPlayer
    // is UNCHANGED -- for_each always iterates a collection relative to the original acting
    // player (friendly/enemy/all from their perspective), so an effect like self_damage or
    // gain_resource inside the loop is still that player's own action.
    public EffectContext WithSelf(SlotIndex slot) =>
        new(State, ControllingPlayer, slot, ChosenTarget, handComposition: HandComposition);

    // As WithSelf, but also reassigns ControllingPlayer to whoever owns `slot`. Used by
    // CombatResolver to fire a reactive trigger (on_next_damage_taken / on_next_ricochet): the
    // trigger's effect belongs to the creature that was hit, not the original attacker, so
    // "gain_resource" or "draw" inside it must credit the DEFENDER's controller, not the
    // acting player who dealt the damage.
    public EffectContext WithSelfAsController(SlotIndex slot) =>
        new(State, slot.Owner, slot, ChosenTarget, handComposition: HandComposition);
}
