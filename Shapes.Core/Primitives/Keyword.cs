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

// Which neighboring slot(s) Ricochet redirects incoming attack damage to. Deterministic and set
// when the keyword is granted -- Ricochet does not pick a random side.
//
// Flags rather than a plain enum because a creature may hold BOTH sides at once -- Snowball's
// Carom grants left and right as two separate effects, and under the old
// single-direction model the second grant silently overwrote the first. With one side armed the
// behaviour is unchanged; with both, CombatResolver tries the granted sides in order and
// redirects to the first that has a friendly neighbor, so a missing neighbor on one side falls
// through to the other rather than wasting the charge.
//
// EACH ARMED SIDE IS ITS OWN CHARGE: a redirect spends only the side it used, so a creature
// holding both deflects twice (once per side) before the keyword clears. That is what makes
// granting both worth more than granting either alone -- see CreatureInstance.ConsumeRicochet.
//
// `Both` is the union, for callers that want to test or set the pair at once; card JSON has no
// spelling for it and arms each side with its own grant.
[Flags]
public enum RicochetDirection
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Both = Left | Right,
}
