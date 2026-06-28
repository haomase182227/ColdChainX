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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public ColdChainMonitoringController(
        ApplicationDbContext db,
        RedisService redisService,
        IColdChainRiskService riskService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _db = db;
        _redisService = redisService;
        _riskService = riskService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
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
                Orders = orders,
                LatestTelemetry = latest,
                Eta = eta
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

    [HttpGet("trip/{tripId:guid}/chart/temperature")]
    public async Task<IActionResult> GetTripTemperatureChart(
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
                    t.TempC
                })
            }
        });
    }

    [HttpGet("trip/{tripId:guid}/chart/route")]
    public async Task<IActionResult> GetTripRouteChart(
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
                    t.Lat,
                    t.Lon
                })
            }
        });
    }

    [HttpGet("trip/{tripId:guid}/chart/route-goong")]
    public async Task<IActionResult> GetTripRouteGoongChart(
        Guid tripId,
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

        if (rawLogs.Count < 2)
            return BadRequest(new { Success = false, Error = "Not enough data points." });

        var points = rawLogs
            .Select(t => new TrackingPoint(t.Timestamp, t.Temperature, t.Latitude, t.Longitude))
            .ToList();
        
        var sampledPoints = TrackingDownsampler.Downsample(points, 23);

        var origin = sampledPoints.First();
        var destination = sampledPoints.Last();
        var waypoints = sampledPoints.Skip(1).Take(sampledPoints.Count - 2).ToList();

        var originStr = $"{origin.Lat},{origin.Lon}";
        var destStr = $"{destination.Lat},{destination.Lon}";
        var waypointsStr = string.Join("|", waypoints.Select(wp => $"{wp.Lat},{wp.Lon}"));

        var apiKey = Environment.GetEnvironmentVariable("key") ?? _configuration["GoongApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(500, new { Success = false, Error = "GoongApiKey is missing in environment variables (.env file) or configuration." });

        var url = $"https://rsapi.goong.io/Direction?origin={originStr}&destination={destStr}&waypoints={waypointsStr}&vehicle=truck&api_key={apiKey}";

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return StatusCode(500, new { Success = false, Error = "Failed to call Goong API.", Detail = errorContent });
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        
        if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            return StatusCode(500, new { Success = false, Error = "No routes found from Goong API." });

        var firstRoute = routes[0];
        var overviewPolyline = firstRoute.GetProperty("overview_polyline").GetProperty("points").GetString();
        
        double totalDistance = 0;
        double totalDuration = 0;
        if (firstRoute.TryGetProperty("legs", out var legs))
        {
            foreach (var leg in legs.EnumerateArray())
            {
                totalDistance += leg.GetProperty("distance").GetProperty("value").GetDouble();
                totalDuration += leg.GetProperty("duration").GetProperty("value").GetDouble();
            }
        }
        string distanceText = $"{(totalDistance / 1000):F1} km";
        string durationText = $"{(totalDuration / 60):F0} mins";

        return Ok(new
        {
            Success = true,
            Data = new
            {
                TripId = tripId,
                RawPointCount = points.Count,
                DistanceText = distanceText,
                DurationText = durationText,
                EncodedPolyline = overviewPolyline
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
    [HttpGet("trip/{tripId}/alerts/risk")]
    public async Task<IActionResult> GetTripRiskAlerts(Guid tripId, CancellationToken cancellationToken)
    {
        var alerts = await _db.AlertLogs
            .Where(a => a.TripId == tripId && (a.AlertType == "TEMP_HIGH" || a.AlertType == "TEMP_CRITICAL" || a.AlertType == "DOOR_OPEN"))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.AlertId,
                a.AlertType,
                Title = a.AlertType == "TEMP_CRITICAL" ? "Cảnh báo Khẩn cấp: Hỏng Hóc Hàng Hóa (Model Risk)" 
                      : a.AlertType == "TEMP_HIGH" ? "Cảnh báo Rủi ro: Lệch Nhiệt Độ (Model Risk)" 
                      : "Cảnh báo An ninh: Cửa Mở Bất Thường",
                Message = a.AlertType == "DOOR_OPEN" 
                    ? "Cửa xe tải đang bị mở khi chưa đến điểm giao hàng. Nguy cơ thoát nhiệt và mất cắp cao!"
                    : $"Nhiệt độ hiện tại đạt {a.Value}°C, vượt ngưỡng an toàn. Model Risk đánh giá lô hàng đang gặp rủi ro hư hỏng. Yêu cầu kiểm tra lốc máy lạnh ngay lập tức!",
                ActualTemperatureC = a.Value,
                a.Latitude,
                a.Longitude,
                a.Status,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { Success = true, Data = alerts });
    }

    [HttpGet("trip/{tripId}/alerts/ssa")]
    public async Task<IActionResult> GetTripSsaAlerts(Guid tripId, CancellationToken cancellationToken)
    {
        var alerts = await _db.AlertLogs
            .Where(a => a.TripId == tripId && a.AlertType == "TEMP_FORECAST_BREACH")
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.AlertId,
                a.AlertType,
                Title = "Cảnh báo Tiên tri: Tăng Nhiệt Đột Biến (Model SSA)",
                Message = $"Phân tích chuỗi thời gian (SSA) phát hiện sự phá vỡ chu kỳ lạnh. Dự báo trong 30 phút tới, nhiệt độ sẽ vọt lên mức {a.Value}°C. Khuyến cáo hạ nhiệt độ lốc máy lạnh hoặc kiểm tra khe hở của thùng xe để phòng ngừa rủi ro trước khi nó xảy ra.",
                ForecastedSpikeTemp = a.Value,
                a.Latitude,
                a.Longitude,
                a.Status,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { Success = true, Data = alerts });
    }

    [HttpGet("trip/{tripId}/alerts/smart")]
    public async Task<IActionResult> GetTripSmartAlerts(Guid tripId, CancellationToken cancellationToken)
    {
        var alerts = await _db.AlertLogs
            .Where(a => a.TripId == tripId && a.AlertType == "SMART_COLDCHAIN_RISK")
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.AlertId,
                a.AlertType,
                Title = "Phán Quyết Của Hệ Chuyên Gia (Smart Expert System)",
                Message = $"Tổng hợp tín hiệu từ cả Model Risk và Model SSA: Chuỗi cung ứng lạnh đang bị đe dọa nghiêm trọng! Điểm rủi ro tổng hợp (Smart Risk Score) đánh giá ở mức: {a.Value}. Cần sự can thiệp từ Điều Phối Viên ngay lập tức để cứu vãn chuyến hàng.",
                SmartRiskScore = a.Value,
                a.Latitude,
                a.Longitude,
                a.Status,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { Success = true, Data = alerts });
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
