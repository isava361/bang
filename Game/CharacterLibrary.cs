static class CharacterLibrary
{
    private static readonly List<CharacterDefinition> Characters = new()
    {
        // === Base game ===
        new("Лаки Дьюк", 4, "При «проверке» откройте 2 карты и выберите лучший результат.", "/assets/characters/lucky_duke.webp"),
        new("Слэб Убийца", 4, "Ваши Бэнг! наносят 2 урона.", "/assets/characters/slab_the_killer.webp"),
        new("Эль Гринго", 3, "При получении урона возьмите карту из руки атакующего.", "/assets/characters/el_gringo.webp"),
        new("Сьюзи Лафайет", 4, "Когда рука становится пустой, возьмите 1 карту.", "/assets/characters/suzy_lafayette.webp"),
        new("Роуз Дулан", 4, "Встроенный Прицел: вы видите других на дистанции -1.", "/assets/characters/rose_doolan.webp"),
        new("Джесси Джонс", 4, "Первую карту берите из руки выбранного игрока.", "/assets/characters/jesse_jones.webp"),
        new("Барт Кэссиди", 4, "Каждый раз при получении урона берите 1 карту из колоды.", "/assets/characters/bart_cassidy.webp"),
        new("Пол Регрет", 3, "Встроенный Мустанг: другие видят вас на дистанции +1.", "/assets/characters/paul_regret.webp"),
        new("Каламити Джанет", 4, "Используйте Бэнг! как Мимо! и Мимо! как Бэнг!.", "/assets/characters/calamity_janet.webp"),
        new("Кит Карлсон", 4, "Посмотрите 3 верхние карты, оставьте 2, 1 верните.", "/assets/characters/kit_carlson.webp"),
        new("Уилли Кид", 4, "Можно играть Бэнг! без ограничения за ход.", "/assets/characters/willy_the_kid.webp"),
        new("Сид Кетчум", 4, "Сбросьте 2 карты, чтобы восстановить 1 ОЗ (в свой ход).", "/assets/characters/sid_ketchum.webp"),
        new("Валчер Сэм", 4, "Когда игрок устранён, вы забираете все его карты.", "/assets/characters/vulture_sam.webp"),
        new("Педро Рамирес", 4, "Первую карту берите с верхней карты сброса.", "/assets/characters/pedro_ramirez.webp"),

        // === Dodge City ===
        new("Апач Кид", 3, "Карты бубен не влияют на вас.", "/assets/characters/apache_kid.svg", IsDodgeCity: true),
        new("Белль Стар", 4, "В ваш ход у других игроков нет активных синих карт.", "/assets/characters/belle_star.svg", IsDodgeCity: true),
        new("Билл Ноуфейс", 4, "Добирайте 1 + 1 за каждое ранение.", "/assets/characters/bill_noface.svg", IsDodgeCity: true),
        new("Чак Венгам", 4, "В свой ход: потеряйте 1 ОЗ, доберите 2 карты.", "/assets/characters/chuck_wengam.svg", IsDodgeCity: true),
        new("Док Холидэй", 4, "Сбросьте 2 карты — стреляйте в любого (1 раз за ход).", "/assets/characters/doc_holyday.svg", IsDodgeCity: true),
        new("Елена Фуэнте", 3, "Любая карта из руки может быть сыграна как Мимо!.", "/assets/characters/elena_fuente.svg", IsDodgeCity: true),
        new("Грег Диггер", 4, "При устранении другого игрока восстановите 2 ОЗ.", "/assets/characters/greg_digger.svg", IsDodgeCity: true),
        new("Херб Хантер", 4, "При устранении другого игрока доберите 2 карты.", "/assets/characters/herb_hunter.svg", IsDodgeCity: true),
        new("Хосе Дельгадо", 4, "Сбросьте синюю карту — доберите 2 (до 2 раз за ход).", "/assets/characters/jose_delgado.svg", IsDodgeCity: true),
        new("Молли Старк", 4, "Когда играете карту вне своего хода, доберите 1 карту.", "/assets/characters/molly_stark.svg", IsDodgeCity: true),
        new("Пат Бреннан", 4, "Вместо добора возьмите 1 карту из чужого снаряжения.", "/assets/characters/pat_brennan.svg", IsDodgeCity: true),
        new("Пикси Пит", 3, "Добирайте 3 карты вместо 2.", "/assets/characters/pixie_pete.svg", IsDodgeCity: true),
        new("Шон Мэллори", 3, "Нет лимита карт в руке.", "/assets/characters/sean_mallory.svg", IsDodgeCity: true),
        new("Текила Джо", 4, "Пиво восстанавливает 2 ОЗ вместо 1.", "/assets/characters/tequila_joe.svg", IsDodgeCity: true),
        new("Вера Кастер", 3, "В начале хода скопируйте способность другого игрока.", "/assets/characters/vera_custer.svg", IsDodgeCity: true),
    };

    public static CharacterDefinition Draw(Random random, HashSet<int> usedIndices, bool includeDodgeCity = false)
    {
        var available = Enumerable.Range(0, Characters.Count)
            .Where(i => !usedIndices.Contains(i))
            .Where(i => includeDodgeCity || !Characters[i].IsDodgeCity)
            .ToList();
        if (available.Count == 0)
        {
            available = Enumerable.Range(0, Characters.Count)
                .Where(i => includeDodgeCity || !Characters[i].IsDodgeCity)
                .ToList();
            usedIndices.Clear();
        }

        var index = available[random.Next(available.Count)];
        usedIndices.Add(index);
        return Characters[index];
    }

    public static List<CharacterDefinition> GetAll(bool includeDodgeCity = false)
    {
        return includeDodgeCity
            ? Characters.ToList()
            : Characters.Where(c => !c.IsDodgeCity).ToList();
    }
}
