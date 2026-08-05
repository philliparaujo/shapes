# Shapes — Development Plan

A 2-player, turn-based, board-and-cards game. Five phases: playable engine → IS-MCTS AI →
agent measurement & optimization → AI-driven balance → Godot client.

## Status

| Phase                                    | Progress   |
|------------------------------------------|------------|
| 1 — Playable engine                      | 13 / 13    |
| 2 — IS-MCTS AI (naive, correct)          | 6 / 6      |
| 3 — Agent measurement & optimization     | 2 / 7      |
| 4 — AI-driven balance                    | 0 / 7      |
| 5 — Godot client                         | 0 / 12     |

726 tests passing. **Phases 1 and 2 are complete.**

Phase 3 and 4 were split from one combined phase because they need opposite invariants: agent
comparison needs cards/rules **frozen**; balancing needs them **variable**. So Phase 3 freezes
content and varies agents; Phase 4 freezes agents and varies content. Phase 2 correspondingly
ends at a *correct* search, not a fast or tuned one.

**Next up: step 3.3** — apply/undo instead of clone, node pooling, gated by Phase 1's apply/undo
property test. Measure with a same-seed stopwatch before/after, now that `ismcts-heuristic` (step
3.2) is the config worth making fast.

### Common commands

Run from repo root (`shapes/`, where `Shapes.sln` lives).

| What                          | Command                                              |
|-------------------------------|-------------------------------------------------------|
| Build everything              | `dotnet build`                                       |
| Run all tests                 | `dotnet test Shapes.Tests/Shapes.Tests.csproj`       |
| Run one test by name          | `dotnet test Shapes.Tests/Shapes.Tests.csproj --filter "FullyQualifiedName~TestMethodName"` |
| Play the game (console)       | `dotnet run --project Shapes.Console`                |
| Play against the AI           | `dotnet run --project Shapes.Console -- --p2 greedy` |
| **Watch a full AI game**      | `dotnet run --project Shapes.Console -- --p1 greedy --p2 random --seed 7 --quiet` |
| Watch the search play         | `dotnet run --project Shapes.Console -- --p1 ismcts --p2 greedy --seed 7 --quiet` |
| **Run the agent matrix**      | `dotnet run -c Release --project Shapes.Sim -- --agents random,greedy,ismcts,ismcts-heuristic --games 30` |

