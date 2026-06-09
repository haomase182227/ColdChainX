using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _db;

        public NotificationService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<PagedResult<NotificationResponse>>> GetUserNotificationsAsync(Guid userId, bool unreadOnly, int pageNumber, int pageSize)
        {
            var query = _db.Notifications
                .AsNoTracking()
                .Include(n => n.Template)
                .Where(n => n.UserId == userId);

            if (unreadOnly)
                query = query.Where(n => n.IsRead != true);

            var totalRecords = await query.CountAsync();
            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(n => ToResponse(n))
                .ToListAsync();

            return ApiResponse<PagedResult<NotificationResponse>>.SuccessResponse(
                PagedResult<NotificationResponse>.Create(notifications, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Notifications retrieved successfully");
        }

        public async Task<ApiResponse<NotificationResponse>> GetNotificationByIdAsync(Guid notificationId)
        {
            var notification = await _db.Notifications
                .AsNoTracking()
                .Include(n => n.Template)
                .FirstOrDefaultAsync(n => n.NotiId == notificationId);

            if (notification == null)
                return ApiResponse<NotificationResponse>.Failure("Notification not found");

            return ApiResponse<NotificationResponse>.SuccessResponse(ToResponse(notification), "Notification retrieved successfully");
        }

        public async Task<ApiResponse<bool>> MarkAsReadAsync(Guid notificationId)
        {
            var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.NotiId == notificationId);
            if (notification == null)
                return ApiResponse<bool>.Failure("Notification not found");

            notification.IsRead = true;
            await _db.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Notification marked as read");
        }

        public async Task<ApiResponse<int>> MarkAllAsReadAsync(Guid userId)
        {
            var notifications = await _db.Notifications
                .Where(n => n.UserId == userId && n.IsRead != true)
                .ToListAsync();

            foreach (var notification in notifications)
                notification.IsRead = true;

            await _db.SaveChangesAsync();

            return ApiResponse<int>.SuccessResponse(notifications.Count, "Notifications marked as read");
        }

        private static NotificationResponse ToResponse(Notification notification)
        {
            return new NotificationResponse
            {
                NotiId = notification.NotiId,
                UserId = notification.UserId,
                SenderId = notification.SenderId,
                TemplateId = notification.TemplateId,
                Title = RenderTemplate(notification.Template.TitleTemplate, notification.Params),
                Body = RenderTemplate(notification.Template.BodyTemplate, notification.Params),
                Params = notification.Params,
                OrderId = notification.OrderId,
                IsRead = notification.IsRead == true,
                CreatedAt = notification.CreatedAt
            };
        }

        private static string RenderTemplate(string template, string parametersJson)
        {
            if (string.IsNullOrWhiteSpace(parametersJson))
                return template;

            try
            {
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(parametersJson);
                if (parameters == null)
                    return template;

                var rendered = template;
                foreach (var parameter in parameters)
                    rendered = rendered.Replace($"{{{{{parameter.Key}}}}}", parameter.Value, StringComparison.OrdinalIgnoreCase);

                return rendered;
            }
            catch (JsonException)
            {
                return template;
            }
        }

        private static int NormalizePageSize(int pageSize)
            => Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);

        private static int NormalizeSkip(int pageNumber, int pageSize)
        {
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;
            return (safePageNumber - 1) * NormalizePageSize(pageSize);
        }
    }
}
