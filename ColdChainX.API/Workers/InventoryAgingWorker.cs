using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdChainX.API.Workers;

public class InventoryAgingWorker : BackgroundService
{
    private readonly ILogger<InventoryAgingWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // Run every 5 minutes

    // In-memory set to prevent spamming notifications for the same LPN over and over.
    // In a real production system, you'd store a boolean "IsAgingNotified" in the DB.
    private readonly HashSet<Guid> _notifiedLpns = new();

    public InventoryAgingWorker(ILogger<InventoryAgingWorker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InventoryAgingWorker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAgingInventoryAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing InventoryAgingWorker.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task ProcessAgingInventoryAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

        var now = DateTime.UtcNow;

        // Find LPNs that are in the warehouse (IN_STOCK) and not yet assigned to a trip
        var agingLpns = await db.Lpns
            .Include(l => l.Receipt)
            .Where(l => l.State == LpnState.IN_STOCK && l.TripId == null && l.InboundTime != null)
            .ToListAsync(stoppingToken);

        _logger.LogInformation("InventoryAgingWorker is scanning... Found {Count} LPN(s) in IN_STOCK state waiting for dispatch.", agingLpns.Count);

        var dispatchers = await db.Users
            .Include(u => u.Role)
            .Where(u => u.Role.RoleName.ToLower() == "dispatcher" || u.Role.RoleName.ToLower() == "admin")
            .ToListAsync(stoppingToken);

        foreach (var lpn in agingLpns)
        {
            if (_notifiedLpns.Contains(lpn.LpnId))
                continue; // Already notified

            bool isRedAlert = false;
            string alertReason = "";

            var hoursInStorage = (now - lpn.InboundTime!.Value).TotalHours;

            if (hoursInStorage > 48)
            {
                isRedAlert = true;
                alertReason = $"quá 48h (Aging {hoursInStorage:F1}h)";
            }
            else if (lpn.SlaDeadline.HasValue && (lpn.SlaDeadline.Value - now).TotalHours < 12)
            {
                var remaining = (lpn.SlaDeadline.Value - now).TotalHours;
                isRedAlert = true;
                alertReason = $"cấp bách, chỉ còn {remaining:F1}h là trễ SLA";
            }

            if (isRedAlert)
            {
                _logger.LogWarning("LPN {LpnCode} is in RED alert: {Reason}", lpn.LpnCode, alertReason);

                // Ensure template exists
                var template = await db.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == "NOTI_AGING_ALERT", stoppingToken);
                if (template == null)
                {
                    template = new NotificationTemplate
                    {
                        TemplateId = "NOTI_AGING_ALERT",
                        TypeId = Guid.Parse("60000000-0000-0000-0000-000000000001"),
                        TitleTemplate = "Cảnh báo LPN khẩn cấp: {{LpnCode}}",
                        BodyTemplate = "Kiện hàng {{LpnCode}} đang kẹt tại bãi {{Reason}}. Yêu cầu điều phối lên chuyến xe ngay!",
                        Channel = "IN_APP",
                        Status = "ACTIVE"
                    };
                    db.NotificationTemplates.Add(template);
                    await db.SaveChangesAsync(stoppingToken);
                }

                // Find dispatchers for this warehouse (or global dispatchers)
                var targetDispatchers = dispatchers
                    .Where(u => u.WarehouseId == null || (lpn.Receipt != null && u.WarehouseId == lpn.Receipt.WarehouseId))
                    .ToList();

                foreach (var dispatcher in targetDispatchers)
                {
                    var notification = new Notification
                    {
                        NotiId = Guid.NewGuid(),
                        UserId = dispatcher.UserId,
                        TemplateId = "NOTI_AGING_ALERT",
                        Params = $"{{\"LpnCode\": \"{lpn.LpnCode}\", \"Reason\": \"{alertReason}\"}}",
                        OrderId = lpn.OrderId,
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.Notifications.Add(notification);
                }

                _notifiedLpns.Add(lpn.LpnId);
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(stoppingToken);
            
            // Push real-time signalR event to all dispatchers & admins
            await hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin")
                .SendAsync("ReceiveNotification", new 
                { 
                    Title = "Cảnh báo Aging/SLA", 
                    Message = "Có kiện hàng sắp trễ SLA hoặc lưu bãi quá 48h, vui lòng kiểm tra!", 
                    Type = "Warning" 
                }, stoppingToken);
                
            _logger.LogInformation("Successfully sent Aging/SLA warning notifications to Dispatchers and Admins via SignalR and Database.");
        }
    }
}
