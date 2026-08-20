using System;
using System.Collections.Generic;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The one place a sound is played (PLAN.md D4).
//
// AN AUTOLOAD (project.godot's [autoload] section), which is the whole reason this is a singleton
// node rather than a child of some scene. Music has to survive ChangeSceneToFile: the lobby, the
// deckbuilder, the card browser and a match are four separate scenes, and a music player owned by
// any one of them would stop dead -- and restart from the top -- every time the player moved
// between them. An autoload is Godot's only node that outlives a scene change, so the track keeps
// playing across the boundary exactly as a player expects.
//
// BUSES, NOT PER-PLAYER VOLUME. The two levels are applied to two audio buses ("Music", "Sfx")
// created here at startup, not to each AudioStreamPlayer's own VolumeDb. That matters for SFX:
// there are several players in the pool below, and setting volume per player would mean walking
// the pool on every settings change and would still miss any sound already mid-playback. A bus is
// one number that every routed player inherits live.
//
// The buses are built in code rather than authored in a default_bus_layout.tres for the same
// reason UiTheme is built in code rather than as a .tres: a resource file would restate values
// this project already owns elsewhere, and drift from them silently.
public partial class AudioDirector : Node
{
    // Set in _Ready and never cleared -- an autoload lives for the whole process, so this is
    // effectively a static handle to the one instance, and every caller reaches audio through it
    // (SoundFx.Play, the settings panel) rather than by looking the node up by path.
    public static AudioDirector? Instance { get; private set; }

    private const string MusicBus = "Music";
    private const string SfxBus = "Sfx";

    // How many SFX can overlap. Six is chosen against the cue set rather than picked round: one
    // action can legitimately fire three cues at once (play + gain + score at a turn boundary),
    // and a player acting fast can start the next action's cues over the tail of those -- so the
    // pool has to cover roughly two actions' worth. Beyond that, dropping the oldest sound is the
    // right behaviour anyway; a seventh simultaneous voice is noise, not information.
    private const int SfxVoices = 6;

    private readonly List<AudioStreamPlayer> _sfxPlayers = [];
    private int _nextVoice;

    private AudioStreamPlayer? _musicPlayer;
    private MusicPlaylist _playlist = new(0);
    private readonly List<AudioStream> _tracks = [];

    private AudioSettings _settings = new();

    // Fired whenever a level changes, so an open settings panel can re-read without polling.
    public event Action? SettingsChanged;

    public override void _Ready()
    {
        Instance = this;

        // Loaded before the buses are configured, so the very first Apply below already uses the
        // player's own levels rather than briefly playing at a default they had turned down.
        _settings = SettingsStore.Load();

        EnsureBuses();
        BuildMusicPlayer();
        BuildSfxPool();
        Apply();

        StartMusic();
        SoundBank.WarnOnMissingFiles();

        // Every button in the game gets its click sound from here on, including ones built in code
        // long after this runs -- see SoundFx.AttachClicksGlobally on why a tree hook rather than a
        // per-screen walk. Connected from the autoload because this is the one node guaranteed to
        // exist before any screen does.
        SoundFx.AttachClicksGlobally(GetTree());
    }

    // Stops both music and any in-flight SFX before the tree tears down.
    //
    // Godot frees an autoload's children at shutdown regardless, so this is not a leak fix in the
    // ownership sense -- it is about the STREAMS: a player still playing at exit holds its
    // AudioStream referenced past the point the resource system expects to have released
    // everything, which is what surfaces as "resources still in use at exit". Harmless in a real
    // session (the process is ending anyway) but it is noise in exactly the headless logs this
    // project uses to verify Godot behaviour, and a genuine warning is worth more than a familiar
    // one.
    public override void _ExitTree()
    {
        // Disconnected before anything else: the hook holds a delegate that keeps running as the
        // tree tears down, wiring click handlers onto nodes on their way out.
        SoundFx.DetachClicksGlobally(GetTree());

        _musicPlayer?.Stop();

        foreach (var player in _sfxPlayers)
        {
            player.Stop();
        }

        // Drops this class's own references to the loaded streams. Godot's resource system checks
        // for still-referenced resources at shutdown, and an autoload's fields are alive at that
        // point -- so the cached tracks (and SoundBank's cue cache) are exactly what it counts.
        // Clearing them keeps the headless logs honest; see this method's header.
        _musicPlayer!.Stream = null;
        foreach (var player in _sfxPlayers)
        {
            player.Stream = null;
        }

        _tracks.Clear();
        SoundBank.Clear();
    }

    // Creates the two buses if they are not already present. Idempotent, because an autoload can
    // in principle be re-readied by a tool/editor run, and adding a duplicate bus would silently
    // route half the sounds to a bus nothing sets the volume on.
    private static void EnsureBuses()
    {
        foreach (var name in new[] { MusicBus, SfxBus })
        {
            if (AudioServer.GetBusIndex(name) != -1)
            {
                continue;
            }

            var index = AudioServer.BusCount;
            AudioServer.AddBus(index);
            AudioServer.SetBusName(index, name);

            // Both route to Master, so a future master volume (or a mute-on-focus-loss policy)
            // has one place to act on rather than two.
            AudioServer.SetBusSend(index, "Master");
        }
    }

