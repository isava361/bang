using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const int MaxPlayers = 6;
const int StartingHp = 4;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("Bang Online (Console Edition)");
Console.WriteLine("1) Host room");
Console.WriteLine("2) Join room");
Console.Write("Select option: ");
var choice = Console.ReadLine();

if (choice == "1")
{
    await HostRoomAsync();
}
else if (choice == "2")
{
    await JoinRoomAsync();
}
else
{
    Console.WriteLine("Invalid selection.");
}

static async Task HostRoomAsync()
{
    Console.Write("Enter your display name: ");
    var hostName = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(hostName))
    {
        Console.WriteLine("Name is required.");
        return;
    }

    Console.Write("Port to host on (default 5151): ");
    var portInput = Console.ReadLine();
    var port = 5151;
    if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput, out var parsedPort))
    {
        port = parsedPort;
    }

    var server = new BangServer(IPAddress.Any, port);
    var serverTask = server.StartAsync();

    Console.WriteLine($"Hosting room on port {port}. Waiting for players (max {MaxPlayers})...");

    using var client = new BangClient("127.0.0.1", port, hostName);
    await client.ConnectAsync();

    await RunClientLoopAsync(client, isHost: true);

    await server.StopAsync();
    await serverTask;
}

static async Task JoinRoomAsync()
{
    Console.Write("Enter server IP: ");
    var ip = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(ip))
    {
        Console.WriteLine("Server IP is required.");
        return;
    }

    Console.Write("Enter server port (default 5151): ");
    var portInput = Console.ReadLine();
    var port = 5151;
    if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput, out var parsedPort))
    {
        port = parsedPort;
    }

    Console.Write("Enter your display name: ");
    var name = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        Console.WriteLine("Name is required.");
        return;
    }

    using var client = new BangClient(ip, port, name);
    await client.ConnectAsync();

    await RunClientLoopAsync(client, isHost: false);
}

static async Task RunClientLoopAsync(BangClient client, bool isHost)
{
    Console.WriteLine("Connected. Type /help for commands.");

    var receiveTask = Task.Run(async () =>
    {
        await foreach (var message in client.ReceiveAsync())
        {
            switch (message.Type)
            {
                case MessageType.System:
                    Console.WriteLine($"[System] {message.Text}");
                    break;
                case MessageType.Chat:
                    Console.WriteLine($"[{message.From}] {message.Text}");
                    break;
                case MessageType.State:
                    RenderState(message.State);
                    break;
            }
        }
    });

    while (true)
    {
        var input = Console.ReadLine();
        if (input is null)
        {
            continue;
        }

        if (input.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            await client.SendAsync(new ClientCommand(CommandType.Leave));
            break;
        }

        if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp(isHost);
            continue;
        }

        if (isHost && input.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            await client.SendAsync(new ClientCommand(CommandType.Start));
            continue;
        }

        if (input.StartsWith("/say ", StringComparison.OrdinalIgnoreCase))
        {
            var text = input[5..].Trim();
            if (text.Length > 0)
            {
                await client.SendAsync(new ClientCommand(CommandType.Chat, text));
            }
            continue;
        }

        if (input.Equals("/state", StringComparison.OrdinalIgnoreCase))
        {
            await client.SendAsync(new ClientCommand(CommandType.State));
            continue;
        }

        if (input.StartsWith("/play ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var cardIndex))
            {
                string? target = parts.Length >= 3 ? parts[2] : null;
                await client.SendAsync(new ClientCommand(CommandType.Play, cardIndex.ToString(), target));
            }
            else
            {
                Console.WriteLine("Usage: /play <cardIndex> [targetId]");
            }
            continue;
        }

        if (input.Equals("/end", StringComparison.OrdinalIgnoreCase))
        {
            await client.SendAsync(new ClientCommand(CommandType.End));
            continue;
        }

        Console.WriteLine("Unknown command. Type /help for usage.");
    }

    await client.DisconnectAsync();
    await receiveTask;
}

static void PrintHelp(bool isHost)
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  /help            Show commands");
    Console.WriteLine("  /say <message>   Send chat message");
    Console.WriteLine("  /state           Request current game state");
    Console.WriteLine("  /play <index> [targetId]  Play a card from your hand");
    Console.WriteLine("  /end             End your turn");
    Console.WriteLine("  /quit            Leave the room");
    if (isHost)
    {
        Console.WriteLine("  /start           Start the game (host only)");
    }
}

