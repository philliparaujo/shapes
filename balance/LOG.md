# baseline
Everything is using default rules came up with pre step 4.3.
Economy rules: 1/1/1 + 1per
Notes: 
- Economy seems too fast-paced given high unspent resources per turn and undervaluing of _op cards
- Game length of 13 also seems a bit fast given score cap of 10
- Best cards in the game seem to be: 'execute', 'columns', and midrange spike cards that generate resources or cards
- Worst cards in the game seem to be non-damage spells: 'def. stance', 'enrage', 'rally', etc.
- _op cards statistically don't seem preferred, _up cards statistically don't seem unpreferred (weird)
- _up cards play/draw winrate is very low
- _op cards play/draw winrate is above average but not first class
- 'circle bender' moves seem weak, 'bubbles'/'t juggler'/'column' moves seem strong

# economy ruleset
## v1.1-superfast
**Changed:** Economy rules to 2/2/2 + 2per
Notes:
- Lower seat 1 win rate, lower score margin, same decisiveness (good)
- Around same game length (surprisingly good, expected faster)
- More merges, moves, and cards drawn per game (neutral)
- MUCH lower cost pressure
- Around 2.5x more unspent resources (bad)
- _up cards play/draw winrate is slightly higher (bad)
- _op cards seem about the same (still not great)
- Best cards in the game seem to be spike cards (esp. 't dealer') and big anvil cards (expected)
- Worst cards in the game seem to be non-damage spells and basic cards (expected)
- 'circle bender'/1-cost card moves seem weak, 't dealer'/'bubbles'/'t swarm' moves seem strong (expected)

## v1.1-fast
**Changed:** Economy rules to 2/2/2 + 1per
Notes (compared to **v1.1-superfast**):
- Slightly lower seat 1 win rate, lower score margin, less decisiveness (good)
- Slightly longer games, slightly less merges/moves/cards drawn
- Higher cost pressure
- Around 1.5x less unspent resources (good)
- _up cards have slightly more reasonable stats (good)
- _op cards are around the same, but larger disparity in stats (bad)
- Best/worst cards largely unchanged

## v1.1-medium
**Changed:** Economy rules to 2/2/2 + 0per
Notes (compared to **v1.1-fast**):
- Lower seat 1 win rate, lower score margin, less decisiveness (good)
- Longer games, around the same merges/moves/cards
- Higher cost pressure
- Around 1.4x less unspent resources (good)
- _up cards have less reasonable stats (bad)
- _op cards are around the same
- Best cards: 'circle cadet', 'guardian', 't dealer', 'bubbles'
- Worst cards: 'circle bender', 'siphon'
- 'circle bender'/'t flare'/'circle priest' moves seem weak, 't dealer' moves seem strong

## v1.1-slow
**Changed:** Economy rules to 1/1/1 + 0per
Notes (compared to **v1.1-medium**):
- Higher seat 1 win rate, higher score margin (bad)
- Less decisiveness (good)
- Longer games, less cards drawn, less moves, more merges (bad?)
- Super high cost pressure (bad?)
- Around 2.5x less unspent resources (good?)
- _up cards have much more reasonable play winrates, draw winrates still suspiciously ok (good)
- _op cards still not performing well
- Best cards: 't juggler', 'execute', 'guardian', 'bubbles',' circle surfer'
- Worst cards: 'enrage', 'zealot', 'siphon'
- 'bubbles' move winrate is gamebreaking; in general high cost cards getting moves off seems unbalanced
- 'circle cadet'/'zealot' moves seem weak 

## Results
**Final decision:** Using **v1.1-medium**'s ruleset
Reasons
- Removing `incomePerCreatureType` leads to less snowbally games, closer games, more even first/second advantage
- Still a good move usage rate and relatively long games

# card ruleset
## v1.2-baseline (v1.1-medium) 
Economy rules: 2/2/2 + 0per
Card rules: 4 starting, 1/turn, 8 hand limit
Now using `compare.html` for changes

## v1.2-nolimit
**Changed:** Hand limit to 100 (effectively infinite)
Notes (compared to **v1.2-baseline**):
- Results in slightly more resources used (better)
- 'circle surfer', 'worshipper', 't juggler' played slightly less often
- Very little different and I prefer no hand limit generally

## v1.2-hardlimit
**Changed:** Starting hand at 3, hand limit to 5
Notes (compared to **v1.2-baseline**):
- Less cost pressure, more unspent resources (bad?)
- take% is higher across the board with less options to choose and more resources to spend
- Higher cost cards are played more often (take%)
- 't juggler' 'toss' is used more often

## Results
**Final decision:** Using **v1.2-nolimit**'s ruleset
Reasons
- Didn't notice many problems with existing ruleset
- No hand limit is less restrictive and doesn't seem degenerate

