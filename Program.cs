using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 65536;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddHostedService<RoomCleanupService>();
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host;
            return host == "localhost" || host == "127.0.0.1";
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("general", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(1),
                PermitLimit = 10,
                QueueLimit = 0
            }));
    options.AddPolicy("create", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(5),
                PermitLimit = 1,
                QueueLimit = 0
            }));
});

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    app.Urls.Add("http://127.0.0.1:5000");
}

app.MapHub<GameHub>("/gamehub");

const string SessionCookieName = "bang_session";

string? GetPlayerIdFromSession(HttpContext http, RoomManager rooms)
{
    if (!http.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId)) return null;
    return rooms.GetPlayerIdBySession(sessionId);
}

void SetSessionCookie(HttpContext http, string sessionId)
{
    http.Response.Cookies.Append(SessionCookieName, sessionId, new CookieOptions
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    });
}

void ClearSessionCookie(HttpContext http)
{
    http.Response.Cookies.Delete(SessionCookieName, new CookieOptions
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    });
}

IResult UnauthorizedResult()
{
    return Results.Json(new ApiResponse(null, "Не авторизовано."), statusCode: StatusCodes.Status401Unauthorized);
}

string? RequirePlayerId(HttpContext http, RoomManager rooms, out IResult? error)
{
    error = null;
    var playerId = GetPlayerIdFromSession(http, rooms);
    if (playerId == null)
    {
        error = UnauthorizedResult();
        return null;
    }

    if (!rooms.HasPlayer(playerId))
    {
        rooms.ClearSessionForPlayer(playerId);
        ClearSessionCookie(http);
        error = UnauthorizedResult();
        return null;
    }

    rooms.TouchRoomByPlayer(playerId);
    return playerId;
}

async Task BroadcastState(IHubContext<GameHub> hub, GameState game, RoomManager rooms)
{
    var playerIds = rooms.GetAllPlayerIdsInRoom(game.RoomCode);
    foreach (var pid in playerIds)
    {
        var connId = rooms.GetConnectionId(pid);
        if (connId == null) continue;
        var state = game.IsSpectator(pid) ? game.ToSpectatorView(pid) : game.ToView(pid);
        if (state != null)
            await hub.Clients.Client(connId).SendAsync("StateUpdated", state);
    }
}

async Task BroadcastLobby(IHubContext<GameHub> hub, RoomManager rooms)
{
    await hub.Clients.Group("lobby").SendAsync("RoomsUpdated", rooms.ListRooms());
}

// --- Room management endpoints ---

app.MapPost("/api/room/create", async (RoomManager rooms, IHubContext<GameHub> hub) =>
{
    var result = rooms.CreateRoom();
    if (result.RoomCode == null)
    {
        return Results.BadRequest(new ApiResponse(null, "Достигнут лимит комнат. Попробуйте позже."));
    }
    await BroadcastLobby(hub, rooms);
    return Results.Ok(new ApiResponse(new CreateRoomResponse(result.RoomCode), "Комната создана."));
}).RequireRateLimiting("create");

app.MapGet("/api/rooms", (RoomManager rooms) =>
{
    return Results.Ok(new ApiResponse(rooms.ListRooms(), "ОК"));
}).RequireRateLimiting("general");

