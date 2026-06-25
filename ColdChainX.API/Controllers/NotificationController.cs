using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly IApplicationDbContext _context;

        public NotificationController(INotificationService notificationService, IApplicationDbContext context)
        {
            _notificationService = notificationService;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllNotifications()
        {
            var notifications = await _context.Notifications
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new
                {
                    n.NotiId,
                    n.UserId,
                    n.SenderId,
                    n.TemplateId,
                    n.Params,
                    n.OrderId,
                    n.IsRead,
                    n.CreatedAt
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpGet("users/{userId:guid}")]
        public async Task<IActionResult> GetUserNotifications(
            Guid userId,
            [FromQuery] bool unreadOnly = false,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _notificationService.GetUserNotificationsAsync(userId, unreadOnly, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("{notificationId:guid}")]
        public async Task<IActionResult> GetNotificationById(Guid notificationId)
        {
            var result = await _notificationService.GetNotificationByIdAsync(notificationId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPut("{notificationId:guid}/read")]
        public async Task<IActionResult> MarkAsRead(Guid notificationId)
        {
            var result = await _notificationService.MarkAsReadAsync(notificationId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPut("users/{userId:guid}/read-all")]
        public async Task<IActionResult> MarkAllAsRead(Guid userId)
        {
            var result = await _notificationService.MarkAllAsReadAsync(userId);
            return Ok(result);
        }
    }
}
