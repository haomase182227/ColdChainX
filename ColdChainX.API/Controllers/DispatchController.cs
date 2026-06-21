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
using Microsoft.AspNetCore.Hosting;
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
    private readonly IWebHostEnvironment _env;
    private readonly IFileService _fileService;

    public DispatchController(
        IDispatchService dispatchService,
        IVehicleService vehicleService,
        IOrderService orderService,
        ApplicationDbContext db,
        IPdfService pdfService,
        ILocationService locationService,
        IWebHostEnvironment env,
        IFileService fileService)
    {
        _dispatchService = dispatchService;
        _vehicleService = vehicleService;
        _orderService = orderService;
        _db = db;
        _pdfService = pdfService;
        _locationService = locationService;
        _env = env;
        _fileService = fileService;
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
var rawOrders = await (from r in _db.WarehouseReceipts
                               join o in _db.TransportOrders on r.OrderId equals o.OrderId
                               join w in _db.Warehouses on r.WarehouseId equals w.WarehouseId
                               join c in _db.Customers on o.CustomerId equals c.CustomerId into cg
                               from cust in cg.DefaultIfEmpty()
                               select new
                               {
                                   o.OrderId,
                                   o.TrackingCode,
                                   o.ItemName,
                                   o.CustomerId,
                                   o.Category,
                                   o.TempCondition,
                                   Weight = o.ActualWeightKg > 0 ? o.ActualWeightKg : o.ExpectedWeightKg,
                                   Cbm = o.ActualCbm > 0 ? o.ActualCbm : o.ExpectedCbm,
                                   CustomerName = cust != null ? cust.CompanyName : "N/A",
                                   WarehouseName = w.WarehouseName,
                                   o.CreatedAt
                               })
                               .OrderByDescending(x => x.CreatedAt)
                               .ToListAsync();

        var orders = rawOrders.Select(x => {
            var locCode = "RCV-STAGE-01";
            return new
            {
                x.OrderId,
                Label = $"{x.TrackingCode} — {x.ItemName} | {x.Weight}kg / {x.Cbm}m³ ({x.TempCondition}) | Khách: {x.CustomerName} | Kho: {x.WarehouseName} (Vị trí: {locCode})",
                x.TrackingCode,
                x.ItemName,
                x.Category,
                x.TempCondition,
                ExpectedWeightKg = x.Weight,
                ExpectedCbm = x.Cbm,
                Status = "IN_WAREHOUSE",
                x.CreatedAt
            };
        }).ToList();

        return Ok(new { Success = true, Count = orders.Count, Data = orders });
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
        if (orderIds == null || !orderIds.Any())
            return BadRequest(new { Success = false, Error = "Vui lòng chọn ít nhất một đơn hàng." });

        if (form.PlannedStartTime >= form.PlannedEndTime)
            return BadRequest(new { Success = false, Error = "PlannedStartTime phải nhỏ hơn PlannedEndTime." });

        var parsedOrderIds = orderIds.Select(id => Guid.Parse(ExtractGuid(id))).ToList();

        // Tự động tìm kho xuất phát từ đơn hàng đầu tiên
        var firstOrderId = parsedOrderIds.First();
        var firstOrder = await _db.TransportOrders.FindAsync(firstOrderId);
        if (firstOrder == null)
            return BadRequest(new { Success = false, Error = $"Không tìm thấy đơn hàng {firstOrderId}." });

        if (!firstOrder.PickupLocation.HasValue)
            return BadRequest(new { Success = false, Error = $"Đơn hàng {firstOrder.TrackingCode} chưa được nhập kho (thiếu vị trí kho)." });

        var originLocId = firstOrder.PickupLocation.Value;

        var request = new ManualDispatchRequest
        {
            OrderIds = parsedOrderIds,
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

        // Trả về trực tiếp link Cloudinary có chữ ký (Signed URL) để bypass lỗi 401
        var url = _fileService.GetSignedUrl($"coldchainx/lifo_{id}");
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
    public async Task<IActionResult> CreateWarehouseOrder(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _dispatchService.CreateWarehouseOrderAsync(parsedTripId, currentUserId);
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
    public async Task<IActionResult> ApproveWarehouseOrder(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _dispatchService.ApproveWarehouseOrderAsync(parsedTripId, currentUserId);
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
    public async Task<IActionResult> RejectWarehouseOrder(string tripId, [FromForm] RejectWarehouseOrderRequest request)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { Success = false, Error = "Vui lòng nhập lý do từ chối." });

        try
        {
            var result = await _dispatchService.RejectWarehouseOrderAsync(parsedTripId, currentUserId, request.Reason);
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
    public async Task<IActionResult> CheckVehicleIoT(string vehicleId)
    {
        var rawId = ExtractGuid(vehicleId);
        if (!Guid.TryParse(rawId, out var parsedVehicleId))
            return BadRequest(new { Success = false, Error = "VehicleId không hợp lệ." });

        try
        {
            var result = await _dispatchService.CheckVehicleIoTAsync(parsedVehicleId);
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
    public async Task<IActionResult> SealAndDispatch(string tripId, [FromForm] SealAndDispatchRequest request)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SealCode))
            return BadRequest(new { Success = false, Error = "SealCode là bắt buộc." });

        try
        {
            var result = await _dispatchService.SealAndDispatchAsync(parsedTripId, request.SealCode, currentUserId);
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