`--p1`/`--p2` each take `human` (default), `random`, `greedy`, `ismcts`, or `ismcts-heuristic`
(step 3.2's heuristic playout, same search otherwise). `--iterations <n>` sets the `ismcts`/
`ismcts-heuristic` search budget (default 200, in iterations so seeded games replay exactly).
`--seed <n>` skips the prompt; `--quiet` gives one line per action. `--help` lists it all.

**The waiting seat's hand renders as a count** (`--reveal` shows both) so a human never reads
the AI's cards. **Watch games, don't just assert about them** — step 2.4's blocking-slot bug was
invisible to a passing test suite and only surfaced by reading a played game. `Shapes.Sim`
(Phase 3 step 1) is the batch/statistical version of the same idea.

## 0. Confirmed ruleset

Supersedes the reference PDF where they disagree (its resource-acquisition graph is obsolete).

**Resources & types** — three resources in a rock-paper-scissors cycle: △ Spike (pierce),
▢ Anvil (reflect), ◯ Wheel (ricochet). Effectiveness: Anvil → Spike → Wheel → Anvil, 2× damage
on a weak matchup, otherwise 1× (no resistance/halving). **Merged (multi-type) targets** take 2×
if one of their types matches the attacker and another is weak to it — so merging can *increase*
vulnerability (offsets the "free strictly-better action" concern under Merging below; Phase 4
should measure if this is priced right). **Type comes from resource cost, always** — a
move/spell's attack type is its own cost's resource type; a creature's defensive type is its
play cost's. A zero-cost move/spell is the only "typeless" case (flat, unmultiplied damage).
Effectiveness applies after flat modifiers and before clamping (pinned by test). Lives as
`TypeChart` on `RuleSet` (variable in balance sweeps); the merged-target rule itself is
hard-coded.

**Income** — each turn, 1 of each resource, plus +1 of its type per creature controlled (a
merged creature pays one of *each* of its types).

**Board** — 3 slots per player; slot *i* opposes enemy slot *i*.

**Turn structure** — score (+1/friendly creature with an empty opposing slot) → income → draw
(burn on overdraw) → actions (play/move/merge/discard, any order, repeatable) → end turn. Win at
score ≥ X (config, ~10). **Draw is at turn START** (Hearthstone sequencing — a drawn card is
playable immediately), including turn one on top of the opening hand.
`GameState.AdvanceToActions()` is the one entry point sequencing score→income→draw.

**Drawing vs. discarding** — deliberately asymmetric: **overdraw burns the just-drawn card**
(nobody chooses), while a card effect's "discard N" is player-chosen, one card at a time, and
gates every other action (including `EndTurn`) as a pending debt (`GameState.PendingDiscards`)
until paid. An unpayable debt is clamped at incursion, not carried, so it can never deadlock the
action generator. Both halves pinned by test — collapsing them is the plausible regression. The
hand limit lives on the shared `DrawWithBurn` path so every card-effect draw (not just the turn
draw) burns correctly on overflow — a fuzz-caught bug the first time around.

**Creatures & moves** — top-left pips = play cost (unrelated to tiers). No auto-attack/passives;
all damage from activated moves. No summoning sickness; each move usable once per turn.

**Merging** — free action between two adjacent, un-merged friendly creatures: health summed,
moves unioned, types combined, one slot, cannot merge again.

**Destroyed creatures discard to their owner** (not the killer) — every card folded into them via
`MergedFrom`, so cards stay conserved across hand/deck/discard/board (needed by Phase 2's
determinizer, which reconstructs hidden cards by subtraction). Merging itself discards nothing;
tokens discard nothing (never cards). Pinned by `CardConservationTests.cs`.

> ⚠️ **Open design questions for Phase 4:** is merge's stat-gain-vs-vulnerability-and-slot-cost
> tradeoff priced right (if the AI merges almost every chance, no)? And does an unopposed
> creature's double duty — scoring *and* paying income — compound into an unbeatable runaway
> lead? Neither changed yet; both need instrumenting first.

---

## 1. Design decisions

**Language/runtime: C# on .NET 8.** Godot 4 has first-class C# support, so Phase 5 is a client
swap, not a rewrite, provided the engine takes no UI dependency. `Shapes.Core` is a pure class
library (zero UI deps, enforced by test that it references only the BCL) — console, AI, tests,
and Godot are interchangeable consumers.

**Project structure:**
```
shapes/
├─ Shapes.Core/     # Pure engine: Primitives, State, Actions, Effects, Rules, Cards
├─ Shapes.Content/  # JSON card data + rules presets. NOT code.
├─ Shapes.Ai/        # IS-MCTS, determinizer, playout policies, evaluators
├─ Shapes.Console/   # Text client
├─ Shapes.Sim/        # Headless batch runner → CSV/JSON stats
├─ Shapes.Tests/      # xUnit
└─ Shapes.Godot/      # Phase 5 only, references Shapes.Core
```

**State representation** — authoritative state is plain mutable classes (console/tests/Godot);
search state is the same data laid out for speed inside MCTS (`CreatureInstance` as a struct;
moves not stored per-instance, derived from `MergedFrom` card ids since storing them would
duplicate across every clone; board as fixed `CreatureInstance[6]`). **Apply/undo over clone** is
the eventual perf path (Phase 3 step 3) — gated by an apply/undo property test (byte-identical
round trip) written in Phase 1, before the optimization exists. **Determinism** — all randomness
through one seeded `IRandomSource` (hand-rolled xorshift64*, `Fork()`-able so search clones don't
advance the real RNG stream); no `Random.Shared`/`DateTime.Now` anywhere in `Shapes.Core`.

**Cards are JSON data interpreted by a small effect engine, not C# subclasses** — the most
consequential structural decision, since a subclass-per-card becomes the Phase 4 balance
bottleneck (recompile per tweak, AI can't reason about card text). Effect vocabulary (~36 real
cards) covers damage/health/cards/resources/board/status/modifiers/control ops with a small
targeting-selector language (`self`, `opposing`, `chosen_enemy`, etc.); `chosen_*` selectors
expand into distinct legal actions for MCTS. **Single-target rule**: a move/spell may need at
most one player-chosen target — keeps branching flat (N, not N×M), cards readable, and the
Phase 5 targeting UI a single state. Enforced at card-load validation.

**Deck model** — Phases 1–3 use fixed symmetric decks (`deckMode: "symmetric"`), deliberately, so
varying decks doesn't confound card win-rate with deck-composition effects. Phase 5 adds
deckbuilding (`deckMode: "custom"`, explicit card-id-plus-count lists) — this is also an AI
change, since the determinizer currently assumes symmetric decks and throws otherwise (Phase 5
step 9 migrates it).

**Rules as configuration** — income, scoring, draw, hand limit, win condition, type chart all
live in a `RuleSet` loaded from JSON, so a balance experiment is just a named ruleset file. Board
size is the one exception (structural, not a balance knob).

**Is IS-MCTS the right choice? Yes, with caveats.** Genuinely imperfect-information game, no
strong hand-authored eval function, branching factor defeats minimax. Key risks: branching factor
(mitigated by treating each atomic action, not whole turns, as one tree node — 10-40 branching);
strategy fusion / undervaluing info-gathering (mitigated by resampling a fresh determinization
every iteration rather than once per search); and determinization must respect observations (a
sampled card already in the graveyard is a correctness bug). Chose **single-observer IS-MCTS with
per-iteration resampling** over minimax/expectimax (no eval fn) and AlphaZero-style neural (wrong
tool for balance tooling *now*); can escalate to multi-observer later if strength demands it.

**Testing strategy** — xUnit, written alongside each component, not bolted on after. Seeded
determinism and data-driven (synthetic) test cards keep tests cheap and stable under rebalancing.
`StateBuilder` is the fluent fixture for exact board positions without playing toward them.
Coverage priorities: effect interpreter and rules engine (console rendering isn't worth testing).
Bar is "every op and mechanic exercised," not a line-coverage percentage.

---

## 2. Phase plan

### Phase 1 — Playable engine ✅ complete (13/13)

**Goal:** complete, correct, rules-configurable game with a text interface, tests written
alongside each piece.

- [x] **1.** Installed .NET 8 SDK.
- [x] **2.** Solution skeleton incl. `Shapes.Tests`; `Shapes.Core`-only-references-BCL enforced
  by test.
- [x] **3. Primitives** — immutable structs. `ResourcePool.Subtract` throws rather than clamps,
  so a bad payment fails loudly instead of hiding a legality bug.
- [x] **4. `RuleSet` + JSON loading** — validates at load; unknown properties rejected (catches
  typos like `scoreToWinn`).
- [x] **5. State model** — mutable `GameState`/`Board`/`CreatureInstance` with `Clone()`; seeded
  RNG with `Fork()` so search clones never advance the real stream.
- [x] **6. Effect interpreter + op vocabulary**, built before card data so it was validated
  against real cards. Damage order pinned:
  `(base + next_attack_bonus + next_damage_taken_bonus) × typeMultiplier`.
- [x] **7. Card JSON schema + loader + validation** — single-target rule enforced across every
  effect in `CardValidator`.
- [x] **8. Action model** — `PlayCard`/`UseMove`/`Merge`/`Discard`/`EndTurn` + legal-action
  generation, the most important API in the codebase. Value-equal actions so MCTS dedupes
  correctly; generator decides legality, executor trusts it.
- [x] **9. Turn loop** — score→income→draw→actions→end folded into one
  `AdvanceToActions()` entry point so no caller can forget to sequence it.
- [x] **10. Entered all ~36 real cards** — about a third needed new engine mechanics (attack
  buffs, taunt expiry, reactive triggers, hand-composition scoring, a `health_source` selector).
- [x] **11. Console client** — `BoardView` + `Program.cs`, verified to a real scripted win.
- [x] **12. Fuzz harness** — 10,000 games on the real card set, asserts games actually terminate
  (~7s).
- [x] **13. Mobile toolchain spike** — confirmed Godot 4 C#/.NET Android export on a physical
  device. Notes for Phase 5: export *templates* need the .NET 9 SDK alongside .NET 8; Editor
  Settings needs explicit Java/Android SDK paths; a stale-APK trap means rebuilds need
  `adb install -r`, not just re-export.

**Exit criteria:** all met — two humans play to a win at console; all ~36 cards implemented;
scripted games replay identically from seed; apply/undo property tests pass; every effect op
tested; fuzz harness clean over 10k games.

### Phase 2 — IS-MCTS AI (naive but correct) ✅ complete (6/6)

**Goal:** a working search and the seams around it — deliberately not fast, tuned, or measured
(needs a batch runner Phase 3 builds).

- [x] **1. `IAgent` interface** — `Choose(AgentContext, CancellationToken)`, plus `RandomAgent` as
  reference implementation. Contract pinned at the interface level: legal choice, no state
  mutation, seed-determinism.
- [x] **2. `ObservedState`** — strict per-player projection of `GameState`, structurally unable to
  expose the opponent's hand contents or either player's deck order (pinned by a reflection
  check, not just value checks).
- [x] **3. Determinizer** — samples a real `GameState` consistent with observations via multiset
  subtraction from the (symmetric-only) shared decklist.
- [x] **4. Baseline agents** — `RandomAgent` + a non-simulating `GreedyAgent` that scores from
  static card data. It can't simulate: an `ObservedState` has no path to a `GameState` without
  determinizing first, which would make it a 1-iteration IS-MCTS and ruin it as an independent
  yardstick.
- [x] **5. Console hidden-hand mode** — `--reveal` off by default, including in hotseat; no
  exceptions, since an exception is what people forget.
- [x] **6. IS-MCTS** — selection/expansion/playout/backprop, availability-corrected UCB1, fresh
  determinization every iteration.

**Notable bugs found and fixed along the way:** the determinizer's conservation-identity
requirement exposed a real rules bug — destroyed creatures vanished instead of discarding, which
would have let the sampler deal cards that were physically dead. Fixed via
`GameState.DestroyCreature`, the one path all destruction now goes through. `GreedyAgent`'s first
cut only scored *taking* an open slot, not *blocking* an opponent's — a bug a win-rate barely
moved but a blocking-opportunity count exposed sharply (the recurring lesson of this phase: an
aggregate win rate against a weak opponent partly measures the opponent, not the change).
IS-MCTS's availability-corrected UCB1 (Cowling/Powley/Whitehouse 2012) exists because plain UCB1
undercounts actions that aren't legal in every sampled world; the correction's failure mode is
silent (search still runs, just explores the wrong things), which is why it's pinned by a test
verified to fail when sabotaged. A subtler sabotage — determinizing once and cloning thereafter —
does *not* fail a naive draw-count assertion (playouts still consume randomness), so the real
test asserts on `IsMctsAgent.LastDistinctWorldCount` (distinct sampled hands) directly.

**Watched, not just asserted:** `--p1 ismcts --p2 greedy --seed 7 --iterations 300` wins 11–2 in
9 turns, developing the board and spending resources before passing — including one correct-but-
suspicious-looking `EndTurn` that was checked by hand rather than assumed to be the classic
passing bug.

**Exit criteria:** all met — search implemented end to end; satisfies the `IAgent` contract as a
theory case, not a parallel suite; mechanisms pinned by sabotage-verified tests; a full game
watched and read; `ObservedState` provably leaks nothing; console hides the AI's hand by default.
No win rate is asserted anywhere in this phase — see the note below.

> **On strength numbers in Phase 2.** Any figure quoted during this phase is an ad-hoc dev
> observation, not a result — a rate against a weak opponent partly measures the opponent, and
> every number is relative to card balance, which Phase 4 will change.

### Phase 3 — Agent measurement & optimization

**Goal:** make the AI strong and *prove* each change helped. Cards/rules **frozen** all phase —
that's the whole reason this is a separate phase from Phase 4. Everything is gated on step 1: an
unmeasured optimization is a guess, and a bad heuristic can make search weaker while still
looking reasonable in a watched game.

- [x] **1. `Shapes.Sim`** — headless batch runner (N games, parallel via `Parallel.ForEach`,
  every game independently seeded from `(baseSeed, agentOne, agentTwo, gameIndex)` so no two
  games in a matrix ever collide → CSV summary + full-detail JSON). Every ordered pairing
  (including self-play) is run with both seat assignments, reported as separate `PairingSummary`
  rows — never pooled, since pooling hides first-player advantage. Each `GameResult` also carries
  a per-`ActionKind` action count, the behaviour-count instrumentation Phase 2's blocking-slot
  lesson calls for. 23 tests (`Shapes.Tests/Sim/`) cover option parsing, same-seed
  reproducibility of both a single game and a whole matrix, seat-separation, and seed-collision
  freedom.

  **Baseline matrix** (30 games/pairing, `ismcts` at 200 iterations, real card set, seed 1):

  | P1 \ P2   | random | greedy | ismcts |
  |-----------|--------|--------|--------|
  | random    | 63.3%  | 36.7%  | 3.3%   |
  | greedy    | 96.7%  | 70.0%  | 13.3%  |
  | ismcts    | 100.0% | 90.0%  | 80.0%  |

  (Cell = P1's win rate.) `ismcts` beats `random` by a ~97-point margin (100% − 3.3% across
  seats) and `greedy` by a ~77-point margin (90% − 13.3%) — the wider-margin-against-the-weaker-
  opponent ordering Phase 3's exit criteria call for. Ad hoc dev numbers, not a tuned result —
  Phase 3 steps 2–6 (playout policy, performance, tuning) still change these; step 7 records the
  final frozen matrix.
- [x] **2. Playout policy** — `IPlayoutPolicy` (`Shapes.Ai/Search/`), selected via
  `IsMctsAgent`'s constructor and defaulting to `UniformPlayoutPolicy` (the original rollout,
  kept as the control — `IsMctsAgent`'s own correctness tests still run against it unchanged, so
  a heuristic playout can never paper over a selection/backprop bug in the suite that exists to
  catch one). `HeuristicPlayoutPolicy` scores each legal action by the same damage/lethal/board-
  presence priorities as `GreedyAgent`, but computed directly against the playout's own real
  `GameState` (via `EffectContext`/`TargetResolver`) rather than reused wholesale — `GreedyAgent`'s
  scorer is written against a hidden-information-safe `ObservedState` a playout doesn't need, and
  playing back through it once per remaining ply of every iteration would cost far more than
  `GreedyAgent`'s own once-per-`Choose` budget affords. Exposed as `ismcts-heuristic` in both
  `Shapes.Console` and `Shapes.Sim`; `IsMctsAgent.Name` folds the policy in
  (`"ISMCTS(200,heuristic)"`) so a batch matrix never pools the two configurations. 15 new tests
  (`Shapes.Tests/Ai/HeuristicPlayoutPolicyTests.cs` mirrors `GreedyAgentTests`' one-unambiguous-
  action-per-test shape; four more in `IsMctsAgentTests.cs` pin the wiring — default stays
  uniform, the heuristic policy still finds a decided lethal move, and still resamples a new
  world every iteration).

  **Measured result** (30 games/pairing, both at 200 iterations, both seats, seed 1):

  | P1 \ P2            | greedy | ismcts | ismcts-heuristic |
  |---------------------|--------|--------|------------------|
  | greedy              | 70.0%  | 13.3%  | 6.7%             |
  | ismcts              | 90.0%  | 80.0%  | 60.0%            |
  | ismcts-heuristic    | 100.0% | 80.0%  | 46.7%            |

  `ismcts-heuristic` beats plain `ismcts` in both seat assignments (60.0% and 80.0% — the mirror
  cells read 40.0%/20.0% for `ismcts`) and beats `GreedyAgent` by a wider margin than uniform
  `ismcts` does (93.3–100pp vs. 76.7–86.7pp). The heuristic playout is a real improvement at this
  budget — but not a free one: per-decision wall-clock (isolated as seconds/action, since the
  heuristic policy also plays longer games) rose from 0.41s to 0.52s against `GreedyAgent`
  (~1.26×) and from 0.69s to 1.26–1.30s in `ismcts`-involving pairings (~1.8–1.9×), because every
  playout step now builds an `EffectContext` and calls `TargetResolver` instead of an O(1) random
  pick. **`ismcts` therefore stays uniform-by-default** — the cost is real enough that switching
  it silently would confound step 3's own before/after measurements (step 3's clone→apply/undo
  swap and step 5's tuning both need a stable baseline to measure against, not one this step
  quietly moved). `ismcts-heuristic` is the explicit, opt-in stronger-but-slower configuration —
  worth reaching for deliberately (a stronger sparring partner, a Phase 4 balance signal), not a
  silent replacement for what "ismcts" means elsewhere in the codebase.
