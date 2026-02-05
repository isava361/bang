# Bang! Online — Security Audit

## Summary

Found **19 vulnerabilities** across the application. The most dangerous attack
chain: join any room as a spectator, receive all player GUIDs from state
broadcast, then call any API endpoint impersonating any player.

The application in its current state is suitable only for trusted local networks
among friends, **not for public deployment**.

---

## CRITICAL

### 1. Player ID leakage to all room participants

**Files:** `Program.cs` — `ToView()`, `ToSpectatorView()`

Methods `ToView()` and `ToSpectatorView()` include every player's GUID (`p.Id`)
in the state broadcast sent to ALL clients, including spectators. Since the only
"authentication" is knowing a GUID, any spectator can perform actions on behalf
of any player.

**Fix:** Introduce a separate `publicId` for each player. Never send the real
`playerId` (GUID) to other clients. Use `publicId` in `PlayerView` and for
target selection. Map `publicId -> playerId` on the server when processing
actions.

- [x] Add `PublicId` field to `PlayerState`
- [x] Replace `p.Id` with `p.PublicId` in `ToView()` and `ToSpectatorView()`
- [x] Add server-side `publicId -> playerId` mapping in target resolution
- [x] Update frontend to use `publicId` for targeting

---

### 2. Stored XSS via player names

**Files:** `app.js:493-506`, `Program.cs:56-64`

Player names are inserted into `innerHTML` without HTML escaping:
```javascript
<strong>${player.name}</strong>
```

A name like `<img src=x onerror=alert(document.cookie)>` executes JavaScript in
every other player's browser. The server only validates length and emptiness.

**Fix (choose one or both):**

- [x] Server-side: strip or encode HTML characters (`<`, `>`, `"`, `'`, `&`) in
      player names on join (`/api/join`)
- [x] Client-side: use `textContent` instead of `innerHTML` for player names, or
      build DOM elements with `document.createElement` instead of template literals

---

### 3. No authentication

**Files:** `Program.cs` — all endpoints

The entire identity model is a GUID passed as `playerId` in request bodies. No
sessions, no tokens, no cookies, no middleware.

**Fix:** This is partially addressed by fix #1 (hiding real GUIDs). For stronger
protection:

- [ ] Issue a session token (HttpOnly cookie or signed JWT) on `/api/join`
- [ ] Validate the token on every request instead of trusting raw `playerId`

---

## HIGH

### 4. No rate limiting

**Files:** `Program.cs` — middleware pipeline

No rate limiting on any endpoint. Attackers can spam room creation, chat, joins.

**Fix:**