    private void BuildMusicPlayer()
    {
        _musicPlayer = new AudioStreamPlayer { Bus = MusicBus };

        // The playlist advances when a track ends. This is what makes the rotation automatic:
        // nothing schedules the next track, the end of the current one asks for it -- so a track
        // of any length works with no duration bookkeeping here.
        _musicPlayer.Finished += PlayNextTrack;
        AddChild(_musicPlayer);

        LoadTracks();
        _playlist = new MusicPlaylist(_tracks.Count);
    }

    // Reads Audio/Music in sorted filename order, which is what makes the "1,2,3,4,1,2,..."
    // rotation the request asked for fall out of the file names themselves rather than out of a
    // hard-coded list here -- adding a 5.ogg extends the cycle with no code change.
    //
    // DirAccess rather than a glob: res:// is a virtual filesystem in an exported build (the files
    // are inside the .pck, not on disk), so System.IO cannot enumerate them.
    private void LoadTracks()
    {
        _tracks.Clear();

        using var dir = DirAccess.Open("res://Audio/Music");
        if (dir is null)
        {
            GD.PushWarning("AudioDirector: res://Audio/Music not found; music is disabled.");
            return;
        }

        var names = new List<string>();
        dir.ListDirBegin();
        while (true)
        {
            var name = dir.GetNext();
            if (name.Length == 0)
            {
                break;
            }

            if (dir.CurrentIsDir())
            {
                continue;
            }

            // An EXPORTED build renames .ogg to .ogg.import-backed resources but GetNext still
            // reports the source name; in the editor both the .ogg and its .import sidecar are
            // listed. Filtering to .ogg covers both, and the .import entries are what would
            // otherwise be loaded as null and played as silence.
            if (name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        dir.ListDirEnd();

        // Ordinal sort so "10.ogg" lands after "9.ogg" only if someone pads it -- documented
        // rather than solved, because the current set is 1-4 and a natural-order comparer would be
        // more machinery than the problem deserves. Rename to 01/02 if the set ever exceeds nine.
        names.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (GD.Load<AudioStream>($"res://Audio/Music/{name}") is { } stream)
            {
                _tracks.Add(stream);
            }
        }
    }

    private void BuildSfxPool()
    {
        for (var i = 0; i < SfxVoices; i++)
        {
            var player = new AudioStreamPlayer { Bus = SfxBus };
            AddChild(player);
            _sfxPlayers.Add(player);
        }
    }

    // Starts (or restarts) the rotation from its next track. Safe to call with no tracks loaded --
    // Next() returns null and nothing plays, which is the "no music files in the build" case.
    private void StartMusic() => PlayNextTrack();

    private void PlayNextTrack()
    {
        if (_musicPlayer is null || _playlist.Next() is not { } index)
        {
            return;
        }

        // Muted is handled by the bus, not by declining to play: a track that keeps running
        // silently at level 0 means raising the volume mid-track resumes it in place, rather than
        // starting the rotation over from track 1 and losing the player's position in the cycle.
        _musicPlayer.Stream = _tracks[index];
        _musicPlayer.Play();
    }

    public AudioSettings Settings => _settings;

    public void SetMusicLevel(int level)
    {
        _settings.MusicLevel = level;
        Persist();
    }

    public void SetSfxLevel(int level)
    {
        _settings.SfxLevel = level;
        Persist();
    }

    private void Persist()
    {
        Apply();
        SettingsStore.Save(_settings);
        SettingsChanged?.Invoke();
    }

    // Pushes both levels onto their buses. Mute is set as well as volume, per AudioSettings'
    // note on level 0: -80 dB is inaudible but still costs a voice and still counts as playing,
    // and an explicit mute is the honest expression of "off".
    private void Apply()
    {
        ApplyBus(MusicBus, _settings.MusicLevel, AudioSettings.MusicVolumeToDb);
        ApplyBus(SfxBus, _settings.SfxLevel, AudioSettings.VolumeToDb);
    }

    // Takes the curve as a parameter because Music and SFX no longer share one -- see
    // AudioSettings.MusicDecibelsByLevel's note on why the same level means a different dB on each
    // bus now.
    private static void ApplyBus(string bus, int level, Func<int, float> toDb)
    {
        var index = AudioServer.GetBusIndex(bus);
        if (index == -1)
        {
            return;
        }

        AudioServer.SetBusVolumeDb(index, toDb(level));
        AudioServer.SetBusMute(index, AudioSettings.IsMuted(level));
    }

    // Plays one cue, or does nothing if it has no sound mapped or SFX are muted.
    //
    // ROUND-ROBIN OVER THE POOL rather than "find a free player": with a fixed rotation the
    // seventh sound in a row reliably interrupts the oldest still-playing one, which is the
    // behaviour SfxVoices' own comment argues for. Searching for an idle player instead would
    // silently DROP the seventh sound, so a burst of activity would go quiet exactly when the most
    // is happening.
    public void PlayCue(SoundCue cue)
    {
        if (AudioSettings.IsMuted(_settings.SfxLevel) || _sfxPlayers.Count == 0)
        {
            return;
        }

        if (SoundBank.StreamFor(cue) is not { } stream)
        {
            return;
        }

        var player = _sfxPlayers[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _sfxPlayers.Count;

        player.Stream = stream;
        player.Play();
    }
}
