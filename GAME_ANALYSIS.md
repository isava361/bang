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

Each `PlayerState` now has a `List<Card> InPlay` for equipped blue/weapon cards,
in addition to the `Hand` list.

### Key Limitation: No Event Log

Only a single `LastEvent` string is stored. Chat messages also overwrite this field,
meaning a chat message can hide a game action. There is no scrollable event history.

---

## 2. Card Database Analysis

### Deck Composition (82 cards total)

| Card | Count | % of Deck | Category | Notes |
|------|-------|-----------|----------|-------|
| Bang! | 22 | 26.8% | Brown | Primary attack card |
| Missed! | 12 | 14.6% | Brown | Primary defense card |
| Beer | 6 | 7.3% | Brown | Disabled when ≤2 players remain |
| Stagecoach | 4 | 4.9% | Brown | OK |
| Cat Balou | 4 | 4.9% | Brown | Can target hand or equipment |
| Panic! | 4 | 4.9% | Brown | Range 1; can target hand or equipment |
| Duel | 3 | 3.7% | Brown | OK |
| General Store | 3 | 3.7% | Brown | OK |
| Gatling | 2 | 2.4% | Brown | OK |
| Indians! | 2 | 2.4% | Brown | OK |
| Saloon | 2 | 2.4% | Brown | OK |
| Wells Fargo | 2 | 2.4% | Brown | OK |
| Schofield | 3 | 3.7% | Weapon | Range 2 |
| Volcanic | 2 | 2.4% | Weapon | Range 1, unlimited Bang! |
| Remington | 1 | 1.2% | Weapon | Range 3 |
| Rev. Carabine | 1 | 1.2% | Weapon | Range 4 |
| Winchester | 1 | 1.2% | Weapon | Range 5 |
| Barrel | 2 | 2.4% | Blue | 25%/50% auto-dodge |
| Mustang | 2 | 2.4% | Blue | Distance +1 to others |
| Scope | 1 | 1.2% | Blue | Distance -1 to others |

### Missing Card Types (from the original Bang! game)

**Blue (equipment) cards not yet implemented:**
- **Jail** — Skip a player's turn unless they "draw" their way out.
- **Dynamite** — Passes between players and may explode.

**Other missing action cards:**
- **Dodge** (from expansions)

---

## 3. Character Database Analysis

### Character Abilities (all 14 unique)

| Character | HP | Ability | Notes |
|-----------|---:|--------|-------|
| Lucky Duke | 4 | Barrel checks succeed 50% instead of 25% | Passive |
| Slab the Killer | 4 | Bang! deals 2 damage | Passive |
| El Gringo | 3 | Draw from attacker's hand when hit | Passive, per damage |
| Suzy Lafayette | 4 | Draw 1 when hand empties | Triggers after any card consumption |
| Rose Doolan | 4 | Built-in Scope (distance -1) | Passive |
| Jesse Jones | 4 | First draw from a chosen player's hand | Draw phase pending action |
| Bart Cassidy | 4 | Draw 1 from deck when hit | Passive, per damage |
| Paul Regret | 3 | Built-in Mustang (distance +1) | Passive |
| Calamity Janet | 4 | Bang! ↔ Missed! interchangeable | Works in play and defense |
| Kit Carlson | 4 | Look at top 3, keep 2, put 1 back | Draw phase pending action |
| Willy the Kid | 4 | Unlimited Bang! per turn | Passive |
| Sid Ketchum | 4 | Discard 2 cards to heal 1 HP | Active ability via /api/ability |
| Vulture Sam | 4 | Take all cards from eliminated players | Passive on death |
| Pedro Ramirez | 4 | First draw from discard pile | Automatic in draw phase |

---

## 4. Game Logic Issues

### Remaining Issues

1. **Turn order is alphabetical** (`Program.cs`), not based on seating
   position. This is fine for a simplified version but worth noting.

2. **Jail and Dynamite** are not yet implemented. These are the two remaining
   blue cards from the original game.

### Win Condition Edge Cases

- If the Sheriff dies and no Bandits are alive but a Renegade is, the current
  code checks `alivePlayers.Count == 1 && renegadeAlive` — this is correct.
- However, if the Sheriff dies and both Bandits and Renegade are dead
  simultaneously (e.g., from Gatling), the message says "Bandits win after
  the Sheriff falls" even though no bandits are alive. This is technically
  correct by the official rules but the message is confusing.

---

## 5. Frontend / UX Issues

1. **Polling at 4-second intervals** (`app.js`): Feels sluggish in a
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

6. **No "new game" button** after game over — players must refresh and rejoin.

---

## 6. Prioritized Improvement Suggestions

### Priority 1 — Quality of Life

| # | Improvement | Impact |
|---|-------------|--------|
| 1 | **Add event history log** (store last N events, not just one) | Players can catch up on what happened |
| 2 | **Separate chat from game events** | Prevents chat from hiding game actions |
| 3 | **Switch to WebSockets or SSE** for real-time updates | Eliminates polling delay |
| 4 | **Add persistence** (SQLite or JSON file) for game history | Enables statistics and match replays |
| 5 | **Add reconnection support** (store playerId in localStorage) | Prevents losing session on page refresh |
| 6 | **Support multiple game rooms** | Allows concurrent games |
| 7 | **Add "New Game" button** after game over | Avoids manual server restart |
| 8 | **Add Jail and Dynamite** blue cards | Completes the original card set |

---

## 7. Summary

The game now has a comprehensive implementation of the core Bang! mechanics.
All 14 characters have **unique, faithful abilities** matching the original
game. The **distance/range system** uses circular seating with weapon range,
Mustang, Scope, and character modifiers. **Equipment (blue) cards** stay in
play in front of players — Barrel provides auto-dodge, weapons set range,
Mustang and Scope modify distance. **Cat Balou and Panic!** can target
equipment or hand cards. **Dead player cards** are properly handled (discarded
or taken by Vulture Sam, Sheriff-kills-Deputy penalty). **Roles are revealed
on death**, **Beer is disabled in 1v1**, and **self-targeting is blocked**.
The remaining improvements are quality-of-life: event history, chat separation,
real-time updates, persistence, reconnection, multiple rooms, and new game flow.
