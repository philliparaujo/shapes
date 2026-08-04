namespace Shapes.Core.Primitives;

// Status keywords a creature can hold, granted by the `grant_keyword` effect op.
//
// Bit-flags rather than a HashSet for the same reason as TypeMask: this lives on
// CreatureInstance, which the search hot path clones constantly.
[Flags]
public enum KeywordFlags
{
    None = 0,
    Taunt = 1 << 0,
    Reflect = 1 << 1,
    Ricochet = 1 << 2,
}

// Which neighboring slot Ricochet redirects incoming attack damage to. Deterministic and set
// when the keyword is granted -- Ricochet does not pick a random side.
public enum RicochetDirection
{
    Left,
    Right,
}
