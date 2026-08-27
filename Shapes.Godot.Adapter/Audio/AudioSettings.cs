using System.Text.Json.Serialization;

namespace Shapes.Godot.Adapter;

// The player's two volume levels as they live on disk, plus the level -> decibel conversion
// (PLAN.md D4).
//
// Split from the Godot-side SettingsStore for the same reason SavedDeck is split from DeckStore
// and SavedMatch from MatchSaveStore: this half is pure and testable outside the editor, while
// only the file I/O needs Godot's user:// resolution.
//
// A 0-5 INTEGER LEVEL, NOT A FLOAT SLIDER. Requested that way, and it is also the shape that
// survives persistence best: six named steps round-trip exactly through JSON, where a float
// slider's 0.7333 does not, and there is no "is 0.02 audible" ambiguity for the mute end. The
// decibel curve below is what turns those six steps back into something that sounds evenly
// spaced.
public sealed class AudioSettings
{
    // Both default to 3 (of 5) per the request: audible on first launch without being the
    // loudest thing the player owns, and with headroom in both directions so the control
    // demonstrably does something in either direction on first use.
    public const int DefaultLevel = 3;

    public const int MinLevel = 0;
    public const int MaxLevel = 5;

    private int _music = DefaultLevel;
    private int _sfx = DefaultLevel;

    // Clamped in the SETTER rather than trusted from the file: a hand-edited or
    // future-version settings.json is the one input here nothing else validates, and a level of
    // 99 would otherwise reach VolumeToDb and produce a wildly amplified bus.
    public int MusicLevel
    {
        get => _music;
        set => _music = Clamp(value);
    }

    public int SfxLevel
    {
        get => _sfx;
        set => _sfx = Clamp(value);
    }

    public static int Clamp(int level) => level < MinLevel ? MinLevel : level > MaxLevel ? MaxLevel : level;

    // How loud level N actually is, in decibels, which is the unit Godot's AudioServer wants.
    //
    // WHY A TABLE AND NOT A FORMULA. Perceived loudness is logarithmic, so evenly spaced dB steps
    // do NOT sound evenly spaced -- and the obvious linear-amplitude formula (level/5 as a linear
    // gain) crowds every audible difference into the top two steps, leaving 1 and 2 nearly
    // indistinguishable. These six values are picked on the amplitude curve instead: each step is
    // roughly 6 dB, which is the interval that reads as "about half/twice as loud" to a listener.
    //
    // Level 5 is 0 dB -- unity gain, the file played at its authored level -- rather than a boost,
    // so the loudest setting can never clip a source that was already normalized. Every other
    // level is therefore an attenuation, which is the direction a volume control should work in.
    private static readonly float[] SfxDecibelsByLevel =
    [
        // Level 0 is silence. Godot treats -80 dB as effectively muted; the bus is additionally
        // hard-muted at this level (see AudioDirector.Apply) so nothing plays at all rather than
        // playing inaudibly and still costing a voice.
        -80f,
        -30f,
        -24f,
        -18f,
        -12f,
        0f,
    ];

    // Music's own curve, one step louder at every level than SFX's. The two shared one table
    // originally, and the reported symptom ("music settings are too low") was that the *whole*
    // curve sat too quiet for music specifically -- SFX is a short percussive hit judged against a
    // sudden silence, where the same dB reads far louder than a continuous music bed judged against
    // itself. Splitting the tables (rather than moving the shared one) keeps SFX at the level
    // already tuned and not reported as a problem.
    //
    // OLD LEVELS 2-5 BECOME THE NEW 1-4, verbatim -- shifting the whole curve down one slot rather
    // than re-deriving new intermediate values, so a player who had picked "4" and found it
    // comfortable keeps that same dB value at the new "3". Level 5 is new: a genuine BOOST past
    // unity gain (+6 dB, matching the ~6 dB step every other level in this curve uses), because
    // every existing level topped out at the source file's own authored volume -- the only way to
    // give the control a louder step at all is to amplify past that ceiling. Music is the one bus
    // this is safe for: a single continuous track has no other layers to clip against, unlike SFX's
    // pool of up to six simultaneous voices summing on one bus.
    private static readonly float[] MusicDecibelsByLevel =
    [
        -80f,
        -24f,
        -16f,
        -8f,
        0f,
        6f,
    ];

    public static float VolumeToDb(int level) => SfxDecibelsByLevel[Clamp(level)];

    public static float MusicVolumeToDb(int level) => MusicDecibelsByLevel[Clamp(level)];

    // True when this level should silence the bus outright rather than merely attenuate it --
    // see SfxDecibelsByLevel's note on level 0. Shared by both buses: level 0 means silence either
    // way, regardless of which curve the non-zero levels use.
    public static bool IsMuted(int level) => Clamp(level) == MinLevel;

    public AudioSettings Copy() => new() { MusicLevel = MusicLevel, SfxLevel = SfxLevel };
}

// Source-generated JSON, matching DeckSlotsJsonContext's own reason: Godot's .NET export runs
// with trimming, and reflection-based serialization is exactly what trimming removes.
//
// Unknown keys are ignored on load rather than failing the read, the same forward-compatibility
// choice SavedMatchJsonContext documents: a settings file is machine-written by this client, so a
// key from a future version should not cost the player their volume preferences.
[JsonSourceGenerationOptions(
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AudioSettings))]
public sealed partial class AudioSettingsJsonContext : JsonSerializerContext
{
}
