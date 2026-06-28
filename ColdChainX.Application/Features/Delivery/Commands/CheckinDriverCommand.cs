using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class CheckinDriverCommand : IRequest<ApiResponse<CheckinDriverResponse>>
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public Guid StopId { get; set; }
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class CheckinDriverCommandHandler : IRequestHandler<CheckinDriverCommand, ApiResponse<CheckinDriverResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public CheckinDriverCommandHandler(IApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<ApiResponse<CheckinDriverResponse>> Handle(CheckinDriverCommand request, CancellationToken cancellationToken)
    {
        // 1. Find the TripStop to check in
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
            throw new ForbiddenException("You are not authorized to check in for this trip.");

        // 4. Retrieve the Location details for the stop
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == stop.LocationId, cancellationToken);
        if (location == null)
            throw new NotFoundException($"Location for trip stop was not found.");

        // 5. Calculate Haversine distance
        double distance = CalculateDistanceInMeters(
            (double)request.Latitude,
            (double)request.Longitude,
            (double)location.Latitude,
            (double)location.Longitude
        );

        // 6. Read max allowed distance from configuration or fallback to 200 meters
        double maxDistance = 200.0;
        if (_configuration != null)
        {
            var configVal = _configuration["DeliverySettings:MaxCheckinDistanceMeters"];
            if (!string.IsNullOrEmpty(configVal) && double.TryParse(configVal, out var parsedVal))
            {
                maxDistance = parsedVal;
            }
        }

        // 7. Check if driver is within allowed radius
        if (distance > maxDistance)
        {
            throw new ValidationException($"Check-in failed. You are too far from the stop location '{location.Address}'. Current distance: {distance:F0} meters. Max allowed distance is {maxDistance:F0} meters.");
        }

        // 8. Cut the active seal associated with the trip
        var activeSeal = await _context.Seals
            .FirstOrDefaultAsync(s => s.TripId == trip.TripId && s.Status == "APPLIED", cancellationToken);
        string? removedSealCode = null;
        if (activeSeal != null)
        {
            activeSeal.Status = "REMOVED";
            activeSeal.RemovedAt = DateTime.UtcNow;
            removedSealCode = activeSeal.SealCode;
        }

        // 9. Update TripStop arrival time and status
        var checkinTime = DateTime.UtcNow;
        stop.ActualArrivalTime = checkinTime;
        stop.Status = "ARRIVED";

        // 10. Fetch LPNs for this destination location stop and sort by LIFO
        var lpns = await _context.Lpns
            .Include(l => l.Order)
            .Where(l => l.TripId == trip.TripId && l.Order.DestLocation == stop.LocationId)
            .OrderByDescending(l => l.InboundTime ?? l.CreatedAt)
            .ToListAsync(cancellationToken);

        var lpnsToUnload = lpns.Select((l, idx) => new LpnUnloadInfo
        {
            LpnId = l.LpnId,
            LpnCode = l.LpnCode,
            ItemName = l.Order?.ItemName ?? "Unknown Item",
            Quantity = l.Quantity,
            UnloadOrder = idx + 1,
            TempCondition = l.Order?.TempCondition ?? "NORMAL"
        }).ToList();

        await _context.SaveChangesAsync(cancellationToken);

        var response = new CheckinDriverResponse
        {
            StopId = stop.StopId,
            CheckinTime = checkinTime,
            RemovedSealCode = removedSealCode,
            LpnsToUnload = lpnsToUnload
        };

        return ApiResponse<CheckinDriverResponse>.SuccessResponse(response, "Driver checked in successfully at the delivery location.");
    }

    private static double CalculateDistanceInMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }
}
