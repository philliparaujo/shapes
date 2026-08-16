using System;
using Godot;
using Shapes.Core.Primitives;
using GodotTimer = Godot.Timer;

namespace Shapes.Godot.Scripts;

// The paginated "Rules"/"Tutorial" overlay, opened from MenuPanel's Rules button.
//
// Same dimmed-backdrop-plus-centered-panel pattern MenuPanel already uses (Backdrop stops clicks
// reaching the board underneath, mouse_filter = 0), rather than a new visual language for a
// second kind of modal. Pages are plain data (TutorialPage) built once in code from
// references/info.md rather than authored per-node in the .tscn, since the page count and text
// are expected to change as art/gifs are added later (PLAN.md note in the originating request) --
// a fixed node tree would need editing by hand for every wording change.
//
// Raises events instead of closing itself for the same "report, don't decide" reason MenuPanel
// gives: BoardView owns which overlay is on top and decides what ESC should dismiss.
public partial class TutorialOverlay : Control
{
    public event Action? CloseRequested;

    [Export] public NodePath TitleLabelPath { get; set; } = "Backdrop/Panel/Margin/Layout/Header/TitleLabel";
    [Export] public NodePath CloseButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/Header/CloseButton";
    [Export] public NodePath ImageRowPath { get; set; } = "Backdrop/Panel/Margin/Layout/ImageRow";
    [Export] public NodePath BodyLabelPath { get; set; } = "Backdrop/Panel/Margin/Layout/BodyScroll/BodyLabel";
    [Export] public NodePath PrevButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/Footer/PrevButton";
    [Export] public NodePath NextButtonPath { get; set; } = "Backdrop/Panel/Margin/Layout/Footer/NextButton";
    [Export] public NodePath PageLabelPath { get; set; } = "Backdrop/Panel/Margin/Layout/Footer/PageLabel";

    private Label? _title;
    private Button? _closeButton;
    private HBoxContainer? _imageRow;
    private RichTextLabel? _body;
    private Button? _prevButton;
    private Button? _nextButton;
    private Label? _pageLabel;

    // Every page's image(s) fit into this box (KEEP_ASPECT, never cropped/stretched) so the Rules
    // page reads as one consistent layout across a 4:1 UI strip, a portrait card and a 16:9-ish
    // gif alike, rather than each page resizing around whatever its own source screenshot happens
    // to be shaped like. Matches ImageRow's custom_minimum_size in TutorialOverlay.tscn.
    private static readonly Vector2 ImageBoxSize = new(360, 260);

    // Hard ceiling a DisplayScale-enlarged box (TutorialImage.DisplayScale) is clamped against
    // before layout -- Objective's 2.5x banner would otherwise ask for a box wider than the
    // panel's own content area (Panel is 820 wide minus Margin's 28px each side = 764 usable) and
    // taller than is sane for a single image above a page of body text. Clamping AFTER scaling
    // and re-deriving from the aspect ratio (see BuildImageRect) keeps the image's own
    // proportions intact rather than letting the two axes clamp independently and distort it.
    private static readonly Vector2 MaxImageBoxSize = new(750, 400);

    // Animated pages swap frames on a Timer rather than Godot's built-in AnimatedTexture: that
    // resource caps at 256 frames authored one-by-one in the editor/via SetFrameTexture calls,
    // which has no path from "a content record with a frame count" the way this file's
    // TutorialImage does -- driving one AtlasTexture region by hand is the more direct fit for
    // data that already knows its own frame grid.
    private const double AnimationFrameSeconds = 1.0 / 12.0;
    private GodotTimer? _animationTimer;
    private Texture2D? _animationSheet;
    private AtlasTexture? _animationFrame;
    private TutorialImage? _animationSource;
    private int _animationIndex;

    private TutorialPage[] _pages = TutorialContent.Pages;
    private int _pageIndex;

