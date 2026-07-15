using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Features.Events.Handlers;

public class LpnPutawayCompletedEventHandler : INotificationHandler<LpnPutawayCompletedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<LpnPutawayCompletedEventHandler> _logger;

    public LpnPutawayCompletedEventHandler(IApplicationDbContext context, ILogger<LpnPutawayCompletedEventHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task Handle(LpnPutawayCompletedEvent notification, CancellationToken cancellationToken)
    {
        // Không tự động đổi trạng thái Order thành IN_WAREHOUSE nữa.
        // Chỉ cần cập nhật LPN tới trạng thái IN_STOCK là đủ.
        _logger.LogInformation("LPN Putaway completed for Order {OrderId}. Order status unchanged.", notification.OrderId);
        return Task.CompletedTask;
    }
}
