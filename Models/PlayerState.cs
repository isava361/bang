class PlayerState
{
    public PlayerState(string id, string name, CharacterDefinition character)
    {
        Id = id;
        Name = name;
        Character = character;
        Role = Role.Unassigned;
        MaxHp = character.MaxHp;
        Hp = MaxHp;
    }

    public string Id { get; }
    public string PublicId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; private set; }
    public bool IsAlive { get; set; } = true;
    public Role Role { get; private set; }
    public CharacterDefinition Character { get; private set; }
    public List<Card> Hand { get; } = new();
    public List<Card> InPlay { get; } = new();
    public List<Card> FreshGreenCards { get; } = new();
    public int BangsPlayedThisTurn { get; set; }
    public int AbilityUsesThisTurn { get; set; }
    public string? CopiedCharacterName { get; set; }
    public bool IsGhost { get; set; }
    public string ActiveCharacterName => CopiedCharacterName ?? Character.Name;

    public void ResetForNewGame()
    {
        MaxHp = Character.MaxHp + (Role == Role.Sheriff ? 1 : 0);
        Hp = MaxHp;
        IsAlive = true;
        Hand.Clear();
        InPlay.Clear();
        FreshGreenCards.Clear();
        BangsPlayedThisTurn = 0;
        AbilityUsesThisTurn = 0;
        CopiedCharacterName = null;
        IsGhost = false;
    }

    public void ResetTurnFlags()
    {
        BangsPlayedThisTurn = 0;
        AbilityUsesThisTurn = 0;
        CopiedCharacterName = null;
    }

    public void AssignRole(Role role)
    {
        Role = role;
    }

    public void AssignCharacter(CharacterDefinition character)
    {
        Character = character;
    }
}
