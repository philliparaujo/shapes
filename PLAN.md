# Shapes — Development Plan

A 2-player, turn-based, board-and-cards game. Five phases: playable engine → IS-MCTS AI →
agent measurement & optimization → AI-driven balance → Godot client.

## Status

| Phase                                    | Progress   |
|------------------------------------------|------------|
| 1 — Playable engine                      | 13 / 13    |
| 2 — IS-MCTS AI (naive, correct)          | 6 / 6      |
| 3 — Agent measurement & optimization     | 9 / 9      |
| 4 — AI-driven balance                    | 10 / 11    |
| 5 — Godot client                         | 0 / 12     |

892 tests passing. **Phases 1, 2, and 3 are complete.**

Phase 3 and 4 were split from one combined phase because they need opposite invariants: agent
comparison needs cards/rules **frozen**; balancing needs them **variable**. So Phase 3 freezes
content and varies agents; Phase 4 freezes agents and varies content. Phase 2 correspondingly
ends at a *correct* search, not a fast or tuned one.

**Next up: Phase 4 step 6** — sweep card changes in symmetric decks, now against a ruleset that
always terminates. Step 5b (done) added fatigue after the card sweep surfaced a non-terminating
game and traced it to the scoring rule rather than to any card: with score gated on killing
something, any board where defense meets offense stops scoring forever, and 7 of 26 creatures
stalemate their own mirror. Card-level fixes would have meant banning self-heal and permanent
health buffs outright, so the fix was structural. Steps 2b/2c gave the metrics
the denominators, intervals, and diagnostic splits a sweep needs to rank on; 2d made that output
readable and diffable; 2e checked the detectors against a known-wrong answer and found take rate
alone is not reliable for economy/tempo-neutral cards — cost pressure is. Step 3 (done) carried
that forward into the rules-level sweep — economy, hand size/draw, and scoring-threshold variants,
logged in `balance/LOG.md`. Step 4 (done) closed the console-side tooling gap it surfaced — bare
`Move #N`, no move/spell effect text, merged creatures showing only their primary card's name.
Step 5 (done) closed the metrics-side gap: a per-turn take rate (`CardStat.PlayTakeRatePerTurn`/
`MoveStat.UseTakeRatePerTurn`) alongside the existing per-decision rates, and cost/health/resource
type/effect text joined onto every card and move row in the HTML explorer, the CSV exports, and
the console's card/move listings, plus conditional coloring on take-rate outliers in the HTML
report. Agents are frozen at Phase 3's final tuned configuration (step 7's matrix); cards/rules
vary from here instead.

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
> sampled games. Both are real, non-degenerate design issues for steps 3-4 to balance against, not
> settled — content (cards/rules) hasn't changed yet.

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

### Phase 3 — Agent measurement & optimization

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

### Phase 4 — AI-driven balance

**Agents frozen** this phase — the mirror image of Phase 3. Cards/rules untouched since Phase 1
step 10, so every number here reflects genuinely stable content.

