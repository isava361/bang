static class EventCardLibrary
{
    private static readonly List<EventCard> AllEvents = new()
    {
        // === High Noon ===
        new(EventCardType.TheDaltons, "Братья Дальтон", "Все синие карты в игре сбрасываются.", Expansion.HighNoon),
        new(EventCardType.Thirst, "Жажда", "Каждый игрок добирает на 1 карту меньше.", Expansion.HighNoon),
        new(EventCardType.Hangover, "Похмелье", "Способности персонажей не действуют.", Expansion.HighNoon),
        new(EventCardType.Blessing, "Благословение", "Все «проверки» считаются червами.", Expansion.HighNoon),
        new(EventCardType.GhostTown, "Город-призрак", "Устранённые игроки возвращаются с 1 ОЗ на 1 раунд.", Expansion.HighNoon),
        new(EventCardType.GoldRush, "Золотая лихорадка", "Каждый игрок добирает на 1 карту больше.", Expansion.HighNoon),
        new(EventCardType.Shootout, "Перестрелка", "Кто не сыграл Бэнг! в свой ход — теряет 1 ОЗ.", Expansion.HighNoon),
        new(EventCardType.TheDoctor, "Доктор", "В свой ход: сбросьте 2 карты — восстановите 1 ОЗ.", Expansion.HighNoon),
        new(EventCardType.TrainRobbery, "Ограбление поезда", "Каждый передаёт карту влево или теряет 1 ОЗ.", Expansion.HighNoon),
        new(EventCardType.Sermon, "Проповедь", "Нельзя играть Бэнг!.", Expansion.HighNoon),
        new(EventCardType.TheReverend, "Священник", "Нельзя играть Пиво.", Expansion.HighNoon),
        new(EventCardType.NewIdentity, "Новая личность", "Персонажи меняются: каждый получает персонажа соседа слева.", Expansion.HighNoon),
        new(EventCardType.HighNoon, "Полдень", "Кто не сыграл Бэнг! в свой ход — теряет 1 ОЗ.", Expansion.HighNoon),

        // === A Fistful of Cards ===
        new(EventCardType.DeadMan, "Мертвец", "Первый устранённый игрок возвращается с 2 ОЗ.", Expansion.FistfulOfCards),
        new(EventCardType.FistfulOfCards, "Горсть карт", "Добор: возьмите 1 карту у игрока справа.", Expansion.FistfulOfCards),
        new(EventCardType.LawOfTheWest, "Закон Запада", "Добранные карты видны всем.", Expansion.FistfulOfCards),
        new(EventCardType.Sniper, "Снайпер", "Бэнг! игнорирует дистанцию.", Expansion.FistfulOfCards),
        new(EventCardType.Judge, "Судья", "Синие карты в игре не действуют.", Expansion.FistfulOfCards),
        new(EventCardType.Peyote, "Пейот", "Вместо колоды, берите карты из сброса.", Expansion.FistfulOfCards),
        new(EventCardType.Ricochet, "Рикошет", "Если Мимо! сыграно — атакующий получает 1 урон.", Expansion.FistfulOfCards),
        new(EventCardType.RussianRoulette, "Русская рулетка", "По кругу: сбросьте Бэнг! или потеряйте 1 ОЗ.", Expansion.FistfulOfCards),
        new(EventCardType.HardLiquor, "Крепкий алкоголь", "Пиво восстанавливает 2 ОЗ.", Expansion.FistfulOfCards),
        new(EventCardType.Vendetta, "Вендетта", "Убивший игрока добирает 3 дополнительные карты.", Expansion.FistfulOfCards),
        new(EventCardType.WildWestShow, "Шоу Дикого Запада", "Карты в руках видны всем.", Expansion.FistfulOfCards),
        new(EventCardType.AbandonedMine, "Заброшенная шахта", "Берите карты из сброса вместо колоды.", Expansion.FistfulOfCards),
        new(EventCardType.AFistfulOfCards, "Пригоршня карт", "В конце хода: -1 ОЗ за каждую карту в руке.", Expansion.FistfulOfCards),
    };

    // Final events go at the bottom of their respective decks
    private static readonly HashSet<EventCardType> FinalEvents = new()
    {
        EventCardType.HighNoon,
        EventCardType.AFistfulOfCards
    };

    public static Stack<EventCard> BuildDeck(Random random, Expansion enabledExpansions)
    {
        var events = new List<EventCard>();
        var finals = new List<EventCard>();

        foreach (var ev in AllEvents)
        {
            if ((enabledExpansions & ev.Expansion) == 0) continue;
            if (FinalEvents.Contains(ev.Type))
                finals.Add(ev);
            else
                events.Add(ev);
        }

        // Shuffle non-final events
        var shuffled = events.OrderBy(_ => random.Next()).ToList();
        // Add final events at the bottom (they'll be drawn last)
        shuffled.AddRange(finals.OrderBy(_ => random.Next()));

        // Stack: push in reverse so first shuffled = top
        var deck = new Stack<EventCard>();
        for (var i = shuffled.Count - 1; i >= 0; i--)
        {
            deck.Push(shuffled[i]);
        }
        return deck;
    }

    public static List<EventCard> GetAll() => AllEvents.ToList();
}
