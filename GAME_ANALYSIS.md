# Bang! Online — Game Database & Improvement Analysis

## 1. Data Architecture Overview

The game uses **no persistent database**. All state is held in-memory via a singleton
`GameState` class registered in the ASP.NET Core DI container. This means:

- Game state is lost on server restart.
- Only one game room exists at a time.
- No player statistics, match history, or progression are tracked.
- No reconnection support — if a player's browser tab closes, they lose their
  `playerId` and cannot rejoin.

### In-Memory Data Structures

| Structure | Type | Purpose |
|-----------|------|---------|
| `_players` | `Dictionary<string, PlayerState>` | All players keyed by GUID |
| `_turnOrder` | `List<string>` | Ordered player IDs for turn rotation |
| `_drawPile` | `Stack<Card>` | Remaining cards to draw |
| `_discardPile` | `List<Card>` | Played/discarded cards |
| `LastEvent` | `string?` | Only the most recent event (no history) |

### Key Limitation: No Event Log

Only a single `LastEvent` string is stored. Chat messages also overwrite this field,
meaning a chat message can hide a game action. There is no scrollable event history.

---

## 2. Card Database Analysis

### Deck Composition (60 cards total)

| Card | Count | % of Deck | Notes |
|------|-------|-----------|-------|
| Bang! | 25 | 41.7% | Dominant card — matches original game |
| Beer | 6 | 10.0% | Reasonable |
| Stagecoach | 4 | 6.7% | OK |
| Cat Balou | 4 | 6.7% | OK |
| Panic! | 4 | 6.7% | OK |
| Duel | 3 | 5.0% | OK |
| General Store | 3 | 5.0% | Implementation is wrong (see below) |
| Gatling | 2 | 3.3% | OK |
| Indians! | 2 | 3.3% | Implementation is wrong (see below) |
| Saloon | 2 | 3.3% | OK |
| Wells Fargo | 2 | 3.3% | OK |
| **Missed!** | **0** | **0%** | **Critical omission — core defense card** |

### Missing Card Types (from the original Bang! game)

**Defensive cards (critical gap):**
- **Missed!** — The primary defense card. Without it, there is no counterplay to
  Bang!, making the game purely about who draws more attack cards.
- **Barrel** — Equipment that gives a chance to dodge shots.

**Blue (equipment) cards entirely missing:**
- **Mustang** — Increases the distance others need to reach you.
- **Scope** — Decreases the distance you need to reach others.
- **Jail** — Skip a player's turn unless they "draw" their way out.
- **Dynamite** — Passes between players and may explode.
- **Volcanic** — Weapon that allows unlimited Bang! per turn.
- **Remington / Rev. Carabine / Winchester** — Weapons with different ranges.

**Other missing action cards:**
- **Dodge** (from expansions)

### Card Logic Bugs

1. **General Store** (`Program.cs:616`): Currently just draws 2 cards for the
   player. In the actual game, N cards are revealed (where N = number of alive
   players) and each player picks one in turn order. This is a significant
   mechanic that involves all players.

2. **Indians!** (`Program.cs:560`): Currently deals flat 1 damage to everyone
   with no counterplay. In the actual game, each other player must discard a
   Bang! card or take 1 damage. This removes a key decision point.

3. **Duel** (`Program.cs:575`): Currently deals flat 1 damage to the target.
   In the actual game, the challenger and target alternate discarding Bang!
   cards — the first one who cannot (or chooses not to) takes 1 damage. This
   is one of the most strategically interesting cards in the game.

4. **Beer** (`Program.cs:514`): Should be disabled when only 2 players remain
   (official rule). Currently always usable.

5. **Cat Balou** can waste a play if the target has 0 cards — the card is still
   consumed from the player's hand and discarded, but the message says "has no
   cards to discard" (`Program.cs:548`). The card should either not be playable
   on empty-handed targets or at minimum the card should be returned.

---

## 3. Character Database Analysis

