# Bang! Online — Game Database & Improvement Analysis

## TODO (tomorrow)

### Task 1 — Security hardening + quick backend fixes
- [ ] Add HTTPS enforcement (`UseHttpsRedirection` or reverse proxy)
- [ ] CDN SignalR script: add SRI integrity hash or bundle locally
- [ ] Abandoned room cleanup (`IHostedService` background task, 10 min timeout)
- [ ] Simplify verbose error messages (hide distances/ranges from client)
- [ ] Configure Kestrel request body size limit (64 KB — already done, verify)

### Task 2 — Core UI (high impact, visual foundation)
- [ ] **CSS custom properties** — extract hardcoded colors into `:root` variables
- [ ] **Card play animations & visual feedback** — slide/fade on play, flash on target
- [ ] **Connection status indicator** — color-coded green/yellow/red with text labels
- [ ] **Modal Escape key & focus trapping** — `keydown` listener, trap Tab in overlays
- [ ] **Turn indicator enhancement** — pulsing border or banner for active player

### Task 3 — Polish + gameplay feel
- [ ] **Role reveal & death animation** — flip/flash effect on elimination
- [ ] **Mobile experience overhaul** — collapsible sidebar, better distance labels
- [ ] **Card tooltip positioning** — viewport-aware flip logic
- [ ] **Hand card layout redesign** — horizontal scrollable fan instead of grid
- [ ] **Event log & chat improvements** — timestamps, sound notifications

### Task 4 — Nice-to-haves + long-term
- [ ] **Add persistence** — SQLite or JSON file for game history/stats
- [ ] **Lobby polish** — copy room code button, player count in room list
- [ ] **Accessibility audit** — ARIA labels, colorblind-friendly disabled states
- [ ] **Micro-animations** — hover/press, card draw slide-in, HP flash
- [ ] **Disabled button visual distinction** — icon/pattern instead of opacity only

---

## 1. Data Architecture Overview

The game uses **no persistent database**. All state is held in-memory via a
`RoomManager` singleton registered in the ASP.NET Core DI container. The
`RoomManager` owns multiple `GameState` instances, one per room. This means:

- Game state is lost on server restart.
- **Multiple concurrent game rooms** are supported, each with a unique 4-character
  room code (e.g. `A3K9`).
- No player statistics, match history, or progression are tracked.
- **Reconnection is supported** via `localStorage`. The `playerId` and
  `bangRoomCode` are saved on join and automatically restored on page reload
  through the `/api/reconnect` endpoint.

### Room Management

| Structure | Type | Purpose |
|-----------|------|---------|
| `RoomManager._rooms` | `Dictionary<string, GameState>` | All rooms keyed by 4-char code |
| `RoomManager._playerRoomMap` | `Dictionary<string, string>` | Player ID to room code mapping |

Room codes use unambiguous characters (no `0/O/1/I`). Empty rooms are
automatically cleaned up when the last player/spectator leaves.

### Per-Room In-Memory Data Structures

| Structure | Type | Purpose |
|-----------|------|---------|
| `_players` | `Dictionary<string, PlayerState>` | All players keyed by GUID |
| `_spectators` | `HashSet<string>` | Spectator player IDs |
| `_spectatorNames` | `Dictionary<string, string>` | Spectator ID to display name |
| `_turnOrder` | `List<string>` | Ordered player IDs for turn rotation |
| `_drawPile` | `Stack<Card>` | Remaining cards to draw |
| `_discardPile` | `List<Card>` | Played/discarded cards |
| `_eventLog` | `List<string>` | Last 20 game events (scrollable) |
| `_chatLog` | `List<string>` | Last 30 chat messages (separate from events) |

Each `PlayerState` has a `List<Card> InPlay` for equipped blue/weapon cards,
in addition to the `Hand` list.

### Event Log & Chat

Game events and chat messages are stored in **separate lists**. The event log
keeps the last 20 entries and the chat log keeps the last 30. Both are rendered
as scrollable lists in the frontend, with the most recent event highlighted.
Spectators can chat (messages are prefixed with `[Spectator]`).

---

## 2. Card Database Analysis

### Deck Composition (80 cards total)

Every card has a **suit** (Spades, Hearts, Diamonds, Clubs) and a **value**
(2--A), assigned randomly when the deck is built. These are used for the
"draw!" check mechanic (Barrel, Dynamite, Jail).

