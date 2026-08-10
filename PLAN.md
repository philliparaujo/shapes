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
| 5 — Godot client                         | 10 / 18    |

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
- [x] **A4. Card rendering via `EffectText`** — `EffectText.Describe`/`DescribeMove` synthesize
  card text from the op vocabulary (Phase 4 step 3–5) and the Godot card face calls them. A
  hand-authored description in scene data would drift from the numbers the next balance edit
  changes, which is the exact failure `EffectText` was built to prevent; a second description path
  reintroduces it. **Landed early, inside A3** — `Shapes.Godot.Adapter`'s `CardText`/`MoveText`
  wrap `EffectText.Describe`/`DescribeMove` (plus `ResourceIcons` for cost/type icons), and
  `CardFace.Render`, `CardDetailPanel.ShowCard`, and `MoveMenu.Open` all consume them, so no card
  or move string in the client is hand-authored. `CardTextTests.cs` (5 tests) pins real synthesized
  output — creature move text, a gated move's condition prefix, spell effect text, and free-cost
  move rendering — so a silent fallback to a raw op name would fail loudly. Confirmed against the
  current tree: full solution builds with 0 warnings/errors, all `CardText`/`EffectText`/Godot
  adapter tests pass (44/44).
- [x] **A5. Target-selection UI** over the existing `chosen_*` actions — one state, no chaining,
  thanks to the single-target rule. **Before animation, not after:** targeting is the last
  *functional* piece (about a third of the set touches chosen selectors, and those cards are
  unplayable without it), and animating an incomplete action space means reworking it once
  targeting adds an intermediate UI state. `BoardView` now remembers the legal `ChosenTarget`
  actions from `BeginTargeting` and resolves the next `SlotTapped` back to the specific
  `GameAction` via `TryResolveTarget`, covering all three places a target can be needed: a
  move (`OnMoveTapped`), a spell play (`SubmitPlayCard`), and a creature play whose placement
  slot is chosen first and *then* needs a target (`OnSlotTapped`'s placement branch — the
  creature's own `TargetSlot` is fixed before its `ChosenTarget` options are computed, since
  the legal targets can depend on where it lands). Added an explicit "Cancel Targeting" button
  (`BoardView.tscn`) rather than relying on a stray tap to cancel — a miss-tap on a
  non-highlighted slot is a no-op, not a cancel, so a misclick during targeting can't silently
  drop the in-progress choice. **One re-entrancy fix needed:** `PlayerPanel.RenderSlots` rebuilds
  every `SlotView` from scratch on each `Render` call (same as the state A3 already tracked
  outside `Render`), which would silently drop `SetHighlighted` targeting marks the next time
  anything triggers a refresh; `BoardView.Render` now reapplies the current targetable set
  from `_pendingTargetActions` after re-rendering both panels. **Not editor-verified** — no
  Godot editor/CLI was available in this environment (same limit noted for `Shapes.Godot`
  throughout: it isn't `dotnet test`-reachable outside the editor, per the A2 project-structure
  note), so this shipped on a careful hand-trace of the event flow plus a clean `dotnet build`,
  not the playtesting A3's own bugs required to catch. Flag for a real editor pass before B1.
- [x] **A6. Decided against undo — no confirmation dialogs either.** A misclick in an atomic,
  repeatable action model is inevitable, so this was a decision, not a feature request, and the
  mechanism was cheap either way: `GameSession.Submit` already clones `_state` before every
  `ActionExecutor.Apply` to build the `StateDiff` (`Clone()` *is* the undo mechanism — see
  `ApplyUndoSymmetryTests` and Phase 3 step 3, where the apply/undo-record rewrite was built,
  measured, and found to save nothing), so a snapshot-and-restore undo needed no new Core surface
  and would have been genuinely cheap to add. **Rejected anyway, for a reason engineering can't
  fix: a draw is a reveal, not a state change, and undo can erase state but not what the player
  already saw.** A move or spell that draws a card, or an overdraw burn, shows the player
  something and *then* the undo would have to pretend it didn't — the `GameState` reverts fine,
  but the player's memory of the top card doesn't, so undo-past-a-reveal is a real information
  leak no amount of correct snapshotting removes. That forced a sub-decision (undo everything and
  accept the leak, or wall undo off at the first reveal each turn) before any UI could be built,
  and the simpler answer was to skip undo entirely: **every action is a committed decision, the
  same Hearthstone-style stance the atomic single-action model was already built around** (an
  action already carries a fully resolved choice — see `GameAction`'s own header — not a draft
  a player edits before confirming). Confirmation dialogs were considered and also rejected as
  redundant: merge/move are freely repeatable and low-stakes, and A5's targeting UI already forces
  a two-tap sequence (select, then tap the actual target) that functions as an implicit confirm
  for anything with real consequences. **No UI or Core change from this step** — it closes as a
  design note, not a build.

