partial class GameState
{
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
                    p.InPlay.Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value, p.FreshGreenCards.Contains(c))).ToList()))
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
                YourPublicId: myPublicId,
                Settings: GetSettings(),
                CurrentEventName: _currentEvent?.Name,
                CurrentEventDescription: _currentEvent?.Description);
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
                    p.InPlay.Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value, p.FreshGreenCards.Contains(c))).ToList()))
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
                    PendingActionType.DiscardForCost => "Сбросьте карту для активации эффекта.",
                    PendingActionType.BrawlDefense => "Сбросьте карту в Потасовке.",
                    PendingActionType.HowitzerDefense => "Сыграйте Мимо! или получите 1 урон.",
                    PendingActionType.VeraCusterCopy => "Выберите персонажа для копирования.",
                    PendingActionType.PatBrennanDraw => "Возьмите снаряжение или доберите из колоды.",
                    PendingActionType.RussianRoulette => "Сбросьте Бэнг! или потеряйте 1 ОЗ.",
                    PendingActionType.TrainRobbery => "Передайте карту влево или потеряйте 1 ОЗ.",
                    _ => "Ответьте на действие."
                };

                List<CardView>? revealedCards = null;
                var isPrivateReveal = _pendingAction.Type is PendingActionType.KitCarlsonPick or PendingActionType.PatBrennanDraw;
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

            // Wild West Show: reveal all players' hands
            var wildWestShow = IsEventActive(EventCardType.WildWestShow);

            return new GameStateView(
                Started,
                currentPublicId,
                currentName,
                GameOver,
                WinnerMessage,
                wildWestShow ? orderedIds
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
                        p.InPlay.Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value, p.FreshGreenCards.Contains(c))).ToList(),
                        p.Id != viewer.Id ? p.Hand.Select(c => new CardView(c.Name, c.Type, c.Category, c.Description, c.RequiresTarget, c.TargetHint, c.ImagePath, c.Suit.ToString(), c.Value)).ToList() : null))
                    .ToList() : players,
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
                YourPublicId: viewer.PublicId,
                Settings: GetSettings(),
                CurrentEventName: _currentEvent?.Name,
                CurrentEventDescription: _currentEvent?.Description);
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
