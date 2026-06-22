using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inventory.Queries;

public class GetInventoryAgingQueryHandler : IRequestHandler<GetInventoryAgingQuery, List<InventoryAgingDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInventoryAgingQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<InventoryAgingDto>> Handle(GetInventoryAgingQuery request, CancellationToken cancellationToken)
    {
        var lpns = await _context.Lpns
            .Where(l => l.State == LpnState.IN_STOCK || l.State == LpnState.ALLOCATED)
            .Select(l => new 
            {
                l.LpnId,
                l.LpnCode,
                l.Order.ItemName,
                l.StorageLocation,
                l.InboundTime,
                l.SlaDeadline
            })
            .ToListAsync(cancellationToken);

        var result = new List<InventoryAgingDto>();
        var now = DateTime.UtcNow;

        foreach (var lpn in lpns)
        {
            double hoursInStorage = 0;
            if (lpn.InboundTime.HasValue)
            {
                hoursInStorage = (now - lpn.InboundTime.Value).TotalHours;
            }

            string color = "Green";

            if (hoursInStorage > 48 || (lpn.SlaDeadline.HasValue && (lpn.SlaDeadline.Value - now).TotalHours < 12))
            {
                color = "Red";
            }
            else if (hoursInStorage > 24)
            {
                color = "Yellow";
            }

            result.Add(new InventoryAgingDto
            {
                LpnId = lpn.LpnId,
                LpnCode = lpn.LpnCode,
                ItemName = lpn.ItemName,
                StorageLocation = lpn.StorageLocation ?? "N/A",
                InboundTime = lpn.InboundTime,
                SlaDeadline = lpn.SlaDeadline,
                HoursInStorage = Math.Round(hoursInStorage, 2),
                AgingColor = color
            });
        }

        return result;
    }
}
