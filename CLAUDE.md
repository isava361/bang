# Bang! Online

Multiplayer card game "Bang!" implemented as a single-page web application. Russian-language interface.

## Tech Stack

- **Backend:** ASP.NET Core 8.0 (minimal API)
- **Frontend:** TypeScript, HTML5, CSS3 (Vite build, no frameworks)
- **Real-time:** SignalR WebSocket (hub at `/gamehub`)
- **State:** All in-memory, no database. `RoomManager` singleton holds all game rooms.

## Project Structure

```
Program.cs              — Composition root: config, middleware, REST endpoints, helpers
Models/
  Enums.cs              — CardType, CardCategory, CardSuit, Role, PendingActionType
  Card.cs               — Card record
  CardDefinition.cs     — CardDefinition record
  CharacterDefinition.cs — CharacterDefinition record
  PendingAction.cs      — PendingAction class (pending action state)
  PlayerState.cs        — PlayerState class (per-player mutable state)
  Dtos.cs               — Request/response/view records (PlayRequest, GameStateView, etc.)
Game/
  GameState.cs          — Core: fields, properties, player management, PlayCard, AddChat, UseAbility
  GameState.CardResolution.cs — Resolve* methods for all 11 card types + TryGetTarget
  GameState.PendingResponses.cs — Respond() handler for 9 pending action types
  GameState.TurnFlow.cs — EndTurn, AdvanceTurn, BeginTurn, HandleDrawPhase
  GameState.Combat.cs   — ApplyDamage, HandlePlayerDeath, CheckBarrel, CheckSuzyLafayette
  GameState.ViewBuilders.cs — ToView, ToSpectatorView, role visibility helpers
  GameState.Deck.cs     — BuildDeck, ShuffleDeck, DrawCards, CreateCards, formatting
  GameState.Distance.cs — GetDistance, GetWeaponRange, GetBangLimit, alive player lists
  GameState.Setup.cs    — StartGame, ResetGame, AssignRoles, CheckForGameOver
  CardLibrary.cs        — Static card definitions (22 cards)
  CharacterLibrary.cs   — Static character definitions (14 characters)
Infrastructure/
  RoomManager.cs        — Room/session/connection management singleton
  RoomCleanupService.cs — Background cleanup of idle rooms
  GameHub.cs            — SignalR hub for real-time communication
index.html              — Vite entry point HTML
src/
  main.ts               — Entry point: wiring, event listeners, init
  types.ts              — TypeScript interfaces (CardView, GameStateView, etc.)
  constants.ts          — Static reference data (cards, characters, roles)
  utils.ts              — Utility functions (escapeHtml, formatting)
  dom.ts                — Typed DOM element references
  state.ts              — Global mutable state
  api.ts                — HTTP API calls and polling
  signalr.ts            — SignalR connection management
  ui.ts                 — UI rendering, overlays, updateState
  game.ts               — Game flow (join, create, play, etc.)
  styles.css            — All styles
public/
  assets/
    backgrounds/        — Game background images
    cards/              — Card artwork
    characters/         — Character portraits
wwwroot/                — Vite build output (served by ASP.NET)
```

## Build & Run

```
npm install
npm run build          # TypeScript check + Vite build → wwwroot/
dotnet run             # Serves built files on http://0.0.0.0:5000
```

### Development

```
dotnet run             # Terminal 1: backend on http://127.0.0.1:5000
npm run dev            # Terminal 2: Vite dev server on http://localhost:5173 (proxies API+WS)
```

## Architecture

- **Room-based multiplayer:** Players create/join rooms via 4-character codes (e.g. `A3K9`)
- **REST + WebSocket hybrid:** POST endpoints for actions (`/api/play`, `/api/respond`, `/api/end`, etc.), SignalR pushes state updates to all players
- **PublicId system:** Real player GUIDs are never sent to clients. Each player/spectator gets an 8-char hex `PublicId` used in all client-facing state. Server resolves `publicId -> realId` via `FindByPublicId()`.
- **State broadcast:** After every mutating action, server sends personalized `GameStateView` to each player via SignalR `StateUpdated` event. POST endpoints return only a message, not state.

## Key Patterns

- `GameState` is a `partial class` split across 9 files in `Game/` by responsibility (card resolution, turn flow, combat, views, deck, distance, setup)
- All GameState files share the same private fields — partial classes provide navigability without architecture overhead
- `ToView(playerId)` builds a player-specific view of game state (hides other players' hands)
- `ToSpectatorView(spectatorId)` builds a spectator view (sees all cards)
- Frontend uses `innerHTML` with `escapeHtml()` wrapper for all user-controlled data (XSS prevention)
- Player names validated server-side with regex: `^[\p{L}\p{N}\s\-_]{1,16}$`
- No namespaces — all types are in the global namespace (project uses `ImplicitUsings`)

## Security

- Rate limiting: 10 req/sec general, 1 req/5sec for room creation
- CORS: restricted to localhost/127.0.0.1
- Request body limit: 64 KB
- Chat message limit: 200 characters
- Max rooms: 50
- See `SECURITY_AUDIT.md` for full audit

## Conventions

- Backend language: C# with minimal API style (no controllers)
- Frontend: TypeScript with Vite, ES modules, `@microsoft/signalr` npm package
- UI text and error messages are in Russian
- Game terms follow the original Bang! card game terminology