- [x] **1. Metrics** — `MetricsReport.From` aggregates a whole `BatchResult` (never per-pairing,
  same "don't pool seats" rule as `PairingSummary`): win rate by seat, avg game length, per-card
  play-count/win-rate-when-played **and** win-rate-when-drawn, per-move use-count/win-rate,
  merge frequency (both raw and normalized as merges-per-creature-played, so a bare count can't
  hide whether merging is common or rare relative to opportunity), average unspent resources at
  game end, and ending-type counts. Card draws are tracked via a new `TurnEventKind.CardDrawn`
  logged at the one choke point every draw already goes through (`GameState.DrawWithBurn`); a
  card counts as "drawn" from the opening hand onward, and multiple copies drawn in one game
  count that game once toward the win-rate denominator (matches how play win-rate already
  worked). Rates here were bare doubles; step 2b replaced them with interval-carrying types and
  added the opportunity denominators this step's counts turned out to need. Moves are keyed by
  `(CardId, MoveName)`, not by `UseMoveAction.MoveIndex` (only
  meaningful relative to one creature's merge-concatenated move list) or bare `MoveDefinition.Name`
  alone (two cards can share a move name; `GameRunner.ResolveMove` walks `MergedFrom` to find which
  source card actually declared the move at that index, since a merged creature's move can belong
  to either half of the merge). `GameRunner` caps a
  game at 500 turns so a stalled game reports as `EndingType.NonTerminating` (a countable batch
  outcome) instead of hanging the run — step 4.5's "non-terminating games" watch item, now
  enforced rather than assumed. Score curves were tried and dropped here: a flattened per-turn
  score series added size and noise to the JSON output without pulling its weight next to the
  other metrics. Step 2b brought back the form that does pull its weight — a per-turn *margin*
  (one int per turn, aggregated into means rather than stored per game), which is what the
  income-compounding question actually needs. `Shapes.Sim`'s console output, `--json`, and
  `--metrics-json` all surface the report.
- [x] **2. Answered the two flagged design questions directly as behaviour measurements** (24
  self-play games each, 200 iterations, both `ismcts` and `ismcts-heuristic` since the heuristic
  playout is stronger and closer to optimal play): **merge is declined, not auto-taken** —
  `ismcts` merged in 33.2% of opportunities (declined 66.8%), `ismcts-heuristic` in 38.8%
  (declined 61.2%), with a declined merge in 95.8% of games for both. The tradeoff is real, not
  priced as a free strictly-better action. **Unopposed-creature income compounding is real and
  strong** — longest-unopposed-streak vs. final score margin: Pearson r = 0.729 (`ismcts`), 0.480
  (`ismcts-heuristic`); a player who never sustained an unopposed creature 2+ turns running won 0
  games in-sample at either configuration. Confirms both flagged concerns as genuine,
  non-degenerate design issues for step 3/4 to address, not null results — though the sample is
  smaller than Phase 3's 30-game convention and shows correlation, not causal isolation, so a
  larger confirmatory run is worth doing before it drives a specific ruleset change.
- [x] **2b. Made the metrics able to carry step 3** — a review of the step 1 report against what
  a sweep actually needs found four gaps, all now closed. **(a) Confidence intervals on every
  rate** (`Interval`, Wilson score — not the normal approximation, which reports a 0/4 card as
  "0% ± 0", the most confident-looking and least justified number available; `MeanEstimate`, the
  continuous counterpart, for margins and lengths). A rate without its sample size cannot be
  ranked, which is step 3's entire job. **(b) Opportunity denominators** — `CardStat.PlayTakeRate`
  and `MoveStat.UseTakeRate` divide plays/uses by the decision points where that play was *legal*,
  counted from the same `ActionGenerator.Generate` list the agent chose from, deduped per
  (decision, card) so targeting-flexible cards don't get inflated denominators. This is the
  card-level analogue of `MergesPerCreaturePlayed` and is **the primary balance signal**, because
  raw `PlayCount` conflates draw luck, affordability, and preference — only the third is about
  the card. It also separates step 5's two watch items directly: near-zero take rate = dead card,
  near-one = auto-include. `MergeTakeRate` makes step 2's bespoke merge measurement a standing
  metric. **(c) Score margin** (`FinalScoreMargin`, `AbsoluteScoreMargin`, `ScoreMarginByTurn`)
  plus per-turn resource sampling split winner/loser and by seat — game-end resource levels
  averaged both seats together, mixing the winner (just spent everything to close) with the loser
  (starved for turns) and reporting a midpoint describing neither. Margin matters because it is a
  far lower-variance estimator than binary win rate: pinned by test, 100 games at a 56% seat win
  rate give a win-rate interval of [46%, 65%] (cannot call it) and a margin interval of
  [0.31, 1.29] (excludes zero) — same games, one metric resolves and the other doesn't.
  **(d) `RunProvenance`** (ruleset name, card-set content hash, agent config, seed, timestamp) —
  step 4's compare loop is undoable across a `balance/` directory of otherwise anonymous reports.
  Bug found on the way: `MoveOffers*` keyed by the `(CardId, MoveName)` tuple threw at `--json`
  write time (`System.Text.Json` refuses tuple object keys) — now `MoveKey`'s delimited string,
  with a named regression test, since "just use the tuple, it's the same identity" is the natural
  edit that reintroduces it.
- [x] **2c. Closed the three remaining detect-but-not-diagnose gaps.** Step 2b made outliers
  *visible*; these make three of them *actionable*. **(a) Unopposed-slot occupancy** —
  `UnopposedSlotRate`, `UnopposedCreaturesPerStep`, `LongestUnopposedStreak`,
  `GamesWithNoSustainedUnopposed`. Separates the two opposite fixes for a runaway score that the
  score itself conflates: a *low* rate means unopposed slots are hard to get and each is worth a
  lot (tune `PointsPerUnopposedCreature`), a *high* rate means they come easily and the points
  follow (tune board size, removal, durability). Also makes step 2's streak finding a standing
  metric instead of bespoke instrumentation. **(b) Creature survival** —
  `CardStat.SurvivalSteps` (scoring steps held before dying) plus `ScoredWhileAliveRate`. Take
  rate reports "played constantly and dies instantly" identically to "played constantly and
  sticks"; these are opposite problems. `ScoredWhileAliveRate` then splits a blocker (holds a
  contested lane) from a scorer (converts presence into points). Censored samples (alive at game
  end) are dropped rather than counted short — counting them would drag the mean down for
  precisely the creatures that survive best — so read it as a floor, not an estimate.
  **(c) Affordability pressure** — `CostPressure`, batch-level and per-card. Makes the resource
  numbers diagnosable: high unspent resources with *low* pressure means income exceeds what there
  is to buy (an income-level problem), high unspent with *high* pressure means players hold the
  wrong resource *types* (a type-chart/cost-distribution problem). Same two numbers today, and
  they would have been indistinguishable before this.
  **Deliberately not built: effect-magnitude tracking** (damage/healing per pip). Collecting it
  is easy — `CombatResolver.DealDamage` is a single funnel — but the resulting number is not a
  balance signal here: damage is instrumental to holding a slot rather than being the win
  condition, overkill inflates it, type effectiveness doubles it for free, and damage/cards/
  resources share no exchange rate without asserting a cost curve. Time-on-board is the honest
  version of "did this creature do its job" in a game won by occupying slots.
  **Bug caught by cross-check, not by inspection:** the first version observed the seat that
  *ended* its turn rather than the one *receiving* it, reading the board before the opponent
  could contest those slots — over-counting unopposed slot-turns ~40% while looking entirely
  plausible in aggregate. Now pinned by the exact identity `score == slot-turns x
  PointsPerUnopposedCreature`, which reconciles with zero slack on every game.