- [ ] **3. Performance** — apply/undo instead of clone, node pooling; success is a stopwatch
  before/after, gated by Phase 1's apply/undo property test.
- [ ] **4. Determinizations per search** — measure whether reusing a sampled world across several
  iterations trades acceptable sampling breadth for speed.
- [ ] **5. Tuning** — exploration constant, playout depth cap; pure measurement, no
  first-principles shortcut.
- [ ] **6. Re-verify correctness tests still pass at tuned settings.**
- [ ] **7. Record the final agent matrix** as Phase 4's frozen reference instrument.

**Exit criteria:** IS-MCTS decisively beats both baselines, and beats `RandomAgent` by a wider
margin than `GreedyAgent` does (the *ordering* is what proves search adds value); a decision
completes in target wall-clock (~≤2s desktop); every optimization has a same-seed before/after;
agent configuration is frozen and recorded.

### Phase 4 — AI-driven balance

**Agents frozen** this phase — the mirror image of Phase 3. Cards/rules untouched since Phase 1
step 10, so every number here reflects genuinely stable content.

- [ ] **1. Metrics** — win rate by seat, game length, score curves, per-card play/win-rate
  correlation, move usage, merge frequency, resource flow, ending type.
- [ ] **2. Answer the two flagged design questions directly as behaviour measurements** (not win
  rate, per Phase 2's lesson, and only meaningful with Phase 3's frozen competent agent): does the
  AI ever decline a legal merge, and how strongly does unopposed-creature income compound?
