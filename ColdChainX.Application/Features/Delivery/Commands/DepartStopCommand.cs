using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class DepartStopCommand : IRequest<ApiResponse<DepartResponse>>
{
    public Guid StopId { get; set; }
    public string? NewSealCode { get; set; }
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class DepartStopCommandHandler : IRequestHandler<DepartStopCommand, ApiResponse<DepartResponse>>
{
    private readonly IApplicationDbContext _context;

    public DepartStopCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<DepartResponse>> Handle(DepartStopCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch TripStop and validate existence
        var stop = await _context.TripStops
            .FirstOrDefaultAsync(ts => ts.StopId == request.StopId, cancellationToken);
        if (stop == null)
            throw new NotFoundException($"Trip stop with ID '{request.StopId}' was not found.");

        if (stop.TripId == null)
            throw new ValidationException("Stop is not assigned to any trip.");

        // 2. Fetch Trip and validate existence
        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == stop.TripId.Value, cancellationToken);
        if (trip == null)
            throw new NotFoundException($"Trip with ID '{stop.TripId.Value}' was not found.");

        // 3. Validate driver is assigned to this trip
        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == request.UserId, cancellationToken);
        if (driver == null)
            throw new ForbiddenException("Driver profile not found for current user.");

        var isAssignedDriver = await _context.TripDrivers
            .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId, cancellationToken);
        if (!isAssignedDriver)
            throw new ForbiddenException("You are not authorized to depart stops for this trip.");

        // 4. Validate stop flow (must have checked in first, cannot double-depart)
        if (stop.ActualArrivalTime == null)
            throw new ValidationException("Cannot depart. You must check in at this stop first.");

        if (stop.ActualDepartureTime != null)
            throw new ValidationException("This stop has already been departed.");

        var departTime = DateTime.UtcNow;

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // 5. Update TripStop actual departure time and status
                stop.ActualDepartureTime = departTime;
                stop.Status = "DEPARTED";

                // 6. Handle New Seal kẹp chì mới
                if (!string.IsNullOrWhiteSpace(request.NewSealCode))
                {
                    var newSeal = new Seal
                    {
                        SealId = Guid.NewGuid(),
                        TripId = trip.TripId,
                        StopId = stop.StopId,
                        SealCode = request.NewSealCode,
                        AppliedAt = departTime,
                        Status = "APPLIED",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Seals.Add(newSeal);

                    // Update trip current seal code
                    trip.SealNumber = request.NewSealCode;
                }

                // Save stop & seal updates so we can query database state correctly
                await _context.SaveChangesAsync(cancellationToken);

                // 7. Check if all stops on this trip are completed (have ActualDepartureTime)
                var pendingStops = await _context.TripStops
                    .AnyAsync(ts => ts.TripId == trip.TripId && ts.ActualDepartureTime == null, cancellationToken);

                var tripCompleted = false;
                if (!pendingStops)
                {
                    tripCompleted = true;
                    trip.Status = "COMPLETED";
                    trip.CompletedAt = departTime;

                    // Release Vehicle
                    if (trip.VehicleId != null)
                    {
                        var vehicle = await _context.Vehicles
                            .FirstOrDefaultAsync(v => v.VehicleId == trip.VehicleId.Value, cancellationToken);
                        if (vehicle != null)
                        {
                            vehicle.Status = "ACTIVE";
                        }
                    }

                    // Release Drivers
                    var tripDrivers = await _context.TripDrivers
                        .Where(td => td.TripId == trip.TripId)
                        .ToListAsync(cancellationToken);

                    foreach (var td in tripDrivers)
                    {
                        var d = await _context.Drivers
                            .FirstOrDefaultAsync(dr => dr.DriverId == td.DriverId, cancellationToken);
                        if (d != null)
                        {
                            d.Status = "ACTIVE";
                        }
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var response = new DepartResponse
                {
                    StopId = stop.StopId,
                    DepartTime = departTime,
                    NewSealCode = request.NewSealCode,
                    TripCompleted = tripCompleted
                };

                var msg = tripCompleted 
                    ? "Departed stop and completed the entire trip successfully." 
                    : "Departed stop successfully.";

                return ApiResponse<DepartResponse>.SuccessResponse(response, msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
