partial class GameState
{
    private void ApplyDamage(PlayerState attacker, PlayerState target, int damage, string verb)
    {
        target.Hp -= damage;
        if (target.Hp <= 0)
        {
            var aliveCount = _players.Values.Count(p => p.IsAlive);
            while (target.Hp <= 0 && aliveCount > 2)
            {
                var beerIndex = target.Hand.FindIndex(c => c.Type == CardType.Beer);
                if (beerIndex < 0) break;
                var beer = target.Hand[beerIndex];
                target.Hand.RemoveAt(beerIndex);
                _discardPile.Add(beer);
                target.Hp += target.ActiveCharacterName == "Текила Джо" ? 2 : 1;
                AddEvent($"{target.Name} использует Пиво, чтобы остаться в игре!");
            }

            if (target.Hp <= 0)
            {
                target.IsAlive = false;
                HandlePlayerDeath(attacker, target);
            }
        }

        if (target.IsAlive && !IsHangoverActive())
        {
            if (target.ActiveCharacterName == "Барт Кэссиди")
            {
                DrawCards(target, damage);
            }
            else if (target.ActiveCharacterName == "Эль Гринго" && attacker.Id != target.Id)
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
            p.IsAlive && p.Id != dead.Id && p.ActiveCharacterName == "Валчер Сэм");

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

        if (dead.Role == Role.Bandit && killer.Id != dead.Id)
        {
            DrawCards(killer, 3);
            AddEvent($"{killer.Name} получает 3 карты за устранение Бандита.");
        }

        // Vendetta: killer draws 3 extra cards
        if (IsEventActive(EventCardType.Vendetta) && killer.Id != dead.Id && killer.IsAlive)
        {
            DrawCards(killer, 3);
            AddEvent($"{killer.Name} получает 3 дополнительные карты (Вендетта).");
        }

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
            AddEvent($"{killer.Name} (Шериф) сбрасывает все карты за убийство Помощника!");
        }

        // Greg Digger: +2 HP when another player dies
        var gregDigger = _players.Values.FirstOrDefault(p =>
            p.IsAlive && p.Id != dead.Id && p.ActiveCharacterName == "Грег Диггер");
        if (gregDigger != null)
        {
            gregDigger.Hp = Math.Min(gregDigger.Hp + 2, gregDigger.MaxHp);
            AddEvent($"{gregDigger.Name} (Грег Диггер) восстанавливает 2 ОЗ.");
        }

        // Herb Hunter: +2 cards when another player dies
        var herbHunter = _players.Values.FirstOrDefault(p =>
            p.IsAlive && p.Id != dead.Id && p.ActiveCharacterName == "Херб Хантер");
        if (herbHunter != null)
        {
            DrawCards(herbHunter, 2);
            AddEvent($"{herbHunter.Name} (Херб Хантер) добирает 2 карты.");
        }

        // Dead Man: first eliminated player returns with 2 HP (once per game)
        if (IsEventActive(EventCardType.DeadMan) && !_deadManUsed)
        {
            _deadManUsed = true;
            dead.IsAlive = true;
            dead.Hp = 2;
            DrawCards(dead, 3);
            AddEvent($"{dead.Name} возвращается в игру с 2 ОЗ! (Мёртвый человек)");
            // Don't remove from turn order, don't check game over
            return;
        }

        RemoveFromTurnOrder(dead.Id);
        CheckForGameOver();
    }

    private void CheckSuzyLafayette(PlayerState player)
    {
        if (player.ActiveCharacterName == "Сьюзи Лафайет" && player.IsAlive && player.Hand.Count == 0)
        {
            DrawCards(player, 1);
        }
    }

    private bool IsBelleStarTurn()
    {
        if (_turnOrder.Count == 0) return false;
        var currentPlayer = _players[_turnOrder[_turnIndex]];
        return currentPlayer.ActiveCharacterName == "Белль Стар";
    }

    private bool CheckBarrel(PlayerState target)
    {
        if (IsBelleStarTurn()) return false;
        if (IsJudgeActive()) return false;
        if (!target.InPlay.Any(c => c.Type == CardType.Barrel)) return false;

        if (target.ActiveCharacterName == "Лаки Дьюк")
        {
            var card1 = DrawCheckCard();
            var card2 = DrawCheckCard();
            var success = card1.Suit == CardSuit.Hearts || card2.Suit == CardSuit.Hearts;
            AddEvent($"Проверка Бочки! {target.Name} (Лаки Дьюк): {FormatCheckCard(card1)} и {FormatCheckCard(card2)} \u2014 {(success ? "увернулся!" : "не повезло.")}");
            return success;
        }

        var check = DrawCheckCard();
        var result = check.Suit == CardSuit.Hearts;
        AddEvent($"Проверка Бочки! {target.Name}: {FormatCheckCard(check)} \u2014 {(result ? "увернулся!" : "не повезло.")}");
        return result;
    }

    private string FormatAttackMessage(PlayerState attacker, PlayerState target, string verb, int damage)
    {
        if (!target.IsAlive)
        {
            return $"{attacker.Name} {verb} {target.Name} на {damage} урона. {target.Name} выбыл!";
        }

        return $"{attacker.Name} {verb} {target.Name} на {damage} урона.";
    }
}
