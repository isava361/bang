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
