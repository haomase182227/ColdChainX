using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ColdChainX.Infrastructure.Services;

public sealed class IncidentRealtimeNotifier : IIncidentRealtimeNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public IncidentRealtimeNotifier(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyGroupsAsync(
        IReadOnlyCollection<string> groups,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Groups(groups)
            .SendAsync(eventName, payload, cancellationToken);
    }

    public Task NotifyUserAsync(
        Guid userId,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.User(userId.ToString())
            .SendAsync(eventName, payload, cancellationToken);
    }
}
