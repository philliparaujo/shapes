using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Godot.Adapter;

namespace Shapes.Godot.Scripts;

// The deckbuilding tab (PLAN.md C2): ten slots, each a 40-card decklist with at most 3 copies of
// any card, edited as two columns -- the whole collection on the left, the current decklist on
// the right -- with a slot picker above them.
//
// TWO LISTS OF DeckRowViews, not a grid of card faces. CardBrowser is the view for READING cards
// (full detail, paginated because each cell is an expensive real SlotView/HoverDetailPanel); this
// is the view for COUNTING them, where the question at hand is "how many spike two-drops am I
// running" and forty full card faces would bury it. The full-detail reading is still one hover
// away, through the same HoverDetailPanel the board uses -- so this tab adds a card LAYOUT
// (DeckRowView) without adding a second card-detail RENDERER.
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
    [Export] public NodePath SlotPickerPath { get; set; } = "Layout/TopBar/SlotPicker";
    [Export] public NodePath NameEditPath { get; set; } = "Layout/TopBar/NameEdit";
    [Export] public NodePath DeleteButtonPath { get; set; } = "Layout/TopBar/DeleteButton";
    [Export] public NodePath BackButtonPath { get; set; } = "Layout/TopBar/BackButton";
    [Export] public NodePath SearchBarPath { get; set; } = "Layout/FilterBar/SearchBar";
    [Export] public NodePath CostTypeFilterPath { get; set; } = "Layout/FilterBar/CostTypeFilter";
    [Export] public NodePath KindFilterPath { get; set; } = "Layout/FilterBar/KindFilter";
    [Export] public NodePath CollectionListPath { get; set; } = "Layout/Columns/CollectionColumn/ScrollContainer/CollectionList";
    [Export] public NodePath DeckListPath { get; set; } = "Layout/Columns/DeckColumn/ScrollContainer/DeckList";
    [Export] public NodePath CountLabelPath { get; set; } = "Layout/Columns/DeckColumn/DeckHeader/CountLabel";
    [Export] public NodePath StatsLabelPath { get; set; } = "Layout/Columns/DeckColumn/StatsLabel";
    [Export] public string LobbyScenePath { get; set; } = "res://Scenes/Lobby.tscn";

    [Export] public PackedScene? HoverDetailPanelScene { get; set; }

    // Index-aligned with the OptionButton items PopulateFilters adds -- the same convention
    // CardBrowser and Lobby both use for their pickers.
    private static readonly ResourceType?[] CostTypeOrder =
        [null, ResourceType.Spike, ResourceType.Anvil, ResourceType.Wheel];

    private static readonly bool?[] KindOrder = [null, true, false];

    private OptionButton? _slotPicker;
    private LineEdit? _nameEdit;
    private Button? _deleteButton;
    private Button? _backButton;
    private LineEdit? _searchBar;
    private OptionButton? _costTypeFilter;
    private OptionButton? _kindFilter;
    private VBoxContainer? _collectionList;
    private VBoxContainer? _deckList;
    private Label? _countLabel;
    private Label? _statsLabel;

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
        GodotTextFormat.Ensure();

        _slotPicker = GetNode<OptionButton>(SlotPickerPath);
        _nameEdit = GetNode<LineEdit>(NameEditPath);
        _deleteButton = GetNode<Button>(DeleteButtonPath);
        _backButton = GetNode<Button>(BackButtonPath);
        _searchBar = GetNode<LineEdit>(SearchBarPath);
        _costTypeFilter = GetNode<OptionButton>(CostTypeFilterPath);
        _kindFilter = GetNode<OptionButton>(KindFilterPath);
        _collectionList = GetNode<VBoxContainer>(CollectionListPath);
        _deckList = GetNode<VBoxContainer>(DeckListPath);
        _countLabel = GetNode<Label>(CountLabelPath);
        _statsLabel = GetNode<Label>(StatsLabelPath);

        HoverDetailPanelScene ??= GD.Load<PackedScene>("res://Scenes/HoverDetailPanel.tscn");

        var cardsDir = Path.Combine(AppContext.BaseDirectory, "Content", "cards");
        _cards = CardLoader.FromDirectory(cardsDir);
        _slots = DeckStore.Load();

        BuildHoverPanel();
        PopulateFilters();
        PopulateSlotPicker();

        _slotPicker.ItemSelected += OnSlotSelected;
        _nameEdit.TextChanged += OnNameChanged;
        _deleteButton.Pressed += OnDeletePressed;
        _backButton.Pressed += () => GetTree().ChangeSceneToFile(LobbyScenePath);
        _searchBar.TextChanged += _ => RebuildCollection();
        _costTypeFilter.ItemSelected += _ => RebuildCollection();
        _kindFilter.ItemSelected += _ => RebuildCollection();

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

    private void PopulateFilters()
    {
        _costTypeFilter!.Clear();
        _costTypeFilter.AddItem("All costs");
        _costTypeFilter.AddItem("Spike");
        _costTypeFilter.AddItem("Anvil");
        _costTypeFilter.AddItem("Wheel");

        _kindFilter!.Clear();
        _kindFilter.AddItem("All cards");
        _kindFilter.AddItem("Creatures");
        _kindFilter.AddItem("Spells");
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

    // Every card in the set, filtered, each showing how many copies the current deck runs -- so
    // the collection column doubles as the "what have I already got" readout and the player never
    // has to cross-reference the two lists to answer it.
    private void RebuildCollection()
    {
        ClearChildren(_collectionList!);

        var search = _searchBar!.Text.Trim();
        var costType = CostTypeOrder[_costTypeFilter!.Selected];
        var wantCreature = KindOrder[_kindFilter!.Selected];

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

            if (costType is { } type && CardText.SinglePipType(card.Cost) != type)
            {
                continue;
            }

            var row = BuildRow(CardText.Of(card), CurrentDeck.CopiesOf(card.Id));
            _collectionList!.AddChild(row);
        }
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

    // Both columns build the same row type with the same handlers: left click adds a copy, right
    // click removes one, hovering shows the full card. Deliberately identical on both sides --
    // "click the card to get another, right-click to drop one" is one rule to learn rather than
    // two column-specific ones, and it means a deck can be trimmed without hunting for the card
    // in the other list.
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

        if (total == 0)
        {
            _statsLabel!.Text = "Empty slot -- click cards on the left to add them.";
            return;
        }

        var cardIds = CurrentDeck.ToCardIds();
        var byCost = DeckBuilder.TypeCounts(cardIds, _cards!);
        var mean = DeckBuilder.MeanCost(cardIds, _cards!);

        var creatures = cardIds.Count(id => _cards![id].IsCreature);

        _statsLabel!.Text =
            $"Spike {byCost[ResourceType.Spike]}   Anvil {byCost[ResourceType.Anvil]}   "
            + $"Wheel {byCost[ResourceType.Wheel]}      "
            + $"Creatures {creatures}   Spells {cardIds.Count - creatures}      "
            + $"Mean cost {mean:F2}";
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

    // Cost, then name -- the same key CardBrowser sorts by (PLAN.md C4's sort rule), so the two
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
