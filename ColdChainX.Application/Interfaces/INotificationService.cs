using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces;

public interface INotificationService
{
    Task<ApiResponse<DeviceTokenRegistrationResponse>> RegisterDeviceTokenAsync(
        Guid userId,
        RegisterDeviceTokenRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> UnregisterDeviceTokenAsync(
        Guid userId,
        string deviceToken,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResult<NotificationResponse>>> GetUserNotificationsAsync(
        Guid userId,
        bool? isRead,
        string? type,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<NotificationResponse>> GetNotificationByIdAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<int>> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<int>> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<NotificationSendResult> SendToTokenAsync(
        string token,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);

    Task<NotificationSendResult> SendToUserAsync(
        Guid userId,
        string title,
        string body,
        string type,
        string? referenceId = null,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);

    Task<NotificationSendResult> SendToUsersAsync(
        IEnumerable<Guid> userIds,
        string title,
        string body,
        string type,
        string? referenceId = null,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);

    Task<NotificationSendResult> SendToTopicAsync(
        string topic,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);
}