### Character Distribution by Ability

| Ability | Characters | Count | Notes |
|---------|-----------|-------|-------|
| ExtraDraw (3 cards/turn) | Lucky Duke, Jesse Jones, Kit Carlson, Vulture Sam | 4 | Overrepresented; these are the strongest characters |
| DrawOnHit | El Gringo, Bart Cassidy, Sid Ketchum | 3 | OK |
| SteadyHands (+1 HP) | Rose Doolan, Paul Regret, Pedro Ramirez | 3 | Very generic — just +1 HP |
| DrawWhenEmpty | Suzy Lafayette, Calamity Janet | 2 | Weak ability |
| DoubleBangDamage | Slab the Killer | 1 | Very strong — only 1 character has it |
| ExtraBang (2 per turn) | Willy the Kid | 1 | Unique |

### Issues

1. **Only 6 unique abilities across 14 characters.** Many characters are
   mechanically identical — Lucky Duke, Jesse Jones, Kit Carlson, and Vulture Sam
   all do the exact same thing (draw 3 cards per turn). In the original game,
   each of these characters has a distinct ability.

2. **Character draw uses replacement** (`CharacterLibrary.Draw` at
   `Program.cs:1010`): Two players can receive the same character since it's
   `random.Next(Characters.Count)` with no tracking of already-assigned
   characters. Characters should be drawn without replacement.

3. **Original abilities not implemented:**
   - **Lucky Duke**: Should draw 2 "luck" cards and choose the best for barrel
     checks, not just draw 3.
   - **Jesse Jones**: Should draw first card from another player's hand.
   - **Kit Carlson**: Should look at top 3 cards and choose 2 to keep.
   - **Vulture Sam**: Should take all cards from eliminated players.
   - **Calamity Janet**: Should be able to use Bang! as Missed! and vice versa.
   - **Sid Ketchum**: Should be able to discard 2 cards to regain 1 HP.
   - **El Gringo**: Should draw from the attacker's hand when hit (not just
     draw from deck).
   - **Paul Regret**: Should be seen at distance +1 by others (not +1 HP).
   - **Rose Doolan**: Should see others at distance -1 (not +1 HP).
   - **Pedro Ramirez**: Should draw first card from the discard pile.

4. **ExtraDraw is overpowered.** Drawing 3 cards per turn (50% more than normal)
   with no downside is the best ability in the game. Four characters share it,
   so there's a 4/14 ≈ 28.6% chance of getting the best ability.

---

## 4. Game Logic Issues

### Missing Core Mechanics

1. **No distance/range system.** In the original game, players sit in a circle
   and can only target players within their weapon's range. Distance is the
   core strategic mechanic — it determines who you can shoot and who can shoot
   you. Currently every player can target every other player.

2. **No Missed! / defense system.** Without the ability to block attacks, the
   game is purely offensive with no counterplay.

3. **No hand size limit.** In the original game, players must discard down to
   their current HP at the end of their turn. This creates interesting decisions
   about which cards to keep. Currently players can hoard unlimited cards.

4. **No equipment/blue card slots.** The original game has "blue border" cards
   that stay in play in front of you (weapons, barrel, mustang, scope, jail,
   dynamite). This entire subsystem is missing.

5. **Dead player card handling.** When a player dies, their hand simply
   disappears. In the original game:
   - Vulture Sam takes all their cards.
   - Otherwise cards are discarded.
   - If the Sheriff kills a Deputy, the Sheriff discards their entire hand as
     a penalty.

