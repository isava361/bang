enum EventCardType
{
    // High Noon
    HighNoon,
    TheDaltons,
    Thirst,
    Hangover,
    Blessing,
    GhostTown,
    GoldRush,
    Shootout,
    TheDoctor,
    TrainRobbery,
    Sermon,
    TheReverend,
    NewIdentity,

    // A Fistful of Cards
    DeadMan,
    FistfulOfCards,
    LawOfTheWest,
    Sniper,
    Judge,
    Peyote,
    Ricochet,
    RussianRoulette,
    HardLiquor,
    Vendetta,
    WildWestShow,
    AbandonedMine,
    AFistfulOfCards,
}

record EventCard(EventCardType Type, string Name, string Description, Expansion Expansion);
