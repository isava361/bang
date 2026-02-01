# Bang! Online — Improvement Plan

## 1. Split `Program.cs`

**Priority: Highest — unlocks everything else.**

The 3000-line single file is the biggest maintainability problem. Extract into:

- `Models/` — `PlayerState`, `Card`, `GameStateView`, request/response records
- `Game/GameState.cs` — game logic
- `Game/RoomManager.cs` — room management
- `Hubs/GameHub.cs` — SignalR hub
- `Program.cs` — just startup + endpoint registration

## 2. Add tests

Zero tests means every change is a gamble. Start with:

- Unit tests for `GameState` (card logic, turn flow, win conditions)
- Integration tests for API endpoints
- Game logic is already mostly pure methods on `GameState` — very testable once extracted

## 3. Add `.gitignore`

No `.gitignore` exists. Need to exclude at minimum:

- `bin/`, `obj/`
- `.vs/`, `*.user`
- `.env`, credentials

## 4. TypeScript migration for `app.js`

1230 lines of untyped JS with implicit state shape contracts from the server. TypeScript catches bugs where the frontend assumes wrong field names. Incremental approach:

1. Start with `// @ts-check` + JSDoc annotations
2. Rename to `.ts` later when ready

## 5. Persistence

Server restart loses everything. Options:

- SQLite for match history, player stats, leaderboard
- JSON file as minimal alternative
- Adds replay value to the game

## 6. Dockerfile + deployment

No deployment story beyond `dotnet run` on bare metal. A Dockerfile would be ~10 lines:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app
FROM base
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "BangOnline.dll"]
```

## 7. Frontend module structure

`app.js` is heading toward the same monolith problem as `Program.cs`. No framework needed — split into ES modules with native `import`:

- `lobby.js` — room list, create/join
- `game.js` — game board rendering
- `chat.js` — chat + event log
- `signalr.js` — connection management

## 8. Input validation centralization

Validation is scattered across individual endpoints. A shared validation helper or middleware would reduce duplication and prevent forgetting checks on new endpoints.

---

## Not worth doing

- Switching to a frontend framework — vanilla JS is fine for this scope
- Adding a full ORM — overkill for a card game
- Microservices — single process is correct here
- i18n — audience is Russian-speaking, no need