static void RenderState(GameStateView? state)
{
    if (state is null)
    {
        return;
    }

    Console.WriteLine("--- Game State ---");
    Console.WriteLine($"Turn: {state.CurrentPlayerName} (ID {state.CurrentPlayerId})");
    Console.WriteLine("Players:");
    foreach (var player in state.Players)
    {
        var status = player.IsAlive ? "Alive" : "Out";
        Console.WriteLine($"  {player.Name} [ID {player.Id}] HP {player.Hp}/{player.MaxHp} ({status})");
    }

    if (state.YourHand.Count > 0)
    {
        Console.WriteLine("Your hand:");
        for (var i = 0; i < state.YourHand.Count; i++)
        {
            Console.WriteLine($"  [{i}] {state.YourHand[i]}");
        }
    }
    else
    {
        Console.WriteLine("Your hand is empty.");
    }
}

enum CommandType
{
    Join,
    Leave,
    Start,
    Chat,
    Play,
    End,
    State
}

record ClientCommand(CommandType Type, string? Data = null, string? Target = null);

enum MessageType
{
    System,
    Chat,
    State
}

record ServerMessage(MessageType Type, string? Text = null, string? From = null, GameStateView? State = null);

record GameStateView(
    string CurrentPlayerId,
    string CurrentPlayerName,
    List<PlayerView> Players,
    List<string> YourHand
);

record PlayerView(string Id, string Name, int Hp, int MaxHp, bool IsAlive);

class BangClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _name;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public BangClient(string host, int port, string name)
    {
        _host = host;
        _port = port;
        _name = name;
    }

    public async Task ConnectAsync()
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_host, _port);
        var stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        await SendAsync(new ClientCommand(CommandType.Join, _name));
    }

    public async IAsyncEnumerable<ServerMessage> ReceiveAsync()
    {
        if (_reader is null)
        {
            yield break;
        }

        while (true)
        {
            var line = await _reader.ReadLineAsync();
            if (line is null)
            {
                yield break;
            }

            var message = JsonSerializer.Deserialize<ServerMessage>(line);
            if (message is not null)
            {
                yield return message;
            }
        }
    }

    public async Task SendAsync(ClientCommand command)
    {
        if (_writer is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(command);
        await _writer.WriteLineAsync(payload);
    }

    public async Task DisconnectAsync()
    {
        if (_client is null)
        {
            return;
        }

        _client.Close();
        _client.Dispose();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _reader?.Dispose();
        _writer?.Dispose();
    }
}

