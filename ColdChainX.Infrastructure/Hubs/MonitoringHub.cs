using Microsoft.AspNetCore.SignalR;

namespace ColdChainX.Infrastructure.Hubs;

public sealed class MonitoringHub : Hub
{
    public Task JoinTripGroup(string tripId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, BuildTripGroup(tripId));
    }

    public Task LeaveTripGroup(string tripId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildTripGroup(tripId));
    }

    public static string BuildTripGroup(Guid tripId)
    {
        return BuildTripGroup(tripId.ToString());
    }

    private static string BuildTripGroup(string tripId)
    {
        return $"trip:{tripId.Trim()}";
    }
}
