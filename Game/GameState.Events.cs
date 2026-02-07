partial class GameState
{
    private Stack<EventCard> _eventDeck = new();
    private EventCard? _currentEvent;
    private bool _deadManUsed;

    private void BuildEventDeck()
    {
        _currentEvent = null;
        _deadManUsed = false;
        var exp = _enabledExpansions & (Expansion.HighNoon | Expansion.FistfulOfCards);
        if (exp == Expansion.None)
        {
            _eventDeck = new Stack<EventCard>();
            return;
        }
        _eventDeck = EventCardLibrary.BuildDeck(_random, exp);
    }

    private bool IsEventActive(EventCardType type) => _currentEvent?.Type == type;

    private void AdvanceEvent()
    {
        // Remove ghost players from previous Ghost Town
        if (IsEventActive(EventCardType.GhostTown))
        {
            RemoveGhosts();
        }

        if (_eventDeck.Count == 0)
        {
            _currentEvent = null;
            return;
        }

        _currentEvent = _eventDeck.Pop();
        AddEvent($"Событие: {_currentEvent.Name} — {_currentEvent.Description}");

        ApplyEventImmediateEffects();
    }

    private void ApplyEventImmediateEffects()
    {
        if (_currentEvent == null) return;

        switch (_currentEvent.Type)
        {
            case EventCardType.TheDaltons:
            {
                // Discard all blue cards in play for all players
                foreach (var player in _players.Values.Where(p => p.IsAlive))
                {
                    var blueCards = player.InPlay
                        .Where(c => c.Category == CardCategory.Blue)
                        .ToList();
                    foreach (var card in blueCards)
                    {
                        player.InPlay.Remove(card);
                        _discardPile.Add(card);
                    }
                    if (blueCards.Count > 0)
                        AddEvent($"{player.Name} теряет {blueCards.Count} синих карт из-за Братьев Дальтон.");
                }
                break;
            }

            case EventCardType.GhostTown:
            {
                // Dead players return as ghosts with 1 HP
                foreach (var player in _players.Values.Where(p => !p.IsAlive))
                {
                    player.IsAlive = true;
                    player.Hp = 1;
                    player.IsGhost = true;
                    // Add back to turn order
                    if (!_turnOrder.Contains(player.Id))
                    {
                        _turnOrder.Add(player.Id);
                    }
                    AddEvent($"{player.Name} возвращается как призрак!");
                }
                break;
            }

            case EventCardType.NewIdentity:
            {
                // Rotate characters left
                var alivePlayers = _turnOrder.Where(id => _players[id].IsAlive).Select(id => _players[id]).ToList();
                if (alivePlayers.Count >= 2)
                {
                    var savedChars = alivePlayers.Select(p => p.Character).ToList();
                    for (var i = 0; i < alivePlayers.Count; i++)
                    {
                        var nextIdx = (i + 1) % alivePlayers.Count;
                        alivePlayers[i].AssignCharacter(savedChars[nextIdx]);
                        alivePlayers[i].CopiedCharacterName = null;
                    }
                    AddEvent("Персонажи сменились! Каждый получил персонажа соседа.");
                }
                break;
            }

            case EventCardType.RussianRoulette:
            {
                // Chain: each player plays Bang or takes 1 HP
                var responders = GetAlivePlayersInTurnOrder(
                    _turnOrder.Count > 0 ? _turnOrder[_turnIndex] : _players.Values.First(p => p.IsAlive).Id);
                if (responders.Count > 0)
                {
                    _pendingAction = new PendingAction(
                        PendingActionType.RussianRoulette,
                        responders[0],
                        responders);
                    AddEvent("Русская рулетка! Каждый должен сбросить Бэнг! или потерять 1 ОЗ.");
                }
                break;
            }

            case EventCardType.TrainRobbery:
            {
                // Each player gives card to left or takes 1 HP
                var responders = GetAlivePlayersInTurnOrder(
                    _turnOrder.Count > 0 ? _turnOrder[_turnIndex] : _players.Values.First(p => p.IsAlive).Id);
                if (responders.Count > 1)
                {
                    _pendingAction = new PendingAction(
                        PendingActionType.TrainRobbery,
                        responders[0],
                        responders);
                    AddEvent("Ограбление поезда! Передайте карту влево или потеряйте 1 ОЗ.");
                }
                break;
            }
        }
    }

    private void ApplyEventEndOfTurnEffects(PlayerState player)
    {
        if (_currentEvent == null || !player.IsAlive || GameOver) return;

        switch (_currentEvent.Type)
        {
            case EventCardType.HighNoon:
            case EventCardType.Shootout:
            {
                if (player.BangsPlayedThisTurn == 0)
                {
                    player.Hp -= 1;
                    AddEvent($"{player.Name} не сыграл Бэнг! — теряет 1 ОЗ (Полдень).");
                    if (player.Hp <= 0)
                    {
                        player.IsAlive = false;
                        HandlePlayerDeath(player, player);
                    }
                }
                break;
            }

            case EventCardType.AFistfulOfCards:
            {
                var cardCount = player.Hand.Count;
                if (cardCount > 0)
                {
                    player.Hp -= cardCount;
                    AddEvent($"{player.Name} теряет {cardCount} ОЗ за карты в руке (Пригоршня карт).");
                    if (player.Hp <= 0)
                    {
                        player.IsAlive = false;
                        HandlePlayerDeath(player, player);
                    }
                }
                break;
            }
        }
    }

    private void RemoveGhosts()
    {
        foreach (var player in _players.Values.Where(p => p.IsGhost).ToList())
        {
            player.IsGhost = false;
            player.IsAlive = false;
            player.Hp = 0;
            foreach (var card in player.Hand) _discardPile.Add(card);
            foreach (var card in player.InPlay) _discardPile.Add(card);
            player.Hand.Clear();
            player.InPlay.Clear();
            RemoveFromTurnOrder(player.Id);
            AddEvent($"{player.Name} (призрак) снова покидает игру.");
        }
    }

    private bool IsHangoverActive()
    {
        return IsEventActive(EventCardType.Hangover);
    }

    private bool IsJudgeActive()
    {
        return IsEventActive(EventCardType.Judge);
    }
}
