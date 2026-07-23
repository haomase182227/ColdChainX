namespace ColdChainX.Application.Interfaces;

public interface IIncidentRealtimeNotifier
{
    Task NotifyGroupsAsync(
        IReadOnlyCollection<string> groups,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default);

    Task NotifyUserAsync(
        Guid userId,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default);
}
