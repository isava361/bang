using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<GameState>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    app.Urls.Add("http://0.0.0.0:5000");
}

app.MapPost("/api/join", (JoinRequest request, GameState game) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new ApiResponse(null, "Name is required."));
    }

    var result = game.TryAddPlayer(request.Name.Trim());
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    var state = game.ToView(result.PlayerId!);
    return Results.Ok(new ApiResponse(new JoinResponse(result.PlayerId!, state), result.Message));
});

app.MapPost("/api/start", (PlayerRequest request, GameState game) =>
{
    var result = game.StartGame(request.PlayerId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/play", (PlayRequest request, GameState game) =>
{
    var result = game.PlayCard(request.PlayerId, request.CardIndex, request.TargetId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/respond", (RespondRequest request, GameState game) =>
{
    var result = game.Respond(request.PlayerId, request.ResponseType, request.CardIndex, request.TargetId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/end", (PlayerRequest request, GameState game) =>
{
    var result = game.EndTurn(request.PlayerId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/chat", (ChatRequest request, GameState game) =>
{
    var result = game.AddChat(request.PlayerId, request.Text);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/ability", (AbilityRequest request, GameState game) =>
{
    var result = game.UseAbility(request.PlayerId, request.CardIndices);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/newgame", (PlayerRequest request, GameState game) =>
{
    var result = game.ResetGame(request.PlayerId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapGet("/api/reconnect", (string playerId, GameState game) =>
{
    var state = game.ToView(playerId);
    if (state is null)
    {
        return Results.BadRequest(new ApiResponse(null, "Unknown player."));
    }

    return Results.Ok(new ApiResponse(state, "OK"));
});

app.MapGet("/api/state", (string playerId, GameState game) =>
{
    var state = game.ToView(playerId);
    if (state is null)
    {
        return Results.BadRequest(new ApiResponse(null, "Unknown player."));
    }

    return Results.Ok(new ApiResponse(state, "OK"));
});

app.Run();

record JoinRequest(string Name);
record PlayerRequest(string PlayerId);
record PlayRequest(string PlayerId, int CardIndex, string? TargetId);
record RespondRequest(string PlayerId, string ResponseType, int? CardIndex, string? TargetId);
record ChatRequest(string PlayerId, string Text);
record AbilityRequest(string PlayerId, int[] CardIndices);
record JoinResponse(string PlayerId, GameStateView State);
record ApiResponse(object? Data, string Message);

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
    Dictionary<string, int>? Distances
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
    private const int StartingHand = 4;
    private readonly Dictionary<string, PlayerState> _players = new();
    private readonly List<string> _turnOrder = new();
    private readonly Random _random = new();
    private readonly Stack<Card> _drawPile = new();
    private readonly List<Card> _discardPile = new();
    private readonly object _lock = new();
    private readonly HashSet<int> _usedCharacterIndices = new();
    private PendingAction? _pendingAction;
    private int _turnIndex;

    private readonly List<string> _eventLog = new();
    private readonly List<string> _chatLog = new();

    public bool Started { get; private set; }
    public bool GameOver { get; private set; }
    public string? WinnerMessage { get; private set; }

    private void AddEvent(string msg)
    {
        _eventLog.Insert(0, msg);
        if (_eventLog.Count > 20) _eventLog.RemoveAt(_eventLog.Count - 1);
    }

    public CommandResult TryAddPlayer(string name)
    {
        lock (_lock)
        {
            if (Started)
            {
                return new CommandResult(false, "Game already started. Wait for the next round.");
            }

            if (_players.Count >= MaxPlayers)
            {
                return new CommandResult(false, "Room is full.");
            }

            var id = Guid.NewGuid().ToString("N");
            var character = CharacterLibrary.Draw(_random, _usedCharacterIndices);
            var player = new PlayerState(id, name, character);
            _players[id] = player;
            _turnOrder.Add(id);
            AddEvent($"{name} joined as {character.Name}.");
            return new CommandResult(true, "Joined room.", PlayerId: id);
        }
    }

    public CommandResult StartGame(string playerId)
    {
        lock (_lock)
        {
            if (!_players.ContainsKey(playerId))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (Started && !GameOver)
            {
                return new CommandResult(false, "Game already started.");
            }

            if (_players.Count < 2)
            {
                return new CommandResult(false, "Need at least 2 players to start.");
            }

            _turnOrder.Clear();
            _turnOrder.AddRange(_players.Values.OrderBy(p => p.Name).Select(p => p.Id));
            _usedCharacterIndices.Clear();
            _pendingAction = null;
            _eventLog.Clear();
            _chatLog.Clear();
            Started = true;
            GameOver = false;
            WinnerMessage = null;
            BuildDeck();
            ShuffleDeck();

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
                AddEvent($"Game started! {current.Name} takes the first turn as Sheriff.");
            }

            return new CommandResult(true, "Game started.", ToView(playerId));
        }
    }

    public CommandResult PlayCard(string playerId, int index, string? targetId)
    {
        lock (_lock)
        {
            if (!Started)
            {
                return new CommandResult(false, "Game has not started.");
            }

            if (GameOver)
            {
                return new CommandResult(false, "Game is over. Start a new round to play again.");
            }

            if (_pendingAction != null)
            {
                return new CommandResult(false, "Waiting for a player to respond.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Not your turn.");
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (!player.IsAlive)
            {
                return new CommandResult(false, "You are out of the game.");
            }

            if (index < 0 || index >= player.Hand.Count)
            {
                return new CommandResult(false, "Card index out of range.");
            }

            var card = player.Hand[index];
            var isCalamityJanet = player.Character.Name == "Calamity Janet";
            if (card.Type == CardType.Missed && !isCalamityJanet)
            {
                return new CommandResult(false, "Missed! can only be played as a response to being shot.");
            }

            if (card.Type == CardType.Beer && _players.Values.Count(p => p.IsAlive) <= 2)
            {
                return new CommandResult(false, "Beer cannot be used when 2 or fewer players remain.");
            }

            var effectiveType = card.Type;
            if (isCalamityJanet && card.Type == CardType.Missed)
            {
                effectiveType = CardType.Bang;
            }

            if (effectiveType == CardType.Bang && player.BangsPlayedThisTurn >= GetBangLimit(player))
            {
                var limit = GetBangLimit(player);
                return new CommandResult(false, $"You can only play {limit} Bang! each turn.");
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
                    return new CommandResult(false, $"{target.Name} is out of range (distance {distance}, weapon range {GetWeaponRange(player)}).");
                }
                if (effectiveType == CardType.Panic && distance > 1)
                {
                    return new CommandResult(false, $"{target.Name} is out of range for Panic! (distance {distance}, need 1).");
                }
            }

            if (card.Type == CardType.Jail)
            {
                if (target == null)
                {
                    return new CommandResult(false, "Choose a player to jail.");
                }
                if (target.Role == Role.Sheriff)
                {
                    return new CommandResult(false, "The Sheriff cannot be jailed.");
                }
                if (target.InPlay.Any(c => c.Type == CardType.Jail))
                {
                    return new CommandResult(false, $"{target.Name} is already in Jail.");
                }
                player.Hand.RemoveAt(index);
                target.InPlay.Add(card);
                var jailMsg = $"{player.Name} throws {target.Name} in Jail!";
                AddEvent(jailMsg);
                return new CommandResult(true, jailMsg, ToView(playerId));
            }

            if (card.Type == CardType.Dynamite)
            {
                if (player.InPlay.Any(c => c.Type == CardType.Dynamite))
                {
                    return new CommandResult(false, "You already have Dynamite in play.");
                }
                player.Hand.RemoveAt(index);
                player.InPlay.Add(card);
                var dynMsg = $"{player.Name} plays Dynamite!";
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
                var equipMsg = $"{player.Name} equips {card.Name}.";
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
                _ => "Card had no effect."
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
                return new CommandResult(false, "Game has not started.");
            }

            if (GameOver)
            {
                return new CommandResult(false, "Game is over. Start a new round to play again.");
            }

            if (_pendingAction != null)
            {
                return new CommandResult(false, "Waiting for a player to respond.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Not your turn.");
            }

            var endingPlayer = _players[_turnOrder[_turnIndex]];
            if (!endingPlayer.IsAlive)
            {
                return new CommandResult(false, "You are out of the game.");
            }

            CheckSuzyLafayette(endingPlayer);

            if (endingPlayer.Hand.Count > endingPlayer.Hp)
            {
                _pendingAction = new PendingAction(
                    PendingActionType.DiscardExcess,
                    endingPlayer.Id,
                    new[] { endingPlayer.Id });
                var excess = endingPlayer.Hand.Count - endingPlayer.Hp;
                var discardMsg = $"{endingPlayer.Name} must discard {excess} card(s) to match hand limit.";
                AddEvent(discardMsg);
                return new CommandResult(true, discardMsg, ToView(playerId));
            }

            AdvanceTurn();
            return new CommandResult(true, "Turn ended.", ToView(playerId));
        }
    }

    public CommandResult AddChat(string playerId, string text)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new CommandResult(false, "Chat message cannot be empty.");
            }

            var chatMsg = $"{player.Name}: {text.Trim()}";
            _chatLog.Insert(0, chatMsg);
            if (_chatLog.Count > 30) _chatLog.RemoveAt(_chatLog.Count - 1);
            return new CommandResult(true, "Chat sent.", ToView(playerId));
        }
    }

    public CommandResult Respond(string playerId, string responseType, int? cardIndex, string? targetId = null)
    {
        lock (_lock)
        {
            if (GameOver)
            {
                _pendingAction = null;
                return new CommandResult(false, "Game is over.");
            }

            if (_pendingAction == null)
            {
                return new CommandResult(false, "No action to respond to.");
            }

            if (_pendingAction.RespondingPlayerIds.Count == 0)
            {
                _pendingAction = null;
                return new CommandResult(false, "No action to respond to.");
            }

            var currentResponderId = _pendingAction.RespondingPlayerIds.Peek();
            if (currentResponderId != playerId)
            {
                return new CommandResult(false, "It's not your turn to respond.");
            }

            if (!_players.TryGetValue(playerId, out var responder))
            {
                return new CommandResult(false, "Unknown player.");
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
                            return new CommandResult(false, "Invalid card index.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.Character.Name == "Calamity Janet";
                        if (card.Type != CardType.Missed && !(isJanet && card.Type == CardType.Bang))
                        {
                            return new CommandResult(false, isJanet
                                ? "You must play a Missed! or Bang! card to dodge."
                                : "You must play a Missed! card to dodge.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        message = $"{responder.Name} plays {card.Name} and dodges the shot!";
                    }
                    else
                    {
                        var damage = _pendingAction.Damage;
                        ApplyDamage(source ?? responder, responder, damage, "shot");
                        message = FormatAttackMessage(source ?? responder, responder, "shot", damage);
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
                            return new CommandResult(false, "Invalid card index.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.Character.Name == "Calamity Janet";
                        if (card.Type != CardType.Bang && !(isJanet && card.Type == CardType.Missed))
                        {
                            return new CommandResult(false, isJanet
                                ? "You must discard a Bang! or Missed! card to avoid the Indians."
                                : "You must discard a Bang! card to avoid the Indians.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        message = $"{responder.Name} discards {card.Name} to fend off the Indians!";
                    }
                    else
                    {
                        if (source != null)
                        {
                            ApplyDamage(source, responder, 1, "ambushed");
                        }
                        message = $"{responder.Name} is ambushed by the Indians and takes 1 damage.";
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
                            return new CommandResult(false, "Invalid card index.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.Character.Name == "Calamity Janet";
                        if (card.Type != CardType.Bang && !(isJanet && card.Type == CardType.Missed))
                        {
                            return new CommandResult(false, isJanet
                                ? "You must play a Bang! or Missed! card to continue the duel."
                                : "You must play a Bang! card to continue the duel.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);

                        _pendingAction.RespondingPlayerIds.Dequeue();
                        _pendingAction.RespondingPlayerIds.Enqueue(opponentId);
                        message = $"{responder.Name} fires back in the duel! {opponent.Name} must respond.";
                    }
                    else
                    {
                        ApplyDamage(opponent, responder, 1, "lost the duel against");
                        message = $"{responder.Name} cannot continue the duel and takes 1 damage!";
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
                        message = "No more cards to pick.";
                        break;
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                    {
                        return new CommandResult(false, "Invalid card selection.");
                    }

                    var pickedCard = _pendingAction.RevealedCards[cardIndex.Value];
                    _pendingAction.RevealedCards.RemoveAt(cardIndex.Value);
                    responder.Hand.Add(pickedCard);
                    message = $"{responder.Name} picks {pickedCard.Name} from the General Store.";

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
                        message = "Target no longer exists.";
                        break;
                    }

                    var isSteal = _pendingAction.StealMode == "steal";

                    if (responseType == "hand")
                    {
                        if (stealTarget.Hand.Count == 0)
                        {
                            message = $"{stealTarget.Name} has no hand cards left.";
                            _pendingAction = null;
                            break;
                        }
                        var idx = _random.Next(stealTarget.Hand.Count);
                        var card = stealTarget.Hand[idx];
                        stealTarget.Hand.RemoveAt(idx);
                        if (isSteal)
                        {
                            responder.Hand.Add(card);
                            message = $"{responder.Name} steals {card.Name} from {stealTarget.Name}'s hand.";
                        }
                        else
                        {
                            _discardPile.Add(card);
                            message = $"{responder.Name} discards {card.Name} from {stealTarget.Name}'s hand.";
                        }
                    }
                    else if (responseType == "equipment")
                    {
                        if (cardIndex == null || _pendingAction.RevealedCards == null ||
                            cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                        {
                            return new CommandResult(false, "Invalid equipment selection.");
                        }
                        var equipCard = _pendingAction.RevealedCards[cardIndex.Value];
                        stealTarget.InPlay.Remove(equipCard);
                        if (isSteal)
                        {
                            responder.Hand.Add(equipCard);
                            message = $"{responder.Name} steals {equipCard.Name} from {stealTarget.Name}.";
                        }
                        else
                        {
                            _discardPile.Add(equipCard);
                            message = $"{responder.Name} discards {equipCard.Name} from {stealTarget.Name}.";
                        }
                    }
                    else
                    {
                        return new CommandResult(false, "Choose 'hand' or 'equipment'.");
                    }

                    _pendingAction = null;
                    break;
                }

                case PendingActionType.DiscardExcess:
                {
                    if (responseType != "play_card")
                    {
                        return new CommandResult(false, "You must discard a card.");
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                    {
                        return new CommandResult(false, "Invalid card index.");
                    }

                    var card = responder.Hand[cardIndex.Value];
                    responder.Hand.RemoveAt(cardIndex.Value);
                    _discardPile.Add(card);

                    if (responder.Hand.Count <= responder.Hp)
                    {
                        message = $"{responder.Name} discards {card.Name}. Hand limit reached.";
                        _pendingAction = null;
                        AdvanceTurn();
                    }
                    else
                    {
                        var remaining = responder.Hand.Count - responder.Hp;
                        message = $"{responder.Name} discards {card.Name}. {remaining} more to discard.";
                    }
                    break;
                }

                case PendingActionType.JesseJonesSteal:
                {
                    if (string.IsNullOrWhiteSpace(targetId) || !_players.TryGetValue(targetId, out var jesseTarget))
                    {
                        return new CommandResult(false, "Choose a player to draw from.");
                    }
                    if (!jesseTarget.IsAlive || jesseTarget.Hand.Count == 0)
                    {
                        return new CommandResult(false, $"{jesseTarget.Name} has no cards to draw.");
                    }
                    if (jesseTarget.Id == playerId)
                    {
                        return new CommandResult(false, "You cannot draw from yourself.");
                    }

                    var stealIdx = _random.Next(jesseTarget.Hand.Count);
                    var stolenCard = jesseTarget.Hand[stealIdx];
                    jesseTarget.Hand.RemoveAt(stealIdx);
                    responder.Hand.Add(stolenCard);
                    DrawCards(responder, 1);
                    _pendingAction = null;
                    message = $"{responder.Name} draws a card from {jesseTarget.Name} and 1 from the deck.";
                    break;
                }

                case PendingActionType.KitCarlsonPick:
                {
                    if (_pendingAction.RevealedCards == null || _pendingAction.RevealedCards.Count == 0)
                    {
                        _pendingAction = null;
                        message = "No more cards to pick.";
                        break;
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                    {
                        return new CommandResult(false, "Invalid card selection.");
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
                        message = $"{responder.Name} picks {picked.Name} and finishes drawing.";
                    }
                    else
                    {
                        message = $"{responder.Name} picks {picked.Name}. {_pendingAction.KitCarlsonPicksRemaining} more to pick.";
                    }
                    break;
                }

                default:
                    return new CommandResult(false, "Unknown action type.");
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
                return new CommandResult(false, "Game is not active.");
            }

            if (_pendingAction != null)
            {
                return new CommandResult(false, "Waiting for a player to respond.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Not your turn.");
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (player.Character.Name != "Sid Ketchum")
            {
                return new CommandResult(false, "Your character has no active ability.");
            }

            if (cardIndices == null || cardIndices.Length != 2)
            {
                return new CommandResult(false, "You must select exactly 2 cards to discard.");
            }

            if (player.Hp >= player.MaxHp)
            {
                return new CommandResult(false, "You are already at full health.");
            }

            if (player.Hand.Count < 2)
            {
                return new CommandResult(false, "You need at least 2 cards to use this ability.");
            }

            var sorted = cardIndices.OrderByDescending(i => i).ToArray();
            if (sorted.Any(i => i < 0 || i >= player.Hand.Count) || sorted[0] == sorted[1])
            {
                return new CommandResult(false, "Invalid card selection.");
            }

            var card1 = player.Hand[sorted[0]];
            var card2 = player.Hand[sorted[1]];
            player.Hand.RemoveAt(sorted[0]);
            player.Hand.RemoveAt(sorted[1]);
            _discardPile.Add(card1);
            _discardPile.Add(card2);

            player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
            var message = $"{player.Name} discards {card1.Name} and {card2.Name} to heal 1 HP.";
            AddEvent(message);
            CheckSuzyLafayette(player);
            return new CommandResult(true, message, ToView(playerId));
        }
    }

    public CommandResult ResetGame(string playerId)
    {
        lock (_lock)
        {
            if (!_players.ContainsKey(playerId))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (!GameOver)
            {
                return new CommandResult(false, "Game is not over yet.");
            }

            if (_players.Count < 2)
            {
                return new CommandResult(false, "Need at least 2 players to start a new game.");
            }

            _turnOrder.Clear();
            _turnOrder.AddRange(_players.Values.OrderBy(p => p.Name).Select(p => p.Id));
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
                AddEvent($"New game started! {current.Name} takes the first turn as Sheriff.");
            }

            return new CommandResult(true, "New game started.", ToView(playerId));
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

            var currentId = _turnOrder.Count > 0 ? _turnOrder[_turnIndex] : "-";
            var currentName = _players.TryGetValue(currentId, out var current) ? current.Name : "-";
            var players = _players.Values
                .Select(p => new PlayerView(
                    p.Id,
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
                .OrderBy(p => p.Name)
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
                    PendingActionType.BangDefense => $"Play a Missed! to dodge, or take {_pendingAction.Damage} damage.",
                    PendingActionType.GatlingDefense => "Play a Missed! to dodge the Gatling, or take 1 damage.",
                    PendingActionType.IndiansDefense => "Discard a Bang! to avoid the Indians, or take 1 damage.",
                    PendingActionType.DuelChallenge => "Play a Bang! to continue the duel, or take 1 damage.",
                    PendingActionType.GeneralStorePick => "Pick a card from the General Store.",
                    PendingActionType.DiscardExcess => $"Discard down to {responder.Hp} cards ({responder.Hand.Count - responder.Hp} more to discard).",
                    PendingActionType.ChooseStealSource => $"Choose to take a random hand card or a specific equipment piece.",
                    PendingActionType.JesseJonesSteal => "Choose a player to draw a card from.",
                    PendingActionType.KitCarlsonPick => $"Pick a card to keep ({_pendingAction.KitCarlsonPicksRemaining} remaining).",
                    _ => "Respond to the action."
                };

                List<CardView>? revealedCards = null;
                if (_pendingAction.RevealedCards != null)
                {
                    revealedCards = _pendingAction.RevealedCards
                        .Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value))
                        .ToList();
                }

                pendingView = new PendingActionView(
                    _pendingAction.Type.ToString(),
                    responderId,
                    responder.Name,
                    message,
                    revealedCards);
            }

            var weaponRange = GetWeaponRange(viewer);
            Dictionary<string, int>? distances = null;
            if (Started && !GameOver)
            {
                distances = new Dictionary<string, int>();
                foreach (var p in _players.Values)
                {
                    if (p.Id != viewer.Id && p.IsAlive)
                    {
                        distances[p.Id] = GetDistance(viewer.Id, p.Id);
                    }
                }
            }

            return new GameStateView(
                Started,
                currentId,
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
                distances);
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
            bool isLuckyDuke = current.Character.Name == "Lucky Duke";
            bool explodes;

            if (isLuckyDuke)
            {
                var card1 = DrawCheckCard();
                var card2 = DrawCheckCard();
                bool e1 = card1.Suit == CardSuit.Spades && card1.Value >= 2 && card1.Value <= 9;
                bool e2 = card2.Suit == CardSuit.Spades && card2.Value >= 2 && card2.Value <= 9;
                explodes = e1 && e2;
                AddEvent($"Dynamite draw! {current.Name} (Lucky Duke): {FormatCheckCard(card1)} and {FormatCheckCard(card2)}");
            }
            else
            {
                var check = DrawCheckCard();
                explodes = check.Suit == CardSuit.Spades && check.Value >= 2 && check.Value <= 9;
                AddEvent($"Dynamite draw! {current.Name}: {FormatCheckCard(check)}");
            }

            if (explodes)
            {
                _discardPile.Add(dynamite);
                AddEvent($"BOOM! Dynamite explodes on {current.Name} for 3 damage!");
                ApplyDamage(current, current, 3, "blown up by Dynamite");
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
                    AddEvent($"Dynamite fizzles and passes to {nextPlayer.Name}.");
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
            bool isLuckyDuke = current.Character.Name == "Lucky Duke";
            bool escapes;

            if (isLuckyDuke)
            {
                var card1 = DrawCheckCard();
                var card2 = DrawCheckCard();
                escapes = card1.Suit == CardSuit.Hearts || card2.Suit == CardSuit.Hearts;
                AddEvent($"Jail draw! {current.Name} (Lucky Duke): {FormatCheckCard(card1)} and {FormatCheckCard(card2)}");
            }
            else
            {
                var check = DrawCheckCard();
                escapes = check.Suit == CardSuit.Hearts;
                AddEvent($"Jail draw! {current.Name}: {FormatCheckCard(check)}");
            }

            if (escapes)
            {
                AddEvent($"{current.Name} breaks out of Jail!");
            }
            else
            {
                AddEvent($"{current.Name} stays in Jail. Turn skipped.");
                AdvanceTurn();
                return;
            }
        }

        // 3. Normal draw phase
        if (current.IsAlive && !GameOver)
        {
            AddEvent($"{current.Name}'s turn begins.");
            HandleDrawPhase(current);
        }
    }

    private void HandleDrawPhase(PlayerState player)
    {
        switch (player.Character.Name)
        {
            case "Jesse Jones":
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
                    AddEvent($"{player.Name}'s turn begins. Choose a player to draw a card from.");
                    return;
                }
                DrawCards(player, 2);
                break;
            }
            case "Kit Carlson":
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
                AddEvent($"{player.Name}'s turn begins. Pick 2 of 3 revealed cards.");
                return;
            }
            case "Pedro Ramirez":
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
        if (player.Character.Name == "Willy the Kid") return int.MaxValue;
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
        if (target.Character.Name == "Paul Regret") baseDistance += 1;

        if (source.InPlay.Any(c => c.Type == CardType.Scope)) baseDistance -= 1;
        if (source.Character.Name == "Rose Doolan") baseDistance -= 1;

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
            return new Card("Check", CardType.Bang, CardCategory.Brown, "", false, null, "", CardSuit.Clubs, 10);
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
        if (player.Character.Name == "Suzy Lafayette" && player.IsAlive && player.Hand.Count == 0)
        {
            DrawCards(player, 1);
        }
    }

    private bool CheckBarrel(PlayerState target)
    {
        if (!target.InPlay.Any(c => c.Type == CardType.Barrel)) return false;

        if (target.Character.Name == "Lucky Duke")
        {
            var card1 = DrawCheckCard();
            var card2 = DrawCheckCard();
            var success = card1.Suit == CardSuit.Hearts || card2.Suit == CardSuit.Hearts;
            AddEvent($"Barrel draw! {target.Name} (Lucky Duke): {FormatCheckCard(card1)} and {FormatCheckCard(card2)} \u2014 {(success ? "dodged!" : "no luck.")}");
            return success;
        }

        var check = DrawCheckCard();
        var result = check.Suit == CardSuit.Hearts;
        AddEvent($"Barrel draw! {target.Name}: {FormatCheckCard(check)} \u2014 {(result ? "dodged!" : "no luck.")}");
        return result;
    }

    private string ResolveBang(PlayerState attacker, PlayerState target)
    {
        var damage = attacker.Character.Name == "Slab the Killer" ? 2 : 1;

        if (CheckBarrel(target))
        {
            return $"{attacker.Name} shoots at {target.Name}, but the Barrel saves {target.Name}!";
        }

        _pendingAction = new PendingAction(
            PendingActionType.BangDefense,
            attacker.Id,
            new[] { target.Id },
            damage);
        return $"{attacker.Name} shoots at {target.Name}! {target.Name} must respond.";
    }

    private string ResolveBeer(PlayerState player)
    {
        if (player.Hp >= player.MaxHp)
        {
            return $"{player.Name} is already at full health.";
        }

        player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
        return $"{player.Name} drinks a Beer and recovers 1 HP.";
    }

    private string ResolveGatling(PlayerState attacker)
    {
        var allResponders = GetOtherAlivePlayersInTurnOrder(attacker.Id);
        if (allResponders.Count == 0)
        {
            return $"{attacker.Name} fires Gatling, but there is no one to hit.";
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

        var barrelMsg = barrelSaved.Count > 0 ? $" {string.Join(", ", barrelSaved)} dodged with Barrel!" : "";

        if (needsResponse.Count == 0)
        {
            return $"{attacker.Name} fires the Gatling!{barrelMsg} Everyone is safe.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.GatlingDefense,
            attacker.Id,
            needsResponse);
        return $"{attacker.Name} fires the Gatling!{barrelMsg} Remaining players must play a Missed! or take 1 damage.";
    }

    private string ResolveStagecoach(PlayerState player)
    {
        DrawCards(player, 2);
        return $"{player.Name} plays Stagecoach and draws 2 cards.";
    }

    private string ResolveCatBalou(PlayerState attacker, PlayerState target)
    {
        if (target.Hand.Count == 0 && target.InPlay.Count == 0)
        {
            return $"{target.Name} has no cards to discard.";
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
            return $"{attacker.Name} uses Cat Balou on {target.Name}! Choose: random hand card or equipment.";
        }

        if (target.Hand.Count > 0)
        {
            var idx = _random.Next(target.Hand.Count);
            var discarded = target.Hand[idx];
            target.Hand.RemoveAt(idx);
            _discardPile.Add(discarded);
            return $"{attacker.Name} uses Cat Balou on {target.Name}, discarding {discarded.Name}.";
        }

        var equip = target.InPlay[_random.Next(target.InPlay.Count)];
        target.InPlay.Remove(equip);
        _discardPile.Add(equip);
        return $"{attacker.Name} uses Cat Balou on {target.Name}, discarding {equip.Name}.";
    }

    private string ResolveIndians(PlayerState attacker)
    {
        var responders = GetOtherAlivePlayersInTurnOrder(attacker.Id);
        if (responders.Count == 0)
        {
            return $"{attacker.Name} plays Indians!, but there is no one to hit.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.IndiansDefense,
            attacker.Id,
            responders);
        return $"{attacker.Name} plays Indians! Each player must discard a Bang! or take 1 damage.";
    }

    private string ResolveDuel(PlayerState attacker, PlayerState target)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DuelChallenge,
            attacker.Id,
            new[] { target.Id });
        _pendingAction.DuelPlayerA = attacker.Id;
        _pendingAction.DuelPlayerB = target.Id;
        return $"{attacker.Name} challenges {target.Name} to a duel!";
    }

    private string ResolvePanic(PlayerState attacker, PlayerState target)
    {
        if (target.Hand.Count == 0 && target.InPlay.Count == 0)
        {
            return $"{target.Name} has no cards to steal.";
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
            return $"{attacker.Name} uses Panic! on {target.Name}! Choose: random hand card or equipment.";
        }

        if (target.Hand.Count > 0)
        {
            var idx = _random.Next(target.Hand.Count);
            var stolen = target.Hand[idx];
            target.Hand.RemoveAt(idx);
            attacker.Hand.Add(stolen);
            return $"{attacker.Name} uses Panic! to steal {stolen.Name} from {target.Name}.";
        }

        var equip = target.InPlay[_random.Next(target.InPlay.Count)];
        target.InPlay.Remove(equip);
        attacker.Hand.Add(equip);
        return $"{attacker.Name} uses Panic! to steal {equip.Name} from {target.Name}.";
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

        return $"{player.Name} opens the Saloon. Everyone heals 1 HP.";
    }

    private string ResolveWellsFargo(PlayerState player)
    {
        DrawCards(player, 3);
        return $"{player.Name} raids Wells Fargo and draws 3 cards.";
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
            return $"{player.Name} visits the General Store, but the shelves are empty.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.GeneralStorePick,
            player.Id,
            alivePlayers);
        _pendingAction.RevealedCards = revealedCards;
        return $"{player.Name} opens the General Store! Each player picks a card.";
    }

    private bool TryGetTarget(string? targetId, string playerId, out PlayerState target, out string error)
    {
        if (string.IsNullOrWhiteSpace(targetId) || !_players.TryGetValue(targetId, out target!))
        {
            error = "Target player not found.";
            target = null!;
            return false;
        }

        if (target.Id == playerId)
        {
            error = "You cannot target yourself.";
            return false;
        }

        if (!target.IsAlive)
        {
            error = $"{target.Name} is already out.";
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
            target.IsAlive = false;
            HandlePlayerDeath(attacker, target);
        }

        if (target.IsAlive)
        {
            if (target.Character.Name == "Bart Cassidy")
            {
                DrawCards(target, damage);
            }
            else if (target.Character.Name == "El Gringo" && attacker.Id != target.Id)
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
            p.IsAlive && p.Id != dead.Id && p.Character.Name == "Vulture Sam");

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
        }

        RemoveFromTurnOrder(dead.Id);
        CheckForGameOver();
    }

    private string FormatAttackMessage(PlayerState attacker, PlayerState target, string verb, int damage)
    {
        if (!target.IsAlive)
        {
            return $"{attacker.Name} {verb} {target.Name} for {damage} damage. {target.Name} is out!";
        }

        return $"{attacker.Name} {verb} {target.Name} for {damage} damage.";
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
            AddEvent($"{sheriff.Name} is the Sheriff.");
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
                WinnerMessage = "Renegade wins by being the last player standing!";
                AddEvent(WinnerMessage);
                return;
            }

            GameOver = true;
            WinnerMessage = banditsAlive
                ? "Bandits win by taking down the Sheriff!"
                : "Bandits win after the Sheriff falls.";
            AddEvent(WinnerMessage);
            return;
        }

        if (!banditsAlive && !renegadeAlive)
        {
            GameOver = true;
            WinnerMessage = "Sheriff and Deputies win by clearing the outlaws!";
            AddEvent(WinnerMessage);
        }
    }

    private bool IsRoleRevealed(PlayerState player, PlayerState viewer)
    {
        return player.Role == Role.Sheriff || !player.IsAlive || GameOver || player.Id == viewer.Id;
    }

    private string GetRoleNameForViewer(PlayerState player, PlayerState viewer)
    {
        return IsRoleRevealed(player, viewer) ? player.Role.ToString() : "Unknown";
    }
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
    public string Name { get; }
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
            new CardDefinition("Bang!", CardType.Bang, CardCategory.Brown, "Deal 1 damage to a target (2 if you are Slab the Killer).", true, "Choose a player to shoot", "/assets/cards/bang.png")
        },
        {
            CardType.Missed,
            new CardDefinition("Missed!", CardType.Missed, CardCategory.Brown, "Play when shot to negate the damage.", false, null, "/assets/cards/missed.png")
        },
        {
            CardType.Beer,
            new CardDefinition("Beer", CardType.Beer, CardCategory.Brown, "Recover 1 HP.", false, null, "/assets/cards/beer.png")
        },
        {
            CardType.Gatling,
            new CardDefinition("Gatling", CardType.Gatling, CardCategory.Brown, "Each other player must play a Missed! or take 1 damage.", false, null, "/assets/cards/gatling.png")
        },
        {
            CardType.Stagecoach,
            new CardDefinition("Stagecoach", CardType.Stagecoach, CardCategory.Brown, "Draw 2 cards.", false, null, "/assets/cards/stagecoach.png")
        },
        {
            CardType.CatBalou,
            new CardDefinition("Cat Balou", CardType.CatBalou, CardCategory.Brown, "Force a target to discard a card (hand or equipment).", true, "Pick a player to discard", "/assets/cards/cat_balou.png")
        },
        {
            CardType.Indians,
            new CardDefinition("Indians!", CardType.Indians, CardCategory.Brown, "Each other player must discard a Bang! or take 1 damage.", false, null, "/assets/cards/indians.png")
        },
        {
            CardType.Duel,
            new CardDefinition("Duel", CardType.Duel, CardCategory.Brown, "Challenge a player — alternate discarding Bang! cards. First who can't takes 1 damage.", true, "Pick a dueling opponent", "/assets/cards/duel.png")
        },
        {
            CardType.Panic,
            new CardDefinition("Panic!", CardType.Panic, CardCategory.Brown, "Steal a card from a player at distance 1 (hand or equipment).", true, "Pick a player to rob", "/assets/cards/panic.png")
        },
        {
            CardType.Saloon,
            new CardDefinition("Saloon", CardType.Saloon, CardCategory.Brown, "All living players heal 1 HP.", false, null, "/assets/cards/saloon.png")
        },
        {
            CardType.WellsFargo,
            new CardDefinition("Wells Fargo", CardType.WellsFargo, CardCategory.Brown, "Draw 3 cards.", false, null, "/assets/cards/wells_fargo.png")
        },
        {
            CardType.GeneralStore,
            new CardDefinition("General Store", CardType.GeneralStore, CardCategory.Brown, "Reveal cards equal to alive players. Each picks one in turn order.", false, null, "/assets/cards/general_store.png")
        },
        {
            CardType.Barrel,
            new CardDefinition("Barrel", CardType.Barrel, CardCategory.Blue, "When shot, 'draw!' \u2014 if Hearts, the shot is dodged.", false, null, "/assets/cards/barrel.png")
        },
        {
            CardType.Mustang,
            new CardDefinition("Mustang", CardType.Mustang, CardCategory.Blue, "Other players see you at distance +1.", false, null, "/assets/cards/mustang.png")
        },
        {
            CardType.Scope,
            new CardDefinition("Scope", CardType.Scope, CardCategory.Blue, "You see other players at distance -1.", false, null, "/assets/cards/scope.png")
        },
        {
            CardType.Volcanic,
            new CardDefinition("Volcanic", CardType.Volcanic, CardCategory.Weapon, "Weapon (range 1). You can play unlimited Bang! per turn.", false, null, "/assets/cards/volcanic.png")
        },
        {
            CardType.Schofield,
            new CardDefinition("Schofield", CardType.Schofield, CardCategory.Weapon, "Weapon (range 2).", false, null, "/assets/cards/schofield.png")
        },
        {
            CardType.Remington,
            new CardDefinition("Remington", CardType.Remington, CardCategory.Weapon, "Weapon (range 3).", false, null, "/assets/cards/remington.png")
        },
        {
            CardType.RevCarabine,
            new CardDefinition("Rev. Carabine", CardType.RevCarabine, CardCategory.Weapon, "Weapon (range 4).", false, null, "/assets/cards/rev_carabine.png")
        },
        {
            CardType.Winchester,
            new CardDefinition("Winchester", CardType.Winchester, CardCategory.Weapon, "Weapon (range 5).", false, null, "/assets/cards/winchester.png")
        },
        {
            CardType.Jail,
            new CardDefinition("Jail", CardType.Jail, CardCategory.Blue, "Play on another player. They must draw at turn start — skip turn on failure.", true, "Choose a player to jail", "/assets/cards/jail.png")
        },
        {
            CardType.Dynamite,
            new CardDefinition("Dynamite", CardType.Dynamite, CardCategory.Blue, "Play on yourself. Passes between players. May explode for 3 damage.", false, null, "/assets/cards/dynamite.png")
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
            "Lucky Duke",
            4,
            "When you 'draw!', flip 2 cards and choose the best result.",
            "/assets/characters/lucky_duke.png"),
        new CharacterDefinition(
            "Slab the Killer",
            4,
            "Your Bang! cards deal 2 damage.",
            "/assets/characters/slab_the_killer.png"),
        new CharacterDefinition(
            "El Gringo",
            3,
            "When you take damage, draw a card from the attacker's hand.",
            "/assets/characters/el_gringo.png"),
        new CharacterDefinition(
            "Suzy Lafayette",
            4,
            "Whenever your hand becomes empty, draw 1 card.",
            "/assets/characters/suzy_lafayette.png"),
        new CharacterDefinition(
            "Rose Doolan",
            4,
            "Built-in Scope: you see others at distance -1.",
            "/assets/characters/rose_doolan.png"),
        new CharacterDefinition(
            "Jesse Jones",
            4,
            "Draw your first card from a chosen player's hand.",
            "/assets/characters/jesse_jones.png"),
        new CharacterDefinition(
            "Bart Cassidy",
            4,
            "Each time you take damage, draw 1 card from the deck.",
            "/assets/characters/bart_cassidy.png"),
        new CharacterDefinition(
            "Paul Regret",
            3,
            "Built-in Mustang: others see you at distance +1.",
            "/assets/characters/paul_regret.png"),
        new CharacterDefinition(
            "Calamity Janet",
            4,
            "You can use Bang! as Missed! and Missed! as Bang!.",
            "/assets/characters/calamity_janet.png"),
        new CharacterDefinition(
            "Kit Carlson",
            4,
            "Look at the top 3 cards, keep 2, put 1 back.",
            "/assets/characters/kit_carlson.png"),
        new CharacterDefinition(
            "Willy the Kid",
            4,
            "You can play unlimited Bang! cards per turn.",
            "/assets/characters/willy_the_kid.png"),
        new CharacterDefinition(
            "Sid Ketchum",
            4,
            "Discard 2 cards to regain 1 HP (usable on your turn).",
            "/assets/characters/sid_ketchum.png"),
        new CharacterDefinition(
            "Vulture Sam",
            4,
            "When a player is eliminated, you take all their cards.",
            "/assets/characters/vulture_sam.png"),
        new CharacterDefinition(
            "Pedro Ramirez",
            4,
            "Draw your first card from the top of the discard pile.",
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
