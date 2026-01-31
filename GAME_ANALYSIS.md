# Bang! Online — Game Database & Improvement Analysis

## 1. Data Architecture Overview

The game uses **no persistent database**. All state is held in-memory via a singleton
`GameState` class registered in the ASP.NET Core DI container. This means:

- Game state is lost on server restart.
- Only one game room exists at a time.
- No player statistics, match history, or progression are tracked.
- ~~No reconnection support~~ — **Reconnection is now supported** via
  `localStorage`. The `playerId` is saved on join and automatically restored
  on page reload through the `/api/reconnect` endpoint.

### In-Memory Data Structures

| Structure | Type | Purpose |
|-----------|------|---------|
| `_players` | `Dictionary<string, PlayerState>` | All players keyed by GUID |
| `_turnOrder` | `List<string>` | Ordered player IDs for turn rotation |
| `_drawPile` | `Stack<Card>` | Remaining cards to draw |
| `_discardPile` | `List<Card>` | Played/discarded cards |
| `_eventLog` | `List<string>` | Last 20 game events (scrollable) |
| `_chatLog` | `List<string>` | Last 30 chat messages (separate from events) |

Each `PlayerState` now has a `List<Card> InPlay` for equipped blue/weapon cards,
in addition to the `Hand` list.

### Event Log & Chat

Game events and chat messages are stored in **separate lists**. The event log
keeps the last 20 entries and the chat log keeps the last 30. Both are rendered
as scrollable lists in the frontend, with the most recent event highlighted.

---

## 2. Card Database Analysis

### Deck Composition (80 cards total)

Every card has a **suit** (Spades, Hearts, Diamonds, Clubs) and a **value**
(2–A), assigned randomly when the deck is built. These are used for the
"draw!" check mechanic (Barrel, Dynamite, Jail).

| Card | Count | % of Deck | Category | Notes |
|------|-------|-----------|----------|-------|
| Bang! | 22 | 27.5% | Brown | Primary attack card |
| Missed! | 12 | 15.0% | Brown | Primary defense card |
| Beer | 6 | 7.5% | Brown | Disabled when ≤2 players remain |
| Stagecoach | 4 | 5.0% | Brown | Draw 2 cards |
| Cat Balou | 4 | 5.0% | Brown | Can target hand or equipment |
| Panic! | 4 | 5.0% | Brown | Range 1; can target hand or equipment |
| Duel | 3 | 3.8% | Brown | Alternate discarding Bang! cards |
| General Store | 3 | 3.8% | Brown | Reveal N cards, each player picks one |
| Gatling | 2 | 2.5% | Brown | Hits all other players |
| Indians! | 2 | 2.5% | Brown | Each player discards Bang! or takes 1 damage |
| Saloon | 2 | 2.5% | Brown | All living players heal 1 HP |
| Wells Fargo | 2 | 2.5% | Brown | Draw 3 cards |
| Schofield | 3 | 3.8% | Weapon | Range 2 |
| Volcanic | 2 | 2.5% | Weapon | Range 1, unlimited Bang! |
| Remington | 1 | 1.3% | Weapon | Range 3 |
| Rev. Carabine | 1 | 1.3% | Weapon | Range 4 |
| Winchester | 1 | 1.3% | Weapon | Range 5 |
| Barrel | 2 | 2.5% | Blue | "Draw!" — Hearts = dodge |
| Mustang | 2 | 2.5% | Blue | Distance +1 to others |
| Scope | 1 | 1.3% | Blue | Distance -1 to others |
| Jail | 1 | 1.3% | Blue | "Draw!" at turn start — Hearts = escape, else skip turn |
| Dynamite | 1 | 1.3% | Blue | "Draw!" at turn start — Spades 2–9 = explode (3 dmg), else pass |

### Card Suit/Value System ("Draw!" Mechanic)

The "draw!" mechanic flips the top card of the draw pile, checks its suit and
value, then discards it. This is used for:

- **Barrel**: Hearts = shot dodged.
- **Dynamite**: Spades 2–9 = explode for 3 damage. Otherwise passes clockwise.
- **Jail**: Hearts = escape and play normally. Otherwise turn is skipped.
- **Lucky Duke**: Draws 2 cards for any "draw!" check and the game
  auto-selects the favorable result.

---

## 3. Character Database Analysis

### Character Abilities (all 14 unique)

| Character | HP | Ability | Notes |
|-----------|---:|--------|-------|
| Lucky Duke | 4 | "Draw!" flips 2 cards, best result chosen | Passive; affects Barrel, Dynamite, Jail |
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

## 4. Game Logic

### Turn Start Sequence

Each turn follows this order:

