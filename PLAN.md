# Shapes — Development Plan

A 2-player, turn-based, board-and-cards game. Four phases: playable engine → IS-MCTS AI →
AI-driven balance → Godot client.

## Status

| Phase                          | Progress   |
|--------------------------------|------------|
| 1 — Playable engine            | 3 / 14     |
| 2 — IS-MCTS AI                 | 0 / 8      |
| 3 — AI-driven balance          | 0 / 7      |
| 4 — Godot client               | 0 / 12     |

67 tests passing.

**Next up: step 1.4** — `RuleSet` fields + JSON loading.

## 0. Confirmed ruleset

This supersedes the reference PDF where they disagree. The PDF's resource-acquisition graph
(the `111`/`210`/`021` node blob) is **obsolete**.

### Resources & types

Three resources, in a rock-paper-scissors cycle:

| Symbol | Name  | Material | Flavor                    | Keyword   |
|--------|-------|----------|---------------------------|-----------|
| △      | Spike | metal    | sharp, attack, pierce     | pierce    |
| ▢      | Anvil | concrete | sturdy, defense, blunt    | reflect   |
| ◯      | Wheel | rubber   | elastic, speed, ricochet  | ricochet  |

Effectiveness cycle: **Anvil → Spike → Wheel → Anvil** (each beats the next).

### Type effectiveness

A target takes **2× damage** if it is weak to the attacker's type:

- Spike → Wheel = 2×
- Wheel → Anvil = 2×
- Anvil → Spike = 2×

All other single-type matchups are 1×. There is no resistance/halving — only neutral and double.

**Merged (multi-type) targets** take 2× if **one of its types matches the attacker and another
is weak to it**. So Spike deals 2× to a Spike/Wheel creature.

> Note the consequence: **merging can increase vulnerability.** A pure Spike creature takes 1×
> from Spike, but a Spike/Wheel creature takes 2×. This is a genuine cost to merging and
> meaningfully offsets the "free strictly-better action" concern flagged below — merging trades
> a defensive profile for stats and moves. Phase 3 should measure whether that tradeoff is
> priced correctly.

The attacker's type comes from the **creature using the move**. Damage from spells (no creature
source) is **typeless and always 1×** unless a card states otherwise.

Effectiveness is applied when damage resolves, after flat modifiers (`next_attack_bonus`) and
before clamping — the exact ordering is a `RuleSet`-adjacent decision and must be pinned by a
test, since it changes numbers whenever both a bonus and a 2× apply.

Considered unlikely to change, but implemented as a swappable `ITypeChart` behind the `RuleSet`
so the multiplier, the cycle, and the merged-target rule can all be varied in balance sweeps.

### Income

Each turn a player gains:
- **1 of each resource** (flat `1/1/1`), plus
- **+1 resource per creature controlled**, of that creature's type. A merged creature has
  multiple types and generates one of *each* of its types.

### Board

3 slots per player, arranged facing the opponent's 3 slots. Slot *i* opposes enemy slot *i*.

### Turn structure

1. **Score** — +1 point per friendly creature whose opposing slot is empty.
2. **Income** — as above.
3. **Actions** — in any order, repeatable, until the player ends the turn:
   - Play a card from hand (pay its top-left cost).
   - Use a creature's move (pay the move's cost).
   - Merge two creatures.
4. **End turn** — draw, hand-limit discard, pass.

Win at score ≥ X (currently ~10). Exact value is config.

### Creatures & moves

- Card top-left pips = **play cost**. Nothing to do with tiers.
- No auto-attack, no passive/triggered effects. **All** damage comes from activated moves.
- No summoning sickness — a creature may act the turn it is played.
- Each move may be used **once per turn**; a creature may use **any number of different
  moves** it can afford.

### Merging

- **Free action** (costs no resources).
- Legal only between two **adjacent**, **un-merged** friendly creatures.
- Result: health summed, move lists unioned, typings combined. Occupies one slot.
- A merged creature **cannot merge again**.

> ⚠️ **Design flag.** Merging is free and additive in stats, but it is **not** strictly better:
> a multi-type creature is vulnerable to 2× damage from any type matching one of its types
> (see Type effectiveness), and it consumes a board slot — which costs both a scoring body and
> its per-turn income. So the merge decision is a real tradeoff. What Phase 3 must measure is
> whether it is *priced correctly*: if the AI still merges at nearly every legal opportunity,
> the defensive downside is too cheap.
>
> The sharper remaining concern is **income scaling**: an unopposed creature both *scores* and
> *pays*, compounding tempo twice. That is the leading runaway-leader candidate. Not changing
> either now — instrumenting both first.

