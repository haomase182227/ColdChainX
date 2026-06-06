using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface INotificationService
    {
        Task<ApiResponse<IReadOnlyCollection<NotificationResponse>>> GetUserNotificationsAsync(Guid userId, bool unreadOnly);
        Task<ApiResponse<NotificationResponse>> GetNotificationByIdAsync(Guid notificationId);
        Task<ApiResponse<bool>> MarkAsReadAsync(Guid notificationId);
        Task<ApiResponse<int>> MarkAllAsReadAsync(Guid userId);
    }
}
