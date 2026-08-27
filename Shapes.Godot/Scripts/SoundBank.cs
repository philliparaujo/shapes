using System;
using System.Collections.Generic;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// Which file each SoundCue plays (DESIGN.md D4), keyed the same way CardArt keys card art -- one
// lookup table, cached, with a missing file degrading to silence rather than to an error.
//
// THE ASSIGNMENTS ARE A FIRST PASS AND ARE MEANT TO BE RE-POINTED. They were picked by matching
// each cue's physical metaphor to the imported Kenney-style library by name and character, without
// having heard them (the assistant that wrote this cannot play audio). The mapping lives in this
// one table precisely so re-pointing a cue is a one-line edit here rather than a hunt through the
// call sites -- the reasoning for each choice is recorded beside it so a later listening pass can
// tell "this was deliberate but wrong" from "this was arbitrary".
public static class SoundBank
{
    private const string SfxDirectory = "res://Audio/Sfx";

    // The cue -> filename table. See the class header on why these are provisional.
    private static readonly Dictionary<SoundCue, string> Files = new()
    {
        // A card meeting the table. impactWood is the softest of the wood/plank impacts -- a card
        // is light, so a heavy impact would read as a creature landing rather than a card being
        // set down.
        [SoundCue.CardPlay] = "impactWood_light_000.ogg",

        // A move connecting. The one cue that should read as force rather than as placement, so it
        // takes the metallic plate hit -- distinct in timbre from CardPlay's wood, which matters
        // because these two are the sounds a player hears most and must not blur together.
        [SoundCue.UseMove] = "footstep_concrete_004.ogg",

        // Two creatures becoming one. impactSoft's duller, heavier body reads as mass combining
        // rather than as a strike, and its medium weight separates it from the two light impacts
        // above so a merge is audibly a bigger event than a play.
        [SoundCue.Merge] = "confirmation_003.ogg",

        // The score tick, heard as the opponent losing health (see SoundCue.HeroDamage). The
        // heaviest impact in the set, because this is the only cue that moves the actual win
        // condition -- everything else is board state, this is the game ending sooner.
        [SoundCue.HeroDamage] = "impactPlate_light_000.ogg",

        // Income arriving. A pitched, non-percussive tick rather than an impact: resources are not
        // a collision, and this cue fires every single turn, so it has to be the least fatiguing
        // sound in the set.
        [SoundCue.GainResource] = "drop_001.ogg",

        // Any button. The quietest, shortest sample available -- a UI click is confirmation, not
        // an event, and it competes with every other cue for attention.
        [SoundCue.ButtonClick] = "tick_001.ogg",
    };

    // Cached by cue for the same reason CardArt caches by path: PlayCue runs on every action and
    // every button press, and ResourceLoader.Exists is not itself cached, so an uncached lookup
    // would re-probe the filesystem on each one. A null entry records "no file for this cue" so a
    // missing sample is probed once rather than every press.
    private static readonly Dictionary<SoundCue, AudioStream?> Cache = [];

    // The stream for a cue, or null when its file is absent -- callers treat null as "play
    // nothing", so an unmapped or missing sound is silent rather than fatal. Same degradation
    // CardArt's placeholder fallback provides for a card with no art yet.
    public static AudioStream? StreamFor(SoundCue cue)
    {
        if (Cache.TryGetValue(cue, out var cached))
        {
            return cached;
        }

        AudioStream? stream = null;
        if (Files.TryGetValue(cue, out var file))
        {
            var path = $"{SfxDirectory}/{file}";
            if (ResourceLoader.Exists(path))
            {
                stream = GD.Load<AudioStream>(path);
            }
            else
            {
                GD.PushWarning($"SoundBank: {cue} maps to missing file {path}; it will be silent.");
            }
        }

        Cache[cue] = stream;
        return stream;
    }

    // Probes every cue at startup so a broken mapping is reported ONCE, loudly, at a predictable
    // moment -- called from AudioDirector._Ready.
    //
    // WHY THIS EXISTS. StreamFor degrades a missing file to silence, which is the right runtime
    // behaviour (one bad filename must not take the game down) but a genuinely bad authoring
    // experience: a typo in the table above is invisible until someone notices a specific sound is
    // not playing and has no reason to suspect the filename. That is not hypothetical -- HeroDamage
    // shipped as "imapctPlate_light_000.ogg" and read exactly like a cue-priority bug, which is
    // what it was reported as. The lesson is that "silent fallback" needs a loud counterpart at the
    // one moment a human is in a position to act on it.
    //
    // Deliberately at startup rather than in a test: the table's correctness is a claim about files
    // on disk in the Godot project, which is precisely what unit tests in Shapes.Tests cannot see.
    public static void WarnOnMissingFiles()
    {
        foreach (var cue in Enum.GetValues<SoundCue>())
        {
            if (StreamFor(cue) is null)
            {
                GD.PushError($"SoundBank: {cue} has no playable sound -- it will be silent.");
            }
        }
    }

    // Releases the cached streams. Called only from AudioDirector._ExitTree -- see its note on why
    // a static cache of resources is worth clearing at shutdown. Harmless at any other time: the
    // next StreamFor simply reloads, and GD.Load has its own resource cache underneath this one.
    public static void Clear() => Cache.Clear();
}
