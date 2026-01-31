# План миграции с polling на WebSocket (SignalR)

## Текущая архитектура

- Фронтенд опрашивает `GET /api/state` каждую секунду (`setInterval(refreshState, 1000)`)
- Лобби опрашивает `GET /api/rooms` каждые 3 секунды
- POST-эндпоинты (play, respond, end, chat и др.) возвращают новый `GameStateView` вызывающему игроку
- Остальные игроки узнают об изменениях только при следующем poll-цикле (задержка 0-1 сек)
- Типичный размер ответа: 3.5-7 КБ на запрос

**Проблемы:**
- Лишний трафик: ~18 запросов/мин на игру даже без изменений (уже частично решено JSON-сравнением)
- Задержка до 1 сек для остальных игроков
- Нагрузка растёт линейно с количеством комнат

---

## Целевая архитектура

Использовать **SignalR** (встроен в ASP.NET Core) — обёртка над WebSocket с автоматическим fallback на Long Polling.

### Почему SignalR, а не чистый WebSocket

| | SignalR | Чистый WebSocket |
|---|---|---|
| Reconnect | Автоматический | Ручной |
| Fallback | Long Polling / SSE | Нет |
| Группы (комнаты) | Встроенные | Ручные |
| Сериализация | Автоматическая JSON | Ручная |
| Клиент | npm-пакет `@microsoft/signalr` | Нативный `WebSocket` |

---

## Этапы миграции

### Этап 1. Бэкенд — SignalR Hub

**Файл:** `Program.cs`

1. Добавить SignalR в сервисы:
```csharp
builder.Services.AddSignalR();
```

2. Создать `GameHub`:
```csharp
public class GameHub : Hub
{
    // Клиент присоединяется к комнате
    public async Task JoinRoom(string roomCode)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
    }

    // Клиент покидает комнату
    public async Task LeaveRoom(string roomCode)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);
    }
}
```

3. Подключить маршрут:
```csharp
app.MapHub<GameHub>("/gamehub");
```

4. Хранить маппинг `playerId → connectionId` для адресной отправки:
```csharp
// В RoomManager или отдельном сервисе
ConcurrentDictionary<string, string> _playerConnections = new();
```

### Этап 2. Серверные пуши вместо polling

**Файл:** `Program.cs`

Внедрить `IHubContext<GameHub>` в эндпоинты. После каждого мутирующего действия — отправлять обновлённое состояние всем в комнате:

```csharp
// Пример для /api/play
app.MapPost("/api/play", async (PlayRequest request, RoomManager rooms, IHubContext<GameHub> hub) =>
{
    var game = rooms.GetRoomByPlayer(request.PlayerId);
    // ... валидация ...
    var result = game.PlayCard(request.PlayerId, request.CardIndex, request.TargetId);

    // Отправить персональное состояние каждому игроку в комнате
    await BroadcastState(hub, game, rooms);

    return Results.Ok(new ApiResponse(null, result.Message));
});
```

Функция массовой рассылки:
```csharp
async Task BroadcastState(IHubContext<GameHub> hub, GameState game, RoomManager rooms)
{
    // Каждому игроку — его персональный вид
    foreach (var playerId in game.GetAllPlayerIds())
    {
        var connId = rooms.GetConnectionId(playerId);
        if (connId == null) continue;
        var state = game.IsSpectator(playerId)
            ? game.ToSpectatorView(playerId)
            : game.ToView(playerId);
        await hub.Clients.Client(connId).SendAsync("StateUpdated", state);
    }

    // Обновить лобби для всех, кто на странице лобби
    await hub.Clients.Group("lobby").SendAsync("RoomsUpdated", rooms.ListRooms());
}
```

**Эндпоинты, требующие broadcast:**
- `/api/play` — игра карты
- `/api/respond` — ответ на действие
- `/api/end` — завершение хода
- `/api/start` — старт игры
- `/api/newgame` — новая игра
- `/api/chat` — сообщение в чат
- `/api/ability` — способность персонажа
- `/api/join` — вход в комнату
- `/api/leave` — выход из комнаты
- `/api/rename` — смена имени

### Этап 3. Фронтенд — SignalR клиент

**Файл:** `wwwroot/app.js`

1. Подключить клиент SignalR (CDN или локальный файл):
```html
<!-- index.html -->
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
```

2. Создать подключение:
```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/gamehub")
    .withAutomaticReconnect()
    .build();
```

3. Подписаться на события:
```javascript
connection.on("StateUpdated", (state) => {
    updateState(state);
});

connection.on("RoomsUpdated", (rooms) => {
    renderRoomList(rooms);
});
```

4. При входе в комнату:
```javascript
const joinRoom = async (code) => {
    // ... существующая логика API ...
    await connection.invoke("JoinRoom", code);
};
```

5. При выходе:
```javascript
const leaveRoom = async () => {
    await connection.invoke("LeaveRoom", roomCode);
    // ... существующая логика ...
};
```

### Этап 4. Удалить polling

**Файл:** `wwwroot/app.js`

1. Удалить `setInterval(refreshState, 1000)` (строка 1188)
2. Удалить функцию `refreshState` (строки 1075-1085)
3. Удалить `setInterval(refreshRoomList, 3000)` (строка 903)
4. Удалить переменную `lobbyInterval` и связанную логику
5. Удалить `lastStateJson` — больше не нужна (обновления приходят только при изменениях)

### Этап 5. Reconnect

**Файл:** `wwwroot/app.js`

SignalR имеет встроенный `withAutomaticReconnect()`. При переподключении:

```javascript
connection.onreconnected(async () => {
    if (roomCode) {
        await connection.invoke("JoinRoom", roomCode);
        // Запросить текущее состояние
        const response = await fetch(`/api/reconnect?playerId=${playerId}`);
        const payload = await response.json();
        if (response.ok) updateState(payload.data);
    }
});
```

---

## Порядок миграции (можно делать инкрементально)

```
1. [Бэкенд]  Добавить SignalR, GameHub, маппинг соединений
2. [Фронтенд] Подключить signalr.js, установить соединение
3. [Бэкенд]  Добавить broadcast в POST-эндпоинты (параллельно с polling)
4. [Фронтенд] Подписаться на StateUpdated / RoomsUpdated
5. [Тест]    Убедиться, что обновления приходят через WebSocket
6. [Фронтенд] Удалить setInterval polling
7. [Бэкенд]  GET /api/state можно оставить для reconnect, но убрать из цикла
```

На этапах 3-4 polling и WebSocket работают параллельно — безопасный откат.

---

## Оценка изменений по файлам

| Файл | Что меняется |
|------|-------------|
| `Program.cs` | +SignalR сервис, +GameHub класс, +маппинг соединений, +broadcast в каждый POST |
| `wwwroot/index.html` | +подключение signalr.min.js |
| `wwwroot/app.js` | +SignalR клиент, +обработчики событий, -polling логика |

---

## Что НЕ меняется

- `GameState`, `PlayerState`, `GameStateView` — без изменений
- `ToView()` / `ToSpectatorView()` — без изменений
- Бизнес-логика игры — без изменений
- CSS / HTML структура — без изменений
- REST API эндпоинты остаются (для действий), только теряют обязанность возвращать state
