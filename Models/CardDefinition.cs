record CardDefinition(
    string Name,
    CardType Type,
    CardCategory Category,
    string Description,
    bool RequiresTarget,
    string? TargetHint,
    string ImagePath,
    bool IsReactiveGreen = false);