- [ ] **3. Sweep** rulesets/card variants; rank outliers.
- [ ] **4. Iterate** — edit JSON, rerun, compare; keep a `balance/` experiment log.
- [ ] **5. Watch for** never-played/auto-include cards, degenerate loops, first-player advantage
  beyond ~55%, non-terminating games.
- [ ] **6. Archetype sweeps** (mono vs. mixed, aggro vs. control) once `deckMode: "custom"`
  exists — only after per-card balance has settled.

**Exit criteria:** no extreme play-rate outliers; first-player advantage near even; game length
in target band; merge tradeoff and income-compounding both confirmed as real, non-degenerate
decisions.

### Phase 5 — Godot client (desktop + mobile)

Target Windows/macOS/Linux desktop and Android from one codebase — achievable for a turn-based
card game, but constrains layout/input from the first scene.

- [ ] **1.** Add `Shapes.Godot`, referencing `Shapes.Core` unchanged.
- [ ] **2.** Adapter layer: engine events → visuals; UI only ever submits actions, never mutates
  state.
- [ ] **3.** Responsive layout (anchors/containers, portrait+landscape) from scene one.
- [ ] **4.** Touch-first input with mouse as a superset; ~44px hit targets; no hover-dependent
  info.
- [ ] **5.** Scenes: board, slots, hand, resources, score, card detail.
- [ ] **6.** Real card art and play/move/merge/score/destroy animation.
- [ ] **7.** Target-selection UI over the existing `chosen_*` actions — one state, no chaining,
  thanks to the single-target rule.
