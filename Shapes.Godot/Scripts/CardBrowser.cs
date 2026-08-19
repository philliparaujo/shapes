using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The collection view (PLAN.md C4): every card, always shown in full detail, filterable, in a
// paginated grid. A separate scene (CardBrowser.tscn) rather than a lobby tab, per explicit
// direction, reachable the same one-line ChangeSceneToFile way Lobby reaches GameRoot.
//
// Deliberately reuses the live-game view scripts rather than building a parallel renderer --
// HoverDetailPanel/SlotView already render from static CardText/CardDefinition data (A4's
// EffectText synthesis), so a card looks IDENTICAL here and on the real board. A "Merged"
// creature type is likewise a REAL merge (CreatureInstance.AbsorbMerge, the same method
// ActionExecutor.ApplyMerge calls), not a cosmetic double-render -- the merged health/types/move
// list shown here is exactly what a real board merge would produce.
//
// PAGINATION (added after the first cut was slow): every filtered cell is a real SlotView or
// HoverDetailPanel scene instantiation -- Panel, StyleBoxFlat, several Labels, move buttons, art
// panes -- not cheap data, and the unfiltered Merged case is 27x27 = 729 of them. Nothing here
// is "recomputed" in the sense of repeated engine work (AbsorbMerge itself is a handful of field
// adds); the cost is Godot node/scene construction, so the fix is capping how many cells exist
// at once, not caching a pure-data result. RebuildGrid now computes the full filtered ID list
// cheaply (no nodes), slices out one page, and only instantiates cells for that slice.
public partial class CardBrowser : Control
{
    [Export] public NodePath GridContainerPath { get; set; } =
        "Layout/ScrollContainer/GridCenter/GridPanel/CardGrid";
    [Export] public NodePath GridPanelPath { get; set; } =
        "Layout/ScrollContainer/GridCenter/GridPanel";
    [Export] public NodePath ScrollContainerPath { get; set; } = "Layout/ScrollContainer";
    [Export] public NodePath FilterPanelPath { get; set; } = "Layout/FilterPanel";
    [Export] public NodePath BackButtonPath { get; set; } = "Layout/TopBar/BackButton";
    [Export] public NodePath DeckbuilderButtonPath { get; set; } =
        "Layout/TopBar/TitleGroup/DeckbuilderButton";
    [Export] public NodePath SearchBarPath { get; set; } = "Layout/TopBar/SearchBar";
    [Export] public NodePath CostTypeFilterPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/FilterBar/CostTypeFilter";
    [Export] public NodePath CostAmountFilterPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/FilterBar/CostAmountFilter";
    [Export] public NodePath KindLabelPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/FilterBar/KindLabel";
    [Export] public NodePath KindFilterPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/FilterBar/KindFilter";
    [Export] public NodePath CreatureTypeFilterPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/FilterBar/CreatureTypeFilter";
    [Export] public NodePath ViewModeLabelPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/FilterBar/ViewModeLabel";
    [Export] public NodePath ViewModeFilterPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/FilterBar/ViewModeFilter";
    [Export] public NodePath FirstCreatureFilterPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/MergeBar/FirstCreatureFilter";
    [Export] public NodePath SecondCreatureFilterPath { get; set; } =
        "Layout/FilterPanel/FilterColumn/MergeBar/SecondCreatureFilter";
    [Export] public NodePath MergeBarPath { get; set; } = "Layout/FilterPanel/FilterColumn/MergeBar";
    [Export] public NodePath PageBarPath { get; set; } = "Layout/PageBar";
    [Export] public NodePath PrevPageButtonPath { get; set; } = "Layout/PageBar/PrevPageButton";
    [Export] public NodePath NextPageButtonPath { get; set; } = "Layout/PageBar/NextPageButton";
    [Export] public NodePath PageLabelPath { get; set; } = "Layout/PageBar/PageLabel";
    [Export] public string LobbyScenePath { get; set; } = "res://Scenes/Lobby.tscn";
    [Export] public string DeckbuilderScenePath { get; set; } = "res://Scenes/Deckbuilder.tscn";

    [Export] public PackedScene? SlotViewScene { get; set; }
    [Export] public PackedScene? HoverDetailPanelScene { get; set; }

    // Rows per page is fixed; columns are NOT -- see ColumnsFor. The page fills a COLUMNS x ROWS
    // rectangle either way, so PageSize (used for the "N of M" count and the next/prev math) is
    // derived from whatever ColumnsFor returns rather than a constant.
    private const int Rows = 5;

