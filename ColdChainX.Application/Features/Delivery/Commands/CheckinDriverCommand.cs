using System;
using System.Collections.Generic;
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
    // Optional client metadata kept for backward compatibility. It must never
    // participate in check-in eligibility; only vehicle telemetry is authoritative.
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset? LocationTimestamp { get; set; }
    public double? AccuracyMeters { get; set; }
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
        if (request.ProofImageFile == null && string.IsNullOrWhiteSpace(request.ProofImageUrl))
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

        var stopStatus = stop.Status?.Trim().ToUpperInvariant() ?? string.Empty;
        if (stop.ActualArrivalTime.HasValue || stopStatus == "ARRIVED")
            throw new ConflictException("Điểm dừng này đã được check-in trước đó.");

        var checkinReadyStatuses = new[] { "PLANNED", "EN_ROUTE", "DELAYED_INCIDENT" };
        if (!checkinReadyStatuses.Contains(stopStatus))
            throw new ConflictException($"Không thể check-in điểm dừng ở trạng thái '{stop.Status ?? "UNKNOWN"}'.");

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == stop.LocationId, cancellationToken);
        if (location == null)
            throw new NotFoundException($"Location for trip stop was not found.");

        decimal? vehicleLat = null;
        decimal? vehicleLon = null;
        string gpsSource = "UNKNOWN";
        var now = DateTimeOffset.UtcNow;
        var maxGpsAge = TimeSpan.FromSeconds(GetPositiveConfigurationValue(
            "DeliverySettings:MaxGpsAgeSeconds",
            300));

        var vehicleDevices = trip.VehicleId.HasValue
            ? await _context.IotDevices
                .Where(device => device.VehicleId == trip.VehicleId.Value)
                .OrderByDescending(device => device.IsOnline)
                .ThenByDescending(device => device.LastPingTime)
                .ToListAsync(cancellationToken)
            : new List<IotDevice>();

        if (_realtimeTelemetryService != null)
        {
            RealtimeGpsPosition? latestRedisGps = null;
            foreach (var vehicleDevice in vehicleDevices.Where(device => !string.IsNullOrWhiteSpace(device.DeviceCode)))
            {
                var redisGps = await _realtimeTelemetryService.GetLatestGpsPositionAsync(vehicleDevice.DeviceCode!);
                if (redisGps != null
                    && HasUsableCoordinates(redisGps.Latitude, redisGps.Longitude)
                    && IsFresh(redisGps.Timestamp, now, maxGpsAge)
                    && (latestRedisGps == null || redisGps.Timestamp > latestRedisGps.Timestamp))
                {
                    latestRedisGps = redisGps;
                }
            }

            if (latestRedisGps != null)
            {
                vehicleLat = latestRedisGps.Latitude;
                vehicleLon = latestRedisGps.Longitude;
                gpsSource = $"REDIS_REALTIME (device={latestRedisGps.DeviceCode}, age={now - latestRedisGps.Timestamp:mm\\:ss})";
            }
        }

        if (!vehicleLat.HasValue && vehicleDevices.Count > 0)
        {
            var vehicleDeviceIds = vehicleDevices.Select(device => device.DeviceId).ToList();
            var latestTelemetry = await _context.TelemetryLogs
                .Where(telemetry => telemetry.TripId == trip.TripId
                    && telemetry.DeviceId.HasValue
                    && vehicleDeviceIds.Contains(telemetry.DeviceId.Value))
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync(cancellationToken);

            var telemetryTimestamp = latestTelemetry == null
                ? (DateTimeOffset?)null
                : ToUtcDateTimeOffset(latestTelemetry.Timestamp);
            if (latestTelemetry != null
                && telemetryTimestamp.HasValue
                && HasUsableCoordinates(latestTelemetry.Latitude, latestTelemetry.Longitude)
                && IsFresh(telemetryTimestamp.Value, now, maxGpsAge))
            {
                vehicleLat = latestTelemetry.Latitude;
                vehicleLon = latestTelemetry.Longitude;
                gpsSource = $"SQL_TELEMETRY_LOGS (device={latestTelemetry.DeviceId}, age={now - telemetryTimestamp.Value:mm\\:ss})";
            }
        }

        if (!vehicleLat.HasValue || !vehicleLon.HasValue)
        {
            throw new ValidationException("Không nhận được GPS từ xe.");
        }

        var resolvedLat = vehicleLat.Value;
        var resolvedLon = vehicleLon.Value;

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

        var maxDistance = GetPositiveConfigurationValue(
            "DeliverySettings:MaxCheckinDistanceMeters",
            10000);

        if (distanceMeters > maxDistance)
        {
            throw new ValidationException($"Xe chưa ở trong phạm vi {maxDistance / 1000:F0} km của điểm giao hàng '{location.Address}'. Khoảng cách hiện tại: {distanceMeters:F0} m. Nguồn GPS: {gpsSource}. Tọa độ xe: ({resolvedLat},{resolvedLon}), tọa độ điểm giao: ({location.Latitude},{location.Longitude}).");
        }

        string proofUrl = request.ProofImageUrl;
        if (request.ProofImageFile != null && _fileService != null)
        {
            proofUrl = await _fileService.UploadFileAsync(request.ProofImageFile);
        }

        if (string.IsNullOrWhiteSpace(proofUrl))
        {
            throw new ValidationException("Không thể lưu ảnh bằng chứng check-in.");
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
            MetaData = $"ProofImageUrl: {proofUrl}, DistanceMeters: {distanceMeters:F1}, GpsSource: {gpsSource}, VehicleCoords: ({resolvedLat},{resolvedLon})"
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

        return ApiResponse<CheckinDriverResponse>.SuccessResponse(
            response,
            $"Driver checked in successfully using vehicle GPS with geofence verification (within {maxDistance:F0}m) and proof image.");
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

    private double GetPositiveConfigurationValue(string key, double defaultValue)
    {
        var rawValue = _configuration[key];
        return double.TryParse(rawValue, out var parsedValue) && parsedValue > 0
            ? parsedValue
            : defaultValue;
    }

    private static bool HasUsableCoordinates(decimal latitude, decimal longitude)
    {
        return latitude is >= -90 and <= 90
            && longitude is >= -180 and <= 180
            && (latitude != 0 || longitude != 0);
    }

    private static bool IsFresh(DateTimeOffset timestamp, DateTimeOffset now, TimeSpan maxAge)
    {
        var age = now - timestamp.ToUniversalTime();
        return age >= TimeSpan.FromSeconds(-30) && age <= maxAge;
    }

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime timestamp)
    {
        var utcTimestamp = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utcTimestamp);
    }
}
