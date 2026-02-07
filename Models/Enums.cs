enum CardType
{
    // Base game
    Bang,
    Missed,
    Beer,
    Gatling,
    Stagecoach,
    CatBalou,
    Indians,
    Duel,
    Panic,
    Saloon,
    WellsFargo,
    GeneralStore,
    Barrel,
    Mustang,
    Scope,
    Volcanic,
    Schofield,
    Remington,
    RevCarabine,
    Winchester,
    Jail,
    Dynamite,

    // Dodge City — Brown
    Punch,
    Springfield,
    Dodge,
    Whisky,
    Tequila,
    RagTime,
    Brawl,
    CanCan,
    Conestoga,

    // Dodge City — Green (active)
    Derringer,
    Pepperbox,
    Howitzer,
    BuffaloRifle,
    Canteen,

    // Dodge City — Green (reactive)
    Bible,
    IronPlate,
    Sombrero,
    TenGallonHat,

    // Dodge City — Blue
    Hideout,
    Silver
}

enum CardCategory
{
    Brown,
    Blue,
    Weapon,
    Green
}

enum CardSuit
{
    Spades,
    Hearts,
    Diamonds,
    Clubs
}

enum Role
{
    Unassigned,
    Sheriff,
    Deputy,
    Bandit,
    Renegade
}

[Flags]
enum Expansion
{
    None = 0,
    DodgeCity = 1,
    HighNoon = 2,
    FistfulOfCards = 4
}

enum PendingActionType
{
    BangDefense,
    GatlingDefense,
    IndiansDefense,
    DuelChallenge,
    GeneralStorePick,
    DiscardExcess,
    ChooseStealSource,
    JesseJonesSteal,
    KitCarlsonPick,

    // Dodge City
    DiscardForCost,
    BrawlDefense,
    HowitzerDefense,
    ChooseGreenTarget,
    PatBrennanDraw,
    VeraCusterCopy,
    DocHolidayTarget,

    // Events
    RussianRoulette,
    TrainRobbery
}
