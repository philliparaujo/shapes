namespace Shapes.Core.Primitives;

// The set of types a creature has. One bit per ResourceType.
//
// Single creatures have exactly one type. Merging unions the operands' typings, so a merged
// creature can hold two or three -- which matters twice over:
//
//   * income: a creature generates one resource per turn for EACH of its types
//   * defense: a multi-type creature is 2x vulnerable to any attacker type that matches one
//     of its types while another is weak to it (see TypeChart)
//
// A byte-backed flags struct rather than a HashSet: allocation-free, and set operations
// become single instructions in the search hot path.
public readonly struct TypeMask : IEquatable<TypeMask>
{
    private readonly byte _bits;

    private TypeMask(byte bits) => _bits = bits;

    public static readonly TypeMask None = new(0);

    public static TypeMask Of(ResourceType type) => new((byte)(1 << (int)type));

    public static readonly TypeMask Spike = Of(ResourceType.Spike);
    public static readonly TypeMask Anvil = Of(ResourceType.Anvil);
    public static readonly TypeMask Wheel = Of(ResourceType.Wheel);

    public static TypeMask Of(params ResourceType[] types)
    {
        byte bits = 0;
        foreach (var t in types)
        {
            bits |= (byte)(1 << (int)t);
        }

        return new TypeMask(bits);
    }

    public bool Has(ResourceType type) => (_bits & (1 << (int)type)) != 0;

    public bool IsEmpty => _bits == 0;

    // Number of distinct types. Drives per-turn income: a Spike/Wheel creature pays 1 spike
    // and 1 wheel, so this is also the count of resources it generates.
    public int Count => System.Numerics.BitOperations.PopCount(_bits);

    // Whether this carries more than one type -- the condition for the 2x damage rule.
    //
    // This does not tell you whether a creature was merged. Merging two creatures of the
    // same type produces a merged creature that is still single-type, so it stays 1x on
    // defense while being merge-locked. Read the creature's own merged flag for that.
    public bool IsMultiType => Count > 1;

    public TypeMask Union(TypeMask other) => new((byte)(_bits | other._bits));

    public TypeMask Intersect(TypeMask other) => new((byte)(_bits & other._bits));

    public bool Overlaps(TypeMask other) => (_bits & other._bits) != 0;

    public static TypeMask operator |(TypeMask a, TypeMask b) => a.Union(b);

    public static TypeMask operator &(TypeMask a, TypeMask b) => a.Intersect(b);

    // Enumerates the contained types in ResourceType declaration order. Returns an array
    // rather than yielding: the caller is usually iterating income, and at most 3 elements
    // an array beats an iterator's allocation and indirection.
    public ResourceType[] ToArray()
    {
        if (_bits == 0)
        {
            return [];
        }

        var result = new ResourceType[Count];
        var i = 0;
        foreach (var t in ResourceTypes.All)
        {
            if (Has(t))
            {
                result[i++] = t;
            }
        }

        return result;
    }

    public bool Equals(TypeMask other) => _bits == other._bits;

    public override bool Equals(object? obj) => obj is TypeMask other && Equals(other);

    public override int GetHashCode() => _bits;

    public static bool operator ==(TypeMask a, TypeMask b) => a.Equals(b);

    public static bool operator !=(TypeMask a, TypeMask b) => !a.Equals(b);

    // e.g. "Spike/Wheel", or "-" when empty.
    public override string ToString() =>
        _bits == 0 ? "-" : string.Join("/", ToArray());
}