    // Never fewer than this many columns even on a narrow viewport -- a 1-column grid at a huge
    // cell height would make paging nearly pointless, and this is a floor rather than a target
    // ColumnsFor's own division already reaches naturally at any reasonable window size.
    private const int MinColumns = 3;

    // Grid cell gap, and the grid panel's own left+right content margin -- both read by ColumnsFor
    // to convert the scroll area's raw width into how much of it cells can actually occupy. Named
    // here rather than left as literals in the .tscn/_Ready styling so the two stay the one number
    // ColumnsFor's math depends on.
    private const int GridSeparation = 16;
    private const int GridPanelPadding = 32;

    // Reserved so ColumnsFor never proposes a column count that fits with exactly zero pixels to
    // spare -- see its own note.
    private const int MarginBuffer = 40;

    private enum KindFilterOption
    {
        All,
        Creature,
        Spell,
    }

    private enum CreatureTypeOption
    {
        Original,
        Merged,
    }

    private enum ViewMode
    {
        Tooltip,
        InPlay,
    }

    // One thing to render: either a single card (tooltip or in-play) or a merge pairing
    // (First absorbs Second, or the reverse -- see BuildMergedCell). Computing this list is the
    // "cheap" pass RebuildGrid always does in full; only the current page's slice becomes nodes.
    private readonly record struct Entry(CardDefinition? Card, CardDefinition? First, CardDefinition? Second);

    // Index-aligned with the OptionButton items PopulateFilters adds.
    private static readonly ResourceType?[] CostTypeOrder =
        [null, ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel];

    private static readonly int?[] CostAmountOrder = [null, 1, 2, 3, 4, 5];

    private static readonly KindFilterOption[] KindOrder =
        [KindFilterOption.All, KindFilterOption.Creature, KindFilterOption.Spell];

    private static readonly CreatureTypeOption[] CreatureTypeOrder =
        [CreatureTypeOption.Original, CreatureTypeOption.Merged];

    private static readonly ViewMode[] ViewModeOrder = [ViewMode.Tooltip, ViewMode.InPlay];

    private GridContainer? _grid;
    private Button? _backButton;
    private Button? _deckbuilderButton;
    private LineEdit? _searchBar;
    private OptionButton? _costTypeFilter;
    private OptionButton? _costAmountFilter;
    private Control? _kindLabel;
    private OptionButton? _kindFilter;
    private OptionButton? _creatureTypeFilter;
    private Control? _viewModeLabel;
    private OptionButton? _viewModeFilter;
    private OptionButton? _firstCreatureFilter;
    private OptionButton? _secondCreatureFilter;
    private Control? _mergeBar;
    private Control? _pageBar;
    private Button? _prevPageButton;
    private Button? _nextPageButton;
    private Label? _pageLabel;
    private ScrollContainer? _scrollContainer;

    private CardDatabase? _cards;
    // Creature cards only, pre-sorted cost/name/type (PLAN.md C4's sort rule) -- both the
    // Original grid and the First/Second merge pickers iterate this same order, so a pairing's
    // "first" and "second" dropdowns list creatures in the same order the plain grid does.
    private List<CardDefinition> _creatureCards = [];

    private int _page;

