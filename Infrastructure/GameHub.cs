using Microsoft.AspNetCore.SignalR;

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