# scoring
## v1.3-baseline (v1.2-nolimit)
Economy rules: 2/2/2 + 0per
Card rules: 4 starting, 1/turn, no hand limit
Scoring rules: 10 for win, 1 for unopposed creature

## v1.3-halfscore
**Changed:** scoreToWin to 5
Notes
- Higher seat 1 win rate
- Faster games
- Similar merge/move/draw rates
- Less decisiveness
- Higher cost pressure, less unused resources
- Nearly every card has higher take% (good)
- Cheaper cards seem to have higher draw win rates
- Hard to say what's better

## v1.3-doublescore
**Changed:** scoreToWin to 20
Notes
- Definitely worse than baseline or halfscore
- Too many unspent resources, card draw becomes very strong

## v1.3-creaturedelta
**Changed:** scoreByCreatureDelta to true
Notes
- Games take too long, some never terminate (with good play)
- The worst rule change by far

## Results
**Final decision:** Keeping **v1.3-baseline**'s scoreToWin of 10
Reasons
- Even though games might be slightly too long right now, scoreToWin of 5 introduces too many other problems
  - More seat 1 imbalance
  - More blowouts
  - GamesWithNoSustainedUnopposed becomes >0
  - Higher take rate signals "play every card you can" is a more viable strategy

# balance
Comparing card draw economy per turn graphs to see how card changes should generally be: making cards/spells/moves more/less expensive, more/less card draw, etc.

## v1.4-baseline
- Same ruleset as **v1.3-baseline**
- First simulation run w/o calibration cards, using both ismcts and ismcts-heuristic, and different seed (2 instead of 1)

- Not bad balance (nothing sticks out as game-breakingly OP)
- Some resources accumulate way too much, especially late game
  - Seems heavily dependent on draw order; as opposed to card games where drawing early game cards in the late game is a brick, now there's the added bricks of drawing the wrong resource type (that probably matches your other resources)
- Amount of cards/resources over time seems pretty balanced -> slight ramp of resource amounts/cards over time
- Draw/discard mechanics are very strong
- Lack of cheap single target damage against anvils or spikes
- With lots of excess resources, resource generation cards are weak
- Need to make keyword expiry more consistent between end of turn, damage, and never expirations. Comparing columns and shieldbearer shows that never expiring taunt might be OP
- Need to address how taunt resolves if multiple creatures have taunt at once

### Cards (rated 1-5 on real power level)
Qualitative meaning my opinion on some playtesting and thoughts on the meta
Quantitative meaning power score from current report
Draw WR meaning 'Win (drawn)%' from current report

| Card | Qualitative | Power Score | Draw WR |
| --- | --- | --- | --- |
| Anchor | 3 | 5 | 4 |
| Basic Circle | 4 | 2 | 3 |
| Basic Square | 3 | 2 | 3 |
| Basic T | 1 | 3 | 5 |
| Bubbles | 5 | 5 | 5 |
| Champion T | 2 | 4 | 3 |
| Circle Bender* | 4 | 1 | 1 |
| Circle Cadet | 2 | 4 | 4 |
| Circle Captain* | 2 | 1 | 1 |
| Circle Mouse* | 5 | 4 | 3 |
| Circle Planner | 1 | 3 | 4 |
| Circle Priest | 2 | 2 | 3 |
| Circle Surfer | 5 | 5 | 5 |
| Columns | 4 | 1 | 1 |
| Def. Stance | 1 | 1 | 1 |
| Enrage | 3 | 3 | 5 |
| Execute | 5 | 5 | 2 |
| Gravewarden | 2 | 2 | 2 |
| Guardian | 5 | 4 | 3 |
| Monk | 4 | 4 | 4 |
| Patch Up | 1 | 5 | 1 |
| Rally | 1 | 2 | 3 |
| Relic | 3 | 2 | 2 |
| Shieldbearer | 4 | 5 | 5 |
| Siphon | 3 | 3 | 1 |
| Suffocate | 5 | 5 | 4 |
| T Battery | 2 | 4 | 5 |
| T Body | 3 | 2 | 3 |
| T Dealer | 5 | 4 | 5 |
| T Flare | 1 | 1 | 1 |
| T Juggler | 4 | 4 | 4 |
| T Medic | 2 | 1 | 2 |
| T Swarm | 2 | 3 | 4 |
| Wave Crash | 2 | 1 | 1 |
| Worshipper | 4 | 2 | 3 |
| Zealot | 3 | 1 | 2 |

*BUG: I believe these riccochet cards don't lose riccochet after next attack? Makes them stronger than it should be

## v1.4-keywordfix
**Changed:** Three engine fixes, no card or ruleset edits:
1. **Ricochet is now consumed on trigger**, like reflect — it was previously permanent
   Effect: `circle_bender`, `circle_mouse`, `circle_captain` should be weaker.