---

## 1. Design decisions

### Language & runtime: C# on .NET 8

Correct call, and worth stating why explicitly, since it constrains everything downstream:

- **Godot 4 has first-class C# support** via .NET 6+. Phase 4 is a client swap, not a
  rewrite — *provided* the engine takes no dependency on the console UI.
- Struct types, `Span<T>`, and array pooling let the hot search loop allocate near-zero,
  which matters when MCTS wants 10k–100k playouts per decision.
- The whole plan hinges on the engine core being a **pure class library with zero UI
  dependencies**. Console, AI, tests, and Godot all become interchangeable consumers.

**Prerequisite:** the machine currently has only a 32-bit .NET *runtime* at
`C:\Program Files (x86)\dotnet` and no SDK. Install the **.NET 8 SDK (x64)** before starting.

### Project structure

```
shapes/
├─ Shapes.Core/                 # Pure engine. No UI, no Godot, no console. The keystone.
│  ├─ Primitives/               # ResourceType, ResourcePool, PlayerId, SlotIndex
│  ├─ State/                    # GameState, PlayerState, Board, Slot, CreatureInstance
│  ├─ Actions/                  # GameAction hierarchy + legal-action generation
│  ├─ Effects/                  # Effect atoms + interpreter (see "card representation")
│  ├─ Rules/                    # RuleSet: the config surface (income, scoring, win, draw)
│  └─ Cards/                    # CardDefinition, CardDatabase loader
├─ Shapes.Content/              # JSON card data + rules presets. NOT code.
│  ├─ cards/*.json
│  └─ rulesets/*.json
├─ Shapes.Ai/                   # IS-MCTS, determinizer, playout policies, evaluators
├─ Shapes.Console/              # Text client: human v human, human v AI, AI v AI
├─ Shapes.Sim/                  # Headless batch runner → CSV/JSON stats for balancing
├─ Shapes.Tests/                # xUnit: rules, effects, determinism, invariants
└─ Shapes.Godot/                # Phase 4 only. References Shapes.Core.
```

The dependency rule, enforced by tests: **`Shapes.Core` references nothing but the BCL.**
Everything else points inward. If this holds, Phase 4 is genuinely a UI project.

### Game state representation

Two representations, one source of truth.

**Authoritative state** — plain mutable classes, readable, debuggable, easy to reason about.
Used by the console, tests, and Godot.

**Search state** — the same data laid out for speed, used only inside MCTS:

- `CreatureInstance` as a struct: `{ CardId: ushort, Health: sbyte, MaxHealth: sbyte,
  Types: TypeMask, MovesUsedThisTurn: byte (bitmask), IsMerged: bool, MergedFrom: ushort[] }`
- Board as a fixed `CreatureInstance[6]` (3 per player), slot *i* opposing slot *i+3*.
- `ResourcePool` as a 3-field struct, not a dictionary.
- Hand/deck as `List<ushort>` of card IDs.

**Apply/undo over clone.** MCTS revisits states constantly; cloning a `GameState` per node is
the usual performance killer. Instead every action produces an **undo record** and the search
walks the tree by applying and rolling back on one mutable state. Requires strict discipline —
every effect must be exactly invertible — so it is covered by a property test: *apply then
undo returns a state byte-identical to the original*. Build the naive clone path first, get it
correct, then optimize behind the same interface once tests pin the behavior.

**Determinism.** All randomness flows through a single seeded `IRandomSource` passed into the
state. No `Random.Shared`, no `DateTime.Now`, anywhere in `Shapes.Core`. This is what makes
bug reports reproducible and balance runs comparable.

### Card representation: data, not code

The most consequential structural decision. Cards are **JSON data** interpreted by a small
effect engine, *not* C# subclasses.

A hand-written subclass per card is faster to start and becomes the bottleneck by Phase 3:
every balance tweak is a recompile, and the AI can't reason about what a card does. Data-driven
cards mean the balance loop is *edit JSON → rerun sim*, with no build step, and Phase 4 can
ship card changes without a client patch.