| Card | Count | % of Deck | Category | Notes |
|------|-------|-----------|----------|-------|
| Bang! | 22 | 27.5% | Brown | Primary attack card |
| Missed! | 12 | 15.0% | Brown | Primary defense card |
| Beer | 6 | 7.5% | Brown | Disabled when <=2 players remain |
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
| Barrel | 2 | 2.5% | Blue | "Draw!" -- Hearts = dodge |
| Mustang | 2 | 2.5% | Blue | Distance +1 to others |
| Scope | 1 | 1.3% | Blue | Distance -1 to others |
| Jail | 1 | 1.3% | Blue | "Draw!" at turn start -- Hearts = escape, else skip turn |
| Dynamite | 1 | 1.3% | Blue | "Draw!" at turn start -- Spades 2-9 = explode (3 dmg), else pass |

### Card Suit/Value System ("Draw!" Mechanic)

The "draw!" mechanic flips the top card of the draw pile, checks its suit and
value, then discards it. This is used for:

- **Barrel**: Hearts = shot dodged.
- **Dynamite**: Spades 2--9 = explode for 3 damage. Otherwise passes clockwise.
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
| Calamity Janet | 4 | Bang! <-> Missed! interchangeable | Works in play and defense |
| Kit Carlson | 4 | Look at top 3, keep 2, put 1 back | Draw phase pending action |
| Willy the Kid | 4 | Unlimited Bang! per turn | Passive |
| Sid Ketchum | 4 | Discard 2 cards to heal 1 HP | Active ability via /api/ability |
| Vulture Sam | 4 | Take all cards from eliminated players | Passive on death |
| Pedro Ramirez | 4 | First draw from discard pile | Automatic in draw phase |

---

## 4. Game Logic

### Turn Start Sequence

Each turn follows this order:

1. **Dynamite check** -- If the player has Dynamite in play, draw a check card.
   Spades 2--9 explodes for 3 damage (Dynamite discarded). Otherwise Dynamite
   passes to the next alive player clockwise. If the explosion kills the
   player, the turn moves to the next player (who also gets Dynamite/Jail
   checks).
2. **Jail check** -- If the player has Jail in play, draw a check card. Hearts
   means escape (Jail discarded, play normally). Otherwise the turn is
   skipped entirely and advances to the next player.
3. **Draw phase** -- Character-specific card drawing (Jesse Jones, Kit Carlson,
   Pedro Ramirez, or default draw 2).
4. **Play phase** -- Play cards, use abilities, etc.
5. **Discard phase** -- If hand exceeds HP, discard down to HP limit.

### Room & Spectator Logic

- **Joining a room** while the game is in progress (or room is full) adds the
  player as a **spectator**. Spectators see the game state (player cards hidden),
  can chat (messages prefixed with `[Spectator]`), but cannot play cards, start
  the game, end turns, or use abilities.
- **Leaving mid-game** eliminates the player: their cards are discarded, pending
  actions involving them are cleaned up, and the turn advances if it was their
  turn.
- **New Game** promotes spectators to players (up to 6). Overflow stays
  spectating.
- **Empty rooms** are automatically removed when the last person leaves.

### Remaining Notes

1. **Turn order is alphabetical** (`Program.cs`), not based on seating
   position. This is fine for a simplified version but worth noting.

### Win Condition Edge Cases

- If the Sheriff dies and no Bandits are alive but a Renegade is, the current
  code checks `alivePlayers.Count == 1 && renegadeAlive` -- this is correct.
- However, if the Sheriff dies and both Bandits and Renegade are dead
  simultaneously (e.g., from Gatling), the message says "Bandits win after
  the Sheriff falls" even though no bandits are alive. This is technically
  correct by the official rules but the message is confusing.

---

## 5. Frontend / UX

### Implemented

- **Lobby panel** -- enter a name, then create or join rooms by code. Room
  list auto-refreshes every 3 seconds showing player/spectator counts and
  game status.
- **Multiple game rooms** -- each room has a unique 4-character code displayed
  as a badge in the game panel header.
- **Leave button** -- returns to the lobby; mid-game departure eliminates the
  player and cleans up pending actions.
- **Spectator mode** -- blue banner ("You are spectating this game"), action
  buttons disabled, empty hand. Spectators can still chat and view the event
  log.
- **Event history log** -- scrollable list of the last 20 game events, with
  the most recent event highlighted.
- **Chat separated from game events** -- dedicated chat message list above
  the chat input, independent of the event log.
- **Real-time updates via SignalR** -- no polling; state pushed to all clients
  on every action via WebSocket.
