using Shapes.Core.Effects;
using Shapes.Core.Primitives;

namespace Shapes.Core.Cards;

// What kind of card this is. Creatures occupy a slot; spells resolve and are gone.
public enum CardKind
{
    Creature = 0,
    Spell = 1,
}

// One card, as static data shared by every copy in play.
//
// Immutable, and there is exactly one instance per card id for the whole process: a
// CreatureInstance stores only the card id and looks its moves up here (see
// CreatureInstance.MergedFrom), so this must never be mutated by play.
//
// Validation lives in CardValidator rather than this constructor, unlike RuleSet. A ruleset is
// a handful of independent scalars, so each can be checked where it is assigned; a card's
// rules are cross-cutting -- the single-target rule spans every effect of every move, and
// "unknown op" needs the effect registry -- so they belong in one place that can also name the
// offending card, move, and effect in the message.
public sealed class CardDefinition
{
    public string Id { get; }

    public string Name { get; }

    public CardKind Kind { get; }

    // The top-left pip cost paid to play this card from hand.
    public ResourcePool Cost { get; }

    // Creature stats. Both are zero for a spell, which never enters the board.
    public int Health { get; }

    public TypeMask Types { get; }

    // A spell has exactly one effect list and no moves; a creature has moves and no effects of
    // its own (no passive/triggered effects exist -- all damage comes from activated moves).
    public IReadOnlyList<MoveDefinition> Moves { get; }

    public IReadOnlyList<EffectNode> Effects { get; }

    // The one chosen_* selector this card's own effects declare, or null. Computed once here
    // rather than by walking the effect tree per call: the tree is immutable static data, so the
    // answer can never change, and legal-action generation asks for it on every card in hand on
    // every turn -- inside the per-slot loop, at that. Walking it there made the search pay for
    // an allocation-heavy tree traversal to re-derive a constant.
    //
    // At most one, per the single-target rule; CardValidator rejects a card declaring more.
    public TargetSelector? ChosenSelector { get; }

    public CardDefinition(
        string id,
        string name,
        CardKind kind,
        ResourcePool cost,
        int health,
        TypeMask types,
        IReadOnlyList<MoveDefinition>? moves = null,
        IReadOnlyList<EffectNode>? effects = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        Kind = kind;
        Cost = cost;
        Health = health;
        Types = types;
        Moves = moves ?? [];
        Effects = effects ?? [];
        ChosenSelector = EffectTree.FindChosenSelector(Effects);
    }

    public bool IsCreature => Kind == CardKind.Creature;

    public override string ToString() => $"{Id} ({Name})";
}

// One activated move on a creature. Usable once per turn, any number of different moves per
// creature per turn, subject only to affordability.
public sealed class MoveDefinition
{
    public string Name { get; }

    public ResourcePool Cost { get; }

    // The attacking type for damage this move deals, derived from its cost rather than
    // declared separately -- a move paid for with spikes attacks as Spike. Null for a
    // zero-cost move, whose damage is typeless and always 1x, exactly like a spell's.
    //
    // CardValidator enforces that a move's cost is single-type, which is what makes this
    // well-defined: a mixed-cost move would have no single answer here, and EffectContext.MoveType
    // deliberately does not model more than one attacking type.
    public ResourceType? AttackType { get; }

    // Optional gate on using the move at all, e.g. { "op": "self_at_full_health" }. Evaluated
    // by legal-action generation (step 1.8) -- an unmet condition means the move is not a legal
    // action, rather than a legal action that resolves to nothing.
    public EffectNode? Condition { get; }

    public IReadOnlyList<EffectNode> Effects { get; }

    // As CardDefinition.ChosenSelector: precomputed, because this is asked once per move per
    // creature per generated turn and the answer is a property of static card data.
    public TargetSelector? ChosenSelector { get; }

    public MoveDefinition(
        string name,
        ResourcePool cost,
        IReadOnlyList<EffectNode> effects,
        EffectNode? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(effects);

        Name = name;
        Cost = cost;
        Effects = effects;
        Condition = condition;
        AttackType = SingleCostType(cost);
        ChosenSelector = EffectTree.FindChosenSelector(effects);
    }

    // The one type present in a single-type cost, or null for a zero cost. Returns null rather
    // than throwing on a mixed cost: CardValidator rejects those before construction, and this
    // is not the layer that should produce that error message.
    private static ResourceType? SingleCostType(ResourcePool cost)
    {
        ResourceType? found = null;

        foreach (var type in ResourceTypes.All)
        {
            if (cost[type] <= 0)
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = type;
        }

        return found;
    }

    public override string ToString() => $"{Name} ({Cost})";
}
