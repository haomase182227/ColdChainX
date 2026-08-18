using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class PutawayLpnCommandHandler : IRequestHandler<PutawayLpnCommand, PutawayLpnResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly IMediator _mediator;

    public PutawayLpnCommandHandler(IApplicationDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<PutawayLpnResponse> Handle(PutawayLpnCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns
            .Include(l => l.Receipt)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
        {
            return new PutawayLpnResponse { Success = false, Message = "LPN not found." };
        }

        if (lpn.State != LpnState.RECEIVING)
        {
            return new PutawayLpnResponse { Success = false, Message = $"LPN is not in RECEIVING state. Current state: {lpn.State}" };
        }

        if (lpn.Receipt == null || string.IsNullOrWhiteSpace(lpn.Receipt.PdfUrl))
        {
            return new PutawayLpnResponse
            {
                Success = false,
                Message = "Warehouse receipt must be generated before putaway."
            };
        }

        if (string.IsNullOrWhiteSpace(request.StorageLocation))
        {
            return new PutawayLpnResponse { Success = false, Message = "StorageLocation is required." };
        }

        if (request.WarehouseId == Guid.Empty)
        {
            return new PutawayLpnResponse { Success = false, Message = "WarehouseId is required." };
        }

        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.WarehouseId == request.WarehouseId, cancellationToken);
        if (!warehouseExists)
        {
            return new PutawayLpnResponse { Success = false, Message = "Warehouse not found." };
        }

        lpn.StorageLocation = request.StorageLocation;
        lpn.WarehouseId = request.WarehouseId;
        lpn.InboundTime = DateTime.UtcNow;
        lpn.State = LpnState.IN_STOCK;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        var orderLpns = await _context.Lpns
            .Where(item => item.OrderId == lpn.OrderId && item.State != LpnState.DELETED)
            .ToListAsync(cancellationToken);
        if (orderLpns.Count > 0 && orderLpns.All(item => item.State == LpnState.IN_STOCK))
        {
            var order = await _context.TransportOrders
                .FirstOrDefaultAsync(item => item.OrderId == lpn.OrderId, cancellationToken);
            if (order != null)
                order.Status = "IN_STOCK";

            var asns = await _context.InboundAsns
                .Where(item => item.OrderId == lpn.OrderId)
                .ToListAsync(cancellationToken);
            foreach (var asn in asns)
                asn.Status = "PUTAWAY_COMPLETED";

            if (lpn.Receipt != null)
                lpn.Receipt.ReferenceDocNo = "COMPLETED";

            await _context.SaveChangesAsync(cancellationToken);
        }

        await _mediator.Publish(new Events.LpnPutawayCompletedEvent(lpn.OrderId, lpn.LpnId), cancellationToken);

        return new PutawayLpnResponse 
        { 
            Success = true, 
            Message = $"LPN successfully putaway to {request.StorageLocation}."
        };
    }
}