- **Reconnection via localStorage** -- `playerId` and `bangRoomCode` saved on
  join, auto-restored on page reload through `/api/reconnect`. Failed reconnect
  falls back to the lobby.
- **"New Game" button** -- appears when the game is over, calls `/api/newgame`.
  Promotes spectators to players before starting.
- **Card suit/value display** -- every card in hand, equipment, and overlays
  shows its suit symbol and value (e.g. `7`spade, `K`heart). Hearts/Diamonds
  are red, Spades/Clubs are gray.
- **Card art fix** -- images use `object-fit: contain` with `max-height: 260px`
  so the full artwork is visible without cropping.

### Remaining Issues

1. **No role reveal animation or notification** when a player dies.
2. **No visual feedback** on card play success -- the card just disappears
   from the hand.
3. ~~Consider **WebSocket/SSE** for instant updates instead of polling.~~ **Done** -- SignalR push, no polling.
4. **No Escape key or focus trapping** on modal overlays.
5. **Connection status badge** doesn't clearly indicate state.
6. **Mobile sidebar** has no collapse toggle — pushes content down.
7. **Card tooltips** clip at viewport edges.
8. **Turn indicator** (`.active` glow) is too subtle.
9. **No ARIA labels** on interactive elements.
10. **Hand card grid** wraps awkwardly on mid-size screens.

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
| 9 | **Multiple game rooms** with RoomManager and room codes | Done |
| 10 | **Spectator mode** for players joining mid-game or full rooms | Done |
| 11 | **Leave/rejoin** with mid-game elimination and lobby fallback | Done |
| 12 | **Card art fix** (full artwork visible, no cropping) | Done |

### Remaining — UI Improvements

#### Priority 1 — Critical (High impact, affects core experience)

| # | Improvement | Area | Details |
|---|-------------|------|---------|
| ~~1~~ | ~~**Switch to WebSockets/SSE** for real-time updates~~ | ~~Networking~~ | **Done.** POST endpoints no longer return state; all updates pushed via SignalR `StateUpdated` / `RoomsUpdated`. No polling intervals remain. |
| 2 | **Card play animations & visual feedback** | UX | Cards disappear instantly from hand with no indication of success. Add a slide/fade-out animation on play, and a brief highlight/flash on the target player receiving an action. |
| 3 | **CSS custom properties (variables)** | Code quality | Colors are hardcoded everywhere (`#e0482e`, `#3a3a4a`, `#0f0f12`, etc.). Extract into `:root` variables for consistency and to enable future theming (e.g. light mode). |
| 4 | **Modal/overlay keyboard & focus handling** | Accessibility | No Escape key handler to close overlays. No focus trapping inside modals — Tab can navigate behind the overlay. Add `keydown` listener for Escape and trap focus within active overlay. |
| 5 | **Connection status indicator** | UX | The header badge doesn't clearly distinguish Connected/Connecting/Disconnected. Add color-coded states (green/yellow/red) with text labels. |

#### Priority 2 — Important (Noticeable polish, improves usability)

| # | Improvement | Area | Details |
|---|-------------|------|---------|
| 6 | **Role reveal & death animation** | Visual polish | Eliminated players just fade to 50% opacity (`.out` class). Add a role-card flip reveal animation, red flash, or slide-out effect to make eliminations feel impactful. |
| 7 | **Mobile experience overhaul** | Responsive | Sidebar library has no collapse/toggle on small screens — it pushes content down. Add a hamburger menu or collapsible panel. Player distance labels are hard to read on narrow viewports. The poker table is hidden entirely on mobile with no alternative visual. |
| 8 | **Turn indicator enhancement** | UX | The current `.active` red glow on the player card is subtle. Add a more prominent visual — pulsing border, top banner, or animated arrow pointing to the active player. |
| 9 | **Card tooltip positioning** | UX | Tooltips render inside the card element and clip at viewport edges. Add position-aware logic to flip tooltip direction when near bottom/right edge. |
| 10 | **Add persistence** (SQLite or JSON file) | Backend | Enables game history, statistics, and match replays. Currently all state is lost on server restart. |

#### Priority 3 — Nice to Have (Polish & refinement)