6. **No role reveal on death.** Dead players' roles are never revealed to other
   players (only the Sheriff's role is shown). The role should be revealed when
   a player is eliminated — this is critical for the social deduction aspect.

7. **Players can target themselves** with targeted cards like Bang!, Cat Balou,
   Panic!, and Duel. The game should prevent self-targeting.

8. **Turn order is alphabetical** (`Program.cs:197`), not based on seating
   position. This is fine for a simplified version but worth noting.

### Win Condition Edge Cases

- If the Sheriff dies and no Bandits are alive but a Renegade is, the current
  code checks `alivePlayers.Count == 1 && renegadeAlive` — this is correct.
- However, if the Sheriff dies and both Bandits and Renegade are dead
  simultaneously (e.g., from Gatling), the message says "Bandits win after
  the Sheriff falls" even though no bandits are alive. This is technically
  correct by the official rules but the message is confusing.

---

## 5. Frontend / UX Issues

1. **Polling at 4-second intervals** (`app.js:509`): Feels sluggish in a
   real-time game. Consider reducing to 1-2 seconds or switching to
   WebSocket/Server-Sent Events for instant updates.

2. **Only last event visible** — no scrollable event log. Players miss what
   happened if they weren't watching at the exact moment.

3. **Chat overwrites game events** — sending a chat message replaces the
   `LastEvent` string on the server, so other players see the chat instead
   of the last game action.

4. **No role reveal animation or notification** when a player dies.

5. **No visual feedback** on card play success — the card just disappears
   from the hand.

6. **No indication of how many cards** opponents hold (important strategic
   info in the original game).

7. **No "new game" button** after game over — players must refresh and rejoin.

---

## 6. Prioritized Improvement Suggestions

### Priority 1 — Game-Breaking Fixes

| # | Improvement | Impact |
|---|-------------|--------|
| 1 | **Add Missed! cards** (15-20 in deck) and implement defense response flow | Transforms gameplay from pure offense to strategic play |
| 2 | **Fix General Store** to let each player pick a card | Restores a core multiplayer interaction card |
| 3 | **Fix Indians!** so players can discard a Bang! to avoid damage | Adds counterplay and hand management decisions |
| 4 | **Fix Duel** to alternate Bang! discards between players | Restores the most strategic card interaction |
| 5 | **Prevent duplicate character assignment** | Ensures each player has a unique identity |
| 6 | **Add hand size limit** (discard to HP at end of turn) | Prevents card hoarding and adds end-of-turn decisions |

### Priority 2 — Important Gameplay Additions

| # | Improvement | Impact |
|---|-------------|--------|
| 7 | **Add distance/range system** with circular seating | Core strategic layer of the original game |
| 8 | **Add equipment (blue) cards**: Barrel, Mustang, Scope, weapons | Adds persistent board state and loadout customization |
| 9 | **Give each character a unique ability** instead of sharing 6 abilities across 14 | Makes character selection meaningful |
| 10 | **Reveal roles on death** to all players | Essential for social deduction gameplay |
| 11 | **Prevent self-targeting** on attack/steal cards | Bug fix |
| 12 | **Disable Beer in 1v1** (2-player remaining) | Official rule |

### Priority 3 — Quality of Life

| # | Improvement | Impact |
|---|-------------|--------|
| 13 | **Add event history log** (store last N events, not just one) | Players can catch up on what happened |
| 14 | **Separate chat from game events** | Prevents chat from hiding game actions |
| 15 | **Switch to WebSockets or SSE** for real-time updates | Eliminates polling delay |
| 16 | **Show opponent hand sizes** | Important strategic information |
| 17 | **Add persistence** (SQLite or JSON file) for game history | Enables statistics and match replays |
| 18 | **Add reconnection support** (store playerId in localStorage) | Prevents losing session on page refresh |
| 19 | **Support multiple game rooms** | Allows concurrent games |
| 20 | **Add "New Game" button** after game over | Avoids manual server restart |

---

## 7. Summary

The game has a solid foundation with clean code architecture and a working
multiplayer flow. The most impactful improvements center around adding the
**Missed! card** (and the defense response system it requires), fixing three
incorrectly-implemented cards (General Store, Indians!, Duel), and making each
character mechanically unique. These changes would transform the game from a
simplified "draw and attack" experience into something much closer to the
strategic depth of the original Bang! card game.