2. **Cannot use moves a second time after merging.** 
   Effect: merging should be slightly weaker
3. **Merging now sums both halves' play cost.** 
   Effect: `suffocate` should be weaker

Notes
- (1) Riccochet moves are being activated more often; `circle_captain` actually got better, while `circle_bender` and `circle_mouse` got insignificantly slightly worse
- (2) Less moves are being used, more resources are being spent, potentially leading to shorter/more balanced games
- (3) `suffocate` is notably weaker, while some large anvil cards are slightly better

Nerf targets: execute, bubbles, circle surfer, shieldbearer, suffocate, t dealer
Buff targets: t flare, def stance, rally, wave crash, circle captain, circle bender

## v1.4-change1
**Changed:**

Nerfs
- Execute: cost 2->3
  - if chosen enemy is damaged: deal 6->4 damage to chosen enemy; else: deal 3->2 damage to chosen enemy
- Bubbles: 
  - Burst: deal 6 damage to all enemies; self-damage 3->6
  - Fizz: cost 1->2
- Circle Surfer:
  - Wipeout: discard 2; deal 3 damage to all enemies -> discard 2; deal 6 damage to opposing enemy
- Shieldbearer:
  - Brace For Impact: next time self takes damage: draw 3->2
  - Shield Bash: deal 2 damage to opposing; grant self taunt -> deal 2 damage to opposing; grant self taunt until next turn
- Suffocate: cost 4->5
- T Dealer: health 5->4
  - Deal Out: draw up to 5->4 cards
- T Juggler:
  - Catch and Throw: draw 1; deal 3->2 damage to opposing
  
Buffs
- T Flare:
  - Meltdown: gain spike (source's health); destroy self -> gain spike (source's health); destroy self; draw 2 cards
- Def. Stance: +2->+3 max health to all friendlies
- Rally: cost 3->2
- Wave Crash: deal 1 damage to all enemies -> deal 1 damage to all enemies; heals 1 to all friendlies
- Circle Captain: 
  - Wardance: grants self ricochet (left) -> grants self ricochet (left); grant left friendly reflect
- Circle Bender: health 2->3
  - Anticipate: next time self ricochets: gain 3 wheel -> next time self ricochets: deal 2 damage to all enemies
  - Deflect: grant self ricochet (right); +1 max health to self
- Gravewarden:
  - Reap: draw 1->2 per creature destroyed this turn
- Circle Priest:
  - Focus Strike: deal 1->2 damage to opposing

Notes
- Some games are non-terminating
  - Affected global changes/metrics
  - Might need a future fatigue mechanic

## v1.4-change1fix
**Changed:**
- Fatigue mechanic - 1 point against at start of turn if deck empty guarantees no ties
- Zealot:
  - Martyrdom: next time self takes damage: gain 2->3 anvil
- Circle Bender:
  - Anticipate (cost 1): next time self ricochets: deal 2 damage to all enemies -> Exhale (cost 1): deal 2 damage to opposing; heal self for 2
  - Deflect (cost 1): grant self ricochet (right); +1 max health to self -> Inhale (cost 1): +2 attack to self (persistent) 
- Monk:
  - Recover: only if self is damaged: +4->3 max health to self

Direct balance successes:
- Execute: nerf worked, now balanced
- Bubbles: nerf worked, still good
- Circle Surfer: nerf worked, now balanced
- Shieldbearer: nerf worked, now balanced
- T Dealer: nerf worked, now balanced 
- Circle Captain: buff worked, now balanced
- Circle Bender: rework worked, now balanced
- Gravewarden: buff worked, now strong
- Circle Priest: buff worked, now balanced

Direct balance change fails:
- T Flare: nerf worked, still bad; massive move imbalance
- T Juggler: nerf did not work, still good; spike economy still seems too strong
- Def. Stance: buff did not work, still bad
- Rally: buff did not work, still bad; perhaps no change needed with nerfed spike economy
- Wave Crash: buff did not work, still bad
- Zealot: rework did not work, now very weak
- Monk: rework worked, now weak

Indirect balance change issues:
- Basic Square: super weak
- Circle Planner: too weak
- T Body: too strong

Overall notes to address:
- 58.3% win rate is still quite high
- 21.8 turn length after fatigue is still very high, 37 cards drawn per game is high
- Spike economy is too strong

## v1.4-change2
**Changed:**
Nerfs:
- T Juggler:
  - Toss: discard 1; gain 3->2 spike
- T Body:
  - Overclock: self-damage 1->2: gain 3 spike
  - Piston: if opposing is damaged: deal 8 damage to opposing; else: deal 4 damage to opposing -> deal 2 damage to opposing; deal 2 damage to opposing
- T Flare: health 3->4
  - Meltdown: cost 1->2