- [ ] **8.** AI opponent via `IAgent` (difficulty = search budget), run off the main thread and
  capped on mobile.
- [ ] **9. Deckbuilder** (`deckMode: "custom"`) — also owns migrating the determinizer off its
  symmetric-deck assumption, since custom decks make the opponent's decklist itself hidden (a
  belief-distribution problem, not just a partition problem). Most of `Determinizer` and its test
  suite are unaffected (phrased against observations, not deck provenance); only `UnseenCardsOf`
  changes, to sample from a belief model instead of reading `BuildSymmetricDeck`. First belief
  model: constrain to cards demonstrably played, fill the rest uniformly within deck-size/copy
  limits — crude but sound, same justification as Phase 2's uniform sampling.
- [ ] **10.** Persistence (`user://`): decks, settings, progress.
- [ ] **11.** Polish: sound, transitions, menus.
- [ ] **12.** Export pipeline (desktop + signed Android `.aab`), reusing/re-verifying the step
  1.13 toolchain rather than rediscovering it.

**Exit criteria:** full game playable with visuals on desktop and on a physical Android device;
deckbuilder validates against engine rules; AI plays custom decks without assuming a mirrored
opponent decklist; `Shapes.Core` unmodified from Phase 4.

---

## 3. Cross-cutting principles

- **Core stays pure.** No UI/Godot/I-O in `Shapes.Core`; AOT-safe (source-generated JSON, no
  reflection-heavy binding). Test-enforced.
- **One target maximum** per card, for flat branching and a simple targeting UI.
- **Data over code.** Cards and rules are JSON; balance changes never need a recompile.
- **Determinism everywhere.** One seeded RNG source; any game reproducible from its seed.
- **Legal-action generation is the contract** all consumers (console/AI/Godot) share.
- **Test the invariants, not just the paths** — apply/undo symmetry, resource conservation, no
  negative health, no observation leakage.
- **Build the naive version first.** Correct, then fast, behind a stable interface.
