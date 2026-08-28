using System.Collections.Generic;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Scripts;

// One inline run of text within a tutorial paragraph or bullet, carrying its own emphasis rather
// than a markup string -- this project never lets a RichTextLabel parse BBCode (see
// InlineResourceIcons' header on why: card/rules text would need escaping, and a stray "[" in
// authored text would be read as a tag). TutorialOverlay Push/Pops styling per run the same way
// InlineResourceIcons.AddKeywordedText already does for card text, so this is that same technique
// applied to hand-authored prose instead of generated effect text.
//
// A resource-type mention (Type != null) is its own run kind rather than Bold-with-a-color: on
// most pages it renders as bold+tinted text (matching info.md's own emphasis), but the
// Economy and Type Effectiveness pages additionally want the drawn shape beside the word, and
// only TutorialOverlay's render loop knows which behavior the current page wants -- the run
// itself just carries "this text names this resource," not how that gets drawn.
public enum RunStyle
{
    Plain,
    Bold,
    Italic,
}

public sealed record TutorialRun(string Text, RunStyle Style = RunStyle.Plain, ResourceType? Type = null)
{
    public static TutorialRun Plain(string text) => new(text);
    public static TutorialRun Bold(string text) => new(text, RunStyle.Bold);
    public static TutorialRun Italic(string text) => new(text, RunStyle.Italic);

    // A resource-type word (wheel/anvil/spike, singular or plural). Bold+tinted everywhere; the
    // owning page additionally decides whether an icon rides along (see TutorialPage.IconizeTypes).
    public static TutorialRun Resource(string text, ResourceType type) => new(text, RunStyle.Bold, type);
}

// One paragraph or bullet item: a run of styled text fragments that concatenate into one line of
// prose. Kept as a list of runs (not a single string) for the same reason TutorialRun exists --
// so "bold this word" is data TutorialOverlay reads, not markup it has to parse.
public sealed record TutorialLine(IReadOnlyList<TutorialRun> Runs)
{
    public static TutorialLine Of(params TutorialRun[] runs) => new(runs);
}

// One block of a tutorial page: either a paragraph (rendered as running prose) or a bullet list
// (each TutorialLine its own bulleted item). Separate from TutorialLine because a bullet list
// needs a "•" and a hanging indent per item that a plain paragraph must not get -- the two read
// as different shapes on the page, matching how info.md itself switches from prose
// paragraphs to "- " list items on the Merging and Type Effectiveness sections.
public sealed record TutorialBlock(IReadOnlyList<TutorialLine> Lines, bool IsBulletList = false)
{
    public static TutorialBlock Paragraph(params TutorialRun[] runs) => new([TutorialLine.Of(runs)]);

    public static TutorialBlock Bullets(params TutorialLine[] items) => new(items, IsBulletList: true);
}

// One image or looping gif shown above a page's body text. Two shapes:
//
//   * A still (FrameCount == 1) is one PNG shown as-is -- TutorialOverlay fits it into the page's
//     shared image box with KEEP_ASPECT rather than cropping or stretching it, since the source
//     screenshots span very different aspect ratios (a 4:1 UI strip vs. a portrait card) and
//     forcing one ratio on all of them would crop content out of some and pad others to nothing.
//   * An animated gif was converted to a grid SPRITE SHEET at import time (Godot has no native
//     .gif importer), so FrameCount/Columns/Rows describe how to slice FramePath into frames and
//     TutorialOverlay drives the animation itself on a Timer -- see that class for why a Timer
//     rather than AnimatedTexture (Godot 4's AnimatedTexture caps at 256 frames set up in the
//     editor, not authorable from a content file like this one).
//
// FrameWidth/FrameHeight are the SOURCE pixel size of one frame (needed to slice the sheet), not
// a display size -- TutorialOverlay scales the whole thing into the shared image box afterward,
// same as a still.
//
// DisplayScale multiplies whatever size TutorialOverlay's normal "fit inside the shared box"
// math would otherwise pick -- an escape hatch for the handful of source images whose own pixel
// size badly mismatches the box's shape. A 4:1 banner (Objective) fit into a square-ish box comes
// out tiny because its WIDTH hits the box edge long before its height does; a small 134x137 icon
// diagram (Scoring) fit into a 260-tall box upscales ~1.9x and visibly softens. 1.0 (the default)
// means "just fit the box, no per-image opinion" -- only the pages that looked visibly wrong at
// that default carry a non-1.0 value, tuned by eye against the rendered panel, not computed from
// the source pixel dimensions -- see each page's own value below for the reasoning.
public sealed record TutorialImage(
    string FramePath, int FrameCount = 1, int Columns = 1, int Rows = 1,
    int FrameWidth = 0, int FrameHeight = 0, float DisplayScale = 1.0f)
{
    public static TutorialImage Still(string path, float displayScale = 1.0f) =>
        new(path, DisplayScale: displayScale);

    public static TutorialImage Animated(
        string sheetPath, int frameCount, int columns, int rows, int frameWidth, int frameHeight,
        float displayScale = 1.0f) =>
        new(sheetPath, frameCount, columns, rows, frameWidth, frameHeight, displayScale);

    public bool IsAnimated => FrameCount > 1;
}