Buffs:
- Def. Stance: cost 2->1; +3->2 max health to all friendlies
- Basic Square: health 3->2
  - Fortify: +1 max health to self -> +1 max health to self; heal self for 1

Results:
- T Juggler: nerf worked, now balanced
- T Body: nerf worked, now balanced
- T Flare: nerf failed, now super strong and moves still imbalanced
- Def. Stance: buff worked, now balanced
- Basic Square: buff did not work, still weak
- Wheel resources too little, spike resources still too much

## v1.4-change3, v1.4-change3rerun
v1.4-change3: seed=2
v1.4-change3rerun: seed=3

**Changed:**
Nerfs:
- T Flare:
  - Meltdown: gain spike (source's health); destroy self; draw 2->1
- Champion T: health 6->5
- Enrage: cost 1->2

Buffs:
- Basic Square: health 2->3
- Basic T:
  - Finish: if opposing is damaged: deal 3->4 damage to opposing
- Circle Bender:
  - Exhale: deal 2->1 damage to opposing; heal self for 2
  - Inhale: +2->3 attack to self (persistent)
- Circle Captain: health 4->5

Results:
- Everything seems pretty balanced, changes were all generally better
- Future balance changes will address game length / seat 1 advantage
- First run had anvils as weakest, second had spikes as weakest and anvils as strongest... (hopefully all resource types are about equal)

## Overall Results
**22 of 36 cards changed** across four card passes (change1 → change1fix → change2 → change3),
plus the three engine fixes in keywordfix and the fatigue rule in change1fix. Card changes
compared to **v1.4-baseline**:

Nerfs
- (z +1.12 -> +1.03) **Bubbles**:
  - Fizz: cost 1->2
  - Burst: deal 6 damage to all enemies; self-damage 3->6
- (z +1.25 -> +0.58) **Suffocate**: cost 4->5
- (z +0.37 -> +0.15) **Monk**:
  - Recover: only if self is damaged: +4->3 max health to self
- (z +0.72 -> +0.27) **Shieldbearer**:
  - Brace For Impact: next time self takes damage: draw 3->2
  - Shield Bash: grant self taunt -> grant self taunt until next turn
- (z +0.33 -> +0.73) **Champion T**: health 6->5
- (z +0.21 -> -0.58) **Enrage**: cost 1->2
- (z +1.03 -> +0.37) **Execute**: cost 2->3
  - if chosen enemy is damaged: deal 6->4 damage; else: deal 3->2 damage
- (z -0.15 -> -0.09) **T Body**:
  - Piston: if opposing is damaged: deal 8 else 4 -> deal 2 damage; deal 2 
- (z +0.32 -> -0.52) **T Dealer**: health 5->4
  - Deal Out: draw up to 5->4 cardsdamage
  - Overclock: self-damage 1->2; gain 3 spike
- (z +0.46 -> +0.07) **T Juggler**:
  - Toss: discard 1; gain 3->2 spike
  - Catch and Throw: draw 1; deal 3->2 damage to opposing

Buffs
- (z -1.03 -> +0.44) **Circle Captain**: health 4->5
  - Wardance: grant self ricochet (left) -> grant self ricochet (left); grant left friendly reflect
- (z -0.26 -> -0.70) **Circle Priest**:
  - Focus Strike: deal 1->2 damage to opposing
- (z -1.18 -> -1.46) **Wave Crash**:
  - deal 1 damage to all enemies -> deal 1 damage to all enemies; heal all friendlies for 1
- (z -0.26 -> -0.95) **Basic Square**:
  - Fortify: +1 max health to self -> +1 max health to self; heal self for 1
- (z -1.48 -> -0.66) **Def. Stance**: cost 2->1; +2 max health to all friendlies (unchanged net)
- (z -0.39 -> +0.01) **Gravewarden**:
  - Reap: draw 1->2 per creature destroyed this turn
- (z -0.57 -> -0.54) **Zealot**:
  - Martyrdom: next time self takes damage: gain 2->3 anvil
- (z +0.22 -> -0.11) **Basic T**:
  - Finish: if opposing is damaged: deal 3->4 damage to opposing
- (z -0.45 -> -0.35) **Rally**: cost 3->2
- (z -1.51 -> -0.45) **T Flare**: health 3->4
  - Meltdown: cost 1->2; gain spike (source's health); destroy self; **+draw 1**

Reworks
- (z -1.73 -> +0.02) **Circle Bender**: health 2->3
  - Anticipate (next time self ricochets: gain 3 wheel) -> **Exhale**: deal 1 damage to opposing;
    heal self for 2
  - Deflect (grant self ricochet (right)) -> **Inhale**: +3 attack to self (persistent)
- (z +0.69 -> +0.42) **Circle Surfer**:
  - Wipeout: discard 2; deal 3 damage to all enemies -> discard 2; deal 6 damage to opposing