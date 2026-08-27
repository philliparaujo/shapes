namespace Shapes.Godot.Adapter;

// The game events that make a sound (PLAN.md D4).
//
// Deliberately NOT the same enum as AnimationCue, even though three members overlap by name.
// AnimationCue is "something happened at this slot worth drawing attention to" and is derived per
// slot -- a spell hitting three creatures is three Damage cues, which is correct for three
// separate damage numbers floating up and badly wrong for audio, where it is three copies of one
// impact sound firing on the same frame. The sets also diverge at both ends: audio wants
// ButtonClick and GainResource, which are not board animations at all, and does not want Scoring,
// which is a per-creature highlight with no distinct sound of its own.
//
// Keeping them separate is what lets SoundScript collapse the per-slot cues into one sound per
// action without distorting the animation vocabulary to suit the speakers.
public enum SoundCue
{
    // A card left the hand and hit the board (creature placed or spell resolved).
    CardPlay,

    // A creature's move was activated.
    UseMove,

    // Two friendly creatures became one.
    Merge,

    // The turn-start score tick: a point scored reads as the OPPONENT losing health, which is the
    // "deal hero damage (at start of turn)" the request names. See BoardAnimator's own note on why
    // scoring is drawn on the victim rather than the scorer -- this is the audio half of that same
    // decision.
    HeroDamage,

    // Income arrived at turn start (a resource pool went up).
    GainResource,

    // Any UI button press. The only cue here not derived from a StateDiff.
    ButtonClick,
}
