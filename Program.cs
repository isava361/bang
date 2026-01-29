using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<GameState>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/join", (JoinRequest request, GameState game) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new ApiResponse(null, "Name is required."));
    }

    var result = game.TryAddPlayer(request.Name.Trim());
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    var state = game.ToView(result.PlayerId!);
    return Results.Ok(new ApiResponse(new JoinResponse(result.PlayerId!, state), result.Message));
});

app.MapPost("/api/start", (PlayerRequest request, GameState game) =>
{
    var result = game.StartGame(request.PlayerId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/play", (PlayRequest request, GameState game) =>
{
    var result = game.PlayCard(request.PlayerId, request.CardIndex, request.TargetId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/end", (PlayerRequest request, GameState game) =>
{
    var result = game.EndTurn(request.PlayerId);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapPost("/api/chat", (ChatRequest request, GameState game) =>
{
    var result = game.AddChat(request.PlayerId, request.Text);
    if (!result.Success)
    {
        return Results.BadRequest(new ApiResponse(null, result.Message));
    }

    return Results.Ok(new ApiResponse(result.State, result.Message));
});

app.MapGet("/api/state", (string playerId, GameState game) =>
{
    var state = game.ToView(playerId);
    if (state is null)
    {
        return Results.BadRequest(new ApiResponse(null, "Unknown player."));
    }

    return Results.Ok(new ApiResponse(state, "OK"));
});

app.Run();

record JoinRequest(string Name);
record PlayerRequest(string PlayerId);
record PlayRequest(string PlayerId, int CardIndex, string? TargetId);
record ChatRequest(string PlayerId, string Text);
record JoinResponse(string PlayerId, GameStateView State);
record ApiResponse(object? Data, string Message);

record GameStateView(
    bool Started,
    string CurrentPlayerId,
    string CurrentPlayerName,
    List<PlayerView> Players,
    List<CardView> YourHand,
    string? LastEvent
);

record PlayerView(
    string Id,
    string Name,
    int Hp,
    int MaxHp,
    bool IsAlive,
    string CharacterName,
    string CharacterDescription
);

record CardView(
    string Name,
    CardType Type,
    string Description,
    bool RequiresTarget,
    string? TargetHint
);

record CommandResult(bool Success, string Message, GameStateView? State = null, string? PlayerId = null);

class GameState
{
    private const int MaxPlayers = 6;
    private const int StartingHand = 4;
    private readonly Dictionary<string, PlayerState> _players = new();
    private readonly List<string> _turnOrder = new();
    private readonly Random _random = new();
    private readonly Stack<Card> _drawPile = new();
    private readonly List<Card> _discardPile = new();
    private readonly object _lock = new();
    private int _turnIndex;

    public bool Started { get; private set; }
    public string? LastEvent { get; private set; }

    public CommandResult TryAddPlayer(string name)
    {
        lock (_lock)
        {
            if (Started)
            {
                return new CommandResult(false, "Game already started. Wait for the next round.");
            }

            if (_players.Count >= MaxPlayers)
            {
                return new CommandResult(false, "Room is full.");
            }

            var id = Guid.NewGuid().ToString("N");
            var character = CharacterLibrary.Draw(_random);
            var player = new PlayerState(id, name, character);
            _players[id] = player;
            _turnOrder.Add(id);
            LastEvent = $"{name} joined as {character.Name}.";
            return new CommandResult(true, "Joined room.", PlayerId: id);
        }
    }

    public CommandResult StartGame(string playerId)
    {
        lock (_lock)
        {
            if (!_players.ContainsKey(playerId))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (Started)
            {
                return new CommandResult(false, "Game already started.");
            }

            if (_players.Count < 2)
            {
                return new CommandResult(false, "Need at least 2 players to start.");
            }

            Started = true;
            BuildDeck();
            ShuffleDeck();
            _turnIndex = 0;

            foreach (var player in _players.Values)
            {
                player.ResetForNewGame();
                DrawCards(player, StartingHand);
            }

            var current = _players[_turnOrder[_turnIndex]];
            DrawCards(current, GetStartTurnDrawCount(current));
            LastEvent = $"Game started! {current.Name} takes the first turn.";

            return new CommandResult(true, "Game started.", ToView(playerId));
        }
    }

    public CommandResult PlayCard(string playerId, int index, string? targetId)
    {
        lock (_lock)
        {
            if (!Started)
            {
                return new CommandResult(false, "Game has not started.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Not your turn.");
            }

            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (index < 0 || index >= player.Hand.Count)
            {
                return new CommandResult(false, "Card index out of range.");
            }

            var card = player.Hand[index];
            player.Hand.RemoveAt(index);
            _discardPile.Add(card);

            var message = card.Type switch
            {
                CardType.Bang => ResolveBang(player, targetId),
                CardType.Beer => ResolveBeer(player),
                CardType.Gatling => ResolveGatling(player),
                CardType.Stagecoach => ResolveStagecoach(player),
                CardType.CatBalou => ResolveCatBalou(player, targetId),
                CardType.Indians => ResolveIndians(player),
                CardType.Duel => ResolveDuel(player, targetId),
                CardType.Panic => ResolvePanic(player, targetId),
                CardType.Saloon => ResolveSaloon(player),
                CardType.WellsFargo => ResolveWellsFargo(player),
                CardType.GeneralStore => ResolveGeneralStore(player),
                _ => "Card had no effect."
            };

            LastEvent = message;
            return new CommandResult(true, message, ToView(playerId));
        }
    }

    public CommandResult EndTurn(string playerId)
    {
        lock (_lock)
        {
            if (!Started)
            {
                return new CommandResult(false, "Game has not started.");
            }

            if (!IsPlayersTurn(playerId))
            {
                return new CommandResult(false, "Not your turn.");
            }

            var endingPlayer = _players[_turnOrder[_turnIndex]];
            if (endingPlayer.Character.Ability == CharacterAbility.DrawWhenEmpty && endingPlayer.Hand.Count == 0)
            {
                DrawCards(endingPlayer, 1);
                LastEvent = $"{endingPlayer.Name} refreshes with a bonus draw.";
            }

            AdvanceTurn();
            var current = _players[_turnOrder[_turnIndex]];
            LastEvent = $"{current.Name}'s turn begins.";
            return new CommandResult(true, "Turn ended.", ToView(playerId));
        }
    }

    public CommandResult AddChat(string playerId, string text)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(playerId, out var player))
            {
                return new CommandResult(false, "Unknown player.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new CommandResult(false, "Chat message cannot be empty.");
            }

            LastEvent = $"{player.Name}: {text.Trim()}";
            return new CommandResult(true, "Chat sent.", ToView(playerId));
        }
    }

    public GameStateView? ToView(string playerId)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(playerId, out var viewer))
            {
                return null;
            }

            var currentId = _turnOrder.Count > 0 ? _turnOrder[_turnIndex] : "-";
            var currentName = _players.TryGetValue(currentId, out var current) ? current.Name : "-";
            var players = _players.Values
                .Select(p => new PlayerView(
                    p.Id,
                    p.Name,
                    p.Hp,
                    p.MaxHp,
                    p.IsAlive,
                    p.Character.Name,
                    p.Character.Description))
                .OrderBy(p => p.Name)
                .ToList();

            var hand = viewer.Hand
                .Select(c => new CardView(c.Name, c.Type, c.Description, c.RequiresTarget, c.TargetHint))
                .ToList();

            return new GameStateView(
                Started,
                currentId,
                currentName,
                players,
                hand,
                LastEvent);
        }
    }

    private bool IsPlayersTurn(string playerId)
    {
        return _turnOrder.Count > 0 && _turnOrder[_turnIndex] == playerId;
    }

    private void AdvanceTurn()
    {
        if (_turnOrder.Count == 0)
        {
            return;
        }

        _turnIndex = (_turnIndex + 1) % _turnOrder.Count;
        var current = _players[_turnOrder[_turnIndex]];
        DrawCards(current, GetStartTurnDrawCount(current));
    }

    private int GetStartTurnDrawCount(PlayerState player)
    {
        return player.Character.Ability == CharacterAbility.ExtraDraw ? 3 : 2;
    }

    private void BuildDeck()
    {
        _drawPile.Clear();
        _discardPile.Clear();

        var cards = new List<Card>();
        cards.AddRange(CreateCards(CardType.Bang, 25));
        cards.AddRange(CreateCards(CardType.Beer, 6));
        cards.AddRange(CreateCards(CardType.Gatling, 2));
        cards.AddRange(CreateCards(CardType.Stagecoach, 4));
        cards.AddRange(CreateCards(CardType.CatBalou, 4));
        cards.AddRange(CreateCards(CardType.Indians, 2));
        cards.AddRange(CreateCards(CardType.Duel, 3));
        cards.AddRange(CreateCards(CardType.Panic, 4));
        cards.AddRange(CreateCards(CardType.Saloon, 2));
        cards.AddRange(CreateCards(CardType.WellsFargo, 2));
        cards.AddRange(CreateCards(CardType.GeneralStore, 3));

        foreach (var card in cards.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }
    }

    private void ShuffleDeck()
    {
        var cards = _drawPile.ToList();
        _drawPile.Clear();
        foreach (var card in cards.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }
    }

    private void DrawCards(PlayerState player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (_drawPile.Count == 0)
            {
                ReshuffleDiscardIntoDraw();
            }

            if (_drawPile.Count == 0)
            {
                break;
            }

            player.Hand.Add(_drawPile.Pop());
        }
    }

    private void ReshuffleDiscardIntoDraw()
    {
        if (_discardPile.Count == 0)
        {
            return;
        }

        foreach (var card in _discardPile.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }

        _discardPile.Clear();
    }

    private string ResolveBang(PlayerState attacker, string? targetId)
    {
        if (!TryGetTarget(targetId, out var target, out var error))
        {
            return error;
        }

        var damage = attacker.Character.Ability == CharacterAbility.DoubleBangDamage ? 2 : 1;
        ApplyDamage(attacker, target, damage, "shot");
        return FormatAttackMessage(attacker, target, "shot", damage);
    }

    private string ResolveBeer(PlayerState player)
    {
        if (player.Hp >= player.MaxHp)
        {
            return $"{player.Name} is already at full health.";
        }

        player.Hp = Math.Min(player.Hp + 1, player.MaxHp);
        return $"{player.Name} drinks a Beer and recovers 1 HP.";
    }

    private string ResolveGatling(PlayerState attacker)
    {
        foreach (var target in _players.Values)
        {
            if (target.Id == attacker.Id || !target.IsAlive)
            {
                continue;
            }

            ApplyDamage(attacker, target, 1, "riddled");
        }

        return $"{attacker.Name} fires Gatling! Everyone else takes 1 damage.";
    }

    private string ResolveStagecoach(PlayerState player)
    {
        DrawCards(player, 2);
        return $"{player.Name} plays Stagecoach and draws 2 cards.";
    }

    private string ResolveCatBalou(PlayerState attacker, string? targetId)
    {
        if (!TryGetTarget(targetId, out var target, out var error))
        {
            return error;
        }

        if (target.Hand.Count == 0)
        {
            return $"{target.Name} has no cards to discard.";
        }

        var index = _random.Next(target.Hand.Count);
        var discarded = target.Hand[index];
        target.Hand.RemoveAt(index);
        _discardPile.Add(discarded);
        return $"{attacker.Name} uses Cat Balou on {target.Name}, discarding {discarded.Name}.";
    }

    private string ResolveIndians(PlayerState attacker)
    {
        foreach (var target in _players.Values)
        {
            if (target.Id == attacker.Id || !target.IsAlive)
            {
                continue;
            }

            ApplyDamage(attacker, target, 1, "ambushed");
        }

        return $"{attacker.Name} plays Indians! Everyone else takes 1 damage.";
    }

    private string ResolveDuel(PlayerState attacker, string? targetId)
    {
        if (!TryGetTarget(targetId, out var target, out var error))
        {
            return error;
        }

        ApplyDamage(attacker, target, 1, "dueled");
        return $"{attacker.Name} challenges {target.Name} to a duel. {target.Name} takes 1 damage.";
    }

    private string ResolvePanic(PlayerState attacker, string? targetId)
    {
        if (!TryGetTarget(targetId, out var target, out var error))
        {
            return error;
        }

        if (target.Hand.Count == 0)
        {
            return $"{target.Name} has no cards to steal.";
        }

        var index = _random.Next(target.Hand.Count);
        var stolen = target.Hand[index];
        target.Hand.RemoveAt(index);
        attacker.Hand.Add(stolen);
        return $"{attacker.Name} uses Panic to steal {stolen.Name} from {target.Name}.";
    }

    private string ResolveSaloon(PlayerState player)
    {
        foreach (var target in _players.Values)
        {
            if (!target.IsAlive)
            {
                continue;
            }

            target.Hp = Math.Min(target.Hp + 1, target.MaxHp);
        }

        return $"{player.Name} opens the Saloon. Everyone heals 1 HP.";
    }

    private string ResolveWellsFargo(PlayerState player)
    {
        DrawCards(player, 3);
        return $"{player.Name} raids Wells Fargo and draws 3 cards.";
    }

    private string ResolveGeneralStore(PlayerState player)
    {
        DrawCards(player, 2);
        return $"{player.Name} visits the General Store and draws 2 cards.";
    }

    private bool TryGetTarget(string? targetId, out PlayerState target, out string error)
    {
        if (string.IsNullOrWhiteSpace(targetId) || !_players.TryGetValue(targetId, out target!))
        {
            error = "Target player not found.";
            target = null!;
            return false;
        }

        if (!target.IsAlive)
        {
            error = $"{target.Name} is already out.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void ApplyDamage(PlayerState attacker, PlayerState target, int damage, string verb)
    {
        target.Hp -= damage;
        if (target.Hp <= 0)
        {
            target.IsAlive = false;
        }

        if (target.Character.Ability == CharacterAbility.DrawOnHit && target.IsAlive)
        {
            DrawCards(target, 1);
        }
    }

    private string FormatAttackMessage(PlayerState attacker, PlayerState target, string verb, int damage)
    {
        if (!target.IsAlive)
        {
            return $"{attacker.Name} {verb} {target.Name} for {damage} damage. {target.Name} is out!";
        }

        return $"{attacker.Name} {verb} {target.Name} for {damage} damage.";
    }

    private IEnumerable<Card> CreateCards(CardType type, int count)
    {
        var definition = CardLibrary.Get(type);
        for (var i = 0; i < count; i++)
        {
            yield return new Card(definition.Name, definition.Type, definition.Description, definition.RequiresTarget, definition.TargetHint);
        }
    }
}

class PlayerState
{
    public PlayerState(string id, string name, CharacterDefinition character)
    {
        Id = id;
        Name = name;
        Character = character;
        MaxHp = character.MaxHp;
        Hp = MaxHp;
    }

    public string Id { get; }
    public string Name { get; }
    public int Hp { get; set; }
    public int MaxHp { get; private set; }
    public bool IsAlive { get; set; } = true;
    public CharacterDefinition Character { get; private set; }
    public List<Card> Hand { get; } = new();

    public void ResetForNewGame()
    {
        MaxHp = Character.MaxHp;
        Hp = MaxHp;
        IsAlive = true;
        Hand.Clear();
    }
}

record Card(string Name, CardType Type, string Description, bool RequiresTarget, string? TargetHint);

record CardDefinition(string Name, CardType Type, string Description, bool RequiresTarget, string? TargetHint);

enum CardType
{
    Bang,
    Beer,
    Gatling,
    Stagecoach,
    CatBalou,
    Indians,
    Duel,
    Panic,
    Saloon,
    WellsFargo,
    GeneralStore
}

static class CardLibrary
{
    private static readonly Dictionary<CardType, CardDefinition> Definitions = new()
    {
        {
            CardType.Bang,
            new CardDefinition("Bang!", CardType.Bang, "Deal 1 damage to a target (2 if you are Slab the Killer).", true, "Choose a player to shoot")
        },
        {
            CardType.Beer,
            new CardDefinition("Beer", CardType.Beer, "Recover 1 HP.", false, null)
        },
        {
            CardType.Gatling,
            new CardDefinition("Gatling", CardType.Gatling, "Deal 1 damage to every other player.", false, null)
        },
        {
            CardType.Stagecoach,
            new CardDefinition("Stagecoach", CardType.Stagecoach, "Draw 2 cards.", false, null)
        },
        {
            CardType.CatBalou,
            new CardDefinition("Cat Balou", CardType.CatBalou, "Force a target to discard a random card.", true, "Pick a player to discard")
        },
        {
            CardType.Indians,
            new CardDefinition("Indians!", CardType.Indians, "Deal 1 damage to every other player.", false, null)
        },
        {
            CardType.Duel,
            new CardDefinition("Duel", CardType.Duel, "Target player takes 1 damage.", true, "Pick a dueling opponent")
        },
        {
            CardType.Panic,
            new CardDefinition("Panic!", CardType.Panic, "Steal a random card from a target.", true, "Pick a player to rob")
        },
        {
            CardType.Saloon,
            new CardDefinition("Saloon", CardType.Saloon, "All living players heal 1 HP.", false, null)
        },
        {
            CardType.WellsFargo,
            new CardDefinition("Wells Fargo", CardType.WellsFargo, "Draw 3 cards.", false, null)
        },
        {
            CardType.GeneralStore,
            new CardDefinition("General Store", CardType.GeneralStore, "Draw 2 cards.", false, null)
        }
    };

    public static CardDefinition Get(CardType type) => Definitions[type];
}

enum CharacterAbility
{
    ExtraDraw,
    DoubleBangDamage,
    DrawOnHit,
    DrawWhenEmpty,
    SteadyHands
}

record CharacterDefinition(string Name, int MaxHp, CharacterAbility Ability, string Description);

static class CharacterLibrary
{
    private static readonly List<CharacterDefinition> Characters = new()
    {
        new CharacterDefinition(
            "Lucky Duke",
            4,
            CharacterAbility.ExtraDraw,
            "Start each turn by drawing 3 cards instead of 2."),
        new CharacterDefinition(
            "Slab the Killer",
            4,
            CharacterAbility.DoubleBangDamage,
            "Your Bang! cards deal 2 damage."),
        new CharacterDefinition(
            "El Gringo",
            3,
            CharacterAbility.DrawOnHit,
            "Whenever you are hit, draw 1 card."),
        new CharacterDefinition(
            "Suzy Lafayette",
            4,
            CharacterAbility.DrawWhenEmpty,
            "When you end your turn with no cards, draw 1."),
        new CharacterDefinition(
            "Rose Doolan",
            5,
            CharacterAbility.SteadyHands,
            "Steady aim gives you +1 max HP."),
        new CharacterDefinition(
            "Jesse Jones",
            4,
            CharacterAbility.ExtraDraw,
            "Always ready: draw 3 cards at the start of your turn."),
        new CharacterDefinition(
            "Bart Cassidy",
            4,
            CharacterAbility.DrawOnHit,
            "Every time you take damage, draw 1 card."),
        new CharacterDefinition(
            "Paul Regret",
            5,
            CharacterAbility.SteadyHands,
            "Tougher than he looks: +1 max HP."),
        new CharacterDefinition(
            "Calamity Janet",
            4,
            CharacterAbility.DrawWhenEmpty,
            "Lives on the edge: draw 1 when your hand empties."),
        new CharacterDefinition(
            "Kit Carlson",
            4,
            CharacterAbility.ExtraDraw,
            "Scout the trail: draw 3 cards at turn start."),
        new CharacterDefinition(
            "Willy the Kid",
            4,
            CharacterAbility.DoubleBangDamage,
            "Fastest gun: Bang! deals 2 damage."),
        new CharacterDefinition(
            "Sid Ketchum",
            4,
            CharacterAbility.DrawOnHit,
            "Pain fuels you: draw 1 card when hit."),
        new CharacterDefinition(
            "Vulture Sam",
            4,
            CharacterAbility.ExtraDraw,
            "Always prepared: draw 3 cards at the start of your turn."),
        new CharacterDefinition(
            "Pedro Ramirez",
            5,
            CharacterAbility.SteadyHands,
            "Hardy ranger: +1 max HP.")
    };

    public static CharacterDefinition Draw(Random random)
    {
        return Characters[random.Next(Characters.Count)];
    }
}
