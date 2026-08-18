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
| 5 — Godot client                         | 19 / 24    |

1194 tests passing. **Phases 1, 2, 3, and 4 are complete.**

Phase 3 and 4 were split from one combined phase because they need opposite invariants: agent
comparison needs cards/rules **frozen**; balancing needs them **variable**. So Phase 3 freezes
content and varies agents; Phase 4 freezes agents and varies content. Phase 2 correspondingly
ends at a *correct* search, not a fast or tuned one.

**In progress: Phase 5** — the Godot client. Milestones A, B, and C are done (only card art
authoring remains outstanding from B). A hotseat game is playable end to end in the editor with
drag-and-drop, animation, an AI seat, save/resume, and a rules page reachable from both the lobby
and the in-game pause menu, and the screen is drawn from a fixed seat when you are playing an AI
rather than flipping to its side on its turn (D1). Two installs can also now host/join a real game
over a relay (D5) — see that step's own notes for what's verified versus still needing a manual
two-instance run through the Godot editor. What remains in Milestone D: UI polish/audio (D4) and
export (D6). Content is settled at `v1.7-final` and the balance
record lives in `balance/LOG.md`; the one item Phase 4 left open is a small seat-2 edge visible only
at large samples (see that phase's closing note).

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

> **⚠️ Determinizer caveat (permanent, accepted — the migration was cut, and this is where its
> reasoning lives).** The plan was to migrate IS-MCTS off the supplied-decklist cheat onto a real
> belief distribution. Dropped because the debt it pays off is **a measurement debt, and measurement
> is `Shapes.Sim`'s job, not the client's** — against a human the cheat is a *difficulty* setting,
> not a correctness bug, and one that makes the AI stronger, which is the direction a solo player
> wants. IS-MCTS on
> non-symmetric decks works by being handed its opponent's **real decklist**
> (`Determinizer(cards, opponentDeck)`). Hand contents and deck order are still hidden and sampled,
> but deck *composition* is not — the agent knows which 40 cards the opponent drew from, which a
> human would not. **Consequence, and it does not expire: treat any `ismcts` win rate measured on
> `--deck random` or `--deck custom` as an optimistic bound on that agent's true strength, and never
> compare it against a symmetric-deck run.** Nothing in `balance/LOG.md` is affected — that record is
> entirely symmetric-deck, where the shared decklist is public information anyway and the cheat is
> not a cheat.
>
> The known fix, if this is ever revisited, is a belief distribution: sample a plausible decklist per
> iteration, constrained to cards demonstrably played and filled uniformly otherwise. It changes only
> `Determinizer.UnseenCardsOf`, leaving the public surface and everything downstream untouched — so
> deferring it costs no architectural flexibility. Revisit only if custom-deck AI *strength* becomes
> the question being measured; against a human it reads as difficulty, not incorrectness.

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

### Phase 5 — Godot client (desktop + mobile) — 18/24, in progress

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

#### Milestone C — the other scenes — 9/9 complete

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
  opponent's supplied decklist, and now permanently — that migration was cut; see the Deck model
  caveat above for the reasoning and the standing consequence.
- [x] **C3. Persistence** (`user://`): decks, settings, progress — the durable-data half C6 didn't
  cover. Deck slots persist through `DeckStore` (write-through on every edit, one cached JSON
  document, corrupt-file-tolerant), mirroring `MatchSaveStore`'s pure-adapter/Godot-IO split.
- [x] **C4b. Card stats — cut, deliberately.** Win-rate/pick-rate context per card from
  `balance/LOG.md` was never going to survive contact with the client: every number in that file is
  measured against *symmetric* decks (see the caveat below), so showing it beside a card in a
  deckbuilder that exists to build *asymmetric* ones would be presenting a statistic under exactly
  the conditions that invalidate it. It would also freeze at whatever the last sweep said and drift
  silently on the next balance edit — the same failure `EffectText` exists to prevent for card text
  (A4). Stats stay in `Shapes.Sim`, which is where they can carry their own intervals and
  provenance. No code.
- [x] **C7. Rules info page** — a paginated overlay (`TutorialOverlay`) covering the full rule set,
  not just the four board-can't-show items originally scoped: objective, economy, cards, the board,
  type effectiveness (with the cycle diagram), scoring, merging (including that it can *increase*
  vulnerability), and fatigue. **Not a tutorial** — no scripted first game, no guided steps, no
  progress tracking; it is prose-and-pictures, paged with Prev/Next, closed by an X button or ESC.
  Reachable from both the lobby (`Lobby`'s Rules button) and the in-game pause menu
  (`BoardView.OpenPauseMenu` → `MenuPanel`'s Rules button), so a player stuck mid-match can read a
  rule without abandoning the game, and a new player can read them before ever starting one. Content
  is static hand-authored data (`TutorialContent`) rendered by pushing per-run font/color state onto
  the same `RichTextLabel` formatting stack `InlineResourceIcons` already uses for card text — no
  BBCode parsing, matching this project's rule that rules/card text is never markup. Images and two
  cropped, autoplaying/looping gifs (converted to sprite sheets at import time; Godot has no native
  .gif importer) live under `Art/rules/`, fit into a shared per-page image box so the panel reads as
  one consistent layout across very different source aspect ratios.

#### Milestone D — ship — 3/6

- [x] **D1. Viewer seat — separate "whose turn" from "whose screen."** Replaced the cut determinizer
  migration (whose reasoning now lives with the Deck model caveat above, where its standing
  consequence already was), and took the client-side half of D5 (multiplayer) forward:
  `BoardView.Render` computed
  `self = state.ActivePlayer`, so the board flipped seats every turn. That is correct for hotseat,
  which was the only mode that existed when it was written, and wrong for both modes that came after
  it. **Sequenced first in this milestone because it was a correctness bug in shipped single-player,
  not preparation for a feature that may never be built** — D5 already identified it as "a refactor
  this codebase wants regardless," and doing it here leaves D5's decision step about the server and
  nothing else.

  **What was wrong.** `ActivePlayer` was doing three jobs at once: engine turn order, which row is
  the bottom row, and whose hand is legible. Hand-hiding was `PlayerPanel.RenderHand` early-returning
  unless `isActiveHand` — hiding by *not drawing*, the console's step 2.5 precedent, which is sound
  only while one screen serves both seats. Against an AI seat this fanned the AI's hand face-up at
  the bottom of the screen and mirrored the board mid-game, once per AI action, held for
  `MoveDelaySeconds` by `RunAiTurns`' per-action `RefreshAll`. The player watched their opponent's
  turn from inside their opponent's seat, holding their cards.

  **What landed.** `ViewerMode` in `Shapes.Godot.Adapter`: `FollowsActive` (hotseat, and the right
  way to watch AI-vs-AI) or `Fixed(PlayerId)` (one human seat, later a network client), with
  `ViewerMode.For(seatOne, seatTwo)` deriving which — exactly one `AgentKind.Human` seat gives
  `Fixed(thatSeat)`, otherwise `FollowsActive`, so **local two-player hotseat kept its flipping
  behaviour unchanged and needed no lobby control**. `MatchConfig.Viewer` is a *derived property*
  rather than a stored field, which is why resume needed no changes at all: `SavedMatch` already
  persists both `SeatConfig`s, so a save written before D1 resumes with the correct perspective
  instead of a defaulted one. `GameRoot` resolves it per read (never cached — under `FollowsActive`
  the answer changes with the turn) and passes it into `Render`, which stops deriving it; the rail,
  avatars, identity and hand fan all follow from that one substitution.

  **The load-bearing split was in `PlayerPanel`.** `isActiveHand` conflated "this is my hand" with "I
  may act"; it became `showHand: player == viewer` and `interactive: showHand && viewer ==
  state.ActivePlayer`. That is the piece that makes the AI case correct rather than merely stable: on
  the AI's turn the human keeps seeing their own hand, inert, instead of the board changing sides.
  One guard on `GameRoot.Submit` makes input outside your turn a no-op — D5's generalization of
  `_aiTurnInProgress` to "not my turn," arriving early and testable with no transport.

  **Four defects the plan for this step did not name, each found while implementing it.** Move
  usability was gated on `slot.Owner == state.ActivePlayer`, a correct reading of "is this mine" only
  while the viewer *was* the active player — under `Fixed` it lit the opponent's move buttons up as
  usable on their own turn; merge-drag had the identical defect. `playableIds`/`discardableIds` are
  built from the active player's action list, so applied to the viewer's cards during an opponent
  turn they highlighted cards by card-id coincidence. And `PlayAnimation` was oriented by
  `ActivePlayer`, which on a board that no longer turns around plays the AI's attack travelling *away*
  from the human it is aimed at. All four are the same root cause as the headline bug — a seat
  identity standing in for a perspective — which is the argument for having done this as one step.

  **One thing added beyond the brief:** the End Turn button now reads "Opponent's turn N" when it is
  not yours. The old design could leave that implicit because the board itself flipped; once the view
  stops moving, a disabled button is too subtle to carry the distinction alone.

  **Not in scope, deliberately: redaction.** Clients still hold the full `GameState` including both
  hands; secrecy remains cosmetic. Fine for local play and *not* fine over a wire, but the fix (a
  per-seat projection on the wire, for which `Shapes.Ai`'s `ObservedState` is already the right shape
  and already written) only pays off against a real server — pulling it in would have made this step
  depend on D5's decision instead of standing free of it. It stays listed under D5.

  **Verified** by two headless harnesses that assert against the live `GameState` and the real
  `CardFace` nodes rather than eyeballing a screenshot: the vs-AI case held the human's hand on
  screen for all ~475 frames of the AI's turn with the viewer never moving off seat two (human in
  seat *two* deliberately — seat one is the case a "just check player one" derivation gets right by
  accident), and the hotseat case still follows the active seat across a handover. The check was
  itself checked: reverting `Viewer` to the old expression produced **912 violations** naming the
  AI's four visible cards, so the assertion can actually go red. 1165 unit tests pass;
  `Shapes.Core` untouched.

  **Debt:** the two harnesses (`ViewerSeatShotHarness`, `HotseatFlipShotHarness`) are temporary
  scaffolding on the `UiShotHarness` pattern, and the read-only accessors they need
  (`CardFace.CardId`/`IsDraggable`, `GameRoot.SessionForTesting`/`ViewerForTesting`) exist only for
  them — delete together. Their screenshot half is a no-op under `--headless` (the dummy driver has
  no viewport texture), so **nobody has visually confirmed the new frame reads well** — that the
  inert hand looks deliberately inert rather than broken. **Partly answered since, and the answer
  was "no"**: a windowed tour taken while scoping D2 caught the human's hand rendering at full
  opacity and full colour during the AI's turn, visually identical to a playable hand, with only the
  greyed "Opponent's turn N" button distinguishing the two states. **D2 answered the question and
  deliberately did not take the fix**: item 3 there solved the same "which of four reasons is this
  control inert" problem for move buttons, but the hand wants a panel-level restyle rather than a
  legibility mechanism, so it now sits with **D3**'s feedback-state pass beside the "Opponent's turn
  N" button it shares a frame with.
- [x] **D2. Action legibility — make a played turn readable while it happens.** Five changes to the
  board screen, all sharing one problem: **the client renders state, not events.** Every action
  resolves instantly into a new board, so anything that is not a lasting state change — which move
  fired, what card was played, what the damage was attributed to — exists only in the animation
  frame that showed it and is gone. That is survivable in hotseat, where you performed the action
  yourself, and it is the whole difficulty in the two modes that came after: watching an AI seat
  (shipped) and watching a remote seat (D5). **Sequenced before D3's visual pass deliberately** —
  these change what the board screen must *contain*, and restyling a screen before its content is
  settled means styling it twice.

  **D1 is the direct precedent and the reason this is one step, not five.** That step found four
  unnamed defects that were all one root cause (a seat identity standing in for a perspective);
  these five items are likewise one cause seen from five angles, and three of them (2, 4, 5) are the
  same event stream rendered at three durations.

  - **1. Pace every agent ~50-100% slower.** `GameRoot.MoveDelaySeconds` is `1.2` and paces every
    agent uniformly (its own note explains why: the rhythm of watching shouldn't change with which
    agent plays). Raise to ~2.0-2.4s. One constant, and the one item here that is purely a number.
    **Make it a `[Export]` rather than a `const`** so the value is tunable from the editor during
    this step's own playtesting instead of through a rebuild — the same reasoning C5 used when it
    introduced the delay.
  - **2. Recap panel: the last card played, held then faded.** Occupies the currently-empty left-edge
    gap between the type chart (`offset_top` 14→178) and the hover detail panel (bottom-anchored,
    −355→−12) — a real hole in the existing layout, not a new region. Reuses `HoverDetailPanel`'s
    renderer (`Show(CardText)`), so a recapped card looks identical to the same card hovered, per the
    C4/deckbuilder precedent of never adding a second card renderer. Holds ~4s, then fades; a newer
    action replaces it outright rather than queueing. **Shows for both seats** — decided rather than
    assumed: the uniform rule is simpler to reason about and to test, doubles as confirmation
    feedback for your own plays, and is the only variant that behaves correctly for an AI-vs-AI
    spectator, where *neither* seat is "yours".
  - **3. Mark moves already used this turn.** `CreatureInstance.HasUsedMove(i)` is **already public
    and already correct** — no engine change, so `Shapes.Core` stays untouched as the milestone
    requires. `MoveButtonFactory` currently renders an unusable move only as `Disabled` +
    `Modulate 0.55` alpha, which conflates *four* distinct reasons (used already / condition unmet /
    unaffordable / not your turn) into one grey. Give "used" its own treatment. **Both seats**, which
    costs nothing extra and is never ambiguous: `ResetMovesForNewTurn` clears the flags at the
    **owner's turn end** (not the next turn's start), so at most one seat's flags are ever set —
    "highlight both" reads as "what has been spent so far this turn" with no two-seat collision
    possible. Worth pinning that with a test, since it is the assumption the whole item rests on.
  - **4. Recap moves too, not just cards.** Same panel and same lifetime as item 2, showing move name
    plus the creature that used it. This is what makes item 2 worth building: **a card play is
    already visible** (a card leaves the hand and a creature appears), whereas a move firing is the
    single least legible action in the game — the only trace is a health number changing somewhere.
  - **5. Full action log, opened from a bottom-right icon button.** Bottom-**right** because the
    hover detail panel owns the bottom-left; the corner is currently free (the ⚙ button is
    top-right/preset 1) and `HandFan` spans that band with `mouse_filter = 2`, so nothing there
    swallows the click. A scrollable overlay on the `TutorialOverlay`/`MenuPanel` pattern (dimmed
    backdrop, ESC to close, `BoardView` owning which overlay is on top) rather than a fourth kind of
    modal. **Separate from the recap, not an expansion of it** — the recap auto-fades, so making it
    the click target means the affordance disappears exactly when a player wants it.

    **The log is a rendering of `StateDiff`, not new bookkeeping** — this is why item 5 is much
    smaller than it sounds. `GameRoot` already computes a `StateDiff` for *every* action at both
    seams (`Submit` for human, `RunAiTurns` for AI) and already appends the `GameAction` to
    `_actionLog`; A2 built `StateDiff` precisely because `GameState.TurnEvents` "has no
    damage/move-used/resource-change entries and is cleared on EndTurn." The pairing
    `(GameAction, StateDiff)` already carries every effect this item asks to log: health via
    `SlotDiff`/`CreatureSnapshot`, and score/resources/hand/deck/discard via `PlayerDiff`. So the
    work is a formatter over a stream that exists, plus retaining it. Describe actions through
    `EffectText`/`CardText`, never hand-authored strings (A4's rule).

  **Where this lands architecturally.** Formatting and the retained entry list belong in
  `Shapes.Godot.Adapter` (plain class library, `dotnet test`-reachable — the whole reason it exists);
  only the panels/overlay are `Shapes.Godot`. That split is what lets the log formatter be tested
  without the editor, which the screenshot harnesses notably cannot be.

  **Two things to decide while building, not before.** Whether the recap should suppress itself
  during animation (two cues for one action may read as duplication); and whether the log retains
  the whole match or a bounded tail — unbounded is correct for `SavedMatch`'s replay model but is a
  live-memory question on mobile.

  **Verification.** Item 1 is visual. Items 2/4 want a windowed run — **`--headless` cannot see any
  of this**, since the dummy driver has no viewport texture and `SavePng` silently no-ops (D1's
  standing debt, and the reason its harnesses never confirmed anything visually). Items 3 and 5 are
  the testable half: `HasUsedMove` reset timing, and log formatting as a pure function of
  `(GameAction, StateDiff)` in `Shapes.Godot.Adapter`. **Take the D1 debt's windowed run during this
  step** — it is already outstanding, it needs exactly the same windowed session, and item 3's
  treatment of "not your turn" is the thing that would fix the inert-hand frame it asks about.

  ---

  **What landed.** All five, `Shapes.Core` untouched, 1187 tests passing (22 new).

  **A second pass fixed four things the first cut got wrong**, all caught by reading the windowed
  frames rather than by any test — which is the standing argument for taking the windowed run as
  part of the step rather than after it:

  - **The recap's header was clipped by its own card.** `HoverDetailPanel.tscn` is authored with
    `z_index = 100` (it is normally the board's floating tooltip, drawn over everything), so a
    sibling header at the default z drew *underneath* it. Fixed by raising the header rather than
    resetting the card's z, which would fight the scene's authored value on every instantiation.
    The labels also needed an explicit `Size`: nothing lays out a child of a `Panel`, so
    `CustomMinimumSize` alone left `ClipText` with nothing to clip against.
  - **The spent marking took three cuts to land.** A strike-through hairline read as a rendering
    artifact, cutting through wrapped icon-embedded text at whatever height the row happened to be.
    A corner "USED" chip was legible but noisy — a second object crowding a row that already holds a
    cost pip, a name and a description. **What shipped is a dark scrim plus the row's own text
    recoloured amber**, which carries the same information in space the row already spends: nothing
    is added to the layout, and the whole row changes at once. **Precedence over the other three
    unusable reasons is the property being protected**, and the hue shift preserves it better than
    either predecessor: unaffordable / condition-unmet / not-your-turn all express themselves by
    *removing* contrast, so shifting HUE moves on an axis none of them touch. Spent is applied
    **instead of** the disabled fade rather than on top, or the amber would wash toward the same grey
    every other unusable move wears. The scrim is added *before* the content host so it draws under
    the text rather than veiling the colour the marking depends on.
  - **The marking now lasts until that seat's own next turn**, so a move stays flagged through the
    opponent's whole turn. **This required a new adapter component and is the one place D2 could not
    just read the engine.** `CreatureInstance.HasUsedMove` is the right source for *legality* — it
    is what `ActionGenerator` consults — but `ResetMovesForNewTurn` clears it at the owner's turn
    END, because that is all the engine needs. So the display it fed vanished at exactly the moment
    a spectator most wants it: "what did they just spend" is a question asked *while watching
    someone else's turn*. Changing the engine's timing was doubly wrong — the milestone forbids
    touching `Shapes.Core`, and that timing is correct for the job the engine has. How long a cue
    stays on screen is a view concern, so `SpentMoveTracker` holds the memory in the adapter, keyed
    by slot rather than by creature identity (a `CreatureInstance` is mutable and a merge folds one
    away entirely). Its subtleties, each pinned by test: a seat's record clears at the start of that
    seat's **own** turn so the two seats' markings coexist; the handover is processed **before** the
    action is recorded, so a move used as a turn's first action is not wiped by that same turn's
    start; and markings are dropped for emptied slots on every render, which covers death, merge and
    replacement without modelling any of them.
  - **Start-of-turn effects were filed under the wrong turn.** `ActionExecutor.Apply` runs
    `AdvanceToActions()` internally, so one `EndTurn` submit carries both the act of ending a go and
    the *next* seat's scoring, income and draw. Now split into two entries. **The subtle half:
    splitting on `TurnNumber` was not enough** — only the P2→P1 handover increments it, so P2's
    scoring stayed attributed to P1's End Turn line. The correct key is the **active seat changing**,
    which happens on every handover.
  - **The recap has two presentations and no caption above either.** The captioned header was
    dropped entirely: it overlapped the card it captioned (the card panel is authored at
    `z_index = 100` and wins against an ordinary sibling), and it spent ~60px the left edge does not
    have. **A played card now shows the card alone** — the face already carries its name, cost, art
    and moves, so a caption repeating the name was pure duplication and context already says a card
    appearing here was played. **A used move shows a compact 60px strip** — move name over creature
    name beside that creature's art — because a move has no card face of its own, and rendering the
    whole creature to say "it used one of these two" both buries the answer and costs the height the
    card case needs. Carried as an `ActionRecapKind` rather than inferred by string-sniffing the
    title. The card case also re-fits from `GetCombinedMinimumSize` after every `Show` (deferred a
    frame, since move-row heights are not settled until layout runs), so a one-line spell no longer
    paints the same box as a four-move creature.
  - **Sizing, not a visibility rule, is what resolves the left-edge crowding.** An earlier cut had
    the recap hide whenever the hover tooltip appeared. That fixed the overlap and broke the common
    case: playing a card yourself puts the cursor over your hand, so your own recap flashed up and
    vanished instantly. Removed. The type-cycle chart shrank instead (164px square → 140, with orbit
    and shape diameters scaled to match) — it is static reference material, where the other three
    things in that column are live.

    **The real bug took four rounds to find, and every earlier round "fixed" a non-problem.** The
    symptom was a played-card recap painting over the chart's caption while every rect the code
    could read said there was no overlap (chart ending 154, card reported 176–386). Three rounds
    went into nudging offsets and clamps against measurements that were *correct and irrelevant*.
    The cause: `HoverDetailPanel`'s root is a plain `Control`, so `GetCombinedMinimumSize()` on it
    returns **0** — the fit silently collapsed to the 210px floor every time — and the inner
    `PanelContainer`, which will not shrink below its children's minimum, responded by painting at
    its own 327px height *centred* on the rect it was given, overhanging 58px above a control whose
    own rect measured innocent. Measuring the inner panel instead made paint and measurement agree,
    after which the clamp worked as written. **Two lessons worth keeping**: a `Control` root reports
    no minimum for its children, and a container handed too small a rect overhangs rather than
    clips — so a parent's rect is not evidence about what its child paints. (A separate red herring
    along the way: `CallDeferred(nameof(PrivateMethod))` resolves through Godot's method table,
    which does not see an ordinary private C# method, so that call never fired at all.)
  - **The log now reads as prose.** `ActionLogText` is a log-specific describer, deliberately
    separate from `ActionText`: that one renders an action as an *identity* for the console's action
    menu (ids, exact costs) and is pinned by tests asserting that contract. A log line answers a
    different question, so it uses card names, names slots the way a player points at them
    ("Player 2's middle slot", never `P2:1`), and restates no costs — the effect lines underneath
    already say what was spent. Resource glyphs are gone too: these strings render into plain
    `Label`s, where `InlineResourceIcons`' sentinels mean nothing, so emitting `△▢◯` would have put
    a second, worse icon vocabulary on screen beside the real ones.

  **The estimate held: item 5 was the smallest of the five, not the largest.** `ActionLog` is ~200
  lines of formatter over a stream that already existed — `GameRoot` was already computing a
  `StateDiff` per action at both seams, so nothing new observes the engine. The one design call
  worth recording is that the readable log is **kept separate from C6's replay log** rather than
  widening it: `SavedMatch` serializes that one, and a save file should not grow a rendering concern
  it has no use for. Consequence, accepted and documented at the call site: **a resumed match starts
  with an empty log**, because `GameSession.Resume` replays actions below both seams and produces no
  diffs. Recovering that scrollback would mean replaying the whole match a second time purely to
  observe it.

  **Item 3 needed no engine change and no new state**, as predicted — `CreatureInstance.HasUsedMove`
  was already public and is the same flag `ActionGenerator` reads for legality, so the marking cannot
  disagree with the rule it depicts. `SlotView.Render` already held the `CreatureInstance`, so the
  flag is read there rather than threaded through the moves tuple. The treatment is a **strike-through
  plus a cooler, dimmer modulate**, deliberately not "more alpha": dimming further would have emitted
  the same signal as the other three unusable reasons, reading as *more* unavailable rather than as a
  different KIND of unavailable. Verified in a real frame on the case that motivated it — a creature
  with one move spent and one live, the two visibly distinct on the same card.

  **Both seat-scope questions were settled by a timing detail, not by taste.**
  `ResetMovesForNewTurn` fires at the **owner's turn end**, so at most one seat's flags are ever set
  and "highlight both" can never show two seats at once. That is non-obvious, sits under an
  unrelated-looking method name, and nothing in the UI would fail loudly if it changed — so it is now
  pinned by `SpentMoveMarkingTests`.

  **One thing the plan flagged to decide during the build, now decided:** the recap holds 1.7s then
  fades over 0.7s, against `MoveDelaySeconds` 2.4 — sized so an entry completes its fade *before* the
  next AI action arrives, rather than every entry being replaced mid-life and the fade never being
  seen. That coupling is why item 1's constant and item 2's timings had to be chosen together.

  **Deliberately still open:** whether the recap should suppress itself during animation. In the
  windowed run the two cues did not read as duplication, so it ships without suppression rather than
  adding a mechanism against a problem that did not appear. Log retention is likewise unbounded — the
  mobile memory question is real but is a D6 export concern, not a desktop one.

  **Not addressed here, and moved to D3:** the inert-hand frame. The windowed run confirmed D1's open
  question (the human's hand renders identically whether or not it is actionable), but the fix is a
  panel-level restyle rather than a legibility mechanism, so it belongs with D3's feedback-state pass
  alongside the "Opponent's turn N" button it sits next to.
- [~] **D3. Professional UI pass — IN PROGRESS, phases 1–2 of 4 done.** A full visual/UX polish beyond C-UI's board-screen HUD:
  consistent styling across lobby/card browser/deckbuilder/game-over, animation and feedback-state
  polish (hover/selected/legal-target states), and card art integration once B1c completes.
  **The lobby is the priority and is not a uniform pass**: a windowed screenshot tour of all five
  screens found the card browser, deckbuilder, and board already near shipping quality, while the
  **lobby never received C-UI's treatment and is still stock Godot theme** — flat grey, five
  identical button slabs, no hierarchy between "Start Game" and "Exit Game", on the first screen a
  player sees. Also found: **real edge-clipping in the deckbuilder and card browser** (both anchor
  their root `Layout` full-rect with no `MarginContainer`, so the deck count renders as `40 / 4` and
  the browser's search field truncates) — one root cause, one fix. Full findings in
  `d3-checklist.md`.

  **Scoped as four phases, so the foundation lands before the per-screen work rather than during
  it.** 1: `Palette` + `Theme`. 2: the clipping bugs. 3: per-screen passes (lobby hierarchy, then
  the checklist residuals). 4: hover/pressed/selected feedback states, which are cheap once a theme
  exists and near-impossible before. **Concept art was considered and dropped from scope** — the art
  pipeline already succeeded (48 cards, rules images, avatars), and these screens are not failing
  for want of art direction.

  **Phases 1–2 done.** `Palette` is the single source for colour and `UiTheme` builds the project
  Theme from it in code — **not** an authored `.tres`, which would have restated every value as a
  literal and drifted from `Palette` the first time one moved (the same reasoning `CardStyle` and
  `MoveRowFactory` each already carry). **Deliberately not a redesign**: every value is one already
  in use, moved rather than re-picked, so the styled screens stay put and only the lobby moves. The
  theme is applied at four scene roots and inherited by everything beneath; `CardStyle`,
  `TableBackdrop`, `BoardFrame` and `MoveRowFactory` now read from `Palette` rather than holding
  their own literals. The lobby and both collection screens also gained the `TableBackdrop` only the
  board had, which was the other half of why they read as flat grey. Phase 2 was one line per scene
  (offsets on the root `Layout`), and `40 / 4` now reads `40 / 40`.

  **Verified by a before/after screenshot pass over all five screens**, taken windowed because
  `--headless` cannot see any of this. That baseline earned itself immediately: it caught a
  regression the change had introduced but nobody would have gone looking for — the first disabled
  style sat so close to the resting one that the rules overlay's disabled `< Prev` and enabled
  `Next >` became indistinguishable. Fixed by dropping `ControlDisabled` clearly below the resting
  surface. **66 colour literals remain** in component-local files (badges, charts, animator cues);
  those are phase 3's business, and the structural sources are consolidated.

  **A follow-up pass then fixed three things the theme itself caused or left undone**, two of them
  regressions it introduced — which is the argument for treating "apply a theme" as a change needing
  the same scrutiny as a feature, not a free win:

  - **Scrollbars vanished on both collection screens.** A `ScrollBar` has no minimum size of its
    own: it is exactly as wide as its stylebox's content margins make it. Godot's default boxes
    carry those margins, a bare `StyleBoxFlat` does not, so styling the bar without restoring them
    collapsed it to a hairline. Now sized explicitly from a `ScrollThickness` constant.
  - **Buttons were felt-and-gold, not grey.** The board is the game's signature surface, and a grey
    button beside it reads as generic UI bolted onto a themed game. `Control`/`ControlHover`/
    `ControlPressed` are now the felt darkened toward the backdrop, with the board's own frame gold
    arriving on the border at hover and on the label when pressed.
  - **In-play creatures had been damaged by the theme.** `SlotView`'s HP/status band is a bare
    `PanelContainer`, so giving *every* `PanelContainer` card stock plus a 2px border turned it into
    a second card nested inside the first; the move buttons likewise inherited the new green and made
    each creature read as a control panel bolted over its art. Both are now styled explicitly at the
    component — a band on a card is not a card, and a move row is part of a printed face, not app
    chrome. **The general lesson: a theme sets the floor for chrome, and anything that is really
    *artwork* has to opt out of it deliberately** rather than inherit and hope.

  Then two more, from reading the result again:

  - **Enabled buttons lightened** so "can I press this" needs no second look — the felt-vs-disabled
    contrast ratio went 1.40 → 1.85, with the label at 5.64 on an enabled control and 2.40 on a
    disabled one.
  - **Focus draws nothing at all.** Godot leaves focus on a button after a click, so a visible focus
    style is not a focus indicator — it is a permanent marker on the last thing you pressed, which
    reads as "this is selected" when nothing is. Worst on a spent move, which kept an outline for the
    rest of the turn on top of its amber. Hover already answers "what is under the cursor", which is
    the question a mouse player asks. **The tradeoff is explicit**: keyboard/gamepad navigation loses
    its position indicator, accepted because nothing here is keyboard-driven, and the fix if that
    changes is to show focus only when it arrived from a key — not to restore it unconditionally.

  **Phase 3 (structure) then reorganised navigation**, which was the half of P0 that theming could
  not reach: the lobby had five peer buttons in one stack, so "Start Game" and "Exit Game" carried
  identical weight. Now **Home** is a title screen of four — Play (primary, larger), Deckbuilding,
  Rules, Exit — and match setup moved to its own **Play** panel holding both seats, Resume vs. Start,
  and a direct "Edit Decks" link, since "these decks are wrong" is the thought a player has *inside*
  match setup. **Deckbuilder and Card Browser now link to each other** rather than routing through
  the menu; they render cards with the same components and answer the same question, so the round
  trip was friction with nothing behind it. The browser consequently left the home menu — it is
  reached from the deckbuilder, where looking cards up is what you are already doing.

  **Home and Play are one scene, not two.** Everything Play needs — the loaded `CardDatabase`, the
  deck slots, the `PendingMatch` handoff, the deck-legality check — is already owned by `Lobby`, so a
  second scene would mean duplicating that or inventing a way to share it. The deck pickers are
  repopulated on entry to Play rather than only in `_Ready`, because Play → Edit Decks → Back lands
  on Home without rebuilding the scene, and the next Play must not list decks as they were.

  **Verified by WALKING the graph, not photographing it.** A harness pressed the six real buttons in
  sequence (Home → Play → back → Deckbuilding → Card Browser → Deckbuilding), failing loudly on any
  button that was missing, disabled, or invisible — which is what actually proves the wiring after
  moving every control into two new panels. It found its own bug first: `ChangeSceneToFile` replaces
  the scene root, so a harness that instantiated the lobby as its own child was freed at the first
  scene change and the walk stopped silently. **Driving a multi-scene flow requires an autoload**,
  which sits outside the swapped tree.

  **Two follow-ups then settled the visual grammar.** The cross-link moved to sit **beside each page
  title** rather than out among that screen's own controls — on the deckbuilder it had landed next to
  "Delete Deck", pairing a navigation link with the one destructive action on the screen. Title and
  link now travel together as a unit on both screens, with a `→` marking it as an exit rather than a
  control that acts on the current page.

  **Button sizing collapsed to two tiers.** It had drifted to four heights (44/48/52/62) across three
  font sizes with no rule, so "tall" and "short" were not reliably distinguishable and the tiers
  carried no meaning. Now **primary is 68px/24pt and secondary is 40px/15pt** — a 1.7× height and
  1.6× font ratio, wide enough that the distinction survives a glance. Exactly one primary per screen
  (Play on Home, Start Game on Play); the seat dropdowns stay at 44px because they are inputs, not
  actions, and should not read as either tier.
- [ ] **D4. Polish:** sound, transitions, menus. Audio wants an asset-source decision *before* this
  step rather than during it.
- [x] **D5. Small-scale multiplayer — built.** Decided: build it. Two installs can host/join over a
  relay and play a real game against each other, with the host choosing seat order (First/Second/
  Random) and their own deck, and the joiner entering the resulting code and choosing theirs.

  **Why this is cheaper than it looks.** Three Phase 1 decisions, taken for unrelated reasons,
  happen to be exactly what a netcode protocol needs. `GameAction` is flat, immutable, value-equal,
  and **fully self-describing — nothing asks the player a question at apply time** (a choice is
  pre-resolved by *being* a distinct legal action), so the wire protocol never needs a mid-resolution
  round trip. `SavedMatch`'s `ActionDto` is already a serialization of it. Determinism (`IRandomSource`,
  no `Random.Shared`/`DateTime.Now` in `Shapes.Core`, stable across platforms and .NET versions) means
  the wire format is **seed + ordered action log**, not board snapshots — and `GameSession.Resume`
  *is* the reconnect path, already written and already exercised by C6.

  **The client-side seat refactor is no longer part of this step — D1 took it.** What was the bulk
  of this item's effort (threading a viewer seat through `BoardView`/`PlayerPanel`/`SideRail` and the
  targeting paths, and generalizing `_aiTurnInProgress` to "not my turn") is done standalone there,
  for the single-player reasons in D1's own entry, so a `Fixed(PlayerId)` viewer already exists for a
  network client to reuse. Three pieces remain here. **Redaction**, which D1 explicitly left alone:
  clients today hold the full `GameState` including both hands, so hidden information is enforced by
  what is drawn rather than by what is sent — over a wire it becomes a per-seat projection, and
  `Shapes.Ai`'s `ObservedState` is already that projection. `GameRoot.Submit` must accept actions
  arriving unprompted rather than only submit-then-refresh. And the failure states — disconnect,
  timeout, concede, rejected action — which are boring, unavoidable, and always underestimated.

  **Redaction is CLIENT-SIDE ONLY, and that is a decision rather than an oversight.** Seed + action
  log and true redaction are in direct tension: a client holding the seed can re-run the same
  deterministic shuffle the server did and derive both hands, so *no* amount of projection on the
  wire hides information from a client that already has the seed. Server-enforced hiding would mean
  withholding the seed entirely and sending per-seat state deltas instead — which discards the whole
  reason this step is cheap (`GameSession.Resume` as the reconnect path, `ActionDto` as the wire
  format, replay-from-seed as the desync check) and replaces it with a second, separately-verified
  serialization of `GameState` — precisely the round-trip contract C6 rejected. **At friends-only
  scope the seed is shared and `ObservedState` is a client-side rendering boundary, not a security
  boundary.** It stops the UI from drawing what a player shouldn't see; it does not stop a modified
  client from computing it. Accepted because the threat model is "my friend", where a referee for
  honest disagreement is the thing worth paying for and cheat-proofing is not. **The line to
  re-open this at:** anything beyond friends/self — a public queue, strangers, or a ladder — makes
  the shared seed indefensible, and that is a redesign of the wire format, not a patch to it. It is
  therefore listed under the exclusions below rather than as a later enhancement.

  **Direct P2P was ruled out, not just deprioritized.** The original plan text above framed the
  server as an optional matchmaking convenience over a possibly-P2P transport. Confirmed with the
  user instead: plain direct TCP (host prints a LAN IP, friend types it in) only works when both
  players share a network — across two different homes, NAT blocks the unsolicited inbound
  connection direct play needs, with no workaround short of the host configuring port forwarding
  per session. A **relay both clients dial out to** is not optional at this scope; it is the only
  transport that actually satisfies "works remotely between friends," since outbound connections
  are never blocked. This is a stronger claim than the original plan text made, and it is why the
  relay shape below is not "the fancy option" but the only one that meets the exit bar.

  **`IMatchTransport` — the seam, done as planned.** `Shapes.Godot.Adapter/IMatchTransport.cs`:
  `ActionReceived`/`PeerDisconnected` events, `SendAsync`. Every local mode (hotseat, vs-AI, C6
  resume) needed no implementation at all — `GameRoot` simply never constructs one, which already
  *is* the "no server running" case the plan called for; only a network match builds a real one.

  **`Shapes.Relay` — a real, minimal relay, built now rather than deferred to a VM that doesn't
  exist yet.** The user had not stood up an Oracle Cloud (or any) server, and didn't want the
  networking work to sit idle waiting on that. So the relay shipped as its own ordinary
  `dotnet run`-able ASP.NET Core project (`Microsoft.NET.Sdk.Web`, WebSockets, ConcurrentDictionary
  match table, no persistence) rather than a design document — usable today on `localhost` for
  same-machine/LAN testing, and movable to a real VM later with zero rework: "copy the binary,
  `dotnet run -- --port <n>`, open that port, change the client's one `ws://` URL setting." It
  deliberately does **not** reference `Shapes.Core`/`Shapes.Ai` (see its own header): it is a dumb
  pipe that pairs two sockets by a 6-character code and forwards frames verbatim, never inspecting
  or validating a `GameAction` — the referee-when-clients-disagree role the original plan text
  described for a full server is **not** implemented; see "left undone" below. The host is the
  seed/seat authority (it resolves "First/Second/Random" and computes the seed), sent to the
  joiner as `MatchStart` once paired, keeping the relay itself rules-free per the redaction
  decision below. **Where Oracle Cloud fits:** exactly where the original plan put it, as the
  eventual host for this same binary — nothing about that changed, it's just not blocking today.

  **`RelayMatchTransport` (`Shapes.Godot.Adapter`)** wraps a `ClientWebSocket` for the host/join
  handshake and then forwards `GameAction`s both ways for the match, reusing `SavedMatch.cs`'s
  existing `ActionDto`/`SeatDto`/`DeckListDto` conversions rather than a second serialization —
  the "cheaper than it looks" case above held up exactly as described. `Lobby.cs` gained a third
  Home panel (**Play Online**, Host/Join tabs) alongside Home/Play, following D3's own panel-swap
  pattern rather than a new scene. `MatchConfig` gained one new field, `ViewerOverride`, because a
  network match is `SeatConfig.Human` vs `SeatConfig.Human` — the exact shape `ViewerMode.For`
  reads as local hotseat and flips every turn, correct only when one screen serves both seats.
  Checked first in `MatchConfig.Viewer`, so every existing local mode (which never sets it) is
  unaffected — pinned by test (`MatchConfigTests`).

  **Redaction shipped exactly as scoped: client-side only.** Both processes still hold the full
  `GameState`; `ObservedState` was not wired into the wire protocol, matching the decision below
  that a shared seed makes server-enforced hiding pointless at friends-only scope.

  **What was cut from this pass, deliberately, to keep the exit bar (two installs, a real game)
  achievable without also rebuilding C6:**
  - **No resumable network match.** `SaveProgress` is a no-op when a transport is present — a
    disconnect ends the match (`BoardView.ShowDisconnected`, reusing the game-over modal) rather
    than leaving a resumable save, since C6's replay-from-seed has no peer to replay against once
    the connection is gone. This is `reconnect-to-stranger`'s exclusion generalized to
    "reconnect at all" for this cut, not a silent gap — flagged here for exactly that reason.
  - **No state hashing.** The plan's "add it in the same commit as the first networked action"
    did not happen — both sides deriving state independently from one seed is trusted, not
    verified, so a desync (a bug, not an adversarial client) would currently surface later and
    confusingly rather than as an immediate assertion. **Left as the clearest follow-up** if this
    gets more real-world use: a per-action state hash, checked on both `RelayMatchTransport` and
    (for symmetry) `LocalTransport`.
  - **No referee.** The relay never calls `LegalActions().Contains(action)` — it doesn't load
    `Shapes.Core` at all (see above). Fine at friends-only scope (the threat model is "my friend,"
    not an adversarial client) but is the piece a public release would need back.
  - **Verification gap, and it's a real one, not a formality: no Godot editor was available in
    this environment**, so the actual Lobby → Host/Join → GameRoot UI flow has not been run or
    screenshotted end-to-end — only its non-Godot half is proven. What **is** verified: the full
    solution builds clean (`dotnet build`) including `Shapes.Godot`/`Shapes.Godot.Adapter`; all
    1194 tests pass (1187 prior + 7 new: `RelayProtocolTests` DTO round-trips, `MatchConfigTests`'
    two new `ViewerOverride` cases); and a live two-client run against a real running
    `Shapes.Relay` instance (host code issued, joiner paired, `MatchStart` delivered with the
    correct per-recipient seat, a `GameAction` forwarded in both directions, and a disconnect
    correctly raising `PeerDisconnected` on the still-connected side) — everything below the
    Godot node tree. **The user still needs to run two real instances through the Lobby's Host/Join
    buttons at least once** before this is trusted the way D1/D2's own playtesting-found-real-bugs
    precedent would want.

  **Explicitly out of scope, unchanged from the original plan:** accounts, rating/ladder, chat,
  spectating, reconnect-to-stranger, and server-enforced hidden information (see the client-side
  redaction decision above) — each comparable in size to the whole core system, and each a
  public-release concern this step was never scoped to cover.
- [ ] **D6. Export pipeline — the last step, after everything above.** Desktop + signed Android
  `.aab`, reusing/re-verifying the step 1.13 toolchain: export templates need the .NET 9 SDK
  alongside .NET 8, Editor Settings needs explicit Java/Android SDK paths, and rebuilds need
  `adb install -r` or a stale APK silently masks the change.

**Exit criteria:** full game playable with visuals on desktop and on a physical Android device;
a seeded hotseat game matches the console's result for the same seed; deckbuilder validates
against engine rules; a backgrounded game resumes; a new player can learn the type cycle from the
in-app rules page without external explanation; `Shapes.Core` unmodified from Phase 4.

**Two criteria were dropped rather than met, both deliberately.** "AI plays custom decks without
assuming a mirrored opponent decklist" went with the cut determinizer migration — it was a
measurement-quality bar, and measurement is `Shapes.Sim`'s job; the standing caveat above is the
honest version of it. Per-card
stats in the client (C4b) went for a sharper reason: it would have displayed symmetric-deck numbers
inside the one screen built for asymmetric decks. If D5's multiplayer is built, add one more:
a networked game must produce the same result on both clients, asserted by state hash, not by
watching it look right.

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
