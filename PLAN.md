# Shapes — Development Plan

A 2-player, turn-based, board-and-cards game. Four phases: playable engine → IS-MCTS AI →
AI-driven balance → Godot client.

## Status

| Phase                          | Progress   |
|--------------------------------|------------|
| 1 — Playable engine            | 13 / 13    |
| 2 — IS-MCTS AI                 | 2 / 9      |
| 3 — AI-driven balance          | 0 / 7      |
| 4 — Godot client               | 0 / 12     |

615 tests passing.

**Phase 1 is complete.** Step 1.13's mobile spike confirmed Godot 4's C#/.NET export works on a
physical Android device.

**Phase 2 is underway.** Step 2.1 landed the `IAgent` seam (`Shapes.Ai/Agents/`). Step 2.2 landed
`ObservedState`, narrowing `AgentContext.State` from the full `GameState` to a projection that
structurally cannot leak the opponent's hand contents or deck order — pinned by
`Shapes.Tests/Agents/ObservedStateTests.cs`.

**Next up: step 2.3** — the determinizer: sample a hidden state consistent with all observations
(deck composition minus known cards, opponent hand size, revealed/discarded cards).

### Common commands

Run these from the repo root (`shapes/`, where `Shapes.sln` lives).

| What                          | Command                                              |
|-------------------------------|-------------------------------------------------------|
| Build everything              | `dotnet build`                                       |
| Run all tests                 | `dotnet test Shapes.Tests/Shapes.Tests.csproj`       |
| Run one test by name          | `dotnet test Shapes.Tests/Shapes.Tests.csproj --filter "FullyQualifiedName~TestMethodName"` |
| Play the game (console)       | `dotnet run --project Shapes.Console`                |

`dotnet build` compiles every project in the solution and reports errors/warnings — run it after
any code change to check nothing broke, before bothering with tests. `dotnet test` builds first
automatically, so a separate build step isn't required before testing.

The `--filter` flag on `dotnet test` accepts a substring match against test names — useful when
iterating on one failing test instead of waiting for all ~570 to run. Drop the whole `--filter`
argument to run everything.

`dotnet run --project Shapes.Console` starts the hotseat text client (step 1.11): it asks for a
random seed, then two players take turns picking numbered actions in the same terminal.

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

> **Merging can increase vulnerability.** A pure Spike creature takes 1× from Spike, but a
> Spike/Wheel creature takes 2×. This offsets the "free strictly-better action" concern below —
> merging trades a defensive profile for stats and moves. Phase 3 should measure whether that
> tradeoff is priced correctly.

**Type comes from resource cost, always** — never "no creature source means typeless." A move's
attack type is the resource type of the *move's own cost*; a spell's attack type is the resource
type of the *spell's own cost*; a creature's defensive type is the resource type(s) of its *play
cost* (a creature still declares `types` explicitly in card data, for readability, but it must
match its cost — enforced at load). A move or spell with a free (zero-cost) cost has no attack
type and deals flat, unmultiplied damage — that is the only "typeless" case, and it is a property
of the cost being empty, not of the source being a spell.

Effectiveness applies after flat modifiers (`next_attack_bonus`) and before clamping — pinned by
test, since it changes numbers whenever both a bonus and a 2× apply.

Implemented as a `TypeChart` on the `RuleSet`, so the multiplier and cycle can be varied in
balance sweeps. The merged-target rule itself is hard-coded (an alternative, "2× on any weak
type, match or not," was considered and rejected — nothing suggests the game wants it; an hour's
work to add if Phase 3 finds mixed merges underpriced).

### Income

Each turn a player gains:
- **1 of each resource** (flat `1/1/1`), plus
- **+1 resource per creature controlled**, of that creature's type. A merged creature has
  multiple types and generates one of *each*.

### Board

3 slots per player, arranged facing the opponent's 3 slots. Slot *i* opposes enemy slot *i*.

### Turn structure

1. **Score** — +1 point per friendly creature whose opposing slot is empty.
2. **Income** — as above.
3. **Draw** — draw `cardsDrawnPerTurn`, burning any card that arrives into a full hand.
4. **Actions** — in any order, repeatable, until the player ends the turn:
   - Play a card from hand (pay its top-left cost).
   - Use a creature's move (pay the move's cost).
   - Merge two creatures.
   - Discard a chosen card, when a card effect owes one (see below).
5. **End turn** — pass. Nothing else: no draw, no cleanup discard.

Win at score ≥ X (currently ~10). Exact value is config.