```json
{
  "id": "circle_cadet",
  "name": "Circle Cadet",
  "kind": "creature",
  "cost": { "wheel": 1 },
  "health": 2,
  "types": ["wheel"],
  "moves": [
    { "name": "Scout",  "cost": { "wheel": 1 },
      "condition": { "op": "self_at_full_health" },
      "effects": [ { "op": "draw", "amount": 1 } ] },
    { "name": "Rebound", "cost": { "wheel": 1 },
      "effects": [ { "op": "damage", "target": "opposing", "amount": 1 },
                   { "op": "heal", "target": "self", "amount": 1 } ] }
  ]
}
```

The effect vocabulary needed to express all ~36 referenced cards:

| Category   | Ops                                                                    |
|------------|------------------------------------------------------------------------|
| Damage     | `damage`, `damage_scaled` (×health / ×count / ×hand-size)              |
| Health     | `heal`, `heal_to_full`, `set_health`, `buff_max_health`, `self_damage` |
| Cards      | `draw`, `discard`, `draw_up_to`                                        |
| Resources  | `gain_resource`, `gain_next_turn`                                      |
| Board      | `destroy`, `summon`                                                    |
| Status     | `grant_keyword` (taunt / reflect / ricochet / pierce), `stun`          |
| Modifiers  | `next_attack_bonus`, `next_damage_taken_bonus`                         |
| Control    | `conditional`, `for_each` (over creatures / hand / damaged / types)    |

Targeting is a small selector language: `self`, `opposing`, `left_friendly`,
`all_enemies`, `all_friendlies`, `chosen_enemy`, `chosen_friendly`. Selectors that require a
player choice (`chosen_*`) expand into distinct legal actions — which is exactly what MCTS
needs to enumerate.

#### Single-target rule (design constraint)

**A move or spell may require at most one chosen target.** No card may ask for a friendly
target *and then* an enemy target. This is a deliberate design constraint with three payoffs:

- **Cards read cleanly** and feel better to use — one decision, not a chain of prompts.
- **Branching stays flat.** With one choice per move, a move expands to *N* legal actions.
  Two chained choices would expand to *N×M*, and the multiplication compounds across a turn
  in which several moves are used. This is one of the cheapest available wins for search cost.
- **The UI is simpler** in Phase 4: a single target-selection state, no multi-step targeting
  flow to build or cancel out of.

Non-chosen selectors are unaffected — `all_enemies`, `self`, `opposing`, and `left_friendly`
are automatic and may freely combine with one `chosen_*` selector. The restriction applies
only to *player choices*.

**Enforced at card-load validation:** a card declaring more than one `chosen_*` selector across
its effect list fails to load. Making this a schema error rather than a convention means it
cannot silently creep back in during Phase 3 balance edits.

### Deck model

**Phases 1–3 — fixed symmetric decks.** Both players use the same deck: 1–2 copies of every
card in the set, configured in the `RuleSet`. This is deliberate for balance work — if decks
vary while cards are being tuned, card win-rate and deck-composition effects are confounded
and neither can be measured. Fixed symmetric decks isolate card balance as the only variable.

```json
{ "deckMode": "symmetric", "copiesPerCard": 2 }
```

**Phase 4 — deckbuilding.** The shipped game lets players construct decks, so the engine
should model a deck as a **list of card ids with counts** from day one, never as an implicit
"all cards" set. Constraints (deck size, max copies, any type/faction limits) belong in the
`RuleSet` so the deckbuilder validates against the same rules the engine enforces:

```json
{ "deckMode": "custom", "deckSize": 30, "maxCopiesPerCard": 2 }
```

Phase 3 gains a second use once this exists: sweeping *deck archetypes* against each other
(mono-type vs. mixed, aggro vs. control) to check no single composition dominates. Do this
only after per-card balance has settled.

### Rules as configuration

Given that income, scoring, draw, hand limit, and win condition "will likely change a lot",
they are a **`RuleSet` object loaded from JSON**, never constants in code:

```json
{
  "name": "default",
  "startingHandSize": 4, "cardsDrawnPerTurn": 1, "handLimit": 8,
  "baseIncome": { "spike": 1, "anvil": 1, "wheel": 1 },
  "incomePerCreatureType": 1,
  "pointsPerUnopposedCreature": 1,
  "scoreToWin": 10,
  "mergeEnabled": true, "mergeRequiresAdjacent": true, "mergeCostsAction": false,
  "maxMergeDepth": 2,
  "deckMode": "symmetric", "copiesPerCard": 2,
  "typeChart": {
    "weaknessMultiplier": 2.0,
    "cycle": { "spike": "wheel", "wheel": "anvil", "anvil": "spike" },
    "mergedTargetRule": "match_and_weak"
  }
}
```

