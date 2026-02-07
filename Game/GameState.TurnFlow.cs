partial class GameState
{
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

            // Sean Mallory: no hand limit
            if (endingPlayer.Hand.Count > endingPlayer.Hp && endingPlayer.ActiveCharacterName != "Шон Мэллори")
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

            // Event end-of-turn effects
            ApplyEventEndOfTurnEffects(endingPlayer);
            if (GameOver) return new CommandResult(true, WinnerMessage ?? "Игра окончена.", ToView(playerId));

            AdvanceTurn();
            return new CommandResult(true, "Ход завершён.", ToView(playerId));
        }
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

        // Advance event when Sheriff's turn begins (new round)
        if (current.Role == Role.Sheriff && _eventDeck.Count > 0)
        {
            AdvanceEvent();
            if (GameOver) return;
            if (!current.IsAlive) return; // Ghost removal or event killed player
            if (_pendingAction != null) return; // Russian Roulette, Train Robbery
        }

        // 0. Green cards become active at the start of the owner's turn.
        // They stay in play until used or removed by other effects.
        current.FreshGreenCards.Clear();

        // 1. Dynamite check
        var dynamite = current.InPlay.FirstOrDefault(c => c.Type == CardType.Dynamite);
        if (dynamite != null)
        {
            current.InPlay.Remove(dynamite);
            bool isLuckyDuke = current.ActiveCharacterName == "Лаки Дьюк";
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
            bool isLuckyDuke = current.ActiveCharacterName == "Лаки Дьюк";
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

        // 3. Vera Custer: copy ability
        if (current.IsAlive && !GameOver && current.Character.Name == "Вера Кастер")
        {
            var copyTargets = _players.Values
                .Where(p => p.IsAlive && p.Id != current.Id)
                .Select(p => p.Id)
                .ToList();
            if (copyTargets.Count > 0)
            {
                _pendingAction = new PendingAction(
                    PendingActionType.VeraCusterCopy,
                    current.Id,
                    new[] { current.Id });
                AddEvent($"Ход {current.Name} начинается. Выберите персонажа для копирования.");
                return;
            }
        }

        // 4. Normal draw phase
        if (current.IsAlive && !GameOver)
        {
            AddEvent($"Ход {current.Name} начинается.");
            HandleDrawPhase(current);
        }
    }

    private int GetEventDrawModifier()
    {
        var mod = 0;
        if (IsEventActive(EventCardType.Thirst)) mod -= 1;
        if (IsEventActive(EventCardType.GoldRush)) mod += 1;
        return mod;
    }

    private void EventAwareDraw(PlayerState player, int count)
    {
        var total = Math.Max(0, count + GetEventDrawModifier());

        // Peyote / Abandoned Mine: draw from discard instead of deck
        if (IsEventActive(EventCardType.Peyote) || IsEventActive(EventCardType.AbandonedMine))
        {
            for (var i = 0; i < total; i++)
            {
                if (_discardPile.Count == 0) break;
                var card = _discardPile[^1];
                _discardPile.RemoveAt(_discardPile.Count - 1);
                player.Hand.Add(card);
            }
            return;
        }

        // Fistful of Cards (draw from player to the right)
        if (IsEventActive(EventCardType.FistfulOfCards))
        {
            var rightNeighbor = GetPreviousAlivePlayerId(player.Id);
            if (rightNeighbor != null)
            {
                var neighbor = _players[rightNeighbor];
                var stolen = Math.Min(total, neighbor.Hand.Count);
                for (var i = 0; i < stolen; i++)
                {
                    if (neighbor.Hand.Count == 0) break;
                    var idx = _random.Next(neighbor.Hand.Count);
                    var card = neighbor.Hand[idx];
                    neighbor.Hand.RemoveAt(idx);
                    player.Hand.Add(card);
                }
                // Draw remaining from deck if neighbor didn't have enough
                if (stolen < total) DrawCards(player, total - stolen);
                return;
            }
        }

        DrawCards(player, total);
    }

    private string? GetPreviousAlivePlayerId(string currentPlayerId)
    {
        var currentIndex = _turnOrder.IndexOf(currentPlayerId);
        if (currentIndex == -1) return null;
        for (var i = 1; i < _turnOrder.Count; i++)
        {
            var idx = (currentIndex - i + _turnOrder.Count) % _turnOrder.Count;
            var id = _turnOrder[idx];
            if (_players[id].IsAlive && id != currentPlayerId)
                return id;
        }
        return null;
    }

    private void HandleDrawPhase(PlayerState player)
    {
        // Hangover: character abilities suspended — use default draw
        if (IsHangoverActive())
        {
            EventAwareDraw(player, 2);
            return;
        }

        switch (player.ActiveCharacterName)
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
                EventAwareDraw(player, 2);
                break;
            }
            case "Кит Карлсон":
            {
                var picksNeeded = Math.Max(0, 2 + GetEventDrawModifier());
                if (picksNeeded == 0) break;
                var revealCount = picksNeeded + 1;
                var revealedCards = new List<Card>();
                for (var i = 0; i < revealCount; i++)
                {
                    if (_drawPile.Count == 0) ReshuffleDiscardIntoDraw();
                    if (_drawPile.Count == 0) break;
                    revealedCards.Add(_drawPile.Pop());
                }
                if (revealedCards.Count <= picksNeeded)
                {
                    foreach (var c in revealedCards) player.Hand.Add(c);
                    break;
                }
                _pendingAction = new PendingAction(
                    PendingActionType.KitCarlsonPick,
                    player.Id,
                    new[] { player.Id });
                _pendingAction.RevealedCards = revealedCards;
                _pendingAction.KitCarlsonPicksRemaining = picksNeeded;
                AddEvent($"Ход {player.Name} начинается. Выберите {picksNeeded} из {revealedCards.Count} открытых карт.");
                return;
            }
            case "Педро Рамирес":
            {
                var total = Math.Max(0, 2 + GetEventDrawModifier());
                if (_discardPile.Count > 0 && total > 0)
                {
                    var topDiscard = _discardPile[^1];
                    _discardPile.RemoveAt(_discardPile.Count - 1);
                    player.Hand.Add(topDiscard);
                    if (total > 1) DrawCards(player, total - 1);
                }
                else
                {
                    DrawCards(player, total);
                }
                break;
            }
            case "Билл Ноуфейс":
            {
                var wounds = player.MaxHp - player.Hp;
                var drawCount = 1 + wounds;
                EventAwareDraw(player, drawCount);
                break;
            }
            case "Пикси Пит":
                EventAwareDraw(player, 3);
                break;
            case "Пат Бреннан":
            {
                // Can choose: draw 1 in-play card from another player, or draw 2 from deck
                var equipTargets = _players.Values
                    .Where(p => p.IsAlive && p.Id != player.Id && p.InPlay.Count > 0)
                    .ToList();
                if (equipTargets.Count > 0)
                {
                    // Build revealed cards list from all targets' equipment
                    var revealedEquipment = new List<Card>();
                    foreach (var t in equipTargets)
                    {
                        revealedEquipment.AddRange(t.InPlay);
                    }
                    _pendingAction = new PendingAction(
                        PendingActionType.PatBrennanDraw,
                        player.Id,
                        new[] { player.Id });
                    _pendingAction.RevealedCards = revealedEquipment;
                    AddEvent($"{player.Name} (Пат Бреннан): возьмите карту из чужого снаряжения или доберите из колоды.");
                    return;
                }
                EventAwareDraw(player, 2);
                break;
            }
            default:
                EventAwareDraw(player, 2);
                break;
        }
    }

    internal void ContinueDrawPhaseAfterVeraCuster(PlayerState current)
    {
        if (current.IsAlive && !GameOver)
        {
            HandleDrawPhase(current);
        }
    }

    private void ContinueTurnAfterEvent()
    {
        if (GameOver || _turnOrder.Count == 0) return;
        var current = _players[_turnOrder[_turnIndex]];
        if (!current.IsAlive) return;

        // Continue BeginTurn from step 0 (green readiness, dynamite, jail, draw)
        current.FreshGreenCards.Clear();

        // Dynamite check
        var dynamite = current.InPlay.FirstOrDefault(c => c.Type == CardType.Dynamite);
        if (dynamite != null)
        {
            current.InPlay.Remove(dynamite);
            bool isLuckyDuke = current.ActiveCharacterName == "Лаки Дьюк";
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

        // Jail check
        var jail = current.InPlay.FirstOrDefault(c => c.Type == CardType.Jail);
        if (jail != null && current.IsAlive)
        {
            current.InPlay.Remove(jail);
            _discardPile.Add(jail);
            bool isLuckyDuke = current.ActiveCharacterName == "Лаки Дьюк";
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

        // Vera Custer copy
        if (current.IsAlive && !GameOver && current.Character.Name == "Вера Кастер")
        {
            var copyTargets = _players.Values
                .Where(p => p.IsAlive && p.Id != current.Id)
                .Select(p => p.Id)
                .ToList();
            if (copyTargets.Count > 0)
            {
                _pendingAction = new PendingAction(
                    PendingActionType.VeraCusterCopy,
                    current.Id,
                    new[] { current.Id });
                AddEvent($"Ход {current.Name} начинается. Выберите персонажа для копирования.");
                return;
            }
        }

        // Normal draw phase
        if (current.IsAlive && !GameOver)
        {
            AddEvent($"Ход {current.Name} начинается.");
            HandleDrawPhase(current);
        }
    }
}
