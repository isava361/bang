# Bang! Online

Multiplayer card game "Bang!" implemented as a single-page web application. Russian-language interface.

## Tech Stack

- **Backend:** ASP.NET Core 8.0 (minimal API, single-file `Program.cs`)
- **Frontend:** Vanilla JavaScript, HTML5, CSS3 (no frameworks)
- **Real-time:** SignalR WebSocket (hub at `/gamehub`)
- **State:** All in-memory, no database. `RoomManager` singleton holds all game rooms.

## Project Structure

```
Program.cs          — Entire backend: endpoints, game logic, GameState, PlayerState, RoomManager, GameHub (~3000 lines)
wwwroot/
  index.html        — Single HTML page
  app.js            — All frontend logic (~1230 lines)
  styles.css        — All styles
  assets/
    backgrounds/    — Game background images
    cards/          — Card artwork
    characters/     — Character portraits
```

## Build & Run

```
dotnet build
dotnet run
```

Server starts on `http://0.0.0.0:5000` by default.

## Architecture

- **Room-based multiplayer:** Players create/join rooms via 4-character codes (e.g. `A3K9`)
- **REST + WebSocket hybrid:** POST endpoints for actions (`/api/play`, `/api/respond`, `/api/end`, etc.), SignalR pushes state updates to all players
- **PublicId system:** Real player GUIDs are never sent to clients. Each player/spectator gets an 8-char hex `PublicId` used in all client-facing state. Server resolves `publicId -> realId` via `FindByPublicId()`.
- **State broadcast:** After every mutating action, server sends personalized `GameStateView` to each player via SignalR `StateUpdated` event. POST endpoints return only a message, not state.

## Key Patterns

- All game logic lives in `GameState` class methods inside `Program.cs`
- `ToView(playerId)` builds a player-specific view of game state (hides other players' hands)
- `ToSpectatorView(spectatorId)` builds a spectator view (sees all cards)
- Frontend uses `innerHTML` with `escapeHtml()` wrapper for all user-controlled data (XSS prevention)
- Player names validated server-side with regex: `^[\p{L}\p{N}\s\-_]{1,16}$`

## Security

- Rate limiting: 10 req/sec general, 1 req/5sec for room creation
- CORS: restricted to localhost/127.0.0.1
- Request body limit: 64 KB
- Chat message limit: 200 characters
- Max rooms: 50
- See `SECURITY_AUDIT.md` for full audit

## Conventions

- Backend language: C# with minimal API style (no controllers)
- Frontend: vanilla JS, no build step, no bundler
- UI text and error messages are in Russian
- Game terms follow the original Bang! card game terminology
