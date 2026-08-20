using ColdChainX.Application.DTOs.Incident;

namespace ColdChainX.Application.Interfaces;

public interface IIncidentWorkflowNotificationService
{
    Task NotifyAsync(
        IncidentWorkflowNotification notification,
        CancellationToken cancellationToken = default);
}
