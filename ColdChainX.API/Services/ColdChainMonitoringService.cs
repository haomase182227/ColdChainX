using ColdChainX.API.Models;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace ColdChainX.API.Services;

public sealed class ColdChainMonitoringService : IColdChainMonitoringService
{
    // DELAYED (Luồng 8 — sự cố/sang xe) vẫn là chuyến đang chở hàng lạnh, telemetry phải được xử lý
    private static readonly string[] ActiveTripStatuses = { "IN_TRANSIT", "DELAYED" };
    private const double DeliveryRadiusMeters = 50;
    private const double SoftGeoFenceRadiusMeters = 1000;
    private const double HardGeoFenceRadiusMeters = 2500;
    private static readonly TimeSpan DeliveryGracePeriod = TimeSpan.FromMinutes(20);
    private static readonly ConcurrentDictionary<Guid, DoorDeliveryState> DoorDeliveryStates = new();

    private readonly ApplicationDbContext _db;
    private readonly ILocationService _locationService;
    private readonly IHubContext<MonitoringHub> _hubContext;
    private readonly IHubContext<NotificationHub> _notificationHub;
    private readonly IColdChainRiskService _riskService;
    private readonly IMqttCommandPublisher _mqttCommandPublisher;
    private readonly ILogger<ColdChainMonitoringService> _logger;

    public ColdChainMonitoringService(
        ApplicationDbContext db,
        ILocationService locationService,
        IHubContext<MonitoringHub> hubContext,
        IHubContext<NotificationHub> notificationHub,
        IColdChainRiskService riskService,
        IMqttCommandPublisher mqttCommandPublisher,
        ILogger<ColdChainMonitoringService> logger)
    {
        _db = db;
        _locationService = locationService;
        _hubContext = hubContext;
        _notificationHub = notificationHub;
        _riskService = riskService;
        _mqttCommandPublisher = mqttCommandPublisher;
        _logger = logger;
    }

    public async Task ProcessTelemetryAsync(TelemetryData data, CancellationToken cancellationToken)
    {
        await ProcessTelemetryBatchAsync(new[] { data }, cancellationToken);
    }

    public async Task ProcessTelemetryBatchAsync(IReadOnlyCollection<TelemetryData> batch, CancellationToken cancellationToken)
    {
        foreach (var data in batch)
        {
            await ProcessOneTelemetryAsync(data, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessOneTelemetryAsync(TelemetryData data, CancellationToken cancellationToken)
    {
        var device = await ResolveDeviceAsync(data.DeviceId, cancellationToken);
        if (device?.VehicleId == null)
        {
            _logger.LogDebug("Telemetry device {DeviceId} is not assigned to a vehicle.", data.DeviceId);
            return;
        }

        var timestamp = ToDbTime(data.Timestamp);
        device.LastPingTime = timestamp;
        device.Status = "ONLINE";

        var trip = await _db.MasterTrips
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.TripStops)
                .ThenInclude(s => s.Location)
            .FirstOrDefaultAsync(t =>
                t.VehicleId == device.VehicleId &&
                ActiveTripStatuses.Contains(t.Status!),
                cancellationToken);

        if (trip == null)
        {
            return;
        }

        var orders = await _db.TransportOrders
            .Where(o => o.MasterTripId == trip.TripId)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            orders = await _db.Lpns
                .Where(l => l.TripId == trip.TripId)
                .Select(l => l.Order)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        var latitude = ToDecimal(data.Lat);
        var longitude = ToDecimal(data.Lon);
        var recentSamples = await GetRecentTemperatureSamplesAsync(trip.TripId, data, cancellationToken);
        var thermalVelocity = CalculateThermalVelocity(recentSamples);
        var risk = _riskService.Evaluate(trip, orders, data.TempC, data.Timestamp);
        var recentTemperatures = recentSamples.Select(s => s.TempC).ToList();
        var forecast = _riskService.ForecastTemperature(recentTemperatures, horizon: 30);
        var geoFence = await EvaluateGeoFenceAsync(trip, data, cancellationToken);
        var atDeliveryPoint = await IsAtDeliveryPointAsync(trip, data, cancellationToken);
        var graceState = UpdateDoorDeliveryGraceState(trip.TripId, data, atDeliveryPoint);
        var expert = _riskService.AssessExpertSystem(
            risk,
            forecast,
            geoFence,
            thermalVelocity,
            data.DoorOpen,
            graceState.IsMuted);

        _db.TelemetryLogs.Add(new TelemetryLog
        {
            LogId = Guid.NewGuid(),
            DeviceId = device.DeviceId,
            TripId = trip.TripId,
            Temperature = ToDecimal(data.TempC),
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp,
            CreatedAt = DateTime.UtcNow
        });

        await BroadcastTelemetryAsync(trip.TripId, data, risk, forecast, geoFence, expert, cancellationToken);

        var alerts = BuildAlerts(trip, data, latitude, longitude, risk, forecast, geoFence, expert, atDeliveryPoint, graceState.IsMuted);
        if (alerts.Count == 0)
        {
            return;
        }

        _db.AlertLogs.AddRange(alerts);
        foreach (var alert in alerts)
        {
            await BroadcastAlertAsync(trip.TripId, alert.AlertType, BuildAlertMessage(alert, risk), data.Timestamp, cancellationToken);
        }

        if (alerts.Any(a => a.AlertType is "DOOR_OPEN" or "TEMP_HIGH" or "TEMP_CRITICAL" or "GEOFENCE_HARD" or "SMART_COLDCHAIN_RISK" or "TEMP_FORECAST_BREACH"))
        {
            var notificationData = new
            {
                TripId = trip.TripId,
                AlertTypes = alerts.Select(a => a.AlertType).ToArray(),
                Timestamp = data.Timestamp
            };

            await _notificationHub.Clients.Group("Group_Dispatcher").SendAsync("ReceiveColdChainAlert", notificationData, cancellationToken);

            var driverUserIds = trip.TripDrivers
                .Where(td => td.Driver?.UserId != null)
                .Select(td => td.Driver.UserId!.Value.ToString())
                .ToList();

            if (driverUserIds.Count > 0)
            {
                await _notificationHub.Clients.Users(driverUserIds).SendAsync("ReceiveColdChainAlert", notificationData, cancellationToken);
            }
        }
    }

    private async Task<IotDevice?> ResolveDeviceAsync(string rawDeviceId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(rawDeviceId, out var deviceGuid))
        {
            return await _db.IotDevices
                .Include(d => d.Vehicle)
                .FirstOrDefaultAsync(d => d.DeviceId == deviceGuid, cancellationToken);
        }

        return await _db.IotDevices
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.DeviceCode == rawDeviceId, cancellationToken);
    }

