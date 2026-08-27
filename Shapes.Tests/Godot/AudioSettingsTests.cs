using Shapes.Godot.Adapter;

namespace Shapes.Tests.Godot;

// DESIGN.md D4: the audio settings model and the music rotation. The bar here is that the two rules
// with real consequences hold -- a level from disk can never reach the mixer out of range, and the
// rotation visits every track before repeating any.
public class AudioSettingsTests
{
    [Fact]
    public void Defaults_to_level_three_on_both_channels()
    {
        var settings = new AudioSettings();

        Assert.Equal(3, settings.MusicLevel);
        Assert.Equal(3, settings.SfxLevel);
    }

    // The setter clamps rather than throwing, because its real input is a settings.json that
    // nothing else validates -- see AudioSettings' own note.
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(99, 5)]
    public void Levels_clamp_into_range(int assigned, int expected)
    {
        var settings = new AudioSettings { MusicLevel = assigned, SfxLevel = assigned };

        Assert.Equal(expected, settings.MusicLevel);
        Assert.Equal(expected, settings.SfxLevel);
    }

    // The property that makes the control usable: every step has to be audibly different from its
    // neighbours, which means strictly increasing. A table that accidentally repeated a value
    // would leave two buttons that do the same thing. Checked on both curves -- see
    // AudioSettings.MusicDecibelsByLevel on why Music no longer shares SFX's table.
    [Fact]
    public void Volume_increases_strictly_with_level()
    {
        for (var level = AudioSettings.MinLevel; level < AudioSettings.MaxLevel; level++)
        {
            Assert.True(
                AudioSettings.VolumeToDb(level) < AudioSettings.VolumeToDb(level + 1),
                $"SFX level {level} should be quieter than {level + 1}");

            Assert.True(
                AudioSettings.MusicVolumeToDb(level) < AudioSettings.MusicVolumeToDb(level + 1),
                $"music level {level} should be quieter than {level + 1}");
        }
    }

    // Unity gain at the top of the SFX curve, per its own note: the loudest SFX setting plays the
    // file as authored rather than boosting it into clipping -- safe for a single card sound, less
    // safe for a bus where up to six voices can sum at once (AudioDirector.SfxVoices).
    [Fact]
    public void Sfx_max_level_is_unity_gain()
    {
        Assert.Equal(0f, AudioSettings.VolumeToDb(AudioSettings.MaxLevel));
    }

    // Music's curve deliberately does NOT top out at unity -- reported as "music settings are too
    // low" even at max, because a continuous music bed reads quieter at the same dB than a short
    // SFX hit does. Level 5 boosts past the source file's own volume; see MusicDecibelsByLevel.
    [Fact]
    public void Music_max_level_boosts_past_unity_gain()
    {
        Assert.True(AudioSettings.MusicVolumeToDb(AudioSettings.MaxLevel) > 0f);
    }

    // THE SHIFT ITSELF: old levels 2-5 become the new 1-4, unchanged, so a player who had already
    // settled on a comfortable music volume keeps that same dB after the rescale -- only the label
    // on the button changed, not what it sounds like at every level except the new top one.
    [Theory]
    [InlineData(1, -30f)]
    [InlineData(2, -24f)]
    [InlineData(3, -18f)]
    [InlineData(4, 0f)]
    public void Music_levels_one_through_four_match_the_old_two_through_five(int newLevel, float expectedDb)
    {
        Assert.Equal(expectedDb, AudioSettings.MusicVolumeToDb(newLevel));
    }

    [Fact]
    public void Only_level_zero_is_muted()
    {
        Assert.True(AudioSettings.IsMuted(0));

        for (var level = 1; level <= AudioSettings.MaxLevel; level++)
        {
            Assert.False(AudioSettings.IsMuted(level));
        }
    }

    // Out-of-range levels are clamped here too, not just in the setter -- both lookups index a
    // fixed table, so an unclamped call would be an IndexOutOfRangeException rather than a wrong
    // volume.
    [Fact]
    public void Volume_lookup_tolerates_out_of_range_levels()
    {
        Assert.Equal(AudioSettings.VolumeToDb(0), AudioSettings.VolumeToDb(-1));
        Assert.Equal(AudioSettings.VolumeToDb(5), AudioSettings.VolumeToDb(999));
        Assert.Equal(AudioSettings.MusicVolumeToDb(0), AudioSettings.MusicVolumeToDb(-1));
        Assert.Equal(AudioSettings.MusicVolumeToDb(5), AudioSettings.MusicVolumeToDb(999));
    }
}
