## Shapes
### Objective
Shapes is a two-player, turn-based card game. In Shapes, you battle with cards in a fictional universe full of spherical **wheel** creatures, blunt **anvil** creatures, and sharp **spike** creatures. You win by maintaining board control which whittles your opponent's health to zero.

### Economy
There are three different resource types in the game: **wheels**, **anvils**, and **spikes**. These resource types work like independent sources of mana or energy. Every turn, you gain **2** of each resource type. Resources are fully **saved between turns**, allowing you to accumulate large quantities of them.

You spend resources on playing cards and activating moves that creatures have.

### Cards
Every turn you draw one card. Cards come in two kinds: **creatures** and **spells**.

#### Creatures
Creatures have a **cost**, **HP**, and two **moves**. You pay the upfront cost once to place the creature on the board. If a creature's health reaches 0, it gets destroyed and removed from the board.

Each move has a cost and effect. Every turn, you can activate any of a creatures' moves by paying the move cost. Each move can be used at most once per turn. Moves can be used on any turn, including the turn you play a creature. 

#### Spells
Spells have a **cost** and **effect**. You pay the upfront cost and the effect immediately triggers, consuming the card.

### Gameplay
#### The Board
The board consists of **3 slots** per side. You can place a creature into any empty slot on your side. Once a creature is placed down onto a slot, it cannot relocate to a different slot. Creatures typically cannot interact with each other unless they are **opposing** each other. Creature moves that interact with an enemy, such as dealing damage, only target opposing creatures.

#### Type effectiveness
Creatures and spells have a type that matches their cost: **wheel**, **anvil**, or **spike**. Types can either be **super effective** or **neutral** against each other.

- **Spikes** are super effective against **wheels** (spikes pop wheels).
- **Wheels** are super effective against **anvils** (wheels roll over anvils).
- **Anvils** are super effective against **spikes** (anvils blunt spikes).

When a spell or move is super effective against a creature, it deals double damage. So a super effective **spike** move deals double damage against a **wheel** creature, but a neutral **wheel** move does regular damage against a **spike** creature.

Creatures can sometimes be dual-type. Moves and spells can only ever have one type.

- **Spikes** are also super effective against **wheel**/**spike**.
- **Wheels** are also super effective against **anvil**/**wheel**.
- **Anvils** are also super effective against **spike/anvil**.

Mastering types is critical to winning the game. Oppose enemy creatures with super effective types and prioritize super effective moves whenever possible.

#### Scoring
Each player has a hero that starts with 7 health. At the start of your turn, you deal 1 damage to your opponent's hero for each **unopposed** creature you have. So, if you start your turn with a full board against an opponent's empty board, you deal 3 damage to their hero. To win, you must bring your opponent's hero down to zero health.

#### Merging
On your turn, you can **merge** two adjacent friendly creatures. Merging is a free action that combines two creatures' resource types, health, moves, and statuses. To trigger a merge, drag one creature onto an adjacent creature. A merged creature cannot merge again.

Merging is often a good idea:
- It can make a creature harder to kill
- It can uncover super effective moves
- Creatures' moves can have synergy with each other
- It is the only way a creature can relocate from the slot they were first placed onto

Merging is sometimes a bad idea: 
- Combining two creatures onto the same slot leaves one more slot empty, which, if opposed by an enemy, deals one more damage to your hero on your opponent's turn. 
- While two unopposed creatures *each* deal 1 damage to an opponent's hero per turn, combining them only does 1 *total* damage per turn. 
- It can uncover super effective moves against your creature

#### Fatigue
Each turn you draw one card. If your deck runs out of cards, your hero takes one damage instead. This mechanic ends stalemates and guarantees a winner, and typically doesn't happen in a regular game. Scoring happens before fatigue triggers.