Every rule the answers flagged as volatile is a field here. A balance experiment becomes a
named ruleset file, and Phase 3 can sweep them programmatically.

**Board size is deliberately not a ruleset field.** It is structural rather than a balance
knob: scoring is defined in terms of facing slots, and the flat board array is sized from it
at compile time. It lives once, as `SlotIndex.SlotsPerPlayer`. Making it runtime-configurable
would mean a second definition that could silently disagree with the compiled constant, in
exchange for flexibility the game does not need.

### Is IS-MCTS the right choice?

**Yes — with caveats worth knowing up front.**

It fits because the game is genuinely imperfect-information (hidden hands, unknown deck order),
has no strong hand-authored evaluation function, and has a branching factor that defeats
minimax. Information Set MCTS handles hidden state by *determinizing* — sampling a concrete
possible world consistent with what the player can observe — and searching that.

The caveats:

1. **Branching factor is the real risk.** Multiple moves per creature, unlimited affordable
   moves, choice targets, merges, and playing several cards per turn means a single turn is a
   *sequence* of many actions. Treat each atomic action as one tree node — never enumerate whole
   turns as single moves, which would explode combinatorially. Expect a per-node branching
   factor around 10–40, which is tractable.
2. **Strategy fusion** — the known weakness of determinized search. The AI implicitly assumes
   hidden information gets revealed, and so undervalues information-gathering and plays
   slightly too optimistically. Mitigated by **multi-observer determinization** (resample the
   hidden state per iteration rather than per search) and, if needed, by moving to IS-MCTS with
   shared information-set nodes.
3. **Determinization must respect observations.** Sampling a deck that contains a card already
   in the graveyard is a correctness bug that silently degrades play. Needs its own test suite.

**Recommendation:** build **single-observer IS-MCTS with per-iteration resampling** first —
simple, strong enough to be useful for balance, and a known quantity. Only escalate to full
multi-observer IS-MCTS if measured play strength justifies it. The interface stays `IAgent`
either way, so this is swappable.

Alternatives considered and rejected: minimax/expectimax (no good eval function, hidden state);
neural approaches à la AlphaZero (needs a trained net and far more infra — right answer if you
later want *superhuman*, wrong answer for *balance tooling now*).

### Testing strategy

xUnit in `Shapes.Tests`, written **alongside** each Phase 1 component rather than bolted on at
the end. The engine is the foundation for three consumers (console, AI, Godot); a rules bug
found in Phase 3 is expensive, because every balance number gathered before it is invalid.

Two properties of the design make testing unusually cheap, and both should be exploited:
seeded determinism means any game is reproducible from its seed, and data-driven cards mean
tests can define **synthetic cards** instead of depending on real ones.

#### Test fixtures

- **`StateBuilder`** — a fluent helper to construct exact board positions without playing
  toward them. Without this, tests degenerate into long action sequences that break whenever
  an unrelated rule changes.

  ```csharp
  var s = new StateBuilder()
      .WithRuleSet(RuleSet.Default)
      .P1(p => p.Slot(0, "circle_cadet", health: 2).Resources(spike: 3).Hand("siphon"))
      .P2(p => p.Slot(1, "monk").Score(4))
      .ActivePlayer(1)
      .Build();
  ```

- **Synthetic test cards** — a fixture-only card set (`test_deal_2`, `test_heal_1`,
  `test_draw_1`) so op tests never break when a real card is rebalanced. Real cards get their
  own separate suite.

#### 1. Core mechanics

**Resources**
- Base income grants exactly `1/1/1`.
- Per-creature income: one creature of each type → correct per-type totals.
- A **merged** creature with multiple types generates one of *each* type. (Direct consequence
  of the merge rules; easy to get wrong.)
- Paying a cost deducts exactly, from the right pools.
- An unaffordable action is **not** in the legal-action list.
- Resources never go negative; conservation holds across a full turn.
- Income respects `RuleSet` overrides (e.g. `incomePerCreatureType: 0`).

**Scoring**
- +1 per friendly creature whose opposing slot is empty.
- Creature *opposed* → no point. Three unopposed → 3 points.
- Slot *i* opposes enemy slot *i* specifically — a test with mismatched occupied slots
  (friendly slot 0, enemy slot 2) must score, catching off-by-one opposition indexing.
- Scoring happens at **start of turn**, before income and actions — a creature played this
  turn does not score until the next one.
