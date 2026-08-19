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

public class ProcessInboundReverseCommand : IRequest<ApiResponse<object>>
{
    public Guid WarehouseId { get; set; }
    public Guid UserId { get; set; } // The Hub User scanning the items
    public List<string> LpnCodes { get; set; } = new List<string>();
    
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
}

public class ProcessInboundReverseCommandHandler : IRequestHandler<ProcessInboundReverseCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public ProcessInboundReverseCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(ProcessInboundReverseCommand request, CancellationToken cancellationToken)
    {
        if (request.LpnCodes == null || !request.LpnCodes.Any())
            throw new ValidationException("List of LpnCodes is required.");

        var warehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.WarehouseId == request.WarehouseId, cancellationToken);
            
        if (warehouse == null)
            throw new NotFoundException("Warehouse not found.");

        var requestedLpnCodes = request.LpnCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedLpnCodes.Count != request.LpnCodes.Count)
            throw new ValidationException("LpnCodes must contain unique, non-empty values.");

        var pendingReturnedItems = await _context.ReturnedItems
            .Include(item => item.Epod)
            .Where(item => item.ItemCode != null
                && requestedLpnCodes.Contains(item.ItemCode)
                && item.ProcessingStatus == "PENDING_INBOUND")
            .ToListAsync(cancellationToken);

        var duplicatePendingCodes = pendingReturnedItems
            .GroupBy(item => item.ItemCode!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicatePendingCodes.Any())
            throw new ConflictException($"Multiple active return records exist for LPN(s): {string.Join(", ", duplicatePendingCodes)}.");

        var returnSlipIds = pendingReturnedItems.Select(item => item.ReturnId).ToList();
        var returnSlips = await _context.InboundReturnSlips
            .Include(slip => slip.Lpn)
            .Include(slip => slip.Order)
            .Where(slip => returnSlipIds.Contains(slip.ReturnSlipId))
            .ToListAsync(cancellationToken);

        var returnLines = new List<(InboundReturnSlip Slip, ReturnedItem ReturnedItem, Lpn Lpn, bool IsPartial)>();
        foreach (var lpnCode in requestedLpnCodes)
        {
            var returnedItem = pendingReturnedItems.SingleOrDefault(item =>
                string.Equals(item.ItemCode, lpnCode, StringComparison.OrdinalIgnoreCase));
            if (returnedItem == null)
                throw new ValidationException($"LPN '{lpnCode}' does not have an active PENDING_INBOUND return item.");

            var returnSlip = returnSlips.SingleOrDefault(slip => slip.ReturnSlipId == returnedItem.ReturnId);
            if (returnSlip?.Lpn == null)
                throw new ValidationException($"LPN '{lpnCode}' does not have a matching inbound return slip.");

            var lpn = returnSlip.Lpn;
            var isPartialReturn = lpn.State == LpnState.DELIVERED
                && string.Equals(returnedItem.Epod?.Status, "OSD_PARTIAL_DELIVER", StringComparison.OrdinalIgnoreCase)
                && returnSlip.ReturnedQty > 0;
            var isWholeLpnReturn = lpn.State == LpnState.RETURN_PENDING
                || lpn.State == LpnState.DELIVERY_RETURNED;

            if (!isWholeLpnReturn && !isPartialReturn)
            {
                throw new ValidationException(
                    $"LPN '{lpnCode}' is not eligible for reverse inbound. " +
                    $"Current state: {lpn.State}; order status: {returnSlip.Order?.Status ?? "UNKNOWN"}.");
            }

            returnLines.Add((returnSlip, returnedItem, lpn, isPartialReturn));
        }

        var distinctOrderIds = returnLines.Select(line => line.Lpn.OrderId).Distinct().ToList();
        if (distinctOrderIds.Count != 1)
            throw new ValidationException("Inbound reverse can only process LPNs from one transport order at a time.");

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var processedAt = DateTime.UtcNow;
            var receipt = new WarehouseReceipt
            {
                ReceiptId = Guid.NewGuid(),
                ReceiptCode = $"WR-REV-{processedAt:yyyyMMddHHmmss}",
                OrderId = distinctOrderIds[0],
                WarehouseId = request.WarehouseId,
                ReceiptType = "REVERSE_LOGISTICS",
                ReceiverId = request.UserId,
                DelivererName = "Hub Return",
                CreatedAt = processedAt
            };
            
            _context.WarehouseReceipts.Add(receipt);

            foreach (var line in returnLines)
            {
                line.ReturnedItem.ReturnedQty = line.Slip.ReturnedQty;
                line.ReturnedItem.ProcessingStatus = "RECEIVED_AT_HUB";
                line.ReturnedItem.ProcessedBy = request.UserId;
                line.ReturnedItem.ProcessedAt = processedAt;

                if (line.IsPartial)
                {
                    // The existing LPN represents the accepted portion. The return slip represents
                    // only the rejected units, so reverse receipt must not put that LPN back in stock.
                    continue;
                }

                line.Lpn.State = LpnState.IN_STOCK;
                line.Lpn.WarehouseId = request.WarehouseId;
                line.Lpn.ReceiptId = receipt.ReceiptId;
                line.Lpn.UpdatedAt = processedAt;
                line.Lpn.RouteId = null;
                line.Lpn.TripId = null;
            }

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

            var orderIds = returnLines.Select(line => line.Lpn.OrderId).Distinct().ToList();
            var orders = await _context.TransportOrders
                .Where(o => orderIds.Contains(o.OrderId))
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                var allLpnsForOrder = await _context.Lpns.Where(l => l.OrderId == order.OrderId).ToListAsync(cancellationToken);
                
                if (allLpnsForOrder.All(l => l.State == LpnState.IN_STOCK))
                {
                    order.Status = "RETURNED_TO_HUB";
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = new
            {
                receipt.ReceiptId,
                receipt.ReceiptCode,
                receipt.OrderId,
                receipt.WarehouseId,
                receipt.ReceiptType,
                receipt.CreatedAt,
                LpnCodes = returnLines.Select(line => line.Lpn.LpnCode).ToList(),
                ReturnLines = returnLines.Select(line => new
                {
                    LpnCode = line.Lpn.LpnCode,
                    ReturnedQuantity = line.Slip.ReturnedQty,
                    IsPartialReturn = line.IsPartial,
                    LpnStateAfterReverse = line.Lpn.State.ToString(),
                    AcceptedLpnQuantityPreserved = line.IsPartial ? line.Lpn.Quantity : (int?)null
                }).ToList()
            };

            return ApiResponse<object>.SuccessResponse(response, "Inbound reverse processed successfully.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
