partial class GameState
{
    public CommandResult UseGreenCard(string playerId, int greenCardIndex, string? targetPublicId)
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
            if (!player.IsAlive)
                return new CommandResult(false, "Вы выбыли из игры.");

            if (greenCardIndex < 0 || greenCardIndex >= player.InPlay.Count)
                return new CommandResult(false, "Неверный индекс карты.");

            var card = player.InPlay[greenCardIndex];
            if (card.Category != CardCategory.Green)
                return new CommandResult(false, "Эта карта не является зелёной.");

            var def = CardLibrary.Get(card.Type);
            if (def.IsReactiveGreen)
                return new CommandResult(false, "Эта карта активируется только в ответ на атаку.");

            if (player.FreshGreenCards.Contains(card))
                return new CommandResult(false, "This green card can only be used starting from your next turn.");

            // Validate target for cards that need one
            PlayerState? target = null;
            if (card.Type is CardType.Derringer or CardType.Pepperbox or CardType.BuffaloRifle)
            {
                if (string.IsNullOrWhiteSpace(targetPublicId))
                    return new CommandResult(false, "Выберите цель.");

                target = FindByPublicId(targetPublicId);
                if (target == null || !target.IsAlive || target.Id == playerId)
                    return new CommandResult(false, "Недопустимая цель.");

                if (card.Type == CardType.Derringer)
                {
                    var distance = GetDistance(playerId, target.Id);
                    if (distance > 1)
                        return new CommandResult(false, $"{target.Name} вне зоны досягаемости (дистанция {distance}, нужно 1).");
                }
            }

            // Remove green card from play and discard
            player.InPlay.RemoveAt(greenCardIndex);
            player.FreshGreenCards.Remove(card);
            _discardPile.Add(card);

            // Apache Kid: immune to Diamond suit cards
            if (target != null && target.ActiveCharacterName == "Апач Кид" && card.Suit == CardSuit.Diamonds)
            {
                var apacheMsg = $"{player.Name} активирует {card.Name} (♦), но Апач Кид неуязвим к бубновым картам!";
                if (card.Type == CardType.Derringer) DrawCards(player, 1);
                AddEvent(apacheMsg);
                return new CommandResult(true, apacheMsg, ToView(playerId));
            }

            string message;
            switch (card.Type)
            {
                case CardType.Derringer:
                    DrawCards(player, 1);
                    if (CheckBarrel(target!))
                    {
                        message = $"{player.Name} активирует Дерринджер против {target!.Name}, но Бочка спасает! (+1 карта)";
                    }
                    else
                    {
                        _pendingAction = new PendingAction(
                            PendingActionType.BangDefense,
                            player.Id,
                            new[] { target!.Id });
                        message = $"{player.Name} активирует Дерринджер против {target!.Name}! (+1 карта) {target.Name} должен ответить.";
                    }
                    break;

                case CardType.Pepperbox:
                case CardType.BuffaloRifle:
                    if (CheckBarrel(target!))
                    {
                        message = $"{player.Name} активирует {card.Name} против {target!.Name}, но Бочка спасает!";
                    }
                    else
                    {
                        _pendingAction = new PendingAction(
                            PendingActionType.BangDefense,
                            player.Id,
                            new[] { target!.Id });
                        message = $"{player.Name} активирует {card.Name} против {target!.Name}! {target.Name} должен ответить.";
                    }
                    break;

                case CardType.Howitzer:
                    var allResponders = GetOtherAlivePlayersInTurnOrder(player.Id);
                    // Apache Kid: immune to Diamond suit
                    if (card.Suit == CardSuit.Diamonds)
                    {
                        var immuneH = allResponders.Where(id => _players[id].ActiveCharacterName == "Апач Кид").ToList();
                        foreach (var id in immuneH)
                        {
                            allResponders.Remove(id);
                            AddEvent($"{_players[id].Name} (Апач Кид) неуязвим к бубновым картам!");
                        }
                    }
                    if (allResponders.Count == 0)
                    {
                        message = $"{player.Name} активирует Гаубицу, но некого поразить.";
                        break;
                    }
                    var barrelSaved = new List<string>();
                    var needsResponse = new List<string>();
                    foreach (var id in allResponders)
                    {
                        var p = _players[id];
                        if (CheckBarrel(p))
                            barrelSaved.Add(p.Name);
                        else
                            needsResponse.Add(id);
                    }
                    var barrelMsg = barrelSaved.Count > 0
                        ? $" {string.Join(", ", barrelSaved)} увернулись с помощью Бочки!"
                        : "";
                    if (needsResponse.Count == 0)
                    {
                        message = $"{player.Name} активирует Гаубицу!{barrelMsg} Все в безопасности.";
                        break;
                    }
                    _pendingAction = new PendingAction(
                        PendingActionType.HowitzerDefense,
                        player.Id,
                        needsResponse);
                    message = $"{player.Name} активирует Гаубицу!{barrelMsg} Оставшиеся должны сыграть Мимо! или получить 1 урон.";
                    break;

                case CardType.Canteen:
                    if (player.Hp >= player.MaxHp)
                    {
                        message = $"У {player.Name} уже максимальное здоровье.";
                    }
                    else
                    {
                        player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
                        message = $"{player.Name} использует Флягу и восстанавливает 1 ОЗ.";
                    }
                    break;

                default:
                    message = "Неизвестная зелёная карта.";
                    break;
            }

            CheckSuzyLafayette(player);
            AddEvent(message);

            if (GameOver && !string.IsNullOrWhiteSpace(WinnerMessage))
                message = WinnerMessage;

            return new CommandResult(true, message, ToView(playerId));
        }
    }
}