- Win triggers at `scoreToWin`; the game ends immediately and no further actions are legal.
- Score respects `pointsPerUnopposedCreature` overrides.

**Turn structure**
- Phase order is score → income → actions → end.
- Each move usable **once per turn**; a second use of the same move is not legal.
- Different moves on one creature are all legal while affordable.
- Per-turn move-usage flags reset at turn boundaries.
- No summoning sickness: a creature played this turn can immediately act.
- Draw, hand limit, and discard behave per `RuleSet`.
- Deck exhaustion is handled deliberately (no crash) — whatever the chosen rule is.

**Board & merging**
- Playing into an occupied or out-of-range slot is illegal; board caps at 3.
- Merge sums health, unions moves, combines typing.
- Merge is legal **only** between adjacent, un-merged friendlies — non-adjacent, enemy, and
  already-merged targets each rejected.
- A merged creature cannot merge again (`maxMergeDepth`).
- Merge costs no resources and does not consume the turn.
- Death at 0 health frees the slot; a freed slot changes opposition for scoring.

**Type effectiveness**
- Each cycle edge deals 2×: Spike→Wheel, Wheel→Anvil, Anvil→Spike.
- Each reverse edge deals 1×: Wheel→Spike, Anvil→Wheel, Spike→Anvil.
- Same-type is 1× (Spike→Spike).
- **Merged targets:** Spike→Spike/Wheel is 2× (one type matches, the other is weak).
- Merged target with a match but **no** weak type is 1×.
- Merged target with a weak type but **no** match — pin the expected result explicitly; this
  is the case most likely to be read differently by a future implementer.
- Tri-type targets behave per the same rule.
- The **attacker's** type is the type of the creature using the move, not the card played.
- Spell damage with no creature source is typeless and always 1×.
- **Ordering:** a `next_attack_bonus` combined with a 2× produces the pinned number (i.e.
  `(base + bonus) × 2`, not `base × 2 + bonus`). Locks the rule against silent drift.
- Effectiveness respects `RuleSet` overrides (`weaknessMultiplier: 1.0` disables it entirely).

#### 2. Effect ops

Every op in the vocabulary gets a focused test using synthetic cards — this is the suite that
makes card data trustworthy, and the one most likely to catch silent Phase 3 corruption.

- **Damage:** exact amounts; lethal removes the creature; overkill doesn't wrap negative;
  `damage_scaled` computes correctly for ×health, ×count, ×hand-size, including the zero case.
- **Health:** `heal` caps at max; `heal_to_full` from arbitrary damage; `buff_max_health`
  raises both current and max; `self_damage` can kill its own creature.
- **Cards:** `draw` on empty deck; `draw_up_to` when already at/above the target (must be a
  no-op, not negative); `discard` from an empty hand.
- **Resources:** `gain_resource`; `gain_next_turn` lands on the *following* turn, not this one.
- **Board:** `destroy` frees the slot; `summon` into a full board.
- **Status:** each keyword (taunt/reflect/ricochet/pierce) actually alters resolution — not
  merely a flag that gets set and read by nothing.
- **Modifiers:** `next_attack_bonus` applies once and clears; `next_damage_taken_bonus` same;
  interaction of two stacked modifiers is defined.
- **Control flow:** `conditional` on both branches; `for_each` over an empty collection is a
  no-op; `for_each` counts match the board.
- **Targeting selectors:** `self`, `opposing` (with an empty opposing slot), `left_friendly`
  from slot 0 (no left neighbor — a likely crash site), `all_enemies`, `all_friendlies`; each
  `chosen_*` selector expands into one legal action **per valid target**, and zero when there
  are none.
- **Multi-effect moves** apply in declared order, and an effect that kills a creature
  mid-sequence doesn't corrupt the remaining effects.

#### 3. Card data validation

Data-driven cards need a schema guard, or a typo becomes a silent gameplay bug:

- Every card in `Shapes.Content` loads without error.
- An unknown op, selector, or resource name **fails loudly** at load, not at use.
- Costs, health, and amounts are non-negative; ids are unique.
- **Single-target rule:** a card declaring two or more `chosen_*` selectors fails to load.
  Asserted both against a deliberately-invalid fixture card and across the whole real card set.
- Deck definitions validate against `RuleSet` limits (size, max copies); a symmetric deck
  builds with the configured `copiesPerCard`.
- A generated test case per real card asserts it can be legally played from a suitable state
  and each of its moves is usable — a cheap smoke test across all ~36.