- [x] Add `builder.Services.AddRateLimiter()` with per-IP policies
- [x] Apply `RequireRateLimiting()` to all POST endpoints
- [x] Example policy: 10 requests/second per IP for game actions, 1
      request/second for room creation

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("general", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("create", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(5);
        opt.PermitLimit = 1;
        opt.QueueLimit = 0;
    });
});
```

---

### 5. Unlimited room creation — memory exhaustion DoS

**Files:** `Program.cs` — `/api/room/create`, `RoomManager.CreateRoom()`,
`GenerateRoomCode()`

`/api/room/create` requires no `playerId` and has no limit. When all 923,521
room codes are exhausted, `GenerateRoomCode()` enters an infinite loop.

**Fix:**

- [x] Add a maximum room count (e.g. 100) in `CreateRoom()`, return error when
      exceeded
- [ ] Require a `playerId` or IP to create a room
- [ ] Add a safety check in `GenerateRoomCode()` to bail out after N attempts

---

### 6. No chat message length limit

**Files:** `Program.cs:689-724` — `AddChat()`

Only `IsNullOrWhiteSpace` is checked. A 30MB message gets stored in `_chatLog`
and broadcast to all clients via SignalR on every state update.

**Fix:**

- [x] Add a length limit (e.g. 200 characters) in `AddChat()`:
      ```csharp
      if (text.Trim().Length > 200)
          return new CommandResult(false, "Сообщение слишком длинное.");
      ```

---

### 7. SignalR Hub without authorization

**Files:** `Program.cs` — `GameHub` class

Any client can call `Register("victim-guid")` to hijack another player's
connection, or `JoinRoom("XXXX")` to listen to any room.

**Fix:**

- [x] In `Register()`: validate that the `connectionId` belongs to the player
      (e.g. via a token passed during connection)
- [ ] In `JoinRoom()`: verify the caller is actually in the room before adding
      to the SignalR group
- [ ] Consider requiring a signed token as a query parameter on the SignalR
      connection

---

### 8. No CORS policy

**Files:** `Program.cs` — middleware pipeline

No `AddCors()` or `UseCors()`. Any website can make API requests to the server.

**Fix:**

- [x] Add CORS with explicit allowed origin:
      ```csharp
      builder.Services.AddCors(options =>
      {
          options.AddDefaultPolicy(policy =>
          {
              policy.WithOrigins("https://yourdomain.com")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
          });
      });
      // ...
      app.UseCors();
      ```

---

## MEDIUM

### 9. Public room listing

**Files:** `Program.cs` — `GET /api/rooms`

Returns all rooms without any authentication. Combined with joining as spectator,
exposes all player IDs (see #1).

**Fix:**

- [ ] Consider requiring a valid `playerId` query parameter to list rooms
- [ ] Or accept as low risk once #1 is fixed (GUIDs no longer leaked)

---

### 10. System.Random for game-critical randomness

**Files:** `Program.cs:313, 2791`

`System.Random` is not cryptographically secure. Deck shuffling, barrel checks,
and room code generation all use it.

**Fix:**

- [ ] Replace with `RandomNumberGenerator` for room code generation
- [ ] For deck shuffle: acceptable if fairness against sophisticated attackers
      is not a concern; otherwise use `RandomNumberGenerator`

---

### 11. Unicode and control characters in player names

**Files:** `Program.cs:56-64`

Names can contain zero-width characters, RTL overrides, emoji sequences, and
newlines. Players can impersonate others visually or break UI layout.

**Fix:**

- [x] Allow only printable characters (letters, digits, spaces, basic
      punctuation) via regex:
      ```csharp
      if (!Regex.IsMatch(name, @"^[\p{L}\p{N}\s\-_]{1,16}$"))
          return Results.BadRequest("Имя содержит недопустимые символы.");
      ```

---

### 12. localStorage player ID exposed to XSS

**Files:** `app.js:936-938`

`bangPlayerId` is stored in `localStorage`. Any XSS (see #2) can read it.

**Fix:**

- [x] Addressed automatically once #2 (XSS) is fixed
- [ ] For defense in depth: use HttpOnly cookies for session tokens instead of
      localStorage (requires #3)

---

### 13. Full state broadcast amplification

**Files:** `Program.cs` — `BroadcastState()`

The entire game state (event log, chat, all players) is serialized and sent to
every player on every action. A large chat message (see #6) gets amplified N
times.

**Fix:**

- [x] Addressed partially by #6 (chat length limit)
- [ ] Consider sending incremental updates (deltas) instead of full state
- [ ] Or at minimum, exclude chat/events from state and send them separately

---

## LOW

### 14. HTTP by default, no HTTPS enforcement

**Files:** `Program.cs:15-18`

Server defaults to `http://0.0.0.0:5000`. All traffic including player IDs
transmitted in plaintext.

**Fix:**

- [ ] Add `app.UseHttpsRedirection()` and configure HTTPS in Kestrel
- [ ] Or rely on a reverse proxy (nginx, Cloudflare) for TLS termination

---

### 15. No request body size limit

**Files:** `Program.cs`

Default Kestrel limit is 30MB per request. JSON deserialization processes the
full payload before validation.

**Fix:**

- [x] Configure Kestrel max request body size:
      ```csharp
      builder.WebHost.ConfigureKestrel(options =>
      {
          options.Limits.MaxRequestBodySize = 1024 * 64; // 64 KB
      });
      ```

---

### 16. Abandoned rooms never cleaned up

**Files:** `Program.cs` — `RoomManager.UnregisterPlayer()`

Rooms are only removed when the last player explicitly calls `/api/leave`. If
players close the browser without leaving, rooms persist forever.

**Fix:**

- [ ] Add a background cleanup task (`IHostedService`) that removes rooms with
      no active SignalR connections after a timeout (e.g. 10 minutes)
- [x] Or add a heartbeat mechanism via SignalR `OnDisconnectedAsync`

---

### 17. CDN script without Subresource Integrity (SRI)

**Files:** `index.html:129`

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
```

No `integrity` attribute. If the CDN is compromised, arbitrary JS executes in
every user's browser.

**Fix:**

- [ ] Add SRI hash:
      ```html
      <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"
              integrity="sha384-..." crossorigin="anonymous"></script>
      ```
- [ ] Or bundle the script locally in `wwwroot/lib/`

---

### 18. Verbose error messages leak game state

**Files:** `Program.cs` — various error responses

Error messages reveal exact distances and weapon ranges:
```
"Имя вне зоны досягаемости (расстояние 3, дальность оружия 1)."
```

**Fix:**

- [ ] Simplify error messages: `"Цель вне зоны досягаемости."`
- [ ] Remove numeric details from client-facing errors

---

## Fix Priority Order

Recommended order to implement fixes:

1. **#2 — XSS** (trivially exploitable, affects all clients)
2. **#1 — Player ID leakage** (breaks entire game model)
3. **#6 — Chat length limit** (one-line fix, prevents DoS)
4. **#5 — Room creation limit** (one-line fix, prevents DoS)
5. **#4 — Rate limiting** (middleware setup, prevents spam)
6. **#7 — SignalR hub validation**
7. **#8 — CORS policy**
8. **#11 — Name character validation**
9. **#17 — SRI hash on CDN script**
10. **#15 — Request body size limit**
11. Remaining items as time permits
