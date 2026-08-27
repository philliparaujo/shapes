using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The deckbuilding tab (DESIGN.md C2): ten slots, each a 40-card decklist with at most 3 copies of
// any card, edited as two columns -- the whole collection on the left, the current decklist on
// the right -- with a slot picker above them.
//
// TWO DIFFERENT VIEWS, one per column, because the columns ask different questions. The
// collection (left, and the wider of the two) is a grid of full card FACES -- real
// HoverDetailPanels, the same renderer the board hover and CardBrowser use -- because choosing
// what to add is a question about what cards DO. The decklist (right, narrower) stays a column of
// DeckRowViews, because "what is in this deck" is a counting question and forty full faces would
// bury it. Neither column adds a card-detail RENDERER: the collection reuses the tooltip scene
// wholesale, and the decklist is a card LAYOUT over the same CardText/CardArt data.
//
// The earlier cut ran DeckRowView down BOTH columns, which made the two sides visually
// interchangeable -- one wall of near-identical grey bands, where the only way to tell which side
// you were pointing at was to read the header. Two renderers is what makes the sides tell
// themselves apart at a glance.
//
// PAGINATION, as CardBrowser: a collection cell is now a real HoverDetailPanel instantiation
// (Panel, StyleBoxFlat, several Labels, move rows, an art pane), not the cheap band it was, and
// the full card set at once is the same node-count cost CardBrowser's header measures. Filtering
// computes the whole matching list cheaply and only the current page becomes nodes.
//
// Edits write through to disk immediately (DeckStore.Save on every add/remove/rename/delete)
// rather than behind a Save button -- see DeckStore.Save's own note. That also means there is no
// "unsaved changes" state to guard when leaving the tab, which is why Back is an unconditional
// scene change here and needs no confirmation prompt.
//
// PARTIAL DECKS ARE SAVEABLE. A 17-card slot persists exactly as a 40-card one does, and the
// count readout turns red to say it is not yet legal; legality is enforced at MATCH START
// (Lobby.OnStartPressed, via SavedDeck.ToDeck -> DeckBuilder.Custom), not at edit time. Blocking
// the save instead would mean losing an in-progress deck to any interruption, which is the exact
// work this tab is most expensive to redo.
public partial class Deckbuilder : Control
{
    [Export] public NodePath SlotPickerPath { get; set; } = "Layout/FilterPanel/FilterBar/SlotPicker";
    [Export] public NodePath NameEditPath { get; set; } = "Layout/FilterPanel/FilterBar/NameEdit";
    [Export] public NodePath DeleteButtonPath { get; set; } = "Layout/FilterPanel/FilterBar/DeleteButton";
    [Export] public NodePath CompleteDeckButtonPath { get; set; } =
        "Layout/FilterPanel/FilterBar/CompleteDeckButton";
    [Export] public NodePath BackButtonPath { get; set; } = "Layout/TopBar/BackButton";
    [Export] public NodePath CardBrowserButtonPath { get; set; } = "Layout/TopBar/CardBrowserButton";
    [Export] public NodePath SearchBarPath { get; set; } = "Layout/FilterPanel/FilterBar/SearchBar";
    [Export] public NodePath CostTypeFilterPath { get; set; } =
        "Layout/FilterPanel/FilterBar/CostTypeFilter";
    [Export] public NodePath CostAmountFilterPath { get; set; } =
        "Layout/FilterPanel/FilterBar/CostAmountFilter";
    [Export] public NodePath KindFilterPath { get; set; } = "Layout/FilterPanel/FilterBar/KindFilter";
    [Export] public NodePath FilterPanelPath { get; set; } = "Layout/FilterPanel";
    [Export] public NodePath CollectionPanelPath { get; set; } = "Layout/Columns/CollectionPanel";
    [Export] public NodePath DeckPanelPath { get; set; } = "Layout/Columns/DeckPanel";
    [Export] public NodePath CollectionListPath { get; set; } =
        "Layout/Columns/CollectionPanel/CollectionColumn/ScrollContainer/CollectionList";
    [Export] public NodePath CollectionHeaderPath { get; set; } =
        "Layout/Columns/CollectionPanel/CollectionColumn/CollectionHeader/CollectionHeaderLabel";
    [Export] public NodePath PageBarPath { get; set; } =
        "Layout/Columns/CollectionPanel/CollectionColumn/CollectionHeader/PageBar";
    [Export] public NodePath PrevPageButtonPath { get; set; } =
        "Layout/Columns/CollectionPanel/CollectionColumn/CollectionHeader/PageBar/PrevPageButton";
    [Export] public NodePath NextPageButtonPath { get; set; } =
        "Layout/Columns/CollectionPanel/CollectionColumn/CollectionHeader/PageBar/NextPageButton";
    [Export] public NodePath PageLabelPath { get; set; } =
        "Layout/Columns/CollectionPanel/CollectionColumn/CollectionHeader/PageBar/PageLabel";
    [Export] public NodePath DeckListPath { get; set; } =
        "Layout/Columns/DeckPanel/DeckColumn/ScrollContainer/DeckList";
    [Export] public NodePath CountLabelPath { get; set; } =
        "Layout/Columns/DeckPanel/DeckColumn/DeckHeader/CountLabel";
    [Export] public NodePath StatsLabelPath { get; set; } = "Layout/Columns/DeckPanel/DeckColumn/StatsLabel";
    [Export] public NodePath CostCurvePath { get; set; } = "Layout/Columns/DeckPanel/DeckColumn/CostCurve";
    [Export] public string LobbyScenePath { get; set; } = "res://Scenes/Lobby.tscn";
    [Export] public string CardBrowserScenePath { get; set; } = "res://Scenes/CardBrowser.tscn";