#### 4. Invariants & properties

Property-style tests over random legal play, which catch what example tests miss:

- **Apply/undo symmetry** — apply then undo yields a byte-identical state. Gates the Phase 2
  optimization; write it in Phase 1 even while still cloning.
- **Determinism** — same seed and same actions produce identical final states, twice.
- **Legal-action soundness** — every generated action applies without throwing; no legal action
  is unaffordable.
- **Termination** — random-play games always end (guards against degenerate infinite loops).
- **No illegal state** — fuzz thousands of random games asserting no negative health/resources,
  no more than 3 creatures per side, no duplicate card instances.
- **Observation leakage** (Phase 2, but specify now) — `ObservedState` never exposes the
  opponent's hand or deck order. The correctness of every AI result depends on this.

**Coverage target:** effect interpreter and rules engine are the priority; console rendering is
not worth testing. A reasonable bar is every op exercised at least once and every mechanic above
covered, rather than a blanket line-coverage percentage.

---

## 2. Phase plan

### Phase 1 — Playable engine (foundation; do not rush)

**Goal:** a complete, correct, rules-configurable game with a text interface.

Tests are written **with** each step, not after — see the testing strategy above. The step
numbers below name the suite that lands with each piece.

- [x] **1. Prerequisite:** install .NET 8 SDK (x64).
  <br>*Done: SDK 8.0.423. The machine also carried a 32-bit runtime-only .NET 5.0.12 whose
  `dotnet.exe` shadowed the SDK on PATH; uninstalled, including the files the MSI left behind.*
- [x] **2. Solution + project skeleton**, including `Shapes.Tests` from the start; enforce the
  "Core references nothing" rule with a test.
  <br>*Done: 6 projects, `Directory.Build.props` (warnings-as-errors, nullable, AOT analyzers
  on Core), `.gitignore`, README, content-copy pipeline verified. `CorePurityTests` reads
  `Shapes.Core.csproj` as XML rather than inspecting the compiled assembly — the compiler
  elides references that no code uses, so an unused-but-declared dependency is invisible at
  runtime. Verified by deliberately adding a package and confirming the test fails.*
- [x] **3. Primitives:** `ResourceType`, `ResourcePool`, `TypeMask`, `PlayerId`, `SlotIndex`.
  <br>*tests: pool arithmetic, no negatives, type-mask combination.*
  <br>*Done: 67 tests. All are immutable structs — allocation-free for the MCTS hot path, and
  a pool can never be mutated out from under a search node. Two decisions worth remembering:
  `ResourcePool.Subtract` **throws** rather than clamping (an unaffordable payment means
  legal-action generation let through an unpayable action, and clamping would hide the real
  bug upstream — `TrySubtract` covers the expected-failure path); and the slot-opposition and
  merge-adjacency rules live on `SlotIndex` rather than as index arithmetic spread across the
  engine. Both rules were mutation-tested — a mirrored `2-i` opposition and a silent clamp
  were each confirmed to fail the suite before reverting.*
- [ ] **4. `RuleSet` + JSON loading.** Every volatile rule is a field from day one.
  <br>*tests: defaults load; overrides actually take effect.*
  <br>*(partial: placeholder class and `Shapes.Content/rulesets/default.json` exist; no fields
  or loader yet.)*
- [ ] **5. State model:** `GameState` / `PlayerState` / `Board` / `CreatureInstance`; seeded
  `IRandomSource`.
  <br>*tests: `StateBuilder` fixture, determinism from seed.*
- [ ] **6. Effect interpreter** + the op vocabulary above. **The critical piece** — build it
  before entering card data, so the vocabulary is validated against real cards.
  <br>*tests: the full per-op suite against synthetic cards. Largest suite in the project;
  write each op's test as that op is implemented.*
- [ ] **7. Card JSON schema + loader + validation** (fail loudly on an unknown op; reject
  multiple `chosen_*` selectors per card).
  <br>*tests: card-data validation suite.*
- [ ] **8. Action model:** `PlayCard`, `UseMove`, `Merge`, `EndTurn` + legal-action generation.
  Legal-action generation is the single most important API in the codebase — the AI, the
  console, and the UI all consume it.
  <br>*tests: legality rules (affordability, occupied slots, merge adjacency/depth,
  once-per-turn moves), legal-action soundness property.*
- [ ] **9. Turn loop:** score → income → actions → end.
  <br>*tests: scoring, income, type-effectiveness, phase-order, win-condition suites.*
