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
        policy.SetIsOriginAllowed(_ => true)
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

    var result = game.UseAbility(playerId, request.CardIndices, request.TargetId);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/usegreen", async (UseGreenRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (game.IsSpectator(playerId)) return Results.BadRequest(new ApiResponse(null, "Зрители не могут использовать карты."));

    var result = game.UseGreenCard(playerId, request.CardIndex, request.TargetId);
    if (!result.Success) return Results.BadRequest(new ApiResponse(null, result.Message));
    await BroadcastState(hub, game, rooms);
    return Results.Ok(new ApiResponse(null, result.Message));
}).RequireRateLimiting("general");

app.MapPost("/api/settings", async (SettingsRequest request, RoomManager rooms, IHubContext<GameHub> hub, HttpContext http) =>
{
    var playerId = RequirePlayerId(http, rooms, out var authError);
    if (playerId == null) return authError!;

    var game = rooms.GetRoomByPlayer(playerId);
    if (game is null) return Results.BadRequest(new ApiResponse(null, "Вы не в комнате."));
    if (!game.IsHost(playerId)) return Results.BadRequest(new ApiResponse(null, "Только хост может менять настройки."));

    var expansions = Expansion.None;
    if (request.DodgeCity) expansions |= Expansion.DodgeCity;
    if (request.HighNoon) expansions |= Expansion.HighNoon;
    if (request.FistfulOfCards) expansions |= Expansion.FistfulOfCards;

    var result = game.UpdateSettings(playerId, expansions);
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
