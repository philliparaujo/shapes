using Godot;

namespace Shapes.Godot.Scripts;

// The one place the client asks "what am I running on" (DESIGN.md D7): is this a touch device, how
// much bigger must content be drawn, and what is the safe-area inset.
//
// WHY A GATE AT ALL. Milestone D was tuned against the desktop window, so D7's hard requirement is
// that desktop renders identically after the mobile pass. Every consumer routes through here and
// takes an early return (or multiplies by one) when IsTouch is false, which makes "desktop is
// unchanged" a property of one branch rather than of arithmetic that happens to come out even.
//
// TOUCH, NOT "ANDROID". The question every consumer actually asks is "are fingers the input", and
// OS.HasFeature covers the mobile exports without naming one. The editor's mobile preview does not
// set it, which is deliberate and matches D7's verification note: these bugs survive a clean
// compile, so the check must be true only where the real device is.
public static class Platform
{
    // Cached: the export target does not change, and the app is landscape-locked (project.godot),
    // so the safe area does not rotate. D7c explicitly rules out a Resized hook that would need to
    // invalidate this.
    private static bool? _isTouch;

    public static bool IsTouch => _isTouch ??= OS.HasFeature("mobile");

    // How much larger 2D content is drawn on a phone, fed to Window.ContentScaleFactor.
    //
    // THE ONE LEVER, and the correction to this step's first cut. That version raised control
    // MINIMUM SIZES to a 48dp floor and left every font size, card metric and rail constant alone,
    // which is what produced the reported symptom: buttons grew tall while their text stayed put,
    // so the padding read as dead space, and the board and rail did not grow at all. Content scale
    // multiplies the root viewport's stretch transform, so a single number moves controls, fonts,
    // card art and everything drawn in _Draw together, in proportion -- which is what "make it
    // bigger on a phone" actually means. Chasing ~15 files of font-size literals instead would be
    // the exact drift CardMetrics, MoveRowFactory and CardStyle each exist to prevent.
    //
    // WHY 1.12, AND WHY THAT IS A CEILING RATHER THAN A TASTE CALL. Content scale trades canvas
    // units for size: at factor k the canvas becomes 1000/k units tall, and the board is very
    // nearly fully subscribed already -- the 96-unit top margin, two 297-unit rows with their
    // margins and separation, and the 210-unit hand band come to ~992 of the 1000 available. That
    // is 8 units of slack, which is why the first cut of this step could not make anything bigger.
    //
    // The headroom is bought by reclaiming the two margins on touch: BoardView.ApplyTouchLayout
    // drops the top margin to TouchTopMargin and the hand band to TouchHandBand below, which frees
    // ~107 units. Two 297-unit rows plus lean margins need ~885, so the arithmetic ceiling is
    // 1000/885 ~= 1.13; 1.12 keeps a little slack for rounding. Past that the rows stop fitting
    // between the top of the screen and the hand, and the fix stops being a fit-and-finish pass and
    // starts being D7's explicitly out-of-scope second layout.
    //
    // So this is genuinely modest, and deliberately so -- the board is roughly 12% larger, not 50%.
    // Making the CARDS themselves substantially bigger is not reachable inside one landscape screen
    // that also shows six slots and a hand; that would need the second information architecture
    // D7 rules out. See the note at the end of this step for what remains.
    //
    // Deliberately a CONSTANT, not derived from DPI. Deriving it looked principled in the first cut
    // and was wrong twice over: it made the layout unpredictable across devices (a 560-dpi phone
    // and a 400-dpi phone would get different canvases, so only one could be verified on hardware),
    // and the vertical budget above is what really bounds the factor -- and that budget is the same
    // 1000 units on every device, because stretch/aspect="expand" always makes height the limiting
    // axis. A number the board is known to fit beats a number that varies per handset.
    public const float TouchContentScale = 1.12f;

    // The board's touch margins, in canvas units, replacing BoardView.tscn's desktop 96 and 210.
    // They live here beside the factor they pay for, since changing either without the other is
    // what makes the two rows stop fitting.
    public const int TouchTopMargin = 24;
    public const int TouchHandBand = 175;

    public static float ContentScale => IsTouch ? TouchContentScale : 1f;

