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

        // If all LPNs are SHIPPED, change the Order status to COMPLETED
        if (orderLpns.Any() && orderLpns.All(l => l.State == LpnState.SHIPPED))
        {
            var order = await _context.TransportOrders
                .FirstOrDefaultAsync(o => o.OrderId == notification.OrderId, cancellationToken);
                
            if (order != null && order.Status != "COMPLETED")
            {
                order.Status = "COMPLETED";
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Order {OrderId} is now COMPLETED because all LPNs have been shipped.", order.OrderId);
            }
        }
    }
}
