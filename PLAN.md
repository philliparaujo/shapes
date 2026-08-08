# Shapes — Development Plan

A 2-player, turn-based, board-and-cards game. Five phases: playable engine → IS-MCTS AI →
agent measurement & optimization → AI-driven balance → Godot client.

## Status

| Phase                                    | Progress   |
|------------------------------------------|------------|
| 1 — Playable engine                      | 13 / 13    |
| 2 — IS-MCTS AI (naive, correct)          | 6 / 6      |
| 3 — Agent measurement & optimization     | 9 / 9      |
| 4 — AI-driven balance                    | 14 / 14    |
| 5 — Godot client                         | 3 / 16     |

951 tests passing. **Phases 1, 2, 3, and 4 are complete.**

Phase 3 and 4 were split from one combined phase because they need opposite invariants: agent
comparison needs cards/rules **frozen**; balancing needs them **variable**. So Phase 3 freezes
content and varies agents; Phase 4 freezes agents and varies content. Phase 2 correspondingly
ends at a *correct* search, not a fast or tuned one.

**In progress: Phase 5** — the Godot client. Milestone A (one playable screen) is underway:
A1–A3 done, a hotseat game is playable end to end in the editor (play/move/merge/discard/end
turn all working). Content is settled at `v1.7-final` and the balance record lives in
`balance/LOG.md`; the one item Phase 4 left open is a small seat-2 edge visible only at large
samples (see that phase's closing note).

**What the metrics can and cannot decide.** They *detect* outliers; they do not *interpret* them.
A 3% take rate means "cut or buff" for a vanilla creature and "working as designed" for a
situational answer card — the number is identical and the correct action is opposite. Nothing in
the report knows what a card costs or is meant to do, so the loop is: metrics narrow 36 cards to
the handful worth arguing about, you read those, you change one, and the metrics tell you whether
the change did what you intended. That last step is the rigorous one. A standing caveat underneath
all of it: every number is relative to the agent, so a card whose payoff lands outside IS-MCTS's
horizon reads as weak, and "buffing" it would be balancing for the AI's blind spot.

**Sweeps need hundreds of games per configuration, not tens.** Every rate now reports its own
interval, and `Shapes.Sim` closes with a one-line resolution check ("N of M cards have a win-rate
interval still straddling 50%"). At 20 games that reads 36 of 36 — the honest answer to "do I have
enough data yet," and the number to drive up before believing any ranking.

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
| **See stats from played games** | `dotnet run -c Release --project Shapes.Sim -- --agents greedy,ismcts --games 30 --metrics-json metrics.json` |
| **Browse stats in the metrics explorer** | `dotnet run -c Release --project Shapes.Sim -- --agents greedy,ismcts --games 30 --report report.html` |
| **Re-explore a saved metrics.json** | `dotnet run -c Release --project Shapes.Sim -- --from-metrics-json metrics.json --report report.html` |
| **Compare two saved metrics.json runs** | `dotnet run -c Release --project Shapes.Sim -- --compare baseline/metrics.json,candidate/metrics.json --compare-report compare.html` |

`--p1`/`--p2` each take `human` (default), `random`, `greedy`, `ismcts`, or `ismcts-heuristic`
(step 3.2's heuristic playout, same search otherwise). `--iterations <n>` sets the `ismcts`/
`ismcts-heuristic` search budget (default 200, in iterations so seeded games replay exactly).
`--seed <n>` skips the prompt; `--quiet` gives one line per action. `--help` lists it all.

**Stats are `Shapes.Sim`'s job, not the console's** — the console client only renders the board
live; it has no stats output of its own. Every `Shapes.Sim` run prints a metrics summary after the
pairing table: seat win rate and score margin (both with 95% intervals), game length, move usage,
merge frequency and merge take rate, unopposed-slot occupancy and streaks, cost pressure and
per-turn resource levels split winner/loser, creature survival, ending types, cards and moves
ranked **by take rate** (chosen ÷ times the play was legal), and a closing resolution line saying
how many cards the run still cannot rank. `--json PATH` writes full
per-game detail plus the metrics report; `--metrics-json PATH` writes just the aggregated
`MetricsReport`, stamped with `RunProvenance` so two reports can be diffed. Point `--agents` at a
single pairing and lower `--games` for a quick look at "how did that kind of game usually go"
rather than the full matrix.

**`--compare` is the CLI-first A/B diff** — reads two `--metrics-json` files (no games played) and
writes a standalone HTML report covering summary/scoring/economy stat tiles plus outer-joined
card and move tables, sorted by `|Δ take rate|`, colored only when the two Wilson/normal intervals
actually fail to overlap ("moved beyond noise" vs. "moved"). The metrics explorer's own diff view
(`--report`'s file picker) is the same idea done manually inside one report; this is the one-shot
version for the step 3/4 edit → rerun → compare loop.

**Read take rate before win rate.** Win rate compresses toward 50% under symmetric decks (both
seats hold every card, so most cards contribute a win *and* a loss in most games) and is a
correlation regardless. Take rate is a direct measure of what a strong agent chose when it had the
option, and is the metric a card-level balance change should move.

**A big matrix shows live progress, not silence until the end** — `Shapes.Sim` redraws a single
`completed/total games  rate  elapsed` line in place as games finish (only when stdout is a real
terminal; piping to a file or CI log skips it, so logs stay clean). Nothing to opt into — every
run gets it.

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

> ⚠️ **Open design questions for Phase 4, now measured (step 2):** is merge's
> stat-gain-vs-vulnerability-and-slot-cost tradeoff priced right? Confirmed not a free
> strictly-better action — both `ismcts` and `ismcts-heuristic` decline the majority of legal
> merges (~61-67%). Does an unopposed creature's double duty — scoring *and* paying income —
> compound into a runaway lead? Confirmed strongly correlated with winning (streak-vs-margin
> Pearson r = 0.73/0.48); a player who never held an unopposed creature 2+ turns running won zero
> sampled games. Both are real, non-degenerate design issues, and steps 3/5b/6 balanced against
> them rather than removing them: unopposed-slot rate settled at ~13.5% after the card sweep, and
> the scoring rule's dependence on removal is what step 5b's fatigue backstop exists to bound.

---

## 1. Design decisions

**Language/runtime: C# on .NET 8.** Godot 4 has first-class C# support, so Phase 5 is a client
swap, not a rewrite, provided the engine takes no UI dependency. `Shapes.Core` is a pure class
library (zero UI deps, enforced by test that it references only the BCL) — console, AI, tests,
and Godot are interchangeable consumers.

**Project structure:**
```
shapes/
├─ Shapes.Core/           # Pure engine: Primitives, State, Actions, Effects, Rules, Cards
├─ Shapes.Content/        # JSON card data + rules presets. NOT code.
├─ Shapes.Ai/              # IS-MCTS, determinizer, playout policies, evaluators
├─ Shapes.Console/         # Text client
├─ Shapes.Sim/              # Headless batch runner → CSV/JSON stats
├─ Shapes.Tests/            # xUnit
├─ Shapes.Godot.Adapter/    # Phase 5 only: GameSession/StateDiff/text formatting (plain class
│                           # library, not the Godot project itself -- see A1/A2 note below)
└─ Shapes.Godot/            # Phase 5 only, references Shapes.Core + Shapes.Godot.Adapter
```

**`Shapes.Godot.Adapter` is a real project, separate from `Shapes.Godot`, and wasn't in the
original plan.** A2's view-model layer (`GameSession`, `StateDiff`, text formatting) turned out
to need its own plain `Microsoft.NET.Sdk` class library rather than living inside `Shapes.Godot`
itself: the Godot.NET.Sdk's source generator requires a `GodotProjectDir` MSBuild property that
only the Godot editor/CLI supplies, so referencing `Shapes.Godot.csproj` directly from
`Shapes.Tests` fails outside the editor. `Shapes.Godot.Adapter` builds and tests under a plain
`dotnet build`/`dotnet test`, same as every other project; `Shapes.Godot` consumes it and stays a
thin shell (scenes/scripts only). Discovered while implementing A2 — see that step's notes.

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
step C2 migrates it).

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

**Notable bugs found:** destroyed creatures vanished instead of discarding, breaking the
determinizer's conservation identity (fixed via `GameState.DestroyCreature`). `GreedyAgent`
initially scored only *taking* an open slot, not *blocking* one — barely moved win rate but was
sharply visible in behaviour counts (recurring lesson: aggregate win rate against a weak opponent
partly measures the opponent). Availability-corrected UCB1 pinned by a sabotage-verified test,
since an uncorrected search still runs and still looks plausible.

**Exit criteria:** all met — search implemented end to end; satisfies the `IAgent` contract as a
theory case, not a parallel suite; mechanisms pinned by sabotage-verified tests; a full game
watched and read; `ObservedState` provably leaks nothing; console hides the AI's hand by default.
No win rate is asserted anywhere in this phase — see the note below.

> **On strength numbers in Phase 2.** Any figure quoted during this phase is an ad-hoc dev
> observation, not a result — a rate against a weak opponent partly measures the opponent, and
> every number is relative to card balance, which Phase 4 will change.

### Phase 3 — Agent measurement & optimization ✅ complete (9/9)

**Goal:** make the AI strong and *prove* each change helped. Cards/rules **frozen** all phase —
that's the whole reason this is a separate phase from Phase 4. Everything is gated on step 1: an
unmeasured optimization is a guess, and a bad heuristic can make search weaker while still
looking reasonable in a watched game.

- [x] **1. `Shapes.Sim`** — headless batch runner, both seats/pairing reported separately (never
  pooled), per-`ActionKind` behaviour counts. 23 tests. Baseline (30 games/pairing, 200
  iterations): `ismcts` beat `random` by ~97pt and `greedy` by ~77pt — required ordering, untuned.
- [x] **2. Playout policy** — `IPlayoutPolicy`; default stays `UniformPlayoutPolicy` (correctness
  control), `HeuristicPlayoutPolicy` (scores like `GreedyAgent`, against the real playout state)
  exposed as opt-in `ismcts-heuristic`. Measured stronger (beats plain `ismcts` both seats, wider
  margin vs. `greedy`) but ~1.3–1.9× slower per decision, so it stays opt-in rather than default.
- [x] **3–3c. Performance — profiled instead of guessed.** The planned apply/undo+node-pooling
  rewrite was built, measured, and **saved nothing** (`IsMctsAgent` never actually cloned in its
  hot loop). Profiling found 86.4%/iteration in playout `Generate`+`Apply` vs. determinization's
  ~6%, then three targeted fixes: **(3a)** tuned `PlayoutDepth` 400→200 from a measured length
  distribution, **2.1× speedup** (the big win); **(3b)** `PlayoutActionSampler.SampleOne`
  reservoir-sampling fast path for uniform playouts, **~1.07×** (smaller than expected — the
  list/hashset wasn't the expensive part); **(3c)** removed an unconditional defensive copy in
  `Board.RemoveDead()` and converted `EffectContext` to a `readonly struct`, **~1.09×**, stacking
  with 3b. Lesson: measured allocation/depth fixes won; both inspection-guessed rewrites (3 and 3b)
  underperformed their own premise.
- [x] **4. Determinizations per search — measured, not worth taking.** `IterationsPerDeterminization`
  (default 1) lets iterations share a sampled world. Timing across reuse windows 1–50 was
  indistinguishable from noise (matches determinization's ~6% ceiling); play quality unaffected
  too. Kept as a tested, unused-by-default parameter.
- [x] **5. Tuning — exploration constant.** Round-robin of 5 candidates found `c=1.0` best
  (53.3%) vs. the textbook default `sqrt(2)` (47.5%, tied-worst); confirmed head-to-head at 80
  games (56.2%). **`DefaultExploration` is now `1.0`**, picked up automatically everywhere.
- [x] **6. Re-verified correctness tests still pass** at the tuned setting — all 733 pass.
- [x] **7. Recorded the final agent matrix** as Phase 4's frozen reference (30 games/pairing, 200
  iterations, every Phase 3 change baked in): `ismcts` beats `random` by ~90pt and `greedy` by
  ~80pt — same required ordering as the untuned baseline. Frozen config: uniform playout, 200
  iterations, `PlayoutDepth = 200`, `IterationsPerDeterminization = 1`, `DefaultExploration = 1.0`.

**Exit criteria:** IS-MCTS decisively beats both baselines, and beats `RandomAgent` by a wider
margin than `GreedyAgent` does (the *ordering* is what proves search adds value); a decision
completes in target wall-clock (~≤2s desktop); every optimization has a same-seed before/after;
agent configuration is frozen and recorded.

### Phase 4 — AI-driven balance ✅ complete (14/14)

**Agents frozen** this phase — the mirror image of Phase 3. Every step below is a same-seed A/B
against the run before it; the full experiment record, every variant tried and why it was kept or
rejected, lives in `balance/LOG.md`.

- [x] **1–2. Metrics + the two flagged design questions.** `MetricsReport.From` aggregates a whole
  `BatchResult` (never pooling seats): per-seat win rate, length, per-card play/draw win rate,
  merge frequency normalized per creature played, endings. Answers: **merge is declined, not
  auto-taken** (33–39% of opportunities, a declined merge in 95.8% of games), and
  **unopposed-creature income compounding is real** — longest-streak vs. final margin r = 0.73/0.48,
  and a player who never sustained an unopposed creature 2+ turns won **zero** sampled games. Both
  confirmed as genuine design tensions, not null results.
- [x] **2b–2c. Made the metrics able to rank.** Wilson intervals on every rate (the normal
  approximation reports a 0/4 card as "0% ± 0" — the most confident-looking and least justified
  number available); **opportunity denominators**, making take rate the primary balance signal since
  raw play count conflates draw luck, affordability, and preference; score margin, a far
  lower-variance estimator than win rate (100 games: win-rate interval [46%, 65%] can't call it,
  margin [0.31, 1.29] excludes zero — same games); plus unopposed-slot occupancy, survival, and cost
  pressure. **Bug caught by cross-check, not inspection:** the first unopposed-slot version read the
  board from the seat *ending* its turn rather than the one receiving it, over-counting ~40% while
  looking entirely plausible in aggregate.
- [x] **2d–2e. Explorer + calibration.** One run is ~1,600 numbers for cards alone, so `--report`
  builds a self-contained HTML page whose **diff view is the point** — step 4 iterates edit → rerun
  → compare, which static text cannot do. Six deliberately mispriced spells then tested the
  detectors: **cost pressure separated them cleanly, take rate did not.** Standing caveat — take
  rate alone cannot rank economy/tempo-neutral cards.
- [x] **3–5. Rules sweep and tooling.** Settled 2/2/2 income with `incomePerCreatureType` removed,
  no hand limit, `scoreToWin` 10. `scoreByCreatureDelta` was the worst change tried — games stopped
  terminating. Console gained `EffectText`, which *synthesizes* card text from the op vocabulary
  rather than storing it, deliberately: a hand-authored description would drift from the numbers a
  balance edit changes. Metrics gained a **per-turn** take rate, separating "not urgent" from "not
  wanted" — identical on the per-decision denominator.
- [x] **5b. Fatigue — a structural tiebreak, so termination stops depending on removal.** A
  501-turn game traced to the scoring rule, not a card: **score requires an unopposed creature,
  unopposed requires a kill, so any board where defense ≥ offense stops scoring permanently.** A
  sweep of all 26 creatures in self-mirrors found **7 that stalemate their own mirror** — a property
  of the rule, and patching it card-by-card would mean banning self-heal and permanent max-health
  buffs as mechanics. Rule: empty deck at turn start gives the opponent 1 score. Chosen over
  fatigue-as-damage because damage resolves the *board*, and the failure mode is a board that cannot
  be resolved at all. Length percentiles landed here too — one outlier moved the reported mean from
  21.3 to 26.7, so a single mean actively misleads on exactly the shape a termination problem makes.
- [x] **6. Card sweep** — five paired 400-game runs, ~30 edits across 20 of 36 cards; balanced by
  the exit criteria. **Cost changes moved cards; magnitude changes usually did not** (`def_stance`
  absorbed three buffs to its *effect* unmoved, then jumped +0.85 z the moment its cost fell).
  Reworks beat tuning where the problem was structural (`circle_bender` +2.18 z only after both
  moves were replaced). **The methodological finding matters more than any card:** re-running an
  identical configuration under a different seed moved the mean card **0.36 z** and one card
  **1.34**. **Read any single-run per-card delta under ~0.6 z as no evidence** — 400 games ranks
  *groups*, not individuals.
- [x] **7. Game length — a global sweep, not another card pass.** Length hadn't moved across the
  entire step-6 sweep, which is the evidence it was never card-level. **Result: `scoreToWin` 7 plus
  a global cost increase**, 16.2 turns. **The pairing is the finding** — alone each half was the
  *worst* option on its axis (the cost rise was the only variant slower than baseline; the bare
  threshold cut spiked seat 1 to 60.3%), but together they cancel and post the sweep's best margin.
  No single-lever run could have predicted it.
- [x] **8. First-player balance — closed with a seat-2 compensation, not content tuning.** The cause
  was pre-play: seat 1 develops a full turn first and the gap never closes (unspent resources +2.0
  to +2.2 per type all game — almost exactly one turn of `BaseIncome`, the signature of a one-time
  debt that compounds). Applied at one engine seam, `ApplySecondSeatCompensation()`, because three
  callers set a game up and a run that silently skipped it would report an asymmetry the ruleset had
  already fixed. **1/1/1 resources plus 1 card** took the margin from +0.79 to **−0.20 [−0.59,
  +0.19]**. The pairing is again the finding: +2 cards passes on margin while leaving seat 2's
  economy *below where it started* — the "papered over rather than fixed" failure, caught only by
  reading per-seat curves alongside the outcome. **Starting score was inert and ships at 0.**
- [x] **9. Final pass — settled at `v1.7-final`**, 23 of 36 cards changed. Margin **−0.32 [−0.68,
  +0.04]**, median length 15, fatigue 1.75% at 400/400 terminating, resource types within **0.06 z**.
  **The deviation is the finding.** Its own budget ("change only cards whose |z| ≥ 0.6") assumed a
  per-card problem; the first two passes instead drifted on *game shape* (games ≥30 turns 8.8% →
  16.8%, fatigue deciding up to 8.3%). Reading a `--json` dump of the long games found a cause no
  z-score could show: **creature supply (4.55 HP/turn) structurally exceeds damage throughput (2.30
  HP/turn)**, so once slots fill, replacement absorbs removal and unopposed slots stop appearing.
  **Healing was not the cause** — damage exceeds it ~8:1, identically in short games. So the fix was
  a deliberate 20-card structural pass **over the stated budget**, reversing all three shape metrics
  to better than baseline. The budget rule stands for card tuning; it does not apply when the
  diagnosis is a whole-format ratio. Resource parity was the other win, cause found rather than
  tuned: `rally` alone was ~57% of spike's generation budget. **A 4000-game rerun** then confirmed
  step 6's noise floor directly — several 400-game "findings" evaporated, and 27 of 36 cards
  separated from the field median versus 16 at 400. Sample size, not another metric, made per-card
  ranking possible.

**Exit criteria:** all four met at `v1.7-final`. Take rate/turn spans 23–46% with no card outside
it; median length 15 (p95 32), 400/400 terminating, fatigue 1.75%; merge and income-compounding
confirmed as real, non-degenerate decisions (step 2); first-player margin −0.32 [−0.68, +0.04].
Step 9 additionally settled resource-type power parity (0.06 z) and unspent-resource parity (0.25
spread), neither an exit criterion and neither previously met.

**Carried into Phase 5.** `enrage` (−2.06 at 4000 games) is the set's one dead card; `anchor` and
`basic_square::Jab` the clearest overperformers; spells run −0.43 against creatures' +0.10 and
cost-1 cards +0.58 against ~−0.1 elsewhere. None breaks an exit criterion, and all are better
judged against real decks. **The one open item:** at 4000 games the margin reads −0.28 [−0.40,
−0.16], *excluding* zero where the 400-game criterion straddles it — a small real seat-2 edge the
criterion's own sample size cannot see. Likely step 8's compensation being over-generous now that
games run ~2 turns shorter; the fix is a ruleset knob, not a card edit, and Phase 5's custom decks
will move it again. **Archetype balance was always out of scope** — not measurable on a symmetric
deck where both seats hold every card.

### Phase 5 — Godot client (desktop + mobile)

Target Windows/macOS/Linux desktop and Android from one codebase — achievable for a turn-based
card game, but constrains layout/input from the first scene.

**Organised as four milestones, not a flat checklist.** The previous numbering read as a build
order and wasn't one: responsive layout and touch input are *constraints on* the board scene, not
steps after it, and the deckbuilder can't start until one game is playable end to end. Milestone A
is the whole game on one screen; B makes it good; C fans out into the other scenes; D ships it.
Within a milestone the order is real.

**The extensibility rule for the whole phase: everything new lives in `Shapes.Godot` or
`Shapes.Godot.Adapter`** (the latter split out during A2 for build reasons — see the
project-structure note above; it is still UI code, not engine code). The exit criterion is
`Shapes.Core` unmodified from Phase 4, and the way that criterion gets broken is gradually — a
render-only enum member here, a UI-shaped convenience property there. Treat any urge to add to
Core as the signal that the adapter (A2) is cutting at the wrong seam.

#### Milestone A — one playable screen (hotseat, no art)

The target is the full rules running under a finger: every card, all targeting, no art and no
animation. Done when a seeded hotseat game in Godot reaches the same result as the same seed in
the console.

- [x] **A1.** Added `Shapes.Godot`, referencing `Shapes.Core` unchanged (plus `Shapes.Ai` and
  `Shapes.Content`, needed for `CardDatabase`/`AgentContext` the same way the console references
  them). Godot 4.5 C#/.NET; added to the root `Shapes.sln` so `dotnet build` from the repo root
  builds all 8 projects with zero errors/warnings — CI and tests never need the editor. **Bug
  caught mid-step:** an intermediate `dotnet build` triggered Godot/MSBuild to rewrite the root
  `.sln` and silently drop the actual `Shapes.Godot.csproj` reference, leaving only an empty
  solution-folder stub — `dotnet sln add` had to be re-run after. Renamed the Godot-generated
  `Shapes.csproj`/`Shapes.sln` to `Shapes.Godot.csproj`/`Shapes.Godot.sln` for naming consistency
  with the rest of the solution (safe pre-A2, since no `.cs` files existed yet to reference the
  old assembly name).
- [x] **A2. Adapter layer.** `GameSession` (owns the one `GameState`, mirrors the console's exact
  setup sequence — symmetric decks, shuffle, draw, second-seat compensation, advance to actions —
  so a seeded Godot game matches a seeded console game) and `StateDiff`/`SlotDiff`/`PlayerDiff`
  (built by diffing `GameState.Clone()` before/after `ActionExecutor.Apply`, exactly as specified,
  never reading `TurnEvents`). **Had to become its own project, `Shapes.Godot.Adapter`, not code
  inside `Shapes.Godot`** — see the project-structure note above; discovered when referencing
  `Shapes.Godot.csproj` from `Shapes.Tests` failed outside the Godot editor. 12 xUnit tests
  covering setup-sequence parity, legal-action delegation, the clone-before-apply property (a
  regression guard for the exact bug this layer exists to avoid: diffing against a live reference
  would make every diff read empty), and a full seeded game driven end-to-end via nothing but
  `Submit`/`LegalActions` to termination with a winner.
- [x] **A3. Board scene under the responsive + touch constraints.** Full vertical slice: `GameRoot`
  (owns `GameSession`, the only script that submits `GameAction`s) driving `BoardView`
  (turn bar, end-turn button, both `PlayerPanel`s, `MoveMenu`, `GameOverPanel`), each
  `PlayerPanel` holding 3 `SlotView`s (≥44px touch targets) and a hand row of `CardFace`s, plus a
  tap-triggered `CardDetailPanel` standing in for the desktop-tooltip information a hover model
  can't give a touch client. Anchors/containers throughout, no fixed pixel layout. Waiting seat's
  hand renders as a count only, carrying step 2.5's hidden-hand precedent into Godot. Also
  pre-built `ResourceIcons`/`ActionText`/`CardText` in `Shapes.Godot.Adapter` (A4's actual text
  synthesis, needed early since there's no rendering a card without it) — ported from
  `Shapes.Console`'s versions rather than shared, since `Shapes.Console` is structurally
  unreachable from a Godot-SDK project. **Three real bugs found by playtesting, not review:**
  (1) `CardDetailPanel` unconditionally called `Pressed -=` on a handler that was never connected
  on the first show — a hard error in Godot's C# signal binding (unlike a plain C# event's silent
  no-op) that aborted the rest of the tap handler; fixed by tracking connection state explicitly.
  (2) `BoardView.ClearSelection` → `MoveMenu.Close` → unconditional `Cancelled` event →
  `ClearSelection` again was unbounded mutual recursion, freezing the whole editor since it ran
  synchronously on every single `Submit` call (any action at all, not just one path) — fixed by
  splitting "close silently" from "close because the user cancelled." (3) The Play button's
  success path called `Hide()`, which fired `Closed` unconditionally — and `Closed` is wired to
  `ClearSelection`, which wiped the `PendingPlacementCardId` a creature-card play had just set, in
  the same call stack, before the next slot tap could ever see it (read as "tapping a slot
  deselects instead of placing"). Fixed by only firing `Closed` on an explicit user dismissal, not
  on every panel close. All three were invisible to the type checker and to `dotnet build` —
  found only by actually playing a hotseat game in the editor, the exact lesson step 2.4's
  blocking-slot bug already taught the console.
- [ ] **A4. Card rendering via `EffectText`** — `EffectText.Describe`/`DescribeMove` synthesize
  card text from the op vocabulary (Phase 4 step 3–5) and the Godot card face calls them. A
  hand-authored description in scene data would drift from the numbers the next balance edit
  changes, which is the exact failure `EffectText` was built to prevent; a second description path
  reintroduces it.
- [ ] **A5. Target-selection UI** over the existing `chosen_*` actions — one state, no chaining,
  thanks to the single-target rule. **Before animation, not after:** targeting is the last
  *functional* piece (about a third of the set touches chosen selectors, and those cards are
  unplayable without it), and animating an incomplete action space means reworking it once
  targeting adds an intermediate UI state.
- [ ] **A6. Undo, or an explicit decision against it.** A misclick in an atomic, repeatable action
  model is inevitable, so this is a decision, not a feature request — and it changes the UI either
  way (no-undo means confirmation steps on destructive actions). **Note the mechanism honestly:
  Core has no undo API.** `Clone()` *is* the undo mechanism (see `ApplyUndoSymmetryTests`, and
  Phase 3 step 3, where the apply/undo-record rewrite was built, measured, and found to save
  nothing). So UI undo means snapshotting a clone per action and restoring it — cheap at human
  turn scale, and it needs no Core change. Scope it to within-turn only: undo across `EndTurn`
  would have to un-draw a card.

#### Milestone B — make it feel like a game

- [ ] **B1. Real card art and animation** — play/move/merge/score/destroy, driven by A2's diff.
- [ ] **B2. AI opponent via `IAgent`** (difficulty = search budget), off the main thread and capped
  on mobile. `Choose(AgentContext, CancellationToken)` already takes the token, so the seam exists
  — **what's missing is the policy**: what the player sees during a ~2s search, and what happens
  when the app is backgrounded mid-search. Cancel-and-restart on resume is the safe default;
  decide it here rather than discovering it on a device.
- [ ] **B3. Interrupted-game persistence** — mobile is the platform that kills a backgrounded app
  mid-turn, so "resume game" is a different problem from "save deck" and is scheduled separately
  from it (C3). Two viable mechanisms: serialize `GameState` (needs RNG stream position,
  `PendingDiscards`, `MergedFrom` chains, `TurnEvents` — all of it, correctly), or replay
  seed-plus-action-log, which Phase 1's determinism guarantee already makes sound and is the
  cheaper bet. Pick one deliberately; a half-serialized state that desyncs is worse than no resume.
- [ ] **B4. Tutorial / rules surfacing** — **the item the old plan omitted entirely, and the
  difference between playable and learnable.** Nothing about the ruleset is self-evident from a
  board: a rock-paper-scissors type cycle, merging that can *increase* vulnerability, scoring that
  requires an unopposed slot, and fatigue. The console gave players that context in text and the
  Godot client gives them none. Minimum bar: the type cycle legible on the board itself, and a
  reachable rules reference.

#### Milestone C — the other scenes

- [ ] **C1. Lobby / match setup** — seat choice, opponent (human hotseat or AI difficulty), ruleset.
  Small, but it's what stops A-milestone launch config from calcifying into hardcoded scene state.
- [ ] **C2. Deckbuilder** (`deckMode: "custom"`) — also owns migrating the determinizer off its
  symmetric-deck assumption, since custom decks make the opponent's decklist itself hidden (a
  belief-distribution problem, not just a partition problem). Most of `Determinizer` and its test
  suite are unaffected (phrased against observations, not deck provenance); only `UnseenCardsOf`
  changes, to sample from a belief model instead of reading `BuildSymmetricDeck` — the file's own
  comments already anticipate exactly this edit, and `Determinize` throws on a non-symmetric
  ruleset today so the unmigrated path fails loudly rather than silently sampling nonsense. First
  belief model: constrain to cards demonstrably played, fill the rest uniformly within
  deck-size/copy limits — crude but sound, same justification as Phase 2's uniform sampling.
- [ ] **C3. Persistence** (`user://`): decks, settings, progress — the durable-data half, B3 having
  taken the interrupted-game half.
- [ ] **C4. Card browser / stats** — the collection view, reading `CardDatabase` and `EffectText`.

#### Milestone D — ship

- [ ] **D1. Polish:** sound, transitions, menus. Audio wants an asset-source decision *before* this
  step rather than during it.
- [ ] **D2. Export pipeline** (desktop + signed Android `.aab`), reusing/re-verifying the step 1.13
  toolchain rather than rediscovering it: export templates need the .NET 9 SDK alongside .NET 8,
  Editor Settings needs explicit Java/Android SDK paths, and rebuilds need `adb install -r` or a
  stale APK silently masks the change.

**Exit criteria:** full game playable with visuals on desktop and on a physical Android device;
a seeded hotseat game matches the console's result for the same seed; deckbuilder validates
against engine rules; AI plays custom decks without assuming a mirrored opponent decklist; a
backgrounded game resumes; a new player can learn the type cycle without external explanation;
`Shapes.Core` unmodified from Phase 4.

**The console and `Shapes.Sim` remain the card pipeline — permanently, not transitionally.** They
answer questions Godot structurally cannot. `Shapes.Sim` is where a card is *measured*: Phase 4
step 9 established that 400 games ranks groups and 4000 is where per-card ranking becomes
possible, a regime no interactive client enters. The console is where a card is *read* — step 2.4's
blocking-slot bug was invisible to a passing suite and surfaced only by watching a game. Godot adds
a third question the other two can't answer: is the card legible and satisfying to a human? So the
loop for new content stays: author JSON → console watch (does it work) → sim sweep (is it
balanced) → Godot (does it read). **One caveat once C2 lands:** every number in `balance/LOG.md` is
against symmetric decks, and cards measured on custom decks are a different experiment — keep those
runs in separate directories rather than comparing them to `v1.7-final`.

**On the carried-over seat-2 margin.** Phase 4 left a small real seat-2 edge (−0.28 [−0.40, −0.16]
at 4000 games). Deliberately *not* scheduled in this phase: the fix is a ruleset knob, and C2's
custom decks will move the number again. Re-measure after C2, not before — tuning against a
symmetric-deck margin that is about to be invalidated would be balancing twice and trusting the
wrong one.

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
