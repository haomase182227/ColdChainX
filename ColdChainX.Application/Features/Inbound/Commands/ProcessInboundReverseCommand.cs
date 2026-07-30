using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ProcessInboundReverseCommand : IRequest<ApiResponse<WarehouseReceipt>>
{
    public Guid WarehouseId { get; set; }
    public Guid UserId { get; set; } // The Hub User scanning the items
    public List<string> LpnCodes { get; set; } = new List<string>();
    
    // Optional: Identify which driver/vehicle returned them to update location
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
}

public class ProcessInboundReverseCommandHandler : IRequestHandler<ProcessInboundReverseCommand, ApiResponse<WarehouseReceipt>>
{
    private readonly IApplicationDbContext _context;

    public ProcessInboundReverseCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<WarehouseReceipt>> Handle(ProcessInboundReverseCommand request, CancellationToken cancellationToken)
    {
        if (request.LpnCodes == null || !request.LpnCodes.Any())
            throw new ValidationException("List of LpnCodes is required.");

        var warehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.WarehouseId == request.WarehouseId, cancellationToken);
            
        if (warehouse == null)
            throw new NotFoundException("Warehouse not found.");

        var lpns = await _context.Lpns
            .Where(l => request.LpnCodes.Contains(l.LpnCode))
            .ToListAsync(cancellationToken);

        if (lpns.Count != request.LpnCodes.Count)
            throw new ValidationException("Some LPNs were not found in the system.");

        var invalidLpns = lpns.Where(l => l.State != LpnState.RETURN_PENDING && l.State != LpnState.DELIVERY_RETURNED).ToList();
        if (invalidLpns.Any())
        {
            var invalidCodes = string.Join(", ", invalidLpns.Select(l => l.LpnCode));
            throw new ValidationException($"The following LPNs are not in a valid return state (must be RETURN_PENDING or DELIVERY_RETURNED): {invalidCodes}");
        }

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Create Reverse Warehouse Receipt
            var receipt = new WarehouseReceipt
            {
                ReceiptId = Guid.NewGuid(),
                ReceiptCode = $"WR-REV-{DateTime.UtcNow:yyyyMMddHHmmss}",
                WarehouseId = request.WarehouseId,
                ReceiptType = "REVERSE_LOGISTICS",
                ReceiverId = request.UserId,
                DelivererName = "Hub Return",
                CreatedAt = DateTime.UtcNow
            };
            
            _context.WarehouseReceipts.Add(receipt);

            // Update LPNs to IN_STOCK at the Hub
            foreach (var lpn in lpns)
            {
                lpn.State = LpnState.IN_STOCK;
                lpn.WarehouseId = request.WarehouseId;
                lpn.ReceiptId = receipt.ReceiptId;
                lpn.UpdatedAt = DateTime.UtcNow;
                // Optionally clear routing data if they were on a trip
                lpn.RouteId = null;
                lpn.TripId = null;
            }

            // Update Vehicle and Driver Location
            if (request.VehicleId.HasValue)
            {
                var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId.Value, cancellationToken);
                if (vehicle != null)
                {
                    vehicle.CurrentLocation = warehouse.Address;
                }
            }

            if (request.DriverId.HasValue)
            {
                var driver = await _context.Drivers.FirstOrDefaultAsync(d => d.DriverId == request.DriverId.Value, cancellationToken);
                if (driver != null)
                {
                    driver.CurrentLocation = warehouse.Address;
                }
            }

            // Update associated Transport Orders if all LPNs are reversed
            var orderIds = lpns.Select(l => l.OrderId).Distinct().ToList();
            var orders = await _context.TransportOrders
                .Where(o => orderIds.Contains(o.OrderId))
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                var allLpnsForOrder = await _context.Lpns.Where(l => l.OrderId == order.OrderId).ToListAsync(cancellationToken);
                
                // If all LPNs of this order are now in stock (meaning they failed delivery and came back)
                // We should change order status to RETURNED_TO_HUB or similar. For now we use RETURNED
                if (allLpnsForOrder.All(l => l.State == LpnState.IN_STOCK))
                {
                    order.Status = "RETURNED_TO_HUB";
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ApiResponse<WarehouseReceipt>.SuccessResponse(receipt, "Inbound reverse processed successfully.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