1. **Dynamite check** — If the player has Dynamite in play, draw a check card.
   Spades 2–9 explodes for 3 damage (Dynamite discarded). Otherwise Dynamite
   passes to the next alive player clockwise. If the explosion kills the
   player, the turn moves to the next player (who also gets Dynamite/Jail
   checks).
2. **Jail check** — If the player has Jail in play, draw a check card. Hearts
   means escape (Jail discarded, play normally). Otherwise the turn is
   skipped entirely and advances to the next player.
3. **Draw phase** — Character-specific card drawing (Jesse Jones, Kit Carlson,
   Pedro Ramirez, or default draw 2).
4. **Play phase** — Play cards, use abilities, etc.
5. **Discard phase** — If hand exceeds HP, discard down to HP limit.

### Remaining Notes

1. **Turn order is alphabetical** (`Program.cs`), not based on seating
   position. This is fine for a simplified version but worth noting.

### Win Condition Edge Cases

- If the Sheriff dies and no Bandits are alive but a Renegade is, the current
  code checks `alivePlayers.Count == 1 && renegadeAlive` — this is correct.
- However, if the Sheriff dies and both Bandits and Renegade are dead
  simultaneously (e.g., from Gatling), the message says "Bandits win after
  the Sheriff falls" even though no bandits are alive. This is technically
  correct by the official rules but the message is confusing.

---

## 5. Frontend / UX

### Implemented

- **Event history log** — scrollable list of the last 20 game events, with
  the most recent event highlighted.
- **Chat separated from game events** — dedicated chat message list above
  the chat input, independent of the event log.
- **Polling at 1-second intervals** — responsive enough for casual play.
- **Reconnection via localStorage** — `playerId` saved on join, auto-restored
  on page reload through `/api/reconnect`.
- **"New Game" button** — appears when the game is over, calls `/api/newgame`.
- **Card suit/value display** — every card in hand, equipment, and overlays
  shows its suit symbol and value (e.g. `7♠`, `K♥`). Hearts/Diamonds are
  red, Spades/Clubs are gray.

### Remaining Issues

1. **No role reveal animation or notification** when a player dies.
2. **No visual feedback** on card play success — the card just disappears
   from the hand.
3. Consider **WebSocket/SSE** for instant updates instead of polling.

---

## 6. Prioritized Improvement Suggestions

### Completed

| # | Improvement | Status |
|---|-------------|--------|
| 1 | **Event history log** (last 20 events, scrollable) | Done |
| 2 | **Separate chat from game events** | Done |
| 3 | **Reconnection support** (localStorage + /api/reconnect) | Done |
| 4 | **"New Game" button** after game over | Done |
| 5 | **Jail card** with "draw!" check at turn start | Done |
| 6 | **Dynamite card** with "draw!" check, passing, and explosion | Done |
| 7 | **Card suit/value system** and "draw!" mechanic for Barrel/Dynamite/Jail | Done |
| 8 | **Polling reduced** from 4s to 1s | Done |

### Remaining

| # | Improvement | Impact |
|---|-------------|--------|
| 1 | **Switch to WebSockets or SSE** for real-time updates | Eliminates polling delay entirely |
| 2 | **Add persistence** (SQLite or JSON file) for game history | Enables statistics and match replays |
| 3 | **Support multiple game rooms** | Allows concurrent games |
| 4 | **Role reveal animation** on player death | Visual polish |
| 5 | **Card play feedback** (animation or flash) | Visual polish |

---

## 7. Summary

The game has a **complete implementation of the original Bang! card set**
including all 80 cards with suit/value, all 14 characters with unique abilities,
and the full "draw!" check mechanic for Barrel, Dynamite, and Jail.

**Core mechanics:** The **distance/range system** uses circular seating with
weapon range, Mustang, Scope, and character modifiers. **Equipment (blue)
cards** stay in play — Barrel uses "draw!" (Hearts = dodge), weapons set range,
Mustang and Scope modify distance, **Jail** skips a turn unless the player
draws Hearts, and **Dynamite** passes clockwise and explodes on Spades 2–9
for 3 damage. **Cat Balou and Panic!** can target equipment or hand cards.
**Dead player cards** are properly handled (discarded or taken by Vulture Sam,
Sheriff-kills-Deputy penalty). **Lucky Duke** draws 2 cards for any "draw!"
check with the best result auto-selected. **Roles are revealed on death**,
**Beer is disabled in 1v1**, and **self-targeting is blocked**.

**Quality of life:** Event history (scrollable, last 20), separate chat log
(last 30), reconnection via localStorage, "New Game" button, 1-second polling,
and suit/value displayed on all cards.

**Remaining improvements:** WebSocket/SSE for real-time updates, persistence,
multiple game rooms, and visual polish (death animations, card play feedback).