class BangServer
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private readonly GameState _gameState = new();
    private CancellationTokenSource? _cts;

    public BangServer(IPAddress address, int port)
    {
        _listener = new TcpListener(address, port);
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        while (!_cts.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client, _cts.Token);
        }
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _listener.Stop();
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken token)
    {
        var connection = new ClientConnection(tcpClient);
        try
        {
            await foreach (var command in connection.ReceiveAsync(token))
            {
                switch (command.Type)
                {
                    case CommandType.Join:
                        await HandleJoinAsync(connection, command.Data);
                        break;
                    case CommandType.Leave:
                        await HandleLeaveAsync(connection);
                        return;
                    case CommandType.Start:
                        await HandleStartAsync(connection);
                        break;
                    case CommandType.Chat:
                        await HandleChatAsync(connection, command.Data);
                        break;
                    case CommandType.Play:
                        await HandlePlayAsync(connection, command);
                        break;
                    case CommandType.End:
                        await HandleEndAsync(connection);
                        break;
                    case CommandType.State:
                        await SendStateAsync(connection);
                        break;
                }
            }
        }
        finally
        {
            await HandleLeaveAsync(connection);
        }
    }

    private async Task HandleJoinAsync(ClientConnection connection, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Name required."));
            return;
        }

        if (_clients.Count >= MaxPlayers)
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Room is full."));
            await connection.DisposeAsync();
            return;
        }

        connection.PlayerId = Guid.NewGuid().ToString("N");
        connection.Name = name.Trim();
        _clients[connection.PlayerId] = connection;

        _gameState.EnsurePlayer(connection.PlayerId, connection.Name);

        await BroadcastAsync(new ServerMessage(MessageType.System, $"{connection.Name} joined the room."));
        await BroadcastStateAsync();
    }

    private async Task HandleLeaveAsync(ClientConnection connection)
    {
        if (connection.PlayerId is null)
        {
            return;
        }

        if (_clients.TryRemove(connection.PlayerId, out _))
        {
            _gameState.RemovePlayer(connection.PlayerId);
            await BroadcastAsync(new ServerMessage(MessageType.System, $"{connection.Name} left the room."));
            await BroadcastStateAsync();
        }

        await connection.DisposeAsync();
    }

    private async Task HandleStartAsync(ClientConnection connection)
    {
        if (_gameState.Started)
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Game already started."));
            return;
        }

        if (_clients.Count < 2)
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Need at least 2 players to start."));
            return;
        }

        _gameState.Start();
        await BroadcastAsync(new ServerMessage(MessageType.System, "Game started!"));
        await BroadcastStateAsync();
    }

    private async Task HandleChatAsync(ClientConnection connection, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await BroadcastAsync(new ServerMessage(MessageType.Chat, text.Trim(), connection.Name));
    }

    private async Task HandlePlayAsync(ClientConnection connection, ClientCommand command)
    {
        if (!_gameState.Started)
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Game has not started."));
            return;
        }

        if (!_gameState.IsPlayersTurn(connection.PlayerId))
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Not your turn."));
            return;
        }

        if (!int.TryParse(command.Data, out var index))
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Invalid card index."));
            return;
        }

        var result = _gameState.PlayCard(connection.PlayerId, index, command.Target);
        await BroadcastAsync(new ServerMessage(MessageType.System, result));
        await BroadcastStateAsync();
    }

    private async Task HandleEndAsync(ClientConnection connection)
    {
        if (!_gameState.Started)
        {
            return;
        }

        if (!_gameState.IsPlayersTurn(connection.PlayerId))
        {
            await connection.SendAsync(new ServerMessage(MessageType.System, "Not your turn."));
            return;
        }

        _gameState.AdvanceTurn();
        await BroadcastStateAsync();
    }

    private async Task SendStateAsync(ClientConnection connection)
    {
        var state = _gameState.ToView(connection.PlayerId);
        await connection.SendAsync(new ServerMessage(MessageType.State, State: state));
    }

    private async Task BroadcastStateAsync()
    {
        foreach (var client in _clients.Values)
        {
            await SendStateAsync(client);
        }
    }

    private async Task BroadcastAsync(ServerMessage message)
    {
        foreach (var client in _clients.Values)
        {
            await client.SendAsync(message);
        }
    }
}

class ClientConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public ClientConnection(TcpClient client)
    {
        _client = client;
        var stream = client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
    }

    public string? PlayerId { get; set; }
    public string Name { get; set; } = "";

    public async IAsyncEnumerable<ClientCommand> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync();
            if (line is null)
            {
                yield break;
            }

            var command = JsonSerializer.Deserialize<ClientCommand>(line);
            if (command is not null)
            {
                yield return command;
            }
        }
    }

    public async Task SendAsync(ServerMessage message)
    {
        var payload = JsonSerializer.Serialize(message);
        await _writer.WriteLineAsync(payload);
    }

    public ValueTask DisposeAsync()
    {
        _client.Close();
        _client.Dispose();
        _reader.Dispose();
        _writer.Dispose();
        return ValueTask.CompletedTask;
    }
}

class GameState
{
    private readonly Dictionary<string, PlayerState> _players = new();
    private readonly List<string> _turnOrder = new();
    private readonly Random _random = new();
    private readonly Stack<Card> _drawPile = new();
    private readonly List<Card> _discardPile = new();
    private int _turnIndex;

    public bool Started { get; private set; }

    public void EnsurePlayer(string id, string name)
    {
        if (_players.ContainsKey(id))
        {
            return;
        }

        _players[id] = new PlayerState(id, name, StartingHp);
        _turnOrder.Add(id);
    }

    public void RemovePlayer(string id)
    {
        if (_players.Remove(id))
        {
            _turnOrder.Remove(id);
        }
    }

