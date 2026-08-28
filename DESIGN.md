# Shapes — Design & Development Record

A design document for brainstorming and finalizing the game's ruleset, structure, and development timeline. Useful context to keep Claude informed and on-track with plans.

## 1. Ruleset
See [info.md](info.md) for a more player-oriented tutorial.

**Resources & types** — three resources in a rock-paper-scissors cycle: △ Spike, ▢ Anvil, ◯ Wheel. Effectiveness: Anvil → Spike → Wheel → Anvil. 2× damage if super effective, otherwise 1× (no resistance/halving).

Merged (multi-type) targets take 2× if one of their types matches the attacker and another is weak to it — so merging can *increase* vulnerability. Creature types come from resource cost, always — a move/spell's attack type is its own cost's resource type; a creature's defensive type is its play cost's. Type effectiveness applies after all flat modifiers. Lives as `TypeChart` on `RuleSet`.

**Creatures & moves**
- Top-left resources = play cost
- No auto-attack/passives; all damage from activated moves
- No summoning sickness; each move usable once per turn.

**Merging** — free action between two adjacent, un-merged friendly creatures: health summed, moves unioned, types combined, one slot, cannot merge again.

**Board** — 3 slots per player; slot *i* opposes enemy slot *i*.

**Turn structure** — `GameState.AdvanceToActions()` is the one entry point sequencing turn order:
1. Score (+1/friendly creature with an empty opposing slot)
2. Check win condition (score ≥ `scoreToWin`)
3. Income (each turn, 2 of each resource)
4. Draw (burn on overdraw)
5. **Actions** (play/move/merge/discard, any order, repeatable)
6. End turn


**Drawing vs. discarding** — overdrawing burns the just-drawn card, but the finalized ruleset has an infinite hand size. Discard effects allow the player to choose discarded cards.

---

## 2. Design decisions

**Language/runtime: C# on .NET 8.** Base game developed on standard C#, then ported to Godot for UI development. Phases structured such that porting is a client swap, not a rewrite. `Shapes.Core` is a pure class library (zero UI deps, enforced by test that it references only the BCL) — console, AI, tests, and Godot are interchangeable consumers.

