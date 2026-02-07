using Microsoft.AspNetCore.SignalR;

class RoomCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RoomIdleTimeout = TimeSpan.FromMinutes(2);
    private readonly RoomManager _rooms;
    private readonly IHubContext<GameHub> _hub;

    public RoomCleanupService(RoomManager rooms, IHubContext<GameHub> hub)
    {
        _rooms = rooms;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var removed = _rooms.CleanupInactiveRooms(RoomIdleTimeout);
            if (removed > 0)
            {
                await _hub.Clients.Group("lobby").SendAsync("RoomsUpdated", _rooms.ListRooms(), cancellationToken: stoppingToken);
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}