    private List<AlertLog> BuildAlerts(
        MasterTrip trip,
        TelemetryData data,
        decimal latitude,
        decimal longitude,
        ColdChainRiskResult risk,
        TemperatureForecastResult forecast,
        GeoFenceAssessment geoFence,
        ColdChainExpertAssessment expert,
        bool atDeliveryPoint,
        bool isGraceMuted)
    {
        var alerts = new List<AlertLog>();

        if (data.DoorOpen && !atDeliveryPoint)
        {
            alerts.Add(CreateAlert(trip.TripId, "DOOR_OPEN", null, latitude, longitude));
        }

        if (isGraceMuted)
        {
            return alerts;
        }

        if (risk.TempDeviationC > 0)
        {
            alerts.Add(CreateAlert(
                trip.TripId,
                risk.RiskScore >= 90 ? "TEMP_CRITICAL" : "TEMP_HIGH",
                risk.ActualTempC,
                latitude,
                longitude));
        }

        if (forecast.ForecastTempC.Count > 0 && forecast.MaxForecastTempC > (double)risk.RequiredMaxTempC)
        {
            alerts.Add(CreateAlert(
                trip.TripId,
                "TEMP_FORECAST_BREACH",
                (decimal)Math.Round(forecast.MaxForecastTempC, 2),
                latitude,
                longitude));
        }

        if (geoFence.IsHardViolation)
        {
            alerts.Add(CreateAlert(
                trip.TripId,
                "GEOFENCE_HARD",
                ToAlertValue(geoFence.NearestDistanceMeters),
                latitude,
                longitude));
        }
        else if (geoFence.IsSoftViolation)
        {
            alerts.Add(CreateAlert(
                trip.TripId,
                "GEOFENCE_SOFT",
                ToAlertValue(geoFence.NearestDistanceMeters),
                latitude,
                longitude));
        }

        if (expert.Severity is "CRITICAL" or "HIGH"
            && alerts.All(a => a.AlertType is not "TEMP_CRITICAL" and not "GEOFENCE_HARD"))
        {
            alerts.Add(CreateAlert(
                trip.TripId,
                "SMART_COLDCHAIN_RISK",
                expert.SmartRiskScore,
                latitude,
                longitude));
        }

        return alerts;
    }