- [ ] **10. Enter all ~36 cards** from the references as JSON.
  <br>*tests: generated per-card smoke test — each card playable, each move usable.*
- [ ] **11. Console client:** render board/hands/resources, numbered legal actions, hotseat play.
- [ ] **12. Debug affordances** the PDF's "Demo reqs" asked for — adjustable score, health,
  manual creature removal, forced draws, resource editing, POV swap. Build these as **console
  commands over engine methods**, so the AI and Godot inherit them.
- [ ] **13. Fuzz harness:** thousands of seeded random-play games asserting the invariants
  (termination, no illegal state). Cheap to write once legal-action generation exists, and it
  catches the rule interactions that hand-written tests miss.
- [ ] **14. Mobile toolchain spike** (timeboxed, ~half a day, parallel to the above). Build a
  hello-world Godot 4 C# project and export it to Android and iOS. This validates the riskiest
  assumption in the whole plan — that Godot's C# export supports the target mobile platforms —
  at the point where the response to bad news is still cheap. Do **not** defer this to Phase 4.

**Exit criteria:**
- [ ] Two humans can play a full game to a win at the console.
- [ ] All ~36 cards implemented.
- [ ] A scripted game replays identically from a seed.
- [ ] Apply/undo property tests pass.
- [ ] Every effect op has a passing test.
- [ ] Fuzz harness runs clean over 10k games.

### Phase 2 — IS-MCTS AI

- [ ] **1. `IAgent` interface:** `GameAction Choose(ObservedState s, CancellationToken ct)`.
- [ ] **2. `ObservedState`** — a strict projection of `GameState` to one player's knowledge.
  If the AI can read the opponent's hand, everything downstream is invalid; enforce by test.
- [ ] **3. Determinizer:** sample a hidden state consistent with all observations (deck
  composition minus known cards, opponent hand size, revealed/discarded cards).
- [ ] **4. Baseline agents first:** `RandomAgent`, `GreedyAgent` (one-ply heuristic). These are
  the yardstick — an MCTS that cannot crush both has a bug.
- [ ] **5. IS-MCTS:** selection (UCB1), expansion, playout, backprop; per-iteration resampling.
- [ ] **6. Playout policy:** start uniform-random, then lightly heuristic (prefer damage/score
  moves) — usually a large strength gain for modest cost.
- [ ] **7. Performance:** apply/undo, node pooling, budget by time *or* iteration count.
- [ ] **8. Tuning:** exploration constant, playout depth cap, determinizations per search.

**Exit criteria:**
- [ ] IS-MCTS beats `RandomAgent` >95% over 500+ seeded games.
- [ ] IS-MCTS beats `GreedyAgent` >80% over 500+ seeded games.
- [ ] A decision at a realistic budget completes in target wall-clock (suggest ≤2s desktop).
- [ ] `ObservedState` provably leaks no hidden information.

### Phase 3 — AI-driven balance

- [ ] **1. `Shapes.Sim`:** headless batch runner, N games, parallel, seeded, → CSV/JSON.
- [ ] **2. Metrics:** win rate by player-1/2 (first-player advantage), average game length,
  score curves, per-card play/win-rate correlation, per-move usage frequency, merge frequency,
  resource starvation/flooding, and how often games end by score-out vs. board wipe.
- [ ] **3. Answer the two flagged questions first:**
  - [ ] (a) Does the AI ever *decline* a legal merge? If it merges at nearly every opportunity,
    the multi-type 2× vulnerability is priced too cheaply to make merging a real decision.
  - [ ] (b) How strongly does unopposed-creature income compound into a runaway lead? An
    unopposed creature both scores *and* pays, so tempo advantage compounds twice.
- [ ] **4. Sweep:** parameterize over rulesets and card stat variants; run the matrix; rank
  outliers.
- [ ] **5. Iterate:** adjust JSON, rerun, compare. Keep a `balance/` log of each experiment and
  result so changes are traceable.
- [ ] **6. Watch for:** never-played cards, auto-include cards, degenerate loops, first-player
  advantage beyond ~55%, games that never terminate.
- [ ] **7. Archetype sweeps** (once `deckMode: "custom"` exists): mono-type vs. mixed, aggro vs.
  control. Do this only after per-card balance has settled.

**Exit criteria:**
- [ ] No card with an extreme play-rate outlier (never-played or auto-include).
- [ ] First-player advantage within a few points of even.
- [ ] Game length in a target band.
- [ ] Merge tradeoff confirmed as a real decision.
- [ ] Income compounding confirmed not to produce runaway leads.

