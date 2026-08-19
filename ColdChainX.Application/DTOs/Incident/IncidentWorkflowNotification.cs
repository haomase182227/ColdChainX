namespace ColdChainX.Application.DTOs.Incident;

public sealed class IncidentWorkflowNotification
{
    public Guid IncidentId { get; init; }
    public Guid? TripId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public IReadOnlyCollection<string> RecipientRoles { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<Guid> AdditionalUserIds { get; init; } = Array.Empty<Guid>();
    public Guid? RecipientWarehouseId { get; init; }
    public bool IncludeReporter { get; init; } = true;
    public bool IncludeTripDrivers { get; init; } = true;
    public IReadOnlyCollection<string> RealtimeGroups { get; init; } = Array.Empty<string>();
    public string RealtimeEventName { get; init; } = "IncidentWorkflowActionCompleted";
    public string NotificationType { get; init; } = "INCIDENT_WORKFLOW";
    public string? ReferenceId { get; init; }
    public string Screen { get; init; } = "INCIDENT_DETAIL";
    public IReadOnlyDictionary<string, string> AdditionalData { get; init; } =
        new Dictionary<string, string>();
    public object? Payload { get; init; }
}
