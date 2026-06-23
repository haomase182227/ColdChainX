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

public class LpnShippedEventHandler : INotificationHandler<LpnShippedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<LpnShippedEventHandler> _logger;

    public LpnShippedEventHandler(IApplicationDbContext context, ILogger<LpnShippedEventHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Handle(LpnShippedEvent notification, CancellationToken cancellationToken)
    {
        var orderLpns = await _context.Lpns
            .Where(l => l.OrderId == notification.OrderId)
            .ToListAsync(cancellationToken);

        // Log khi toàn bộ LPN của đơn hàng đã lên xe — trạng thái COMPLETED sẽ được cập nhật
        // sau khi giao hàng thành công (ePOD), không phải tại thời điểm xếp xe.
        if (orderLpns.Any() && orderLpns.All(l => l.State == LpnState.RELEASED))
        {
            _logger.LogInformation(
                "All LPNs for order {OrderId} have been loaded onto the vehicle.",
                notification.OrderId);
        }
    }
}
