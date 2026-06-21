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

    public async Task Handle(LpnPutawayCompletedEvent notification, CancellationToken cancellationToken)
    {
        var orderLpns = await _context.Lpns
            .Where(l => l.OrderId == notification.OrderId)
            .ToListAsync(cancellationToken);

        // If all LPNs are IN_STOCK, change the Order status to IN_WAREHOUSE
        if (orderLpns.Any() && orderLpns.All(l => l.State == LpnState.IN_STOCK))
        {
            var order = await _context.TransportOrders
                .FirstOrDefaultAsync(o => o.OrderId == notification.OrderId, cancellationToken);
                
            if (order != null && order.Status != "IN_WAREHOUSE")
            {
                order.Status = "IN_WAREHOUSE";
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Order {OrderId} is now IN_WAREHOUSE because all LPNs have been putaway.", order.OrderId);
            }
        }
    }
}