    // Matched to BodyLabel's normal_font_size in TutorialOverlay.tscn -- kept slightly larger
    // than the panel's original 15px. The project's window/stretch/mode is "canvas_items"
    // (project.godot), which upscales the whole UI to the real window size rather than rendering
    // at native resolution; a non-integer scale factor there softens small text project-wide, and
    // there is no per-panel fix for that (it would need an MSDF font resource swapped in
    // everywhere, well beyond this page). A larger base size is the contained mitigation --
    // more actual glyph detail survives the same multiplicative blur.
    private const int BodyFontSize = 17;

    // Same highlight yellow every other rules-text view in the project uses for "this word is not
    // the ordinary case" (InlineResourceIcons.KeywordColor, ResourceIconFactory
    // .DiscountedNumberColor) -- a plain (non-resource) bold word takes this so the Rules page's
    // emphasis reads as the same convention as a card's move text, not a second one invented here.
    private static readonly Color BoldColor = ResourceIconFactory.DiscountedNumberColor;

    private FontVariation? _lightFont;
    private FontVariation? _boldFont;
    private FontVariation? _italicFont;

    public override void _Ready()
    {
        _title = GetNode<Label>(TitleLabelPath);
        _closeButton = GetNode<Button>(CloseButtonPath);
        _imageRow = GetNode<HBoxContainer>(ImageRowPath);
        _body = GetNode<RichTextLabel>(BodyLabelPath);
        _prevButton = GetNode<Button>(PrevButtonPath);
        _nextButton = GetNode<Button>(NextButtonPath);
        _pageLabel = GetNode<Label>(PageLabelPath);

        _animationTimer = new GodotTimer { WaitTime = AnimationFrameSeconds, Autostart = false };
        _animationTimer.Timeout += AdvanceAnimationFrame;
        AddChild(_animationTimer);

        _closeButton.Pressed += () => CloseRequested?.Invoke();
        _prevButton.Pressed += () => GoToPage(_pageIndex - 1);
        _nextButton.Pressed += () => GoToPage(_pageIndex + 1);

        // The default theme's Noto Sans is a variable font, so every weight used on this page is
        // a FontVariation on it rather than a separate font asset -- there is no bundled font file
        // in this project (see CardStyle/KeywordTooltip, which all lean on the same default theme
        // font too). Built once here and reused per GoToPage rather than per run, since a
        // FontVariation is a Resource -- cheap to share, wasteful to reallocate per word.
        var baseFont = _body!.GetThemeDefaultFont();

        // -0.6: light enough to read as de-emphasized body copy without thinning past legibility
        // at 15px, picked by eye against the panel's dark background.
        _lightFont = new FontVariation { BaseFont = baseFont };
        _lightFont.SetVariationEmbolden(-0.6f);

        // Heavier than a typical bold face for the same reason InlineResourceIcons.BoldFont is:
        // Embolden thickens the outline by a fraction of an em, so body-text sizes need more of it
        // than a print bold face would to read as clearly heavier than the light run around it.
        _boldFont = new FontVariation { BaseFont = baseFont };
        _boldFont.SetVariationEmbolden(0.6f);

        _italicFont = new FontVariation { BaseFont = baseFont };
        _italicFont.SetVariationTransform(new Transform2D(1f, 0.2f, 0f, 1f, 0f, 0f));

        Visible = false;
    }

