record PlayRequest(int CardIndex, string? TargetId);
record RespondRequest(string ResponseType, int? CardIndex, string? TargetId);
record ChatRequest(string Text);
record AbilityRequest(int[] CardIndices, string? TargetId = null);
record ApiResponse(object? Data, string Message);
record RoomInfo(string RoomCode, int PlayerCount, int SpectatorCount, bool Started, bool GameOver, string StatusText);
record JoinRoomRequest(string Name, string RoomCode);
record CreateRoomResponse(string RoomCode);
record RenameRequest(string NewName);
record UseGreenRequest(int CardIndex, string? TargetId);
record SettingsRequest(bool DodgeCity, bool HighNoon, bool FistfulOfCards);
record GameSettings(bool DodgeCity, bool HighNoon, bool FistfulOfCards);

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
    string? YourPublicId = null,
    GameSettings? Settings = null,
    string? CurrentEventName = null,
    string? CurrentEventDescription = null
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
    List<CardView> Equipment,
    List<CardView>? RevealedHand = null
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
    int Value,
    bool IsFresh = false
);

record CommandResult(bool Success, string Message, GameStateView? State = null, string? PlayerId = null);