    private static AlertLog CreateAlert(Guid tripId, string type, decimal? value, decimal latitude, decimal longitude)
    {
        return new AlertLog
        {
            AlertId = Guid.NewGuid(),
            TripId = tripId,
            AlertType = type,
            Value = value,
            Latitude = latitude,
            Longitude = longitude,
            Status = "NEW",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static decimal ToAlertValue(double value)
    {
        return (decimal)Math.Round(Math.Min(value, 999.99), 2);
    }

    private async Task<bool> IsAtDeliveryPointAsync(MasterTrip trip, TelemetryData data, CancellationToken cancellationToken)
    {
        if (data.Lat == 0 || data.Lon == 0)
        {
            return false;
        }

        var distance = HaversineMeters(
            data.Lat,
            data.Lon,
            (double)trip.DestinationLocation.Latitude,
            (double)trip.DestinationLocation.Longitude);

        if (distance > SoftGeoFenceRadiusMeters)
        {
            return false;
        }

        var roadDistance = await TryGetDrivingDistanceMetersAsync(
            data.Lat,
            data.Lon,
            (double)trip.DestinationLocation.Latitude,
            (double)trip.DestinationLocation.Longitude,
            cancellationToken);

        return (roadDistance ?? distance) <= DeliveryRadiusMeters;
    }

    private async Task BroadcastTelemetryAsync(
        Guid tripId,
        TelemetryData data,
        ColdChainRiskResult risk,
        TemperatureForecastResult forecast,
        GeoFenceAssessment geoFence,
        ColdChainExpertAssessment expert,
        CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(MonitoringHub.BuildTripGroup(tripId))
            .SendAsync("ReceiveTelemetry", new
            {
                TripId = tripId,
                data.Lat,
                data.Lon,
                data.TempC,
                data.DoorOpen,
                data.Timestamp,
                Risk = risk,
                Forecast = forecast,
                GeoFence = geoFence,
                Expert = expert
            }, cancellationToken);
    }

    private async Task BroadcastAlertAsync(
        Guid tripId,
        string alertType,
        string message,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(MonitoringHub.BuildTripGroup(tripId))
            .SendAsync("ReceiveAlert", new
            {
                TripId = tripId,
                AlertType = alertType,
                Message = message,
                Timestamp = timestamp
            }, cancellationToken);
    }

    private static string BuildAlertMessage(AlertLog alert, ColdChainRiskResult risk)
    {
        return alert.AlertType switch
        {
            "DOOR_OPEN" => "Door opened outside delivery point.",
            "TEMP_HIGH" => $"Temperature is above {risk.CargoCategory} limit: {risk.ActualTempC:0.##}C.",
            "TEMP_CRITICAL" => $"Critical temperature risk for {risk.CargoCategory}: {risk.PredictedLabel}.",
            "TEMP_FORECAST_BREACH" => "SSA forecast predicts a cold-chain temperature breach.",
            "GEOFENCE_SOFT" => "Vehicle moved outside the soft geofence layer.",
            "GEOFENCE_HARD" => "Vehicle moved outside the hard geofence layer.",
            "SMART_COLDCHAIN_RISK" => "AI expert system escalated this shipment for human review.",
            _ => alert.AlertType
        };
    }

    private async Task<IReadOnlyList<TemperatureSample>> GetRecentTemperatureSamplesAsync(
        Guid tripId,
        TelemetryData current,
        CancellationToken cancellationToken)
    {
        var history = await _db.TelemetryLogs
            .Where(t => t.TripId == tripId)
            .OrderByDescending(t => t.Timestamp)
            .Take(240)
            .Select(t => new TemperatureSample(t.Timestamp, (double)t.Temperature))
            .ToListAsync(cancellationToken);

        history.Reverse();
        history.Add(new TemperatureSample(ToDbTime(current.Timestamp), current.TempC));
        return history;
    }

    private async Task<GeoFenceAssessment> EvaluateGeoFenceAsync(
        MasterTrip trip,
        TelemetryData data,
        CancellationToken cancellationToken)
    {
        if (data.Lat == 0 || data.Lon == 0)
        {
            return new GeoFenceAssessment
            {
                Layer = "NO_GPS",
                SoftRadiusMeters = SoftGeoFenceRadiusMeters,
                HardRadiusMeters = HardGeoFenceRadiusMeters,
                NearestDistanceMeters = 0
            };
        }

        var locations = new List<(string Name, double Lat, double Lon)>
        {
            ("Origin", (double)trip.OriginLocation.Latitude, (double)trip.OriginLocation.Longitude),
            ("Destination", (double)trip.DestinationLocation.Latitude, (double)trip.DestinationLocation.Longitude)
        };

        locations.AddRange(trip.TripStops
            .Where(s => s.Location != null)
            .Select(s => ($"Stop {s.StopSequence}: {s.Location!.Address}", (double)s.Location.Latitude, (double)s.Location.Longitude)));

        var nearest = locations
            .Select(l => new
            {
                l.Name,
                l.Lat,
                l.Lon,
                DistanceMeters = HaversineMeters(data.Lat, data.Lon, l.Lat, l.Lon)
            })
            .OrderBy(l => l.DistanceMeters)
            .First();

        var distanceMeters = nearest.DistanceMeters;
        var distanceSource = "haversine";
        if (nearest.DistanceMeters <= SoftGeoFenceRadiusMeters)
        {
            var drivingDistance = await TryGetDrivingDistanceMetersAsync(
                data.Lat,
                data.Lon,
                nearest.Lat,
                nearest.Lon,
                cancellationToken);

            if (drivingDistance.HasValue)
            {
                distanceMeters = drivingDistance.Value;
                distanceSource = "goong-driving";
            }
        }

        var layer = distanceMeters <= SoftGeoFenceRadiusMeters ? "INNER"
            : distanceMeters <= HardGeoFenceRadiusMeters ? "SOFT"
            : "HARD";

        return new GeoFenceAssessment
        {
            Layer = layer,
            IsSoftViolation = distanceMeters > SoftGeoFenceRadiusMeters,
            IsHardViolation = distanceMeters > HardGeoFenceRadiusMeters,
            NearestDistanceMeters = Math.Round(distanceMeters, 2),
            SoftRadiusMeters = SoftGeoFenceRadiusMeters,
            HardRadiusMeters = HardGeoFenceRadiusMeters,
            NearestLocation = nearest.Name,
            DistanceSource = distanceSource
        };
    }

    private async Task<double?> TryGetDrivingDistanceMetersAsync(
        double originLat,
        double originLon,
        double destinationLat,
        double destinationLon,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var km = await _locationService.GetDistanceKmAsync(
                ToDecimal(originLat),
                ToDecimal(originLon),
                ToDecimal(destinationLat),
                ToDecimal(destinationLon));
            return (double)km * 1000;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Goong driving distance check failed; falling back to Haversine.");
            return null;
        }
    }

    private static double CalculateThermalVelocity(IReadOnlyList<TemperatureSample> samples)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        var previous = samples[^2];
        var current = samples[^1];
        var minutes = Math.Max(0.001, (current.Timestamp - previous.Timestamp).TotalMinutes);
        return Math.Abs(current.TempC - previous.TempC) / minutes;
    }