    public override void _Ready()
    {
        // PLAN.md D3 phase 1 -- the project theme, inherited by everything below this root.
        UiTheme.ApplyTo(this);

        GodotTextFormat.Ensure();
        _grid = GetNode<GridContainer>(GridContainerPath);
        _backButton = GetNode<Button>(BackButtonPath);
        _deckbuilderButton = GetNode<Button>(DeckbuilderButtonPath);
        _searchBar = GetNode<LineEdit>(SearchBarPath);
        _costTypeFilter = GetNode<OptionButton>(CostTypeFilterPath);
        _costAmountFilter = GetNode<OptionButton>(CostAmountFilterPath);
        _kindLabel = GetNode<Control>(KindLabelPath);
        _kindFilter = GetNode<OptionButton>(KindFilterPath);
        _creatureTypeFilter = GetNode<OptionButton>(CreatureTypeFilterPath);
        _viewModeLabel = GetNode<Control>(ViewModeLabelPath);
        _viewModeFilter = GetNode<OptionButton>(ViewModeFilterPath);
        _firstCreatureFilter = GetNode<OptionButton>(FirstCreatureFilterPath);
        _secondCreatureFilter = GetNode<OptionButton>(SecondCreatureFilterPath);
        _mergeBar = GetNode<Control>(MergeBarPath);
        _pageBar = GetNode<Control>(PageBarPath);
        _prevPageButton = GetNode<Button>(PrevPageButtonPath);
        _nextPageButton = GetNode<Button>(NextPageButtonPath);
        _pageLabel = GetNode<Label>(PageLabelPath);
        _scrollContainer = GetNode<ScrollContainer>(ScrollContainerPath);

        // Raised rather than the theme's default panel fill (Palette.Surface sits almost flush
        // with TableBackdrop's mid-tone -- see Palette's own values -- so an un-overridden panel
        // would barely separate from the backdrop behind it). SurfaceRaised is the same "lifted"
        // fill the board's own panels use, which is what makes the filter controls and the card
        // grid read as consoles sitting on the room rather than floating directly on it.
        var raisedPanel = new StyleBoxFlat { BgColor = Palette.SurfaceRaised, BorderColor = Palette.SurfaceEdge };
        raisedPanel.SetBorderWidthAll(Palette.BorderWidth);
        raisedPanel.SetCornerRadiusAll(Palette.CornerRadius);
        raisedPanel.ContentMarginLeft = GridPanelPadding / 2;
        raisedPanel.ContentMarginRight = GridPanelPadding / 2;
        raisedPanel.ContentMarginTop = 12;
        raisedPanel.ContentMarginBottom = 12;

        GetNode<PanelContainer>(FilterPanelPath).AddThemeStyleboxOverride("panel", raisedPanel);
        GetNode<PanelContainer>(GridPanelPath).AddThemeStyleboxOverride("panel", raisedPanel);

        _grid.AddThemeConstantOverride("h_separation", GridSeparation);
        _grid.AddThemeConstantOverride("v_separation", GridSeparation);

        // Columns depend on the scroll area's width (ColumnsFor), so a window resize -- not just
        // a filter change -- can change how many fit. Without this, resizing the window keeps
        // whatever column count was chosen at the last filter change, which is exactly the "wrong
        // margin on a different aspect ratio" this whole recompute exists to avoid.
        _scrollContainer.Resized += RebuildGrid;

        SlotViewScene ??= GD.Load<PackedScene>("res://Scenes/SlotView.tscn");
        HoverDetailPanelScene ??= GD.Load<PackedScene>("res://Scenes/HoverDetailPanel.tscn");

        _backButton.Pressed += () => GetTree().ChangeSceneToFile(LobbyScenePath);

        // Straight across to the deckbuilder (PLAN.md D3 phase 3). The two screens render cards
        // with the same components and are used for the same question -- "what is in this set" --
        // so making a player route back through the menu to switch between them was friction with
        // nothing behind it.
        _deckbuilderButton.Pressed += () => GetTree().ChangeSceneToFile(DeckbuilderScenePath);

        var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
        _cards = CardLoader.FromDirectory(cardsDir);
        _creatureCards = [.. _cards.All.Where(c => c.IsCreature).OrderBy(SortKey)];

        PopulateFilters();

        _searchBar.TextChanged += _ => OnFilterChanged();
        _costTypeFilter.ItemSelected += _ => OnFilterChanged();
        _costAmountFilter.ItemSelected += _ => OnFilterChanged();
        _kindFilter.ItemSelected += _ => OnKindChanged();
        _creatureTypeFilter.ItemSelected += _ => OnCreatureTypeChanged();
        _viewModeFilter.ItemSelected += _ => OnFilterChanged();
        _firstCreatureFilter.ItemSelected += _ => OnFilterChanged();
        _secondCreatureFilter.ItemSelected += _ => OnFilterChanged();
        _prevPageButton.Pressed += () => ChangePage(-1);
        _nextPageButton.Pressed += () => ChangePage(1);

        OnCreatureTypeChanged();
    }

    private static (int Cost, string Name, ResourceType Type) SortKey(CardDefinition card)
    {
        var type = CardText.SinglePipType(card.Cost);
        var amount = type is { } t ? card.Cost[t] : 0;

        // Typeless (free) cards sort as ResourceType 0 (Spike) alongside real Spike cards --
        // there are none in the current set (every card has a single-type cost), so this never
        // has to make a real judgment call; picking a stable, defined order over throwing keeps
        // the browser working if that ever changes.
        return (amount, card.Name, type ?? ResourceType.Spike);
    }