app.MapPost("/api/join", async (JoinRoomRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new ApiResponse(null, "Введите имя."));
    }

    if (request.Name.Trim().Length > 16)
    {
        return Results.BadRequest(new ApiResponse(null, "Имя не должно превышать 16 символов."));
    }

    if (!Regex.IsMatch(request.Name.Trim(), @"^[\p{L}\p{N}\s\-_]{1,16}$"))
    {
        return Results.BadRequest(new ApiResponse(null, "Имя содержит недопустимые символы."));
    }

    if (string.IsNullOrWhiteSpace(request.RoomCode))
    {
        return Results.BadRequest(new ApiResponse(null, "Введите код комнаты."));
    }

    var existingPlayerId = GetPlayerIdFromSession(http, rooms);
    if (existingPlayerId != null)
    {
        if (rooms.HasPlayer(existingPlayerId))
        {
            return Results.BadRequest(new ApiResponse(null, "Вы уже находитесь в комнате."));
        }
        rooms.ClearSessionForPlayer(existingPlayerId);
    }

    var game = rooms.GetRoom(request.RoomCode.Trim());
    if (game is null)
    {
        return Results.BadRequest(new ApiResponse(null, "Комната не найдена."));
    }

    var result = game.TryAddPlayer(request.Name.Trim());
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    rooms.RegisterPlayer(result.PlayerId!, game.RoomCode);
    var isSpec = game.IsSpectator(result.PlayerId!);
    var state = isSpec ? game.ToSpectatorView(result.PlayerId!) : game.ToView(result.PlayerId!);
    var sessionId = rooms.CreateSession(result.PlayerId!);
    SetSessionCookie(http, sessionId);
    await BroadcastState(hub, game, rooms);
    await BroadcastLobby(hub, rooms);
    return Results.Ok(new ApiResponse(state, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/leave", async (RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null)
    {
        return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    }

    var roomCode = game.RoomCode;
    var result = game.RemovePlayer(playerId);
    rooms.UnregisterPlayer(playerId);
    rooms.ClearSessionForPlayer(playerId);
    ClearSessionCookie(http);
    await BroadcastState(hub, game, rooms);
    await BroadcastLobby(hub, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

// --- Gameplay endpoints ---

app.MapPost("/api/start", async (RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (game.IsSpectator(playerId)) return Results.BadRequest(new ApiResponse(null, "Зрители не могут начать игру."));
    if (!game.IsHost(playerId)) return Results.BadRequest(new ApiResponse(null, "Только хост может начать игру."));

    var result = game.StartGame(playerId);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    await BroadcastLobby(hub, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/play", async (PlayRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (game.IsSpectator(playerId)) return Results.BadRequest(new ApiResponse(null, "Зрители не могут играть карты."));

    var result = game.PlayCard(playerId, request.CardIndex, request.TargetId);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/respond", async (RespondRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (game.IsSpectator(playerId)) return Results.BadRequest(new ApiResponse(null, "Зрители не могут отвечать."));

    var result = game.Respond(playerId, request.ResponseType, request.CardIndex, request.TargetId);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/end", async (RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (game.IsSpectator(playerId)) return Results.BadRequest(new ApiResponse(null, "Зрители не могут завершать ход."));

    var result = game.EndTurn(playerId);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/chat", async (ChatRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));

    var result = game.AddChat(playerId, request.Text);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/ability", async (AbilityRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (game.IsSpectator(playerId)) return Results.BadRequest(new ApiResponse(null, "Зрители не могут использовать способности."));

    var result = game.UseAbility(playerId, request.CardIndices);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/newgame", async (RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (!game.IsHost(playerId)) return Results.BadRequest(new ApiResponse(null, "Только хост может начать новую игру."));

    var result = game.ResetGame(playerId);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    await BroadcastLobby(hub, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/rename", async (RenameRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(request.NewName))
        return Results.BadRequest(new ApiResponse(null, "Введите новое имя."));

    if (request.NewName.Trim().Length > 16)
        return Results.BadRequest(new ApiResponse(null, "Имя не должно превышать 16 символов."));

    if (!Regex.IsMatch(request.NewName.Trim(), @"^[\p{L}\p{N}\s\-_]{1,16}$"))
        return Results.BadRequest(new ApiResponse(null, "Имя содержит недопустимые символы."));

    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));

    var result = game.RenamePlayer(playerId, request.NewName.Trim());
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    await BroadcastLobby(hub, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapGet("/api/reconnect", (RoomManager rooms, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Неизвестный игрок."));

    var state = game.IsSpectator(playerId) ? game.ToSpectatorView(playerId) : game.ToView(playerId);
    if (state is null) return Results.BadRequest(new ApiResponse(null, "Неизвестный игрок."));
    return Results.Ok(new ApiResponse(state, "ОК"));
}).RequireRateLimiting("general");

app.MapGet("/api/state", (RoomManager rooms, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));

    var state = game.IsSpectator(playerId) ? game.ToSpectatorView(playerId) : game.ToView(playerId);
    if (state is null) return Results.BadRequest(new ApiResponse(null, "Неизвестный игрок."));
    return Results.Ok(new ApiResponse(state, "ОК"));
}).RequireRateLimiting("general");

app.Run();

record PlayRequest(int CardIndex, string? TargetId);
record RespondRequest(string ResponseType, int? CardIndex, string? TargetId);
record ChatRequest(string Text);
record AbilityRequest(int[] CardIndices);
record ApiResponse(object? Data, string Message);
record RoomInfo(string RoomCode, int PlayerCount, int SpectatorCount, bool Started, bool GameOver, string StatusText);
record JoinRoomRequest(string Name, string RoomCode);
record CreateRoomResponse(string RoomCode);
record RenameRequest(string NewName);

record PendingActionView(
    string Type,
    string RespondingPlayerId,
    string RespondingPlayerName,
    string Message,
    List<CardView>? RevealedCards
);

record GameStateView(
    bool Started,
    string CurrentPlayerId,
    string CurrentPlayerName,
    bool GameOver,
    string? WinnerMessage,
    List<PlayerView> Players,
    List<CardView> YourHand,
    int BangsPlayedThisTurn,
    int BangLimit,
    List<string> EventLog,
    List<string> ChatMessages,
    PendingActionView? PendingAction,
    int WeaponRange,
    Dictionary<string, int>? Distances,
    bool IsSpectator = false,
    string? RoomCode = null,
    string? HostId = null,
    string? YourPublicId = null
);

record PlayerView(
    string Id,
    string Name,
    int Hp,
    int MaxHp,
    bool IsAlive,
    string Role,
    bool RoleRevealed,
    string CharacterName,
    string CharacterDescription,
    string CharacterPortrait,
    int HandCount,
    List<CardView> Equipment
);

record CardView(
    string Name,
    CardType Type,
    CardCategory Category,
    string Description,
    bool RequiresTarget,
    string? TargetHint,
    string ImagePath,
    string Suit,
    int Value
);

record CommandResult(bool Success, string Message, GameStateView? State = null, string? PlayerId = null);

class GameState
{
    private const int MaxPlayers = 6;
    private const int MaxSpectators = 10;
    private const int StartingHand = 4;
    private readonly Dictionary<string, PlayerState> _players = new();
    private readonly List<string> _turnOrder = new();
    private readonly List<string> _seatOrder = new();
    private readonly Random _random = new();
    private readonly Stack<Card> _drawPile = new();
    private readonly List<Card> _discardPile = new();
    private readonly object _lock = new();
    private readonly HashSet<int> _usedCharacterIndices = new();
    private PendingAction? _pendingAction;
    private int _turnIndex;

    private readonly List<string> _eventLog = new();
    private readonly List<string> _chatLog = new();
    private readonly HashSet<string> _spectators = new();
    private readonly Dictionary<string, string> _spectatorNames = new();
    private readonly Dictionary<string, string> _spectatorPublicIds = new();
    private string? _hostId;

    public string RoomCode { get; }

    public GameState(string roomCode = "")
    {
        RoomCode = roomCode;
    }

    public bool Started { get; private set; }
    public bool GameOver { get; private set; }
    public string? WinnerMessage { get; private set; }

    private void AddEvent(string msg)
    {
        _eventLog.Insert(0, msg);
        if (_eventLog.Count > 20) _eventLog.RemoveAt(_eventLog.Count - 1);
    }

    private static string PluralCard(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11) return $"{n} карту";
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return $"{n} карты";
        return $"{n} карт";
    }

    private PlayerState? FindByPublicId(string publicId)
    {
        return _players.Values.FirstOrDefault(p => p.PublicId == publicId);
    }

    private string? GetPublicId(string realId)
    {
        if (_players.TryGetValue(realId, out var p)) return p.PublicId;
        if (_spectatorPublicIds.TryGetValue(realId, out var spId)) return spId;
        return null;
    }

    public CommandResult TryAddPlayer(string name)
    {
        lock (_lock)
        {
            if ((Started && !GameOver) || _players.Count >= MaxPlayers)
            {
                if (_spectators.Count >= MaxSpectators)
                    return new CommandResult(false, "Комната переполнена.");

                var specId = Guid.NewGuid().ToString("N");
                _spectators.Add(specId);
                _spectatorNames[specId] = name;
                _spectatorPublicIds[specId] = Guid.NewGuid().ToString("N")[..8];
                _hostId ??= specId;
                AddEvent($"{name} присоединился как зритель.");
                return new CommandResult(true, "Вы зритель.", PlayerId: specId);
            }

            var id = Guid.NewGuid().ToString("N");
            var character = CharacterLibrary.Draw(_random, _usedCharacterIndices);
            var player = new PlayerState(id, name, character);
            _players[id] = player;
            _turnOrder.Add(id);
            _seatOrder.Add(id);
            _hostId ??= id;
            AddEvent($"{name} присоединился как {character.Name}.");
            return new CommandResult(true, "Вы в комнате.", PlayerId: id);
        }
    }

    public CommandResult RenamePlayer(string playerId, string newName)
    {
        lock (_lock)
        {
            if (_players.TryGetValue(playerId, out var player))
            {
                var oldName = player.Name;
                player.Name = newName;
                AddEvent($"{oldName} теперь известен как {newName}.");
                return new CommandResult(true, "Имя изменено.");
            }

            if (_spectators.Contains(playerId))
            {
                var oldName = _spectatorNames.GetValueOrDefault(playerId, "Зритель");
                _spectatorNames[playerId] = newName;
                AddEvent($"{oldName} теперь известен как {newName}.");
                return new CommandResult(true, "Имя изменено.");
            }

            return new CommandResult(false, "Игрок не найден.");
        }
    }

    public CommandResult StartGame(string playerId)
    {
        lock (_lock)
        {
            if (!_players.ContainsKey(playerId))
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            if (Started && !GameOver)
            {
                return new CommandResult(false, "Игра уже начата.");
            }

            if (_players.Count < 2)
            {
                return new CommandResult(false, "Нужно минимум 2 игрока.");
            }

            _turnOrder.Clear();
            _turnOrder.AddRange(_players.Values.OrderBy(_ => _random.Next()).Select(p => p.Id));
            _seatOrder.Clear();
            _seatOrder.AddRange(_turnOrder);
            _usedCharacterIndices.Clear();
            _pendingAction = null;
            _eventLog.Clear();
            _chatLog.Clear();
            Started = true;
            GameOver = false;
            WinnerMessage = null;
            BuildDeck();
            ShuffleDeck();

            foreach (var player in _players.Values)
            {
                var newCharacter = CharacterLibrary.Draw(_random, _usedCharacterIndices);
                player.AssignCharacter(newCharacter);
            }

            AssignRoles();
            foreach (var player in _players.Values)
            {
                player.ResetForNewGame();
                DrawCards(player, StartingHand);
            }

            _turnIndex = Math.Max(0, _turnOrder.FindIndex(id => _players[id].Role == Role.Sheriff));
            var current = _players[_turnOrder[_turnIndex]];
            current.ResetTurnFlags();
            HandleDrawPhase(current);
            if (_pendingAction == null)
            {
                AddEvent($"Игра началась! {current.Name} ходит первым как Шериф.");
            }

            return new CommandResult(true, "Игра началась.", ToView(playerId));
        }
    }

    public CommandResult PlayCard(string playerId, int index, string? targetId)
    {
        lock (_lock)
        {
            if (!Started)
            {
                return new CommandResult(false, "Игра ещё не началась.");
            }

            if (GameOver)
            {
                return new CommandResult(false, "Игра окончена. Начните новый раунд.");
            }

            if (_pendingAction != null)
            {
                return new CommandResult(false, "Ожидание ответа от игрока.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Сейчас не ваш ход.");
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            if (!player.IsAlive)
            {
                return new CommandResult(false, "Вы выбыли из игры.");
            }

            if (index < 0 || index >= player.Hand.Count)
            {
                return new CommandResult(false, "Неверный индекс карты.");
            }

            var card = player.Hand[index];
            var isCalamityJanet = player.Character.Name == "Каламити Джанет";
            if (card.Type == CardType.Missed && !isCalamityJanet)
            {
                return new CommandResult(false, "Мимо! можно играть только в ответ на выстрел.");
            }

            if (card.Type == CardType.Beer && _players.Values.Count(p => p.IsAlive) <= 2)
            {
                return new CommandResult(false, "Пиво нельзя использовать, когда осталось 2 или менее игроков.");
            }

            var effectiveType = card.Type;
            if (isCalamityJanet && card.Type == CardType.Missed)
            {
                effectiveType = CardType.Bang;
            }

            if (effectiveType == CardType.Bang && player.BangsPlayedThisTurn >= GetBangLimit(player))
            {
                var limit = GetBangLimit(player);
                return new CommandResult(false, $"Можно сыграть только {limit} Бэнг! за ход.");
            }

            var needsTarget = card.RequiresTarget || effectiveType == CardType.Bang;
            PlayerState? target = null;
            if (needsTarget && !TryGetTarget(targetId, playerId, out target, out var error))
            {
                return new CommandResult(false, error);
            }

            if (target != null)
            {
                var distance = GetDistance(playerId, target.Id);
                if (effectiveType == CardType.Bang && distance > GetWeaponRange(player))
                {
                    return new CommandResult(false, $"{target.Name} вне зоны досягаемости (расстояние {distance}, дальность оружия {GetWeaponRange(player)}).");
                }
                if (effectiveType == CardType.Panic && distance > 1)
                {
                    return new CommandResult(false, $"{target.Name} вне зоны досягаемости для Паники! (расстояние {distance}, нужно 1).");
                }
            }

            if (card.Type == CardType.Jail)
            {
                if (target == null)
                {
                    return new CommandResult(false, "Выберите игрока для заключения.");
                }
                if (target.Role == Role.Sheriff)
                {
                    return new CommandResult(false, "Шерифа нельзя посадить в тюрьму.");
                }
                if (target.InPlay.Any(c => c.Type == CardType.Jail))
                {
                    return new CommandResult(false, $"{target.Name} уже в тюрьме.");
                }
                player.Hand.RemoveAt(index);
                target.InPlay.Add(card);
                var jailMsg = $"{player.Name} бросает {target.Name} в тюрьму!";
                AddEvent(jailMsg);
                return new CommandResult(true, jailMsg, ToView(playerId));
            }

            if (card.Type == CardType.Dynamite)
            {
                if (player.InPlay.Any(c => c.Type == CardType.Dynamite))
                {
                    return new CommandResult(false, "У вас уже есть Динамит в игре.");
                }
                player.Hand.RemoveAt(index);
                player.InPlay.Add(card);
                var dynMsg = $"{player.Name} играет Динамит!";
                AddEvent(dynMsg);
                return new CommandResult(true, dynMsg, ToView(playerId));
            }

            player.Hand.RemoveAt(index);

            if (card.Category == CardCategory.Blue || card.Category == CardCategory.Weapon)
            {
                if (card.Category == CardCategory.Weapon)
                {
                    var oldWeapon = player.InPlay.FirstOrDefault(c => c.Category == CardCategory.Weapon);
                    if (oldWeapon != null)
                    {
                        player.InPlay.Remove(oldWeapon);
                        _discardPile.Add(oldWeapon);
                    }
                }
                else
                {
                    var duplicate = player.InPlay.FirstOrDefault(c => c.Type == card.Type);
                    if (duplicate != null)
                    {
                        player.InPlay.Remove(duplicate);
                        _discardPile.Add(duplicate);
                    }
                }

                player.InPlay.Add(card);
                var equipMsg = $"{player.Name} экипирует {card.Name}.";
                AddEvent(equipMsg);
                return new CommandResult(true, equipMsg, ToView(playerId));
            }

            _discardPile.Add(card);

            var message = effectiveType switch
            {
                CardType.Bang => ResolveBang(player, target!),
                CardType.Beer => ResolveBeer(player),
                CardType.Gatling => ResolveGatling(player),
                CardType.Stagecoach => ResolveStagecoach(player),
                CardType.CatBalou => ResolveCatBalou(player, target!),
                CardType.Indians => ResolveIndians(player),
                CardType.Duel => ResolveDuel(player, target!),
                CardType.Panic => ResolvePanic(player, target!),
                CardType.Saloon => ResolveSaloon(player),
                CardType.WellsFargo => ResolveWellsFargo(player),
                CardType.GeneralStore => ResolveGeneralStore(player),
                _ => "Карта не возымела эффекта."
            };

            if (effectiveType == CardType.Bang)
            {
                player.BangsPlayedThisTurn += 1;
            }

            CheckSuzyLafayette(player);

            if (GameOver && !string.IsNullOrWhiteSpace(WinnerMessage))
            {
                message = WinnerMessage;
            }

            AddEvent(message);
            return new CommandResult(true, message, ToView(playerId));
        }
    }

    public CommandResult EndTurn(string playerId)
    {
        lock (_lock)
        {
            if (!Started)
            {
                return new CommandResult(false, "Игра ещё не началась.");
            }

            if (GameOver)
            {
                return new CommandResult(false, "Игра окончена. Начните новый раунд.");
            }

            if (_pendingAction != null)
            {
                return new CommandResult(false, "Ожидание ответа от игрока.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Сейчас не ваш ход.");
            }

            var endingPlayer = _players[_turnOrder[_turnIndex]];
            if (!endingPlayer.IsAlive)
            {
                return new CommandResult(false, "Вы выбыли из игры.");
            }

            CheckSuzyLafayette(endingPlayer);

            if (endingPlayer.Hand.Count > endingPlayer.Hp)
            {
                _pendingAction = new PendingAction(
                    PendingActionType.DiscardExcess,
                    endingPlayer.Id,
                    new[] { endingPlayer.Id });
                var excess = endingPlayer.Hand.Count - endingPlayer.Hp;
                var discardMsg = $"{endingPlayer.Name} должен сбросить {PluralCard(excess)} до лимита руки.";
                AddEvent(discardMsg);
                return new CommandResult(true, discardMsg, ToView(playerId));
            }

            AdvanceTurn();
            return new CommandResult(true, "Ход завершён.", ToView(playerId));
        }
    }

    public CommandResult AddChat(string playerId, string text)
    {
        lock (_lock)
        {
            string senderName;
            bool isSpectator = _spectators.Contains(playerId);

            if (isSpectator)
            {
                if (!_spectatorNames.TryGetValue(playerId, out senderName!))
                {
                    return new CommandResult(false, "Неизвестный игрок.");
                }
            }
            else if (_players.TryGetValue(playerId, out var player))
            {
                senderName = player.Name;
            }
            else
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new CommandResult(false, "Сообщение не может быть пустым.");
            }

            if (text.Trim().Length > 200)
            {
                return new CommandResult(false, "Сообщение слишком длинное (макс. 200 символов).");
            }

            var prefix = isSpectator ? "[Зритель] " : "";
            var chatMsg = $"{prefix}{senderName}: {text.Trim()}";
            _chatLog.Insert(0, chatMsg);
            if (_chatLog.Count > 30) _chatLog.RemoveAt(_chatLog.Count - 1);

            var view = isSpectator ? ToSpectatorView(playerId) : ToView(playerId);
            return new CommandResult(true, "Сообщение отправлено.", view);
        }
    }

    public CommandResult Respond(string playerId, string responseType, int? cardIndex, string? targetId = null)
    {
        lock (_lock)
        {
            if (GameOver)
            {
                _pendingAction = null;
                return new CommandResult(false, "Игра окончена.");
            }

            if (_pendingAction == null)
            {
                return new CommandResult(false, "Нет действия для ответа.");
            }

            if (_pendingAction.RespondingPlayerIds.Count == 0)
            {
                _pendingAction = null;
                return new CommandResult(false, "Нет действия для ответа.");
            }

            var currentResponderId = _pendingAction.RespondingPlayerIds.Peek();
            if (currentResponderId != playerId)
            {
                return new CommandResult(false, "Сейчас не ваша очередь отвечать.");
            }

            if (!_players.TryGetValue(playerId, out var responder))
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            var source = _players.ContainsKey(_pendingAction.SourcePlayerId)
                ? _players[_pendingAction.SourcePlayerId]
                : null;

            string message;

            switch (_pendingAction.Type)
            {
                case PendingActionType.BangDefense:
                case PendingActionType.GatlingDefense:
                {
                    if (responseType == "play_card")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                        {
                            return new CommandResult(false, "Неверный индекс карты.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.Character.Name == "Каламити Джанет";
                        if (card.Type != CardType.Missed && !(isJanet && card.Type == CardType.Bang))
                        {
                            return new CommandResult(false, isJanet
                                ? "Нужно сыграть Мимо! или Бэнг!, чтобы увернуться."
                                : "Нужно сыграть Мимо!, чтобы увернуться.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        message = $"{responder.Name} играет {card.Name} и уворачивается от выстрела!";
                    }
                    else
                    {
                        var damage = _pendingAction.Damage;
                        ApplyDamage(source ?? responder, responder, damage, "стреляет в");
                        message = FormatAttackMessage(source ?? responder, responder, "стреляет в", damage);
                    }

                    _pendingAction.RespondingPlayerIds.Dequeue();
                    if (_pendingAction.RespondingPlayerIds.Count == 0)
                    {
                        _pendingAction = null;
                    }
                    break;
                }

                case PendingActionType.IndiansDefense:
                {
                    if (responseType == "play_card")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                        {
                            return new CommandResult(false, "Неверный индекс карты.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.Character.Name == "Каламити Джанет";
                        if (card.Type != CardType.Bang && !(isJanet && card.Type == CardType.Missed))
                        {
                            return new CommandResult(false, isJanet
                                ? "Нужно сбросить Бэнг! или Мимо!, чтобы избежать атаки индейцев."
                                : "Нужно сбросить Бэнг!, чтобы избежать атаки индейцев.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        message = $"{responder.Name} сбрасывает {card.Name} и отбивается от индейцев!";
                    }
                    else
                    {
                        if (source != null)
                        {
                            ApplyDamage(source, responder, 1, "атакован индейцами");
                        }
                        message = $"{responder.Name} атакован индейцами и получает 1 урон.";
                    }

                    _pendingAction.RespondingPlayerIds.Dequeue();
                    if (_pendingAction.RespondingPlayerIds.Count == 0)
                    {
                        _pendingAction = null;
                    }
                    break;
                }

                case PendingActionType.DuelChallenge:
                {
                    var opponentId = _pendingAction.DuelPlayerA == playerId
                        ? _pendingAction.DuelPlayerB!
                        : _pendingAction.DuelPlayerA!;
                    var opponent = _players[opponentId];

                    if (responseType == "play_card")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                        {
                            return new CommandResult(false, "Неверный индекс карты.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.Character.Name == "Каламити Джанет";
                        if (card.Type != CardType.Bang && !(isJanet && card.Type == CardType.Missed))
                        {
                            return new CommandResult(false, isJanet
                                ? "Нужно сыграть Бэнг! или Мимо!, чтобы продолжить дуэль."
                                : "Нужно сыграть Бэнг!, чтобы продолжить дуэль.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);

                        _pendingAction.RespondingPlayerIds.Dequeue();
                        _pendingAction.RespondingPlayerIds.Enqueue(opponentId);
                        message = $"{responder.Name} отвечает в дуэли! {opponent.Name} должен ответить.";
                    }
                    else
                    {
                        ApplyDamage(opponent, responder, 1, "проиграл дуэль против");
                        message = $"{responder.Name} не может продолжить дуэль и получает 1 урон!";
                        _pendingAction = null;
                    }
                    break;
                }

                case PendingActionType.GeneralStorePick:
                {
                    if (_pendingAction.RevealedCards == null || _pendingAction.RevealedCards.Count == 0)
                    {
                        _pendingAction.RespondingPlayerIds.Dequeue();
                        if (_pendingAction.RespondingPlayerIds.Count == 0)
                        {
                            _pendingAction = null;
                        }
                        message = "Больше нет карт для выбора.";
                        break;
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                    {
                        return new CommandResult(false, "Неверный выбор карты.");
                    }

                    var pickedCard = _pendingAction.RevealedCards[cardIndex.Value];
                    _pendingAction.RevealedCards.RemoveAt(cardIndex.Value);
                    responder.Hand.Add(pickedCard);
                    message = $"{responder.Name} берёт {pickedCard.Name} из Магазина.";

                    _pendingAction.RespondingPlayerIds.Dequeue();
                    if (_pendingAction.RespondingPlayerIds.Count == 0 || _pendingAction.RevealedCards.Count == 0)
                    {
                        if (_pendingAction.RevealedCards.Count > 0)
                        {
                            foreach (var leftover in _pendingAction.RevealedCards)
                            {
                                _discardPile.Add(leftover);
                            }
                        }
                        _pendingAction = null;
                    }
                    break;
                }

                case PendingActionType.ChooseStealSource:
                {
                    var stealTarget = _players.ContainsKey(_pendingAction.StealTargetId!)
                        ? _players[_pendingAction.StealTargetId!]
                        : null;
                    if (stealTarget == null)
                    {
                        _pendingAction = null;
                        message = "Цель больше не существует.";
                        break;
                    }

                    var isSteal = _pendingAction.StealMode == "steal";

                    if (responseType == "hand")
                    {
                        if (stealTarget.Hand.Count == 0)
                        {
                            message = $"У {stealTarget.Name} не осталось карт в руке.";
                            _pendingAction = null;
                            break;
                        }
                        var idx = _random.Next(stealTarget.Hand.Count);
                        var card = stealTarget.Hand[idx];
                        stealTarget.Hand.RemoveAt(idx);
                        if (isSteal)
                        {
                            responder.Hand.Add(card);
                            message = $"{responder.Name} крадёт карту из руки {stealTarget.Name}.";
                        }
                        else
                        {
                            _discardPile.Add(card);
                            message = $"{responder.Name} сбрасывает {card.Name} из руки {stealTarget.Name}.";
                        }
                    }
                    else if (responseType == "equipment")
                    {
                        if (cardIndex == null || _pendingAction.RevealedCards == null ||
                            cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                        {
                            return new CommandResult(false, "Неверный выбор снаряжения.");
                        }
                        var equipCard = _pendingAction.RevealedCards[cardIndex.Value];
                        stealTarget.InPlay.Remove(equipCard);
                        if (isSteal)
                        {
                            responder.Hand.Add(equipCard);
                            message = $"{responder.Name} крадёт {equipCard.Name} у {stealTarget.Name}.";
                        }
                        else
                        {
                            _discardPile.Add(equipCard);
                            message = $"{responder.Name} сбрасывает {equipCard.Name} у {stealTarget.Name}.";
                        }
                    }
                    else
                    {
                        return new CommandResult(false, "Выберите источник: рука или снаряжение.");
                    }

                    _pendingAction = null;
                    break;
                }

                case PendingActionType.DiscardExcess:
                {
                    if (responseType != "play_card")
                    {
                        return new CommandResult(false, "Вы должны сбросить карту.");
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                    {
                        return new CommandResult(false, "Неверный индекс карты.");
                    }

                    var card = responder.Hand[cardIndex.Value];
                    responder.Hand.RemoveAt(cardIndex.Value);
                    _discardPile.Add(card);

                    if (responder.Hand.Count <= responder.Hp)
                    {
                        message = $"{responder.Name} сбрасывает {card.Name}. Лимит руки достигнут.";
                        _pendingAction = null;
                        AdvanceTurn();
                    }
                    else
                    {
                        var remaining = responder.Hand.Count - responder.Hp;
                        message = $"{responder.Name} сбрасывает {card.Name}. Осталось сбросить: {remaining}.";
                    }
                    break;
                }

                case PendingActionType.JesseJonesSteal:
                {
                    var jesseTarget = string.IsNullOrWhiteSpace(targetId) ? null : FindByPublicId(targetId);
                    if (jesseTarget == null)
                    {
                        return new CommandResult(false, "Выберите игрока, у которого взять карту.");
                    }
                    if (!jesseTarget.IsAlive || jesseTarget.Hand.Count == 0)
                    {
                        return new CommandResult(false, $"У {jesseTarget.Name} нет карт для взятия.");
                    }
                    if (jesseTarget.Id == playerId)
                    {
                        return new CommandResult(false, "Нельзя тянуть карту у себя.");
                    }

                    var stealIdx = _random.Next(jesseTarget.Hand.Count);
                    var stolenCard = jesseTarget.Hand[stealIdx];
                    jesseTarget.Hand.RemoveAt(stealIdx);
                    responder.Hand.Add(stolenCard);
                    DrawCards(responder, 1);
                    _pendingAction = null;
                    message = $"{responder.Name} тянет карту у {jesseTarget.Name} и 1 из колоды.";
                    break;
                }

                case PendingActionType.KitCarlsonPick:
                {
                    if (_pendingAction.RevealedCards == null || _pendingAction.RevealedCards.Count == 0)
                    {
                        _pendingAction = null;
                        message = "Больше нет карт для выбора.";
                        break;
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                    {
                        return new CommandResult(false, "Неверный выбор карты.");
                    }

                    var picked = _pendingAction.RevealedCards[cardIndex.Value];
                    _pendingAction.RevealedCards.RemoveAt(cardIndex.Value);
                    responder.Hand.Add(picked);
                    _pendingAction.KitCarlsonPicksRemaining--;

                    if (_pendingAction.KitCarlsonPicksRemaining <= 0 || _pendingAction.RevealedCards.Count == 0)
                    {
                        foreach (var leftover in _pendingAction.RevealedCards)
                        {
                            _drawPile.Push(leftover);
                        }
                        _pendingAction = null;
                        message = $"{responder.Name} завершает набор.";
                    }
                    else
                    {
                        message = $"{responder.Name} берёт карту. Осталось выбрать: {_pendingAction.KitCarlsonPicksRemaining}.";
                    }
                    break;
                }

                default:
                    return new CommandResult(false, "Неизвестный тип действия.");
            }

            if (GameOver)
            {
                _pendingAction = null;
                if (!string.IsNullOrWhiteSpace(WinnerMessage))
                {
                    message = WinnerMessage;
                }
            }

            AddEvent(message);
            return new CommandResult(true, message, ToView(playerId));
        }
    }

    public CommandResult UseAbility(string playerId, int[] cardIndices)
    {
        lock (_lock)
        {
            if (!Started || GameOver)
            {
                return new CommandResult(false, "Игра не активна.");
            }

            if (_pendingAction != null)
            {
                return new CommandResult(false, "Ожидание ответа от игрока.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Сейчас не ваш ход.");
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            if (player.Character.Name != "Сид Кетчум")
            {
                return new CommandResult(false, "У вашего персонажа нет активной способности.");
            }

            if (cardIndices == null || cardIndices.Length != 2)
            {
                return new CommandResult(false, "Нужно выбрать ровно 2 карты для сброса.");
            }

            if (player.Hp >= player.MaxHp)
            {
                return new CommandResult(false, "У вас уже максимальное здоровье.");
            }

            if (player.Hand.Count < 2)
            {
                return new CommandResult(false, "Нужно минимум 2 карты для использования способности.");
            }

            var sorted = cardIndices.OrderByDescending(i => i).ToArray();
            if (sorted.Any(i => i < 0 || i >= player.Hand.Count) || sorted[0] == sorted[1])
            {
                return new CommandResult(false, "Неверный выбор карты.");
            }

            var card1 = player.Hand[sorted[0]];
            var card2 = player.Hand[sorted[1]];
            player.Hand.RemoveAt(sorted[0]);
            player.Hand.RemoveAt(sorted[1]);
            _discardPile.Add(card1);
            _discardPile.Add(card2);

            player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
            var message = $"{player.Name} сбрасывает {card1.Name} и {card2.Name}, чтобы восстановить 1 ОЗ.";
            AddEvent(message);
            CheckSuzyLafayette(player);
            return new CommandResult(true, message, ToView(playerId));
        }
    }

    public CommandResult ResetGame(string playerId)
    {
        lock (_lock)
        {
            if (!_players.ContainsKey(playerId) && !_spectators.Contains(playerId))
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            if (!GameOver)
            {
                return new CommandResult(false, "Игра ещё не окончена.");
            }

            // Promote spectators to players (up to MaxPlayers)
            var specIds = _spectators.ToList();
            foreach (var specId in specIds)
            {
                if (_players.Count >= MaxPlayers) break;
                var name = _spectatorNames[specId];
                var character = CharacterLibrary.Draw(_random, _usedCharacterIndices);
                var player = new PlayerState(specId, name, character);
                _players[specId] = player;
                _spectators.Remove(specId);
                _spectatorNames.Remove(specId);
                _spectatorPublicIds.Remove(specId);
                AddEvent($"{name} повышен из зрителя до игрока.");
            }

            if (_players.Count < 2)
            {
                return new CommandResult(false, "Нужно минимум 2 игрока для новой игры.");
            }

            _turnOrder.Clear();
            _turnOrder.AddRange(_players.Values.OrderBy(_ => _random.Next()).Select(p => p.Id));
            _seatOrder.Clear();
            _seatOrder.AddRange(_turnOrder);
            _usedCharacterIndices.Clear();
            _pendingAction = null;
            _eventLog.Clear();
            _chatLog.Clear();
            Started = true;
            GameOver = false;
            WinnerMessage = null;
            BuildDeck();
            ShuffleDeck();

            foreach (var player in _players.Values)
            {
                var newCharacter = CharacterLibrary.Draw(_random, _usedCharacterIndices);
                player.AssignCharacter(newCharacter);
            }

            AssignRoles();
            foreach (var player in _players.Values)
            {
                player.ResetForNewGame();
                DrawCards(player, StartingHand);
            }

            _turnIndex = Math.Max(0, _turnOrder.FindIndex(id => _players[id].Role == Role.Sheriff));
            var current = _players[_turnOrder[_turnIndex]];
            current.ResetTurnFlags();
            HandleDrawPhase(current);
            if (_pendingAction == null)
            {
                AddEvent($"Новая игра началась! {current.Name} ходит первым как Шериф.");
            }

            var isSpec = _spectators.Contains(playerId);
            return new CommandResult(true, "Новая игра началась.", isSpec ? ToSpectatorView(playerId) : ToView(playerId));
        }
    }

    public CommandResult RemovePlayer(string playerId)
    {
        lock (_lock)
        {
            // Remove spectator
            if (_spectators.Contains(playerId))
            {
                var specName = _spectatorNames.GetValueOrDefault(playerId, "Зритель");
                _spectators.Remove(playerId);
                _spectatorNames.Remove(playerId);
                _spectatorPublicIds.Remove(playerId);
                AddEvent($"{specName} (зритель) покинул комнату.");
                TransferHostIfNeeded(playerId);
                return new CommandResult(true, "Вы покинули комнату.");
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            var name = player.Name;

            // Pre-game removal
            if (!Started || GameOver)
            {
                _players.Remove(playerId);
                _turnOrder.Remove(playerId);
                _seatOrder.Remove(playerId);
                AddEvent($"{name} покинул комнату.");
                TransferHostIfNeeded(playerId);
                return new CommandResult(true, "Вы покинули комнату.");
            }

            // Mid-game removal: eliminate the player
            if (player.IsAlive)
            {
                player.Hp = 0;
                player.IsAlive = false;

                // Clean up pending actions involving this player
                if (_pendingAction != null)
                {
                    var newQueue = new Queue<string>(
                        _pendingAction.RespondingPlayerIds.Where(id => id != playerId));
                    _pendingAction.RespondingPlayerIds.Clear();
                    foreach (var id in newQueue) _pendingAction.RespondingPlayerIds.Enqueue(id);

                    if (_pendingAction.RespondingPlayerIds.Count == 0)
                    {
                        _pendingAction = null;
                    }

                    // If it was a duel and one participant left
                    if (_pendingAction?.Type == PendingActionType.DuelChallenge &&
                        (_pendingAction.DuelPlayerA == playerId || _pendingAction.DuelPlayerB == playerId))
                    {
                        _pendingAction = null;
                    }

                    // If the steal target left
                    if (_pendingAction?.StealTargetId == playerId)
                    {
                        _pendingAction = null;
                    }
                }

                // Discard all cards
                foreach (var card in player.Hand) _discardPile.Add(card);
                foreach (var card in player.InPlay) _discardPile.Add(card);
                player.Hand.Clear();
                player.InPlay.Clear();

                var wasCurrentTurn = IsPlayersTurn(playerId);
                RemoveFromTurnOrder(playerId);
                CheckForGameOver();

                AddEvent($"{name} покинул игру и был устранён.");

                if (!GameOver && wasCurrentTurn && _turnOrder.Count > 0)
                {
                    var next = _players[_turnOrder[_turnIndex]];
                    if (next.IsAlive) BeginTurn(next);
                }
            }

            TransferHostIfNeeded(playerId);
            return new CommandResult(true, "Вы покинули комнату.");
        }
    }

    public bool IsSpectator(string playerId)
    {
        lock (_lock) { return _spectators.Contains(playerId); }
    }

    public bool IsHost(string playerId) => _hostId == playerId;

    private void TransferHostIfNeeded(string leavingId)
    {
        if (_hostId != leavingId) return;
        // Transfer to first alive player, then any player, then any spectator
        _hostId = _turnOrder.FirstOrDefault(id => _players.TryGetValue(id, out var p) && p.IsAlive)
                  ?? _players.Keys.FirstOrDefault()
                  ?? _spectators.FirstOrDefault();
    }

    public bool HasPlayer(string playerId)
    {
        lock (_lock) { return _players.ContainsKey(playerId) || _spectators.Contains(playerId); }
    }

    public bool IsEmpty()
    {
        lock (_lock) { return _players.Count == 0 && _spectators.Count == 0; }
    }

    public RoomInfo GetRoomInfo()
    {
        lock (_lock)
        {
            var status = GameOver ? "Игра окончена" : Started ? "В процессе" : $"Ожидание ({_players.Count}/{MaxPlayers})";
            return new RoomInfo(RoomCode, _players.Count, _spectators.Count, Started, GameOver, status);
        }
    }

    public GameStateView? ToSpectatorView(string playerId)
    {
        lock (_lock)
        {
            if (!_spectators.Contains(playerId)) return null;

            var currentRealId = _turnOrder.Count > 0 ? _turnOrder[_turnIndex] : null;
            var currentPublicId = currentRealId != null ? GetPublicId(currentRealId) ?? "-" : "-";
            var currentName = currentRealId != null && _players.TryGetValue(currentRealId, out var current) ? current.Name : "-";
            var seatList = _seatOrder.Count > 0 ? _seatOrder : _turnOrder;
            var orderedIds = seatList.Where(id => _players.ContainsKey(id)).ToList();
            var players = orderedIds
                .Select(id => _players[id])
                .Select(p => new PlayerView(
                    p.PublicId,
                    p.Name,
                    p.Hp,
                    p.MaxHp,
                    p.IsAlive,
                    TranslateRole(GameOver || !p.IsAlive || p.Role == Role.Sheriff ? p.Role.ToString() : "Неизвестно"),
                    GameOver || !p.IsAlive || p.Role == Role.Sheriff,
                    p.Character.Name,
                    p.Character.Description,
                    p.Character.PortraitPath,
                    p.Hand.Count,
                    p.InPlay.Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value)).ToList()))
                .ToList();

            PendingActionView? pendingView = null;
            if (_pendingAction != null && _pendingAction.RespondingPlayerIds.Count > 0)
            {
                var responderId = _pendingAction.RespondingPlayerIds.Peek();
                var responder = _players[responderId];
                pendingView = new PendingActionView(
                    _pendingAction.Type.ToString(),
                    responder.PublicId,
                    responder.Name,
                    "Ожидание ответа...",
                    null);
            }

            var myPublicId = _spectatorPublicIds.TryGetValue(playerId, out var spPubId) ? spPubId : playerId;

            return new GameStateView(
                Started,
                currentPublicId,
                currentName,
                GameOver,
                WinnerMessage,
                players,
                new List<CardView>(),
                0,
                0,
                new List<string>(_eventLog),
                new List<string>(_chatLog),
                pendingView,
                0,
                null,
                IsSpectator: true,
                RoomCode: RoomCode,
                HostId: GetPublicId(_hostId ?? ""),
                YourPublicId: myPublicId);
        }
    }

    public GameStateView? ToView(string playerId)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(playerId, out var viewer))
            {
                return null;
            }

            var currentRealId = _turnOrder.Count > 0 ? _turnOrder[_turnIndex] : null;
            var currentPublicId = currentRealId != null ? GetPublicId(currentRealId) ?? "-" : "-";
            var currentName = currentRealId != null && _players.TryGetValue(currentRealId, out var current) ? current.Name : "-";
            var seatList = _seatOrder.Count > 0 ? _seatOrder : _turnOrder;
            var orderedIds = seatList.Where(id => _players.ContainsKey(id)).ToList();
            var viewerIdx = orderedIds.IndexOf(playerId);
            if (viewerIdx > 0)
                orderedIds = orderedIds.Skip(viewerIdx).Concat(orderedIds.Take(viewerIdx)).ToList();

            var players = orderedIds
                .Select(id => _players[id])
                .Select(p => new PlayerView(
                    p.PublicId,
                    p.Name,
                    p.Hp,
                    p.MaxHp,
                    p.IsAlive,
                    GetRoleNameForViewer(p, viewer),
                    IsRoleRevealed(p, viewer),
                    p.Character.Name,
                    p.Character.Description,
                    p.Character.PortraitPath,
                    p.Hand.Count,
                    p.InPlay.Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value)).ToList()))
                .ToList();

            var hand = viewer.Hand
                .Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value))
                .ToList();

            PendingActionView? pendingView = null;
            if (_pendingAction != null && _pendingAction.RespondingPlayerIds.Count > 0)
            {
                var responderId = _pendingAction.RespondingPlayerIds.Peek();
                var responder = _players[responderId];
                var message = _pendingAction.Type switch
                {
                    PendingActionType.BangDefense => $"Сыграйте Мимо!, чтобы увернуться, или получите {_pendingAction.Damage} урона.",
                    PendingActionType.GatlingDefense => "Сыграйте Мимо!, чтобы увернуться от Гатлинга, или получите 1 урон.",
                    PendingActionType.IndiansDefense => "Сбросьте Бэнг!, чтобы избежать индейцев, или получите 1 урон.",
                    PendingActionType.DuelChallenge => "Сыграйте Бэнг!, чтобы продолжить дуэль, или получите 1 урон.",
                    PendingActionType.GeneralStorePick => "Выберите карту из Магазина.",
                    PendingActionType.DiscardExcess => $"Сбросьте до {responder.Hp} карт (осталось сбросить: {responder.Hand.Count - responder.Hp}).",
                    PendingActionType.ChooseStealSource => $"Выберите: случайная карта из руки или конкретное снаряжение.",
                    PendingActionType.JesseJonesSteal => "Выберите игрока, у которого взять карту.",
                    PendingActionType.KitCarlsonPick => $"Выберите карту (осталось: {_pendingAction.KitCarlsonPicksRemaining}).",
                    _ => "Ответьте на действие."
                };

                List<CardView>? revealedCards = null;
                var isPrivateReveal = _pendingAction.Type == PendingActionType.KitCarlsonPick;
                if (_pendingAction.RevealedCards != null && (!isPrivateReveal || responderId == playerId))
                {
                    revealedCards = _pendingAction.RevealedCards
                        .Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value))
                        .ToList();
                }

                pendingView = new PendingActionView(
                    _pendingAction.Type.ToString(),
                    responder.PublicId,
                    responder.Name,
                    message,
                    revealedCards);
            }

            var weaponRange = GetWeaponRange(viewer);
            Dictionary<string, int>? distances = null;
            if (Started && !GameOver && viewer.IsAlive)
            {
                distances = new Dictionary<string, int>();
                foreach (var p in _players.Values)
                {
                    if (p.Id != viewer.Id && p.IsAlive)
                    {
                        distances[p.PublicId] = GetDistance(viewer.Id, p.Id);
                    }
                }
            }

            return new GameStateView(
                Started,
                currentPublicId,
                currentName,
                GameOver,
                WinnerMessage,
                players,
                hand,
                viewer.BangsPlayedThisTurn,
                GetBangLimit(viewer),
                new List<string>(_eventLog),
                new List<string>(_chatLog),
                pendingView,
                weaponRange,
                distances,
                IsSpectator: false,
                RoomCode: RoomCode,
                HostId: GetPublicId(_hostId ?? ""),
                YourPublicId: viewer.PublicId);
        }
    }

    private bool IsPlayersTurn(string playerId)
    {
        return _turnOrder.Count > 0 && _turnOrder[_turnIndex] == playerId;
    }

    private void AdvanceTurn()
    {
        if (_turnOrder.Count == 0) return;

        for (var i = 0; i < _turnOrder.Count; i++)
        {
            _turnIndex = (_turnIndex + 1) % _turnOrder.Count;
            var current = _players[_turnOrder[_turnIndex]];
            if (current.IsAlive) break;
        }

        if (_turnOrder.Count == 0 || GameOver) return;
        BeginTurn(_players[_turnOrder[_turnIndex]]);
    }

    private void BeginTurn(PlayerState current)
    {
        if (!current.IsAlive || GameOver) return;

        current.ResetTurnFlags();

        // 1. Dynamite check
        var dynamite = current.InPlay.FirstOrDefault(c => c.Type == CardType.Dynamite);
        if (dynamite != null)
        {
            current.InPlay.Remove(dynamite);
            bool isLuckyDuke = current.Character.Name == "Лаки Дьюк";
            bool explodes;

            if (isLuckyDuke)
            {
                var card1 = DrawCheckCard();
                var card2 = DrawCheckCard();
                bool e1 = card1.Suit == CardSuit.Spades && card1.Value >= 2 && card1.Value <= 9;
                bool e2 = card2.Suit == CardSuit.Spades && card2.Value >= 2 && card2.Value <= 9;
                explodes = e1 && e2;
                AddEvent($"Проверка Динамита! {current.Name} (Лаки Дьюк): {FormatCheckCard(card1)} и {FormatCheckCard(card2)}");
            }
            else
            {
                var check = DrawCheckCard();
                explodes = check.Suit == CardSuit.Spades && check.Value >= 2 && check.Value <= 9;
                AddEvent($"Проверка Динамита! {current.Name}: {FormatCheckCard(check)}");
            }

            if (explodes)
            {
                _discardPile.Add(dynamite);
                AddEvent($"БУМ! Динамит взрывается у {current.Name} и наносит 3 урона!");
                ApplyDamage(current, current, 3, "взорван динамитом");
                if (GameOver) return;
                if (!current.IsAlive)
                {
                    // RemoveFromTurnOrder already adjusted _turnIndex
                    if (_turnOrder.Count == 0) return;
                    var next = _players[_turnOrder[_turnIndex]];
                    if (!next.IsAlive) return;
                    BeginTurn(next);
                    return;
                }
            }
            else
            {
                var nextAliveId = GetNextAlivePlayerId(current.Id);
                if (nextAliveId != null)
                {
                    var nextPlayer = _players[nextAliveId];
                    nextPlayer.InPlay.Add(dynamite);
                    AddEvent($"Динамит не взорвался и переходит к {nextPlayer.Name}.");
                }
                else
                {
                    _discardPile.Add(dynamite);
                }
            }
        }

        // 2. Jail check
        var jail = current.InPlay.FirstOrDefault(c => c.Type == CardType.Jail);
        if (jail != null && current.IsAlive)
        {
            current.InPlay.Remove(jail);
            _discardPile.Add(jail);
            bool isLuckyDuke = current.Character.Name == "Лаки Дьюк";
            bool escapes;

            if (isLuckyDuke)
            {
                var card1 = DrawCheckCard();
                var card2 = DrawCheckCard();
                escapes = card1.Suit == CardSuit.Hearts || card2.Suit == CardSuit.Hearts;
                AddEvent($"Проверка Тюрьмы! {current.Name} (Лаки Дьюк): {FormatCheckCard(card1)} и {FormatCheckCard(card2)}");
            }
            else
            {
                var check = DrawCheckCard();
                escapes = check.Suit == CardSuit.Hearts;
                AddEvent($"Проверка Тюрьмы! {current.Name}: {FormatCheckCard(check)}");
            }

            if (escapes)
            {
                AddEvent($"{current.Name} вырывается из тюрьмы!");
            }
            else
            {
                AddEvent($"{current.Name} остаётся в тюрьме. Ход пропущен.");
                AdvanceTurn();
                return;
            }
        }

        // 3. Normal draw phase
        if (current.IsAlive && !GameOver)
        {
            AddEvent($"Ход {current.Name} начинается.");
            HandleDrawPhase(current);
        }
    }

    private void HandleDrawPhase(PlayerState player)
    {
        switch (player.Character.Name)
        {
            case "Джесси Джонс":
            {
                var validTargets = _players.Values
                    .Where(p => p.IsAlive && p.Id != player.Id && p.Hand.Count > 0)
                    .Select(p => p.Id)
                    .ToList();
                if (validTargets.Count > 0)
                {
                    _pendingAction = new PendingAction(
                        PendingActionType.JesseJonesSteal,
                        player.Id,
                        new[] { player.Id });
                    AddEvent($"Ход {player.Name} начинается. Выберите игрока, у которого взять карту.");
                    return;
                }
                DrawCards(player, 2);
                break;
            }
            case "Кит Карлсон":
            {
                var revealedCards = new List<Card>();
                for (var i = 0; i < 3; i++)
                {
                    if (_drawPile.Count == 0) ReshuffleDiscardIntoDraw();
                    if (_drawPile.Count == 0) break;
                    revealedCards.Add(_drawPile.Pop());
                }
                if (revealedCards.Count <= 2)
                {
                    foreach (var c in revealedCards) player.Hand.Add(c);
                    break;
                }
                _pendingAction = new PendingAction(
                    PendingActionType.KitCarlsonPick,
                    player.Id,
                    new[] { player.Id });
                _pendingAction.RevealedCards = revealedCards;
                _pendingAction.KitCarlsonPicksRemaining = 2;
                AddEvent($"Ход {player.Name} начинается. Выберите 2 из 3 открытых карт.");
                return;
            }
            case "Педро Рамирес":
            {
                if (_discardPile.Count > 0)
                {
                    var topDiscard = _discardPile[^1];
                    _discardPile.RemoveAt(_discardPile.Count - 1);
                    player.Hand.Add(topDiscard);
                    DrawCards(player, 1);
                }
                else
                {
                    DrawCards(player, 2);
                }
                break;
            }
            default:
                DrawCards(player, 2);
                break;
        }
    }

    private int GetBangLimit(PlayerState player)
    {
        if (player.Character.Name == "Уилли Кид") return int.MaxValue;
        if (player.InPlay.Any(c => c.Type == CardType.Volcanic)) return int.MaxValue;
        return 1;
    }

    private int GetWeaponRange(PlayerState player)
    {
        var weapon = player.InPlay.FirstOrDefault(c => c.Category == CardCategory.Weapon);
        if (weapon == null) return 1;
        return weapon.Type switch
        {
            CardType.Volcanic => 1,
            CardType.Schofield => 2,
            CardType.Remington => 3,
            CardType.RevCarabine => 4,
            CardType.Winchester => 5,
            _ => 1
        };
    }

    private int GetDistance(string fromId, string toId)
    {
        var aliveIds = _turnOrder.Where(id => _players[id].IsAlive).ToList();
        var fromIndex = aliveIds.IndexOf(fromId);
        var toIndex = aliveIds.IndexOf(toId);
        if (fromIndex == -1 || toIndex == -1) return int.MaxValue;

        var count = aliveIds.Count;
        var clockwise = (toIndex - fromIndex + count) % count;
        var counterClockwise = (fromIndex - toIndex + count) % count;
        var baseDistance = Math.Min(clockwise, counterClockwise);

        var target = _players[toId];
        var source = _players[fromId];

        if (target.InPlay.Any(c => c.Type == CardType.Mustang)) baseDistance += 1;
        if (target.Character.Name == "Пол Регрет") baseDistance += 1;

        if (source.InPlay.Any(c => c.Type == CardType.Scope)) baseDistance -= 1;
        if (source.Character.Name == "Роуз Дулан") baseDistance -= 1;

        return Math.Max(1, baseDistance);
    }

    private List<string> GetOtherAlivePlayersInTurnOrder(string excludePlayerId)
    {
        var result = new List<string>();
        var startIndex = (_turnIndex + 1) % _turnOrder.Count;
        for (var i = 0; i < _turnOrder.Count; i++)
        {
            var idx = (startIndex + i) % _turnOrder.Count;
            var id = _turnOrder[idx];
            if (id != excludePlayerId && _players[id].IsAlive)
            {
                result.Add(id);
            }
        }
        return result;
    }

    private List<string> GetAlivePlayersInTurnOrder(string startPlayerId)
    {
        var result = new List<string>();
        var startIndex = _turnOrder.IndexOf(startPlayerId);
        if (startIndex == -1) return result;
        for (var i = 0; i < _turnOrder.Count; i++)
        {
            var idx = (startIndex + i) % _turnOrder.Count;
            var id = _turnOrder[idx];
            if (_players[id].IsAlive)
            {
                result.Add(id);
            }
        }
        return result;
    }

    private void BuildDeck()
    {
        _drawPile.Clear();
        _discardPile.Clear();

        var suitValuePool = new List<(CardSuit Suit, int Value)>();
        for (var round = 0; round < 2; round++)
        {
            foreach (var suit in AllSuits)
            {
                for (var value = 2; value <= 14; value++)
                {
                    suitValuePool.Add((suit, value));
                }
            }
        }
        var shuffledSV = new Queue<(CardSuit, int)>(suitValuePool.OrderBy(_ => _random.Next()));

        var cards = new List<Card>();
        cards.AddRange(CreateCards(CardType.Bang, 22, shuffledSV));
        cards.AddRange(CreateCards(CardType.Missed, 12, shuffledSV));
        cards.AddRange(CreateCards(CardType.Beer, 6, shuffledSV));
        cards.AddRange(CreateCards(CardType.Gatling, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Stagecoach, 4, shuffledSV));
        cards.AddRange(CreateCards(CardType.CatBalou, 4, shuffledSV));
        cards.AddRange(CreateCards(CardType.Indians, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Duel, 3, shuffledSV));
        cards.AddRange(CreateCards(CardType.Panic, 4, shuffledSV));
        cards.AddRange(CreateCards(CardType.Saloon, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.WellsFargo, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.GeneralStore, 3, shuffledSV));
        cards.AddRange(CreateCards(CardType.Barrel, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Mustang, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Scope, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Volcanic, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Schofield, 3, shuffledSV));
        cards.AddRange(CreateCards(CardType.Remington, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.RevCarabine, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Winchester, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Jail, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Dynamite, 1, shuffledSV));

        foreach (var card in cards.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }
    }

    private void ShuffleDeck()
    {
        var cards = _drawPile.ToList();
        _drawPile.Clear();
        foreach (var card in cards.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }
    }

    private void DrawCards(PlayerState player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (_drawPile.Count == 0)
            {
                ReshuffleDiscardIntoDraw();
            }

            if (_drawPile.Count == 0)
            {
                break;
            }

            player.Hand.Add(_drawPile.Pop());
        }
    }

    private void ReshuffleDiscardIntoDraw()
    {
        if (_discardPile.Count == 0)
        {
            return;
        }

        foreach (var card in _discardPile.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }

        _discardPile.Clear();
    }

    private static string FormatCardValue(int value)
    {
        return value switch
        {
            11 => "J",
            12 => "Q",
            13 => "K",
            14 => "A",
            _ => value.ToString()
        };
    }

    private static string FormatCheckCard(Card card)
    {
        var suitSymbol = card.Suit switch
        {
            CardSuit.Spades => "\u2660",
            CardSuit.Hearts => "\u2665",
            CardSuit.Diamonds => "\u2666",
            CardSuit.Clubs => "\u2663",
            _ => "?"
        };
        return $"{FormatCardValue(card.Value)}{suitSymbol}";
    }

    private Card DrawCheckCard()
    {
        if (_drawPile.Count == 0) ReshuffleDiscardIntoDraw();
        if (_drawPile.Count == 0)
        {
            return new Card("Проверка", CardType.Bang, CardCategory.Brown, "", false, null, "", CardSuit.Clubs, 10);
        }
        var card = _drawPile.Pop();
        _discardPile.Add(card);
        return card;
    }

    private string? GetNextAlivePlayerId(string currentPlayerId)
    {
        var currentIndex = _turnOrder.IndexOf(currentPlayerId);
        if (currentIndex == -1) return null;
        for (var i = 1; i < _turnOrder.Count; i++)
        {
            var idx = (currentIndex + i) % _turnOrder.Count;
            var id = _turnOrder[idx];
            if (_players[id].IsAlive && id != currentPlayerId)
            {
                return id;
            }
        }
        return null;
    }

    private void CheckSuzyLafayette(PlayerState player)
    {
        if (player.Character.Name == "Сьюзи Лафайет" && player.IsAlive && player.Hand.Count == 0)
        {
            DrawCards(player, 1);
        }
    }

    private bool CheckBarrel(PlayerState target)
    {
        if (!target.InPlay.Any(c => c.Type == CardType.Barrel)) return false;

        if (target.Character.Name == "Лаки Дьюк")
        {
            var card1 = DrawCheckCard();
            var card2 = DrawCheckCard();
            var success = card1.Suit == CardSuit.Hearts || card2.Suit == CardSuit.Hearts;
            AddEvent($"Проверка Бочки! {target.Name} (Лаки Дьюк): {FormatCheckCard(card1)} и {FormatCheckCard(card2)} \u2014 {(success ? "увернулся!" : "не повезло.")}");
            return success;
        }

        var check = DrawCheckCard();
        var result = check.Suit == CardSuit.Hearts;
        AddEvent($"Проверка Бочки! {target.Name}: {FormatCheckCard(check)} \u2014 {(result ? "увернулся!" : "не повезло.")}");
        return result;
    }

    private string ResolveBang(PlayerState attacker, PlayerState target)
    {
        var damage = attacker.Character.Name == "Слэб Убийца" ? 2 : 1;

        if (CheckBarrel(target))
        {
            return $"{attacker.Name} стреляет в {target.Name}, но Бочка спасает {target.Name}!";
        }

        _pendingAction = new PendingAction(
            PendingActionType.BangDefense,
            attacker.Id,
            new[] { target.Id },
            damage);
        return $"{attacker.Name} стреляет в {target.Name}! {target.Name} должен ответить.";
    }

    private string ResolveBeer(PlayerState player)
    {
        if (player.Hp >= player.MaxHp)
        {
            return $"У {player.Name} уже максимальное здоровье.";
        }

        player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
        return $"{player.Name} выпивает Пиво и восстанавливает 1 ОЗ.";
    }

    private string ResolveGatling(PlayerState attacker)
    {
        var allResponders = GetOtherAlivePlayersInTurnOrder(attacker.Id);
        if (allResponders.Count == 0)
        {
            return $"{attacker.Name} стреляет из Гатлинга, но некого поразить.";
        }

        var barrelSaved = new List<string>();
        var needsResponse = new List<string>();
        foreach (var id in allResponders)
        {
            var p = _players[id];
            if (CheckBarrel(p))
            {
                barrelSaved.Add(p.Name);
            }
            else
            {
                needsResponse.Add(id);
            }
        }

        var barrelMsg = barrelSaved.Count > 0 ? $" {string.Join(", ", barrelSaved)} увернулись с помощью Бочки!" : "";

        if (needsResponse.Count == 0)
        {
            return $"{attacker.Name} стреляет из Гатлинга!{barrelMsg} Все в безопасности.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.GatlingDefense,
            attacker.Id,
            needsResponse);
        return $"{attacker.Name} стреляет из Гатлинга!{barrelMsg} Оставшиеся игроки должны сыграть Мимо! или получить 1 урон.";
    }

    private string ResolveStagecoach(PlayerState player)
    {
        DrawCards(player, 2);
        return $"{player.Name} играет Дилижанс и тянет 2 карты.";
    }

    private string ResolveCatBalou(PlayerState attacker, PlayerState target)
    {
        if (target.Hand.Count == 0 && target.InPlay.Count == 0)
        {
            return $"{target.Name} не имеет карт для сброса.";
        }

        if (target.Hand.Count > 0 && target.InPlay.Count > 0)
        {
            _pendingAction = new PendingAction(
                PendingActionType.ChooseStealSource,
                attacker.Id,
                new[] { attacker.Id });
            _pendingAction.StealTargetId = target.Id;
            _pendingAction.StealMode = "discard";
            _pendingAction.RevealedCards = target.InPlay.ToList();
            return $"{attacker.Name} использует Красотка против {target.Name}! Выберите: случайная карта из руки или снаряжение.";
        }

        if (target.Hand.Count > 0)
        {
            var idx = _random.Next(target.Hand.Count);
            var discarded = target.Hand[idx];
            target.Hand.RemoveAt(idx);
            _discardPile.Add(discarded);
            return $"{attacker.Name} использует Красотка против {target.Name} и сбрасывает {discarded.Name}.";
        }

        var equip = target.InPlay[_random.Next(target.InPlay.Count)];
        target.InPlay.Remove(equip);
        _discardPile.Add(equip);
        return $"{attacker.Name} использует Красотка против {target.Name} и сбрасывает {equip.Name}.";
    }

    private string ResolveIndians(PlayerState attacker)
    {
        var responders = GetOtherAlivePlayersInTurnOrder(attacker.Id);
        if (responders.Count == 0)
        {
            return $"{attacker.Name} играет Индейцы!, но некого атаковать.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.IndiansDefense,
            attacker.Id,
            responders);
        return $"{attacker.Name} играет Индейцы! Каждый должен сбросить Бэнг! или получить 1 урон.";
    }

    private string ResolveDuel(PlayerState attacker, PlayerState target)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DuelChallenge,
            attacker.Id,
            new[] { target.Id });
        _pendingAction.DuelPlayerA = attacker.Id;
        _pendingAction.DuelPlayerB = target.Id;
        return $"{attacker.Name} вызывает {target.Name} на дуэль!";
    }

    private string ResolvePanic(PlayerState attacker, PlayerState target)
    {
        if (target.Hand.Count == 0 && target.InPlay.Count == 0)
        {
            return $"{target.Name} не имеет карт для кражи.";
        }

        if (target.Hand.Count > 0 && target.InPlay.Count > 0)
        {
            _pendingAction = new PendingAction(
                PendingActionType.ChooseStealSource,
                attacker.Id,
                new[] { attacker.Id });
            _pendingAction.StealTargetId = target.Id;
            _pendingAction.StealMode = "steal";
            _pendingAction.RevealedCards = target.InPlay.ToList();
            return $"{attacker.Name} использует Паника! против {target.Name}! Выберите: случайная карта из руки или снаряжение.";
        }

        if (target.Hand.Count > 0)
        {
            var idx = _random.Next(target.Hand.Count);
            var stolen = target.Hand[idx];
            target.Hand.RemoveAt(idx);
            attacker.Hand.Add(stolen);
            return $"{attacker.Name} использует Паника! и крадёт карту из руки {target.Name}.";
        }

        var equip = target.InPlay[_random.Next(target.InPlay.Count)];
        target.InPlay.Remove(equip);
        attacker.Hand.Add(equip);
        return $"{attacker.Name} использует Паника! и крадёт {equip.Name} у {target.Name}.";
    }

    private string ResolveSaloon(PlayerState player)
    {
        foreach (var target in _players.Values)
        {
            if (!target.IsAlive)
            {
                continue;
            }

            target.Hp = Math.Min(target.Hp + 1, target.MaxHp);
        }

        return $"{player.Name} открывает Салун. Все восстанавливают 1 ОЗ.";
    }

    private string ResolveWellsFargo(PlayerState player)
    {
        DrawCards(player, 3);
        return $"{player.Name} грабит Уэллс Фарго и тянет 3 карты.";
    }

    private string ResolveGeneralStore(PlayerState player)
    {
        var alivePlayers = GetAlivePlayersInTurnOrder(player.Id);
        var cardCount = alivePlayers.Count;
        var revealedCards = new List<Card>();
        for (var i = 0; i < cardCount; i++)
        {
            if (_drawPile.Count == 0)
            {
                ReshuffleDiscardIntoDraw();
            }

            if (_drawPile.Count == 0)
            {
                break;
            }

            revealedCards.Add(_drawPile.Pop());
        }

        if (revealedCards.Count == 0)
        {
            return $"{player.Name} заходит в Магазин, но полки пусты.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.GeneralStorePick,
            player.Id,
            alivePlayers);
        _pendingAction.RevealedCards = revealedCards;
        return $"{player.Name} открывает Магазин! Каждый выбирает карту.";
    }

    private bool TryGetTarget(string? targetPublicId, string playerId, out PlayerState target, out string error)
    {
        target = null!;
        if (string.IsNullOrWhiteSpace(targetPublicId))
        {
            error = "Игрок-цель не найден.";
            return false;
        }

        var found = FindByPublicId(targetPublicId);
        if (found == null)
        {
            error = "Игрок-цель не найден.";
            return false;
        }
        target = found;

        if (target.Id == playerId)
        {
            error = "Нельзя выбирать себя в качестве цели.";
            return false;
        }

        if (!target.IsAlive)
        {
            error = $"{target.Name} уже выбыл.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void ApplyDamage(PlayerState attacker, PlayerState target, int damage, string verb)
    {
        target.Hp -= damage;
        if (target.Hp <= 0)
        {
            var aliveCount = _players.Values.Count(p => p.IsAlive);
            while (target.Hp <= 0 && aliveCount > 2)
            {
                var beerIndex = target.Hand.FindIndex(c => c.Type == CardType.Beer);
                if (beerIndex < 0) break;
                var beer = target.Hand[beerIndex];
                target.Hand.RemoveAt(beerIndex);
                _discardPile.Add(beer);
                target.Hp += 1;
                AddEvent($"{target.Name} использует Пиво, чтобы остаться в игре!");
            }

            if (target.Hp <= 0)
            {
                target.IsAlive = false;
                HandlePlayerDeath(attacker, target);
            }
        }

        if (target.IsAlive)
        {
            if (target.Character.Name == "Барт Кэссиди")
            {
                DrawCards(target, damage);
            }
            else if (target.Character.Name == "Эль Гринго" && attacker.Id != target.Id)
            {
                for (var i = 0; i < damage && attacker.Hand.Count > 0; i++)
                {
                    var idx = _random.Next(attacker.Hand.Count);
                    var stolen = attacker.Hand[idx];
                    attacker.Hand.RemoveAt(idx);
                    target.Hand.Add(stolen);
                }
            }
        }
    }

    private void HandlePlayerDeath(PlayerState killer, PlayerState dead)
    {
        var vultureSam = _players.Values.FirstOrDefault(p =>
            p.IsAlive && p.Id != dead.Id && p.Character.Name == "Валчер Сэм");

        if (vultureSam != null)
        {
            foreach (var card in dead.Hand)
            {
                vultureSam.Hand.Add(card);
            }
            foreach (var card in dead.InPlay)
            {
                vultureSam.Hand.Add(card);
            }
        }
        else
        {
            foreach (var card in dead.Hand)
            {
                _discardPile.Add(card);
            }
            foreach (var card in dead.InPlay)
            {
                _discardPile.Add(card);
            }
        }

        dead.Hand.Clear();
        dead.InPlay.Clear();

        if (dead.Role == Role.Bandit && killer.Id != dead.Id)
        {
            DrawCards(killer, 3);
            AddEvent($"{killer.Name} получает 3 карты за устранение Бандита.");
        }

        if (killer.Role == Role.Sheriff && dead.Role == Role.Deputy)
        {
            foreach (var card in killer.Hand)
            {
                _discardPile.Add(card);
            }
            foreach (var card in killer.InPlay)
            {
                _discardPile.Add(card);
            }
            killer.Hand.Clear();
            killer.InPlay.Clear();
            AddEvent($"{killer.Name} (Шериф) сбрасывает все карты за убийство Помощника!");
        }

        RemoveFromTurnOrder(dead.Id);
        CheckForGameOver();
    }

    private string FormatAttackMessage(PlayerState attacker, PlayerState target, string verb, int damage)
    {
        if (!target.IsAlive)
        {
            return $"{attacker.Name} {verb} {target.Name} на {damage} урона. {target.Name} выбыл!";
        }

        return $"{attacker.Name} {verb} {target.Name} на {damage} урона.";
    }

    private static readonly CardSuit[] AllSuits = { CardSuit.Spades, CardSuit.Hearts, CardSuit.Diamonds, CardSuit.Clubs };

    private IEnumerable<Card> CreateCards(CardType type, int count, Queue<(CardSuit Suit, int Value)> suitValues)
    {
        var definition = CardLibrary.Get(type);
        for (var i = 0; i < count; i++)
        {
            var sv = suitValues.Dequeue();
            yield return new Card(
                definition.Name,
                definition.Type,
                definition.Category,
                definition.Description,
                definition.RequiresTarget,
                definition.TargetHint,
                definition.ImagePath,
                sv.Suit,
                sv.Value);
        }
    }

    private void AssignRoles()
    {
        var roles = BuildRoleDeck(_players.Count);
        var shuffledPlayers = _players.Values.OrderBy(_ => _random.Next()).ToList();
        for (var i = 0; i < shuffledPlayers.Count; i++)
        {
            shuffledPlayers[i].AssignRole(roles[i]);
        }

        var sheriff = shuffledPlayers.FirstOrDefault(p => p.Role == Role.Sheriff);
        if (sheriff != null)
        {
            AddEvent($"{sheriff.Name} — Шериф.");
        }
    }

    private List<Role> BuildRoleDeck(int playerCount)
    {
        return playerCount switch
        {
            2 => new List<Role> { Role.Sheriff, Role.Bandit },
            3 => new List<Role> { Role.Sheriff, Role.Bandit, Role.Renegade },
            4 => new List<Role> { Role.Sheriff, Role.Bandit, Role.Bandit, Role.Renegade },
            5 => new List<Role> { Role.Sheriff, Role.Bandit, Role.Bandit, Role.Deputy, Role.Renegade },
            _ => new List<Role> { Role.Sheriff, Role.Bandit, Role.Bandit, Role.Bandit, Role.Deputy, Role.Renegade }
        };
    }

    private void RemoveFromTurnOrder(string playerId)
    {
        var index = _turnOrder.IndexOf(playerId);
        if (index == -1)
        {
            return;
        }

        _turnOrder.RemoveAt(index);
        if (_turnOrder.Count == 0)
        {
            _turnIndex = 0;
            return;
        }

        if (index < _turnIndex)
        {
            _turnIndex -= 1;
        }

        if (_turnIndex >= _turnOrder.Count)
        {
            _turnIndex = 0;
        }
    }

    private void CheckForGameOver()
    {
        if (GameOver)
        {
            return;
        }

        var alivePlayers = _players.Values.Where(p => p.IsAlive).ToList();
        var sheriffAlive = alivePlayers.Any(p => p.Role == Role.Sheriff);
        var banditsAlive = alivePlayers.Any(p => p.Role == Role.Bandit);
        var renegadeAlive = alivePlayers.Any(p => p.Role == Role.Renegade);

        if (!sheriffAlive)
        {
            if (alivePlayers.Count == 1 && renegadeAlive)
            {
                GameOver = true;
                WinnerMessage = "Ренегат побеждает, оставшись последним!";
                AddEvent(WinnerMessage);
                return;
            }

            GameOver = true;
            WinnerMessage = banditsAlive
                ? "Бандиты побеждают, устранив Шерифа!"
                : "Бандиты побеждают после гибели Шерифа.";
            AddEvent(WinnerMessage);
            return;
        }

        if (!banditsAlive && !renegadeAlive)
        {
            GameOver = true;
            WinnerMessage = "Шериф и помощники побеждают, очистив город от бандитов!";
            AddEvent(WinnerMessage);
        }
    }

    private bool IsRoleRevealed(PlayerState player, PlayerState viewer)
    {
        return player.Role == Role.Sheriff || !player.IsAlive || GameOver || player.Id == viewer.Id;
    }

    private string GetRoleNameForViewer(PlayerState player, PlayerState viewer)
    {
        return TranslateRole(IsRoleRevealed(player, viewer) ? player.Role.ToString() : "Неизвестно");
    }

    private static string TranslateRole(string role) => role switch
    {
        "Sheriff" => "Шериф",
        "Deputy" => "Помощник",
        "Bandit" => "Бандит",
        "Renegade" => "Ренегат",
        _ => "Неизвестно"
    };
}

class PlayerState
{
    public PlayerState(string id, string name, CharacterDefinition character)
    {
        Id = id;
        Name = name;
        Character = character;
        Role = Role.Unassigned;
        MaxHp = character.MaxHp;
        Hp = MaxHp;
    }

    public string Id { get; }
    public string PublicId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; private set; }
    public bool IsAlive { get; set; } = true;
    public Role Role { get; private set; }
    public CharacterDefinition Character { get; private set; }
    public List<Card> Hand { get; } = new();
    public List<Card> InPlay { get; } = new();
    public int BangsPlayedThisTurn { get; set; }

    public void ResetForNewGame()
    {
        MaxHp = Character.MaxHp + (Role == Role.Sheriff ? 1 : 0);
        Hp = MaxHp;
        IsAlive = true;
        Hand.Clear();
        InPlay.Clear();
        BangsPlayedThisTurn = 0;
    }

    public void ResetTurnFlags()
    {
        BangsPlayedThisTurn = 0;
    }

    public void AssignRole(Role role)
    {
        Role = role;
    }

    public void AssignCharacter(CharacterDefinition character)
    {
        Character = character;
    }
}

record Card(string Name, CardType Type, CardCategory Category, string Description, bool RequiresTarget, string? TargetHint, string ImagePath, CardSuit Suit, int Value);

record CardDefinition(string Name, CardType Type, CardCategory Category, string Description, bool RequiresTarget, string? TargetHint, string ImagePath);

enum CardType
{
    Bang,
    Missed,
    Beer,
    Gatling,
    Stagecoach,
    CatBalou,
    Indians,
    Duel,
    Panic,
    Saloon,
    WellsFargo,
    GeneralStore,
    Barrel,
    Mustang,
    Scope,
    Volcanic,
    Schofield,
    Remington,
    RevCarabine,
    Winchester,
    Jail,
    Dynamite
}

enum CardCategory
{
    Brown,
    Blue,
    Weapon
}

enum CardSuit
{
    Spades,
    Hearts,
    Diamonds,
    Clubs
}

enum Role
{
    Unassigned,
    Sheriff,
    Deputy,
    Bandit,
    Renegade
}

enum PendingActionType
{
    BangDefense,
    GatlingDefense,
    IndiansDefense,
    DuelChallenge,
    GeneralStorePick,
    DiscardExcess,
    ChooseStealSource,
    JesseJonesSteal,
    KitCarlsonPick
}

class PendingAction
{
    public PendingAction(PendingActionType type, string sourcePlayerId, IEnumerable<string> respondingPlayerIds, int damage = 1)
    {
        Type = type;
        SourcePlayerId = sourcePlayerId;
        RespondingPlayerIds = new Queue<string>(respondingPlayerIds);
        Damage = damage;
    }

    public PendingActionType Type { get; }
    public string SourcePlayerId { get; }
    public Queue<string> RespondingPlayerIds { get; }
    public int Damage { get; }
    public List<Card>? RevealedCards { get; set; }
    public string? DuelPlayerA { get; set; }
    public string? DuelPlayerB { get; set; }
    public string? StealTargetId { get; set; }
    public string? StealMode { get; set; }
    public int KitCarlsonPicksRemaining { get; set; }
}

static class CardLibrary
{
    private static readonly Dictionary<CardType, CardDefinition> Definitions = new()
    {
        {
            CardType.Bang,
            new CardDefinition("Бэнг!", CardType.Bang, CardCategory.Brown, "Нанесите 1 урон цели (2, если вы Слэб Убийца).", true, "Выберите игрока для выстрела", "/assets/cards/bang.png")
        },
        {
            CardType.Missed,
            new CardDefinition("Мимо!", CardType.Missed, CardCategory.Brown, "Сыграйте в ответ на выстрел, чтобы отменить урон.", false, null, "/assets/cards/missed.png")
        },
        {
            CardType.Beer,
            new CardDefinition("Пиво", CardType.Beer, CardCategory.Brown, "Восстановите 1 ОЗ.", false, null, "/assets/cards/beer.png")
        },
        {
            CardType.Gatling,
            new CardDefinition("Гатлинг", CardType.Gatling, CardCategory.Brown, "Каждый другой игрок должен сыграть Мимо! или получить 1 урон.", false, null, "/assets/cards/gatling.png")
        },
        {
            CardType.Stagecoach,
            new CardDefinition("Дилижанс", CardType.Stagecoach, CardCategory.Brown, "Доберите 2 карты.", false, null, "/assets/cards/stagecoach.png")
        },
        {
            CardType.CatBalou,
            new CardDefinition("Красотка", CardType.CatBalou, CardCategory.Brown, "Заставьте цель сбросить карту (рука или снаряжение).", true, "Выберите игрока для сброса", "/assets/cards/cat_balou.png")
        },
        {
            CardType.Indians,
            new CardDefinition("Индейцы!", CardType.Indians, CardCategory.Brown, "Каждый другой игрок должен сбросить Бэнг! или получить 1 урон.", false, null, "/assets/cards/indians.png")
        },
        {
            CardType.Duel,
            new CardDefinition("Дуэль", CardType.Duel, CardCategory.Brown, "Вызовите игрока на дуэль — по очереди сбрасывайте Бэнг!. Кто не сможет, получает 1 урон.", true, "Выберите соперника для дуэли", "/assets/cards/duel.png")
        },
        {
            CardType.Panic,
            new CardDefinition("Паника!", CardType.Panic, CardCategory.Brown, "Украдите карту у игрока на дистанции 1 (рука или снаряжение).", true, "Выберите игрока для кражи", "/assets/cards/panic.png")
        },
        {
            CardType.Saloon,
            new CardDefinition("Салун", CardType.Saloon, CardCategory.Brown, "Все живые игроки восстанавливают 1 ОЗ.", false, null, "/assets/cards/saloon.png")
        },
        {
            CardType.WellsFargo,
            new CardDefinition("Уэллс Фарго", CardType.WellsFargo, CardCategory.Brown, "Доберите 3 карты.", false, null, "/assets/cards/wells_fargo.png")
        },
        {
            CardType.GeneralStore,
            new CardDefinition("Магазин", CardType.GeneralStore, CardCategory.Brown, "Откройте карты по числу живых игроков. Каждый выбирает по очереди.", false, null, "/assets/cards/general_store.png")
        },
        {
            CardType.Barrel,
            new CardDefinition("Бочка", CardType.Barrel, CardCategory.Blue, "При выстреле выполните «проверку»: если червы, выстрел избегается.", false, null, "/assets/cards/barrel.png")
        },
        {
            CardType.Mustang,
            new CardDefinition("Мустанг", CardType.Mustang, CardCategory.Blue, "Другие видят вас на дистанции +1.", false, null, "/assets/cards/mustang.png")
        },
        {
            CardType.Scope,
            new CardDefinition("Прицел", CardType.Scope, CardCategory.Blue, "Вы видите других на дистанции -1.", false, null, "/assets/cards/scope.png")
        },
        {
            CardType.Volcanic,
            new CardDefinition("Вулканик", CardType.Volcanic, CardCategory.Weapon, "Оружие (дальность 1). Можно играть Бэнг! без ограничения за ход.", false, null, "/assets/cards/volcanic.png")
        },
        {
            CardType.Schofield,
            new CardDefinition("Скофилд", CardType.Schofield, CardCategory.Weapon, "Оружие (дальность 2).", false, null, "/assets/cards/schofield.png")
        },
        {
            CardType.Remington,
            new CardDefinition("Ремингтон", CardType.Remington, CardCategory.Weapon, "Оружие (дальность 3).", false, null, "/assets/cards/remington.png")
        },
        {
            CardType.RevCarabine,
            new CardDefinition("Карабин", CardType.RevCarabine, CardCategory.Weapon, "Оружие (дальность 4).", false, null, "/assets/cards/rev_carabine.png")
        },
        {
            CardType.Winchester,
            new CardDefinition("Винчестер", CardType.Winchester, CardCategory.Weapon, "Оружие (дальность 5).", false, null, "/assets/cards/winchester.png")
        },
        {
            CardType.Jail,
            new CardDefinition("Тюрьма", CardType.Jail, CardCategory.Blue, "Сыграйте на другого игрока. В начале хода он делает проверку — при неудаче ход пропускается.", true, "Выберите игрока для тюрьмы", "/assets/cards/jail.png")
        },
        {
            CardType.Dynamite,
            new CardDefinition("Динамит", CardType.Dynamite, CardCategory.Blue, "Сыграйте на себя. Переходит между игроками. Может взорваться и нанести 3 урона.", false, null, "/assets/cards/dynamite.png")
        }
    };

    public static CardDefinition Get(CardType type) => Definitions[type];
}

record CharacterDefinition(string Name, int MaxHp, string Description, string PortraitPath);

static class CharacterLibrary
{
    private static readonly List<CharacterDefinition> Characters = new()
    {
        new CharacterDefinition(
            "Лаки Дьюк",
            4,
            "При «проверке» откройте 2 карты и выберите лучший результат.",
            "/assets/characters/lucky_duke.png"),
        new CharacterDefinition(
            "Слэб Убийца",
            4,
            "Ваши Бэнг! наносят 2 урона.",
            "/assets/characters/slab_the_killer.png"),
        new CharacterDefinition(
            "Эль Гринго",
            3,
            "При получении урона возьмите карту из руки атакующего.",
            "/assets/characters/el_gringo.png"),
        new CharacterDefinition(
            "Сьюзи Лафайет",
            4,
            "Когда рука становится пустой, возьмите 1 карту.",
            "/assets/characters/suzy_lafayette.png"),
        new CharacterDefinition(
            "Роуз Дулан",
            4,
            "Встроенный Прицел: вы видите других на дистанции -1.",
            "/assets/characters/rose_doolan.png"),
        new CharacterDefinition(
            "Джесси Джонс",
            4,
            "Первую карту берите из руки выбранного игрока.",
            "/assets/characters/jesse_jones.png"),
        new CharacterDefinition(
            "Барт Кэссиди",
            4,
            "Каждый раз при получении урона берите 1 карту из колоды.",
            "/assets/characters/bart_cassidy.png"),
        new CharacterDefinition(
            "Пол Регрет",
            3,
            "Встроенный Мустанг: другие видят вас на дистанции +1.",
            "/assets/characters/paul_regret.png"),
        new CharacterDefinition(
            "Каламити Джанет",
            4,
            "Используйте Бэнг! как Мимо! и Мимо! как Бэнг!.",
            "/assets/characters/calamity_janet.png"),
        new CharacterDefinition(
            "Кит Карлсон",
            4,
            "Посмотрите 3 верхние карты, оставьте 2, 1 верните.",
            "/assets/characters/kit_carlson.png"),
        new CharacterDefinition(
            "Уилли Кид",
            4,
            "Можно играть Бэнг! без ограничения за ход.",
            "/assets/characters/willy_the_kid.png"),
        new CharacterDefinition(
            "Сид Кетчум",
            4,
            "Сбросьте 2 карты, чтобы восстановить 1 ОЗ (в свой ход).",
            "/assets/characters/sid_ketchum.png"),
        new CharacterDefinition(
            "Валчер Сэм",
            4,
            "Когда игрок устранён, вы забираете все его карты.",
            "/assets/characters/vulture_sam.png"),
        new CharacterDefinition(
            "Педро Рамирес",
            4,
            "Первую карту берите с верхней карты сброса.",
            "/assets/characters/pedro_ramirez.png")
    };

    public static CharacterDefinition Draw(Random random, HashSet<int> usedIndices)
    {
        var available = Enumerable.Range(0, Characters.Count).Where(i => !usedIndices.Contains(i)).ToList();
        if (available.Count == 0)
        {
            available = Enumerable.Range(0, Characters.Count).ToList();
            usedIndices.Clear();
        }

        var index = available[random.Next(available.Count)];
        usedIndices.Add(index);
        return Characters[index];
    }
}

class RoomManager
{
    private readonly Dictionary<string, GameState> _rooms = new();
    private readonly Dictionary<string, DateTime> _roomLastActivityUtc = new();
    private readonly Dictionary<string, string> _playerRoomMap = new();
    private readonly Dictionary<string, string> _sessionPlayerMap = new();
    private readonly Dictionary<string, string> _playerSessionMap = new();
    private readonly object _lock = new();
    private readonly Random _random = new();
    private const string RoomChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int MaxRooms = 50;

    public (string? RoomCode, GameState? Room) CreateRoom()
    {
        lock (_lock)
        {
            if (_rooms.Count >= MaxRooms) return (null, null);
            var code = GenerateRoomCode();
            if (code == null) return (null, null);
            var room = new GameState(code);
            _rooms[code] = room;
            _roomLastActivityUtc[code] = DateTime.UtcNow;
            return (code, room);
        }
    }

    public GameState? GetRoom(string code)
    {
        lock (_lock)
        {
            return _rooms.TryGetValue(code.ToUpperInvariant(), out var room) ? room : null;
        }
    }

    public GameState? GetRoomByPlayer(string playerId)
    {
        lock (_lock)
        {
            if (_playerRoomMap.TryGetValue(playerId, out var code) && _rooms.TryGetValue(code, out var room))
            {
                return room;
            }
            return null;
        }
    }

    public bool HasPlayer(string playerId)
    {
        lock (_lock)
        {
            if (_playerRoomMap.TryGetValue(playerId, out var code) && _rooms.TryGetValue(code, out var room))
            {
                if (room.HasPlayer(playerId)) return true;
                _playerRoomMap.Remove(playerId);
            }
            return false;
        }
    }

    private void TouchRoomInternal(string roomCode, DateTime now)
    {
        if (_rooms.ContainsKey(roomCode))
        {
            _roomLastActivityUtc[roomCode] = now;
        }
    }

    public void TouchRoomByPlayer(string playerId)
    {
        lock (_lock)
        {
            if (_playerRoomMap.TryGetValue(playerId, out var code))
            {
                TouchRoomInternal(code, DateTime.UtcNow);
            }
        }
    }

    public int CleanupInactiveRooms(TimeSpan idleThreshold)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var codes = _rooms.Keys.ToList();
            var removed = 0;
            foreach (var code in codes)
            {
                if (HasActiveConnectionsInternal(code)) continue;
                if (!_roomLastActivityUtc.TryGetValue(code, out var last))
                {
                    _roomLastActivityUtc[code] = now;
                    continue;
                }
                if (now - last < idleThreshold) continue;
                RemoveRoomInternal(code);
                removed++;
            }
            return removed;
        }
    }

    private bool HasActiveConnectionsInternal(string roomCode)
    {
        foreach (var entry in _playerRoomMap)
        {
            if (entry.Value == roomCode && _playerConnectionMap.ContainsKey(entry.Key))
            {
                return true;
            }
        }
        return false;
    }

    private void RemoveRoomInternal(string roomCode)
    {
        _rooms.Remove(roomCode);
        _roomLastActivityUtc.Remove(roomCode);
        var playerIds = _playerRoomMap.Where(kv => kv.Value == roomCode).Select(kv => kv.Key).ToList();
        foreach (var playerId in playerIds)
        {
            _playerRoomMap.Remove(playerId);
            _playerConnectionMap.Remove(playerId);
            if (_playerSessionMap.TryGetValue(playerId, out var sessionId))
            {
                _playerSessionMap.Remove(playerId);
                _sessionPlayerMap.Remove(sessionId);
            }
        }
    }

    public void RegisterPlayer(string playerId, string roomCode)
    {
        lock (_lock)
        {
            _playerRoomMap[playerId] = roomCode;
            TouchRoomInternal(roomCode, DateTime.UtcNow);
        }
    }

    public void UnregisterPlayer(string playerId)
    {
        lock (_lock)
        {
            if (_playerRoomMap.TryGetValue(playerId, out var code))
            {
                _playerRoomMap.Remove(playerId);
                if (_rooms.TryGetValue(code, out var room) && room.IsEmpty())
                {
                    TouchRoomInternal(code, DateTime.UtcNow);
                }
            }
        }
    }

    public string CreateSession(string playerId)
    {
        lock (_lock)
        {
            ClearSessionForPlayer(playerId);
            var sessionId = Guid.NewGuid().ToString("N");
            _sessionPlayerMap[sessionId] = playerId;
            _playerSessionMap[playerId] = sessionId;
            return sessionId;
        }
    }

    public string? GetPlayerIdBySession(string sessionId)
    {
        lock (_lock)
        {
            return _sessionPlayerMap.TryGetValue(sessionId, out var playerId) ? playerId : null;
        }
    }

    public void ClearSessionForPlayer(string playerId)
    {
        lock (_lock)
        {
            if (_playerSessionMap.TryGetValue(playerId, out var sessionId))
            {
                _playerSessionMap.Remove(playerId);
                _sessionPlayerMap.Remove(sessionId);
            }
        }
    }

    public List<RoomInfo> ListRooms()
    {
        lock (_lock)
        {
            return _rooms.Values.Select(r => r.GetRoomInfo()).ToList();
        }
    }

    private readonly Dictionary<string, string> _playerConnectionMap = new();

    public void SetConnection(string playerId, string connectionId)
    {
        lock (_lock)
        {
            _playerConnectionMap[playerId] = connectionId;
            if (_playerRoomMap.TryGetValue(playerId, out var code))
            {
                TouchRoomInternal(code, DateTime.UtcNow);
            }
        }
    }

    public void RemoveConnection(string connectionId)
    {
        lock (_lock)
        {
            var key = _playerConnectionMap.FirstOrDefault(kv => kv.Value == connectionId).Key;
            if (key == null) return;
            _playerConnectionMap.Remove(key);
            if (_playerRoomMap.TryGetValue(key, out var code))
            {
                TouchRoomInternal(code, DateTime.UtcNow);
            }
        }
    }

    public string? GetPlayerIdByConnection(string connectionId)
    {
        lock (_lock)
        {
            var entry = _playerConnectionMap.FirstOrDefault(kv => kv.Value == connectionId);
            return entry.Key;
        }
    }

    public string? GetConnectionId(string playerId)
    {
        lock (_lock) { return _playerConnectionMap.TryGetValue(playerId, out var cid) ? cid : null; }
    }

    public List<string> GetAllPlayerIdsInRoom(string roomCode)
    {
        lock (_lock)
        {
            return _playerRoomMap.Where(kv => kv.Value == roomCode).Select(kv => kv.Key).ToList();
        }
    }

    private string? GenerateRoomCode()
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var code = new string(Enumerable.Range(0, 4).Select(_ => RoomChars[_random.Next(RoomChars.Length)]).ToArray());
            if (!_rooms.ContainsKey(code)) return code;
        }
        return null;
    }
}

class RoomCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RoomIdleTimeout = TimeSpan.FromMinutes(2);
    private readonly RoomManager _rooms;
    private readonly IHubContext<GameHub> _hub;

    public RoomCleanupService(RoomManager rooms, IHubContext<GameHub> hub)
    {
        _rooms = rooms;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var removed = _rooms.CleanupInactiveRooms(RoomIdleTimeout);
            if (removed > 0)
            {
                await _hub.Clients.Group("lobby").SendAsync("RoomsUpdated", _rooms.ListRooms(), cancellationToken: stoppingToken);
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}

class GameHub : Hub
{
    private const string SessionCookieName = "bang_session";
    private readonly RoomManager _rooms;

    public GameHub(RoomManager rooms) { _rooms = rooms; }

    public async Task Register()
    {
        var http = Context.GetHttpContext();
        if (http == null) return;
        if (!http.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId)) return;
        var playerId = _rooms.GetPlayerIdBySession(sessionId);
        if (playerId == null) return;
        if (_rooms.GetRoomByPlayer(playerId) == null) return;
        _rooms.SetConnection(playerId, Context.ConnectionId);
        var game = _rooms.GetRoomByPlayer(playerId);
        if (game != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, game.RoomCode);
        }
    }

    public async Task JoinRoom(string roomCode)
    {
        if (roomCode == "lobby")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
            return;
        }
        var connPlayerId = _rooms.GetPlayerIdByConnection(Context.ConnectionId);
        if (connPlayerId == null) return;
        var game = _rooms.GetRoomByPlayer(connPlayerId);
        if (game == null || game.RoomCode != roomCode) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
    }

    public async Task LeaveRoom(string roomCode)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _rooms.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}