    public void Start()
    {
        Started = true;
        BuildDeck();
        ShuffleDeck();
        _turnIndex = 0;
        foreach (var player in _players.Values)
        {
            player.Hand.Clear();
            DrawCards(player, 4);
        }

        if (_turnOrder.Count > 0)
        {
            DrawCards(_players[_turnOrder[_turnIndex]], 2);
        }
    }

    public bool IsPlayersTurn(string? playerId)
    {
        return playerId != null && _turnOrder.Count > 0 && _turnOrder[_turnIndex] == playerId;
    }

    public void AdvanceTurn()
    {
        if (_turnOrder.Count == 0)
        {
            return;
        }

        _turnIndex = (_turnIndex + 1) % _turnOrder.Count;
        var current = _players[_turnOrder[_turnIndex]];
        DrawCards(current, 2);
    }

    public string PlayCard(string playerId, int index, string? targetId)
    {
        if (!_players.TryGetValue(playerId, out var player))
        {
            return "Unknown player.";
        }

        if (index < 0 || index >= player.Hand.Count)
        {
            return "Card index out of range.";
        }

        var card = player.Hand[index];
        player.Hand.RemoveAt(index);
        _discardPile.Add(card);

        return card.Type switch
        {
            CardType.Bang => ResolveBang(player, targetId),
            CardType.Beer => ResolveBeer(player),
            CardType.Gatling => ResolveGatling(player),
            CardType.Stagecoach => ResolveStagecoach(player),
            CardType.CatBalou => ResolveCatBalou(player, targetId),
            _ => "Card had no effect."
        };
    }

    public GameStateView ToView(string? viewerId)
    {
        var currentId = _turnOrder.Count > 0 ? _turnOrder[_turnIndex] : "-";
        var currentName = _players.TryGetValue(currentId, out var current) ? current.Name : "-";
        var players = _players.Values
            .Select(p => new PlayerView(p.Id, p.Name, p.Hp, p.MaxHp, p.IsAlive))
            .ToList();

        var hand = new List<string>();
        if (viewerId != null && _players.TryGetValue(viewerId, out var viewer))
        {
            hand = viewer.Hand.Select(c => c.Name).ToList();
        }

        return new GameStateView(currentId, currentName, players, hand);
    }

    private void BuildDeck()
    {
        _drawPile.Clear();
        _discardPile.Clear();

        var cards = new List<Card>();
        cards.AddRange(CreateCards("Bang!", CardType.Bang, 25));
        cards.AddRange(CreateCards("Beer", CardType.Beer, 6));
        cards.AddRange(CreateCards("Gatling", CardType.Gatling, 2));
        cards.AddRange(CreateCards("Stagecoach", CardType.Stagecoach, 4));
        cards.AddRange(CreateCards("Cat Balou", CardType.CatBalou, 4));

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
        if (string.IsNullOrWhiteSpace(targetId) || !_players.TryGetValue(targetId, out var target))
        {
            return "Bang! requires a target ID.";
        }

        if (!target.IsAlive)
        {
            return $"{target.Name} is already out.";
        }

        target.Hp -= 1;
        if (target.Hp <= 0)
        {
            target.IsAlive = false;
            return $"{attacker.Name} shot {target.Name}. {target.Name} is out!";
        }

        return $"{attacker.Name} shot {target.Name}.";
    }

    private string ResolveBeer(PlayerState player)
    {
        if (player.Hp >= player.MaxHp)
        {
            return $"{player.Name} is already at full health.";
        }

        player.Hp += 1;
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

            target.Hp -= 1;
            if (target.Hp <= 0)
            {
                target.IsAlive = false;
            }
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
        if (string.IsNullOrWhiteSpace(targetId) || !_players.TryGetValue(targetId, out var target))
        {
            return "Cat Balou requires a target ID.";
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

    private static IEnumerable<Card> CreateCards(string name, CardType type, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new Card(name, type);
        }
    }
}

class PlayerState
{
    public PlayerState(string id, string name, int maxHp)
    {
        Id = id;
        Name = name;
        MaxHp = maxHp;
        Hp = maxHp;
    }

    public string Id { get; }
    public string Name { get; }
    public int Hp { get; set; }
    public int MaxHp { get; }
    public bool IsAlive { get; set; } = true;
    public List<Card> Hand { get; } = new();
}

record Card(string Name, CardType Type);

enum CardType
{
    Bang,
    Beer,
    Gatling,
    Stagecoach,
    CatBalou
}
