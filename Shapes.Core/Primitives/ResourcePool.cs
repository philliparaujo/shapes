namespace Shapes.Core.Primitives;

// A player's held resources: one count per type.
//
// A 3-field readonly struct rather than a dictionary. This sits in the hot search path --
// MCTS copies pools constantly -- so it must be allocation-free and cheap to compare.
// Being immutable also means a pool can never be mutated out from under a search node.
//
// Counts are non-negative by construction. Subtracting more than is held is a bug in the
// caller (legal-action generation should have filtered the action out), so Subtract throws
// rather than clamping. Use TrySubtract where failure is an expected outcome.
public readonly struct ResourcePool : IEquatable<ResourcePool>
{
    public static readonly ResourcePool Empty = default;

    public int Spike { get; }
    public int Anvil { get; }
    public int Wheel { get; }

    public ResourcePool(int spike, int anvil, int wheel)
    {
        if (spike < 0) throw new ArgumentOutOfRangeException(nameof(spike), spike, "Resource counts cannot be negative.");
        if (anvil < 0) throw new ArgumentOutOfRangeException(nameof(anvil), anvil, "Resource counts cannot be negative.");
        if (wheel < 0) throw new ArgumentOutOfRangeException(nameof(wheel), wheel, "Resource counts cannot be negative.");

        Spike = spike;
        Anvil = anvil;
        Wheel = wheel;
    }

    // Indexer over ResourceType so callers can loop the three types without a switch.
    // The enum values are pinned to 0/1/2 precisely so this stays a jump-free lookup.
    public int this[ResourceType type] => type switch
    {
        ResourceType.Spike => Spike,
        ResourceType.Anvil => Anvil,
        ResourceType.Wheel => Wheel,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown resource type."),
    };

    public int Total => Spike + Anvil + Wheel;

    public bool IsEmpty => Spike == 0 && Anvil == 0 && Wheel == 0;

    public static ResourcePool Of(ResourceType type, int amount) => type switch
    {
        ResourceType.Spike => new ResourcePool(amount, 0, 0),
        ResourceType.Anvil => new ResourcePool(0, amount, 0),
        ResourceType.Wheel => new ResourcePool(0, 0, amount),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown resource type."),
    };

    public ResourcePool Add(ResourcePool other) =>
        new(Spike + other.Spike, Anvil + other.Anvil, Wheel + other.Wheel);

    public ResourcePool Add(ResourceType type, int amount) => Add(Of(type, amount));

    // True when this pool holds at least `cost` of every type -- i.e. the cost is affordable.
    public bool Covers(ResourcePool cost) =>
        Spike >= cost.Spike && Anvil >= cost.Anvil && Wheel >= cost.Wheel;

    public bool TrySubtract(ResourcePool cost, out ResourcePool result)
    {
        if (!Covers(cost))
        {
            result = default;
            return false;
        }

        result = new ResourcePool(Spike - cost.Spike, Anvil - cost.Anvil, Wheel - cost.Wheel);
        return true;
    }

    // Throws when the cost is not covered. An unaffordable subtraction means legal-action
    // generation let through an action the player cannot pay for, which is a logic error
    // worth surfacing immediately rather than absorbing into a clamped zero.
    public ResourcePool Subtract(ResourcePool cost)
    {
        if (!TrySubtract(cost, out var result))
        {
            throw new InvalidOperationException(
                $"Cannot subtract {cost} from {this}: insufficient resources.");
        }

        return result;
    }

    public static ResourcePool operator +(ResourcePool a, ResourcePool b) => a.Add(b);

    public static ResourcePool operator -(ResourcePool a, ResourcePool b) => a.Subtract(b);

    public bool Equals(ResourcePool other) =>
        Spike == other.Spike && Anvil == other.Anvil && Wheel == other.Wheel;

    public override bool Equals(object? obj) => obj is ResourcePool other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Spike, Anvil, Wheel);

    public static bool operator ==(ResourcePool a, ResourcePool b) => a.Equals(b);

    public static bool operator !=(ResourcePool a, ResourcePool b) => !a.Equals(b);

    // Compact form for test failures and console rendering, e.g. "2/0/1".
    public override string ToString() => $"{Spike}/{Anvil}/{Wheel}";
}