    [Export] public PackedScene? HoverDetailPanelScene { get; set; }

    // A page of collection cells. 15 = 5 columns x 3 rows, matching the grid's own column count
    // so a full page fills a rectangle rather than ending mid-row (the same reasoning, and the
    // same arithmetic, as CardBrowser.PageSize). See the class header on why the card-face grid
    // needs paging at all.
    private const int PageSize = 15;

    // Index-aligned with the OptionButton items PopulateFilters adds -- the same convention
    // CardBrowser and Lobby both use for their pickers.
    private static readonly ResourceType?[] CostTypeOrder =
        [null, ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel];

    private static readonly int?[] CostAmountOrder = [null, 1, 2, 3, 4, 5];

    private static readonly bool?[] KindOrder = [null, true, false];

    private OptionButton? _slotPicker;
    private LineEdit? _nameEdit;
    private Button? _deleteButton;
    private Button? _completeDeckButton;
    private Button? _backButton;
    private Button? _cardBrowserButton;
    private LineEdit? _searchBar;
    private OptionButton? _costTypeFilter;
    private OptionButton? _costAmountFilter;
    private OptionButton? _kindFilter;
    private GridContainer? _collectionList;
    private Label? _collectionHeader;
    private Control? _pageBar;
    private Button? _prevPageButton;
    private Button? _nextPageButton;
    private Label? _pageLabel;
    private VBoxContainer? _deckList;
    private Label? _countLabel;
    private Label? _statsLabel;
    private CostCurveChart? _costCurve;

    private int _page;

    private CardDatabase? _cards;
    private RuleSet _rules = RuleSet.Default;
    private DeckSlots _slots = DeckSlots.Empty();
    private int _slotIndex;

    // The floating card tooltip, created once and reused -- a hover that instantiated a fresh
    // HoverDetailPanel per row would leak a node per mouse-over across a long editing session.
    private HoverDetailPanel? _hoverPanel;

    private int DeckSize => DeckBuilder.DeckSizeOf(_rules);
    private int MaxCopies => DeckBuilder.MaxCopiesOf(_rules);
    private SavedDeck CurrentDeck => _slots.Slots[_slotIndex];

