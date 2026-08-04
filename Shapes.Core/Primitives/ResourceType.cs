namespace Shapes.Core.Primitives;

// The three resource/creature types, in a rock-paper-scissors cycle where each type
// deals double damage to the next: Spike beats Wheel, Wheel beats Anvil, Anvil beats Spike.
//
// Values are explicit and stable: they are used as array indices in the hot search path
// and are persisted in card JSON, so renumbering them is a breaking data-format change.
public enum ResourceType
{
    Spike = 0,  // metal.    Sharp, attack, pierce.      Deals 2x to Wheel.
    Anvil = 1,  // concrete. Sturdy, defense, blunt.     Deals 2x to Spike.
    Wheel = 2,  // rubber.   Elastic, speed, ricochet.   Deals 2x to Anvil.
}

public static class ResourceTypes
{
    // Sizes the fixed-length resource arrays.
    public const int Count = 3;

    public static readonly ResourceType[] All =
    [
        ResourceType.Spike,
        ResourceType.Anvil,
        ResourceType.Wheel,
    ];
}