// One page of the Rules/Tutorial overlay: a heading, its content blocks, and the images/gifs (if
// any) shown above the text -- most pages carry exactly one, Cards carries two side by side (a
// creature and a spell card), and a page with none simply renders text only.
//
// IconizeTypes is per-page rather than per-run: the originating request calls out the Economy
// and Type Effectiveness pages specifically as the ones dense enough with resource mentions that
// a reader benefits from the shape riding along next to the word ("**wheels** (circle icon)"),
// while every other page's occasional mention reads better as plain bold+color text without a
// shape interrupting the line. A per-run override would let two pages drift on the same word
// ("wheel" iconized here, not there) for no reason a reader could see.
public sealed record TutorialPage(
    string Title, IReadOnlyList<TutorialBlock> Blocks, bool IconizeTypes = false,
    IReadOnlyList<TutorialImage>? Images = null);

// The Rules/Tutorial overlay's page data, transcribed from info.md -- including which
// words that file bolds/italicizes, so the in-game Rules page carries the same emphasis as the
// source document rather than flattening everything to one weight.
//
// Broken along that file's own section headers rather than by a fixed character count: each page
// is one self-contained rules topic (a player finishing "Merging" mid-explanation and flipping to
// the next page should land on a new topic, not the second half of the same thought). Type
// effectiveness gets its own page separate from the rest of "Gameplay" because it is the section
// the in-game TypeCycleChart corner widget already visualizes -- a natural place to add that
// image once one exists -- and because it is dense enough on its own to crowd a shared page.
public static class TutorialContent
{
    private static TutorialRun Wheel(string text = "wheel") => TutorialRun.Resource(text, ResourceType.Wheel);
    private static TutorialRun Anvil(string text = "anvil") => TutorialRun.Resource(text, ResourceType.Anvil);
    private static TutorialRun Spike(string text = "spike") => TutorialRun.Resource(text, ResourceType.Spike);
    private static TutorialRun P(string text) => TutorialRun.Plain(text);
    private static TutorialRun B(string text) => TutorialRun.Bold(text);
    private static TutorialRun I(string text) => TutorialRun.Italic(text);