    private void PopulateFilters()
    {
        _costTypeFilter!.Clear();
        _costTypeFilter.AddItem("All");
        _costTypeFilter.AddItem("Spike");
        _costTypeFilter.AddItem("Anvil");
        _costTypeFilter.AddItem("Wheel");

        _costAmountFilter!.Clear();
        _costAmountFilter.AddItem("All");
        foreach (var amount in CostAmountOrder.Skip(1))
        {
            _costAmountFilter.AddItem(amount!.Value.ToString());
        }

        _kindFilter!.Clear();
        _kindFilter.AddItem("All");
        _kindFilter.AddItem("Creature");
        _kindFilter.AddItem("Spell");

        _creatureTypeFilter!.Clear();
        _creatureTypeFilter.AddItem("Original");
        _creatureTypeFilter.AddItem("Merged");

        // Only meaningful for creatures (a spell never occupies a board slot, so it always
        // renders as its tooltip regardless of this filter) -- shown/hidden by OnCreatureTypeChanged
        // rather than disabled, so the filter bar only ever offers choices that do something.
        _viewModeFilter!.Clear();
        _viewModeFilter.AddItem("Tooltip");
        _viewModeFilter.AddItem("In-play");

        PopulateCreaturePicker(_firstCreatureFilter!);
        PopulateCreaturePicker(_secondCreatureFilter!);
    }

    private void PopulateCreaturePicker(OptionButton picker)
    {
        picker.Clear();
        picker.AddItem("All");
        foreach (var card in _creatureCards)
        {
            picker.AddItem(card.Name);
        }
    }

    // Merged forces Kind=Creature and Creature view=In-play -- a merged creature has no
    // meaningful "spell" reading and no un-merged tooltip of its own, so Kind (AND its label --
    // an OptionButton hiding while its "Kind" text label stays on screen just leaves a gap where
    // the control was) is hidden while Merged is selected, and restored once Original is
    // reselected. The merge picker row (First/Second) is the mirror image: hidden entirely under
    // Original, since it has nothing to filter there. Creature view's own visibility is a
    // separate question from Merged/Original -- see UpdateViewModeVisibility -- so this only
    // touches Kind/MergeBar directly and defers to that for Creature view.
    private void OnCreatureTypeChanged()
    {
        var isMerged = CreatureTypeOrder[_creatureTypeFilter!.Selected] == CreatureTypeOption.Merged;

        _kindLabel!.Visible = !isMerged;
        _kindFilter!.Visible = !isMerged;
        _mergeBar!.Visible = isMerged;

        UpdateViewModeVisibility();
        OnFilterChanged();
    }

    // Kind alone decides whether Creature view has anything to mean -- picking it back to
    // All/Spell after having it on Creature needs the same reset UpdateViewModeVisibility
    // already does for Merged, which is why this shares that helper rather than duplicating it.
    private void OnKindChanged()
    {
        UpdateViewModeVisibility();
        OnFilterChanged();
    }

    // Creature view only means anything when the grid can actually contain an un-merged
    // creature to show in-play: Creature type = Original (Merged always renders in-play itself,
    // via its own MergeBar, and has no separate tooltip reading) AND Kind = Creature (a filter
    // list mixing spells and creatures, or spells alone, has no in-play view to offer for a
    // spell). Outside that combination the control (and its label -- see OnCreatureTypeChanged's
    // note on why the label needs to hide too) is hidden AND reset to Tooltip, not just hidden --
    // a stale "In-play" selection left over from a prior Kind=Creature session must not silently
    // keep affecting spell cells once it's hidden, since BuildOriginalCell reads Selected
    // regardless of Visible.
    private void UpdateViewModeVisibility()
    {
        var isMerged = CreatureTypeOrder[_creatureTypeFilter!.Selected] == CreatureTypeOption.Merged;
        var isCreatureOnly = KindOrder[_kindFilter!.Selected] == KindFilterOption.Creature;
        var showViewMode = !isMerged && isCreatureOnly;

        _viewModeLabel!.Visible = showViewMode;
        _viewModeFilter!.Visible = showViewMode;

        if (!showViewMode)
        {
            _viewModeFilter.Selected = (int)ViewMode.Tooltip;
        }
    }

    private void OnFilterChanged()
    {
        _page = 0;
        RebuildGrid();
    }

