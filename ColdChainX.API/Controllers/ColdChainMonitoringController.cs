using ColdChainX.API.Services;
using ColdChainX.Application.Helpers;
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
    private static readonly string[] DefaultTrackingTripStatuses =
    {
        "IN_TRANSIT",
        "DELAYED",
        "SEALED",
        "DISPATCHED"
    };

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
            return BadRequest(new { Success = false, Error = "Vui lÃ²ng cung cáº¥p VehicleId vÃ  DeviceCode." });
        }

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId, cancellationToken);
        if (vehicle == null)
        {
            return NotFound(new { Success = false, Error = "KhÃ´ng tÃ¬m tháº¥y xe." });
        }

        var device = await _db.IotDevices
            .FirstOrDefaultAsync(d => d.DeviceCode == request.DeviceCode, cancellationToken);

        if (device == null)
        {
            return NotFound(new { Success = false, Error = "Thiáº¿t bá»‹ IoT khÃ´ng tá»“n táº¡i. Vui lÃ²ng khai bÃ¡o thiáº¿t bá»‹ trÆ°á»›c." });
        }

        device.VehicleId = request.VehicleId;
        device.Status = "ASSIGNED";

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

    [HttpGet("tracking/trips")]
    public async Task<IActionResult> GetTrackingTrips(
        [FromQuery] string[]? statuses,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var safePageNumber = pageNumber < 1 ? 1 : pageNumber;
        var safePageSize = Math.Clamp(pageSize < 1 ? 50 : pageSize, 1, 200);
        var statusFilter = NormalizeTrackingStatuses(statuses);

        var query = _db.MasterTrips
            .AsNoTracking()
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Vehicle)
                .ThenInclude(v => v!.IotDevices)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
            .AsSplitQuery()
            .Where(t => t.Status != null && statusFilter.Contains(t.Status));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();
            var isGuidSearch = Guid.TryParse(keyword, out var searchedTripId);

            query = query.Where(t =>
                (isGuidSearch && t.TripId == searchedTripId)
                || (t.Vehicle != null && t.Vehicle.TruckPlate.ToLower().Contains(keyword))
                || t.TripDrivers.Any(td => td.Driver.FullName.ToLower().Contains(keyword))
                || (t.Vehicle != null && t.Vehicle.IotDevices.Any(d =>
                    d.DeviceCode != null && d.DeviceCode.ToLower().Contains(keyword)))
                || t.TransportOrders.Any(o => o.TrackingCode.ToLower().Contains(keyword)));
        }

        var totalRecords = await query.CountAsync(cancellationToken);

        var trips = await query
            .OrderByDescending(t => t.StartedAt.HasValue)
            .ThenByDescending(t => t.StartedAt)
            .ThenByDescending(t => t.PlannedStartTime)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var tripIds = trips.Select(t => t.TripId).ToArray();
        var orders = await _db.TransportOrders
            .AsNoTracking()
            .Where(o => o.MasterTripId.HasValue && tripIds.Contains(o.MasterTripId.Value))
            .Select(o => new TrackingTripOrderRow
            {
                MasterTripId = o.MasterTripId!.Value,
                OrderId = o.OrderId,
                TrackingCode = o.TrackingCode,
                ItemName = o.ItemName,
                Category = o.Category,
                TempCondition = o.TempCondition
            })
            .ToListAsync(cancellationToken);

        var ordersByTripId = orders
            .GroupBy(o => o.MasterTripId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.ToResponse()).ToList());

        var data = new List<TrackingTripListItemResponse>(trips.Count);
        foreach (var trip in trips)
        {
            var device = SelectTrackingDevice(trip.Vehicle?.IotDevices);
            var redisKey = BuildRedisKey(device);
            var latest = redisKey == null ? null : await _redisService.GetLatestAsync(redisKey);
            var eta = latest == null
                ? null
                : BuildEta(latest.Lat, latest.Lon, (double)trip.DestinationLocation.Latitude, (double)trip.DestinationLocation.Longitude);

            var drivers = trip.TripDrivers
                .OrderBy(td => td.DriverRole == "PRIMARY" ? 0 : 1)
                .ThenBy(td => td.CreatedAt)
                .Select(td => new TrackingTripDriverResponse
                {
                    DriverId = td.DriverId,
                    FullName = td.Driver.FullName,
                    DriverRole = td.DriverRole
                })
                .ToList();

            var tripOrders = ordersByTripId.TryGetValue(trip.TripId, out var foundOrders)
                ? foundOrders
                : new List<TrackingTripOrderItemResponse>();

            data.Add(new TrackingTripListItemResponse
            {
                TripId = trip.TripId,
                Status = trip.Status,
                PlannedStartTime = trip.PlannedStartTime,
                PlannedEndTime = trip.PlannedEndTime,
                StartedAt = trip.StartedAt,
                CompletedAt = trip.CompletedAt,
                SealNumber = trip.SealNumber,
                Vehicle = trip.Vehicle == null ? null : new TrackingTripVehicleResponse
                {
                    VehicleId = trip.Vehicle.VehicleId,
                    TruckPlate = trip.Vehicle.TruckPlate
                },
                Driver = drivers.Count == 0 ? null : string.Join(", ", drivers.Select(d => d.FullName)),
                Drivers = drivers,
                Device = device == null ? null : new TrackingTripDeviceResponse
                {
                    DeviceId = device.DeviceId,
                    DeviceCode = device.DeviceCode,
                    Status = device.Status,
                    IsOnline = device.IsOnline,
                    LastPingTime = device.LastPingTime
                },
                Orders = tripOrders,
                OrderCount = tripOrders.Count,
                LatestTelemetry = latest,
                Eta = eta
            });
        }

        return Ok(new
        {
            Success = true,
            PageNumber = safePageNumber,
            PageSize = safePageSize,
            TotalRecords = totalRecords,
            TotalPages = (int)Math.Ceiling(totalRecords / (double)safePageSize),
            Data = data
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

    private static string[] NormalizeTrackingStatuses(string[]? statuses)
    {
        var normalized = statuses?
            .SelectMany(s => (s ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToArray();

        return normalized is { Length: > 0 }
            ? normalized
            : DefaultTrackingTripStatuses;
    }

    private static IotDevice? SelectTrackingDevice(IEnumerable<IotDevice>? devices)
    {
        return devices?
            .OrderBy(d => string.IsNullOrWhiteSpace(d.DeviceCode) ? 1 : 0)
            .ThenByDescending(d => d.LastPingTime)
            .FirstOrDefault();
    }

    private static string? BuildRedisKey(IotDevice? device)
    {
        if (device == null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(device.DeviceCode)
            ? device.DeviceId.ToString()
            : device.DeviceCode;
    }
}

public sealed class AssignDeviceRequest
{
    public Guid VehicleId { get; set; }

    public string DeviceCode { get; set; } = string.Empty;
}

public sealed class StartTripMonitoringRequest
{
    public Guid TripId { get; set; }
}

public sealed class TrackingTripListItemResponse
{
    public Guid TripId { get; set; }

    public string? Status { get; set; }

    public DateTime PlannedStartTime { get; set; }

    public DateTime PlannedEndTime { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? SealNumber { get; set; }

    public TrackingTripVehicleResponse? Vehicle { get; set; }

    public string? Driver { get; set; }

    public List<TrackingTripDriverResponse> Drivers { get; set; } = new();

    public TrackingTripDeviceResponse? Device { get; set; }

    public List<TrackingTripOrderItemResponse> Orders { get; set; } = new();

    public int OrderCount { get; set; }

    public object? LatestTelemetry { get; set; }

    public object? Eta { get; set; }
}

public sealed class TrackingTripVehicleResponse
{
    public Guid VehicleId { get; set; }

    public string TruckPlate { get; set; } = string.Empty;
}

public sealed class TrackingTripDriverResponse
{
    public Guid DriverId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string DriverRole { get; set; } = string.Empty;
}

public sealed class TrackingTripDeviceResponse
{
    public Guid DeviceId { get; set; }

    public string? DeviceCode { get; set; }

    public string? Status { get; set; }

    public bool IsOnline { get; set; }

    public DateTime? LastPingTime { get; set; }
}

public sealed class TrackingTripOrderItemResponse
{
    public Guid OrderId { get; set; }

    public string TrackingCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string TempCondition { get; set; } = string.Empty;
}

public sealed class TrackingTripOrderRow
{
    public Guid MasterTripId { get; set; }

    public Guid OrderId { get; set; }

    public string TrackingCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string TempCondition { get; set; } = string.Empty;

    public TrackingTripOrderItemResponse ToResponse()
    {
        return new TrackingTripOrderItemResponse
        {
            OrderId = OrderId,
            TrackingCode = TrackingCode,
            ItemName = ItemName,
            Category = Category,
            TempCondition = TempCondition
        };
    }
}
