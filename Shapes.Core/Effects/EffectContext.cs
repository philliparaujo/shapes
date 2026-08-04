using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Core.Effects;

// Everything an effect op needs to resolve, beyond its own arguments.
//
// SourceSlot is null for spell effects (no creature source -- damage from these is typeless
// and untouched by taunt, per the confirmed ruleset). ChosenTarget is the slot a player
// picked for this effect's chosen_* selector, resolved by legal-action generation (step 1.8)
// before the op ever runs; the interpreter itself never asks the player anything.
public sealed class EffectContext
{
    public GameState State { get; }

    public PlayerId ControllingPlayer { get; }

    public SlotIndex? SourceSlot { get; }

    public SlotIndex? ChosenTarget { get; }

    // The attacking resource type for damage dealt by this effect, when it has one. Resolved
    // by the caller from the move's cost -- a move's cost must be single-type or empty, which
    // step 1.7's card-load validation enforces, so the interpreter never has to disambiguate a
    // mixed-cost move itself. Null for spells and any non-damage effect: spell damage is
    // typeless and always 1x, per the confirmed ruleset.
    public ResourceType? MoveType { get; }

    public EffectContext(
        GameState state, PlayerId controllingPlayer, SlotIndex? sourceSlot, SlotIndex? chosenTarget,
        ResourceType? moveType = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        ControllingPlayer = controllingPlayer;
        SourceSlot = sourceSlot;
        ChosenTarget = chosenTarget;
        MoveType = moveType;
    }

    public CreatureInstance? SourceCreature => SourceSlot is { } slot ? State.Board[slot] : null;

    // Whether taunt restrictions apply: only to creature moves, never to spells (no creature
    // source to be taunted away from).
    public bool HasCreatureSource => SourceSlot is not null;

    public EffectContext WithChosenTarget(SlotIndex? target) =>
        new(State, ControllingPlayer, SourceSlot, target, MoveType);

    // Rebinds "self" to a different slot -- used by for_each so nested effects targeting
    // "self" apply to the creature currently being iterated, not the original move's source.
    // MoveType does not carry over: type effectiveness inside a for_each loop is deliberately
    // not modeled yet, since nothing in the vocabulary needs a looped attack.
    public EffectContext WithSelf(SlotIndex slot) =>
        new(State, ControllingPlayer, slot, ChosenTarget);
}