- [x] **2d. Metrics explorer** (`--report PATH.html`) — a self-contained, dependency-free HTML
  page written alongside `--metrics-json`, because the report has outgrown reading. One run is
  ~3,700 lines of JSON and ~1,600 numbers for cards alone, and the console output is a fixed
  slice chosen in advance; neither answers "which cards are outliers on take rate *and* have
  intervals tight enough to act on." Sortable card and move tables (click a header to sort, click
  again to reverse), a minimum-n filter that greys out rows too noisy to rank, and a batch-level
  scoring/economy summary panel.
  **The diff view is the point, not the sorting** — step 4 iterates edit → rerun → compare, and
  comparison is exactly what static text cannot do. The page has no server to load a second run
  through, so the diff view is client-side: a file picker loads a second `--metrics-json` file
  entirely in-browser (`FileReader`, no upload) as the baseline, and every card row then shows a Δ
  plus the baseline rate overlaid on the interval bar, colored when the two intervals don't
  overlap ("moved beyond noise" vs. "moved"). Default view (no baseline loaded) is **cards whose
  interval excludes the field median**, so the page opens on the handful worth arguing about
  rather than on all 36 — `Interval.Excludes` already answers that question. Baseline files are
  PascalCase (`ResultWriter`'s convention) while the inlined run is camelCase (for the page's own
  JS); a small `camelizeKeys` normalizer on load means the diff view accepts either without the
  script maintaining two spellings of every property.
  Lives in `Shapes.Sim/HtmlReportWriter.cs` next to `ResultWriter` (a reporting concern, no engine
  coupling — `Shapes.Core` stays pure). Data inlined as a JSON `<script>` block, no CDN, no build
  step, no install; `JavaScriptEncoder.Default`'s escaping of `<`/`>`/`&` is what keeps a card id
  or move name from ever producing a literal `</script>` and truncating the payload, pinned by a
  regression test asserting the page has exactly two script tags. `--cards-csv PATH` /
  `--moves-csv PATH` ship alongside it as the escape hatch for questions the page did not
  anticipate — flat per-card/per-move rows including interval bounds, built directly on
  `ResultWriter`'s existing CSV conventions. `--from-metrics-json PATH` skips playing games and
  reads a previously written `--metrics-json` file back in, so `--report`/`--cards-csv`/
  `--moves-csv` can be (re)derived from a saved run without replaying the batch that produced it.
