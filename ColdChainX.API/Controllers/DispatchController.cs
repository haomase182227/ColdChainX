using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DispatchController : ControllerBase
{
    private readonly IDispatchService _dispatchService;
    private readonly IVehicleService _vehicleService;
    private readonly IOrderService _orderService;
    private readonly ApplicationDbContext _db;
    private readonly IPdfService _pdfService;
    private readonly ILocationService _locationService;

    public DispatchController(
        IDispatchService dispatchService,
        IVehicleService vehicleService,
        IOrderService orderService,
        ApplicationDbContext db,
        IPdfService pdfService,
        ILocationService locationService)
    {
        _dispatchService = dispatchService;
        _vehicleService = vehicleService;
        _orderService = orderService;
        _db = db;
        _pdfService = pdfService;
        _locationService = locationService;
    }

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idClaim, out var userId) ? userId : Guid.Empty;
    }

    // ── Lookup endpoints (dùng để populate dropdown trong form) ───────────────

    /// <summary>
    /// [Lookup] Danh sách xe tải đang ACTIVE — dùng để chọn xe cho plan-load.
    /// </summary>
    [HttpGet("lookup/vehicles")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupVehicles()
    {
        var result = await _vehicleService.GetAllAsync();
        var items = result.Data?
            .Where(v => v.Status == "ACTIVE")
            .Select(v => new
            {
                v.VehicleId,
                Label      = $"{v.TruckPlate} — {v.VehicleType} | tải {v.MaxWeight}kg / {v.MaxCbm}m³",
                v.TruckPlate,
                v.VehicleType,
                v.MaxWeight,
                v.MaxCbm,
                v.MinTemp,
                v.MaxTemp
            })
            .ToList();
        return Ok(new { Success = true, Data = items });
    }

    /// <summary>
    /// [Lookup] Danh sách Location đang ACTIVE — dùng để chọn kho xuất phát.
    /// </summary>
    [HttpGet("lookup/locations")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupLocations()
    {
        var locations = await _db.Locations
            .Where(l => l.Status == "ACTIVE")
            .OrderBy(l => l.Address)
            .Select(l => new
            {
                l.LocationId,
                Label     = l.Address,
                l.Address,
                l.Latitude,
                l.Longitude
            })
            .ToListAsync();

        return Ok(new { Success = true, Data = locations });
    }

    /// <summary>
    /// [Lookup] Danh sách đơn hàng đang ở trạng thái IN_WAREHOUSE — dùng để chọn đơn cho plan-load.
    /// </summary>
    [HttpGet("lookup/orders-ready")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupOrdersReady()
    {
        var orders = await _db.TransportOrders
            .Where(o => o.Status == "IN_WAREHOUSE")
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.OrderId,
                Label         = $"{o.TrackingCode} — {o.ItemName} | {o.ExpectedWeightKg}kg / {o.ExpectedCbm}m³ ({o.TempCondition})",
                o.TrackingCode,
                o.ItemName,
                o.Category,
                o.TempCondition,
                o.ExpectedWeightKg,
                o.ExpectedCbm,
                o.Status,
                o.CreatedAt
            })
            .ToListAsync();

        return Ok(new { Success = true, Count = orders.Count, Data = orders });
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  API 1: MANUAL-DISPATCH — Ghép chuyến thủ công
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thủ công chọn đơn hàng IN_WAREHOUSE và gán vào một xe tải khả dụng.
    /// Hệ thống sẽ kiểm tra nhiệt độ, tải trọng, bằng lái tài xế trước khi sinh lộ trình.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("seed-orders")]
    public async Task<IActionResult> SeedOrders()
    {
        var defaultDest = _db.Locations.FirstOrDefault()?.LocationId;

        // Cập nhật các đơn hàng cũ bị thiếu DestLocation
        var oldOrders = await _db.TransportOrders.Where(o => o.DestLocation == null).ToListAsync();
        foreach(var old in oldOrders)
        {
            old.DestLocation = defaultDest;
            // Cũng fix luôn Quantity bị null/0 cho mấy cái cũ
            if (old.Quantity == 0) old.Quantity = 1;
        }

        var r = new Random().Next(1000, 9999);
        var orders = new List<TransportOrder>
        {
            new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = $"ORD-{r}-001", ItemName = "Thịt bò Úc đông lạnh", Category = "Thực phẩm", TempCondition = "FROZEN", ExpectedWeightKg = 500, Quantity = 10, Status = "IN_WAREHOUSE", PackingType = "Thùng carton", DestLocation = defaultDest, CreatedAt = DateTime.UtcNow },
            new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = $"ORD-{r}-002", ItemName = "Cá hồi Na Uy", Category = "Thủy hải sản", TempCondition = "CHILLED", ExpectedWeightKg = 300, Quantity = 5, Status = "IN_WAREHOUSE", PackingType = "Thùng xốp", DestLocation = defaultDest, CreatedAt = DateTime.UtcNow },
            new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = $"ORD-{r}-003", ItemName = "Sữa tươi TH True Milk", Category = "Sữa", TempCondition = "2 to 8", ExpectedWeightKg = 1200, Quantity = 100, Status = "IN_WAREHOUSE", PackingType = "Thùng carton", DestLocation = defaultDest, CreatedAt = DateTime.UtcNow },
            new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = $"ORD-{r}-004", ItemName = "Kem Merino", Category = "Thực phẩm", TempCondition = "-25 to -15", ExpectedWeightKg = 150, Quantity = 20, Status = "IN_WAREHOUSE", PackingType = "Thùng carton", DestLocation = defaultDest, CreatedAt = DateTime.UtcNow },
            new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = $"ORD-{r}-005", ItemName = "Vắc-xin AstraZeneca", Category = "Y tế", TempCondition = "2 to 8", ExpectedWeightKg = 50, Quantity = 2, Status = "IN_WAREHOUSE", PackingType = "Kiện y tế chuyên dụng", DestLocation = defaultDest, CreatedAt = DateTime.UtcNow },
            new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = $"ORD-{r}-006", ItemName = "Rau sạch Đà Lạt", Category = "Nông sản", TempCondition = "AMBIENT", ExpectedWeightKg = 800, Quantity = 50, Status = "IN_WAREHOUSE", PackingType = "Sọt nhựa", DestLocation = defaultDest, CreatedAt = DateTime.UtcNow }
        };

        _db.TransportOrders.AddRange(orders);
        await _db.SaveChangesAsync();
        
        return Ok(new { Success = true, Message = $"Đã cập nhật {oldOrders.Count} đơn cũ và tạo thêm {orders.Count} đơn mới." });
    }

    [HttpPost("manual-dispatch")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ManualDispatchResult), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(typeof(object), 500)]
    public async Task<IActionResult> ManualDispatch(
        [FromQuery] List<string> orderIds,
        [FromForm] ManualDispatchFormRequest form)
    {
        var rawOriginLocId = ExtractGuid(form.OriginWarehouseLocationId);
        if (!Guid.TryParse(rawOriginLocId, out var originLocId))
            return BadRequest(new { Success = false, Error = "OriginWarehouseLocationId không hợp lệ." });

        if (form.PlannedStartTime >= form.PlannedEndTime)
            return BadRequest(new { Success = false, Error = "PlannedStartTime phải nhỏ hơn PlannedEndTime." });

        var request = new ManualDispatchRequest
        {
            OrderIds = orderIds.Select(id => Guid.Parse(ExtractGuid(id))).ToList(),
            VehicleId = Guid.Parse(ExtractGuid(form.VehicleId)),
            OriginWarehouseLocationId = originLocId,
            PlannedStartTime          = form.PlannedStartTime,
            PlannedEndTime            = form.PlannedEndTime
        };

        try
        {
            var result = await _dispatchService.ManualDispatchAsync(request);

            // Sinh file PDF Lệnh điều động + Load Plan
            var goongKey = Environment.GetEnvironmentVariable("key") ?? "xV6YBygCVRIQYybUrDAfaqYuuVfO9qvQBqQSA7uK";
            var html = ManifestTemplateBuilder.BuildHtml(result, goongKey);
            var pdfUrl = await _pdfService.SaveWaybillPdfAsync(html, result.TripId.ToString());
            
            result.LifoPdfUrl = pdfUrl;

            return Ok(new { Success = true, Data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống khi manual-dispatch.", Detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 1.1: LẤY LẠI LINK SƠ ĐỒ LIFO PDF BẰNG TRIP ID
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet("trip/{tripId}/lifo-url")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetLifoUrl(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var id))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        // Vì tên file trên Cloudinary giờ đã được cố định theo dạng lifo_{tripId}.pdf
        // Ta có thể dễ dàng lấy lại link mà không cần lưu vào Database!
        var cloudName = _db.Database.GetDbConnection().ConnectionString.Contains("localhost") ? "dbt5zpage" : "dbt5zpage"; // Giả định
        // Lấy từ cấu hình nếu muốn chuẩn: 
        // Nhưng tạm hardcode theo appsettings hiện tại
        var url = $"https://res.cloudinary.com/dbt5zpage/image/upload/coldchainx/lifo_{id}.pdf";
        
        // Trả về luôn URL mà không cần kiểm tra HEAD vì Cloudinary có thể chặn HEAD request (trả về 401)
        return Ok(new { Success = true, LifoPdfUrl = url });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 1.2: LẤY LẠI BẢN ĐỒ DẪN ĐƯỜNG (GOONG) THEO TRIP ID
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet("trip/{tripId}/route")]
    [ProducesResponseType(typeof(GoongDirectionsResult), 200)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetTripRoute(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var id))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        var trip = await _db.MasterTrips
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Location)
            .FirstOrDefaultAsync(t => t.TripId == id);

        if (trip == null)
            return NotFound(new { Success = false, Error = "Không tìm thấy chuyến đi." });

        var waypoints = new List<(decimal Lat, decimal Lon, string Address)>
        {
            (trip.OriginLocation.Latitude, trip.OriginLocation.Longitude, trip.OriginLocation.Address)
        };

        foreach (var stop in trip.TripStops.OrderBy(s => s.StopSequence))
        {
            if (stop.Location != null)
                waypoints.Add((stop.Location.Latitude, stop.Location.Longitude, stop.Location.Address));
        }

        // Điểm cuối cùng (DestLocation có thể đã nằm trong TripStops, nhưng cứ thêm cho chắc nếu thiếu)
        var lastStop = waypoints.LastOrDefault();
        if (lastStop.Lat != trip.DestinationLocation.Latitude || lastStop.Lon != trip.DestinationLocation.Longitude)
        {
            waypoints.Add((trip.DestinationLocation.Latitude, trip.DestinationLocation.Longitude, trip.DestinationLocation.Address));
        }

        try
        {
            var directions = await _locationService.GetDirectionsAsync(waypoints);
            return Ok(new { Success = true, Data = directions });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi khi gọi Goong API.", Detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 2: WAREHOUSE ORDER — Lệnh bốc xếp cho kho
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tạo lệnh bốc xếp cho kho (sau khi đã dispatch). Trip chuyển sang PENDING_WH_APPROVAL.
    /// Gửi thông báo cho WarehouseMonitor để duyệt.
    /// </summary>
    [HttpPost("warehouse-order/{tripId}")]
    [ProducesResponseType(typeof(WarehouseOrderResult), 200)]
    public async Task<IActionResult> CreateWarehouseOrder(Guid tripId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _dispatchService.CreateWarehouseOrderAsync(tripId, currentUserId);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// WH Monitor duyệt lệnh bốc xếp. Trip và Orders chuyển sang LOADING.
    /// Gửi thông báo cho Loader.
    /// </summary>
    [HttpPost("warehouse-order/{tripId}/approve")]
    [ProducesResponseType(typeof(WarehouseOrderResult), 200)]
    public async Task<IActionResult> ApproveWarehouseOrder(Guid tripId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _dispatchService.ApproveWarehouseOrderAsync(tripId, currentUserId);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// WH Monitor từ chối lệnh bốc xếp. Trip chuyển sang WH_REJECTED, Orders về IN_WAREHOUSE.
    /// </summary>
    [HttpPost("warehouse-order/{tripId}/reject")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(WarehouseOrderResult), 200)]
    public async Task<IActionResult> RejectWarehouseOrder(Guid tripId, [FromForm] RejectWarehouseOrderRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { Success = false, Error = "Vui lòng nhập lý do từ chối." });

        try
        {
            var result = await _dispatchService.RejectWarehouseOrderAsync(tripId, currentUserId, request.Reason);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 3: IOT CHECK — Kiểm tra tín hiệu IoT xe
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kiểm tra trạng thái kết nối, GPS, nhiệt độ, pin của các thiết bị IoT gắn trên xe.
    /// </summary>
    [HttpGet("vehicle-iot-check/{vehicleId}")]
    [ProducesResponseType(typeof(VehicleIoTStatus), 200)]
    public async Task<IActionResult> CheckVehicleIoT(Guid vehicleId)
    {
        try
        {
            var result = await _dispatchService.CheckVehicleIoTAsync(vehicleId);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 4: SEAL & DISPATCH — Kẹp chì + kiểm tra chất hàng
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kiểm tra tất cả đơn hàng đã được xếp lên xe chưa. Nếu đủ → kẹp chì → cấp E-Waybill.
    /// Chuyển Trip sang SEALED / DISPATCHED.
    /// </summary>
    [HttpPost("seal-and-dispatch/{tripId}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SealAndDispatchResult), 200)]
    public async Task<IActionResult> SealAndDispatch(Guid tripId, [FromForm] SealAndDispatchRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SealCode))
            return BadRequest(new { Success = false, Error = "SealCode là bắt buộc." });

        try
        {
            var result = await _dispatchService.SealAndDispatchAsync(tripId, request.SealCode, currentUserId);
            return Ok(new { Success = true, Data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống khi kẹp chì.", Detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BACKLOG — Xử lý hàng tồn
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Quét các đơn hàng IN_WAREHOUSE tồn lâu hơn số ngày chỉ định, ghép vào các xe nhỏ (≤ 2000kg).
    /// </summary>
    [HttpPost("process-backlog")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(BacklogDispatchResult), 200)]
    public async Task<IActionResult> ProcessBacklog([FromForm] ProcessBacklogRequest request)
    {
        var rawOriginLocId = ExtractGuid(request.OriginWarehouseLocationId);
        if (!Guid.TryParse(rawOriginLocId, out var originLocId))
            return BadRequest(new { Success = false, Error = "OriginWarehouseLocationId không hợp lệ." });

        if (request.PlannedStartTime >= request.PlannedEndTime)
            return BadRequest(new { Success = false, Error = "PlannedStartTime phải nhỏ hơn PlannedEndTime." });

        var backlogDays = request.BacklogDays > 0 ? request.BacklogDays : 1;

        try
        {
            var result = await _dispatchService.ProcessBacklogOrdersAsync(
                originLocId, request.PlannedStartTime, request.PlannedEndTime, backlogDays);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    private static string ExtractGuid(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var parts = input.Split(new[] { ':', '|' });
        return parts[0].Trim();
    }
}
