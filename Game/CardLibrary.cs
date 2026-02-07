static class CardLibrary
{
    private static readonly Dictionary<CardType, CardDefinition> Definitions = new()
    {
        // === Base game ===
        { CardType.Bang, new CardDefinition("Бэнг!", CardType.Bang, CardCategory.Brown, "Нанесите 1 урон цели (2, если вы Слэб Убийца).", true, "Выберите игрока для выстрела", "/assets/cards/bang.webp") },
        { CardType.Missed, new CardDefinition("Мимо!", CardType.Missed, CardCategory.Brown, "Сыграйте в ответ на выстрел, чтобы отменить урон.", false, null, "/assets/cards/missed.webp") },
        { CardType.Beer, new CardDefinition("Пиво", CardType.Beer, CardCategory.Brown, "Восстановите 1 ОЗ.", false, null, "/assets/cards/beer.webp") },
        { CardType.Gatling, new CardDefinition("Гатлинг", CardType.Gatling, CardCategory.Brown, "Каждый другой игрок должен сыграть Мимо! или получить 1 урон.", false, null, "/assets/cards/gatling.webp") },
        { CardType.Stagecoach, new CardDefinition("Дилижанс", CardType.Stagecoach, CardCategory.Brown, "Доберите 2 карты.", false, null, "/assets/cards/stagecoach.webp") },
        { CardType.CatBalou, new CardDefinition("Красотка", CardType.CatBalou, CardCategory.Brown, "Заставьте цель сбросить карту (рука или снаряжение).", true, "Выберите игрока для сброса", "/assets/cards/cat_balou.webp") },
        { CardType.Indians, new CardDefinition("Индейцы!", CardType.Indians, CardCategory.Brown, "Каждый другой игрок должен сбросить Бэнг! или получить 1 урон.", false, null, "/assets/cards/indians.webp") },
        { CardType.Duel, new CardDefinition("Дуэль", CardType.Duel, CardCategory.Brown, "Вызовите игрока на дуэль — по очереди сбрасывайте Бэнг!. Кто не сможет, получает 1 урон.", true, "Выберите соперника для дуэли", "/assets/cards/duel.webp") },
        { CardType.Panic, new CardDefinition("Паника!", CardType.Panic, CardCategory.Brown, "Украдите карту у игрока на дистанции 1 (рука или снаряжение).", true, "Выберите игрока для кражи", "/assets/cards/panic.webp") },
        { CardType.Saloon, new CardDefinition("Салун", CardType.Saloon, CardCategory.Brown, "Все живые игроки восстанавливают 1 ОЗ.", false, null, "/assets/cards/saloon.webp") },
        { CardType.WellsFargo, new CardDefinition("Уэллс Фарго", CardType.WellsFargo, CardCategory.Brown, "Доберите 3 карты.", false, null, "/assets/cards/wells_fargo.webp") },
        { CardType.GeneralStore, new CardDefinition("Магазин", CardType.GeneralStore, CardCategory.Brown, "Откройте карты по числу живых игроков. Каждый выбирает по очереди.", false, null, "/assets/cards/general_store.webp") },
        { CardType.Barrel, new CardDefinition("Бочка", CardType.Barrel, CardCategory.Blue, "При выстреле выполните «проверку»: если червы, выстрел избегается.", false, null, "/assets/cards/barrel.webp") },
        { CardType.Mustang, new CardDefinition("Мустанг", CardType.Mustang, CardCategory.Blue, "Другие видят вас на дистанции +1.", false, null, "/assets/cards/mustang.webp") },
        { CardType.Scope, new CardDefinition("Прицел", CardType.Scope, CardCategory.Blue, "Вы видите других на дистанции -1.", false, null, "/assets/cards/scope.webp") },
        { CardType.Volcanic, new CardDefinition("Вулканик", CardType.Volcanic, CardCategory.Weapon, "Оружие (дальность 1). Можно играть Бэнг! без ограничения за ход.", false, null, "/assets/cards/volcanic.webp") },
        { CardType.Schofield, new CardDefinition("Скофилд", CardType.Schofield, CardCategory.Weapon, "Оружие (дальность 2).", false, null, "/assets/cards/schofield.webp") },
        { CardType.Remington, new CardDefinition("Ремингтон", CardType.Remington, CardCategory.Weapon, "Оружие (дальность 3).", false, null, "/assets/cards/remington.webp") },
        { CardType.RevCarabine, new CardDefinition("Карабин", CardType.RevCarabine, CardCategory.Weapon, "Оружие (дальность 4).", false, null, "/assets/cards/rev_carabine.webp") },
        { CardType.Winchester, new CardDefinition("Винчестер", CardType.Winchester, CardCategory.Weapon, "Оружие (дальность 5).", false, null, "/assets/cards/winchester.webp") },
        { CardType.Jail, new CardDefinition("Тюрьма", CardType.Jail, CardCategory.Blue, "Сыграйте на другого игрока. В начале хода он делает проверку — при неудаче ход пропускается.", true, "Выберите игрока для тюрьмы", "/assets/cards/jail.webp") },
        { CardType.Dynamite, new CardDefinition("Динамит", CardType.Dynamite, CardCategory.Blue, "Сыграйте на себя. Переходит между игроками. Может взорваться и нанести 3 урона.", false, null, "/assets/cards/dynamite.webp") },

        // === Dodge City — Brown ===
        { CardType.Punch, new CardDefinition("Удар", CardType.Punch, CardCategory.Brown, "Нанесите 1 урон цели на дистанции 1. Не считается как Бэнг!.", true, "Выберите игрока для удара", "/assets/cards/punch.svg") },
        { CardType.Springfield, new CardDefinition("Спрингфилд", CardType.Springfield, CardCategory.Brown, "Сбросьте 1 карту из руки — цель на любой дистанции получает 1 урон.", true, "Выберите цель для Спрингфилда", "/assets/cards/springfield.svg") },
        { CardType.Dodge, new CardDefinition("Уворот", CardType.Dodge, CardCategory.Brown, "Как Мимо!, но после использования доберите 1 карту.", false, null, "/assets/cards/dodge.svg") },
        { CardType.Whisky, new CardDefinition("Виски", CardType.Whisky, CardCategory.Brown, "Сбросьте 1 карту из руки — восстановите 2 ОЗ.", false, null, "/assets/cards/whisky.svg") },
        { CardType.Tequila, new CardDefinition("Текила", CardType.Tequila, CardCategory.Brown, "Сбросьте 1 карту из руки — любой игрок восстанавливает 1 ОЗ.", true, "Выберите игрока для лечения", "/assets/cards/tequila.svg") },
        { CardType.RagTime, new CardDefinition("Рэгтайм", CardType.RagTime, CardCategory.Brown, "Сбросьте 1 карту из руки — украдите карту у любого игрока.", true, "Выберите цель для кражи", "/assets/cards/rag_time.svg") },
        { CardType.Brawl, new CardDefinition("Потасовка", CardType.Brawl, CardCategory.Brown, "Сбросьте 1 карту из руки — каждый другой игрок сбрасывает 1 карту.", false, null, "/assets/cards/brawl.svg") },
        { CardType.CanCan, new CardDefinition("Канкан", CardType.CanCan, CardCategory.Brown, "Заставьте любого игрока сбросить карту (любая дистанция).", true, "Выберите игрока для сброса", "/assets/cards/can_can.svg") },
        { CardType.Conestoga, new CardDefinition("Фургон", CardType.Conestoga, CardCategory.Brown, "Украдите карту у любого игрока (любая дистанция).", true, "Выберите игрока для кражи", "/assets/cards/conestoga.svg") },

        // === Dodge City — Green (active) ===
        { CardType.Derringer, new CardDefinition("Дерринджер", CardType.Derringer, CardCategory.Green, "Активируйте: Бэнг! на дистанции 1 + доберите 1 карту.", false, null, "/assets/cards/derringer.svg") },
        { CardType.Pepperbox, new CardDefinition("Пепербокс", CardType.Pepperbox, CardCategory.Green, "Активируйте: Бэнг! на любой дистанции.", false, null, "/assets/cards/pepperbox.svg") },
        { CardType.Howitzer, new CardDefinition("Гаубица", CardType.Howitzer, CardCategory.Green, "Активируйте: как Гатлинг (все получают 1 урон или Мимо!).", false, null, "/assets/cards/howitzer.svg") },
        { CardType.BuffaloRifle, new CardDefinition("Буффало", CardType.BuffaloRifle, CardCategory.Green, "Активируйте: Бэнг! на любой дистанции.", false, null, "/assets/cards/buffalo_rifle.svg") },
        { CardType.Canteen, new CardDefinition("Фляга", CardType.Canteen, CardCategory.Green, "Активируйте: восстановите 1 ОЗ.", false, null, "/assets/cards/canteen.svg") },

        // === Dodge City — Green (reactive) ===
        { CardType.Bible, new CardDefinition("Библия", CardType.Bible, CardCategory.Green, "Реакция: уклонитесь от Бэнг! (из снаряжения).", false, null, "/assets/cards/bible.svg", IsReactiveGreen: true) },
        { CardType.IronPlate, new CardDefinition("Железный щит", CardType.IronPlate, CardCategory.Green, "Реакция: уклонитесь от Бэнг! (из снаряжения).", false, null, "/assets/cards/iron_plate.svg", IsReactiveGreen: true) },
        { CardType.Sombrero, new CardDefinition("Сомбреро", CardType.Sombrero, CardCategory.Green, "Реакция: уклонитесь от Бэнг! (из снаряжения).", false, null, "/assets/cards/sombrero.svg", IsReactiveGreen: true) },
        { CardType.TenGallonHat, new CardDefinition("Шляпа", CardType.TenGallonHat, CardCategory.Green, "Реакция: уклонитесь от Бэнг! (из снаряжения).", false, null, "/assets/cards/ten_gallon_hat.svg", IsReactiveGreen: true) },

        // === Dodge City — Blue ===
        { CardType.Hideout, new CardDefinition("Укрытие", CardType.Hideout, CardCategory.Blue, "Другие видят вас на дистанции +1.", false, null, "/assets/cards/hideout.svg") },
        { CardType.Silver, new CardDefinition("Серебро", CardType.Silver, CardCategory.Blue, "Вы видите других на дистанции -1.", false, null, "/assets/cards/silver.svg") },
    };

    public static CardDefinition Get(CardType type) => Definitions[type];
}
