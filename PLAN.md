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
| 5 — Godot client                         | 14 / 22    |

951 tests passing. **Phases 1, 2, 3, and 4 are complete.**

Phase 3 and 4 were split from one combined phase because they need opposite invariants: agent
comparison needs cards/rules **frozen**; balancing needs them **variable**. So Phase 3 freezes
content and varies agents; Phase 4 freezes agents and varies content. Phase 2 correspondingly
ends at a *correct* search, not a fast or tuned one.

**In progress: Phase 5** — the Godot client. Milestones A and B are essentially done (only card art
authoring remains); Milestone C is next, starting with a professional game-screen UI pass. A
hotseat game is playable end to end in the editor with drag-and-drop, animation, an AI seat, and
save/resume. Content is settled at `v1.7-final` and the balance record lives in `balance/LOG.md`;
the one item Phase 4 left open is a small seat-2 edge visible only at large samples (see that
phase's closing note).

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

**Included win rate** (`--deck random`) is the deckbuilding counterpart: of the decks that *ran*
a card, how often that seat won. Unlike win-rate-when-played/drawn it is not conditioned on the
card showing up — a card that sits in the deck undrawn all game still counts, and those are
exactly the games the other two rates cannot see. Deck inclusion is decided before the game
starts and is independent of anything that happens in it, which makes this the one card win-rate
free of mid-game selection. **One deck = one trial regardless of copies**: copy-weighting would
let a single game's outcome count three times as three perfectly-correlated "trials" and report a
Wilson interval up to √3 narrower than the evidence supports, for exactly the cards people run
three of. The copy-count signal instead lives in a **1×/2×/3× breakdown** beside it, which
answers "does running more copies win more" empirically rather than by assumption. Under
`--deck default` every deck runs every card, so this collapses to the pooled seat win rate for
every card — the sim says so and the report hides the columns rather than printing 36 identical
numbers.

**Deck stats** answer the level above the card: *what kind of deck wins*. Win rate bucketed by the
deck's own composition — mean card cost in 0.2-wide bands, then three views of each resource type
— one deck played by one seat being one trial, as above. The three views answer different
questions and are worth reading together: **cards by cost** is how many cards *demand* that
resource (the quantity `MinPerType` constrains — a shortage here means the deck stalls whenever
that pool runs dry), **creatures** is how many cards *are* that type once in play (a shortage
loses the type-chart matchup, and spells are absent by definition), and **cost pips** is the total
paid. A deck can demand plenty of spike while fielding few spike creatures, spending it on spells
instead. Buckets are anchored to fixed multiples
(`2.00–2.20`, never "min to min+0.2") so two runs stay comparable, half-open on the right so a
boundary deck is not double-counted, and empty interior buckets are kept because a gap is itself
information. **Every group carries a separation verdict**, because bucketed win rates drawn as
bars always *look* like a trend — the label says whether any two buckets' intervals actually
separate, and at 800 decks most groups still honestly read "not distinguishable".

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

**Deck model** — every game is played with a `Deck` (`Shapes.Core/Cards/Deck.cs`), dealt through
the single `GameSetup.Deal` entry point that the console, the sim, the Godot adapter, and the test
fixtures all share. Three sources: the **default deck** (one copy of every card — the console's
only deck, deliberately exempt from the 40-card limit so a console game exercises the whole set),
an explicit **custom** decklist, and a **constrained random** deck (40 cards, ≤3 copies each,
generated to sit within ±0.2 of the default deck's mean cost and to run ≥10 cards **demanding
each resource type by play cost**). `Shapes.Sim` selects between them with
`--deck default|custom|random`.

**The type minimum is about resource demand, not board typing.** Income arrives as three separate
pools, so a deck whose cards nearly all cost spike drains spike dry while anvil and wheel pile up
unspent — bottlenecked on one resource and idle on two. A 3-spike *spell* is that demand exactly
as a 3-spike creature is; the pool cannot tell them apart. Counting by cost is therefore what the
constraint has to do, and **spells count**. (They originally did not: the seeding phase drew from
creatures only, and since no shipping creature is multi-type, the three minimums consumed ~30 of
40 slots before a spell was eligible — holding spells to **6.4%** of a deck against the 25% an
unbiased draw gives, so every spell's per-card metrics rested on ~⅓ the sample every creature's
did. Fixed; spells now sit at 24.0%, and creature/spell mean deck-inclusion is 1.02× where it was
2.90×. Pinned by regression test in `ContentCardSetTests`.)

Phases 1–3 measured on fixed symmetric decks deliberately, so varying decks didn't confound card
win-rate with deck-composition effects — and **that is still the default**, so every number in
`balance/LOG.md` stays comparable to a fresh `--deck default` run.

> **⚠️ Determinizer follow-up (owed, tracked at D1).** IS-MCTS on non-symmetric decks currently
> works by being handed its opponent's **real decklist** (`Determinizer(cards, opponentDeck)`).
> Hand contents and deck order are still hidden and sampled, but deck *composition* is not — the
> agent knows which 40 cards the opponent drew from, which a human would not. This is a deliberate
> temporary cheat, taken so IS-MCTS stays usable on exactly the deck-diversity runs that need it
> rather than throwing. **Consequence: treat any `ismcts` win rate measured on `--deck random` or
> `--deck custom` as an optimistic bound on that agent's true strength, and never compare it
> against a symmetric-deck run.** The real fix is the belief distribution described in C2/D1
> (sample a plausible decklist per iteration, constrained to cards demonstrably played and filled
> uniformly otherwise); it changes only `Determinizer.UnseenCardsOf`, leaving the public surface
> and everything downstream untouched.

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

### Phase 5 — Godot client (desktop + mobile) — 14/22, in progress

Target Windows/macOS/Linux desktop and Android from one codebase. Organised as four milestones:
A got the full rules onto one screen with no art; B made it feel like a game (interaction model,
art, animation); C fans out into the other scenes; D ships it. **Everything new lives in
`Shapes.Godot` or `Shapes.Godot.Adapter`** — `Shapes.Core` stays unmodified from Phase 4.

#### Milestone A — one playable screen (hotseat, no art) ✅ complete (6/6)

**Goal:** full rules running under a finger — every card, all targeting, no art/animation — with a
seeded Godot game matching the same seed's console result.

- [x] **A1.** `Shapes.Godot` added to the solution, referencing `Shapes.Core` unchanged; builds
  clean from the repo root alongside the other 7 projects.
- [x] **A2. Adapter layer** — `GameSession` (owns the game, mirrors console setup exactly) and
  `StateDiff` (diffs state before/after each action). Needed its own project,
  `Shapes.Godot.Adapter`, since the Godot SDK isn't `dotnet test`-reachable outside the editor —
  see the project-structure note above.
  Adapter/UI code the whole phase, not engine code.
- [x] **A3. Board scene**, responsive/touch-first: `GameRoot` → `BoardView` → `PlayerPanel` ×2 →
  `SlotView`/`CardFace`, plus a tap tooltip. Three real bugs (signal-handler wiring, an
  unbounded-recursion freeze, an event-ordering bug that broke card placement) found only by
  playtesting, not by the type checker or `dotnet build` — the standing lesson for every Godot step
  since.
- [x] **A4. Card text synthesized from the op vocabulary** (`EffectText`/`CardText`), never
  hand-authored, so a balance edit can't drift out of sync with what the client shows. Landed
  inside A3.
- [x] **A5. Target-selection UI** for `chosen_*` actions, one flat state (no chaining) thanks to
  the single-target rule.
- [x] **A6. Decided against undo and confirmation dialogs.** Cheap to build (state cloning already
  happens for the diff), but a draw is a reveal — undo can roll back state, not what the player
  already saw — so every action ships as a committed decision instead. Design note, no code.

#### Milestone B — make it feel like a game ✅ mostly complete (4/5)

- [x] **B1a. Drag-and-drop** replaces tap for play/merge; moves are always-visible buttons (a drag
  can't disambiguate 2+ legal moves onto one target); discard stays tap-based. Several real bugs
  (drag events absorbed by a child button, layout sizing, drop-target fallthrough) found by
  playtesting and fixed.
  a `Button` under it.
- [x] **B1a2. Fixed-position hover detail panel** (desktop only) restores the full card detail that
  B1a's compacted move buttons removed, after six playtest rounds of a hover-anchored tooltip
  proved unfixable in general (repositioning correctly against every screen edge/control shape is
  the wrong problem) — replaced with a panel that never moves. Also consolidated the status bar and
  fixed the opponent/self panel split to 20/80.
- [x] **B1b. Status/keyword badges** on board slots (taunt/reflect/ricochet/stun/attack-buff),
  visible at a glance with no tap needed, folded into the B1a2 hover view.
- [~] **B1c. Real card art — IN PROGRESS, pipeline done, 5 of 36 cards authored.** Art pipeline
  (`CardArt.For(cardId, ...)`, keyed on card id with a placeholder fallback) and layout groundwork
  landed; remaining work is purely authoring the other 31 cards — no further engineering.
- [x] **B1d. Animation from the A2 diff** — play/move/merge/damage/heal/destroy/score, via a
  transparent overlay (`BoardAnimator`) rather than reconciling node identity, which was rejected as
  too high-risk to touch. Two real bugs (scale/position ordering, a same-frame stale-child race)
  found post-playtest and fixed.

#### Milestone C — the other scenes — 7/9 complete

- [x] **C1. Lobby / match setup with a working AI seat** — per-seat choice of Human/Random/Greedy/
  IS-MCTS/IS-MCTS-heuristic, mirroring the console's own agent factory. AI turns currently run
  synchronously per action (fixed by C5).
- [x] **C4. Card browser** — every card shown in full detail, filterable/searchable, paginated grid;
  reuses the live board's own render code so a card looks identical here and in a real game. Went
  through five same-day revisions (pagination for performance, filter-visibility bugs, a merge
  self-pairing bug) each caught by real headless Godot runs.
- [x] **C5. AI opponent responsiveness** — moved the search to the thread pool with a paced delay
  between moves, so a human watching an AI game sees it progress instead of jumping straight to
  game-over. Minimal cancellation (torn-down scene drops an in-flight search); full
  backgrounded-app policy remains open, same as C6 below.
- [x] **C6. Interrupted-game persistence** — seed + action log (not a `GameState` serialization),
  replayed on resume via Phase 1's determinism guarantee. Saves after every action so a mobile OS
  kill mid-turn still resumes correctly; one save slot, cleared on game-over.
- [x] **C-UI. Professional game screen UI** — a real HUD pass over the board screen, which had been
  Godot's default theme with zero custom styling: a sidebar/grouping for per-player stats (surfacing
  deck count, discard count, and the previously invisible pending-income/pending-score previews
  alongside score and resources), icon chips instead of text glyphs for resources, a styled End Turn
  control, and a consistent visual language (palette, type, spacing) applied across board slots, hand
  cards, and the status bar.
- [x] **C2. Deckbuilder** (`deckMode: "custom"`) — engine half plus the Godot UI. Engine:
  `Deck`/`DeckBuilder`/`DeckLoader` in `Shapes.Core`, the shared `GameSetup.Deal` path, decklist
  files, constrained random generation, and `Shapes.Sim`'s `--deck` modes. UI: a Deckbuilding tab
  over ten `user://` deck slots (40 cards, ≤3 copies), edited as collection/decklist columns of a
  new short-wide card row view, with per-seat deck dropdowns in the lobby. Partial decks save;
  legality is enforced at match start through the same `DeckBuilder.Custom` path the sim uses.
  `GameSession` gained per-seat decks (`Start(hand, deckOne, deckTwo)`, `OpponentDeckOf`) — the
  agent factory had been handing both seats seat one's decklist, which only became wrong once the
  seats could differ — and `SavedMatch` now persists both decklists, without which a resumed
  custom-deck game replays its action log against the wrong deal. The determinizer still reads the
  opponent's supplied decklist; that migration is tracked under D1.
- [x] **C3. Persistence** (`user://`): decks, settings, progress — the durable-data half C6 didn't
  cover. Deck slots persist through `DeckStore` (write-through on every edit, one cached JSON
  document, corrupt-file-tolerant), mirroring `MatchSaveStore`'s pure-adapter/Godot-IO split.
- [ ] **C4b. Card stats** — win-rate/pick-rate context per card from `balance/LOG.md`, deferred out
  of C4 since the collection view didn't need it to ship.
- [ ] **C7. Tutorial / rules surfacing** — nothing about the ruleset (type cycle, merge
  vulnerability, unopposed-slot scoring, fatigue) is self-evident from the board; the console
  explained it in text and the Godot client currently explains none of it. *(Moved to Milestone D.)*

#### Milestone D — ship — 0/5

- [ ] **D1. Determinizer migration** (the deckbuilder UI half landed with C2). Migrate off the
  supplied-decklist cheat onto a real belief distribution: constrain to cards demonstrably played,
  fill the rest uniformly. A correctness debt, not a nice-to-have — until it lands, `ismcts` numbers
  on non-default decks are measured with the agent knowing its opponent's decklist, and C2's
  per-seat decks make that assumption reachable in normal play rather than only in the sim. See the
  Deck model warning above.
- [ ] **D2. Tutorial / rules surfacing (C7).** Persistence (C3) landed with C2's deck slots;
  settings and progress are still unpersisted if either grows a durable surface.
- [ ] **D3. Professional UI pass** — a full visual/UX polish beyond C-UI's board-screen HUD:
  consistent styling across lobby/card browser/deckbuilder/game-over, animation and feedback-state
  polish (hover/selected/legal-target states), and card art integration once B1c completes.
- [ ] **D4. Polish:** sound, transitions, menus. Audio wants an asset-source decision *before* this
  step rather than during it.
- [ ] **D5. Export pipeline** (desktop + signed Android `.aab`), reusing/re-verifying the step 1.13
  toolchain: export templates need the .NET 9 SDK alongside .NET 8, Editor Settings needs explicit
  Java/Android SDK paths, and rebuilds need `adb install -r` or a stale APK silently masks the
  change.

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
balanced) → Godot (does it read). **One caveat, now that C2 has landed:** every number in
`balance/LOG.md` is against symmetric decks, and cards measured on custom decks are a different
experiment — keep those runs in separate directories rather than comparing them to `v1.7-final`.

**On the carried-over seat-2 margin.** Phase 4 left a small real seat-2 edge (−0.28 [−0.40, −0.16]
at 4000 games). Deliberately *not* scheduled in this phase: the fix is a ruleset knob, and C2's
custom decks move the number again. C2 has now landed, so the re-measure it was waiting on is
unblocked — measure before touching the knob, since the symmetric-deck margin above is the one
custom decks invalidate.

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
