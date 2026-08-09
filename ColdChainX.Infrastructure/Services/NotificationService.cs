using System.Text;
using System.Text.Json;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Infrastructure.Services;

public class NotificationService : INotificationService
{
    public const int FirebaseMulticastBatchSize = 500;

    private static readonly HashSet<string> SupportedPlatforms =
        new(StringComparer.OrdinalIgnoreCase) { "Android", "iOS" };

    private static readonly HashSet<string> SensitiveDataKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "private_key",
            "privateKey",
            "client_email",
            "clientEmail",
            "authorization",
            "jwt",
            "access_token",
            "accessToken",
            "refresh_token",
            "refreshToken"
        };

    private readonly ApplicationDbContext _db;
    private readonly IFirebaseMessagingClient _firebaseClient;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext db,
        IFirebaseMessagingClient firebaseClient,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _firebaseClient = firebaseClient;
        _logger = logger;
    }

    public async Task<ApiResponse<DeviceTokenRegistrationResponse>> RegisterDeviceTokenAsync(
        Guid userId,
        RegisterDeviceTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRegistrationRequest(request);
        if (validationError != null)
            return ApiResponse<DeviceTokenRegistrationResponse>.Failure(validationError);

        if (!await _db.Users.AnyAsync(u => u.UserId == userId, cancellationToken))
            return ApiResponse<DeviceTokenRegistrationResponse>.Failure("Authenticated user was not found.", 404);

        var token = request.DeviceToken!.Trim();
        var platform = NormalizePlatform(request.Platform!);
        var deviceId = TrimToNull(request.DeviceId);
        var deviceName = TrimToNull(request.DeviceName);
        var appVersion = TrimToNull(request.AppVersion);
        var now = DateTime.UtcNow;

        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

        if (existing == null)
        {
            existing = new DeviceToken
            {
                DeviceTokenId = Guid.NewGuid(),
                UserId = userId,
                Token = token,
                Platform = platform,
                DeviceId = deviceId,
                DeviceName = deviceName,
                AppVersion = appVersion,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                LastUsedAt = now
            };
            _db.DeviceTokens.Add(existing);
        }
        else
        {
            existing.UserId = userId;
            existing.Platform = platform;
            existing.DeviceId = deviceId;
            existing.DeviceName = deviceName;
            existing.AppVersion = appVersion;
            existing.IsActive = true;
            existing.UpdatedAt = now;
            existing.LastUsedAt = now;
        }

        if (deviceId != null)
        {
            var staleDeviceTokens = await _db.DeviceTokens
                .Where(d => d.UserId == userId &&
                            d.DeviceId == deviceId &&
                            d.DeviceTokenId != existing.DeviceTokenId &&
                            d.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var staleToken in staleDeviceTokens)
            {
                staleToken.IsActive = false;
                staleToken.UpdatedAt = now;
            }
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "A concurrent device-token registration conflicted with the unique token index.");
            return ApiResponse<DeviceTokenRegistrationResponse>.Failure(
                "The device token is being registered by another request. Please retry.",
                409);
        }

        return ApiResponse<DeviceTokenRegistrationResponse>.SuccessResponse(
            new DeviceTokenRegistrationResponse
            {
                DeviceTokenId = existing.DeviceTokenId,
                Platform = existing.Platform,
                DeviceId = existing.DeviceId,
                IsActive = existing.IsActive,
                UpdatedAt = existing.UpdatedAt
            },
            "Device token registered successfully.");
    }

    public async Task<ApiResponse<bool>> UnregisterDeviceTokenAsync(
        Guid userId,
        string deviceToken,
        CancellationToken cancellationToken = default)
    {
        var token = deviceToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return ApiResponse<bool>.Failure("Device token is required.");
        if (token.Length > 4096)
            return ApiResponse<bool>.Failure("Device token is too long.");

        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(
                d => d.UserId == userId && d.Token == token,
                cancellationToken);

        if (existing == null || !existing.IsActive)
            return ApiResponse<bool>.SuccessResponse(true, "Device token is unregistered.");

        existing.IsActive = false;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Device token unregistered successfully.");
    }

    public async Task<ApiResponse<PagedResult<NotificationResponse>>> GetUserNotificationsAsync(
        Guid userId,
        bool? isRead,
        string? type,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;
        var safePageSize = NormalizePageSize(pageSize);
        var normalizedType = TrimToNull(type)?.ToUpperInvariant();

        var query = _db.Notifications
            .AsNoTracking()
            .Include(n => n.Template)
            .Where(n => n.UserId == userId);

        if (isRead.HasValue)
            query = query.Where(n => (n.IsRead == true) == isRead.Value);

        if (normalizedType != null)
        {
            query = query.Where(n =>
                (n.Type != null && n.Type.ToUpper() == normalizedType) ||
                (n.Type == null && n.TemplateId != null && n.TemplateId.ToUpper() == normalizedType));
        }

        var totalRecords = await query.CountAsync(cancellationToken);
        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var response = notifications.Select(ToResponse).ToList();
        return ApiResponse<PagedResult<NotificationResponse>>.SuccessResponse(
            PagedResult<NotificationResponse>.Create(
                response,
                totalRecords,
                safePageNumber,
                safePageSize),
            "Notifications retrieved successfully.");
    }

    public async Task<ApiResponse<NotificationResponse>> GetNotificationByIdAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await _db.Notifications
            .AsNoTracking()
            .Include(n => n.Template)
            .FirstOrDefaultAsync(
                n => n.NotiId == notificationId && n.UserId == userId,
                cancellationToken);

        return notification == null
            ? ApiResponse<NotificationResponse>.Failure("Notification not found.", 404)
            : ApiResponse<NotificationResponse>.SuccessResponse(
                ToResponse(notification),
                "Notification retrieved successfully.");
    }

    public async Task<ApiResponse<int>> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var unreadCount = await _db.Notifications.CountAsync(
            n => n.UserId == userId && n.IsRead != true,
            cancellationToken);

        return ApiResponse<int>.SuccessResponse(
            unreadCount,
            "Unread notification count retrieved successfully.");
    }

    public async Task<ApiResponse<bool>> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(
            n => n.NotiId == notificationId && n.UserId == userId,
            cancellationToken);

        if (notification == null)
            return ApiResponse<bool>.Failure("Notification not found.", 404);

        if (notification.IsRead != true)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse<bool>.SuccessResponse(true, "Notification marked as read.");
    }

    public async Task<ApiResponse<int>> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _db.Notifications
            .Where(n => n.UserId == userId && n.IsRead != true)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        if (notifications.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return ApiResponse<int>.SuccessResponse(
            notifications.Count,
            "Notifications marked as read.");
    }

    public async Task<NotificationSendResult> SendToTokenAsync(
        string token,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var result = new NotificationSendResult { TotalTokens = 1 };
        if (string.IsNullOrWhiteSpace(token))
        {
            result.FailedSends = 1;
            result.ErrorMessage = "Device token is required.";
            return result;
        }

        if (!TryPrepareMessage(title, body, null, null, data, out var message, out var error))
        {
            result.FailedSends = 1;
            result.ErrorMessage = error;
            return result;
        }

        var responses = await SafeSendTokensAsync(
            new[] { token.Trim() },
            message.Title,
            message.Body,
            message.Data,
            cancellationToken);

        ApplySendCounts(result, responses);
        return result;
    }

    public async Task<NotificationSendResult> SendToUserAsync(
        Guid userId,
        string title,
        string body,
        string type,
        string? referenceId = null,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryPrepareMessage(title, body, type, referenceId, data, out var message, out var error))
            return new NotificationSendResult { ErrorMessage = error };

        if (!await _db.Users.AnyAsync(u => u.UserId == userId, cancellationToken))
            return new NotificationSendResult { ErrorMessage = "Notification recipient was not found." };

        var history = new Notification
        {
            NotiId = Guid.NewGuid(),
            UserId = userId,
            TemplateId = null,
            Params = JsonSerializer.Serialize(message.Data),
            Title = message.Title,
            Body = message.Body,
            Type = message.Type,
            ReferenceId = message.ReferenceId,
            DataJson = JsonSerializer.Serialize(message.Data),
            IsRead = false,
            CreatedAt = UtcNowWithoutKind(),
            DeliveryStatus = "PENDING"
        };
        _db.Notifications.Add(history);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save notification history for user {UserId}.", userId);
            return new NotificationSendResult { ErrorMessage = "Notification history could not be saved." };
        }

        var result = new NotificationSendResult();
        result.NotificationIds.Add(history.NotiId);

        var activeTokens = await _db.DeviceTokens
            .Where(d => d.UserId == userId && d.IsActive)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        result.TotalTokens = activeTokens.Count;
        if (activeTokens.Count == 0)
        {
            history.DeliveryStatus = "FAILED";
            history.FailureReason = "No active device tokens are registered.";
            result.ErrorMessage = history.FailureReason;
            await _db.SaveChangesAsync(cancellationToken);
            return result;
        }

        var allResponses = new List<FirebaseTokenSendResult>(activeTokens.Count);
        for (var offset = 0; offset < activeTokens.Count; offset += FirebaseMulticastBatchSize)
        {
            var batch = activeTokens
                .Skip(offset)
                .Take(FirebaseMulticastBatchSize)
                .Select(d => d.Token)
                .ToList();

            var batchResponses = await SafeSendTokensAsync(
                batch,
                message.Title,
                message.Body,
                message.Data,
                cancellationToken);
            allResponses.AddRange(batchResponses);
        }

        while (allResponses.Count < activeTokens.Count)
        {
            allResponses.Add(new FirebaseTokenSendResult
            {
                ErrorCode = "MissingFirebaseResponse",
                ErrorMessage = "Firebase did not return a result for this token."
            });
        }

        var usedAt = DateTime.UtcNow;
        for (var i = 0; i < activeTokens.Count; i++)
        {
            var response = allResponses[i];
            var deviceToken = activeTokens[i];
            if (response.Success)
            {
                deviceToken.LastUsedAt = usedAt;
                deviceToken.UpdatedAt = usedAt;
            }
            else if (response.IsInvalidToken)
            {
                deviceToken.IsActive = false;
                deviceToken.UpdatedAt = usedAt;
            }
        }

        ApplySendCounts(result, allResponses.Take(activeTokens.Count));
        history.DeliveryStatus = result.SuccessfulSends == result.TotalTokens
            ? "SENT"
            : result.SuccessfulSends > 0
                ? "PARTIALLY_SENT"
                : "FAILED";
        history.SentAt = result.SuccessfulSends > 0 ? usedAt : null;
        history.FailureReason = BuildFailureReason(allResponses.Take(activeTokens.Count));
        result.ErrorMessage = history.FailureReason;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist Firebase delivery results for notification {NotificationId}.",
                history.NotiId);
            result.ErrorMessage = "Firebase completed, but its delivery result could not be saved.";
        }

        return result;
    }

    public async Task<NotificationSendResult> SendToUsersAsync(
        IEnumerable<Guid> userIds,
        string title,
        string body,
        string type,
        string? referenceId = null,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var aggregate = new NotificationSendResult();
        foreach (var userId in userIds.Where(id => id != Guid.Empty).Distinct())
        {
            var result = await SendToUserAsync(
                userId,
                title,
                body,
                type,
                referenceId,
                data,
                cancellationToken);
            aggregate.Add(result);
        }

        return aggregate;
    }

    public async Task<NotificationSendResult> SendToTopicAsync(
        string topic,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var result = new NotificationSendResult { TotalTokens = 1 };
        if (string.IsNullOrWhiteSpace(topic))
        {
            result.FailedSends = 1;
            result.ErrorMessage = "Firebase topic is required.";
            return result;
        }

        if (!TryPrepareMessage(title, body, null, null, data, out var message, out var error))
        {
            result.FailedSends = 1;
            result.ErrorMessage = error;
            return result;
        }

        FirebaseTokenSendResult response;
        try
        {
            response = await _firebaseClient.SendToTopicAsync(
                topic.Trim(),
                message.Title,
                message.Body,
                message.Data,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected Firebase topic send failure.");
            response = new FirebaseTokenSendResult
            {
                ErrorCode = "FirebaseSendFailed",
                ErrorMessage = "Firebase notification delivery failed.",
                IsTemporaryFailure = true
            };
        }

        ApplySendCounts(result, new[] { response });
        return result;
    }

    public static string RenderTemplate(string? template, string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;
        if (string.IsNullOrWhiteSpace(parametersJson))
            return template;

        try
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson);
            if (parameters == null)
                return template;

            var rendered = template;
            foreach (var parameter in parameters)
            {
                var value = parameter.Value?.ToString() ?? string.Empty;
                rendered = rendered.Replace(
                    $"{{{{{parameter.Key}}}}}",
                    value,
                    StringComparison.OrdinalIgnoreCase);
                rendered = rendered.Replace(
                    $"{{{parameter.Key}}}",
                    value,
                    StringComparison.OrdinalIgnoreCase);
            }

            return rendered;
        }
        catch (JsonException)
        {
            return template;
        }
    }

    private async Task<IReadOnlyList<FirebaseTokenSendResult>> SafeSendTokensAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _firebaseClient.SendToTokensAsync(
                tokens,
                title,
                body,
                data,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected Firebase multicast send failure for {TokenCount} devices.", tokens.Count);
            return tokens.Select(_ => new FirebaseTokenSendResult
            {
                ErrorCode = "FirebaseSendFailed",
                ErrorMessage = "Firebase notification delivery failed.",
                IsTemporaryFailure = true
            }).ToList();
        }
    }

    private static NotificationResponse ToResponse(Notification notification)
    {
        return new NotificationResponse
        {
            NotiId = notification.NotiId,
            UserId = notification.UserId,
            SenderId = notification.SenderId,
            TemplateId = notification.TemplateId,
            Title = notification.Title ??
                    RenderTemplate(notification.Template?.TitleTemplate, notification.Params),
            Body = notification.Body ??
                   RenderTemplate(notification.Template?.BodyTemplate, notification.Params),
            Params = notification.Params,
            OrderId = notification.OrderId,
            Type = notification.Type ?? notification.TemplateId,
            ReferenceId = notification.ReferenceId ?? notification.OrderId?.ToString(),
            DataJson = notification.DataJson ?? notification.Params,
            IsRead = notification.IsRead == true,
            ReadAt = notification.ReadAt,
            CreatedAt = notification.CreatedAt,
            SentAt = notification.SentAt,
            DeliveryStatus = notification.DeliveryStatus
        };
    }

    private static string? ValidateRegistrationRequest(RegisterDeviceTokenRequest? request)
    {
        if (request == null)
            return "Request body is required.";

        var token = request.DeviceToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return "Device token is required.";
        if (token.Length > 4096)
            return "Device token is too long.";

        var platform = request.Platform?.Trim();
        if (string.IsNullOrWhiteSpace(platform))
            return "Platform is required.";
        if (!SupportedPlatforms.Contains(platform))
            return "Platform must be Android or iOS.";

        if (TrimToNull(request.DeviceId)?.Length > 255)
            return "Device ID must not exceed 255 characters.";
        if (TrimToNull(request.DeviceName)?.Length > 200)
            return "Device name must not exceed 200 characters.";
        if (TrimToNull(request.AppVersion)?.Length > 50)
            return "App version must not exceed 50 characters.";

        return null;
    }

    private static bool TryPrepareMessage(
        string title,
        string body,
        string? type,
        string? referenceId,
        IDictionary<string, string>? data,
        out PreparedMessage message,
        out string? error)
    {
        var normalizedTitle = title?.Trim();
        var normalizedBody = body?.Trim();
        var normalizedType = TrimToNull(type)?.ToUpperInvariant();
        var normalizedReferenceId = TrimToNull(referenceId);

        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            message = default!;
            error = "Notification title is required.";
            return false;
        }
        if (normalizedTitle.Length > 200)
        {
            message = default!;
            error = "Notification title must not exceed 200 characters.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(normalizedBody))
        {
            message = default!;
            error = "Notification body is required.";
            return false;
        }
        if (normalizedBody.Length > 1000)
        {
            message = default!;
            error = "Notification body must not exceed 1000 characters.";
            return false;
        }
        if (type != null && string.IsNullOrWhiteSpace(normalizedType))
        {
            message = default!;
            error = "Notification type is required.";
            return false;
        }
        if (normalizedType?.Length > 50)
        {
            message = default!;
            error = "Notification type must not exceed 50 characters.";
            return false;
        }
        if (normalizedReferenceId?.Length > 100)
        {
            message = default!;
            error = "Notification reference ID must not exceed 100 characters.";
            return false;
        }

        var normalizedData = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data != null)
        {
            foreach (var pair in data)
            {
                var key = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
                {
                    message = default!;
                    error = "Firebase data keys must contain between 1 and 128 characters.";
                    return false;
                }
                if (SensitiveDataKeys.Contains(key))
                {
                    message = default!;
                    error = $"Sensitive Firebase data key '{key}' is not allowed.";
                    return false;
                }
                if (pair.Value == null)
                {
                    message = default!;
                    error = $"Firebase data value for '{key}' must be a string.";
                    return false;
                }

                normalizedData[key] = pair.Value;
            }
        }

        if (normalizedType != null)
            normalizedData["type"] = normalizedType;
        if (normalizedReferenceId != null)
            normalizedData["referenceId"] = normalizedReferenceId;

        if (normalizedData.Count > 50 ||
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(normalizedData)) > 3500)
        {
            message = default!;
            error = "Firebase data payload is too large.";
            return false;
        }

        message = new PreparedMessage(
            normalizedTitle,
            normalizedBody,
            normalizedType,
            normalizedReferenceId,
            normalizedData);
        error = null;
        return true;
    }

    private static void ApplySendCounts(
        NotificationSendResult target,
        IEnumerable<FirebaseTokenSendResult> responses)
    {
        foreach (var response in responses)
        {
            if (response.Success)
                target.SuccessfulSends++;
            else
                target.FailedSends++;
        }

        if (target.FailedSends > 0)
            target.ErrorMessage = BuildFailureReason(responses);
    }

    private static string? BuildFailureReason(IEnumerable<FirebaseTokenSendResult> responses)
    {
        var errors = responses
            .Where(r => !r.Success)
            .Select(r => string.IsNullOrWhiteSpace(r.ErrorCode) ? "FirebaseSendFailed" : r.ErrorCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        return errors.Count == 0
            ? null
            : $"Firebase delivery failed: {string.Join(", ", errors)}.";
    }

    private static string NormalizePlatform(string platform)
        => platform.Trim().Equals("ios", StringComparison.OrdinalIgnoreCase) ? "iOS" : "Android";

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int NormalizePageSize(int pageSize)
        => Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);

    private static DateTime UtcNowWithoutKind()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private sealed record PreparedMessage(
        string Title,
        string Body,
        string? Type,
        string? ReferenceId,
        IReadOnlyDictionary<string, string> Data);
}