    public override void _Ready()
    {
        // DESIGN.md D3 phase 1 -- the project theme, inherited by everything below this root.
        // DESIGN.md D7: content scale, applied before anything measures itself. Idempotent and
        // no-op on desktop -- see Platform.ApplyContentScale.
        Platform.ApplyContentScale(this);

        UiTheme.ApplyTo(this);

        // DESIGN.md D7c: this screen's Layout is anchored to all four edges, so on a phone it draws
        // under the system bars and any display cutout (the Android preset sets immersive mode).
        // Inert on desktop by TouchLayout's own gate -- the .tscn offsets are left untouched there.
        TouchLayout.InsetOffsets(GetNodeOrNull<Control>("Layout"));

        GodotTextFormat.Ensure();

        _slotPicker = GetNode<OptionButton>(SlotPickerPath);
        _nameEdit = GetNode<LineEdit>(NameEditPath);
        _deleteButton = GetNode<Button>(DeleteButtonPath);
        _completeDeckButton = GetNode<Button>(CompleteDeckButtonPath);
        _backButton = GetNode<Button>(BackButtonPath);
        _cardBrowserButton = GetNode<Button>(CardBrowserButtonPath);
        _searchBar = GetNode<LineEdit>(SearchBarPath);
        _costTypeFilter = GetNode<OptionButton>(CostTypeFilterPath);
        _costAmountFilter = GetNode<OptionButton>(CostAmountFilterPath);
        _kindFilter = GetNode<OptionButton>(KindFilterPath);
        _collectionList = GetNode<GridContainer>(CollectionListPath);
        _collectionHeader = GetNode<Label>(CollectionHeaderPath);
        _pageBar = GetNode<Control>(PageBarPath);
        _prevPageButton = GetNode<Button>(PrevPageButtonPath);
        _nextPageButton = GetNode<Button>(NextPageButtonPath);
        _pageLabel = GetNode<Label>(PageLabelPath);
        _deckList = GetNode<VBoxContainer>(DeckListPath);
        _countLabel = GetNode<Label>(CountLabelPath);
        _statsLabel = GetNode<Label>(StatsLabelPath);
        _costCurve = GetNode<CostCurveChart>(CostCurvePath);

        // Raised rather than the theme's default panel fill -- see CardBrowser's own _Ready note:
        // Palette.Surface sits almost flush with TableBackdrop's mid-tone, so an un-overridden
        // panel barely separates from the backdrop behind it. Matches CardBrowser's filter/grid
        // panels so the two card screens (reached from one another via the header buttons) read
        // as the same console rather than two different levels of polish.
        var raisedPanel = new StyleBoxFlat { BgColor = Palette.SurfaceRaised, BorderColor = Palette.SurfaceEdge };
        raisedPanel.SetBorderWidthAll(Palette.BorderWidth);
        raisedPanel.SetCornerRadiusAll(Palette.CornerRadius);
        raisedPanel.ContentMarginLeft = 16;
        raisedPanel.ContentMarginRight = 16;
        raisedPanel.ContentMarginTop = 12;
        raisedPanel.ContentMarginBottom = 12;

        GetNode<PanelContainer>(FilterPanelPath).AddThemeStyleboxOverride("panel", raisedPanel);
        GetNode<PanelContainer>(CollectionPanelPath).AddThemeStyleboxOverride("panel", raisedPanel);
        GetNode<PanelContainer>(DeckPanelPath).AddThemeStyleboxOverride("panel", raisedPanel);

        HoverDetailPanelScene ??= GD.Load<PackedScene>("res://Scenes/HoverDetailPanel.tscn");

        _cards = ContentLoader.LoadCards();
        _slots = DeckStore.Load();

        BuildHoverPanel();
        PopulateFilters();
        PopulateSlotPicker();

        _slotPicker.ItemSelected += OnSlotSelected;
        _nameEdit.TextChanged += OnNameChanged;
        _deleteButton.Pressed += OnDeletePressed;
        _completeDeckButton.Pressed += OnCompleteDeckPressed;
        _backButton.Pressed += () => GetTree().ChangeSceneToFile(LobbyScenePath);

        // The return leg of the browser's own link (DESIGN.md D3 phase 3). Safe as an unconditional
        // scene change for the reason this class's header already gives about Back: every edit is
        // written through to DeckStore immediately, so there is no unsaved state to guard.
        _cardBrowserButton.Pressed += () => GetTree().ChangeSceneToFile(CardBrowserScenePath);
        // A filter change resets to page one; paging does not. Narrowing the set while sitting on
        // page 4 would otherwise land on an empty page whose contents the filter just removed.
        _searchBar.TextChanged += _ => OnFilterChanged();
        _costTypeFilter.ItemSelected += _ => OnFilterChanged();
        _costAmountFilter.ItemSelected += _ => OnFilterChanged();
        _kindFilter.ItemSelected += _ => OnFilterChanged();
        _prevPageButton.Pressed += () => ChangePage(-1);
        _nextPageButton.Pressed += () => ChangePage(1);

        SelectSlot(0);
    }

