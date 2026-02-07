partial class GameState
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
    private Expansion _enabledExpansions = Expansion.None;

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

    private bool IsPlayersTurn(string playerId)
    {
        return _turnOrder.Count > 0 && _turnOrder[_turnIndex] == playerId;
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
            var character = CharacterLibrary.Draw(_random, _usedCharacterIndices, HasExpansion(Expansion.DodgeCity));
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

            // Fully remove the player so the room can be cleaned up
            _players.Remove(playerId);
            _seatOrder.Remove(playerId);
            TransferHostIfNeeded(playerId);
            return new CommandResult(true, "Вы покинули комнату.");
        }
    }

    public bool IsSpectator(string playerId)
    {
        lock (_lock) { return _spectators.Contains(playerId); }
    }

    public bool IsHost(string playerId) => _hostId == playerId;

    public bool HasExpansion(Expansion exp) => (_enabledExpansions & exp) != 0;

    public GameSettings GetSettings() => new(
        HasExpansion(Expansion.DodgeCity),
        HasExpansion(Expansion.HighNoon),
        HasExpansion(Expansion.FistfulOfCards));

    public CommandResult UpdateSettings(string playerId, Expansion expansions)
    {
        lock (_lock)
        {
            if (Started && !GameOver)
                return new CommandResult(false, "Нельзя менять настройки во время игры.");
            if (_hostId != playerId)
                return new CommandResult(false, "Только хост может менять настройки.");
            _enabledExpansions = expansions;
            AddEvent($"Настройки обновлены.");
            return new CommandResult(true, "Настройки обновлены.");
        }
    }

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
            var isCalamityJanet = player.ActiveCharacterName == "Каламити Джанет";

            // Cards that can only be played as a response
            if (card.Type == CardType.Missed && !isCalamityJanet)
            {
                return new CommandResult(false, "Мимо! можно играть только в ответ на выстрел.");
            }
            if (card.Type == CardType.Dodge)
            {
                return new CommandResult(false, "Уворот можно играть только в ответ на выстрел.");
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

            // Sermon: Bangs cannot be played
            if (effectiveType == CardType.Bang && IsEventActive(EventCardType.Sermon))
            {
                return new CommandResult(false, "Проповедь! Нельзя играть Бэнг! в этом раунде.");
            }

            // The Reverend: Beers cannot be played
            if (card.Type == CardType.Beer && IsEventActive(EventCardType.TheReverend))
            {
                return new CommandResult(false, "Священник! Нельзя играть Пиво в этом раунде.");
            }

            if (effectiveType == CardType.Bang && player.BangsPlayedThisTurn >= GetBangLimit(player))
            {
                var limit = GetBangLimit(player);
                return new CommandResult(false, $"Можно сыграть только {limit} Бэнг! за ход.");
            }

            // Discard-for-cost cards need at least 1 additional card in hand
            var isDiscardCost = card.Type is CardType.Springfield or CardType.Whisky
                or CardType.Tequila or CardType.RagTime or CardType.Brawl;
            if (isDiscardCost && player.Hand.Count < 2)
            {
                return new CommandResult(false, "Нужна минимум ещё 1 карта в руке для сброса.");
            }

            var needsTarget = card.RequiresTarget || effectiveType == CardType.Bang;
            var allowSelf = card.Type == CardType.Tequila;
            PlayerState? target = null;
            if (needsTarget && !TryGetTarget(targetId, playerId, out target, out var error, allowSelf))
            {
                return new CommandResult(false, error);
            }

            if (target != null)
            {
                var distance = GetDistance(playerId, target.Id);
                // Sniper: Bangs ignore distance
                var sniperActive = IsEventActive(EventCardType.Sniper);
                if (effectiveType == CardType.Bang && !sniperActive && distance > GetWeaponRange(player))
                {
                    return new CommandResult(false, $"{target.Name} вне зоны досягаемости (расстояние {distance}, дальность оружия {GetWeaponRange(player)}).");
                }
                if (effectiveType == CardType.Panic && distance > 1)
                {
                    return new CommandResult(false, $"{target.Name} вне зоны досягаемости для Паники! (расстояние {distance}, нужно 1).");
                }
                if (card.Type == CardType.Punch && distance > 1)
                {
                    return new CommandResult(false, $"{target.Name} вне зоны досягаемости для Удара (расстояние {distance}, нужно 1).");
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

            if (card.Category == CardCategory.Green)
            {
                var duplicate = player.InPlay.FirstOrDefault(c => c.Type == card.Type);
                if (duplicate != null)
                {
                    player.InPlay.Remove(duplicate);
                    _discardPile.Add(duplicate);
                }
                player.InPlay.Add(card);
                player.FreshGreenCards.Add(card);
                var greenMsg = $"{player.Name} выкладывает {card.Name}.";
                AddEvent(greenMsg);
                return new CommandResult(true, greenMsg, ToView(playerId));
            }

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

            // Apache Kid: Diamond cards from other players have no effect
            if (target != null && target.ActiveCharacterName == "Апач Кид" && card.Suit == CardSuit.Diamonds)
            {
                var apacheMsg = $"{player.Name} играет {card.Name} (♦), но Апач Кид неуязвим к бубновым картам!";
                if (effectiveType == CardType.Bang) player.BangsPlayedThisTurn += 1;
                CheckSuzyLafayette(player);
                AddEvent(apacheMsg);
                return new CommandResult(true, apacheMsg, ToView(playerId));
            }

            var message = effectiveType switch
            {
                CardType.Bang => ResolveBang(player, target!),
                CardType.Beer => ResolveBeer(player),
                CardType.Gatling => ResolveGatling(player, card.Suit),
                CardType.Stagecoach => ResolveStagecoach(player),
                CardType.CatBalou => ResolveCatBalou(player, target!),
                CardType.Indians => ResolveIndians(player, card.Suit),
                CardType.Duel => ResolveDuel(player, target!),
                CardType.Panic => ResolvePanic(player, target!),
                CardType.Saloon => ResolveSaloon(player),
                CardType.WellsFargo => ResolveWellsFargo(player),
                CardType.GeneralStore => ResolveGeneralStore(player),
                // Dodge City
                CardType.Punch => ResolvePunch(player, target!),
                CardType.CanCan => ResolveCanCan(player, target!),
                CardType.Conestoga => ResolveConestoga(player, target!),
                CardType.Springfield => ResolveSpringfield(player, target!),
                CardType.Whisky => ResolveWhisky(player),
                CardType.Tequila => ResolveTequila(player, target!),
                CardType.RagTime => ResolveRagTime(player, target!),
                CardType.Brawl => ResolveBrawl(player, card.Suit),
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

    public CommandResult UseAbility(string playerId, int[] cardIndices, string? targetPublicId = null)
    {
        lock (_lock)
        {
            if (!Started || GameOver)
                return new CommandResult(false, "Игра не активна.");
            if (_pendingAction != null)
                return new CommandResult(false, "Ожидание ответа от игрока.");
            if (!IsPlayersTurn(playerId))
                return new CommandResult(false, "Сейчас не ваш ход.");
            if (!_players.TryGetValue(playerId, out var player))
                return new CommandResult(false, "Неизвестный игрок.");

            // The Doctor event: anyone can discard 2 for +1 HP
            if (IsEventActive(EventCardType.TheDoctor))
            {
                if (cardIndices != null && cardIndices.Length == 2 && player.Hand.Count >= 2 && player.Hp < player.MaxHp)
                {
                    var sorted = cardIndices.OrderByDescending(i => i).ToArray();
                    if (sorted.All(i => i >= 0 && i < player.Hand.Count) && sorted[0] != sorted[1])
                    {
                        var c1 = player.Hand[sorted[0]];
                        var c2 = player.Hand[sorted[1]];
                        player.Hand.RemoveAt(sorted[0]);
                        player.Hand.RemoveAt(sorted[1]);
                        _discardPile.Add(c1);
                        _discardPile.Add(c2);
                        player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
                        var docMsg = $"{player.Name} сбрасывает 2 карты (Доктор) и восстанавливает 1 ОЗ.";
                        AddEvent(docMsg);
                        CheckSuzyLafayette(player);
                        return new CommandResult(true, docMsg, ToView(playerId));
                    }
                }
            }

            // Hangover: character abilities suspended
            if (IsHangoverActive())
                return new CommandResult(false, "Похмелье! Способности персонажей не действуют.");

            var charName = player.ActiveCharacterName;
            string message;

            switch (charName)
            {
                case "Сид Кетчум":
                {
                    if (cardIndices == null || cardIndices.Length != 2)
                        return new CommandResult(false, "Нужно выбрать ровно 2 карты для сброса.");
                    if (player.Hp >= player.MaxHp)
                        return new CommandResult(false, "У вас уже максимальное здоровье.");
                    if (player.Hand.Count < 2)
                        return new CommandResult(false, "Нужно минимум 2 карты.");
                    var sorted = cardIndices.OrderByDescending(i => i).ToArray();
                    if (sorted.Any(i => i < 0 || i >= player.Hand.Count) || sorted[0] == sorted[1])
                        return new CommandResult(false, "Неверный выбор карты.");
                    var c1 = player.Hand[sorted[0]];
                    var c2 = player.Hand[sorted[1]];
                    player.Hand.RemoveAt(sorted[0]);
                    player.Hand.RemoveAt(sorted[1]);
                    _discardPile.Add(c1);
                    _discardPile.Add(c2);
                    player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
                    message = $"{player.Name} сбрасывает {c1.Name} и {c2.Name}, чтобы восстановить 1 ОЗ.";
                    break;
                }
                case "Чак Венгам":
                {
                    if (player.Hp <= 1)
                        return new CommandResult(false, "Слишком мало ОЗ для использования способности.");
                    player.Hp -= 1;
                    DrawCards(player, 2);
                    message = $"{player.Name} теряет 1 ОЗ и добирает 2 карты.";
                    break;
                }
                case "Док Холидэй":
                {
                    if (player.AbilityUsesThisTurn >= 1)
                        return new CommandResult(false, "Способность уже использована в этом ходу.");
                    if (cardIndices == null || cardIndices.Length != 2)
                        return new CommandResult(false, "Нужно выбрать ровно 2 карты для сброса.");
                    if (player.Hand.Count < 2)
                        return new CommandResult(false, "Нужно минимум 2 карты.");
                    var dSorted = cardIndices.OrderByDescending(i => i).ToArray();
                    if (dSorted.Any(i => i < 0 || i >= player.Hand.Count) || dSorted[0] == dSorted[1])
                        return new CommandResult(false, "Неверный выбор карты.");
                    if (string.IsNullOrWhiteSpace(targetPublicId))
                        return new CommandResult(false, "Выберите цель для выстрела.");
                    var docTarget = FindByPublicId(targetPublicId);
                    if (docTarget == null || !docTarget.IsAlive || docTarget.Id == playerId)
                        return new CommandResult(false, "Недопустимая цель.");
                    var dc1 = player.Hand[dSorted[0]];
                    var dc2 = player.Hand[dSorted[1]];
                    player.Hand.RemoveAt(dSorted[0]);
                    player.Hand.RemoveAt(dSorted[1]);
                    _discardPile.Add(dc1);
                    _discardPile.Add(dc2);
                    player.AbilityUsesThisTurn++;
                    if (CheckBarrel(docTarget))
                    {
                        message = $"{player.Name} стреляет в {docTarget.Name} (Док Холидэй), но Бочка спасает!";
                    }
                    else
                    {
                        _pendingAction = new PendingAction(
                            PendingActionType.BangDefense,
                            player.Id,
                            new[] { docTarget.Id });
                        message = $"{player.Name} стреляет в {docTarget.Name} (Док Холидэй)! {docTarget.Name} должен ответить.";
                    }
                    break;
                }
                case "Хосе Дельгадо":
                {
                    if (player.AbilityUsesThisTurn >= 2)
                        return new CommandResult(false, "Способность уже использована 2 раза в этом ходу.");
                    if (cardIndices == null || cardIndices.Length != 1)
                        return new CommandResult(false, "Выберите 1 синюю карту для сброса.");
                    var jIdx = cardIndices[0];
                    if (jIdx < 0 || jIdx >= player.Hand.Count)
                        return new CommandResult(false, "Неверный индекс карты.");
                    var jCard = player.Hand[jIdx];
                    if (jCard.Category != CardCategory.Blue)
                        return new CommandResult(false, "Нужно сбросить синюю карту.");
                    player.Hand.RemoveAt(jIdx);
                    _discardPile.Add(jCard);
                    DrawCards(player, 2);
                    player.AbilityUsesThisTurn++;
                    message = $"{player.Name} сбрасывает {jCard.Name} и добирает 2 карты.";
                    break;
                }
                default:
                    return new CommandResult(false, "У вашего персонажа нет активной способности.");
            }

            AddEvent(message);
            CheckSuzyLafayette(player);

            if (GameOver && !string.IsNullOrWhiteSpace(WinnerMessage))
                message = WinnerMessage;

            return new CommandResult(true, message, ToView(playerId));
        }
    }
}
