using Godot;

namespace Shapes.Godot.Scripts;

// TEMPORARY -- scaffolding for comparing the three merged-art treatments (PLAN.md 5.C-UI).
// Loads the card browser three times, once per MergedArtStyle, and screenshots each.
//
// Uses the browser rather than a live board because its Merged view already renders real merged
// creatures through SlotView, so whatever it shows is exactly what the board will show.
public partial class MergedArtShotHarness : Control
{
    private static readonly MergedArtStyle[] Styles =
    [
        MergedArtStyle.AngledSoft,
        MergedArtStyle.AngledAsymmetric,
        MergedArtStyle.Layered,
    ];

    private static readonly string[] FileNames =
    [
        "merged-1-angled.png",
        "merged-2-asymmetric.png",
        "merged-3-layered.png",
    ];

    // Frames to let the browser build its grid before shooting. The Merged view instantiates a
    // lot of SlotViews, so this is generous.
    private const int SettleFrames = 90;

    private int _frame;
    private int _styleIndex = -1;
    private Node? _browser;

    public override void _Ready() => LoadNextStyle();

    public override void _Process(double delta)
    {
        _frame++;

        if (_frame < SettleFrames)
        {
            return;
        }

        Shoot(FileNames[_styleIndex]);

        if (_styleIndex + 1 >= Styles.Length)
        {
            GetTree().Quit();
            return;
        }

        LoadNextStyle();
    }

    private void LoadNextStyle()
    {
        _styleIndex++;
        _frame = 0;

        if (_browser is not null)
        {
            RemoveChild(_browser);
            _browser.QueueFree();
        }

        // Set BEFORE instantiating: SlotView reads the style while rendering, and the browser
        // renders its whole grid during _Ready.
        SlotView.MergedStyle = Styles[_styleIndex];

        var scene = GD.Load<PackedScene>("res://Scenes/CardBrowser.tscn");
        _browser = scene.Instantiate();
        AddChild(_browser);

        SelectMergedView();
    }

    // Drives the browser's own Creature-type dropdown to "Merged" rather than reaching into its
    // internals -- the same option a player would pick.
    private void SelectMergedView()
    {
        var dropdown = FindMergedDropdown(_browser!);
        if (dropdown is null)
        {
            GD.PrintErr("MergedArtShotHarness: creature-type dropdown not found");
            return;
        }

        for (var i = 0; i < dropdown.ItemCount; i++)
        {
            if (dropdown.GetItemText(i) == "Merged")
            {
                dropdown.Select(i);
                dropdown.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                return;
            }
        }
    }

    private static OptionButton? FindMergedDropdown(Node node)
    {
        if (node is OptionButton option)
        {
            for (var i = 0; i < option.ItemCount; i++)
            {
                if (option.GetItemText(i) == "Merged")
                {
                    return option;
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            if (FindMergedDropdown(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void Shoot(string fileName)
    {
        var image = GetViewport().GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath($"user://{fileName}");
        image.SavePng(path);
        GD.Print($"MergedArtShotHarness wrote {path}");
    }
}
