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