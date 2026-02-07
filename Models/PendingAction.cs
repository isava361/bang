class PendingAction
{
    public PendingAction(PendingActionType type, string sourcePlayerId, IEnumerable<string> respondingPlayerIds, int damage = 1)
    {
        Type = type;
        SourcePlayerId = sourcePlayerId;
        RespondingPlayerIds = new Queue<string>(respondingPlayerIds);
        Damage = damage;
    }

    public PendingActionType Type { get; }
    public string SourcePlayerId { get; }
    public Queue<string> RespondingPlayerIds { get; }
    public int Damage { get; }
    public List<Card>? RevealedCards { get; set; }
    public string? DuelPlayerA { get; set; }
    public string? DuelPlayerB { get; set; }
    public string? StealTargetId { get; set; }
    public string? StealMode { get; set; }
    public int KitCarlsonPicksRemaining { get; set; }

    // Dodge City: discard-for-cost deferred effect
    public CardType? DeferredCardType { get; set; }
    public string? DeferredTargetId { get; set; }

    // Dodge City: attacking card suit (for Apache Kid)
    public CardSuit? AttackingSuit { get; set; }
}
