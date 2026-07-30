using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class UpdateLocationCommand : IRequest<ApiResponse<bool>>
{
    public Guid TripId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class UpdateLocationCommandHandler : IRequestHandler<UpdateLocationCommand, ApiResponse<bool>>
{
    private readonly IApplicationDbContext _context;

    public UpdateLocationCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<bool>> Handle(UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        // 1. Update vehicle location if trip has vehicle assigned
        var trip = await _context.MasterTrips
            .Include(t => t.TripStops)
            .ThenInclude(ts => ts.Location)
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);

        if (trip == null || (trip.Status != "DISPATCHED" && trip.Status != "IN-TRANSIT"))
            return ApiResponse<bool>.SuccessResponse(true, "Location recorded. Trip is not currently active.");

        if (trip.VehicleId.HasValue)
        {
            var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == trip.VehicleId, cancellationToken);
            if (vehicle != null)
            {
                vehicle.CurrentLocation = $"{request.Latitude},{request.Longitude}";
            }
        }

        // 2. Identify the next upcoming stop
        var nextStop = trip.TripStops
            .Where(ts => ts.ActualArrivalTime == null)
            .OrderBy(ts => ts.StopSequence)
            .FirstOrDefault();

        if (nextStop != null && nextStop.Location != null)
        {
            // 3. Calculate Haversine Distance in meters
            double distance = CalculateDistanceInMeters(
                (double)request.Latitude,
                (double)request.Longitude,
                (double)nextStop.Location.Latitude,
                (double)nextStop.Location.Longitude
            );

            // 4. Geofence Check (5000m)
            if (distance <= 5000)
            {
                // Check if already notified
                var alreadyNotified = await _context.TripStopEvents
                    .AnyAsync(e => e.StopId == nextStop.StopId && e.EventType == "GEOFENCE_NOTIFIED", cancellationToken);

                if (!alreadyNotified)
                {
                    // Create Notification Event
                    _context.TripStopEvents.Add(new TripStopEvent
                    {
                        EventId = Guid.NewGuid(),
                        StopId = nextStop.StopId,
                        EventType = "GEOFENCE_NOTIFIED",
                        EventTime = DateTime.UtcNow,
                        MetaData = $"Triggered at distance {distance:F0}m"
                    });

                    // Send actual Notification to Customer
                    var customerId = await _context.TransportOrders
                        .Where(o => o.MasterTripId == trip.TripId && o.DestLocation == nextStop.LocationId)
                        .Select(o => o.CustomerId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (customerId != Guid.Empty && customerId != null)
                    {
                        var vehicle = trip.VehicleId.HasValue ? await _context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == trip.VehicleId, cancellationToken) : null;
                        string truckPlate = vehicle?.TruckPlate ?? "Không xác định";

                        // Ensure GEOFENCE_ETA template has clean Shopee-style notification without specific time details or URLs
                        var existingTemplate = await _context.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == "GEOFENCE_ETA", cancellationToken);
                        string shopeeTitle = "📦 Đơn hàng chuỗi lạnh của bạn sắp được giao tới!";
                        string shopeeBody = "Xe vận chuyển {{TruckPlate}} đang di chuyển ở sát khu vực giao hàng của bạn. Vui lòng chuẩn bị sẵn nhân sự KCS và súng bắn nhiệt để tiếp nhận hàng hoá theo quy trình LIFO nhé!";
                        
                        if (existingTemplate == null)
                        {
                            var typeId = await _context.Messagetypes.Select(m => m.TypeId).FirstOrDefaultAsync(cancellationToken);
                            _context.NotificationTemplates.Add(new NotificationTemplate
                            {
                                TemplateId = "GEOFENCE_ETA",
                                TitleTemplate = shopeeTitle,
                                BodyTemplate = shopeeBody,
                                Channel = "ALL",
                                Status = "ACTIVE",
                                TypeId = typeId
                            });
                            await _context.SaveChangesAsync(cancellationToken);
                        }
                        else if (existingTemplate.TitleTemplate != shopeeTitle || existingTemplate.BodyTemplate != shopeeBody)
                        {
                            existingTemplate.TitleTemplate = shopeeTitle;
                            existingTemplate.BodyTemplate = shopeeBody;
                        }

                        _context.Notifications.Add(new Notification
                        {
                            NotiId = Guid.NewGuid(),
                            UserId = customerId.Value,
                            TemplateId = "GEOFENCE_ETA",
                            Params = $"{{\"TruckPlate\":\"{truckPlate}\",\"TripId\":\"{trip.TripId}\",\"StopId\":\"{nextStop.StopId}\"}}",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Location updated successfully.");
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