#### Milestone B — make it feel like a game

**B1 grew from a one-line "art and animation" pass into an interaction-model rewrite, decided
before any of it was built (2026-08-08).** Two gaps surfaced that aren't cosmetic: (1) A3's
tap-select-then-tap-target flow, while deliberately touch-first, doesn't match genre convention
(Hearthstone-style drag-and-drop), and (2) `SlotView`/`CardFace` only ever read
`Health`/`MaxHealth`/`Types`/`IsMerged` off `CreatureInstance` — moves, `Keywords`
(taunt/reflect/ricochet), `IsStunned`, `AttackBuff`, and the one-shot `NextAttackBonus`/
`NextDamageTakenBonus` are all tracked by Core today (see `CreatureInstance.cs`) and none of it
reaches the board. Both are real reworks of A3/A5's interaction layer, not a skin over it, so they
get their own sub-steps rather than hiding inside "animation." Sequenced before B1's art and
animation passes (B1c/B1d) for the same reason A5 was sequenced before B1 originally: animating a
slot view that's about to be redesigned for status icons and move buttons means reworking the
animation once the redesign lands.

- [x] **B1a. Drag-and-drop replaces tap for play/merge; moves become always-visible buttons, not
  drag targets.** Splits by action kind: **play** a card by dragging it onto a board slot
  (`SlotView._CanDropData`/`_DropData`) or, for a targetless spell, onto the self panel's
  background (`PlayerPanel`) — replacing A3's tap-card→tap-slot flow. A spell needing a `chosen_*`
  target can also be dropped directly on the enemy creature it targets, resolving immediately;
  A5's tap-to-target UI remains the fallback only when the drop can't supply both a placement slot
  and a chosen target at once. **Merge** is drag a friendly creature onto an adjacent friendly one
  (`MoveMenu`, which used to bury this option, is deleted along with `MoveMenu.tscn`). **Using a
  move is deliberately NOT a drag** — `SlotView` renders a `MoveList` of always-visible buttons for
  the creature's currently-usable moves, since a drag alone can't disambiguate 2+ legal moves onto
  the same target; a move needing a `chosen_*` target still finishes with A5's tap-to-target.
  **Discard stays tap-based** (`AwaitingDiscard` has no drag precedent). New `DragPayload` packs a
  hand-card-id or source `SlotIndex` into the `Variant`-shaped dictionary Godot's drag API
  requires; `_CanDropData`/`_DropData` only ever *report* a drop, and `GameRoot` re-checks every
  one against real `LegalActions()` before submitting — the same "view reports, GameRoot decides"
  split every gesture here follows. **`CardDetailPanel` (A3/A4) is deleted, not just unused** —
  playing is drag-only, with no tap-to-play or tap-to-inspect fallback; a tap on a hand card now
  does nothing outside `AwaitingDiscard`.
  **Found only by playtesting, not review, same as A5's history:** (1) `SlotView`/`CardFace` were
  originally a wrapper `Control` around a child `Button`; Godot dispatches `_GetDragData`/
  `_CanDropData`/`_DropData` to whichever `Control` is under the mouse, and the topmost `Button`
  absorbed every drag gesture before the wrapper's overrides ever ran — dead on arrival despite a
  clean build. Fixed by making both scenes' root node the `Button` itself. (2) Move-button effect
  text was `TooltipText`-only, violating A3's "no hover-dependent information" rule on a touch
  client with no hover — moved into the label with `AutowrapMode`. (3)/(4) Drop-target fallthrough
  gaps (a spell dropped on an occupied slot, or directly on its own target) — fixed by routing both
  through the same resolution paths their non-drag equivalents already used.
  **Two more real layout bugs, chased through several fix attempts each:** a small default window
  plus **`CustomMinimumSize` being a floor, not a cap** — a `(0, 48)` move button let Godot size
  the whole board row to its widest *unwrapped* line instead of ever wrapping, and nesting a
  `ScrollContainer` inside it didn't bound anything either, since a `ScrollContainer`'s own
  `custom_minimum_size` is the same kind of floor. Root layout bug underneath both: `BoardView`
  split the window a fixed 50/50 between panels, so real content growing past its half pushed into
  the other panel instead of the window growing. Resolved per explicit direction (no scrolling, no
  truncation, less density instead): hand cards show name+cost only per move (full text deferred
  to what became B1a2's hover view); board slots keep full move text inside a real fixed budget —
  `MoveButtonFactory` buttons at 240×42 with `ClipText`/an 11pt floor, `SlotView` itself clipped —
  sized off the true worst case (`MaxMergeDepth` 2 × every real card having exactly 2 moves = 4
  moves, never more), so a scroll-free fixed height is sound rather than a guess. Window default
  grew to 1600×1000 to fit the arithmetic. Unusable moves render disabled/dimmed rather than
  omitted (`IsUsable` flows through to `Button.Disabled`), so "no moves" and "one move I can't use
  yet" don't look identical.
- [x] **B1a2. Hover detail view — the debt B1a's compacting left behind**, closing the "shown on
  hover" promise B1a's text-reduction made rather than leaving it a dangling comment. Desktop-only
  (mobile dispatches no `MouseEntered`/`MouseExited`), additive over B1a's tap/drag model:
  hovering a hand card or board slot shows `HoverDetailPanel` with the same full
  name/cost/stats/effects/move-text `CardDetailPanel` used to (no Play button, `mouse_filter =
  Ignore` throughout so a tooltip can never itself be a click target). `CardFace`'s hand card is
  exactly one `CardDefinition`'s `CardText`; a `SlotView`'s board creature shows the *merged* move
  list across every card folded in via `MergedFrom`, so `HoverDetailPanel.Show` takes plain fields
  with a `CardText`-shaped convenience overload for `CardFace`'s case, rather than forcing one
  payload shape. Also the natural home for B1b's later status detail, once it lands.
  **Six playtest rounds, each catching a real bug the previous one didn't — the load-bearing
  lessons, not the blow-by-blow:** early rounds anchored the tooltip to the hovered control (grow
  upward for hand cards since they sit on the bottom row, reflow neighboring cards via spacers to
  avoid overlap) and chased a sequence of real but narrow bugs from that choice — downward-only
  growth, a stale-size read racing a same-frame `AddChild`'s own deferred layout, a spacer race
  between adjacent cards' hover-start/hover-end. **Root cause, once named plainly: a tooltip that
  repositions itself off what's hovered has to get that repositioning right against every screen
  edge, control shape, and content size, and each fix closed one case while the next playtest found
  another.** Replaced entirely with a fixed box in the bottom-left corner of the screen that never
  moves regardless of what's hovered (`HoverDetailPanel.tscn` anchored, ~252×260, 12px from the
  edges); `SlotView`/`CardFace` now only say *what* to show, never *where*.
  `PlayerPanel.RenderHand` adds left padding (`HoverPanelClearanceWidth`) so the first hand cards
  don't render under the now-permanent box. Sibling-reflow-on-hover was dropped outright rather
  than fixed a third time, once the fixed-position tooltip removed the overlap it existed to avoid.
  **Two structural fixes landed alongside the tooltip work, per explicit request:** the status bar
  consolidated (score/resources/turn label/end-turn button all in one `BoardView`-level `StatusBar`
  instead of scattered per-panel rows), and `OpponentPanel`/`SelfPanel`'s split tightened from an
  even 50/50 to 20/80 — a deliberate fixed ratio (not content-based sizing, which would make the
  self panel's height jump whenever the opponent's board grows tall mid-turn) reflecting that only
  the self panel needs room for a full hand row. Hovering an opponent's board creature also showed
  no moves at first — `RenderSlots` gated the move *list* on ownership when only the move *button*
  should be, since "can this be clicked" and "what can this creature do" are different questions;
  `SlotView.Render` gained an optional `hoverMoves` parameter to decouple them.
  **Not editor-verified beyond those six playtests** — same standing limitation as every Godot-side
  step this phase; verified by `dotnet build` (clean) and the full test suite, not by hovering
  again.
