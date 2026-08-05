# Shapes — Development Plan

A 2-player, turn-based, board-and-cards game. Five phases: playable engine → IS-MCTS AI →
agent measurement & optimization → AI-driven balance → Godot client.

## Status

| Phase                                    | Progress   |
|------------------------------------------|------------|
| 1 — Playable engine                      | 13 / 13    |
| 2 — IS-MCTS AI (naive, correct)          | 6 / 6      |
| 3 — Agent measurement & optimization     | 7 / 9      |
| 4 — AI-driven balance                    | 0 / 7      |
| 5 — Godot client                         | 0 / 12     |

729 tests passing. **Phases 1 and 2 are complete.**

Phase 3 and 4 were split from one combined phase because they need opposite invariants: agent
comparison needs cards/rules **frozen**; balancing needs them **variable**. So Phase 3 freezes
content and varies agents; Phase 4 freezes agents and varies content. Phase 2 correspondingly
ends at a *correct* search, not a fast or tuned one.

**Next up: step 3.5** — tuning (exploration constant, remaining knobs 3.3a didn't already
settle), by measurement against `Shapes.Sim`.

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
duplicate across every clone; board as fixed `CreatureInstance[6]`). **Apply/undo over clone**
was Phase 3 step 3's originally planned perf path, gated by an apply/undo property test
(byte-identical round trip) written in Phase 1 before the optimization existed — built in full,
then measured and found to save nothing, because `IsMctsAgent` was never cloning in its hot loop
to begin with (see step 3's notes). Profiling found the real cost is playout-depth
`ActionGenerator.Generate` calls instead; steps 3.3a/3.3b chase that. **Determinism** — all randomness
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
- [x] **3. Performance — profiled, and re-scoped from what was guessed to what was measured.**
  The step as originally written ("apply/undo instead of clone, node pooling") was a guess made
  before any profiling existed. Both were implemented in full — a `GameStateMemento`-based
  apply/undo API on `GameState`/`ActionExecutor` (satisfying Phase 1's byte-identical property
  test, extended with two new apply/undo-specific cases), `Determinizer.RepopulateInto` reusing
  one scratch `GameState` object graph across a search's iterations instead of allocating fresh
  per iteration, and `SearchNodePool` reusing `SearchNode` objects across `Choose()` calls — then
  measured with a same-seed stopwatch (a git worktree of the pre-change commit vs. the changed
  code, plus an isolated microbenchmark) and **produced no measurable wall-clock improvement**
  (~81.7ms/decision before, ~82.0ms/decision after, at 200 iterations — within noise). That
  attempt's code was discarded rather than merged, since a change that cannot show a before/after
  win fails this step's own success criterion.

  **Why it didn't help, found by profiling instead of guessing**: `IsMctsAgent` never actually
  cloned — `Determinizer.Determinize` already built one private `GameState` per iteration and
  mutated it in place, so there was no clone-in-a-hot-loop for apply/undo to remove. A stopwatch
  bracketing every phase of one iteration (2000 iterations, real mid-game position) found:

  | Phase                          | % of iteration time | Calls/iteration |
  |---------------------------------|---------------------|------------------|
  | `Determinize`                   | 5.9%                | 1                |
  | Selection `Generate` + `Apply`  | 5.2%                | ~12              |
  | **Playout `Generate`**          | **51.6%**           | ~98              |
  | **Playout `Apply`**             | **34.8%**           | ~98              |
  | Playout policy's action pick    | 0.5%                | ~98              |

  Determinization — the thing the original step 3 text targeted — was never more than ~6% of
  the cost. **86.4% of one iteration is `ActionGenerator.Generate` + `ActionExecutor.Apply`,
  called roughly 98 times per playout** (a playout runs until the game ends or hits
  `PlayoutDepth`, untuned at 400 since step 5 hadn't run yet). `Generate` is the more expensive
  half of that because it fully enumerates every legal action on every call — allocating a
  `List<GameAction>`, one or two `HashSet<string>`s, and an `EffectContext` per hand
  card/move considered — even when the caller (a uniform-random playout) only needs to pick
  *one* action. Correct architecture for a console menu or an MCTS tree expansion; expensive
  when called at playout depth thousands of times per decision.

  This is the actual target for step 3's stopwatch-gated win, and it is bigger and
  higher-blast-radius than "apply/undo + node pooling" — `ActionGenerator.Generate` is, by its
  own doc comment, "the single most important API in the codebase," shared by console, AI, and
  tests. Split into two independently-measurable sub-steps below rather than folded back into
  this one, so each has its own before/after and its own review surface.
- [x] **3a. Tuned `PlayoutDepth` down from its untuned 400 to 200.** Measured the uniform-random
  playout-length distribution directly (4000 samples: a real game played a random 0–60-action
  prefix, then a uniform-random rollout to completion or the old 400 cap — matching exactly what
  `IsMctsAgent.PlayOut` does, rather than reusing whole-game `Shapes.Sim` stats, which reflect
  decision-making agents' games, not a playout's own uniform-random length): **p50=90, p90=198,
  p95=244, p99=330, max=541**. 400 covered 99.8% of playouts; 200 covers 90.4%. Chose 200 —
  covering the large majority while cutting worst-case playout length roughly in half — because a
  truncated playout is scored by `Reward()`'s score-margin heuristic rather than discarded, so
  truncation is a soft precision cost on the long tail, not a correctness bug.

  **Measured result**: an isolated same-seed microbenchmark (one fixed real mid-game position,
  200-iteration search, only `PlayoutDepth` varied, 20 repeated `Choose` calls per setting — the
  `Shapes.Sim` full-game numbers were too noisy for this, since changing the cap changes the
  search's own decisions and therefore game length, entangling the speedup with unrelated
  variance) showed **87.55ms → 41.67ms per decision, a 2.1× speedup** — the real win step 3's
  profiling predicted, versus step 3's own apply/undo attempt's measured zero. Win rates in
  `Shapes.Sim` matrices stayed consistent with pre-change numbers (no collapse in play quality);
  726 tests still pass unchanged, since no test pinned the old default.
- [x] **3b. A cheap playout-only action sampler.** Added `PlayoutActionSampler.SampleOne`
  (`Shapes.Core/Actions/`), a reservoir-sampling mirror of `ActionGenerator.Generate`'s exact
  traversal (same hand/board/move iteration order, same legality checks, same `chosen_*` target
  expansion) that returns one uniformly-chosen legal action directly, without ever allocating the
  `List<GameAction>` or dedup `HashSet<string>` `Generate` builds for every caller. `IsMctsAgent`'s
  `PlayOut` takes this path only when `_playoutPolicy` is `UniformPlayoutPolicy.Instance`
  (`ReferenceEquals` check) — `HeuristicPlayoutPolicy` still needs the full materialized list
  since it scores every candidate to pick the best, so `IPlayoutPolicy`'s contract ("pick one from
  this list") is untouched rather than widened to fit a policy that no longer needs a list.
  `ActionGenerator.Generate` itself, and every other caller (console, tree expansion, tests), is
  unmodified.

  **Correctness**: `Shapes.Tests/Actions/PlayoutActionSamplerTests.cs` (3 tests) drives 300 random
  games (`LegalActionSoundnessTests`' shape) asserting `SampleOne`'s result is always a member of
  `Generate`'s own list at that position (and null exactly when `Generate`'s list is empty), plus
  a 20,000-draw uniformity check at one fixed multi-option position. **Not** tested as exact
  per-call agreement with `legal[random.Next(legal.Count)]` even from an `IRandomSource.Fork()`'d
  identical position — reservoir sampling consumes one random draw per candidate where index-pick
  consumes exactly one, so the two walk the same random stream differently and land on different
  (each still individually uniform) picks; confirmed by hand this is a property of the two
  algorithms, not a bug, before dropping that assertion. All existing `IsMctsAgent` correctness
  tests (selection/expansion/backprop/resampling) now exercise this path unchanged, since it's the
  new default `PlayOut` behaviour for the uniform policy.

  **Measured result**: a git-worktree before/after of the same fixed real mid-game position (60
  repeated `Choose` calls per side, 200 iterations, `-c Release`) showed **48.28ms → 45.09ms per
  decision, a ~1.07× (~6.6%) speedup** — real and reproducible across repeated runs, but far
  smaller than step 3.3a's 2.1×. Root cause, found by re-examining what the sampler actually
  avoids: step 3.3's profiling attributed playout `Generate` cost to *building* an `EffectContext`
  and calling `TargetResolver` per hand card/move considered, not to the outer `List`/`HashSet`
  bookkeeping — and `PlayoutActionSampler` still has to consider every candidate (and therefore
  still builds every `EffectContext`/resolves every target) to reservoir-sample correctly over
  them. It only ever removes the list/hashset allocations sitting on top of that, which turns out
  to be the smaller share of the cost. 729 tests still pass; no correctness regression.
- [x] **3c. Profiled `Apply` and `EffectContext` directly, instead of guessing from 3b's
  leftover theory.** 3b's own before/after showed a much smaller win than step 3's table implied,
  which meant the table's shape (Generate 51.6% / Apply 34.8%) was right but *where inside each*
  the cost lived was still unmeasured. Added the same kind of temporary `Stopwatch` bracketing
  step 3 used (2000 iterations, same real mid-game position, removed after recording results — no
  instrumentation left in the shipped code) around `ActionExecutor.Apply`'s internals and around
  `PlayoutActionSampler`'s per-candidate `EffectContext`/`TargetResolver`/`ConditionEvaluator`
  calls.

  **What it found**: the two top-level buckets reproduced step 3's split almost exactly
  (playout `SampleOne` ~51.7%, `Apply` ~35.3%, `Determinize` ~5.8%, selection `Generate` ~3.1%).
  Inside them, `EffectContext` construction + `TargetResolver` + `ConditionEvaluator` combined
  were only ~8% of total iteration time — confirming 3b's re-reading that `EffectContext` was
  never the expensive part. Two real, previously-unmeasured costs turned up instead:

  - `Board.RemoveDead()` — called on **every** `ActionExecutor.Apply`, whether or not anything
    died — was iterating `AllCreatures().ToList()`, an unconditional defensive copy. The copy
    protects against nothing: `AllCreatures()`'s enumerator reads `_slots[slot.ToFlatIndex()]`
    fresh at each step from a fixed, precomputed slot-index sequence, so nulling an *earlier*
    slot mid-loop cannot invalidate a *later* one. There is no live view of a mutable collection
    for the copy to protect. Removed the `.ToList()`, and made the result list itself lazy —
    allocated only once a first dead creature is actually found, returning a single shared empty
    `List` (`Board.NoneRemoved`, never mutated) on the common "nothing died" path, so a call that
    finds nothing to report allocates nothing. Kept the public signature `List<...>` (not
    nullable), so every existing caller's `foreach`/`.Count`/`Assert.Empty` usage needed no
    changes.
  - `EffectContext` was a `sealed class`, heap-allocated on every construction — of which there
    are thousands per playout (once per hand card/move `PlayoutActionSampler`/`ActionGenerator`
    considers, plus once per `ActionExecutor.ResolveEffects` call) — despite every existing call
    site already passing it by value and never storing it past one call (`EffectOp.Apply` takes
    it as a parameter; `WithChosenTarget`/`WithSelf`/`WithSelfAsController` return a fresh value
    rather than mutating in place). Converted to a `readonly struct`. Required removing five
    `ArgumentNullException.ThrowIfNull(ctx)` calls across `EffectInterpreter`, `ConditionEvaluator`,
    and `TargetResolver` — meaningless on a non-nullable value type — and nothing else; no call
    site's logic changed, since every existing usage pattern already matched struct-by-value
    semantics.

  **Measured result**: a git-worktree before/after of the same fixed real mid-game position (60
  repeated `Choose` calls per side, 200 iterations, `-c Release`, 6 total runs per side to control
  for machine variance) showed **~43.4ms → ~40.0ms per decision, a ~1.09× (~8%) speedup** — smaller
  than 3.3a's 2.1×, similar in size to 3.3b's ~1.07×, and stacks with it rather than replacing it
  (this step changes `Board`/`EffectContext`, used by every `Apply` call including 3.3b's playout
  path). Confirms the emerging pattern for this phase: the two big, clean wins were tuning
  `PlayoutDepth` (3.3a, cuts the iteration *count*) and this step's two allocation fixes (3.3c,
  cuts unconditional per-call waste); the two shape-changing rewrites aimed at "the expensive-
  looking API" by inspection alone (step 3's original apply/undo guess, 3.3b's list/HashSet guess)
  both underperformed their own premise once actually measured. 729 tests still pass; no
  correctness regression. All temporary profiling instrumentation was removed before committing —
  none of it is in the diff, matching how step 3's original profiling was done and discarded.
- [x] **4. Determinizations per search — measured, and the trade is not worth taking.** Added
  `IterationsPerDeterminization` to `IsMctsAgent` (default 1, true per-iteration resampling,
  unchanged from step 2.6): the number of consecutive iterations that share one `Determinize`
  sample before drawing a fresh one. `Choose` samples once, then hands the same `GameState` to
  `RunIteration` for up to that many iterations, cloning it per iteration once reuse is requested
  (`Clone()` forks the RNG, so a clone's rollout cannot leak into a sibling or the real game) --
  at the default of 1 the clone is skipped entirely and the world is mutated directly exactly as
  before, so the existing default path pays nothing for the feature's existence.
  `LastDistinctWorldCount` (the resampling instrumentation step 2.6 built) needed no changes: it
  already counts distinct sampled hands, which is exactly "how many times did we actually
  resample" regardless of how many iterations each sample served.

  **Why this was worth measuring rather than assuming**: step 3's profiling table put
  `Determinize` at ~5.9% of one iteration's cost, well behind playout `Generate`/`Apply`'s 86.4%
  -- so reuse could only ever buy a small, capped win, and the question was whether that win
  survives contact with a stopwatch at all, and whether the sampling breadth it costs is even
  visible in play quality.

  **Measured result** (git-worktree-style same-seed microbenchmark, one fixed real mid-game
  position built by driving `RandomAgent` 30 actions into a fresh game from seed 21, 200
  iterations, `-c Release`, 10 warmup `Choose` calls per setting before timing 40 more -- the
  first pass without adequate warmup showed reuse=1 at 53ms vs reuse=2-50 clustered at ~31ms, a
  gap that reproduced in reverse order too and turned out to be JIT tiering on the first
  configuration constructed in the process, not the mechanism; re-run with warmup and in both
  forward and reverse order collapsed it):

  | Reuse window | ms/decision (forward) | ms/decision (reverse) |
  |--------------|------------------------|------------------------|
  | 1            | 31.89                  | 33.51                  |
  | 2            | 33.85                  | 31.90                  |
  | 5            | 30.73                  | 30.83                  |
  | 10           | 30.26                  | 29.45                  |
  | 20           | 30.24                  | 31.15                  |
  | 50           | 30.55                  | 29.24                  |

  No reuse window is distinguishable from any other -- every value falls within ~4ms of every
  other, which is machine noise, not a trend. This matches the profiling prediction: even
  eliminating `Determinize` entirely could not buy more than its ~5.9% share, which is under 2ms
  at this position's ~31ms baseline and is exactly the size noise this benchmark can't resolve
  from zero.

  **Play quality** (same real card set, 200 iterations, 30 games per reuse window against the
  reuse=1 baseline, seats alternated every game to cancel first-player advantage):

  | Reuse window vs. reuse=1 | Win rate |
  |----------------------------|----------|
  | 2                           | 14/30 (46.7%) |
  | 5                           | 15/30 (50.0%) |
  | 10                          | 12/30 (40.0%) |
  | 20                          | 16/30 (53.3%) |

  All four sit inside ordinary 30-game noise around 50% -- no reuse window measurably weakens
  play, but per the timing table above there was never a speed budget to spend that weakening
  would have been trading against. **`IterationsPerDeterminization` stays at its default of 1.**
  Shipped as a constructor parameter (tested in `IsMctsAgentTests.cs`: a reuse window larger than
  the whole budget samples exactly one world, a window of N resamples every N iterations, a
  reused world's mutations do not leak into the next iteration sharing it, and an explicit 1
  reproduces the default path's decision bit-for-bit) rather than reverted outright, since the
  measurement is what step 3.4 asked for and a future change to `Determinize`'s cost (Phase 5's
  belief-model sampling, say) could revisit the trade -- but nothing in `Shapes.Sim`/`Shapes.Console`
  wires it to anything other than 1, matching step 3's own precedent of keeping a
  measured-not-worth-it change out of the default path rather than deleting the capability. 733
  tests pass (729 + 4 new); no correctness regression.
- [ ] **5. Tuning** — exploration constant, playout depth cap (whatever 3a did not already settle);
  pure measurement, no first-principles shortcut.
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