- [x] **2e. Calibration spells** — six deliberately mispriced spells (one over- and one
  underpowered per resource), added as `Spike/Anvil/Wheel OP` (cost 1, gain 2 of that resource and
  draw a card) and `Spike/Anvil/Wheel UP` (cost 3, deal 1 damage), text-differentiable by name
  alone. Live in `Shapes.Content/cards-calibration/`, loaded only via `Shapes.Sim --calibration`,
  never merged into `Shapes.Content/cards/` — so `BuildSymmetricDeck`'s 36-card baseline and
  `CardSetHash` on real runs are untouched. **Mixed result, read through 2d's explorer at 40
  games/pairing, `ismcts` + `ismcts-heuristic`:** cost pressure cleanly separated both groups from
  the field and from each other (OP: 11-16% vs. field median 42%; UP: 55-70%, the highest in the
  set) — that detector works as designed. **Take rate did not separate them** (OP/UP both landed
  mid-pack, 14-19%, against a field median of ~21%) — a real instrument gap, not a calibration
  failure to paper over. Two causes: (1) the UP spells' `damage` effect needs a `chosen_enemy`
  target, so their take-rate denominator is legality-gated like `siphon`/`execute`, inflating their
  rate relative to the always-legal OP spells; (2) IS-MCTS's `PlayoutDepth=200` playout horizon
  undervalues a pure economy play with no immediate board impact, the exact blind spot PLAN.md
  already flags ("a card whose payoff lands outside IS-MCTS's horizon reads as weak"). **Actionable
  for step 3:** take rate alone cannot be trusted to rank economy/tempo-neutral cards; cost pressure
  is the more reliable signal for that category, and any auto-include/dead-card call on a
  card in that shape should cross-check both before acting.
- [x] **3. Sweep rules changes** (economy, cards, scoring) and rank outliers. **Delta-based, not
  rank-in-isolation:** under `deckMode: "symmetric"` both seats hold every card, so most cards are
  played by both in most games — contributing one win and one loss and compressing win rate
  mechanically toward 0.5 (the step 1 test run's cards sat 0.41–0.60 almost entirely because of
  this, not because the set is balanced). Per-card win rate therefore cannot rank cards no matter
  how many games are run. Rank on take rate, and establish causation by changing one thing and
  diffing two reports. This is the start of the `balance/` experiment log: edit JSON, rerun,
  compare, record what changed and why in `balance/LOG.md`. Covers the economy sweep (income
  levels, `incomePerCreatureType`), hand-size/draw sweep, and the scoring-threshold sweep — see
  `balance/LOG.md` for the full record of each variant tried and why it was kept or rejected.
- [x] **4. Console upgrades** for reading agent-vs-agent games during a sweep. **`EffectText`**
  (`Shapes.Core/Effects/EffectText.cs`) synthesizes a phrase from the op vocabulary — there is no
  hand-authored description field on any card (deliberately: it would drift from the numbers a
  balance edit changes) — covering all ~28 ops including nested `conditional`/`for_each` and the
  scaled-damage/heal/resource family's scale basis. **`ActionText.Describe`**
  (`Shapes.Console/ActionText.cs`, extracted from a `Program.cs` local function so it is directly
  testable like `BoardView`) replaces the bare `Move #N` — `GameAction.Describe()` cannot resolve
  a real move name itself since it has no `CardDatabase`/`GameState` access; `ActionText` walks
  `CreatureInstance.MergedFrom` through `CardDatabase.MovesOf` to find the actual `MoveDefinition`
  at that concatenated index, then appends its `EffectText`. `PlayCardAction`/`DiscardAction` get
  the same real-name-plus-effects treatment. **`BoardView.DescribeSlot`** now joins every card in
  `MergedFrom` (`"T Body + Basic Circle"`) instead of showing only `CreatureInstance.CardId`, the
  primary card — the gap `CreatureInstance.ToString()`'s debug form had too (a bare
  `(merged x2)` count, no names). **`--step`** pauses for Enter after every action, independent of
  `--quiet` — chosen over a fixed-ms delay because a timer fights both a fast-to-skim turn and a
  slow-to-read multi-effect spell in the same game; a manual step lets the reader set the pace
  instead. 30 new tests (850 total): `EffectTextTests` (one per op shape, plus an unknown-op
  fallback so a future op degrades to its raw name instead of crashing the console mid-game),
  `ActionTextTests` (including a merged-creature move-index case pinning `MoveIndexOffset`
  correctness through the display layer), `BoardViewMergeTests`, `ConsoleOptionsStepTests`.
