using Godot;
using Godot.Collections;
using Shapes.Core.Primitives;

namespace Shapes.Godot.Scripts;

// What's being dragged, for PLAN.md B1a's drag-and-drop gestures (play a card, merge a
// creature). Godot's _GetDragData/_DropData trade in Variant, not arbitrary C# objects, so this
// packs into a Godot.Collections.Dictionary (Variant-compatible) rather than being passed
// directly -- exactly one of CardId/SourceSlot is set, matching the two drag sources that exist
// (CardFace for a hand card, SlotView for a board creature).
public readonly struct DragPayload
{
    private const string KindKey = "shapes_drag_kind";
    private const string CardKind = "hand_card";
    private const string CreatureKind = "creature";
    private const string CardIdKey = "card_id";
    private const string SlotFlatIndexKey = "slot_flat_index";

    public string? CardId { get; private init; }

    public SlotIndex? SourceSlot { get; private init; }

    public static Variant ForHandCard(string cardId) => new Dictionary
    {
        [KindKey] = CardKind,
        [CardIdKey] = cardId,
    };

    public static Variant ForCreature(SlotIndex slot) => new Dictionary
    {
        [KindKey] = CreatureKind,
        [SlotFlatIndexKey] = slot.ToFlatIndex(),
    };

    public static bool TryRead(Variant data, out DragPayload payload)
    {
        payload = default;

        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        var dict = data.AsGodotDictionary();
        if (!dict.TryGetValue(KindKey, out var kind))
        {
            return false;
        }

        switch (kind.AsString())
        {
            case CardKind when dict.TryGetValue(CardIdKey, out var cardId):
                payload = new DragPayload { CardId = cardId.AsString() };
                return true;

            case CreatureKind when dict.TryGetValue(SlotFlatIndexKey, out var flat):
                payload = new DragPayload { SourceSlot = SlotIndex.FromFlatIndex(flat.AsInt32()) };
                return true;

            default:
                return false;
        }
    }
}
