using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Services;

public sealed class IncidentWorkflowNotificationService : IIncidentWorkflowNotificationService
{
    private readonly IApplicationDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<IncidentWorkflowNotificationService> _logger;

    public IncidentWorkflowNotificationService(
        IApplicationDbContext db,
        INotificationService notificationService,
        IIncidentRealtimeNotifier realtimeNotifier,
        ILogger<IncidentWorkflowNotificationService> logger)
    {
        _db = db;
        _notificationService = notificationService;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task NotifyAsync(
        IncidentWorkflowNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (notification.IncidentId == Guid.Empty
            || string.IsNullOrWhiteSpace(notification.Action)
            || string.IsNullOrWhiteSpace(notification.Title)
            || string.IsNullOrWhiteSpace(notification.Body))
        {
            return;
        }

        try
        {
            var incident = await _db.IncidentReports
                .AsNoTracking()
                .Where(i => i.IncidentId == notification.IncidentId)
                .Select(i => new { i.ReportedBy, i.TripId, i.Status })
                .FirstOrDefaultAsync(cancellationToken);
            if (incident == null)
                return;

            var roleNames = notification.RecipientRoles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            var roleRecipientIds = roleNames.Count == 0
                ? new List<Guid>()
                : await _db.Users
                    .AsNoTracking()
                    .Where(user => user.Role != null
                        && roleNames.Contains(user.Role.RoleName.ToUpper())
                        && (!notification.RecipientWarehouseId.HasValue
                            || user.WarehouseId == notification.RecipientWarehouseId.Value)
                        && (user.Status == null || user.Status.ToUpper() == "ACTIVE"))
                    .Select(user => user.UserId)
                    .ToListAsync(cancellationToken);

            var tripId = notification.TripId ?? incident.TripId;
            var tripDriverUserIds = notification.IncludeTripDrivers && tripId.HasValue
                ? await _db.TripDrivers
                    .AsNoTracking()
                    .Where(td => td.TripId == tripId.Value && td.Driver.UserId.HasValue)
                    .Select(td => td.Driver.UserId!.Value)
                    .ToListAsync(cancellationToken)
                : new List<Guid>();

            var directRecipientIds = notification.AdditionalUserIds
                .Concat(notification.IncludeReporter
                    ? new[] { incident.ReportedBy }
                    : Array.Empty<Guid>())
                .Concat(tripDriverUserIds)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var allRecipientIds = roleRecipientIds
                .Concat(directRecipientIds)
                .Distinct()
                .ToList();

            var data = new Dictionary<string, string>
            {
                ["incidentId"] = notification.IncidentId.ToString(),
                ["tripId"] = tripId?.ToString() ?? string.Empty,
                ["action"] = notification.Action,
                ["incidentStatus"] = incident.Status ?? string.Empty,
                ["screen"] = notification.Screen
            };
            foreach (var item in notification.AdditionalData)
                data[item.Key] = item.Value;
            await _notificationService.SendToUsersAsync(
                allRecipientIds,
                notification.Title,
                notification.Body,
                notification.NotificationType,
                notification.ReferenceId ?? notification.IncidentId.ToString(),
                data,
                cancellationToken);

            var realtimePayload = notification.Payload ?? new
            {
                notification.IncidentId,
                TripId = tripId,
                notification.Action,
                IncidentStatus = incident.Status,
                notification.Title,
                notification.Body,
                Data = notification.Payload,
                OccurredAt = DateTime.UtcNow
            };
            var groups = notification.RealtimeGroups
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Distinct()
                .ToList();
            if (groups.Count > 0)
            {
                await _realtimeNotifier.NotifyGroupsAsync(
                    groups,
                    notification.RealtimeEventName,
                    realtimePayload,
                    cancellationToken);
            }

            var realtimeUserIds = groups.Count == 0
                ? allRecipientIds
                : directRecipientIds.Except(roleRecipientIds).ToList();
            foreach (var userId in realtimeUserIds)
            {
                await _realtimeNotifier.NotifyUserAsync(
                    userId,
                    notification.RealtimeEventName,
                    realtimePayload,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // The business action has already been committed. Notification delivery must not roll it back.
            _logger.LogWarning(
                ex,
                "Could not deliver incident workflow notification {Action} for incident {IncidentId}.",
                notification.Action,
                notification.IncidentId);
        }
    }
}