    public void Open()
    {
        GoToPage(0);
        Visible = true;

        // Deferred for the same reason MenuPanel.Open defers its focus grab: a control cannot
        // take focus in the same frame it becomes visible.
        _closeButton!.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => Visible = false;

    private void GoToPage(int index)
    {
        _pageIndex = Mathf.Clamp(index, 0, _pages.Length - 1);
        var page = _pages[_pageIndex];

        _title!.Text = page.Title;
        _pageLabel!.Text = $"{_pageIndex + 1} / {_pages.Length}";
        _prevButton!.Disabled = _pageIndex == 0;
        _nextButton!.Disabled = _pageIndex == _pages.Length - 1;

        RenderImages(page);
        RenderBody(page);
    }

    // Rebuilds ImageRow for the page: one TextureRect per TutorialImage (Cards' two side by side),
    // each fit into ImageBoxSize with KEEP_ASPECT so different source aspect ratios never crop or
    // distort -- see ImageBoxSize's own comment. A page with no images leaves the row empty rather
    // than collapsing its height, so Prev/Next never shifts the body text up and down as the
    // reader pages between an illustrated topic and a text-only one.
    private void RenderImages(TutorialPage page)
    {
        foreach (var child in _imageRow!.GetChildren())
        {
            _imageRow.RemoveChild(child);
            child.QueueFree();
        }

        // Stopped unconditionally before rebuilding: leaving a page with an animated image cancels
        // that page's own frame source below, and a still-running timer whose _animationSource no
        // longer matches the new page's texture would otherwise animate the wrong TextureRect (or
        // one that was just freed) on its next tick.
        _animationTimer!.Stop();
        _animationSource = null;

        if (page.Images is not { Count: > 0 } images)
        {
            return;
        }

        // Cards' two images split ImageBoxSize's width between them rather than each claiming a
        // full ImageBoxSize -- otherwise two side-by-side boxes would double the row's width and
        // the row would stop matching every other page's footprint, defeating the whole point of
        // a shared image box (see ImageBoxSize's own comment on why consistent sizing means a
        // consistent OUTER box, not a consistent image shape).
        var slotWidth = ImageBoxSize.X / images.Count;
        var slotSize = new Vector2(slotWidth, ImageBoxSize.Y);

        foreach (var image in images)
        {
            _imageRow.AddChild(BuildImageRect(image, slotSize));
        }
    }

    private TextureRect BuildImageRect(TutorialImage image, Vector2 slotSize)
    {
        var texture = GD.Load<Texture2D>(image.FramePath);

        // One frame's pixel size, not the whole sheet's -- an animated image's texture IS the
        // full sprite sheet (see the animated branch below), so its own Texture2D.GetSize() would
        // be the sheet's grid dimensions, not one frame's shape.
        var nativeSize = image.IsAnimated
            ? new Vector2(image.FrameWidth, image.FrameHeight)
            : texture.GetSize();

        // Fit nativeSize's OWN aspect ratio into slotSize first (KeepAspectCentered's own math,
        // done here rather than left to the Control so DisplayScale has something correctly-
        // shaped to scale). Scaling slotSize directly, as an earlier version of this did, ignored
        // the image's aspect ratio entirely -- a 4:1 banner scaled inside a 1.4:1 box clamped
        // against the box's own height long before it came anywhere near the requested width.
        var fitScale = Mathf.Min(slotSize.X / nativeSize.X, slotSize.Y / nativeSize.Y);
        var boxSize = nativeSize * fitScale * image.DisplayScale;

        // Clamped uniformly (one shared factor for both axes) rather than per-axis, so an
        // oversized box shrinks back within MaxImageBoxSize without changing the aspect ratio
        // just derived above.
        var overflow = Mathf.Max(boxSize.X / MaxImageBoxSize.X, boxSize.Y / MaxImageBoxSize.Y);
        if (overflow > 1f)
        {
            boxSize /= overflow;
        }

        var rect = new TextureRect
        {
            CustomMinimumSize = boxSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };

        if (!image.IsAnimated)
        {
            rect.Texture = texture;
            return rect;
        }

        // Only one animated image is ever on screen at a time in this content (board-gif and
        // merging-gif each own a whole page), so the timer drives this single rect directly
        // rather than a list -- see the fields' own comments on why a Timer over AtlasTexture
        // regions rather than Godot's built-in AnimatedTexture.
        _animationSheet = texture;
        _animationFrame = new AtlasTexture
        {
            Atlas = _animationSheet,
            Region = FrameRegion(image, 0),
        };
        rect.Texture = _animationFrame;

        _animationSource = image;
        _animationIndex = 0;
        _animationTimer!.Start();

        return rect;
    }

    private static Rect2 FrameRegion(TutorialImage image, int index)
    {
        var column = index % image.Columns;
        var row = index / image.Columns;
        return new Rect2(
            column * image.FrameWidth, row * image.FrameHeight, image.FrameWidth, image.FrameHeight);
    }

    // The Timer callback: steps the shared AtlasTexture to the next cell in the sheet and wraps,
    // which is what makes the gif "play automatically and loop" without any scene-level Play()
    // call -- it starts the moment RenderImages puts an animated page on screen and never stops
    // until a Prev/Next swaps it out.
    private void AdvanceAnimationFrame()
    {
        if (_animationSource is not { } image || _animationFrame is null)
        {
            return;
        }

        _animationIndex = (_animationIndex + 1) % image.FrameCount;
        _animationFrame.Region = FrameRegion(image, _animationIndex);
    }

    // Renders a page's blocks by pushing/popping formatting per run, the same technique
    // InlineResourceIcons.AddKeywordedText uses for card text -- BbcodeEnabled stays false (see
    // that class's header on why this project never lets a RichTextLabel parse markup), so
    // "bold this word" has to be driven through the formatting stack directly instead of wrapped
    // in a tag.
    private void RenderBody(TutorialPage page)
    {
        _body!.Clear();

        for (var i = 0; i < page.Blocks.Count; i++)
        {
            if (i > 0)
            {
                // Blank line between blocks so paragraphs and bullet groups read as separate
                // thoughts, matching the blank-line-separated paragraphs in references/info.md.
                _body.AddText("\n\n");
            }

            var block = page.Blocks[i];
            if (!block.IsBulletList)
            {
                AddLine(block.Lines[0], page.IconizeTypes);
                continue;
            }

            for (var j = 0; j < block.Lines.Count; j++)
            {
                if (j > 0)
                {
                    _body.AddText("\n");
                }

                // A drawn bullet char rather than PushList: PushList's own indent/marker sizing is
                // tuned for BBCode-driven lists, and this label never enables BBCode (see above) --
                // a literal "•  " keeps the same manual-formatting approach as every run around it.
                _body.AddText("•  ");
                AddLine(block.Lines[j], page.IconizeTypes);
            }
        }
    }

    private void AddLine(TutorialLine line, bool iconizeTypes)
    {
        foreach (var run in line.Runs)
        {
            AddRun(run, iconizeTypes);
        }
    }

    private void AddRun(TutorialRun run, bool iconizeTypes)
    {
        var font = run.Style switch
        {
            RunStyle.Bold => _boldFont,
            RunStyle.Italic => _italicFont,
            _ => _lightFont,
        };

        _body!.PushFont(font);
        if (run.Type is { } type)
        {
            _body.PushColor(ResourceIconFactory.ColorOf(type));
        }
        else if (run.Style == RunStyle.Bold)
        {
            _body.PushColor(BoldColor);
        }

        _body.AddText(run.Text);

        if (run.Type is not null || run.Style == RunStyle.Bold)
        {
            _body.Pop();
        }

        _body.Pop();

        // The Economy and Type Effectiveness pages additionally draw the resource's shape right
        // after its own word (page.IconizeTypes -- see TutorialPage's header on why this is a
        // per-page choice, not per-run): "**wheels** (circle icon)" as the request put it, rather
        // than replacing the word with the icon the way InlineResourceIcons does for terse move
        // text, since these pages are read as prose and the word must stay legible on its own.
        if (iconizeTypes && run.Type is { } resourceType)
        {
            var size = InlineResourceIcons.SizeForFont(BodyFontSize);
            _body!.AddText(" ");
            _body.AddImage(
                InlineResourceIcons.TextureFor(resourceType, size), size, size,
                new Color(1f, 1f, 1f), InlineAlignment.CenterTo | InlineAlignment.ToCenter);
        }
    }
}