- [x] **B1b. Status/keyword display on the board slot, at a glance, no tap required.** New
  `StatusIcons.Describe(CreatureInstance)` (`Shapes.Godot.Adapter`, 17 tests) returns one
  `StatusBadge` (glyph + tooltip) per active status — shield=taunt, mirror=reflect,
  arrow=ricochet, lightning=stun, `+N atk` as text rather than an icon since it's a number worth
  reading directly, and the one-shot `NextAttackBonus`/`NextDamageTakenBonus` triggers each get
  their own icon since they silently change the next combat's math. Taunt distinguishes persistent
  from `until_next_turn` (dimmed via `IsExpiring`) using a new public
  `CreatureInstance.TauntExpiresNextTurn` getter, the one read-only `Shapes.Core` exposure this
  step needed. Badges render as their own `Label`s in an `HFlowContainer` (so the attack buff can
  carry its own color/size, amber/14pt, distinct from health and the dimmer glyph badges) and fold
  into the B1a2 hover stat line rather than needing a second hover mechanism.
  **Also reworked `SlotView`'s layout**, since a merged creature's concatenated name ("Circle
  Cadet+") was pushing the whole slot taller: resource/type icons moved inline with the name on a
  `HeaderRow` where only the name label expands/wraps, health moved to a `StatusRow` shared with
  the new badges. **Not editor-verified** — verified by `dotnet build` (clean) and the full test
  suite; subsequently playtested and confirmed, as every B-milestone step has been.

**B1's original "art and animation" line splits into B1c (art) and B1d (animation),
2026-08-08.** They share a sentence in the old plan but almost nothing as tasks: art is a
layout-and-asset problem, animation is a node-lifetime problem, and each has a definition of done
the other doesn't affect. Bundling them means animation blocks on an asset decision it doesn't
need. Same reason B1a spawned B1a2 — scope that surfaced on contact gets its own step. **Art keeps
the B1c number deliberately:** eight source comments across `Shapes.Godot`/`Shapes.Godot.Adapter`
already cite "PLAN.md B1c" for art concerns (card proportions, art placeholders, cost badges,
per-source-card art panes), and renumbering art would invalidate every one of them.

- [~] **B1c. Real card art — IN PROGRESS: pipeline done, 5 of 36 cards authored.** Replaces
  placeholder geometry on card faces and board slots with actual artwork. Layout groundwork landed
  first (`CardMetrics` centralizes card proportions; `ResourceIconFactory` draws type shapes as
  both cost badges and a full-panel placeholder; `SlotView` renders split art for merged
  creatures). **Asset-source decision: generated raster art, authored at 2:1** (1774×887), derived
  from the four aspect ratios the same art must survive (7:5 in hand/tooltip, 2:1 in play, ~1:1 per
  pane when merged) — 2:1 is the widest target and the centered square is the intersection of all
  four, so the authoring rule is subject inside that centered square, focal point ~42–45% from the
  top (biased up since every art region sits under a title band and nothing crops vertically).
  **`CardArt.For(cardId, fallbackType)` is the whole seam** — resolves `res://art/cards/{cardId}.png`,
  falls back to the placeholder when absent, which is what makes the set fillable one card at a
  time. **Keyed on the card ID, never the JSON filename** — `safeguard.json` still carries the id
  `patch_up` (renamed in v1.7; the id stayed because `balance/` history keys off it), so `CardText`
  carries a `CardId` field reaching every art site. `CardArtTests` asserts every art file names a
  real card id, verified failing on a planted misnamed file first — a silent placeholder fallback
  is exactly what makes that invisible otherwise. **Two rendering details are load-bearing:**
  `StretchMode.KeepAspectCovered` (crop, never letterbox) and `ExpandMode.IgnoreSize` with a zero
  `CustomMinimumSize` (without it, the 1774px-wide source imposes a 1774px floor on every layout it
  appears in). First playtest confirmed the two risk cases (merged 1:1 panes stay legible; type
  legibility survives the placeholder→art transition). **Remaining: author the other 31 cards** —
  dropping a correctly named PNG in is the entire per-card cost; texture import settings, atlasing,
  and the mobile texture budget (~2.4MB per source PNG × 36) are deliberately deferred until the
  set is complete.
- [x] **B1d. Animation driven by A2's diff** — play/move/merge/damage/heal/destroy/score, over the
  drag-based interactions and status-aware slot view B1a/B1b established. Sequenced last in B1
  because it's the piece that would otherwise need redoing. **A2's seam is finally consumed:**
  `GameSession.Submit` has returned a `StateDiff` since A2 with nothing reading it; `Submit` now
  captures it and passes it to `BoardView.PlayAnimation` after `RefreshAll`.
  **Resolved the step's central choice — overlay, not node reconciliation.** `PlayerPanel`'s render
  path still `QueueFree`s and rebuilds every `SlotView`/`CardFace`; `BoardAnimator` is a
  mouse-transparent full-rect `Control` above the board that spawns its own short-lived nodes at
  slot positions and frees them. Reconciling node identity was the alternative, rejected on risk:
  it would touch every event-rewiring path in `PlayerPanel`, precisely where A3's playtest bugs and
  A5's dropped-highlight bug came from. **The honest cost:** this animates *at* slots, so a move is
  a ghost frame at the destination rather than the card sliding — a real polish ceiling, with
  reconciliation still available later without rewriting `AnimationScript`.
  **`AnimationScript` (Adapter, pure, 12 tests) derives order the diff cannot** — a `StateDiff` is
  an unordered set, so this rejoins a departure+arrival pair (same `CardId`/`Health`) into one
  `Move`, and imposes the cue ordering move/play → merge → damage → heal → destroy → score.
  **Damage-before-destroy is the load-bearing rule** — a killing blow is both, and the reverse
  order animates the damage number over an already-empty slot. **Input policy: animations never
  block input**, following from A6 — the state has already changed before anything draws, so every
  effect is self-contained and self-freeing rather than a queue that must drain.
  **Two real bugs found post-playtest (2026-08-09), neither visible to the type checker or the
  test suite:** (1) `BoardAnimator.Place` set a spawned node's `Position` before its `Scale`; since
  `Ghost`'s frame scales around a centered `PivotOffset`, applying scale *after* position dragged
  the rendered corner right/down by `pivot * (1 - scale)` — fixed by scaling first, positioning
  last. (2) `PlayerPanel.RenderSlots`/`RenderHand` called `QueueFree()` on old children without
  `RemoveChild()` first; since `QueueFree` only marks a node for deletion at end-of-frame, the
  container briefly held both the dying and the new children in the same frame `BoardAnimator`
  reads `GlobalPosition` from, which is what actually put animations at the wrong slot (the
  pivot-scale bug was real but too small to be the reported symptom). Fixed by `RemoveChild`ing
  immediately. **Still wants a real editor playtest to confirm both fixes** — verified so far only
  by `dotnet build` (clean) and the full suite, the same standing limitation as every Godot-side
  step this phase.

#### Milestone C — the other scenes

**B2/B3/B4 moved here from Milestone B, after C1/C4, 2026-08-09.** All three assumed an AI
opponent was imminent, and B2 specifically would have pushed hotseat toward a secondary mode
rather than the primary one it still is. Deferring them past the lobby (C1) and card browser (C4)
keeps 2-human hotseat as the mode worth investing in near-term — more play-screen settings and
polish on the game that already exists, rather than clearing room for AI by cutting hotseat early.
Renumbered C5–C7 to keep the milestone's own step order meaningful rather than leaving gaps.
**C5's core then pulled forward into C1 the same day**, once C1 was actually being built: a lobby
that offers an AI difficulty toggle and then does nothing when it's picked is worse than not
offering it, so C1 below ships a working AI seat rather than a decorative one. What stayed behind
in C5 is specifically the parts that need real design (off-main-thread search, cancellation,
backgrounded-app policy), not "AI opponent" as a whole.

- [x] **C1. Lobby / match setup, including a working AI seat (C5's core pulled forward).** Player
  choice per seat, independently — Human, Random, Greedy, IS-MCTS, or IS-MCTS with the heuristic
  playout policy — so 0, 1, or 2 human players are just two independent pickers rather than a mode
  switch; ruleset choice is not yet exposed (still `RuleSet.Default` only, C2's deckbuilder is the
  natural point to revisit that). `Lobby.tscn`/`Lobby.cs` is the new `run/main_scene`, replacing
  `GameRoot.tscn`'s old role; `ChangeSceneToFile` into `GameRoot.tscn` on Start, with the chosen
  `MatchConfig` carried across via `PendingMatch` (a plain static field set immediately before the
  scene change and consumed once in `GameRoot._Ready` — Godot has no constructor-argument path
  through a scene change, and this is the smallest mechanism that covers it; opening `GameRoot.tscn`
  directly in the editor still falls back to two-human hotseat rather than failing to start).
  **`AgentFactory`/`AgentKind`/`SeatConfig`/`MatchConfig`** (`Shapes.Godot.Adapter`, 8 tests) mirror
  `Shapes.Console`'s `BuildAgent` switch exactly — same five kinds, same per-seat derived random
  stream (`seed * 7919` / `seed * 104729`) — so the lobby offers nothing the console hasn't already
  proven out. Difficulty is an iteration count (`SearchBudget.OfIterations`, presets 200/1000/5000),
  never a time budget, for the same reason the console's `--iterations` is: a wall-clock budget
  makes the same seed play a different game on a different machine.
  **AI turns run synchronously on the main thread — a deliberate first pass, not an oversight.**
  `GameRoot.RunAiTurns` calls the active seat's `agent.Choose` → `Submit` in a loop, one call per
  *action* (matching `Shapes.Console`'s own loop granularity, so a turn needing several actions —
  discard down to the hand limit, then play — plays out identically to a human at that seat), until
  control reaches a human seat or the game ends; called after `StartNewGame` and after every human
  `Submit`, so a human-v-AI game hands off automatically in both directions and an AI-v-AI game runs
  to completion with no input at all. **What C5 still owns:** running that search off the main
  thread with cooperative cancellation, and the backgrounded-app/what-the-player-sees-during-search
  policy that needs before either matters — a stall during a 200–5000-iteration search on desktop is
  short enough to ship as-is, but is exactly the thing a slower device or a higher difficulty preset
  would turn into a real freeze.
  **Not editor-verified** — same standing limitation as every Godot-side step this phase; verified
  by `dotnet build` (clean) and the full test suite (987/987), including an all-AI-seat game run to
  completion over three seeds (`MatchConfigTests`) as the closest thing to an editor playtest
  reachable outside the editor.
- [ ] **C2. Deckbuilder** (`deckMode: "custom"`) — also owns migrating the determinizer off its
  symmetric-deck assumption, since custom decks make the opponent's decklist itself hidden (a
  belief-distribution problem, not just a partition problem). Most of `Determinizer` and its test
  suite are unaffected (phrased against observations, not deck provenance); only `UnseenCardsOf`
  changes, to sample from a belief model instead of reading `BuildSymmetricDeck` — the file's own
  comments already anticipate exactly this edit, and `Determinize` throws on a non-symmetric
  ruleset today so the unmigrated path fails loudly rather than silently sampling nonsense. First
  belief model: constrain to cards demonstrably played, fill the rest uniformly within
  deck-size/copy limits — crude but sound, same justification as Phase 2's uniform sampling.
- [ ] **C3. Persistence** (`user://`): decks, settings, progress — the durable-data half, C6 having
  taken the interrupted-game half.
- [x] **C4. Card browser** — every card, always shown in full detail, filterable, in a grid. A
  separate scene (`CardBrowser.tscn`) rather than a lobby tab, per explicit direction, reached from
  a "Card Browser" button on `Lobby.tscn` and returning via its own Back button — the same one-line
  `ChangeSceneToFile` convention `Lobby` already established for reaching `GameRoot`.
  **Reuses the live-game view scripts rather than building a parallel renderer.**
  `HoverDetailPanel`/`SlotView` already render from static `CardText`/`CardDefinition` data (A4's
  `EffectText` synthesis), so a card looks identical here and on the real board. Nothing here
  submits a `GameAction` or touches `GameSession` — every cell is built once from `CardDatabase.All`
  and never mutated (`SlotView` renders with `isDraggable: false`). The in-play cell uses a
  synthetic `SlotIndex(PlayerId.One, 0)` and a fresh full-health `CreatureInstance(card.Id,
  card.Health, card.Types)` in place of a live `Board` — `SlotView.Render` only ever needed a
  `CreatureInstance` and a `CardDatabase`, never `GameState`.
  **Revised same day, before this ever reached a user playtest** — the first pass (in-hand face +
  hover-triggered tooltip + single-column rows, described in an earlier draft of this entry) was
  replaced outright per follow-up direction: every card now shows its full tooltip
  (`HoverDetailPanel.Show(CardText)`) permanently rather than needing a hover, a `GridContainer`
  replaces the row list so cards sit side by side, `CardFace`/the in-hand format and the per-row
  name/kind label are both gone (redundant once the tooltip and in-play view already carry the
  name), and a filter bar (`Kind`: All/Creature/Spell, `Cost`: All/1–5, `Type`: All/Spike/Anvil/
  Wheel, `Creature view`: Tooltip/In-play) rebuilds the grid from `CardDatabase.All` on every
  change rather than filtering a static list. Cost/type filters read `CardText.SinglePipType`/the
  card's single-type cost amount, the same derivation the cost badge itself uses, so a filter can
  never disagree with what the badge shows. A spell has no board presence, so `Creature view` only
  changes a creature's cell — a spell always renders as its tooltip regardless of that filter's
  setting, per its own semantics rather than a special case bolted on. `HoverDetailPanel`'s root
  anchors are baked for its usual full-rect-parent, fixed-corner use (PLAN.md B1a2); confirmed by
  an actual run, not assumed, that a `GridContainer` parent overrides a child's own anchor/position
  the same way `HBoxContainer` already does elsewhere, so no changes to that scene were needed to
  reuse it as a grid cell.
  **Two real bugs, both caught by actual headless Godot runs, neither visible to `dotnet build` or
  the test suite:** (1, first pass) building a row's `CardFace`/`SlotView` children and calling
  `.Render(...)` on them before adding the row itself to the live scene tree — a node's `_Ready`
  (which resolves its own `GetNode<...>` child references) only fires once a node enters the tree
  its scene root is already part of, not merely once it becomes a child of an off-tree `Control`, so
  `Render` threw a `NullReferenceException` on a still-null label reference. Fixed by attaching
  every node to the live tree before calling any `Render`, a lesson this revision's `BuildCell`
  still follows. (2) None found in the revision itself — verified clean on the first headless pass,
  unlike the first version.
  **Editor-verified, unlike most Godot-side steps this phase** — a locally available Godot 4.5.1
  binary made real headless runs and on-screen screenshots possible (a throwaway rig scene, deleted
  after use): `--headless` loads of `Lobby.tscn`, `CardBrowser.tscn`, and `GameRoot.tscn` all clean,
  plus screenshots confirming the grid layout, the default (Tooltip, All/All/All) view, and the
  Kind=Creature + Creature view=In-play filter combination all render correctly — the first steps in
  Phase 5 to get an actual editor/runtime check rather than resting on `dotnet build` and the test
  suite alone.
  **Revised again same day: 5 columns, a reordered filter bar, and a real "Merged" creature
  type.** Grid widened 4→5 columns; filter order changed to Type, Cost, Kind, Creature type,
  (Kind=Creature only) Creature view — `Kind` and `Creature view` now hide themselves
  (`OptionButton.Visible`, not disabled) whenever `Creature type = Merged` is selected, since a
  merged creature is always a creature shown in-play, so those two controls would otherwise offer
  choices that do nothing. **A new sort rule (cost, then name, then resource type) applies
  everywhere** — the plain grid, and the creature list the merge pickers themselves are built
  from, so "First"/"Second" list creatures in the same order the plain grid would show them.
  **A search bar** (top-right, `LineEdit.TextChanged`) narrows by a case-insensitive name
  substring, composing with every other filter rather than replacing them.
  **`Creature type = Merged` is a REAL merge, not a cosmetic double-render** — reveals a
  First/Second row (`MergeBar`) and builds every *ordered* pairing of two creatures (originally
  27×26, corrected to 27×27 in a later revision below) by constructing each source's own
  `CreatureInstance` and calling `receiving.AbsorbMerge(absorbed, cards.MoveCountOf)` — the exact
  method `ActionExecutor.ApplyMerge` calls on a real board, so the combined health/types/move list
  shown here is what a real merge actually produces, not a hand-assembled approximation that could
  drift from it. Order matters and is preserved deliberately: `AbsorbMerge`'s move-list
  concatenation and merged display name both depend on which source is "first," so "Circle A +
  Circle B" and "Circle B + Circle A" are genuinely different cells, not duplicates. First/Second
  each default to "All" (every pairing) and narrow to one specific creature when set; to see a
  specific reverse pairing, pick the two creatures in the other order (a `Flip` checkbox was tried
  and removed the same day — see the next revision).
  **Verified the same way as the first revision** — headless loads clean, plus screenshots of the
  default grid (5 columns, correct sort order), a filtered single merged pairing, and the full
  unfiltered 702-pairing grid (scrolls, does not hang or error) confirmed all correct before this
  was considered done.
  **Revised a third time same day, after real use surfaced three problems this round's own
  verification hadn't caught:**
  1. **Slow.** Every filter change rebuilt every matching cell as real Godot nodes (`SlotView`/
     `HoverDetailPanel` — a `Panel`, a `StyleBoxFlat`, several `Label`s, move buttons, art panes
     each), and the unfiltered Merged case is 702 of them at once. Nothing was being
     "recomputed" in the sense of repeated engine work — `AbsorbMerge` itself is a handful of
     field adds — the cost was node/scene construction, so caching pure data would not have
     fixed it. **Fixed with pagination**, not caching: `RebuildGrid` now computes the full
     filtered/sorted result list as plain data first (cheap, no nodes — a new `Entry` record,
     either one card or one merge pairing), then instantiates real cells for only the current
     25-card page (5×5, matching the grid's own column count). A `PageBar` (`< Prev` / `Page N /
     M (total)` / `Next >`) appears only when there's more than one page; any filter change
     resets to page 1.
  2. **`Creature view` (and `Kind`) stayed visible under `Creature type = Merged`.**
     `OnCreatureTypeChanged` hid the `OptionButton`s themselves but never their sibling `Label`
     nodes (`KindLabel`/`ViewModeLabel` in `CardBrowser.tscn`), so the filter bar showed
     "Kind"/"Creature view" text sitting over an empty gap where the dropdown used to be —
     looked like the filter never hid at all. Fixed by hiding each label alongside its control.
  3. **Flip didn't visibly do anything.** It swapped which of the First/Second *filters*
     supplied the absorbing vs. absorbed creature before filtering — correct in isolation, but
     with both pickers left on "All" (the default state right after switching to Merged, the
     state most likely to be tried first) every ordered pair already appears both ways in the
     unfiltered list, so swapping the filters is a genuine no-op there. **Removed rather than
     reworked**, per explicit direction, once a per-cell-swap alternative (visibly flips even at
     "All", but only by relabeling which of two *already-separately-existing* entries a cell
     shows, so the full unfiltered set doesn't actually change either) was weighed and rejected
     as solving nothing real: First/Second already reach every reverse pairing directly by
     picking the two creatures in the other order, which is what shipped instead.
  **Verified the same way as every revision this step** — headless loads clean; screenshots
  confirm the label-hiding fix, a 25-of-702 first page with working `Prev`/`Next` (page 2 of 2
  correctly shows the remaining 11 cards for a 36-total Original filter), and that picking
  First/Second in reversed order renders the reversed merge.
  **Revised a fourth time same day: `Creature view`'s visibility was only wired to `Creature
  type`, never to `Kind`**, so it stayed visible (and usable) even with `Kind = All` or `Spell`,
  where it has nothing to control — a spell has no in-play view regardless of this filter.
  `OnKindChanged` (new) and `OnCreatureTypeChanged` now both defer to one shared
  `UpdateViewModeVisibility`, which shows `Creature view` (and its label) only when `Creature
  type = Original` AND `Kind = Creature`, and — not just hides it — resets it to `Tooltip`
  whenever it's hidden, since `BuildOriginalCell` reads `_viewModeFilter.Selected` regardless of
  `Visible` and a stale `In-play` selection left over from a prior `Kind = Creature` session must
  not silently keep affecting spell cells the next time the control is hidden. Verified with
  screenshots: `Kind = All` hides it; `Kind = Creature` shows it (defaulted to `Tooltip`); and a
  `Creature` → `In-play` → `Spell` sequence hides it and correctly renders all spells as tooltips
  rather than getting stuck.
  **Revised a fifth time same day: merged pairings excluded a card with itself.**
  `MergedEntries()` skipped `first.Id == second.Id` on the reasoning "a creature cannot merge with
  itself" — true of one *instance*, but not of two separate copies of the same card played to
  adjacent slots, which is a real, legal board scenario:
  `ActionGenerator.AddMergeActions` rules out same-**slot** merges (`sourceSlot == targetSlot`),
  never same-**card-id** ones, and `AbsorbMerge` itself never checks `CardId` equality either, so
  rendering "Circle + Circle" needed no special case once the skip was removed — 27×27 = 729
  ordered pairings (including 27 self-pairs), not 27×26 = 702. Verified with a screenshot:
  "Basic Circle + Basic Circle" renders at 4/4 health (2+2), both move sets and both art panes
  duplicated, exactly matching what merging two real copies on a board would produce.
- [ ] **C4b. Card stats** — win-rate/pick-rate context per card from `balance/LOG.md`, deferred out
  of C4 rather than bundled: the collection *view* needed no balance data to be useful, and
  pulling live numbers into the client raises its own question (bundled at build time, or read from
  disk) that the browser itself didn't need answered to ship.
- [ ] **C5. AI opponent responsiveness: off the main thread, capped on mobile** — C1 shipped a
  working AI seat that searches synchronously; this is specifically what's left. `Choose(AgentContext,
  CancellationToken)` already takes the token, so the seam exists — **what's missing is the
  policy**: what the player sees during a ~2s search, and what happens when the app is backgrounded
  mid-search. Cancel-and-restart on resume is the safe default; decide it here rather than
  discovering it on a device.
- [ ] **C6. Interrupted-game persistence** — mobile is the platform that kills a backgrounded app
  mid-turn, so "resume game" is a different problem from "save deck" and is scheduled separately
  from it (C3). Two viable mechanisms: serialize `GameState` (needs RNG stream position,
  `PendingDiscards`, `MergedFrom` chains, `TurnEvents` — all of it, correctly), or replay
  seed-plus-action-log, which Phase 1's determinism guarantee already makes sound and is the
  cheaper bet. Pick one deliberately; a half-serialized state that desyncs is worse than no resume.
- [ ] **C7. Tutorial / rules surfacing** — **the item the old plan omitted entirely, and the
  difference between playable and learnable.** Nothing about the ruleset is self-evident from a
  board: a rock-paper-scissors type cycle, merging that can *increase* vulnerability, scoring that
  requires an unopposed slot, and fatigue. The console gave players that context in text and the
  Godot client gives them none. Minimum bar: the type cycle legible on the board itself, and a
  reachable rules reference.

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
