using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Sim;

// Per-seat balance instrumentation for one played game: unopposed-slot occupancy, creature
// survival, and affordability pressure (PLAN.md step 4.2c's three additions).
//
// A class per seat rather than a dozen more paired locals in GameRunner.Play: each of these
// metrics needs its own running state (a streak counter, a map of live creatures, per-card
// tallies), and threading six more `xxxOne`/`xxxTwo` pairs through that method was the point at
// which it stopped being readable. GameRunner still owns the game loop; this owns the bookkeeping.
internal sealed class SeatTracker
{
    private readonly PlayerId _seat;

    // Live creatures by slot, each remembering the scoring step it arrived on and whether it has
    // been unopposed at any point since. Keyed by slot because that is what the board is keyed by
    // and what destruction reports; the card id travels in the value.
    private readonly Dictionary<SlotIndex, LiveCreature> _live = [];

    private readonly List<CreatureLifetime> _survival = [];
    private readonly Dictionary<string, int> _blockedByCost = new(StringComparer.Ordinal);

    private int _currentStreak;

    private readonly List<int> _slotsOccupiedByStep = [];
    private readonly List<int> _combinedHealthByStep = [];

    public SeatTracker(PlayerId seat) => _seat = seat;

    public int UnopposedSlotTurns { get; private set; }

    public int ScoringSteps { get; private set; }

    public int LongestUnopposedStreak { get; private set; }

    // BOARD PRESENCE, per scoring step -- slots occupied and combined CURRENT health (not max;
    // a mauled board reads differently from a fresh one holding the same slot count) of this
    // seat's creatures. Sampled at the same step as unopposed-slot occupancy, since that is the
    // moment the scoring rule itself reads the board (GameState.PendingScore) -- the same
    // reasoning that made getting the unopposed-slot-turns sampling point right non-negotiable
    // (see ObserveScoringStep below) applies here too, not just a convenient reuse.
    public IReadOnlyList<int> SlotsOccupiedByStep => _slotsOccupiedByStep;

    public IReadOnlyList<int> CombinedHealthByStep => _combinedHealthByStep;

    public IReadOnlyList<CreatureLifetime> Survival => _survival;

    public IReadOnlyDictionary<string, int> BlockedByCost => _blockedByCost;

    private sealed record LiveCreature(string CardId, int ArrivedAtStep)
    {
        public bool WasEverUnopposed { get; set; }
    }

    // Called once per scoring step for this seat, from the same turn boundary GameRunner already
    // samples score margin at. Counts slot-turns (how many slots were unopposed), board presence
    // (slots occupied, combined current health), maintains the consecutive-step streak, and
    // marks the creatures currently standing in unopposed slots so their lifetime records can
    // distinguish a scoring creature from a blocker. One loop over this seat's slots covers all
    // of it, since every one of these questions is answered by the same board read.
    public void ObserveScoringStep(Board board)
    {
        ScoringSteps++;

        var unopposedNow = 0;
        var occupiedNow = 0;
        var combinedHealthNow = 0;
        foreach (var slot in SlotIndex.AllFor(_seat))
        {
            var creature = board[slot];
            if (creature is null)
            {
                continue;
            }

            occupiedNow++;
            combinedHealthNow += creature.Health;

            if (!board.IsUnopposed(slot))
            {
                continue;
            }

            unopposedNow++;

            if (_live.TryGetValue(slot, out var liveCreature))
            {
                liveCreature.WasEverUnopposed = true;
            }
        }

        UnopposedSlotTurns += unopposedNow;
        _slotsOccupiedByStep.Add(occupiedNow);
        _combinedHealthByStep.Add(combinedHealthNow);

        // Streak counts STEPS on which at least one slot was unopposed, not slots -- "sustained
        // an unopposed creature across consecutive turns" is the shape step 4.2's finding took,
        // and two unopposed slots on one turn is not the same phenomenon as one slot held over
        // two turns.
        if (unopposedNow > 0)
        {
            _currentStreak++;
            LongestUnopposedStreak = Math.Max(LongestUnopposedStreak, _currentStreak);
        }
        else
        {
            _currentStreak = 0;
        }
    }

    public void OnCreaturePlayed(SlotIndex slot, string cardId, int scoringStep)
    {
        // Overwrite rather than add: a slot can be reused after its previous occupant died, and
        // a merge replaces the surviving slot's occupant. Either way the old entry is stale, and
        // its lifetime (if it was destroyed) was already recorded on the destroy path.
        _live[slot] = new LiveCreature(cardId, scoringStep);
    }

    // A merge consumes two creatures and leaves one. The vacated slot's occupant did not die --
    // it was folded in -- so it is dropped from the live set WITHOUT a lifetime record. Counting
    // it as a death would report merge-heavy cards as dying constantly, which is the opposite of
    // what happened to them; counting it as a survivor would inflate lifetimes with a slot the
    // creature no longer holds. Neither is right, so a merged-away creature simply leaves no
    // survival sample.
    public void OnMergedAway(SlotIndex vacatedSlot) => _live.Remove(vacatedSlot);

    public void OnCreatureDestroyed(SlotIndex slot, int scoringStep)
    {
        if (!_live.Remove(slot, out var creature))
        {
            return;
        }

        _survival.Add(new CreatureLifetime(
            creature.CardId,
            scoringStep - creature.ArrivedAtStep,
            creature.WasEverUnopposed));
    }

    // Cards held in hand that failed ONLY the affordability check. Deliberately does NOT record
    // creatures blocked by a full board -- that is slot pressure, a different constraint with a
    // different fix, and conflating the two is exactly what makes the current resource numbers
    // undiagnosable.
    public void ObserveAffordability(GameState state, CardDatabase cards)
    {
        var player = state[_seat];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cardId in player.Hand)
        {
            if (!seen.Add(cardId))
            {
                continue;
            }

            if (!player.CanAfford(cards.Get(cardId).Cost))
            {
                _blockedByCost[cardId] = _blockedByCost.GetValueOrDefault(cardId) + 1;
            }
        }
    }
}
