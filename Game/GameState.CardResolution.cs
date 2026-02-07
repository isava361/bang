partial class GameState
{
    private string ResolveBang(PlayerState attacker, PlayerState target)
    {
        var damage = attacker.ActiveCharacterName == "Слэб Убийца" ? 2 : 1;

        if (CheckBarrel(target))
        {
            return $"{attacker.Name} стреляет в {target.Name}, но Бочка спасает {target.Name}!";
        }

        _pendingAction = new PendingAction(
            PendingActionType.BangDefense,
            attacker.Id,
            new[] { target.Id },
            damage);
        return $"{attacker.Name} стреляет в {target.Name}! {target.Name} должен ответить.";
    }

    private string ResolveBeer(PlayerState player)
    {
        if (player.Hp >= player.MaxHp)
        {
            return $"У {player.Name} уже максимальное здоровье.";
        }

        var heal = player.ActiveCharacterName == "Текила Джо" ? 2 : 1;
        // Hard Liquor: Beer heals 2 HP
        if (IsEventActive(EventCardType.HardLiquor) && heal < 2) heal = 2;
        player.Hp = Math.Min(player.Hp + heal, player.MaxHp);
        return $"{player.Name} выпивает Пиво и восстанавливает {heal} ОЗ.";
    }

    private string ResolveGatling(PlayerState attacker, CardSuit? attackingSuit = null)
    {
        var allResponders = GetOtherAlivePlayersInTurnOrder(attacker.Id);

        // Apache Kid: immune to Diamond suit cards
        if (attackingSuit == CardSuit.Diamonds)
        {
            var immune = allResponders.Where(id => _players[id].ActiveCharacterName == "Апач Кид").ToList();
            foreach (var id in immune)
            {
                allResponders.Remove(id);
                AddEvent($"{_players[id].Name} (Апач Кид) неуязвим к бубновым картам!");
            }
        }

        if (allResponders.Count == 0)
        {
            return $"{attacker.Name} стреляет из Гатлинга, но некого поразить.";
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

        var barrelMsg = barrelSaved.Count > 0 ? $" {string.Join(", ", barrelSaved)} увернулись с помощью Бочки!" : "";

        if (needsResponse.Count == 0)
        {
            return $"{attacker.Name} стреляет из Гатлинга!{barrelMsg} Все в безопасности.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.GatlingDefense,
            attacker.Id,
            needsResponse);
        return $"{attacker.Name} стреляет из Гатлинга!{barrelMsg} Оставшиеся игроки должны сыграть Мимо! или получить 1 урон.";
    }

    private string ResolveStagecoach(PlayerState player)
    {
        DrawCards(player, 2);
        return $"{player.Name} играет Дилижанс и тянет 2 карты.";
    }

    private string ResolveCatBalou(PlayerState attacker, PlayerState target)
        => ResolveStealOrDiscard(attacker, target, "discard", "Красотка");

    private string ResolveCanCan(PlayerState attacker, PlayerState target)
        => ResolveStealOrDiscard(attacker, target, "discard", "Канкан");

    private string ResolveStealOrDiscard(PlayerState attacker, PlayerState target, string mode, string cardName)
    {
        var isSteal = mode == "steal";
        if (target.Hand.Count == 0 && target.InPlay.Count == 0)
        {
            return isSteal
                ? $"{target.Name} не имеет карт для кражи."
                : $"{target.Name} не имеет карт для сброса.";
        }

        if (target.Hand.Count > 0 && target.InPlay.Count > 0)
        {
            _pendingAction = new PendingAction(
                PendingActionType.ChooseStealSource,
                attacker.Id,
                new[] { attacker.Id });
            _pendingAction.StealTargetId = target.Id;
            _pendingAction.StealMode = mode;
            _pendingAction.RevealedCards = target.InPlay.ToList();
            return $"{attacker.Name} использует {cardName} против {target.Name}! Выберите: случайная карта из руки или снаряжение.";
        }

        if (target.Hand.Count > 0)
        {
            var idx = _random.Next(target.Hand.Count);
            var card = target.Hand[idx];
            target.Hand.RemoveAt(idx);
            if (isSteal)
            {
                attacker.Hand.Add(card);
                return $"{attacker.Name} использует {cardName} и крадёт карту из руки {target.Name}.";
            }
            _discardPile.Add(card);
            return $"{attacker.Name} использует {cardName} против {target.Name} и сбрасывает {card.Name}.";
        }

        var equip = target.InPlay[_random.Next(target.InPlay.Count)];
        target.InPlay.Remove(equip);
        if (isSteal)
        {
            attacker.Hand.Add(equip);
            return $"{attacker.Name} использует {cardName} и крадёт {equip.Name} у {target.Name}.";
        }
        _discardPile.Add(equip);
        return $"{attacker.Name} использует {cardName} против {target.Name} и сбрасывает {equip.Name}.";
    }

    private string ResolveIndians(PlayerState attacker, CardSuit? attackingSuit = null)
    {
        var responders = GetOtherAlivePlayersInTurnOrder(attacker.Id);

        // Apache Kid: immune to Diamond suit cards
        if (attackingSuit == CardSuit.Diamonds)
        {
            var immune = responders.Where(id => _players[id].ActiveCharacterName == "Апач Кид").ToList();
            foreach (var id in immune)
            {
                responders.Remove(id);
                AddEvent($"{_players[id].Name} (Апач Кид) неуязвим к бубновым картам!");
            }
        }

        if (responders.Count == 0)
        {
            return $"{attacker.Name} играет Индейцы!, но некого атаковать.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.IndiansDefense,
            attacker.Id,
            responders);
        return $"{attacker.Name} играет Индейцы! Каждый должен сбросить Бэнг! или получить 1 урон.";
    }

    private string ResolveDuel(PlayerState attacker, PlayerState target)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DuelChallenge,
            attacker.Id,
            new[] { target.Id });
        _pendingAction.DuelPlayerA = attacker.Id;
        _pendingAction.DuelPlayerB = target.Id;
        return $"{attacker.Name} вызывает {target.Name} на дуэль!";
    }

    private string ResolvePanic(PlayerState attacker, PlayerState target)
        => ResolveStealOrDiscard(attacker, target, "steal", "Паника!");

    private string ResolveConestoga(PlayerState attacker, PlayerState target)
        => ResolveStealOrDiscard(attacker, target, "steal", "Фургон");

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

        return $"{player.Name} открывает Салун. Все восстанавливают 1 ОЗ.";
    }

    private string ResolveWellsFargo(PlayerState player)
    {
        DrawCards(player, 3);
        return $"{player.Name} грабит Уэллс Фарго и тянет 3 карты.";
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
            return $"{player.Name} заходит в Магазин, но полки пусты.";
        }

        _pendingAction = new PendingAction(
            PendingActionType.GeneralStorePick,
            player.Id,
            alivePlayers);
        _pendingAction.RevealedCards = revealedCards;
        return $"{player.Name} открывает Магазин! Каждый выбирает карту.";
    }

    private string ResolvePunch(PlayerState attacker, PlayerState target)
    {
        if (CheckBarrel(target))
        {
            return $"{attacker.Name} бьёт {target.Name}, но Бочка спасает!";
        }

        _pendingAction = new PendingAction(
            PendingActionType.BangDefense,
            attacker.Id,
            new[] { target.Id });
        return $"{attacker.Name} бьёт {target.Name}! {target.Name} должен ответить.";
    }

    private string ResolveSpringfield(PlayerState attacker, PlayerState target)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DiscardForCost,
            attacker.Id,
            new[] { attacker.Id });
        _pendingAction.DeferredCardType = CardType.Springfield;
        _pendingAction.DeferredTargetId = target.Id;
        return $"{attacker.Name} использует Спрингфилд против {target.Name}! Сбросьте карту из руки.";
    }

    private string ResolveWhisky(PlayerState player)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DiscardForCost,
            player.Id,
            new[] { player.Id });
        _pendingAction.DeferredCardType = CardType.Whisky;
        return $"{player.Name} пьёт Виски! Сбросьте карту из руки.";
    }

    private string ResolveTequila(PlayerState player, PlayerState target)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DiscardForCost,
            player.Id,
            new[] { player.Id });
        _pendingAction.DeferredCardType = CardType.Tequila;
        _pendingAction.DeferredTargetId = target.Id;
        return $"{player.Name} угощает {target.Name} Текилой! Сбросьте карту из руки.";
    }

    private string ResolveRagTime(PlayerState player, PlayerState target)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DiscardForCost,
            player.Id,
            new[] { player.Id });
        _pendingAction.DeferredCardType = CardType.RagTime;
        _pendingAction.DeferredTargetId = target.Id;
        return $"{player.Name} играет Рэгтайм против {target.Name}! Сбросьте карту из руки.";
    }

    private string ResolveBrawl(PlayerState player, CardSuit? attackingSuit = null)
    {
        _pendingAction = new PendingAction(
            PendingActionType.DiscardForCost,
            player.Id,
            new[] { player.Id });
        _pendingAction.DeferredCardType = CardType.Brawl;
        _pendingAction.AttackingSuit = attackingSuit;
        return $"{player.Name} начинает Потасовку! Сбросьте карту из руки.";
    }

    private string ExecuteDeferredEffect(PlayerState player, PendingAction action)
    {
        var targetPlayer = action.DeferredTargetId != null && _players.ContainsKey(action.DeferredTargetId)
            ? _players[action.DeferredTargetId]
            : null;

        switch (action.DeferredCardType)
        {
            case CardType.Springfield:
                if (targetPlayer != null && targetPlayer.IsAlive)
                {
                    ApplyDamage(player, targetPlayer, 1, "стреляет из Спрингфилда в");
                    return FormatAttackMessage(player, targetPlayer, "стреляет из Спрингфилда в", 1);
                }
                return "Цель больше не доступна.";

            case CardType.Whisky:
                player.Hp = Math.Min(player.Hp + 2, player.MaxHp);
                return $"{player.Name} выпивает Виски и восстанавливает 2 ОЗ.";

            case CardType.Tequila:
                if (targetPlayer != null && targetPlayer.IsAlive)
                {
                    targetPlayer.Hp = Math.Min(targetPlayer.Hp + 1, targetPlayer.MaxHp);
                    return $"{targetPlayer.Name} восстанавливает 1 ОЗ благодаря Текиле.";
                }
                return "Цель больше не доступна.";

            case CardType.RagTime:
                if (targetPlayer == null || !targetPlayer.IsAlive)
                    return "Цель больше не доступна.";
                return ResolveStealOrDiscard(player, targetPlayer, "steal", "Рэгтайм");

            case CardType.Brawl:
                var brawlTargets = GetOtherAlivePlayersInTurnOrder(player.Id)
                    .Where(id => _players[id].Hand.Count > 0 || _players[id].InPlay.Count > 0)
                    .ToList();
                // Apache Kid: immune to Diamond suit
                if (action.AttackingSuit == CardSuit.Diamonds)
                {
                    var immuneBrawl = brawlTargets.Where(id => _players[id].ActiveCharacterName == "Апач Кид").ToList();
                    foreach (var id in immuneBrawl)
                    {
                        brawlTargets.Remove(id);
                        AddEvent($"{_players[id].Name} (Апач Кид) неуязвим к бубновым картам!");
                    }
                }
                if (brawlTargets.Count == 0)
                    return $"{player.Name} устроил Потасовку, но никому нечего сбрасывать.";
                _pendingAction = new PendingAction(
                    PendingActionType.BrawlDefense,
                    player.Id,
                    brawlTargets);
                return $"{player.Name} устроил Потасовку! Каждый игрок должен сбросить карту.";

            default:
                return "Неизвестный эффект.";
        }
    }

    private bool TryGetTarget(string? targetPublicId, string playerId, out PlayerState target, out string error, bool allowSelf = false)
    {
        target = null!;
        if (string.IsNullOrWhiteSpace(targetPublicId))
        {
            error = "Игрок-цель не найден.";
            return false;
        }

        var found = FindByPublicId(targetPublicId);
        if (found == null)
        {
            error = "Игрок-цель не найден.";
            return false;
        }
        target = found;

        if (!allowSelf && target.Id == playerId)
        {
            error = "Нельзя выбирать себя в качестве цели.";
            return false;
        }

        if (!target.IsAlive)
        {
            error = $"{target.Name} уже выбыл.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