### Phase 4 — Godot client (desktop + mobile)

**Target platforms: Windows/macOS/Linux desktop and iOS/Android mobile.** Shipping to both from
one codebase is very achievable for a turn-based card game — there is no realtime input or
performance pressure — but it constrains layout and input from the first scene, so design for
it up front rather than retrofitting.

- [ ] **1. Godot 4.x with .NET;** add `Shapes.Godot` referencing `Shapes.Core` **unchanged**.
- [ ] **2. Adapter layer:** engine events → visual updates. Engine stays authoritative and
  UI-agnostic; the UI *never* mutates state directly, only submits actions.
- [ ] **3. Responsive layout from scene one.** A 3v3 board, two hands, and resource counters
  must fit both a wide desktop window and a tall phone screen. Use Godot's anchor/container
  system with distinct portrait and landscape arrangements; never hard-code pixel positions.
  Retrofitting responsive layout onto fixed-position scenes is the expensive path.
- [ ] **4. Touch-first input**, with mouse as the superset. Tap-to-select-then-tap-to-target
  works identically with a mouse; drag-and-drop needs separate handling on both. Hit targets
  sized for fingers (~44px minimum). No hover-dependent information — a phone has no hover, so
  card details need tap-to-inspect or long-press, not a hover tooltip.
- [ ] **5. Scenes:** board, slots, hand, resource counters, score track, card detail.
- [ ] **6. Art + animation:** real card art replacing placeholders; animation for
  play/move/merge/score/destroy.
- [ ] **7. Target selection UI** over the same `chosen_*` legal actions — single-target only
  (see the single-target rule), so this is one selection state with no chaining.
- [ ] **8. AI opponent** via the existing `IAgent` — difficulty = search budget. **Run search
  off the main thread** and cap the budget on mobile; a 2s desktop budget is far more expensive
  on a phone CPU and will drain battery and stutter the UI if run inline.
- [ ] **9. Deckbuilder** (`deckMode: "custom"`): browse the card set, build and save decks,
  validate against `RuleSet` limits. Reuses the engine's validation so the UI cannot construct
  a deck the engine would reject.
- [ ] **10. Persistence:** saved decks, settings, progress — Godot `user://`, which maps
  correctly on both desktop and mobile sandboxes.
- [ ] **11. Polish:** sound, transitions, menus.
- [ ] **12. Export pipeline:** desktop builds, plus Android (keystore signing) and iOS (Xcode,
  Apple developer account). Established early via the Phase 1 step 14 spike; this step is
  productionizing it, not discovering it.

**Mobile-specific constraints worth knowing before Phase 4 starts:**
- Godot's .NET/C# export to **iOS and Android requires Godot 4.2+** and has historically been
  less mature than GDScript export. Verify the current toolchain supports C# mobile export
  **before** committing — this is the one assumption in the plan that could force a rewrite, so
  validate it with a hello-world mobile build during Phase 1 rather than discovering it late.
- Keep `Shapes.Core` free of any dependency that won't AOT-compile; iOS requires AOT, which
  rules out runtime reflection-heavy patterns. This retroactively reinforces the "Core
  references nothing but the BCL" rule — favor source-generated or explicit JSON
  deserialization over reflection-based binding.

**Exit criteria:**
- [ ] Full game playable with visuals against the Phase 2 AI on a desktop build.
- [ ] The same, on a **physical mobile device**.
- [ ] Deckbuilder functional and validating against the engine's own rules.
- [ ] `Shapes.Core` unmodified from Phase 3.

---

## 3. Cross-cutting principles

- **Core stays pure.** No UI, no engine-specific types, no I/O in `Shapes.Core`. Test-enforced.
  Also **AOT-safe** — no reflection-heavy binding, since iOS export requires AOT compilation.
- **One target maximum.** No card requires more than a single player-chosen target. Keeps
  branching flat for the AI, cards readable, and the targeting UI a single state.
- **Data over code.** Cards and rules are JSON. Balance changes must never require a recompile.
- **Determinism everywhere.** One seeded RNG source; any game reproducible from its seed.
- **Legal-action generation is the contract.** Console, AI, and Godot all consume one API.
- **Test the invariants, not just the paths.** Apply/undo symmetry, resource conservation,
  no negative health, observation never leaks hidden state.
- **Build the naive version first.** Correct, then fast, behind a stable interface.