    public static readonly TutorialPage[] Pages =
    [
        new TutorialPage(
            "Objective",
            [
                TutorialBlock.Paragraph(
                    P("Shapes is a two-player, turn-based card game. In Shapes, you battle with "
                        + "cards in a fictional universe full of spherical "),
                    Wheel(), P(" creatures, blunt "), Anvil(), P(" creatures, and sharp "), Spike(),
                    P(" creatures. You win by maintaining board control which whittles your "
                        + "opponent's health to zero.")),
            ],
            // Wide 4:1 banner -- fit-to-box alone leaves it tiny (width hits the box edge long
            // before height does), so it needs an explicit boost. Per request: scaled up 2-3x.
            Images: [TutorialImage.Still("res://Art/rules/objective.png", displayScale: 2.5f)]),
        new TutorialPage(
            "Economy",
            [
                TutorialBlock.Paragraph(
                    P("There are three different resource types in the game: "), Wheel("wheels"),
                    P(", "), Anvil("anvils"), P(", and "), Spike("spikes"),
                    P(". These resource types work like independent sources of mana or energy. "
                        + "Every turn, you gain "), B("2"), P(" of each resource type. Resources are "),
                    B("saved between turns"), P(", allowing you to accumulate large quantities of "
                        + "them.")),
                TutorialBlock.Paragraph(
                    P("You spend resources on playing cards and activating moves that creatures "
                        + "have.")),
            ],
            IconizeTypes: true,
            Images: [TutorialImage.Still("res://Art/rules/economy.png")]),
        new TutorialPage(
            "Cards",
            [
                TutorialBlock.Paragraph(
                    P("Every turn you draw one card. Cards come in two kinds: "), B("creatures"),
                    P(" and "), B("spells"), P(".")),
                TutorialBlock.Paragraph(
                    P("Creatures have a "), B("cost"), P(", "), B("HP"), P(", and two "), B("moves"),
                    P(". You pay the upfront cost once to place the creature on the board. If a "
                        + "creature's health reaches 0, it gets destroyed and removed from the "
                        + "board.")),
                TutorialBlock.Paragraph(
                    P("Each move has a cost and effect. Every turn, you can activate any of a "
                        + "creature's moves by paying the move cost. Each move can be used at most "
                        + "once per turn. Moves can be used on any turn, including the turn you play "
                        + "a creature.")),
                TutorialBlock.Paragraph(
                    P("Spells have a "), B("cost"), P(" and "), B("effect"),
                    P(". You pay the upfront cost and the effect immediately triggers, consuming "
                        + "the card.")),
            ],
            Images:
            [
                TutorialImage.Still("res://Art/rules/cards-creature.png"),
                TutorialImage.Still("res://Art/rules/cards-spell.png"),
            ]),
        new TutorialPage(
            "The Board",
            [
                TutorialBlock.Paragraph(
                    P("The board consists of "), B("3 slots"),
                    P(" per side. You can place a creature into any empty slot on your side. Once "
                        + "a creature is placed down onto a slot, it cannot relocate to a different "
                        + "slot. Creatures typically cannot interact with each other unless they are "),
                    B("opposing"),
                    P(" each other. Creature moves that interact with an enemy, such as dealing "
                        + "damage, only target opposing creatures.")),
            ],
            Images:
            [
                TutorialImage.Animated(
                    "res://Art/rules/board-gif.png", frameCount: 74, columns: 9, rows: 9,
                    frameWidth: 510, frameHeight: 435),
            ]),
        new TutorialPage(
            "Type Effectiveness",
            [
                TutorialBlock.Paragraph(
                    P("Creatures and spells have a type that matches their cost: "), Wheel(), P(", "),
                    Anvil(), P(", or "), Spike(), P(". Types can either be "), B("super effective"),
                    P(" or "), B("neutral"), P(" against each other.")),
                TutorialBlock.Bullets(
                    TutorialLine.Of(Spike("Spikes"), P(" are super effective against "), Wheel("wheels"),
                        P(" (spikes pop wheels).")),
                    TutorialLine.Of(Wheel("Wheels"), P(" are super effective against "), Anvil("anvils"),
                        P(" (wheels roll over anvils).")),
                    TutorialLine.Of(Anvil("Anvils"), P(" are super effective against "), Spike("spikes"),
                        P(" (anvils blunt spikes)."))),
                TutorialBlock.Paragraph(
                    P("When a spell or move is super effective against a creature, it deals double "
                        + "damage. So a super effective "), Spike(), P(" move deals double damage "
                        + "against a "), Wheel(), P(" creature, but a neutral "), Wheel(),
                    P(" move does regular damage against a "), Spike(), P(" creature.")),
                TutorialBlock.Paragraph(
                    P("Creatures can sometimes be dual-type. Moves and spells can only ever have "
                        + "one type.")),
                TutorialBlock.Bullets(
                    TutorialLine.Of(Spike("Spikes"), P(" are also super effective against "),
                        Wheel(), P("/"), Spike(), P(".")),
                    TutorialLine.Of(Wheel("Wheels"), P(" are also super effective against "),
                        Anvil(), P("/"), Wheel(), P(".")),
                    TutorialLine.Of(Anvil("Anvils"), P(" are also super effective against "),
                        Spike(), P("/"), Anvil(), P("."))),
                TutorialBlock.Paragraph(
                    P("Mastering types is critical to winning the game. Oppose enemy creatures "
                        + "with super effective types and prioritize super effective moves "
                        + "whenever possible.")),
            ],
            IconizeTypes: true,
            // Source is only 185x183 -- fit-to-box alone upscales it ~1.4x and visibly softens
            // the diagram's thin arrows/text. Scaled back down so it displays close to its own
            // native resolution instead of the box's full height.
            Images: [TutorialImage.Still("res://Art/rules/type-effectiveness.png", displayScale: 0.7f)]),
        new TutorialPage(
            "Scoring",
            [
                TutorialBlock.Paragraph(
                    P("Each player has a hero that starts with 7 health. At the start of your "
                        + "turn, you deal 1 damage to your opponent's hero for each "), B("unopposed"),
                    P(" creature you have. So, if you start your turn with a full board against an "
                        + "opponent's empty board, you deal 3 damage to their hero. To win, you must "
                        + "bring your opponent's hero down to zero health.")),
            ],
            // Source is only 134x137 -- fit-to-box alone upscales it ~1.9x, the blurriest of any
            // page's image. Scaled back down for the same reason as Type Effectiveness above.
            Images: [TutorialImage.Still("res://Art/rules/scoring.png", displayScale: 0.6f)]),
        new TutorialPage(
            "Merging",
            [
                TutorialBlock.Paragraph(
                    P("On your turn, you can "), B("merge"),
                    P(" two adjacent friendly creatures. Merging is a free action that combines "
                        + "two creatures' resource types, health, moves, and statuses. To trigger a "
                        + "merge, drag one creature onto an adjacent creature. A merged creature "
                        + "cannot merge again.")),
                TutorialBlock.Paragraph(P("Merging is often a good idea:")),
                TutorialBlock.Bullets(
                    TutorialLine.Of(P("It can make a creature harder to kill")),
                    TutorialLine.Of(P("It can uncover super effective moves")),
                    TutorialLine.Of(P("Creatures' moves can have synergy with each other")),
                    TutorialLine.Of(P("It is the only way a creature can relocate from the slot "
                        + "they were first placed onto"))),
                TutorialBlock.Paragraph(P("Merging is sometimes a bad idea:")),
                TutorialBlock.Bullets(
                    TutorialLine.Of(P("Combining two creatures onto the same slot leaves one more "
                        + "slot empty, which, if opposed by an enemy, deals one more damage to your "
                        + "hero on your opponent's turn.")),
                    TutorialLine.Of(P("While two unopposed creatures "), I("each"),
                        P(" deal 1 damage to an opponent's hero per turn, combining them only does "
                            + "1 "), I("total"), P(" damage per turn.")),
                    TutorialLine.Of(P("It can uncover super effective moves against your "
                        + "creature"))),
            ],
            Images:
            [
                TutorialImage.Animated(
                    "res://Art/rules/merging-gif.png", frameCount: 45, columns: 9, rows: 5,
                    frameWidth: 510, frameHeight: 435),
            ]),
        new TutorialPage(
            "Fatigue",
            [
                TutorialBlock.Paragraph(
                    P("Each turn you draw one card. If your deck runs out of cards, your hero "
                        + "takes one damage instead. This mechanic ends stalemates and guarantees a "
                        + "winner, and typically doesn't happen in a regular game. Scoring happens "
                        + "before fatigue triggers.")),
            ],
            Images: [TutorialImage.Still("res://Art/rules/fatigue.png")]),
    ];
}