- [x] **5. Metrics upgrades.** A per-turn take rate alongside the existing per-decision
  `PlayTakeRate`/`UseTakeRate` — the old rate counts a card/move as "offered" at every decision
  point it stays legal within a turn, so a move that's reliably used once per turn but rarely used
  *first* read as low-take-rate identically to a move nobody wants. `GameRunner` now tracks, per
  seat, which turns each card/move was offered in and which turns it was played/used in
  (`CardOffersByTurn*`/`CardPlaysByTurn*`/`MoveOffersByTurn*`/`MoveUsesByTurn*` on `GameResult`,
  deduplicated once per turn via a per-seat `HashSet` reset on every `TurnNumber` change — the same
  dedup idea `AccountSeat` already used one level up, at the game). `MetricsReport` aggregates
  these into `CardStat.PlayTakeRatePerTurn`/`MoveStat.UseTakeRatePerTurn` (Wilson intervals,
  matching every other rate in the report), separating "not urgent" (offered every turn, played
  most turns, just rarely the first thing done) from "not wanted" (offered every turn, rarely
  played at all) — the two read identically on the decision-level denominator alone.
  **Card/move reference data** (cost, health, resource type, synthesized effect text via the
  existing `EffectText.Describe`) is joined onto the report at write time by a new
  `CardInfo`/`MoveInfo` lookup (`Shapes.Sim/CardInfo.cs`), built from `CardDatabase` and kept out
  of `MetricsReport`/`CardStat`/`MoveStat` themselves — those stay pure aggregation over
  `GameResult` with no `CardDatabase` dependency, matching every existing `MetricsReportTests`
  fixture. Consumed by the HTML explorer (new Type/Cost/Health/Effect columns, inlined as two
  additional `<script>` JSON blocks, `cards` param on `HtmlReportWriter.Write` optional so
  `--from-metrics-json` still works without a loaded card set), the `--cards-csv`/`--moves-csv`
  exports (same new columns), and the console's per-card/per-move listings (cost inline, move
  effect text under each move line). **Formatting**: take-rate cells whose interval sits fully
  above/below the field median are bolded and colored (green above, red below) in the HTML
  explorer, and resource type renders as a colored chip (△/▢/◯) instead of a bare enum name.
  **Composite power score**: an opinionated per-card rollup in the HTML explorer only (no new
  `MetricsReport` fields) — a z-score average of take rate, take rate/turn, and win rate
  played/drawn across the min-n-filtered field, plus (creatures only) a take-rate-weighted mean of
  the creature's own moves' take rate and win rate, so a card that gets played but whose moves go
  unused scores lower than its play stats alone suggest. Z-scored, not rank-averaged, so the
  result preserves how far a card sits from the pack rather than just its order; spells and
  creatures are only comparable within their own kind. Computed client-side
  (`computeCompositeScores` in the template) precisely because it's a judgment call layered on top
  of the real metrics, not a new fact about the games played — the page says so directly next to
  the column. Ported verbatim into `compare.html` (`CompareReportWriter.cs`) as a `Δ power score`
  column, computed independently per side against that side's own field (so a card's z-score can
  move between runs even if its raw take rate barely did, if the rest of the field shifted around
  it) and diffed like every other stat there. `compare.html` never loads a `CardDatabase` (the
  `--compare` path reads two `--metrics-json` files and plays no games), so the creature/spell
  split the move rollup needs is inferred from `moveStats` presence on each side instead of a
  `cardInfo` lookup — true by construction (a spell has effects and no moves, a creature the
  reverse) and needs nothing beyond data already in both files. No confidence interval on this
  score, unlike the rate columns, so "moved" is a fixed `|Δ| ≥ 0.5` threshold (matching
  `report.html`'s own hi/lo coloring cutoff for the same score) rather than a disjoint-intervals
  test. **Hand size by turn**: `GameRunner` samples `Hand.Count` at the same per-turn
  boundary `ScoreMarginByTurn`/`ResourcesByTurn*` already use (`HandSizeByTurnOne/Two` on
  `GameResult`), `MetricsReport` transposes it into per-turn `MeanEstimate`s the same way
  (`ComputeMarginByTurn` generalized into `ComputeSeriesByTurn`, shared by both), and the HTML
  explorer renders it as a two-line chart (seat 1 vs. seat 2, deliberately not a single "P1 − P2"
  series the way score margin is — both seats being starved or both being flush are opposite
  findings a difference would erase). Reads alongside cost pressure: a thin hand with low cost
  pressure is a draw problem, a thin hand with high cost pressure is a resource problem.
  **Resources by turn, per type**: the per-turn counterpart of `ResourcesSeatOne/Two` (which
  already existed but only as one mean-over-the-whole-game per resource type) — `MetricsReport`
  now also exposes `ResourcesByTurnOne/Two` (`ResourceSeriesProfile`: one `MeanEstimate` series
  per resource type), reusing the exact `ResourcesByTurn*` raw per-game samples `ResourceProfile`
  already pools, just transposed by turn index instead of collapsed. `ComputeSeriesByTurn` widened
  to a generic `<T>` form (a value-selector `Func<T, double>` alongside the series selector) so the
  same transpose serves margin/hand-size's `int` series and the new `ResourcePool` series without
  duplicating the turn-alignment logic three times over. Rendered as three small two-line charts
  (Spike/Anvil/Wheel, seat 1 vs. seat 2) in the Economy panel, right below the existing resource
  table — `twoSeatLineChart` factored out of the hand-size chart's renderer since both are "two
  per-seat `MeanEstimate` series, no natural P1-minus-P2 framing" the same way. Reads against
  cost pressure per type: a level climbing turn over turn with low cost pressure for that type
  means income for it outpaces what there is to spend; climbing with high cost pressure means the
  type itself (not the amount) is the bottleneck.
  **Board presence by turn**: slot count and combined CURRENT (not max) creature health, per seat,
  sampled at the same scoring-step boundary `UnopposedSlotTurns*` already reads the board at
  (`SeatTracker.ObserveScoringStep` extended to fold this into the loop it already runs over that
  seat's slots, rather than a second board pass). Exists because unopposed-slot occupancy alone
  can't distinguish "present" from "actually threatening" -- three 1-health creatures occupying
  every slot score identically to three full-health ones, and slot count holding steady while
  combined health falls is a board being worn down, invisible to every other metric in the report.
  `GameResult.SlotsOccupiedByTurn*`/`CombinedHealthByTurn*`, aggregated in `MetricsReport` via the
  same `ComputeSeriesByTurn` used everywhere else in this family, rendered as two more
  `twoSeatLineChart`s in a new "Board presence by turn" panel ahead of Economy.
- [x] **5b. Fatigue — a structural tiebreak, so termination stops depending on removal.** The
  step-6 sweep found a `NonTerminating` game (seed 6909709552674346015, 501 turns, score frozen at
  9-9, last card played on turn 43) and reading it exposed a design coupling rather than a card
  bug: **score requires an unopposed creature, unopposed requires a kill, so any board where
  defense ≥ offense stops scoring permanently.** Nothing else in the game can end it. A sweep of
  all 26 creatures in an isolated 1v1 self-mirror found five outright stalemates under optimal
  play — `columns` (Brace +2 max health/turn vs. Slam 2 damage/turn, an exact tie, both a1),
  `circle_priest` (Mend is `heal_to_full`, strictly unkillable), `basic_square` and `circle_cadet`
  (both hold a "damage 1 + heal self 1" move that cancels in the mirror), `zealot` (Smite deals 1
  and buffs itself +1) — plus two by mutual impotence (`guardian`'s mutual reflect makes attacking
  strictly losing; `circle_bender` has *no* way to damage anything in a lane where its ricochet has
  no friendly neighbor to redirect to). That is 7 of 26 before considering the 26x26 cross-pairings
  or merged stacks, so this is a property of the scoring rule, not a handful of mispriced cards —
  and patching it card-by-card would mean banning self-heal and permanent `buff_max_health` as
  mechanics, which is a real cost to the design space for a problem that is not actually about
  those cards. Deck exhaustion currently has **zero consequence**, which is the specific gap: in
  the stalled game both players simply stopped playing cards on turn 43 and nothing changed for the
  next 458 turns.
  **The rule: at the start of a player's turn, if their deck is empty, their opponent gains 1
  score.** Chosen over fatigue-as-damage (Hearthstone/MTG style) deliberately — damage resolves the
  *board*, but this game's failure mode is a board that cannot be resolved at all, and a new damage
  source would interact with all 26 creatures' health/heal/buff surface. Scoring bypasses the board
  entirely, which is exactly the point: it is a path to victory that no defensive card can answer.
  It also **guarantees no draws** — the two players exhaust their decks on different turns (or, if
  simultaneously, the score gap that has already accrued breaks the tie), so score strictly
  diverges once fatigue starts and `scoreToWin` is always reached. `EndingType.NonTerminating`
  should become unreachable in practice; `GameRunner`'s 500-turn cap stays as a harness guard, and
  a `NonTerminating` result after this lands is a bug report, not a balance finding.
  Implementation sits on existing seams — `PlayerState.DeckIsEmpty` already exists, and
  `GameState.AdvanceToActions()` already sequences score→income→draw, so fatigue belongs in the
  score step it already owns. `RuleSet` gets the knob (`fatigueScorePerTurn`, default 1; 0 disables
  it, so every pre-5b balance run stays reproducible).
  **Metrics** — fatigue is only meaningful if it is measured, and it changes what draw effects are
  worth: `t_dealer`, `circle_surfer`, and `shieldbearer` are currently three of the strongest cards
  in the set and all of them accelerate deck exhaustion, so this may reprice them without any card
  edit. Track, per seat: **deck-exhaustion rate** (share of games where that seat ever drew its
  deck empty), **turn of first exhaustion** (a `MeanEstimate`, so "does this happen at turn 40 in a
  long game or turn 20 routinely" is answerable), **total fatigue score conceded**, and **share of
  games decided by fatigue** — a game where the winner's final margin is no larger than the fatigue
  points they were handed was decided by the timer, not by play, and if that share is large the
  rule is too aggressive. Read the first two against `CardsDrawnWinners`/`CardsDrawnLosers`, which
  already exist.
  **Game-length distribution, not just its mean.** The stalled game also exposed that
  `GameLength.Mean` alone is actively misleading — one 501-turn outlier moved the reported mean from
  21.3 to 26.7 and the standard deviation from 9.0 to 30.2, and it dragged every end-of-game
  resource average with it (total unspent resources appeared to rise 132% when the per-turn levels
  had not moved at all). A single mean cannot show a bimodal or long-tailed length distribution,
  which is exactly the shape a termination problem produces. Add **percentiles** (p5/p25/p50/p75/
  p95, plus min/max) to the length report and render a **histogram** in the HTML explorer, so a
  fat right tail is visible directly rather than inferred from a suspiciously large standard
  deviation. This is the metric that would have caught the stall on the run that introduced it.
  **Sequencing:** land this as its own `v1.5-fatigue` run against the *current* card set and
  compare to `v1.4-keywordfix`, not on top of the `v1.4-change1` card edits — those are still
  unevaluated and mixing them would confound a rules change with 15 card changes.
- [ ] **6. Sweep card changes** in symmetric decks — the per-card pass step 3's rules sweep was
  deliberately sequenced ahead of, since a rules change shifts every card's take rate and doing
  card-level tuning first would mean redoing it. Watch for never-played/auto-include cards,
  degenerate loops, first-player advantage beyond ~55%, non-terminating games. Archetype sweeps
  (mono vs. mixed, aggro vs. control) wait for `deckMode: "custom"` (Phase 5) — only meaningful
  after per-card balance has settled on the symmetric deck.

**Exit criteria:** no extreme take-rate outliers (no dead cards, no auto-includes) at a sample
size where the intervals actually separate them; first-player advantage near even **by score
margin**, not just by a win rate too wide to call; game length in target band; merge tradeoff and
income-compounding both confirmed as real, non-degenerate decisions.

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