**Project structure** — see [README.md](README.md#project-layout) for the full layout.

**State representation**
- Authoritative state: Plain mutable C# classes that the console/tests/Godot read. Optimized for maintainability
- Search state: Same data, optimized for search speed by AI agents (MCTS)

**Determinism** — all randomness through one seeded `IRandomSource` (hand-rolled xorshift64*, `Fork()`-able so search clones don't advance the real RNG stream); no `Random.Shared`/`DateTime.Now` anywhere in `Shapes.Core`.

**Cards are JSON data interpreted by a small effect engine** — alternative to C# subclasses for each card. Preferable since a subclass-per-card becomes the Phase 4 balance bottleneck (recompile per tweak, AI can't reason about card text). Effect vocabulary (~48 real cards) covers ops with a small targeting-selector language (`self`, `opposing`, `chosen_enemy`, etc.); `chosen_*` selectors expand into distinct legal actions for MCTS.

**Single-target rule**: a move/spell may need at most one player-chosen target — keeps branching flat (N, not N×M), cards readable, and the Phase 5 targeting UI a single state. Enforced at card-load validation.

**Deck model** — every game is played with a `Deck` (`Shapes.Core/Cards/Deck.cs`), dealt through the single `GameSetup.Deal` entry point that the console, sim, Godot adapter, and the test fixtures all share.

Deck limits: 40 cards, maximum of three copies of any card

Three deck sources:
1. Default deck — one copy of every card, console's only deck, exempt from the 40-card limit
2. Custom decklist
3. Constrained random deck (reasonable average cost, containing ≥10 cards of each resource cost). `Shapes.Sim` selects between them with `--deck default|custom|random`.

**Rules as configuration** — income, scoring, draw, hand limit, win condition, type chart all live in a `RuleSet` loaded from JSON, so a balance experiment is just a named ruleset file. Board size is the one exception (structural, not a balance knob).

**IS-MCTS as AI agent** — Suitable for an imperfect-information game, no hand-authored eval function, good branching factor. Chose single-observer IS-MCTS with per-iteration resampling over minimax/expectimax (no eval fn) and AlphaZero-style neural (wrong tool for balance tooling).

**Testing strategy** — xUnit, written alongside each component. Seeded determinism and data-driven (synthetic) test cards keep tests cheap and stable under rebalancing. `StateBuilder` is the fluent fixture for exact board positions without playing toward them. Coverage priorities: effect interpreter and rules engine (console rendering isn't worth testing). Test every op/mechanic, not all code lines.

---

## 3. Development Timeline

| Phase                                    | Progress   |
|------------------------------------------|------------|
| 1 — Playable engine                      | 13 / 13    |
| 2 — IS-MCTS AI (naive, correct)          | 6 / 6      |
| 3 — Agent measurement & optimization     | 9 / 9      |
| 4 — AI-driven game balance               | 14 / 14    |
| 5 — Godot client (desktop/mobile)        | 25 / 25    |

### Phase 1 — Playable engine

**Goal:** complete, correct, rules-configurable game with a text interface, tests written alongside each piece.

- [x] **1.** Installed .NET 8 SDK.
- [x] **2.** Solution skeleton includes `Shapes.Tests`; `Shapes.Core` only references BCL (Base Class Library).
- [x] **3. Primitives** — immutable structs. `ResourcePool.Subtract` throws rather than clamps, so a bad payment fails loudly instead of hiding a legality bug.
- [x] **4. `RuleSet` + JSON loading** — validates at load; unknown properties rejected (catches typos like `scoreToWinn`).
- [x] **5. State model** — mutable `GameState`/`Board`/`CreatureInstance` with `Clone()`; seeded RNG with `Fork()` so search clones never advance the real stream.
- [x] **6. Effect interpreter + op vocabulary**, built before card data so it was validated against real cards. Damage order pinned: `(base + next_attack_bonus + next_damage_taken_bonus) × typeMultiplier`.
- [x] **7. Card JSON schema + loader + validation** — single-target rule enforced across every effect in `CardValidator`.
- [x] **8. Action model** — `PlayCard`/`UseMove`/`Merge`/`Discard`/`EndTurn` + legal-action generation. Value-equal actions so MCTS dedupes correctly; generator decides legality, executor trusts it.
- [x] **9. Turn loop** — score→income→draw→actions→end folded into one `AdvanceToActions()` entry point so no caller can forget to sequence it.
- [x] **10. Entered all ~36 real cards** — about a third needed new engine mechanics (attack buffs, taunt expiry, reactive triggers, hand-composition scoring, a `health_source` selector).
- [x] **11. Console client** — `BoardView` + `Program.cs`, verified to a real scripted win.
- [x] **12. Fuzz harness** — 10,000 games on the real card set, asserts games actually terminate (~7s).
- [x] **13. Mobile toolchain spike** — confirmed Godot 4 C#/.NET Android export on a physical device. Notes for Phase 5: export *templates* need the .NET 9 SDK alongside .NET 8; Editor Settings needs explicit Java/Android SDK paths; a stale-APK trap means rebuilds need `adb install -r`, not just re-export.

**Exit criteria:** all met — two humans play to a win at console; all ~36 cards implemented; scripted games replay identically from seed; apply/undo property tests pass; every effect op tested; fuzz harness clean over 10k games.

### Phase 2 — IS-MCTS AI (naive but correct)

**Goal:** a working agent search that can join and play a game; deliberately not efficient, tuned, or measured.

- [x] **1. `IAgent` interface** — `Choose(AgentContext, CancellationToken)`, plus `RandomAgent` as reference implementation. Contract pinned at the interface level: legal choice, no state mutation, seed-determinism.
- [x] **2. `ObservedState`** — strict per-player projection of `GameState`, structurally unable to expose hidden information (opponent's hand contents, either player's deck order).
- [x] **3. Determinizer** — samples a real `GameState` consistent with observations via multiset subtraction from the (symmetric-only) shared decklist.
- [x] **4. Baseline agents** — Added `GreedyAgent` that scores from static heuristics and agent tests.
- [x] **5. Console hides opponent's hand** — `--reveal` flag off by default.
- [x] **6. IS-MCTS** — selection/expansion/playout/backprop, availability-corrected UCB1, fresh determinization every iteration.

**Exit criteria:** all met — search implemented end to end; satisfies the `IAgent` contract; mechanisms tested and verified; a full game watched and read; `ObservedState` provably leaks nothing; console hides the AI's hand by default.

### Phase 3 — Agent measurement & optimization

**Goal:** make the AI provably stronger than before. Cards/rules **frozen** all phase. All changes must be measured using a batch runner.

- [x] **1. `Shapes.Sim`** — headless batch runner, both seats/pairing reported separately (never pooled), stats tracked. Baseline (30 games/pairing, 200 iterations): `ismcts` beat `random` by ~97pt, `greedy` by ~77pt.
- [x] **2. Playout policy** — `IPlayoutPolicy` determines which actions for IS-MCTS agents to try next. Default `UniformPlayoutPolicy` and greedy `HeuristicPlayoutPolicy` exposed as opt-in `ismcts-heuristic`. `ismcts-heuristic` is stronger but ~1.3–1.9× slower per decision, so it stays opt-in rather than default.
- [x] **3a–3c. Performance optimization** — **(3a)** tuned `PlayoutDepth` 400→200 from a measured length distribution, 2.1× speedup; **(3b)** `PlayoutActionSampler.SampleOne` reservoir-sampling fast path for uniform playouts, ~1.07×; **(3c)** removed an unconditional defensive copy in `Board.RemoveDead()` and converted `EffectContext` to a `readonly struct`, ~1.09×.
- [x] **4. Measured determinizations per search parameter** — `IterationsPerDeterminization` (default 1) lets iterations share a sampled world. Both speed and quality were largely unaffected.
- [x] **5. Tuned exploration constant** — Found `c=1.0` (53.3%) better than default `c=sqrt(2)` (47.5%). `DefaultExploration` is now `1.0`.
- [x] **6. Re-verified correctness tests still pass** at the tuned setting.
- [x] **7. Recorded the final agent matrix:** `ismcts` > `greedy` > `random`.

**Exit criteria:** all met — IS-MCTS decisively beats both baselines; decision complete quickly; agent configuration is frozen and recorded.

### Phase 4 — AI-driven game balance

**Goal:** Ensure rulesets are fun and fair, cards are roughly equal in power, no moves are overpowered. Agents frozen this phase. All changes tested with the same seed, and detailed balance documentation lives in `balance/LOG.md`.

- [x] **1. Metrics.** `MetricsReport.From` aggregates a whole
  `BatchResult` (never pooling seats): per-seat win rate, length, per-card play/draw win rate, merge frequency normalized per creature played, endings.
- [x] **2. Answers design questions.** Merges are not auto-takes, and income compounding snowballs.
- [x] **2b–2c. Made metrics rankable.** Wilson intervals on every rate; take rate as primary balance signal; score margin [0.31, 1.29] excludes zero; plus unopposed-slot occupancy, survival, and cost pressure.
- [x] **2d. Metrics explorer.** `--report` flag builds a self-contained HTML page to compare stat differences between versions, useful for step 6.
- [x] **2e. Calibration cards.** Six deliberately mispriced spells that tested effectiveness of balance detectors. Lesson: take rate alone cannot rank economy/tempo-neutral cards.
- [x] **3. Rules sweep.** Edited income, hand size, scoring mechanics (see `LOG.md`).
- [x] **4. Console upgrades.** Console gained `EffectText`, which *synthesizes* and displayed card text during a game. 
- [x] **5. Metrics upgrades.** Metrics gained a per-turn take rate, separating "not urgent" from "not wanted" — identical on the per-decision denominator.
- [x] **5b. Fatigue mechanic.** The scoring rules reveal a stalemate issue: score requires unopposed creatures, so any board where defense ≥ offense stops scoring permanently. 7/26 creatures stalemate in self-mirrors alone. Fatigue preferable to banning self-heal and max-health buffs as mechanics. Rule: empty deck at turn start gives the opponent 1 score.
- [x] **6. Card sweep** — Edited 20/36 cards towards balance, including some reworks (see `LOG.md`).
- [x] **7. Reduced game length.** `scoreToWin` reduced to 7, increased many card costs.
- [x] **8. First-player balance.** To nerf first player advantage, second player gets an extra 1/1/1 resources plus 1 card. Second player is now slightly stronger.
- [x] **9. Final card sweep.** Edited 23/36 cards towards balance.

**Exit criteria:** all met — rulesets, cards, moves are all roughly balanced.

### Phase 5 — Godot client (desktop + mobile)

Target Windows/Android from one codebase. Everything new lives in `Shapes.Godot` or `Shapes.Godot.Adapter` — `Shapes.Core` stays unmodified from Phase 4.

**Milestones:**
- A: one playable game screen
- B: polished game screen
- C: other screens (lobby, card browser, deckbuilder, rules)
- D: final polish and exporting
 
#### Milestone A — one playable screen (hotseat, no art)

- [x] **A1.** `Shapes.Godot` added to the solution, builds clean alongside the other 7 projects.
- [x] **A2. Adapter layer** — `GameSession` (owns the game, mirrors console setup exactly) and `StateDiff` (diffs state before/after each action). Needed its own project (`Shapes.Godot.Adapter`) since the Godot SDK isn't `dotnet test`-reachable outside the editor.
- [x] **A3. Board scene** — responsive/touch-first: `GameRoot` → `BoardView` → `PlayerPanel` ×2 → `SlotView`/`CardFace`, plus a tap tooltip. Bugs found only by playtesting — the standing lesson for every Godot step since.
- [x] **A4. Card text synthesized from the op vocabulary**.
- [x] **A5. Target-selection UI** for `chosen_*` actions — one flat state (no chaining) thanks to the single-target rule.
- [x] **A6. Decided against undo and confirmation dialogs** — Undoing draws can reveal info to players. Design note, no code.

#### Milestone B — make it feel like a game

- [x] **B1a. Drag-and-drop** replaces tap for play/merge; moves are always-visible buttons; discard stays tap-based.
- [x] **B1b. Status/keyword badges** on board slots (taunt/reflect/ricochet/stun/attack-buff), visible at a glance.
- [x] **B1c. Added real card art.** Art pipeline (`CardArt.For(cardId, ...)`, keyed on card id with a placeholder fallback) and layout groundwork landed, then every card was authored: `Art/cards/` and `Content/cards/` hold a matching id per card with no gaps.
- [x] **B1d. Event animations** — play/move/merge/damage/heal/destroy/score, via a transparent overlay (`BoardAnimator`).

#### Milestone C — the other scenes

- [x] **C1. Lobby / match setup with a working AI seat** — per-seat choice of human or agents, mirroring console's agent factory. AI turns currently run synchronously per action (fixed by C5).
- [x] Added 12 new cards (36 -> 48)
- [x] **C2. Deckbuilder** — Engine allows for custom / randomly generated decks (40 cards, ≤3 copies). UI surfaces a Deckbuilding tab with 10 deck slots. Partial decks save, legality enforced at match start. Agents now reason about decklists per player.
- [x] **C3. Persistence** — Decks, settings and progress saved per user and persist on app exit.
- [x] **C4. Card browser** — every card shown in full detail, filterable/searchable, paginated grid, identical to in-game. Card stats are not surfaced to the UI.
- [x] **C5. AI opponent responsiveness** — moved the search to the thread pool with a paced delay between moves.
- [x] **C6. Interrupted-game persistence** — seed + action log (not a `GameState` serialization), replayed on resume via Phase 1's determinism guarantee. Saves after every action so a mobile OS kill mid-turn still resumes correctly; one save slot, cleared on game-over.
- [x] **C7. Rules info page** — a paginated rules overlay: objective, economy, cards, the board, type effectiveness, scoring, merging, and fatigue.
- [x] **C-UI. Professional game screen UI** — replaced Godot's default theme; introduced per-player game sidebars, icon chips for resources, and a consistent visual language (palette, type, spacing, buttons) applied across board slots, hand cards, and the status bar.

#### Milestone D — ship

- [x] **D1. Viewer seat fix** — Depending on singleplayer, local multiplayer, or online multiplayer, correctly handles whether player perspectives flip on each turn.
- [x] **D2. Better action legibility** — AI moves slower; a recap panel shows last card played or move used; moves used in a turn appear distinct; and a full action log.
- [x] **D3. Professional UI pass** — Consistent styling/theme across all scenes, especially the lobby. Reorganized menu button navigation.
- [x] **D4. Added SFX and background music** — Imported 14 SFX + 4
  music tracks, with a Sounds panel for volume customization.
- [x] **D5. Small-scale multiplayer** — Two installs can host/join over a relay and play a real game. Easy because Phase 1 decisions fit a netcode protocol. Built `Shapes.Relay`, a real minimal ASP.NET Core WebSocket relay, and a new Play Online lobby panel (Host/Join tabs).
- [x] **D6. Export pipeline** — Desktop export and Android APK export both work. No `.aab` signing (not planning on releasing to Google Play Store).
- [x] **D7. Mobile UI pass** — Scaled up and adjusted margins for game's screens to work on mobile.