    private void ChangePage(int delta)
    {
        _page = Math.Max(0, _page + delta);
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        if (_cards is null)
        {
            return;
        }

        foreach (var child in _grid!.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }

        var creatureType = CreatureTypeOrder[_creatureTypeFilter!.Selected];
        var entries = creatureType == CreatureTypeOption.Merged ? MergedEntries() : OriginalEntries();

        // Merged entries always render as SlotView (a merge has no un-merged tooltip reading --
        // see OnCreatureTypeChanged); Original entries render as SlotView only under
        // Creature view = In-play. Either way every cell on the page is the SAME size (a single
        // Entry list is never a mix of Card and First/Second, and viewMode applies uniformly), so
        // one check up front decides the whole page's column count.
        var viewMode = ViewModeOrder[_viewModeFilter!.Selected];
        var usesSlotCells = creatureType == CreatureTypeOption.Merged || viewMode == ViewMode.InPlay;

        _grid!.Columns = ColumnsFor(usesSlotCells ? CardMetrics.SlotWidth : CardMetrics.TooltipWidth);
        var pageSize = _grid.Columns * Rows;

        var totalPages = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
        _page = Math.Clamp(_page, 0, totalPages - 1);

        foreach (var entry in entries.Skip(_page * pageSize).Take(pageSize))
        {
            if (entry.Card is { } card)
            {
                BuildOriginalCell(card, viewMode);
            }
            else
            {
                BuildMergedCell(entry.First!, entry.Second!);
            }
        }

        _pageBar!.Visible = entries.Count > pageSize;
        _pageLabel!.Text = $"Page {_page + 1} / {totalPages} ({entries.Count} total)";
        _prevPageButton!.Disabled = _page == 0;
        _nextPageButton!.Disabled = _page >= totalPages - 1;
    }

    // How many `cellWidth`-wide columns fit the scroll area at its CURRENT size, floored to
    // MinColumns.
    //
    // A FIXED column count is what produced the crowding this replaces: 7 columns of 240px
    // tooltip cells add up to a wider row than 5 columns of 330px slot cells, so one constant
    // tuned against one cell size left the other's margins wrong -- and either constant would be
    // wrong again on a viewport of a different width entirely. Deriving columns from the actual
    // available width keeps the grid's own edge close to CardBrowser's left/right inset regardless
    // of cell size or window aspect ratio, rather than a number of columns that only happens to
    // fit at one resolution.
    //
    // Reads _scrollContainer's width rather than the grid panel's own: GridPanel/CardGrid now
    // shrink-to-content and sit inside a CenterContainer (see CardBrowser.tscn), so the panel's
    // width is a RESULT of this method, not an input to it -- asking it "how wide are you" would
    // be circular.
    private int ColumnsFor(float cellWidth)
    {
        // MarginBuffer leaves a visible gutter between the shrink-wrapped grid panel and the
        // scroll area's own edge even when a column count fits exactly -- an edge-to-edge fit
        // reads as cramped even though nothing is actually clipped or overlapping.
        var available = _scrollContainer!.Size.X - GridPanelPadding - MarginBuffer;
        var perColumn = cellWidth + GridSeparation;

        // +GridSeparation because N columns need only (N-1) internal gaps, not N -- without this
        // correction the last column that would actually fit is discarded one column early.
        var columns = (int)((available + GridSeparation) / perColumn);
        return Math.Max(MinColumns, columns);
    }

