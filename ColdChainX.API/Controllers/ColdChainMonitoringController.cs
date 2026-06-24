using ColdChainX.API.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.API.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class ColdChainMonitoringController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly RedisService _redisService;
    private readonly IColdChainRiskService _riskService;

    public ColdChainMonitoringController(
        ApplicationDbContext db,
        RedisService redisService,
        IColdChainRiskService riskService)
    {
        _db = db;
        _redisService = redisService;
        _riskService = riskService;
    }

    [HttpPost("fleet/assign-device")]
    public async Task<IActionResult> AssignDevice([FromBody] AssignDeviceRequest request, CancellationToken cancellationToken)
    {
        if (request.VehicleId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceCode))
        {
            return BadRequest(new { Success = false, Error = "VehicleId and DeviceCode are required." });
        }

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId, cancellationToken);
        if (vehicle == null)
        {
            return NotFound(new { Success = false, Error = "Vehicle not found." });
        }

        var device = await _db.IotDevices
            .FirstOrDefaultAsync(d => d.DeviceCode == request.DeviceCode, cancellationToken);

        if (device == null)
        {
            device = new IotDevice
            {
                DeviceId = Guid.NewGuid(),
                DeviceCode = request.DeviceCode.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _db.IotDevices.Add(device);
        }

        device.VehicleId = request.VehicleId;
        device.Status = "ASSIGNED";
        device.BatteryLevel = request.BatteryLevel ?? device.BatteryLevel;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            Success = true,
            Data = new
            {
                device.DeviceId,
                device.DeviceCode,
                device.VehicleId,
                device.Status,
                vehicle.TruckPlate
            }
        });
    }

    [HttpPost("trip/start")]
    public async Task<IActionResult> StartTripMonitoring([FromBody] StartTripMonitoringRequest request, CancellationToken cancellationToken)
    {
        if (request.TripId == Guid.Empty)
        {
            return BadRequest(new { Success = false, Error = "TripId is required." });
        }

        var trip = await _db.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);

        if (trip == null)
        {
            return NotFound(new { Success = false, Error = "Trip not found." });
        }

        if (!string.Equals(trip.Status, "DISPATCHED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trip.Status, "SEALED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trip.Status, "IN_TRANSIT", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                Success = false,
                Error = "Trip must be SEALED or DISPATCHED before cold-chain monitoring can start.",
                trip.Status
            });
        }

        trip.Status = "IN_TRANSIT";
        trip.StartedAt ??= DateTime.UtcNow;

        if (trip.Vehicle != null)
        {
            trip.Vehicle.Status = "OnTrip";
        }

        if (trip.Driver != null)
        {
            trip.Driver.Status = "OnTrip";
        }

        var orders = await _db.TransportOrders
            .Where(o => o.MasterTripId == trip.TripId)
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.Status = "IN_TRANSIT";
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            Success = true,
            Data = new
            {
                trip.TripId,
                trip.Status,
                trip.StartedAt,
                OrderCount = orders.Count
            }
        });
    }

    [HttpGet("tracking/{tripId:guid}")]
    public async Task<IActionResult> GetTracking(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await _db.MasterTrips
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Vehicle)
                .ThenInclude(v => v!.IotDevices)
            .FirstOrDefaultAsync(t => t.TripId == tripId, cancellationToken);

        if (trip == null)
        {
            return NotFound(new { Success = false, Error = "Trip not found." });
        }

        var orders = await _db.TransportOrders
            .Where(o => o.MasterTripId == tripId)
            .Select(o => new
            {
                o.OrderId,
                o.TrackingCode,
                o.ItemName,
                o.Category,
                o.TempCondition
            })
            .ToListAsync(cancellationToken);

        var device = trip.Vehicle?.IotDevices.FirstOrDefault();
        var redisKey = string.IsNullOrWhiteSpace(device?.DeviceCode)
            ? device?.DeviceId.ToString()
            : device.DeviceCode;
        var latest = redisKey == null ? null : await _redisService.GetLatestAsync(redisKey);

        var risk = latest == null
            ? null
            : _riskService.Evaluate(
                trip,
                await _db.TransportOrders.Where(o => o.MasterTripId == tripId).ToListAsync(cancellationToken),
                latest.TempC,
                latest.Timestamp);

        var activeAlerts = await _db.AlertLogs
            .Where(a => a.TripId == tripId && a.Status == "NEW")
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.AlertId,
                a.AlertType,
                a.Value,
                a.Latitude,
                a.Longitude,
                a.CreatedAt
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        var eta = latest == null
            ? null
            : BuildEta(latest.Lat, latest.Lon, (double)trip.DestinationLocation.Latitude, (double)trip.DestinationLocation.Longitude);

        return Ok(new
        {
            Success = true,
            Data = new
            {
                trip.TripId,
                trip.Status,
                Vehicle = trip.Vehicle == null ? null : new
                {
                    trip.Vehicle.VehicleId,
                    trip.Vehicle.TruckPlate
                },
                Device = device == null ? null : new
                {
                    device.DeviceId,
                    device.DeviceCode,
                    device.Status,
                    device.LastPingTime
                },
                Origin = new
                {
                    trip.OriginLocation.LocationId,
                    trip.OriginLocation.Address,
                    Lat = trip.OriginLocation.Latitude,
                    Lon = trip.OriginLocation.Longitude
                },
                Destination = new
                {
                    trip.DestinationLocation.LocationId,
                    trip.DestinationLocation.Address,
                    Lat = trip.DestinationLocation.Latitude,
                    Lon = trip.DestinationLocation.Longitude
                },
                Orders = orders,
                LatestTelemetry = latest,
                Risk = risk,
                Eta = eta,
                ActiveAlerts = activeAlerts
            }
        });
    }

    [HttpGet("trip/{tripId:guid}/chart")]
    public async Task<IActionResult> GetTripChart(
        Guid tripId,
        [FromQuery] int maxPoints = 200,
        CancellationToken cancellationToken = default)
    {
        var rawLogs = await _db.TelemetryLogs
            .Where(t => t.TripId == tripId)
            .OrderByDescending(t => t.Timestamp)
            .Take(2000)
            .OrderBy(t => t.Timestamp)
            .Select(t => new
            {
                t.Timestamp,
                t.Temperature,
                t.Latitude,
                t.Longitude
            })
            .ToListAsync(cancellationToken);

        var points = rawLogs
            .Select(t => new TrackingPoint(t.Timestamp, t.Temperature, t.Latitude, t.Longitude))
            .ToList();
        var sampledPoints = TrackingDownsampler.Downsample(points, Math.Clamp(maxPoints, 20, 1000));

        var alerts = await _db.AlertLogs
            .Where(a => a.TripId == tripId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.CreatedAt,
                a.AlertType,
                a.Value,
                a.Latitude,
                a.Longitude,
                a.Status
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            Success = true,
            Data = new
            {
                TripId = tripId,
                RawPointCount = points.Count,
                SampledPointCount = sampledPoints.Count,
                Points = sampledPoints.Select(t => new
                {
                    t.Timestamp,
                    t.TempC,
                    t.Lat,
                    t.Lon
                }),
                Alerts = alerts
            }
        });
    }

    [HttpPost("ml/coldchain-ssa/train")]
    public async Task<IActionResult> TrainColdChainSsaModel(
        [FromQuery] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _riskService.TrainSsaModelAsync(overwrite, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(500, new
            {
                result.Success,
                result.Message,
                result.DataPath,
                result.ModelPath
            });
        }

        return Ok(new
        {
            result.Success,
            result.Message,
            result.DataPath,
            result.ModelPath,
            result.WasTrained,
            result.RowCount,
            result.WindowSize,
            result.SeriesLength,
            result.Horizon
        });
    }

    [HttpPost("ml/coldchain-risk/train")]
    public async Task<IActionResult> TrainColdChainRiskModel(
        [FromQuery] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _riskService.TrainModelAsync(overwrite, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(500, new
            {
                result.Success,
                result.Message,
                result.DataPath,
                result.ModelPath
            });
        }

        return Ok(new
        {
            result.Success,
            result.Message,
            result.DataPath,
            result.ModelPath,
            result.WasTrained,
            result.MicroAccuracy,
            result.MacroAccuracy,
            result.LogLoss
        });
    }

    private static object BuildEta(double currentLat, double currentLon, double destinationLat, double destinationLon)
    {
        var remainingKm = HaversineKm(currentLat, currentLon, destinationLat, destinationLon);
        var durationMinutes = (int)Math.Ceiling(remainingKm / 40d * 60d);
        return new
        {
            RemainingDistanceKm = Math.Round(remainingKm, 2),
            EstimatedDurationMinutes = durationMinutes,
            EstimatedArrival = DateTimeOffset.UtcNow.AddMinutes(durationMinutes)
        };
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusKm * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}

public sealed class AssignDeviceRequest
{
    public Guid VehicleId { get; set; }

    public string DeviceCode { get; set; } = string.Empty;

    public int? BatteryLevel { get; set; }
}

public sealed class StartTripMonitoringRequest
{
    public Guid TripId { get; set; }
}
