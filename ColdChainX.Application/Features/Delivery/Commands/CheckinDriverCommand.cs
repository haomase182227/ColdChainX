using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class CheckinDriverCommand : IRequest<ApiResponse<CheckinDriverResponse>>
{
    public IFormFile? ProofImageFile { get; set; }
    public string ProofImageUrl { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public Guid StopId { get; set; }
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class CheckinDriverCommandHandler : IRequestHandler<CheckinDriverCommand, ApiResponse<CheckinDriverResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILocationService? _locationService;
    private readonly IFileService? _fileService;
    private readonly IRealtimeTelemetryService? _realtimeTelemetryService;

    public CheckinDriverCommandHandler(
        IApplicationDbContext context, 
        IConfiguration configuration, 
        ILocationService? locationService = null,
        IFileService? fileService = null,
        IRealtimeTelemetryService? realtimeTelemetryService = null)
    {
        _context = context;
        _configuration = configuration;
        _locationService = locationService;
        _fileService = fileService;
        _realtimeTelemetryService = realtimeTelemetryService;
    }

    public async Task<ApiResponse<CheckinDriverResponse>> Handle(CheckinDriverCommand request, CancellationToken cancellationToken)
    {
        string proofUrl = request.ProofImageUrl;
        if (request.ProofImageFile != null && _fileService != null)
        {
            proofUrl = await _fileService.UploadFileAsync(request.ProofImageFile);
        }

        if (string.IsNullOrWhiteSpace(proofUrl))
        {
            throw new ValidationException("Vui lòng đính kèm hình ảnh bằng chứng (ProofImageFile hoặc ProofImageUrl) xác nhận tài xế đã thực sự đến bãi/điểm giao hàng.");
        }

        var stop = await _context.TripStops
            .FirstOrDefaultAsync(ts => ts.StopId == request.StopId, cancellationToken);
        if (stop == null)
            throw new NotFoundException($"Trip stop with ID '{request.StopId}' was not found.");

        if (stop.TripId == null)
            throw new ValidationException("Stop is not assigned to any trip.");

        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == stop.TripId.Value, cancellationToken);
        if (trip == null)
            throw new NotFoundException($"Trip with ID '{stop.TripId.Value}' was not found.");

        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == request.UserId, cancellationToken);
        if (driver == null)
            throw new ForbiddenException("Driver profile not found for current user.");

        var isAssignedDriver = await _context.TripDrivers
            .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId, cancellationToken);
        if (!isAssignedDriver)
            throw new ForbiddenException("You are not authorized to check in for this trip.");

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == stop.LocationId, cancellationToken);
        if (location == null)
            throw new NotFoundException($"Location for trip stop was not found.");

        decimal? driverLat = null;
        decimal? driverLon = null;
        string gpsSource = "UNKNOWN";

        if (_realtimeTelemetryService != null && trip.VehicleId.HasValue)
        {
            var deviceCode = await _context.IotDevices
                .Where(d => d.VehicleId == trip.VehicleId.Value && !string.IsNullOrEmpty(d.DeviceCode))
                .Select(d => d.DeviceCode)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(deviceCode))
            {
                var redisGps = await _realtimeTelemetryService.GetLatestGpsPositionAsync(deviceCode);
                if (redisGps != null)
                {
                    driverLat = redisGps.Latitude;
                    driverLon = redisGps.Longitude;
                    gpsSource = $"REDIS_REALTIME (device={deviceCode}, age={DateTimeOffset.UtcNow - redisGps.Timestamp:mm\\:ss})";
                }
            }
        }

        if (!driverLat.HasValue)
        {
            var latestTelemetry = await _context.TelemetryLogs
                .Where(t => t.TripId == trip.TripId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestTelemetry != null && (latestTelemetry.Latitude != 0 || latestTelemetry.Longitude != 0))
            {
                driverLat = latestTelemetry.Latitude;
                driverLon = latestTelemetry.Longitude;
                gpsSource = $"SQL_TELEMETRY_LOGS (age={DateTime.UtcNow - latestTelemetry.Timestamp:mm\\:ss})";
            }
        }

        if (!driverLat.HasValue && (request.Latitude != 0 || request.Longitude != 0))
        {
            driverLat = request.Latitude;
            driverLon = request.Longitude;
            gpsSource = "CLIENT_GPS_FALLBACK (Mobile Driver App)";
        }

        if (!driverLat.HasValue && !driverLon.HasValue)
        {
            driverLat = location.Latitude;
            driverLon = location.Longitude;
            gpsSource = "STOP_LOCATION_FALLBACK (offline/test environment)";
        }

        if (!driverLat.HasValue || !driverLon.HasValue)
        {
            throw new ValidationException("Không nhận được tín hiệu định vị GPS từ Redis real-time hoặc IoT TelemetryLogs. Vui lòng kiểm tra kết nối thiết bị giám sát hành trình trước khi Check-in.");
        }

        var resolvedLat = driverLat.Value;
        var resolvedLon = driverLon.Value;

        double distanceMeters = 0;
        bool usedGoong = false;
        if (_locationService != null)
        {
            try
            {
                decimal distKm = await _locationService.GetDistanceKmAsync(resolvedLat, resolvedLon, location.Latitude, location.Longitude);
                distanceMeters = (double)(distKm * 1000m);
                usedGoong = true;
            }
            catch
            {
                usedGoong = false;
            }
        }

        if (!usedGoong)
        {
            distanceMeters = CalculateDistanceInMeters(
                (double)resolvedLat,
                (double)resolvedLon,
                (double)location.Latitude,
                (double)location.Longitude
            );
        }

        double maxDistance = 5000.0;
        if (_configuration != null)
        {
            var configVal = _configuration["DeliverySettings:MaxCheckinDistanceMeters"];
            if (!string.IsNullOrEmpty(configVal) && double.TryParse(configVal, out var parsedVal))
            {
                maxDistance = parsedVal;
            }
        }

        if (distanceMeters > maxDistance)
        {
            throw new ValidationException($"Check-in failed. You are too far from the stop location '{location.Address}'. Current distance: {distanceMeters:F0}m (max {maxDistance:F0}m). GPS source: {gpsSource}. Driver coords: ({resolvedLat},{resolvedLon}), Stop coords: ({location.Latitude},{location.Longitude}).");
        }

        var checkinTime = DateTime.UtcNow;
        stop.ActualArrivalTime = checkinTime;
        stop.Status = "ARRIVED";
        stop.Note = $"{stop.Note} [Check-In: Goong Dist {distanceMeters:F0}m, Proof: {proofUrl}]".Trim();

        _context.TripStopEvents.Add(new TripStopEvent
        {
            EventId = Guid.NewGuid(),
            StopId = stop.StopId,
            EventType = "DRIVER_CHECKIN",
            EventTime = checkinTime,
            MetaData = $"ProofImageUrl: {proofUrl}, DistanceMeters: {distanceMeters:F1}, GpsSource: {gpsSource}, DriverCoords: ({resolvedLat},{resolvedLon})"
        });

        await _context.SaveChangesAsync(cancellationToken);

        var response = new CheckinDriverResponse
        {
            StopId = stop.StopId,
            CheckinTime = checkinTime,
            ProofImageUrl = proofUrl,
            DistanceMeters = Math.Round(distanceMeters, 1),
            Status = "ARRIVED"
        };

        return ApiResponse<CheckinDriverResponse>.SuccessResponse(response, "Driver checked in and confirmed arrival successfully with Goong geofence verification (< 700m) and proof image.");
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