    // Cheap pass: builds the full filtered/sorted list of WHAT to show, no nodes yet -- so
    // filtering and paging can be computed instantly regardless of how many results there are,
    // and only the current page's slice pays the real SlotView/HoverDetailPanel instantiation
    // cost (see the class header on why that cost, not "recomputation," was the real slowness).
    private List<Entry> OriginalEntries()
    {
        var search = _searchBar!.Text.Trim();
        var kind = KindOrder[_kindFilter!.Selected];
        var costAmount = CostAmountOrder[_costAmountFilter!.Selected];
        var costType = CostTypeOrder[_costTypeFilter!.Selected];

        var results = new List<Entry>();
        foreach (var card in _cards!.All.OrderBy(SortKey))
        {
            if (kind == KindFilterOption.Creature && !card.IsCreature)
            {
                continue;
            }

            if (kind == KindFilterOption.Spell && card.IsCreature)
            {
                continue;
            }

            if (search.Length > 0 && !card.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var primaryType = CardText.SinglePipType(card.Cost);
            var amount = primaryType is { } t ? card.Cost[t] : 0;

            if (costAmount is { } wantedAmount && amount != wantedAmount)
            {
                continue;
            }

            if (costType is { } wantedType && primaryType != wantedType)
            {
                continue;
            }

            results.Add(new Entry(card, null, null));
        }

        return results;
    }

    // Every ordered pairing of two creatures, including a card with itself -- 27x27, not 27x26.
    // "CreatureA + CreatureA" is a real board scenario (two separate copies of the same card,
    // e.g. two "Basic Circle"s played to adjacent slots, merged) rather than one instance merging
    // with itself -- ActionGenerator.AddMergeActions rules out SAME-SLOT merges
    // (sourceSlot == targetSlot), never same-CARD-ID merges, and AbsorbMerge itself never checks
    // CardId equality either, so this needed no special case to render correctly. Ordered because
    // AbsorbMerge's move-list concatenation and merged display name both depend on which source is
    // "first" (see BuildMergedCell). No separate flip control: First/Second already narrow to a
    // specific ordered pairing when set, and picking them in the other order shows the reverse
    // merge directly.
    private List<Entry> MergedEntries()
    {
        var search = _searchBar!.Text.Trim();

        var firstChoice = _firstCreatureFilter!.Selected == 0 ? null : _creatureCards[_firstCreatureFilter.Selected - 1];
        var secondChoice = _secondCreatureFilter!.Selected == 0 ? null : _creatureCards[_secondCreatureFilter.Selected - 1];

        var results = new List<Entry>();
        foreach (var first in _creatureCards)
        {
            if (firstChoice is not null && first.Id != firstChoice.Id)
            {
                continue;
            }

            foreach (var second in _creatureCards)
            {
                if (secondChoice is not null && second.Id != secondChoice.Id)
                {
                    continue;
                }

                if (search.Length > 0
                    && !first.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !second.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new Entry(null, first, second));
            }
        }

        return results;
    }

    // Adds the cell to the live tree BEFORE calling any Render -- a node's _Ready (which
    // resolves its own GetNode<...> child references) only runs once it enters the tree its
    // scene root is already part of, not merely once it becomes a child of an off-tree Control.
    private void BuildOriginalCell(CardDefinition card, ViewMode viewMode)
    {
        // A spell has no board presence, so In-play falls back to the tooltip for it -- the
        // filter picks a creature's format, not a promise every card can render both.
        if (viewMode == ViewMode.InPlay && card.IsCreature)
        {
            var creature = new CreatureInstance(card.Id, card.Health, card.Types);
            var moves = card.Moves
                .Select((move, index) => (Index: index, Text: MoveText.Of(move), IsUsable: false))
                .ToList();

            RenderInPlayCell(creature, moves, [CardText.Of(card)]);
            return;
        }

        var panel = HoverDetailPanelScene!.Instantiate<HoverDetailPanel>();
        panel.CustomMinimumSize = CardMetrics.TooltipSize;

        // A browser cell is a static face in a grid, not a floating tooltip -- the explainer stack
        // is TopLevel and would float over the neighbouring cells. Keywords still read as bold in
        // the cell's own move text, which is what this screen is for.
        panel.ShowsKeywordExplainers = false;

        _grid!.AddChild(panel);
        panel.Show(CardText.Of(card));
    }

    // A REAL merge: builds each source card's own CreatureInstance, then AbsorbMerge -- the same
    // method ActionExecutor.ApplyMerge calls -- rather than hand-assembling a merged health/type/
    // move list here and risking it drift from what a board merge actually produces. `first`
    // absorbs `second` (receiving/absorbed, per AbsorbMerge's own naming) -- to see the reverse
    // pairing, pick the two creatures in the other order via the First/Second filters rather than
    // a separate flip control, since every ordered pair already exists as its own entry in
    // MergedEntries' unfiltered list.
    private void BuildMergedCell(CardDefinition first, CardDefinition second)
    {
        var receiving = new CreatureInstance(first.Id, first.Health, first.Types);
        var absorbed = new CreatureInstance(second.Id, second.Health, second.Types);
        receiving.AbsorbMerge(absorbed, _cards!.MoveCountOf);

        var moveDefs = _cards.MovesOf(receiving.MergedFrom);
        var moves = moveDefs
            .Select((move, index) => (Index: index, Text: MoveText.Of(move), IsUsable: false))
            .ToList();

        var hoverCards = new List<CardText> { CardText.Of(first), CardText.Of(second) };
        RenderInPlayCell(receiving, moves, hoverCards);
    }

    private void RenderInPlayCell(
        CreatureInstance creature, List<(int Index, MoveText Text, bool IsUsable)> moves,
        IReadOnlyList<CardText> hoverCards)
    {
        var slot = SlotViewScene!.Instantiate<SlotView>();
        _grid!.AddChild(slot);
        slot.Render(new SlotIndex(PlayerId.One, 0), creature, _cards!, isDraggable: false, moves, hoverCards);
    }
}