    private static DoorGraceState UpdateDoorDeliveryGraceState(Guid tripId, TelemetryData data, bool atDeliveryPoint)
    {
        var now = data.Timestamp == default ? DateTimeOffset.UtcNow : data.Timestamp.ToUniversalTime();
        var state = DoorDeliveryStates.AddOrUpdate(
            tripId,
            _ => new DoorDeliveryState(data.DoorOpen, atDeliveryPoint, false, null),
            (_, existing) =>
            {
                DateTimeOffset? mutedUntil = existing.MutedUntil;
                var pendingGrace = existing.PendingGraceAfterDoorClosedAtDelivery;
                var doorJustClosedAtDelivery = existing.WasDoorOpen && !data.DoorOpen && existing.WasAtDeliveryPoint;
                if (doorJustClosedAtDelivery)
                {
                    pendingGrace = true;
                }

                if (pendingGrace && !atDeliveryPoint)
                {
                    mutedUntil = now.Add(DeliveryGracePeriod);
                    pendingGrace = false;
                }

                if (mutedUntil <= now)
                {
                    mutedUntil = null;
                }

                return new DoorDeliveryState(data.DoorOpen, atDeliveryPoint, pendingGrace, mutedUntil);
            });

        return new DoorGraceState(state.MutedUntil.HasValue && state.MutedUntil > now, state.MutedUntil);
    }

    private static DateTime ToDbTime(DateTimeOffset timestamp)
    {
        var value = timestamp == default ? DateTimeOffset.UtcNow : timestamp.ToUniversalTime();
        return DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
    }

    private static decimal ToDecimal(double value)
    {
        return Math.Round((decimal)value, 7);
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6371000;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMeters * c;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    private sealed record TemperatureSample(DateTime Timestamp, double TempC);

    private sealed record DoorDeliveryState(
        bool WasDoorOpen,
        bool WasAtDeliveryPoint,
        bool PendingGraceAfterDoorClosedAtDelivery,
        DateTimeOffset? MutedUntil);

    private sealed record DoorGraceState(bool IsMuted, DateTimeOffset? MutedUntil);
}
