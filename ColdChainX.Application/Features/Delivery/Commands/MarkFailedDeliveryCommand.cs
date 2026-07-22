using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class MarkFailedDeliveryCommand : IRequest<ApiResponse<bool>>
{
    public Guid StopId { get; set; }
    public string Reason { get; set; } = null!;
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class MarkFailedDeliveryCommandHandler : IRequestHandler<MarkFailedDeliveryCommand, ApiResponse<bool>>
{
    private readonly IApplicationDbContext _context;

    public MarkFailedDeliveryCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<bool>> Handle(MarkFailedDeliveryCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch Stop and validate
        var stop = await _context.TripStops
            .FirstOrDefaultAsync(ts => ts.StopId == request.StopId, cancellationToken);
            
        if (stop == null)
            throw new NotFoundException($"Trip stop with ID '{request.StopId}' was not found.");

        if (stop.TripId == null)
            throw new ValidationException("Stop is not assigned to any trip.");

        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == stop.TripId, cancellationToken);
            
        if (trip == null)
            throw new NotFoundException("Trip not found.");

        // 2. Validate driver is assigned
        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == request.UserId, cancellationToken);
            
        if (driver == null)
            throw new ForbiddenException("Driver profile not found.");

        var isAssigned = await _context.TripDrivers
            .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId, cancellationToken);
            
        if (!isAssigned)
            throw new ForbiddenException("You are not authorized for this trip.");

        // 3. Validate SLA wait time (must be checked-in and wait at least 30 minutes)
        if (stop.ActualArrivalTime == null)
            throw new ValidationException("Cannot mark as failed delivery. You must check in (Arrive) at this stop first.");

        var waitMinutes = (DateTime.UtcNow - stop.ActualArrivalTime.Value).TotalMinutes;
        if (waitMinutes < 30)
            throw new ValidationException($"Chưa hết thời gian chờ quy định (30 phút), không được phép hủy giao hàng. Bạn mới đợi {waitMinutes:F0} phút.");

        // 4. Update Stop Status
        stop.Status = "FAILED_DELIVERY";
        stop.Note = $"Failed Delivery: {request.Reason}";
        stop.ActualDepartureTime = DateTime.UtcNow;

        // 5. Update LPNs and Transport Orders to PENDING_REDELIVERY
        var lpns = await _context.Lpns
            .Include(l => l.Order)
            .Where(l => l.TripId == trip.TripId && l.Order.DestLocation == stop.LocationId)
            .ToListAsync(cancellationToken);

        var orderIds = lpns.Select(l => l.OrderId).Distinct().ToList();

        foreach (var lpn in lpns)
        {
            lpn.State = ColdChainX.Core.Enums.LpnState.RETURN_PENDING;
            lpn.TripId = null; // Unassign from this trip
        }

        var orders = await _context.TransportOrders
            .Where(o => orderIds.Contains(o.OrderId))
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            order.Status = "PENDING_REDELIVERY";
            // Kế toán có thể hook vào trạng thái PENDING_REDELIVERY để charge phí Redelivery.
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Đánh dấu giao hàng thất bại thành công. Các đơn hàng đã được chuyển sang Chờ Giao Lại.");
    }
}
