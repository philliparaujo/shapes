using System;
using System.Collections.Generic;
using Godot;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The Sounds overlay (PLAN.md D4) -- music and SFX volume, each on a 0-5 scale.
//
// Same dimmed-backdrop-plus-centered-panel pattern as TutorialOverlay and MenuPanel, deliberately:
// this is the third modal in the project and inventing a fourth visual language for it would undo
// D3's whole point. It follows TutorialOverlay's structure closely enough that the two read as a
// pair, which is right -- they are the two informational overlays reachable from the same two
// places (the home screen and the in-game pause menu, in both cases directly below Rules).
//
// SIX DISCRETE BUTTONS, NOT A SLIDER. The request asked for "a simple 0-5 select", and the step
// buttons deliver something a slider cannot: the current level is readable at a glance from across
// the room, with no drag needed to discover the range. A Godot HSlider would also need its grabber
// and tick styling specified from scratch to not look stock, which is more theme work than six
// buttons that already inherit UiTheme.
//
// Raises no events for the volume itself -- unlike every other view in this project, which reports
// and lets an owner decide. The difference is that a volume change has exactly one meaning and one
// destination (AudioDirector, the autoload), with no scene-specific policy to apply, so routing it
// through two owners (Lobby and BoardView) would be two copies of the same forwarding with nothing
// to decide between them. Close is still an event, because THAT does differ per host: the lobby
// closes to the home screen, the board closes back to the pause menu.
public partial class SoundsPanel : Control
{
    public event Action? CloseRequested;

    [Export] public NodePath CloseButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/Header/CloseButton";
    [Export] public NodePath MusicStepsPath { get; set; } = "Backdrop/Panel/Margin/Layout/MusicRow/MusicSteps";
    [Export] public NodePath SfxStepsPath { get; set; } = "Backdrop/Panel/Margin/Layout/SfxRow/SfxSteps";

    private Button? _closeButton;
    private HBoxContainer? _musicSteps;
    private HBoxContainer? _sfxSteps;

    private readonly List<Button> _musicButtons = [];
    private readonly List<Button> _sfxButtons = [];

    // Wide enough for a single digit with room around it, and tall enough to be a comfortable
    // touch target on the mobile export (PLAN.md's Android target) -- 44px is the conventional
    // minimum and the same height the lobby's own dropdowns use.
    private static readonly Vector2 StepSize = new(64, 44);

    public override void _Ready()
    {
        _closeButton = GetNode<Button>(CloseButtonPath);
        _musicSteps = GetNode<HBoxContainer>(MusicStepsPath);
        _sfxSteps = GetNode<HBoxContainer>(SfxStepsPath);

        _closeButton.Pressed += () => CloseRequested?.Invoke();

        BuildSteps(_musicSteps, _musicButtons, SetMusic);
        BuildSteps(_sfxSteps, _sfxButtons, SetSfx);

        Visible = false;
    }

    // One button per level, 0..5. The level is both the label and the index, so nothing here has
    // to map between what is shown and what is stored.
    private static void BuildSteps(HBoxContainer row, List<Button> into, Action<int> onPressed)
    {
        // ONE group shared by the whole row -- that is what makes the six mutually exclusive
        // without this class tracking and clearing the previous selection by hand. Constructed
        // once out here rather than per button, since a group of one enforces nothing.
        var group = new ButtonGroup();

        for (var level = AudioSettings.MinLevel; level <= AudioSettings.MaxLevel; level++)
        {
            var value = level;
            var button = new Button
            {
                Text = level.ToString(),
                CustomMinimumSize = StepSize,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,

                // ToggleMode so the selected level can stay visibly pressed. Godot would otherwise
                // only show "pressed" while the mouse is held, and this panel's entire job is to
                // display a persistent choice.
                ToggleMode = true,
                ButtonGroup = group,
            };

            button.Pressed += () => onPressed(value);
            row.AddChild(button);
            into.Add(button);
        }
    }

    // Reads the live levels every time it opens rather than trusting what it last drew: the panel
    // exists in two scenes (Lobby and BoardView), and a level changed in one must show correctly
    // when the other's copy is next opened.
    public void Open()
    {
        Sync();
        Visible = true;

        // Deferred for the same reason MenuPanel.Open and TutorialOverlay.Open defer theirs: a
        // control cannot take focus in the same frame it becomes visible.
        _closeButton!.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => Visible = false;

    // Pushes the stored levels onto the two rows. Safe to call before _Ready has run on a host
    // scene that opens it immediately -- the button lists are empty then and the loops no-op.
    private void Sync()
    {
        var settings = AudioDirector.Instance?.Settings ?? new AudioSettings();
        SyncRow(_musicButtons, settings.MusicLevel);
        SyncRow(_sfxButtons, settings.SfxLevel);
    }

    private static void SyncRow(List<Button> buttons, int level)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            // SetPressedNoSignal, not ButtonPressed: assigning the property fires Pressed, which
            // would call back into SetMusic/SetSfx and re-save the value that was just read --
            // harmless in effect but a genuine feedback loop, and the kind that turns into an
            // infinite one the moment anything downstream also writes back.
            buttons[i].SetPressedNoSignal(i == level);
        }
    }

    private void SetMusic(int level)
    {
        AudioDirector.Instance?.SetMusicLevel(level);

        // No Sync() afterward: the button group already moved the pressed state, and re-syncing
        // would be redoing what the click did.
    }

    private void SetSfx(int level)
    {
        AudioDirector.Instance?.SetSfxLevel(level);

        // Plays a sample at the new level so the choice is audible immediately -- the whole point
        // of an SFX slider is hearing the result, and waiting until the next card is played to
        // find out is what makes a volume control feel broken. Deliberately only on the SFX row:
        // music is already playing continuously, so its own change is audible without a prompt.
        SoundFx.Play(SoundCue.ButtonClick);
    }
}
