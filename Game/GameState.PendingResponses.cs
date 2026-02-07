partial class GameState
{
    public CommandResult Respond(string playerId, string responseType, int? cardIndex, string? targetId = null)
    {
        lock (_lock)
        {
            if (GameOver)
            {
                _pendingAction = null;
                return new CommandResult(false, "Игра окончена.");
            }

            if (_pendingAction == null)
            {
                return new CommandResult(false, "Нет действия для ответа.");
            }

            if (_pendingAction.RespondingPlayerIds.Count == 0)
            {
                _pendingAction = null;
                return new CommandResult(false, "Нет действия для ответа.");
            }

            var currentResponderId = _pendingAction.RespondingPlayerIds.Peek();
            if (currentResponderId != playerId)
            {
                return new CommandResult(false, "Сейчас не ваша очередь отвечать.");
            }

            if (!_players.TryGetValue(playerId, out var responder))
            {
                return new CommandResult(false, "Неизвестный игрок.");
            }

            var source = _players.ContainsKey(_pendingAction.SourcePlayerId)
                ? _players[_pendingAction.SourcePlayerId]
                : null;

            string message;
            bool mollyStarkDraw = false;

            switch (_pendingAction.Type)
            {
                case PendingActionType.BangDefense:
                case PendingActionType.GatlingDefense:
                case PendingActionType.HowitzerDefense:
                {
                    if (responseType == "play_card")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                        {
                            return new CommandResult(false, "Неверный индекс карты.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.ActiveCharacterName == "Каламити Джанет";
                        var isElena = responder.ActiveCharacterName == "Елена Фуэнте";
                        if (card.Type != CardType.Missed && card.Type != CardType.Dodge
                            && !(isJanet && card.Type == CardType.Bang)
                            && !isElena)
                        {
                            return new CommandResult(false, isJanet
                                ? "Нужно сыграть Мимо!/Уворот или Бэнг!, чтобы увернуться."
                                : "Нужно сыграть Мимо! или Уворот, чтобы увернуться.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        mollyStarkDraw = true;

                        if (card.Type == CardType.Dodge)
                        {
                            DrawCards(responder, 1);
                            message = $"{responder.Name} играет Уворот и уклоняется! (+1 карта)";
                        }
                        else if (isElena && card.Type != CardType.Missed && card.Type != CardType.Dodge)
                        {
                            message = $"{responder.Name} (Елена Фуэнте) использует {card.Name} как Мимо! и уворачивается!";
                        }
                        else
                        {
                            message = $"{responder.Name} играет {card.Name} и уворачивается от выстрела!";
                        }

                        // Ricochet: Missed also deals 1 damage to attacker
                        if (IsEventActive(EventCardType.Ricochet) && source != null && source.IsAlive
                            && _pendingAction.Type == PendingActionType.BangDefense)
                        {
                            ApplyDamage(responder, source, 1, "рикошетом попадает в");
                            AddEvent($"Рикошет! {source.Name} получает 1 урон.");
                        }
                    }
                    else if (responseType == "play_green")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.InPlay.Count)
                        {
                            return new CommandResult(false, "Неверный индекс снаряжения.");
                        }
                        var greenCard = responder.InPlay[cardIndex.Value];
                        if (!CardLibrary.Get(greenCard.Type).IsReactiveGreen)
                        {
                            return new CommandResult(false, "Эта карта не может быть использована как защита.");
                        }
                        if (responder.FreshGreenCards.Contains(greenCard))
                        {
                            return new CommandResult(false, "This green card is not active yet.");
                        }
                        responder.InPlay.RemoveAt(cardIndex.Value);
                        _discardPile.Add(greenCard);
                        responder.FreshGreenCards.Remove(greenCard);
                        mollyStarkDraw = true;
                        message = $"{responder.Name} использует {greenCard.Name} и уворачивается от выстрела!";

                        // Ricochet: also applies to green card defense
                        if (IsEventActive(EventCardType.Ricochet) && source != null && source.IsAlive
                            && _pendingAction.Type == PendingActionType.BangDefense)
                        {
                            ApplyDamage(responder, source, 1, "рикошетом попадает в");
                            AddEvent($"Рикошет! {source.Name} получает 1 урон.");
                        }
                    }
                    else
                    {
                        var damage = _pendingAction.Damage;
                        ApplyDamage(source ?? responder, responder, damage, "стреляет в");
                        message = FormatAttackMessage(source ?? responder, responder, "стреляет в", damage);
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
                            return new CommandResult(false, "Неверный индекс карты.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.ActiveCharacterName == "Каламити Джанет";
                        if (card.Type != CardType.Bang && !(isJanet && card.Type == CardType.Missed))
                        {
                            return new CommandResult(false, isJanet
                                ? "Нужно сбросить Бэнг! или Мимо!, чтобы избежать атаки индейцев."
                                : "Нужно сбросить Бэнг!, чтобы избежать атаки индейцев.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        mollyStarkDraw = true;
                        message = $"{responder.Name} сбрасывает {card.Name} и отбивается от индейцев!";
                    }
                    else
                    {
                        if (source != null)
                        {
                            ApplyDamage(source, responder, 1, "атакован индейцами");
                        }
                        message = $"{responder.Name} атакован индейцами и получает 1 урон.";
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
                            return new CommandResult(false, "Неверный индекс карты.");
                        }

                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.ActiveCharacterName == "Каламити Джанет";
                        if (card.Type != CardType.Bang && !(isJanet && card.Type == CardType.Missed))
                        {
                            return new CommandResult(false, isJanet
                                ? "Нужно сыграть Бэнг! или Мимо!, чтобы продолжить дуэль."
                                : "Нужно сыграть Бэнг!, чтобы продолжить дуэль.");
                        }

                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        mollyStarkDraw = true;

                        _pendingAction.RespondingPlayerIds.Dequeue();
                        _pendingAction.RespondingPlayerIds.Enqueue(opponentId);
                        message = $"{responder.Name} отвечает в дуэли! {opponent.Name} должен ответить.";
                    }
                    else
                    {
                        ApplyDamage(opponent, responder, 1, "проиграл дуэль против");
                        message = $"{responder.Name} не может продолжить дуэль и получает 1 урон!";
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
                        message = "Больше нет карт для выбора.";
                        break;
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                    {
                        return new CommandResult(false, "Неверный выбор карты.");
                    }

                    var pickedCard = _pendingAction.RevealedCards[cardIndex.Value];
                    _pendingAction.RevealedCards.RemoveAt(cardIndex.Value);
                    responder.Hand.Add(pickedCard);
                    message = $"{responder.Name} берёт {pickedCard.Name} из Магазина.";

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
                        message = "Цель больше не существует.";
                        break;
                    }

                    var isSteal = _pendingAction.StealMode == "steal";

                    if (responseType == "hand")
                    {
                        if (stealTarget.Hand.Count == 0)
                        {
                            message = $"У {stealTarget.Name} не осталось карт в руке.";
                            _pendingAction = null;
                            break;
                        }
                        var idx = _random.Next(stealTarget.Hand.Count);
                        var card = stealTarget.Hand[idx];
                        stealTarget.Hand.RemoveAt(idx);
                        if (isSteal)
                        {
                            responder.Hand.Add(card);
                            message = $"{responder.Name} крадёт карту из руки {stealTarget.Name}.";
                        }
                        else
                        {
                            _discardPile.Add(card);
                            message = $"{responder.Name} сбрасывает {card.Name} из руки {stealTarget.Name}.";
                        }
                    }
                    else if (responseType == "equipment")
                    {
                        if (cardIndex == null || _pendingAction.RevealedCards == null ||
                            cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                        {
                            return new CommandResult(false, "Неверный выбор снаряжения.");
                        }
                        var equipCard = _pendingAction.RevealedCards[cardIndex.Value];
                        stealTarget.InPlay.Remove(equipCard);
                        if (isSteal)
                        {
                            responder.Hand.Add(equipCard);
                            message = $"{responder.Name} крадёт {equipCard.Name} у {stealTarget.Name}.";
                        }
                        else
                        {
                            _discardPile.Add(equipCard);
                            message = $"{responder.Name} сбрасывает {equipCard.Name} у {stealTarget.Name}.";
                        }
                    }
                    else
                    {
                        return new CommandResult(false, "Выберите источник: рука или снаряжение.");
                    }

                    _pendingAction = null;
                    break;
                }

                case PendingActionType.DiscardExcess:
                {
                    if (responseType != "play_card")
                    {
                        return new CommandResult(false, "Вы должны сбросить карту.");
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                    {
                        return new CommandResult(false, "Неверный индекс карты.");
                    }

                    var card = responder.Hand[cardIndex.Value];
                    responder.Hand.RemoveAt(cardIndex.Value);
                    _discardPile.Add(card);

                    if (responder.Hand.Count <= responder.Hp)
                    {
                        message = $"{responder.Name} сбрасывает {card.Name}. Лимит руки достигнут.";
                        _pendingAction = null;
                        ApplyEventEndOfTurnEffects(responder);
                        if (GameOver) break;
                        AdvanceTurn();
                    }
                    else
                    {
                        var remaining = responder.Hand.Count - responder.Hp;
                        message = $"{responder.Name} сбрасывает {card.Name}. Осталось сбросить: {remaining}.";
                    }
                    break;
                }

                case PendingActionType.JesseJonesSteal:
                {
                    var jesseTarget = string.IsNullOrWhiteSpace(targetId) ? null : FindByPublicId(targetId);
                    if (jesseTarget == null)
                    {
                        return new CommandResult(false, "Выберите игрока, у которого взять карту.");
                    }
                    if (!jesseTarget.IsAlive || jesseTarget.Hand.Count == 0)
                    {
                        return new CommandResult(false, $"У {jesseTarget.Name} нет карт для взятия.");
                    }
                    if (jesseTarget.Id == playerId)
                    {
                        return new CommandResult(false, "Нельзя тянуть карту у себя.");
                    }

                    var stealIdx = _random.Next(jesseTarget.Hand.Count);
                    var stolenCard = jesseTarget.Hand[stealIdx];
                    jesseTarget.Hand.RemoveAt(stealIdx);
                    responder.Hand.Add(stolenCard);
                    var remaining = Math.Max(0, 1 + GetEventDrawModifier());
                    if (remaining > 0) DrawCards(responder, remaining);
                    _pendingAction = null;
                    message = $"{responder.Name} тянет карту у {jesseTarget.Name} и {remaining} из колоды.";
                    break;
                }

                case PendingActionType.KitCarlsonPick:
                {
                    if (_pendingAction.RevealedCards == null || _pendingAction.RevealedCards.Count == 0)
                    {
                        _pendingAction = null;
                        message = "Больше нет карт для выбора.";
                        break;
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                    {
                        return new CommandResult(false, "Неверный выбор карты.");
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
                        message = $"{responder.Name} завершает набор.";
                    }
                    else
                    {
                        message = $"{responder.Name} берёт карту. Осталось выбрать: {_pendingAction.KitCarlsonPicksRemaining}.";
                    }
                    break;
                }

                case PendingActionType.DiscardForCost:
                {
                    if (responseType != "play_card")
                    {
                        return new CommandResult(false, "Вы должны сбросить карту.");
                    }

                    if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                    {
                        return new CommandResult(false, "Неверный индекс карты.");
                    }

                    var costCard = responder.Hand[cardIndex.Value];
                    responder.Hand.RemoveAt(cardIndex.Value);
                    _discardPile.Add(costCard);
                    CheckSuzyLafayette(responder);

                    var savedAction = _pendingAction;
                    _pendingAction = null;
                    message = ExecuteDeferredEffect(responder, savedAction);
                    break;
                }

                case PendingActionType.BrawlDefense:
                {
                    if (responseType == "play_card")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                        {
                            return new CommandResult(false, "Неверный индекс карты.");
                        }
                        var brawlCard = responder.Hand[cardIndex.Value];
                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(brawlCard);
                        CheckSuzyLafayette(responder);
                        message = $"{responder.Name} сбрасывает {brawlCard.Name} в Потасовке.";
                    }
                    else if (responseType == "equipment")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.InPlay.Count)
                        {
                            return new CommandResult(false, "Неверный индекс снаряжения.");
                        }
                        var equipCard = responder.InPlay[cardIndex.Value];
                        responder.InPlay.RemoveAt(cardIndex.Value);
                        _discardPile.Add(equipCard);
                        message = $"{responder.Name} сбрасывает {equipCard.Name} в Потасовке.";
                    }
                    else
                    {
                        return new CommandResult(false, "Выберите карту для сброса.");
                    }

                    _pendingAction.RespondingPlayerIds.Dequeue();
                    if (_pendingAction.RespondingPlayerIds.Count == 0)
                    {
                        _pendingAction = null;
                    }
                    break;
                }

                case PendingActionType.VeraCusterCopy:
                {
                    var veraTarget = string.IsNullOrWhiteSpace(targetId) ? null : FindByPublicId(targetId);
                    if (veraTarget == null || !veraTarget.IsAlive || veraTarget.Id == playerId)
                        return new CommandResult(false, "Выберите живого персонажа для копирования.");
                    responder.CopiedCharacterName = veraTarget.Character.Name;
                    _pendingAction = null;
                    message = $"{responder.Name} копирует способность {veraTarget.Character.Name} на этот ход.";
                    AddEvent(message);
                    ContinueDrawPhaseAfterVeraCuster(responder);
                    return new CommandResult(true, message, ToView(playerId));
                }

                case PendingActionType.PatBrennanDraw:
                {
                    if (responseType == "equipment")
                    {
                        if (cardIndex == null || _pendingAction.RevealedCards == null ||
                            cardIndex < 0 || cardIndex >= _pendingAction.RevealedCards.Count)
                            return new CommandResult(false, "Неверный выбор снаряжения.");
                        var equipCard = _pendingAction.RevealedCards[cardIndex.Value];
                        var owner = _players.Values.FirstOrDefault(p => p.InPlay.Contains(equipCard));
                        if (owner == null)
                        {
                            _pendingAction = null;
                            message = "Снаряжение больше недоступно.";
                            break;
                        }
                        owner.InPlay.Remove(equipCard);
                        responder.Hand.Add(equipCard);
                        _pendingAction = null;
                        message = $"{responder.Name} (Пат Бреннан) забирает {equipCard.Name} у {owner.Name}.";
                    }
                    else if (responseType == "draw_deck")
                    {
                        DrawCards(responder, 2);
                        _pendingAction = null;
                        message = $"{responder.Name} добирает 2 карты из колоды.";
                    }
                    else
                    {
                        return new CommandResult(false, "Выберите снаряжение или добор из колоды.");
                    }
                    break;
                }

                case PendingActionType.RussianRoulette:
                {
                    if (responseType == "play_card")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                            return new CommandResult(false, "Неверный индекс карты.");
                        var card = responder.Hand[cardIndex.Value];
                        var isJanet = responder.ActiveCharacterName == "Каламити Джанет";
                        if (card.Type != CardType.Bang && !(isJanet && card.Type == CardType.Missed))
                            return new CommandResult(false, "Нужно сбросить Бэнг!, чтобы выжить.");
                        responder.Hand.RemoveAt(cardIndex.Value);
                        _discardPile.Add(card);
                        CheckSuzyLafayette(responder);
                        message = $"{responder.Name} сбрасывает {card.Name} и выживает в Русской рулетке!";
                    }
                    else
                    {
                        responder.Hp -= 1;
                        message = $"{responder.Name} не может сбросить Бэнг! и теряет 1 ОЗ (Русская рулетка).";
                        if (responder.Hp <= 0)
                        {
                            responder.IsAlive = false;
                            HandlePlayerDeath(responder, responder);
                        }
                    }

                    _pendingAction.RespondingPlayerIds.Dequeue();
                    if (_pendingAction.RespondingPlayerIds.Count == 0)
                    {
                        _pendingAction = null;
                        // Continue Sheriff's turn after event resolves
                        ContinueTurnAfterEvent();
                    }
                    break;
                }

                case PendingActionType.TrainRobbery:
                {
                    if (responseType == "play_card")
                    {
                        if (cardIndex == null || cardIndex < 0 || cardIndex >= responder.Hand.Count)
                            return new CommandResult(false, "Неверный индекс карты.");
                        var card = responder.Hand[cardIndex.Value];
                        responder.Hand.RemoveAt(cardIndex.Value);
                        // Give card to the player on the left (next alive)
                        var leftNeighbor = GetNextAlivePlayerId(responder.Id);
                        if (leftNeighbor != null)
                        {
                            _players[leftNeighbor].Hand.Add(card);
                            message = $"{responder.Name} передаёт {card.Name} игроку {_players[leftNeighbor].Name}.";
                        }
                        else
                        {
                            _discardPile.Add(card);
                            message = $"{responder.Name} сбрасывает {card.Name}.";
                        }
                        CheckSuzyLafayette(responder);
                    }
                    else
                    {
                        responder.Hp -= 1;
                        message = $"{responder.Name} не передаёт карту и теряет 1 ОЗ (Ограбление поезда).";
                        if (responder.Hp <= 0)
                        {
                            responder.IsAlive = false;
                            HandlePlayerDeath(responder, responder);
                        }
                    }

                    _pendingAction.RespondingPlayerIds.Dequeue();
                    if (_pendingAction.RespondingPlayerIds.Count == 0)
                    {
                        _pendingAction = null;
                        // Continue Sheriff's turn after event resolves
                        ContinueTurnAfterEvent();
                    }
                    break;
                }

                default:
                    return new CommandResult(false, "Неизвестный тип действия.");
            }

            // Molly Stark: draw 1 card when playing a card out of turn
            if (mollyStarkDraw && responder.IsAlive && responder.ActiveCharacterName == "Молли Старк")
            {
                DrawCards(responder, 1);
                AddEvent($"{responder.Name} (Молли Старк) добирает 1 карту.");
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
}