| # | Improvement | Area | Details |
|---|-------------|------|---------|
| 11 | **Hand card layout redesign** | Visual | The grid `repeat(auto-fit, minmax(260px, 1fr))` causes awkward wrapping on mid-size screens. A horizontal scrollable row with slight card overlap/fan effect would feel more natural and save vertical space. |
| 12 | **Event log & chat improvements** | UX | Add timestamps on events/messages. Visual distinction between system events and player chat. Optional sound notifications for key events (Bang played, your turn, etc.). |
| 13 | **Accessibility audit** | Accessibility | Missing ARIA labels on interactive elements (cards, buttons, overlays). Disabled buttons only use opacity — add a visual pattern or icon for colorblind users. Card suit colors (red vs gray) may have insufficient contrast. |
| 14 | **Lobby polish** | UX | Show player count / max players in room list. Add a "copy room code" button for sharing. Animate room list updates instead of full re-render. |
| 15 | **Micro-animations** | Visual polish | Button hover/press states (subtle scale or shadow shift). Card draw animation (slide in from deck). HP change animation (number bounce or flash). |
| 16 | **Disabled button visual distinction** | Accessibility | Disabled buttons only differ by 50% opacity. Add a crossed-out icon, desaturated color, or pattern fill so the state is clear regardless of color perception. |

---

## 7. API Endpoints

### Room Management

| Method | Endpoint | Body | Description |
|--------|----------|------|-------------|
| POST | `/api/room/create` | -- | Creates a new room, returns `{ roomCode }` |
| GET | `/api/rooms` | -- | Lists all rooms with player/spectator counts and status |
| POST | `/api/join` | `{ name, roomCode }` | Joins a room (as player or spectator) |
| POST | `/api/leave` | `{ playerId }` | Leaves the current room |

### Gameplay

| Method | Endpoint | Body | Description |
|--------|----------|------|-------------|
| POST | `/api/start` | `{ playerId }` | Starts the game (blocked for spectators) |
| POST | `/api/play` | `{ playerId, cardIndex, targetId? }` | Plays a card from hand |
| POST | `/api/respond` | `{ playerId, responseType, cardIndex?, targetId? }` | Responds to a pending action |
| POST | `/api/end` | `{ playerId }` | Ends the current turn |
| POST | `/api/chat` | `{ playerId, text }` | Sends a chat message (spectators allowed) |
| POST | `/api/ability` | `{ playerId, cardIndices }` | Uses character ability (Sid Ketchum) |
| POST | `/api/newgame` | `{ playerId }` | Resets for a new game (promotes spectators) |
| GET | `/api/reconnect` | `?playerId=` | Reconnects and returns current game state |
| GET | `/api/state` | `?playerId=` | Polls current game state |

All gameplay endpoints look up the room via `RoomManager.GetRoomByPlayer()` and
block spectators from game actions (except chat and newgame).

---

## 8. Summary

The game has a **complete implementation of the original Bang! card set**
including all 80 cards with suit/value, all 14 characters with unique abilities,
and the full "draw!" check mechanic for Barrel, Dynamite, and Jail.

**Core mechanics:** The **distance/range system** uses circular seating with
weapon range, Mustang, Scope, and character modifiers. **Equipment (blue)
cards** stay in play -- Barrel uses "draw!" (Hearts = dodge), weapons set range,
Mustang and Scope modify distance, **Jail** skips a turn unless the player
draws Hearts, and **Dynamite** passes clockwise and explodes on Spades 2--9
for 3 damage. **Cat Balou and Panic!** can target equipment or hand cards.
**Dead player cards** are properly handled (discarded or taken by Vulture Sam,
Sheriff-kills-Deputy penalty). **Lucky Duke** draws 2 cards for any "draw!"
check with the best result auto-selected. **Roles are revealed on death**,
**Beer is disabled in 1v1**, and **self-targeting is blocked**.

**Multiplayer infrastructure:** **Multiple game rooms** with 4-character codes,
managed by a `RoomManager` singleton. Players joining a room mid-game or when
the room is full become **spectators** who can watch and chat but not play.
**Leave mid-game** eliminates the player and cleans up pending actions.
**Spectators are promoted to players** when a new game starts (up to 6).
Empty rooms are cleaned up automatically.

**Quality of life:** Event history (scrollable, last 20), separate chat log
(last 30), reconnection via localStorage (with room code), lobby with room
browser, "New Game" button, 1-second polling, suit/value displayed on all
cards, and full card art visible without cropping.

**Remaining improvements (16 items, 3 priority tiers):** P1 — WebSocket/SSE
real-time updates, card play animations, CSS variables, modal keyboard/focus
handling, connection status indicator. P2 — death/role-reveal animation, mobile
overhaul, turn indicator, tooltip positioning, persistence. P3 — hand card
layout redesign, event log/chat timestamps, accessibility audit, lobby polish,
micro-animations, disabled button distinction.
