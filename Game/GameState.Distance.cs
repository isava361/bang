partial class GameState
{
    private int GetBangLimit(PlayerState player)
    {
        if (player.ActiveCharacterName == "Уилли Кид") return int.MaxValue;
        if (!IsJudgeActive() && player.InPlay.Any(c => c.Type == CardType.Volcanic)) return int.MaxValue;
        return 1;
    }

    private int GetWeaponRange(PlayerState player)
    {
        if (IsJudgeActive()) return 1;
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

        var blueInactive = IsBelleStarTurn() || IsJudgeActive();
        if (!blueInactive)
        {
            if (target.InPlay.Any(c => c.Type == CardType.Mustang)) baseDistance += 1;
            if (target.InPlay.Any(c => c.Type == CardType.Hideout)) baseDistance += 1;
        }
        if (target.ActiveCharacterName == "Пол Регрет") baseDistance += 1;

        if (!blueInactive)
        {
            if (source.InPlay.Any(c => c.Type == CardType.Scope)) baseDistance -= 1;
            if (source.InPlay.Any(c => c.Type == CardType.Silver)) baseDistance -= 1;
        }
        if (source.ActiveCharacterName == "Роуз Дулан") baseDistance -= 1;

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
}