**Drawing is at turn START, not turn end** (Hearthstone's sequencing). A card drawn at the start
of a turn is playable during it, which makes the draw a live decision rather than a deposit for
next turn. The starting player draws on turn one too, on top of the opening `startingHandSize`
hand — turn one is not a special case. `GameState.AdvanceToActions()` runs score → income → draw
as one entry point, so no caller sequences the phases itself.

### Drawing, discarding, and the hand limit

Two rules that look similar and are deliberately **not** the same:

| Situation                                  | Who decides | What happens                          |
|--------------------------------------------|-------------|---------------------------------------|
| Card drawn into a full hand (**overdraw**) | Nobody      | The drawn card is **burned** — straight to discard |
| A card effect says "**discard N**"         | The player  | They choose which N, one card at a time |

The asymmetry is the Hearthstone/Slay-the-Spire rule, and it is the point: a full hand is a real
cost you cannot dodge by pitching a worse card, while a card that *asks* you to discard is asking
a real question. Collapsing the two in either direction is the plausible regression, so both
halves are pinned by test.

**Overdraw burns the card just drawn**, not an older one — that is what makes it a burn rather
than a hand-limit discard the player might reasonably have wanted to direct. It is logged as a
`CardBurned` turn event so Phase 3 can measure how often the hand limit actually costs a card
rather than inferring it from hand sizes.

**The hand limit is a property of drawing, not of the turn step.** Every draw goes through
`GameState.DrawWithBurn` — the turn draw and every card effect that draws (`draw`, `draw_scaled`,
`draw_up_to`) — so a card reading "draw 3" into a full hand burns all three. Routing only the
turn draw through the check was the first implementation, and the fuzz harness caught it within
two seeds: card-effect draws pushed hands to 10 against a limit of 8.

**Chosen discard is a pending state, not a prompt.** An effect cannot stop mid-resolution to ask
a question — every choice in this engine is a distinct legal action. So the `discard` op records
a debt on `GameState.PendingDiscards`, the effect list finishes resolving normally (a "discard 1,
then gain 3 spike" move pays out its resources immediately), and the action generator then offers
one `DiscardAction` per distinct card in hand until the debt clears. While a debt stands it
suppresses **every** other action including `EndTurn` — a player who could simply end the turn
would never pay.

**One card at a time.** Hand `[A,B,C,D]` owing 3 offers 4 options, then 3, then 2, then ordinary
play resumes. Never one action enumerating all `4-choose-3` combinations: branching stays linear
in hand size rather than binomial, the same reasoning that makes an MCTS node one atomic action
rather than a whole turn, and the console gets a flat numbered list instead of a combination
picker.

**An unpayable debt is clamped, not carried.** "Discard 2" with one card in hand costs one card
and forgets the rest. A debt outliving its turn would let a card silently tax a later one, and —
more urgently — since the generator offers *nothing but* discards while a debt stands, an
unpayable one would return an empty legal-action list and deadlock the game. The executor clamps
to hand size the moment the debt is incurred, which is what keeps `ActionGenerator`'s
non-emptiness invariant true without the generator having to mutate state. Clamping happens after
the *whole* effect list resolves, not inside the op, since a later effect in the same list may
draw cards the player can then afford to pay with.

### Creatures & moves

- Card top-left pips = **play cost**. Nothing to do with tiers.
- No auto-attack, no passive/triggered effects. **All** damage comes from activated moves.
- No summoning sickness — a creature may act the turn it is played.
- Each move may be used **once per turn**; a creature may use any number of different moves it
  can afford.

### Merging

- **Free action** (costs no resources).
- Legal only between two **adjacent**, **un-merged** friendly creatures.
- Result: health summed, move lists unioned, typings combined. Occupies one slot.
- A merged creature **cannot merge again**.

> ⚠️ **Design flag.** Merging is free and additive in stats, but not strictly better: a
> multi-type creature is 2×-vulnerable to any type matching one of its types, and it consumes a
> board slot — costing both a scoring body and its per-turn income. Phase 3 must measure whether
> that tradeoff is priced correctly (if the AI merges at nearly every opportunity, it's too
> cheap).
>
> The sharper concern is **income scaling**: an unopposed creature both *scores* and *pays*,
> compounding tempo twice — the leading runaway-leader candidate. Not changing either now;
> instrumenting both first.

---

## 1. Design decisions

### Language & runtime: C# on .NET 8

- **Godot 4 has first-class C# support** via .NET 6+. Phase 4 is a client swap, not a
  rewrite — provided the engine takes no dependency on the console UI.
- Struct types, `Span<T>`, and array pooling let the hot search loop allocate near-zero, which
  matters when MCTS wants 10k–100k playouts per decision.
- The engine core is a **pure class library with zero UI dependencies**. Console, AI, tests, and
  Godot are all interchangeable consumers.

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

**Authoritative state** — plain mutable classes, readable, debuggable. Used by console, tests,
and Godot.

**Search state** — the same data laid out for speed, used only inside MCTS:

- `CreatureInstance` as a struct: `{ CardId: ushort, Health: sbyte, MaxHealth: sbyte,
  Types: TypeMask, MovesUsedThisTurn: byte (bitmask), IsMerged: bool, MergedFrom: ushort[] }`
- **Moves are not stored on the creature** — they're static card data, so storing them
  per-instance would duplicate the same list across every board copy and MCTS clone. A
  creature's move list is `MergedFrom.SelectMany(id => cards[id].Moves)`, which is why
  `MergedFrom` is an ordered list rather than a set: `MovesUsedThisTurn` indexes into that
  concatenation, so the order is a contract two overlapping source cards' moves would otherwise
  violate.
- Board as a fixed `CreatureInstance[6]` (3 per player), slot *i* opposing slot *i+3*.
- `ResourcePool` as a 3-field struct, not a dictionary; hand/deck as `List<ushort>` of card IDs.

**Apply/undo over clone.** MCTS revisits states constantly; cloning a `GameState` per node is
the usual performance killer. Instead every action should eventually produce an **undo record**
so the search can apply/rollback on one mutable state — this requires every effect to be exactly
invertible, so it's gated by a property test (apply then undo → byte-identical state). Build the
naive clone path first, get it correct, optimize behind the same interface once tests pin the
behavior (Phase 2, step 2.8).

**Determinism.** All randomness flows through a single seeded `IRandomSource`. No
`Random.Shared`, no `DateTime.Now`, anywhere in `Shapes.Core`. Makes bug reports reproducible and
balance runs comparable.

### Card representation: data, not code

The most consequential structural decision. Cards are **JSON data** interpreted by a small
effect engine, not C# subclasses — a hand-written subclass per card becomes the Phase 3
bottleneck (every balance tweak is a recompile, and the AI can't reason about card text).
Data-driven cards make the balance loop *edit JSON → rerun sim*, and let Phase 4 ship card
changes without a client patch.

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
      "condition": { "op": "creature_state", "target": "self", "check": "full_health" },
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
| Damage     | `damage`, `damage_scaled` (×health / ×count / ×hand-size / ×hand-composition / ×resource-held / ×a second creature's health via `health_source`, each with an optional `divisor`) |
| Health     | `heal`, `heal_scaled`, `heal_to_full`, `set_health`, `buff_max_health`, `self_damage` |
| Cards      | `draw`, `draw_scaled` (×creatures destroyed this turn), `discard` (player-chosen — records a pending debt), `draw_up_to` |
| Resources  | `gain_resource`, `gain_resource_scaled`, `gain_next_turn`              |
| Board      | `destroy`, `destroy_refund_cost`, `summon`                             |
| Status     | `grant_keyword` (taunt/reflect/ricochet, optional until-next-turn expiry), `stun`, `on_next_damage_taken`, `on_next_ricochet` (one-shot reactive triggers) |
| Modifiers  | `attack_buff` (persistent, cumulative), `next_attack_bonus`, `next_damage_taken_bonus` |
| Control    | `conditional` (predicate: `creature_state(target, check)`), `for_each` (over creatures/hand, optional filter) |

Targeting is a small selector language: `self`, `opposing`, `left_friendly`, `right_friendly`,
`all_enemies`, `all_friendlies`, `chosen_enemy`, `chosen_friendly`. Selectors requiring a player
choice (`chosen_*`) expand into distinct legal actions — exactly what MCTS needs to enumerate.
`damage_scaled`/`heal_scaled` take an optional second, independent selector (`health_source`)
when the amount's source and the target are different creatures — needed to express "damage an
enemy equal to a *third* creature's health."

#### Single-target rule (design constraint)

**A move or spell may require at most one chosen target.** No card asks for a friendly target
*and then* an enemy target. Deliberate:

- Cards read cleanly — one decision, not a chain of prompts.
- **Branching stays flat.** One choice per move expands to *N* legal actions; two chained
  choices would expand to *N×M*, compounding across a turn. Cheap win for search cost.
- The Phase 4 UI is a single target-selection state, no multi-step targeting flow.

Non-chosen selectors are unaffected and may freely combine with one `chosen_*` selector — the
restriction is only on player choices. **Enforced at card-load validation**: a card declaring
more than one `chosen_*` selector fails to load, so it can't silently creep back in during
Phase 3 balance edits.

### Deck model

**Phases 1–3 — fixed symmetric decks.** Both players use the same deck (1–2 copies of every
card, via `RuleSet`). Deliberate for balance work: varying decks while cards are being tuned
confounds card win-rate with deck-composition effects.

```json
{ "deckMode": "symmetric", "copiesPerCard": 2 }
```

**Phase 4 — deckbuilding.** The engine models a deck as a **list of card ids with counts** from
day one, never an implicit "all cards" set, so deckbuilder constraints (size, max copies) live in
`RuleSet` and validate against the same rules the engine enforces:

```json
{ "deckMode": "custom", "deckSize": 30, "maxCopiesPerCard": 2 }
```

Phase 3 gains archetype sweeps (mono-type vs. mixed, aggro vs. control) once this exists — only
after per-card balance has settled.

### Rules as configuration

Income, scoring, draw, hand limit, and win condition are volatile, so they live in a **`RuleSet`
object loaded from JSON**, never as constants:

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
    "cycle": { "spike": "wheel", "wheel": "anvil", "anvil": "spike" }
  }
}
```

A balance experiment becomes a named ruleset file; Phase 3 can sweep them programmatically.

**Board size is deliberately not a ruleset field** — it's structural, not a balance knob (scoring
is defined in terms of facing slots, and the board array is sized from it at compile time). Lives
once, as `SlotIndex.SlotsPerPlayer`.

### Is IS-MCTS the right choice?

**Yes — with caveats.** The game is genuinely imperfect-information (hidden hands, unknown deck
order), has no strong hand-authored evaluation function, and a branching factor that defeats
minimax. Information Set MCTS handles hidden state by *determinizing* — sampling a concrete
possible world consistent with what the player can observe — and searching that.

1. **Branching factor is the real risk.** Multiple moves per creature, choice targets, merges,
   and several cards per turn means a turn is a *sequence* of many actions. Treat each atomic
   action as one tree node — never enumerate whole turns as single moves. Expect a per-node
   branching factor of 10–40, which is tractable.
2. **Strategy fusion** — determinized search implicitly assumes hidden information gets
   revealed, undervaluing information-gathering. Mitigated by **multi-observer determinization**
   (resample per iteration, not per search) and, if needed, shared information-set nodes.
3. **Determinization must respect observations** — sampling a deck containing a card already in
   the graveyard is a correctness bug that silently degrades play. Needs its own test suite.

**Recommendation:** build **single-observer IS-MCTS with per-iteration resampling** first —
simple, strong enough for balance work, a known quantity. Escalate to multi-observer only if
measured play strength justifies it; the `IAgent` interface stays swappable either way.

Rejected alternatives: minimax/expectimax (no eval function, hidden state); AlphaZero-style
neural approaches (right answer for *superhuman*, wrong answer for *balance tooling now*).

### Testing strategy

xUnit in `Shapes.Tests`, written **alongside** each Phase 1 component, not bolted on after — a
rules bug found in Phase 3 invalidates every balance number gathered before it.

Two properties keep testing cheap: seeded determinism means any game is reproducible from its
seed, and data-driven cards mean tests can define **synthetic cards** instead of depending on
real ones.

#### Test fixtures

- **`StateBuilder`** — fluent helper to construct exact board positions without playing toward
  them, so tests don't degenerate into long action sequences that break on unrelated rule changes.
  ```csharp
  var s = new StateBuilder()
      .WithRuleSet(RuleSet.Default)
      .P1(p => p.Slot(0, "circle_cadet", health: 2).Resources(spike: 3).Hand("siphon"))
      .P2(p => p.Slot(1, "monk").Score(4))
      .ActivePlayer(1)
      .Build();
  ```
- **Synthetic test cards** (`test_deal_2`, `test_heal_1`, …) — a fixture-only set so op tests
  never break when a real card is rebalanced. Real cards get their own separate suite.

#### Coverage areas

- **Resources** — exact income (base + per-creature, including merged multi-type), exact cost
  deduction, unaffordable actions excluded from legal-action list, no negative resources.
- **Scoring** — opposition is per-slot-index (not mirrored), scoring happens at start-of-turn
  before income/actions, win triggers immediately at `scoreToWin`.
- **Turn structure** — phase order score→income→draw→actions→end, once-per-turn move usage, no
  summoning sickness, deck exhaustion handled without a crash.
- **Drawing & discarding** (`Shapes.Tests/Mechanics/DiscardTests.cs`) — draw lands at turn start
  and is playable that turn; overdraw burns the *drawn* card and asks nothing; a card effect's
  `discard N` gates every other action until paid, narrows one card at a time, and is clamped
  when unpayable. Two fuzz invariants back these at scale: no hand ever exceeds the limit, and a
  standing debt is always payable (an unpayable one would deadlock the generator). A third fuzz
  test asserts the pending-discard path is actually *reached*, so the invariants can't pass
  vacuously.
- **Board & merging** — slot/range legality, adjacency + un-merged-only + depth cap, merge is
  free and doesn't consume the turn, death frees the slot and changes opposition.
- **Type effectiveness** — every cycle edge and reverse edge, merged-target match+weak vs.
  match-only vs. weak-only (the last is the case most likely misread), spell damage always 1×,
  bonus-then-multiplier ordering pinned, `weaknessMultiplier: 1.0` disables the system.
- **Effect ops** — every op in the vocabulary table gets a focused test: exact amounts, edge
  cases (lethal, overkill, empty deck/hand, zero counts), each status keyword actually altering
  resolution (not just a flag nothing reads), multi-effect moves applying in order even when an
  earlier effect kills the target of a later one.
- **Card data validation** — every shipped card loads without error; unknown op/selector/resource
  fails loudly at load; single-target rule enforced across the whole set; a generated smoke test
  per real card asserts it's playable and every move usable.
- **Invariants & properties** (random legal play) — apply/undo symmetry (byte-identical state,
  written in Phase 1 even while still cloning — gates the Phase 2 optimization), determinism
  (same seed replays identically), legal-action soundness (every generated action applies without
  throwing), termination, no illegal state at scale. Observation leakage (`ObservedState` never
  exposes the opponent's hand/deck order) is specified now but implemented in Phase 2.

**Coverage target:** effect interpreter and rules engine are the priority; console rendering
isn't worth testing. Bar: every op exercised at least once and every mechanic above covered,
rather than a blanket line-coverage percentage.

---

## 2. Phase plan

### Phase 1 — Playable engine (foundation; do not rush)

**Goal:** a complete, correct, rules-configurable game with a text interface. Tests are written
**with** each step, per the testing strategy above.

- [x] **1. Prerequisite:** install .NET 8 SDK (x64).
- [x] **2. Solution + project skeleton**, including `Shapes.Tests`; `Shapes.Core` references
  nothing but the BCL, enforced by a test that reads the `.csproj` as XML (a compiled-assembly
  check would miss an unused-but-declared dependency).
- [x] **3. Primitives:** `ResourceType`, `ResourcePool`, `TypeMask`, `PlayerId`, `SlotIndex`. All
  immutable structs. `ResourcePool.Subtract` throws rather than clamps — an unaffordable payment
  means legal-action generation let through something unpayable, and clamping would hide that
  bug. Slot-opposition and merge-adjacency rules live on `SlotIndex` rather than as scattered
  index arithmetic.
- [x] **4. `RuleSet` + JSON loading.** Validates in its constructor (fails at load, not hours
  into a sim run). Source-generated `JsonSerializerContext` (AOT-safe). Unknown properties are
  **rejected** — a typo like `scoreToWinn` would otherwise silently fall back to the default.
- [x] **5. State model:** `GameState`/`PlayerState`/`Board`/`CreatureInstance` as mutable classes
  with `Clone()` (struct-in-flat-array layout deferred to Phase 2's apply/undo work). Seeded
  `IRandomSource` is a hand-rolled xorshift64* (not `System.Random`) for cross-platform
  stability, with `Fork()` so cloning a state for search never advances the real game's RNG
  stream.
- [x] **6. Effect interpreter** + op vocabulary — built before card data so the vocabulary is
  validated against real cards. `EffectRegistry` is the single source of truth for "what ops
  exist," read by both the interpreter's dispatch and the schema validator. Status keywords:
  **taunt** restricts `chosen_enemy` to taunted creatures for move-sourced effects only; **reflect**
  is one-shot (redirects the next creature-sourced hit back to the attacker, then clears);
  **ricochet** is standing and directional. Damage resolution order is pinned:
  `(base + next_attack_bonus + next_damage_taken_bonus) × typeMultiplier`.
- [x] **7. Card JSON schema + loader + validation.** Validation lives in a separate
  `CardValidator` (not the constructor) since the single-target rule spans every effect of every
  move. Effect *args* can't use "reject unknown properties" (the schema is op-defined, not
  fixed) — a misspelled argument is caught by the op at use, not at load, which is what makes the
  step 1.10 smoke test load-bearing. Validation walks the whole effect tree including
  `conditional`/`for_each` branches. Decided here: a move's cost must be single-type (attack type
  derives from it); creatures may not declare top-level `effects` (no passive triggers exist).
- [x] **8. Action model:** `PlayCard`, `UseMove`, `Merge`, `Discard`, `EndTurn` + legal-action
  generation —
  the single most important API in the codebase (console, AI, and UI all consume it). Actions
  are immutable with value equality (MCTS dedupes them; reference equality would split a node's
  stats across identical children). Generator/executor is a one-way contract: the generator
  decides legality, the executor assumes it and re-checks nothing. Decided here: an unmet move
  condition makes the move illegal outright (not a legal no-op), and a targeted move with no
  valid target isn't generated at all. Merge is generated in both directions per eligible pair,
  since the result occupies the *target* slot and that changes scoring. `DiscardAction` was added
  later (during Phase 2) when chosen discard replaced the front-of-hand placeholder — it is the
  one action kind that *suppresses* the others rather than adding to them.
- [x] **9. Turn loop:** score → income → draw → actions → end, folded into one entry point
  (`GameState.AdvanceToActions()`) so callers can't forget to run scoring/income/draw before
  acting — previously every `EndTurn()` caller had to remember that itself. The win check sits
  between scoring and income, so a scoring play that wins the game skips that turn's income *and*
  its draw. (Draw moved from turn end to turn start during Phase 2 — see "Drawing, discarding,
  and the hand limit" above.)
- [x] **10. Enter all ~36 cards** from `references/oldcardsdata.txt`. About a third of the set
  needed new mechanics: `PlayCost`/`AttackBuff`/taunt-expiry/reactive-trigger fields on
  `CreatureInstance`; a turn-scoped `GameState.TurnEvents` log (feeds "draw per creature
  destroyed this turn"); the bespoke `self_at_full_health` condition generalized into
  `creature_state(target, check)`; `EffectContext.HandComposition` (precomputed by the caller,
  not fetched by the op, to keep `Effects` free of a `Cards` reference) for "gain resources per
  matching card in hand"; and a `health_source` selector argument for "damage an enemy equal to a
  *third* creature's health," which needs three independent slots (attacker/source/victim) that
  `target` alone can't express. The generated per-card smoke test (`CardSmokeTests`) is what
  catches misspelled effect *arguments*, which `CardValidator` structurally cannot.
- [x] **11. Console client:** render board/hands/resources, numbered legal actions, hotseat play.
  `Shapes.Console/Program.cs` + `BoardView.cs`; `GameAction.Describe()` already provides
  human-readable action text. Named the renderer `BoardView` rather than `Board` (collides with
  `Shapes.Core.State.Board`), and calls `System.Console` explicitly (the project's own namespace
  shadows it). No new engine surface needed. Verified with a scripted game to a real win.
- [x] **12. Fuzz harness:** thousands of seeded random-play games asserting termination and no
  illegal state. `Shapes.Tests/Fuzz/FuzzHarnessTests.cs` — 10,000 games total, against the
  **real** shipped card set (not synthetic test cards, so real cross-card rule interactions are
  exercised), asserting `GameState.IsOver` is actually reached rather than just capping the loop.
  Runs in ~7s.
- [x] **13. Mobile toolchain spike** (Godot 4 hello-world → Android export). Confirmed working
  end-to-end on a physical device (Godot 4.5.1, Vulkan/Forward Mobile). Two things worth
  remembering for Phase 4:
  Godot's Android export **templates** for this version require the **.NET 9 SDK** even though
  the project itself can target `net8.0` — install .NET 9 alongside .NET 8 (confirmed
  side-effect-free for the rest of the repo, which stays pinned to `net8.0` via
  `Directory.Build.props`); and Editor Settings → Export → Android needs its **Java SDK Path**
  and **Android SDK Path** set explicitly (Android Studio's bundled JBR, not an older standalone
  JDK, satisfies the JDK 17+ requirement). One debugging trap: an apparent "blank screen" bug was
  actually a stale APK — `adb shell monkey` relaunches whatever's already installed, so a rebuild
  needs `adb install -r` before relaunching, not just a re-export. The spike lived in its own
  standalone Godot project, never touching `Shapes.Core` or anything else in this repo.

**Exit criteria:**
- [x] Two humans can play a full game to a win at the console.
- [x] All ~36 cards implemented.
- [x] A scripted game replays identically from a seed.
- [x] Apply/undo property tests pass.
- [x] Every effect op has a passing test.
- [x] Fuzz harness runs clean over 10k games.

### Phase 2 — IS-MCTS AI

- [x] **1. `IAgent` interface:** `GameAction Choose(AgentContext ctx, CancellationToken ct)`.
  Signature changed from the plan's original `Choose(ObservedState s, ...)`: an agent also needs
  the legal-action list and the card database, and having each agent call `ActionGenerator`
  itself would be a second definition of legality drifting from the engine's. `AgentContext`
  bundles observation + legal actions + cards, so step 2.2 narrows *one property* to
  `ObservedState` rather than changing `IAgent` and every call site. `AgentContext.State` is the
  full `GameState` until then — deliberate and temporary, since building the interface against a
  type that doesn't exist yet leaves neither it nor its first implementation compilable.
  `RandomAgent` (formally step 2.4) landed here as the reference implementation, so the seam is
  exercised rather than a dead file. The contract is pinned by
  `Shapes.Tests/Agents/AgentContractTests.cs` as three clauses tested against `IAgent`, not
  against one implementation — chosen action is legal and applicable, choosing never mutates the
  caller's state, and same seed → same decisions (with a different-seeds test guarding the
  degenerate "always return `LegalActions[0]`" way that could pass).
- [x] **2. `ObservedState`** — a strict projection of `GameState` to one player's knowledge.
  If the AI can read the opponent's hand, everything downstream is invalid; enforce by test.
  Narrows `AgentContext.State` (see step 2.1); **engine-side only** — no client changes. The
  console keeps rendering from `GameState`, so this step cannot alter what a human sees; that is
  step 2.5's separate decision.

  Lives in `Shapes.Ai/Agents/ObservedState.cs`, not `Shapes.Core` — the dependency-direction
  tests (`CorePurityTests`) would fail a new public type under an unlisted namespace, and the
  point of the projection is to sit on the AI side of the seam anyway. Three types: `ObservedState`
  (board, phase/turn bookkeeping, pending-discard count — all public information), `ObservedSelf`
  (own hand and discard in full, own deck as `DeckComposition` + `DeckSize`), and
  `ObservedOpponent` (hand and deck reduced to **counts** only, everything else — discard,
  resources, score — visible in full, matching what's physically visible across a table). Hand
  size stays knowable on both sides because step 2.3's determinizer needs it to sample a
  correctly-sized hand.

  **Deck order is hidden even from its own owner.** The first pass exposed `Self.Deck` as the
  real ordered list on the theory that a player can see their own deck; that's wrong for this
  game — nothing in the ruleset lets a player see their own next draw, so leaking it would let a
  search built on `ObservedState` quietly read its own future draws. `Self.DeckComposition` is
  the real deck's contents re-sorted into a fixed, draw-order-independent order (alphabetical by
  card id) — composition is legitimately knowable (a player can count their own remaining deck),
  order is not, for either side. Deliberately carries no `GameState` or `IRandomSource` reference
  anywhere in its public surface, so there's no path from an `ObservedState` back to the hidden
  data or the live RNG stream — a determinizer builds its own fresh source rather than reading
  the real one.

  `AgentContext`'s constructor now takes either a `GameState` (the common path — it builds the
  `ObservedState` internally) or an `ObservedState` directly (for tests constructing one side of
  a hidden-information position without a full game). `RandomAgent` needed no changes: it only
  ever reads `context.LegalActions`, never `context.State`.

  Pinned by `Shapes.Tests/Agents/ObservedStateTests.cs`: opponent hand/deck are counts, not
  content, on both an instance-value basis and a "no member of the wrong type exists" reflection
  check (so a future accidental leak has to add the leaking property visibly, not fall through an
  existing one); own hand/deck/discard visible in full; the board is the identical object
  regardless of observer; observing from the other side flips which hand is hidden; no public
  member anywhere in the three types returns `GameState` or `IRandomSource`.
- [ ] **3. Determinizer:** sample a hidden state consistent with all observations (deck
  composition minus known cards, opponent hand size, revealed/discarded cards).
- [ ] **4. Baseline agents first:** `RandomAgent`, `GreedyAgent` (one-ply heuristic). These are
  the yardstick — an MCTS that cannot crush both has a bug.
- [ ] **5. Console hidden-hand mode** (`--reveal` flag, default **off**). Today `BoardView`
  renders both hands in full every turn — fine for hotseat, but the moment step 2.4 makes
  human-vs-AI real it means the human sees the AI's hand while the AI cannot see theirs, so
  "I beat the AI" stops meaning anything.

  Deliberately **not** part of step 2.2, and deliberately not solved by handing the console an
  `ObservedState`. Two independent switches: *what an agent may see* is a correctness property
  enforced by test, while *what the screen shows* is a UI preference. The console renders from
  `GameState` and continues to; this step only decides which parts it prints. Wiring the console
  through `ObservedState` instead would couple a display choice to an engine invariant and make
  full-visibility debugging impossible.

  Default hidden, with the flag restoring today's behaviour — full visibility is genuinely
  useful for debugging a strange board state or watching an AI-vs-AI game, so it stays available
  rather than being deleted. When hidden, the non-active player's hand renders as a **count**
  (hand size is public information — the determinizer in step 2.3 depends on it being knowable).

  Note this changes **hotseat** too: hiding the inactive player's hand is what hotseat should
  have done all along (two humans sharing a screen genuinely shouldn't see each other's cards),
  but it means the current both-hands-visible view becomes `--reveal` rather than the default.
  Worth confirming that trade is wanted before implementing, since it costs a little convenience
  on every hotseat game to gain correctness on all of them.

  Also needs a one-line event log for actions whose result would otherwise be invisible: an
  opponent's discard currently reads off their visible hand, and with hands hidden it would
  happen silently.
- [ ] **6. IS-MCTS:** selection (UCB1), expansion, playout, backprop; per-iteration resampling.
- [ ] **7. Playout policy:** start uniform-random, then lightly heuristic (prefer damage/score
  moves) — usually a large strength gain for modest cost.
- [ ] **8. Performance:** apply/undo, node pooling, budget by time *or* iteration count.
- [ ] **9. Tuning:** exploration constant, playout depth cap, determinizations per search.

**Exit criteria:**
- [ ] IS-MCTS beats `RandomAgent` >95% over 500+ seeded games.
- [ ] IS-MCTS beats `GreedyAgent` >80% over 500+ seeded games.
- [ ] A decision at a realistic budget completes in target wall-clock (suggest ≤2s desktop).
- [ ] `ObservedState` provably leaks no hidden information.
- [ ] A human-vs-AI console game hides the AI's hand by default, and `--reveal` restores full
  visibility for debugging.

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

**Target platforms: Windows/macOS/Linux desktop and Android mobile.** Shipping to both from one
codebase is very achievable for a turn-based card game — there is no realtime input or
performance pressure — but it constrains layout and input from the first scene, so design for it
up front rather than retrofitting.

- [ ] **1. Godot 4.x with .NET;** add `Shapes.Godot` referencing `Shapes.Core` **unchanged**.
- [ ] **2. Adapter layer:** engine events → visual updates. Engine stays authoritative and
  UI-agnostic; the UI *never* mutates state directly, only submits actions.
- [ ] **3. Responsive layout from scene one.** A 3v3 board, two hands, and resource counters
  must fit both a wide desktop window and a tall phone screen. Use Godot's anchor/container
  system with distinct portrait and landscape arrangements; never hard-code pixel positions.
- [ ] **4. Touch-first input**, with mouse as the superset. Tap-to-select-then-tap-to-target
  works identically with a mouse; drag-and-drop needs separate handling on both. Hit targets
  sized for fingers (~44px minimum). No hover-dependent information.
- [ ] **5. Scenes:** board, slots, hand, resource counters, score track, card detail.
- [ ] **6. Art + animation:** real card art replacing placeholders; animation for
  play/move/merge/score/destroy.
- [ ] **7. Target selection UI** over the same `chosen_*` legal actions — single-target only
  (see the single-target rule), so this is one selection state with no chaining.
- [ ] **8. AI opponent** via the existing `IAgent` — difficulty = search budget. **Run search
  off the main thread** and cap the budget on mobile; a 2s desktop budget will drain battery and
  stutter the UI on a phone if run inline.
- [ ] **9. Deckbuilder** (`deckMode: "custom"`): browse the card set, build and save decks,
  validate against `RuleSet` limits.
- [ ] **10. Persistence:** saved decks, settings, progress — Godot `user://`.
- [ ] **11. Polish:** sound, transitions, menus.
- [ ] **12. Export pipeline:** desktop builds, plus Android (release signing, `.aab` for Play
  Store). The debug-keystore path and JDK/SDK toolchain were established in step 1.13 — this
  step productionizes and re-verifies that, on whatever Godot version Phase 4 ships with, rather
  than discovering the toolchain from scratch.

**Before starting:** re-read step 1.13's notes for the exact Android toolchain gotchas (the .NET
9 SDK requirement for export templates, and the Editor Settings Java/Android SDK paths).

**Exit criteria:**
- [ ] Full game playable with visuals against the Phase 2 AI on a desktop build.
- [ ] The same, on a **physical Android device**.
- [ ] Deckbuilder functional and validating against the engine's own rules.
- [ ] `Shapes.Core` unmodified from Phase 3.

---

## 3. Cross-cutting principles

- **Core stays pure.** No UI, no engine-specific types, no I/O in `Shapes.Core`. Test-enforced.
  Also **AOT-safe** — no reflection-heavy binding, favoring source-generated JSON
  (de)serialization instead.
- **One target maximum.** No card requires more than a single player-chosen target. Keeps
  branching flat for the AI, cards readable, and the targeting UI a single state.
- **Data over code.** Cards and rules are JSON. Balance changes must never require a recompile.
- **Determinism everywhere.** One seeded RNG source; any game reproducible from its seed.
- **Legal-action generation is the contract.** Console, AI, and Godot all consume one API.
- **Test the invariants, not just the paths.** Apply/undo symmetry, resource conservation,
  no negative health, observation never leaks hidden state.
- **Build the naive version first.** Correct, then fast, behind a stable interface.
