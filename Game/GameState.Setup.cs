partial class GameState
{
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
            BuildEventDeck();

            foreach (var player in _players.Values)
            {
                var newCharacter = CharacterLibrary.Draw(_random, _usedCharacterIndices, HasExpansion(Expansion.DodgeCity));
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
            // First round: advance event at game start (Sheriff's first turn)
            if (_eventDeck.Count > 0)
            {
                AdvanceEvent();
                if (GameOver) return new CommandResult(true, WinnerMessage ?? "Игра окончена.", ToView(playerId));
            }
            HandleDrawPhase(current);
            if (_pendingAction == null)
            {
                AddEvent($"Игра началась! {current.Name} ходит первым как Шериф.");
            }

            return new CommandResult(true, "Игра началась.", ToView(playerId));
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
                var character = CharacterLibrary.Draw(_random, _usedCharacterIndices, HasExpansion(Expansion.DodgeCity));
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
            BuildEventDeck();

            foreach (var player in _players.Values)
            {
                var newCharacter = CharacterLibrary.Draw(_random, _usedCharacterIndices, HasExpansion(Expansion.DodgeCity));
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
            if (_eventDeck.Count > 0)
            {
                AdvanceEvent();
                if (GameOver)
                {
                    var isSpec2 = _spectators.Contains(playerId);
                    return new CommandResult(true, WinnerMessage ?? "Игра окончена.", isSpec2 ? ToSpectatorView(playerId) : ToView(playerId));
                }
            }
            HandleDrawPhase(current);
            if (_pendingAction == null)
            {
                AddEvent($"Новая игра началась! {current.Name} ходит первым как Шериф.");
            }

            var isSpec = _spectators.Contains(playerId);
            return new CommandResult(true, "Новая игра началась.", isSpec ? ToSpectatorView(playerId) : ToView(playerId));
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
}