    // Applies the content scale to the root window. Idempotent and safe to call from any screen's
    // _Ready, which is how it is wired: every screen root calls it, so whichever one the app opens
    // on gets the scale and a ChangeSceneToFile cannot land on an unscaled screen.
    //
    // A NO-OP ON DESKTOP by the same early return every other member here uses, so the desktop
    // window keeps a content scale factor of exactly 1 and renders identically.
    public static void ApplyContentScale(Node node)
    {
        if (!IsTouch || node is null)
        {
            return;
        }

        var window = node.GetWindow();
        if (window is not null)
        {
            window.ContentScaleFactor = TouchContentScale;
        }
    }

    // The canvas-unit size of the drawable area -- what a Control's anchors resolve against. Note
    // this already reflects ContentScale once it is applied, since scaling shrinks the canvas.
    public static Vector2 GetCanvasSize()
    {
        var viewport = (Engine.GetMainLoop() as SceneTree)?.Root;
        return viewport is null ? new Vector2(1600f, 1000f) : viewport.GetVisibleRect().Size;
    }

    // Physical pixels per canvas unit, under stretch/mode="canvas_items" plus content scale.
    //
    // THIS IS THE CONVERSION TRAP D7c NAMES. GetDisplaySafeArea reports PHYSICAL pixels, while every
    // offset in the .tscn files is in canvas units. Mixing them over-insets by this factor -- on a
    // 2340x1080 phone that is ~2x once content scale is in play, the kind of bug that compiles and
    // runs clean while being visibly wrong.
    //
    // Derived from the ratio the window actually reports rather than recomputed from the project's
    // base resolution, so it stays right if the design canvas or the content scale ever moves.
    public static float PixelsPerCanvasUnit
    {
        get
        {
            var window = (Vector2)DisplayServer.WindowGetSize();
            var canvas = GetCanvasSize();
            return canvas.X <= 0f ? 1f : window.X / canvas.X;
        }
    }

    // Inset from each screen edge that is safe to draw controls in, in CANVAS UNITS (already
    // divided by PixelsPerCanvasUnit -- see above). Order is left, top, right, bottom.
    //
    // Zero on desktop by an early return, not by arithmetic. On Android the app draws under the
    // system bars and any display cutout (the export preset sets screen/immersive_mode=true), so
    // this is what keeps a corner control clear of the rounded screen corner, the camera cutout and
    // the gesture bar.
    public static Vector4 SafeAreaInset()
    {
        if (!IsTouch)
        {
            return Vector4.Zero;
        }

        var safe = DisplayServer.GetDisplaySafeArea();
        var screen = DisplayServer.ScreenGetSize();

        // FALLING BACK TO A FLOOR RATHER THAN TO ZERO, and this is the fix for "corners still cut
        // off". GetDisplaySafeArea is unreliable under immersive mode: it frequently reports the
        // FULL screen rect (the app asked to draw edge to edge, so nothing is reserved), which
        // yields a zero inset and leaves the corner controls exactly where they were -- which is
        // precisely the reported symptom. A rounded screen corner and a camera cutout still eat
        // that space whatever the API says, so the inset never goes below a fixed minimum.
        var left = Mathf.Max(0, safe.Position.X);
        var top = Mathf.Max(0, safe.Position.Y);
        var right = Mathf.Max(0, screen.X - (safe.Position.X + safe.Size.X));
        var bottom = Mathf.Max(0, screen.Y - (safe.Position.Y + safe.Size.Y));

        var scale = PixelsPerCanvasUnit;
        if (scale <= 0f)
        {
            scale = 1f;
        }

        var inset = new Vector4(left, top, right, bottom) / scale;

        return new Vector4(
            Mathf.Max(inset.X, MinEdgeInset),
            Mathf.Max(inset.Y, MinEdgeInset),
            Mathf.Max(inset.Z, MinEdgeInset),
            Mathf.Max(inset.W, MinEdgeInset));
    }

    // The floor above, in canvas units. Sized to clear a modern phone's rounded corner: the buttons
    // sit 14 units from the edge in the .tscn, which is comfortable on the desktop canvas and not
    // on a physical corner radius, so this roughly triples that clearance.
    private const float MinEdgeInset = 30f;
}