    // One panel, parented to this Control and hidden until a row is hovered. Top-level so it
    // floats above both scrolling columns rather than being clipped by whichever one the cursor
    // is inside -- a tooltip that a ScrollContainer clips is worse than no tooltip.
    private void BuildHoverPanel()
    {
        _hoverPanel = HoverDetailPanelScene!.Instantiate<HoverDetailPanel>();
        _hoverPanel.TopLevel = true;
        _hoverPanel.Visible = false;
        _hoverPanel.MouseFilter = MouseFilterEnum.Ignore;
        _hoverPanel.CustomMinimumSize = CardMetrics.TooltipSize;
        AddChild(_hoverPanel);
    }

    // Wording matches CardBrowser.PopulateFilters exactly (DESIGN.md D3 phase 3's "one console, two
    // screens" reasoning) -- a player moving between the two card screens should not have to
    // learn that Deckbuilding's "Type" is CardBrowser's "Type" under a different name.
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
    }

    // Slot labels carry their deck's name and size ("3. Aggro Spike (40)") so the picker doubles
    // as the deck list -- with ten fixed slots there is no separate "my decks" screen, and a
    // picker reading "Slot 3" ten times over would make choosing one a guessing game.
    private void PopulateSlotPicker()
    {
        var selected = _slotPicker!.Selected;
        _slotPicker.Clear();

        for (var i = 0; i < DeckSlots.SlotCount; i++)
        {
            _slotPicker.AddItem(SlotLabel(i));
        }

        // Restore the caller's selection: this is re-run after every rename and card edit (both
        // change a label), and an OptionButton reset to item 0 mid-edit would silently switch
        // which deck the next click edits.
        _slotPicker.Selected = Math.Clamp(selected, 0, DeckSlots.SlotCount - 1);
    }

    private string SlotLabel(int index)
    {
        var deck = _slots.Slots[index];
        var number = index + 1;

        if (deck.IsEmpty)
        {
            return $"{number}. <empty>";
        }

        var name = deck.Name.Length > 0 ? deck.Name : "Unnamed";
        return $"{number}. {name} ({deck.TotalCards})";
    }

    private void OnSlotSelected(long index) => SelectSlot((int)index);

    private void SelectSlot(int index)
    {
        _slotIndex = Math.Clamp(index, 0, DeckSlots.SlotCount - 1);
        _slotPicker!.Selected = _slotIndex;

        // Set without firing TextChanged -- assigning LineEdit.Text does not emit the signal, but
        // being explicit here matters because OnNameChanged writes to disk, and a slot switch
        // must not re-save the deck it is switching AWAY from under the new slot's name.
        _nameEdit!.Text = CurrentDeck.Name;

        RebuildCollection();
        RebuildDeckList();
        UpdateSummary();
    }

    // Renaming is itself an edit: a named empty slot is a deck the player has started, which is
    // what makes "create a new deck" nothing more than picking an empty slot and typing a name.
    private void OnNameChanged(string text)
    {
        CurrentDeck.Name = text;
        DeckStore.Save(_slots);
        PopulateSlotPicker();
    }

    // Clearing the slot rather than removing it from a list -- see DeckSlots' header on why slot
    // index is the stable identity a lobby seat selection refers to. The emptied slot stays
    // selected so the player can immediately build something else in it.
    private void OnDeletePressed()
    {
        _slots.Slots[_slotIndex] = new SavedDeck();
        DeckStore.Save(_slots);

        _nameEdit!.Text = "";
        PopulateSlotPicker();
        RebuildCollection();
        RebuildDeckList();
        UpdateSummary();
    }

    // --- The two columns ------------------------------------------------------------------

    private void OnFilterChanged()
    {
        _page = 0;
        RebuildCollection();
    }

    private void ChangePage(int delta)
    {
        _page = Math.Max(0, _page + delta);
        RebuildCollection();
    }

    // Every card in the set, filtered, each showing how many copies the current deck runs -- so
    // the collection column doubles as the "what have I already got" readout and the player never
    // has to cross-reference the two lists to answer it. That readout is the copies badge AND the
    // cell's own lifted fill (CollectionCardView), which is what makes it answerable by scanning
    // rather than by reading every badge.
    private void RebuildCollection()
    {
        ClearChildren(_collectionList!);

        // The cheap pass first -- which cards match, no nodes -- so filtering and paging cost
        // nothing regardless of set size, and only the visible page pays for card faces. Same
        // split CardBrowser.OriginalEntries makes, and for the same reason.
        var matches = FilteredCards();

        var totalPages = Math.Max(1, (matches.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, totalPages - 1);

        foreach (var card in matches.Skip(_page * PageSize).Take(PageSize))
        {
            var cell = new CollectionCardView();
            cell.AddRequested += OnAddRequested;
            cell.RemoveRequested += OnRemoveRequested;

            // Added to the live tree before Render, which instantiates the card face inside it --
            // a HoverDetailPanel's _Ready resolves its own child references and only runs once it
            // is in the tree (the ordering CardBrowser.BuildOriginalCell documents).
            _collectionList!.AddChild(cell);
            cell.Render(
                CardText.Of(card), CurrentDeck.CopiesOf(card.Id), MaxCopies, HoverDetailPanelScene!);
        }

        _collectionHeader!.Text = $"Collection ({matches.Count})";
        _pageBar!.Visible = matches.Count > PageSize;
        _pageLabel!.Text = $"Page {_page + 1} / {totalPages}";
        _prevPageButton!.Disabled = _page == 0;
        _nextPageButton!.Disabled = _page >= totalPages - 1;
    }

    private List<CardDefinition> FilteredCards()
    {
        var search = _searchBar!.Text.Trim();
        var costType = CostTypeOrder[_costTypeFilter!.Selected];
        var costAmount = CostAmountOrder[_costAmountFilter!.Selected];
        var wantCreature = KindOrder[_kindFilter!.Selected];

        var results = new List<CardDefinition>();
        foreach (var card in _cards!.All.OrderBy(SortKey))
        {
            if (wantCreature is { } creature && card.IsCreature != creature)
            {
                continue;
            }

            if (search.Length > 0 && !card.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var primaryType = CardText.SinglePipType(card.Cost);
            if (costType is { } type && primaryType != type)
            {
                continue;
            }

            var amount = primaryType is { } t ? card.Cost[t] : 0;
            if (costAmount is { } wantedAmount && amount != wantedAmount)
            {
                continue;
            }

            results.Add(card);
        }

        return results;
    }

    // The current decklist, one row per distinct card with its copy count. Sorted by the same
    // cost/name key the collection uses, so a card sits at the same relative position in both
    // columns and the eye can track it across.
    private void RebuildDeckList()
    {
        ClearChildren(_deckList!);

        var entries = CurrentDeck.Cards
            .Where(e => _cards!.Contains(e.CardId))
            .OrderBy(e => SortKey(_cards![e.CardId]));

        foreach (var entry in entries)
        {
            var row = BuildRow(CardText.Of(_cards![entry.CardId]), entry.Count);
            _deckList!.AddChild(row);
        }
    }

    // The decklist's row. The GESTURE is identical to the collection's card cell -- left click
    // adds a copy, right click removes one -- even though the two sides no longer share a view
    // type: "click the card to get another, right-click to drop one" is one rule to learn rather
    // than two column-specific ones, and it means a deck can be trimmed without hunting for the
    // card in the other list. Only the decklist rows raise hover, since a collection cell is
    // already showing the full card face a tooltip would duplicate.
    private DeckRowView BuildRow(CardText card, int copies)
    {
        var row = new DeckRowView();
        row.Render(card, copies > 0 ? copies : null);

        row.AddRequested += OnAddRequested;
        row.RemoveRequested += OnRemoveRequested;
        row.HoverStarted += OnHoverStarted;
        row.HoverEnded += OnHoverEnded;

        return row;
    }

    private void OnAddRequested(string cardId)
    {
        var copies = CurrentDeck.CopiesOf(cardId);

        // Both limits enforced at the point of the edit so an illegal deck is never even
        // constructed: at most MaxCopies of one card, and never more than DeckSize total. The
        // 40-card ceiling is a cap on ADDING, not a legality gate -- a deck under 40 saves fine
        // (see the class header), it just cannot be started.
        if (copies >= MaxCopies || CurrentDeck.TotalCards >= DeckSize)
        {
            return;
        }

        CurrentDeck.SetCopies(cardId, copies + 1);
        CommitEdit();
    }

    private void OnRemoveRequested(string cardId)
    {
        var copies = CurrentDeck.CopiesOf(cardId);
        if (copies <= 0)
        {
            return;
        }

        CurrentDeck.SetCopies(cardId, copies - 1);
        CommitEdit();
    }

    // Every card edit: persist, then rebuild both columns and the readouts. Rebuilding the whole
    // list rather than patching the one changed row is the same call PlayerPanel.RenderHand
    // makes -- a few dozen rows is cheap next to the bookkeeping of finding and mutating one
    // node, and a full rebuild cannot leave the two columns disagreeing about a count.
    private void CommitEdit()
    {
        DeckStore.Save(_slots);

        RebuildCollection();
        RebuildDeckList();
        UpdateSummary();
        PopulateSlotPicker();
    }

    // Fills the current slot up to a legal deck using DeckBuilder.Complete: every card already
    // chosen is kept, and the rest is picked with the same type-balance and cost-matching
    // constraints Starter() uses, so "finish this deck for me" produces something as legal and as
    // curve-sensible as a deck built by hand. With an empty slot this generates a deck from
    // scratch, which is the same call with nothing to keep.
    //
    // Seeded from the wall clock rather than a fixed constant (unlike StarterDeckSeed) --
    // repeated presses are supposed to explore different completions, not reproduce the same one,
    // which is the "make this random" half of the request.
    private void OnCompleteDeckPressed()
    {
        if (_cards is null || CurrentDeck.TotalCards >= DeckSize)
        {
            return;
        }

        var existing = CurrentDeck.ToCardIds();
        var random = new SeededRandom(unchecked((ulong)DateTime.UtcNow.Ticks) ^ (ulong)System.Environment.TickCount);

        Deck completed;
        try
        {
            var name = CurrentDeck.Name.Length > 0 ? CurrentDeck.Name : "custom";
            completed = DeckBuilder.Complete(name, existing, _cards, _rules, random);
        }
        catch (DeckBuildException e)
        {
            GD.PushWarning($"Could not complete deck: {e.Message}");
            return;
        }

        foreach (var group in completed.Cards.GroupBy(id => id, StringComparer.Ordinal))
        {
            CurrentDeck.SetCopies(group.Key, group.Count());
        }

        CommitEdit();
    }

    // --- Readouts -------------------------------------------------------------------------

    // Count and legality up front, then the same cost/type breakdown the sim reports per deck
    // (DeckBuilder.TypeCounts) -- the numbers a player is actually balancing while building, and
    // deliberately the SAME derivation the sim uses so a deck's curve reads identically in both
    // places rather than through a second, drifting definition.
    private void UpdateSummary()
    {
        var total = CurrentDeck.TotalCards;
        var legal = total == DeckSize;

        _countLabel!.Text = $"{total} / {DeckSize}";
        _countLabel.AddThemeColorOverride(
            "font_color", legal ? new Color("8fd694") : new Color("d68f8f"));
        _completeDeckButton!.Disabled = legal;

        if (total == 0)
        {
            _statsLabel!.Text = "Empty slot -- click cards on the left to add them.";
            _costCurve!.SetCounts([]);
            return;
        }

        var cardIds = CurrentDeck.ToCardIds();
        var byCost = DeckBuilder.TypeCounts(cardIds, _cards!);
        var mean = DeckBuilder.MeanCost(cardIds, _cards!);

        var creatures = cardIds.Count(id => _cards![id].IsCreature);

        // The type totals stay in the text even though the chart now shows the same three colors
        // stacked: a stacked bar answers "where in the curve is my spike" well and "exactly how
        // much spike" badly, and the second is the number a player checks a deck against.
        _statsLabel!.Text =
            $"Spike {byCost[ResourceType.Spike]}   Anvil {byCost[ResourceType.Anvil]}   "
            + $"Wheel {byCost[ResourceType.Wheel]}      "
            + $"Creatures {creatures}   Spells {cardIds.Count - creatures}      "
            + $"Mean cost {mean:F2}";

        // One entry per card in the deck, copies included -- the curve is about what you will
        // actually draw (see CostCurveChart.SetCounts).
        _costCurve!.SetCounts(cardIds.Select(id =>
        {
            var card = _cards![id];
            var type = CardText.SinglePipType(card.Cost);
            return (type is { } t ? card.Cost[t] : 0, type);
        }));
    }

    private void OnHoverStarted(string cardId)
    {
        if (_cards is null || !_cards.Contains(cardId))
        {
            return;
        }

        // Show sets Visible itself -- see HoverDetailPanel.Show.
        _hoverPanel!.Show(CardText.Of(_cards[cardId]));

        // Follows the cursor, clamped inside the viewport so a row near the bottom or right edge
        // does not push the panel off screen -- the same placement rule BoardView applies to its
        // own hover panel.
        var mouse = GetGlobalMousePosition();
        var size = _hoverPanel.Size == Vector2.Zero ? CardMetrics.TooltipSize : _hoverPanel.Size;
        var viewport = GetViewportRect().Size;

        _hoverPanel.GlobalPosition = new Vector2(
            Math.Clamp(mouse.X + 16f, 0f, Math.Max(0f, viewport.X - size.X)),
            Math.Clamp(mouse.Y + 16f, 0f, Math.Max(0f, viewport.Y - size.Y)));
    }

    private void OnHoverEnded() => _hoverPanel!.Hide();

    // Cost, then name -- the same key CardBrowser sorts by (DESIGN.md C4's sort rule), so the two
    // card screens present the set in one order rather than two.
    private static (int Cost, string Name) SortKey(CardDefinition card)
    {
        var type = CardText.SinglePipType(card.Cost);
        return (type is { } t ? card.Cost[t] : 0, card.Name);
    }

    private static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
