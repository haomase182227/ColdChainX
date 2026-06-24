using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;

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
    private readonly IMediator _mediator;

    public DispatchController(
        IDispatchService dispatchService,
        IVehicleService vehicleService,
        IOrderService orderService,
        ApplicationDbContext db,
        IPdfService pdfService,
        ILocationService locationService,
        IWebHostEnvironment env,
        IFileService fileService,
        IMediator mediator)
    {
        _dispatchService = dispatchService;
        _vehicleService = vehicleService;
        _orderService = orderService;
        _db = db;
        _pdfService = pdfService;
        _locationService = locationService;
        _env = env;
        _fileService = fileService;
        _mediator = mediator;
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
    /// [Lookup] Danh sách LPN đang ở trạng thái IN_STOCK — dùng để chọn LPN cho manual-dispatch.
    /// </summary>
    [HttpGet("lookup/lpns-ready")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupLpnsReady()
    {
        var rawLpns = await (from l in _db.Lpns
                             join o in _db.TransportOrders on l.OrderId equals o.OrderId
                             join w in _db.Warehouses on l.Receipt.WarehouseId equals w.WarehouseId
                             join c in _db.Customers on o.CustomerId equals c.CustomerId into cg
                             from cust in cg.DefaultIfEmpty()
                             where l.State == LpnState.IN_STOCK
                             select new
                             {
                                 l.LpnId,
                                 l.LpnCode,
                                 l.Quantity,
                                 l.ActualWeightKg,
                                 l.ActualCbm,
                                 o.OrderId,
                                 o.TrackingCode,
                                 o.ItemName,
                                 o.TempCondition,
                                 CustomerName = cust != null ? cust.CompanyName : "N/A",
                                 WarehouseName = w.WarehouseName,
                                 l.CreatedAt
                             })
                             .OrderByDescending(x => x.CreatedAt)
                             .ToListAsync();

        var items = rawLpns.Select(x => new
        {
            x.LpnId,
            Label = $"{x.LpnCode} ({x.TrackingCode}) — {x.ItemName} | Qty: {x.Quantity} | {x.ActualWeightKg}kg / {x.ActualCbm}m³ ({x.TempCondition}) | Khách: {x.CustomerName} | Kho: {x.WarehouseName}",
            x.LpnCode,
            x.TrackingCode,
            x.ItemName,
            x.TempCondition,
            x.Quantity,
            x.ActualWeightKg,
            x.ActualCbm,
            x.OrderId,
            x.CreatedAt
        }).ToList();

        return Ok(new { Success = true, Count = items.Count, Data = items });
    }

    /// <summary>
    /// [BUOC 1/5] Ghep chuyen thu cong — chon xe, tai xe va danh sach LPN.
    /// </summary>
    /// <remarks>
    /// TRANG THAI LPN: IN_STOCK → ALLOCATED
    ///
    /// Dieu kien:
    ///   - Cac LPN duoc chon phai o trang thai IN_STOCK
    ///   - Xe phai ACTIVE va chua duoc gan chuyen nao
    ///   - Tai xe phai co bang lai con han
    ///
    /// Sau buoc nay:
    ///   - LPN.State = ALLOCATED
    ///   - Trip.Status = PLANNED
    ///   - Tra ve LifoPdfUrl (so do xep hang LIFO) va thong tin lo trinh Goong
    ///
    /// Buoc tiep theo: POST /api/Dispatch/trip/{tripId}/start-picking
    /// </remarks>
    [HttpPost("manual-dispatch")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ManualDispatchResult), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(typeof(object), 500)]
    public async Task<IActionResult> ManualDispatch(
        [FromQuery] List<string> lpnIds,
        [FromForm] ManualDispatchFormRequest form)
    {
        if (lpnIds == null || !lpnIds.Any())
            return BadRequest(new { Success = false, Error = "Vui lòng chọn ít nhất một LPN." });

        if (form.PlannedStartTime >= form.PlannedEndTime)
            return BadRequest(new { Success = false, Error = "PlannedStartTime phải nhỏ hơn PlannedEndTime." });

        var parsedLpnIds = lpnIds.Select(id => Guid.Parse(ExtractGuid(id))).ToList();

        // Tự động tìm kho xuất phát từ LPN đầu tiên
        var firstLpnId = parsedLpnIds.First();
        var firstLpn = await _db.Lpns.Include(l => l.Order).FirstOrDefaultAsync(l => l.LpnId == firstLpnId);
        if (firstLpn == null)
            return BadRequest(new { Success = false, Error = $"Không tìm thấy LPN {firstLpnId}." });

        Guid originLocId;
        if (firstLpn.Order != null && firstLpn.Order.PickupLocation.HasValue)
        {
            originLocId = firstLpn.Order.PickupLocation.Value;
        }
        else
        {
            var fallbackLocId = await _db.Locations
                .Where(l => l.Status == "ACTIVE")
                .Select(l => l.LocationId)
                .FirstOrDefaultAsync();
            if (fallbackLocId == Guid.Empty)
                return BadRequest(new { Success = false, Error = "Không tìm thấy vị trí kho xuất phát nào trong hệ thống." });
            originLocId = fallbackLocId;
        }

        var request = new ManualDispatchRequest
        {
            LpnIds = parsedLpnIds,
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
            var pdfUrl = await _pdfService.SaveLifoMapPdfAsync(html, result.TripId.ToString());

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
    //  API 2: START PICKING — Bắt đầu lấy hàng từ kho
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [STEP 2/5 - LOOKUP] Danh sách chuyến ĐÃ GHÉP và sẵn sàng bốc hàng (Status = PLANNED).
    /// </summary>
    /// <remarks>
    /// Dùng để FE biết nên nhập tripId nào vào POST /api/Dispatch/trip/{tripId}/start-picking.
    /// Chỉ trả về các chuyến đã ghép (PLANNED) — tức đã qua bước manual-dispatch.
    /// </remarks>
    [HttpGet("trips/can-start-picking")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> GetTripsCanStartPicking()
    {
        var trips = await _db.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Where(t => t.Status == "PLANNED")
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.TripId,
                t.Status,
                Vehicle = t.Vehicle != null ? t.Vehicle.TruckPlate : "N/A",
                Driver  = t.Driver != null ? t.Driver.FullName : "N/A",
                t.PlannedStartTime,
                t.PlannedEndTime,
                TotalLpns     = _db.Lpns.Count(l => l.TripId == t.TripId),
                AllocatedLpns = _db.Lpns.Count(l => l.TripId == t.TripId && l.State == LpnState.ALLOCATED),
                Label = $"{t.TripId} | Xe {(t.Vehicle != null ? t.Vehicle.TruckPlate : "N/A")} | {_db.Lpns.Count(l => l.TripId == t.TripId)} LPN"
            })
            .ToListAsync();

        return Ok(new { Success = true, Count = trips.Count, Data = trips });
    }

    /// <summary>
    /// [STEP 2/5] Bat dau lenh boc hang — chuyen LPN tu ALLOCATED sang LOADING.
    /// </summary>
    /// <remarks>
    /// LPN state: ALLOCATED → LOADING
    /// Trip status: PLANNED → PICKING
    ///
    /// Precondition : Trip.Status == PLANNED
    /// Postcondition:
    ///   - Tat ca LPN cua chuyen: State = LOADING
    ///   - Trip.Status = PICKING
    ///
    /// Next step: goi POST /api/Outbound/pick cho tung LPN
    /// </remarks>
    /// <param name="tripId">ID chuyen hang (tu ket qua manual-dispatch)</param>
    [HttpPost("trip/{tripId}/start-picking")]
    [ProducesResponseType(typeof(StartPickingResult), 200)]
    public async Task<IActionResult> StartPicking(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        try
        {
            var result = await _dispatchService.StartPickingAsync(parsedTripId);
            return Ok(new { Success = true, Data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống khi bắt đầu picking.", Detail = ex.Message });
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
    /// [STEP 5/5 - LOOKUP] Danh sách chuyến đã xếp xong và ĐỦ ĐIỀU KIỆN kẹp chì.
    /// </summary>
    /// <remarks>
    /// Trả về các chuyến có Status = LOADING_COMPLETED và TẤT CẢ LPN đã ở trạng thái RELEASED.
    /// Dùng để FE biết nên nhập tripId nào vào POST /api/Dispatch/seal-and-dispatch/{tripId}.
    /// </remarks>
    [HttpGet("trips/ready-to-seal")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> GetTripsReadyToSeal()
    {
        var candidates = await _db.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Where(t => t.Status == "LOADING_COMPLETED")
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.TripId,
                t.Status,
                Vehicle = t.Vehicle != null ? t.Vehicle.TruckPlate : "N/A",
                Driver  = t.Driver != null ? t.Driver.FullName : "N/A",
                t.PlannedStartTime,
                t.PlannedEndTime,
                t.SealNumber,
                TotalLpns    = _db.Lpns.Count(l => l.TripId == t.TripId),
                ReleasedLpns = _db.Lpns.Count(l => l.TripId == t.TripId && l.State == LpnState.RELEASED)
            })
            .ToListAsync();

        // Chỉ giữ các chuyến đã đầy đủ LPN ở trạng thái RELEASED và chưa kẹp chì
        var ready = candidates
            .Where(t => t.TotalLpns > 0
                     && t.ReleasedLpns == t.TotalLpns
                     && string.IsNullOrEmpty(t.SealNumber))
            .Select(t => new
            {
                t.TripId,
                t.Status,
                t.Vehicle,
                t.Driver,
                t.PlannedStartTime,
                t.PlannedEndTime,
                t.TotalLpns,
                t.ReleasedLpns,
                Label = $"{t.TripId} | Xe {t.Vehicle} | {t.ReleasedLpns}/{t.TotalLpns} LPN RELEASED"
            })
            .ToList();

        return Ok(new { Success = true, Count = ready.Count, Data = ready });
    }

    /// <summary>
    /// [STEP 5/5] Kep chi + cap giay di duong (E-Waybill).
    /// </summary>
    /// <remarks>
    /// LPN state: RELEASED → SHIPPING
    /// Trip status: LOADING_COMPLETED → SEALED → DISPATCHED
    ///
    /// Precondition:
    ///   - Trip.Status == LOADING_COMPLETED  (da goi load-trip truoc)
    ///   - Tat ca LPN cua chuyen: State == RELEASED
    ///   - SealCode la bat buoc
    ///
    /// Postcondition:
    ///   - LPN.State = SHIPPING
    ///   - Trip.Status = SEALED (hoac DISPATCHED neu sinh duoc E-Waybill)
    ///   - Tao Seal record + OutboundOrder + TransportDocument (E-Waybill PDF)
    ///   - Cap nhat Vehicle.Status = OnTrip, Driver.Status = OnTrip
    /// </remarks>
    /// <param name="tripId">ID chuyen hang</param>
    /// <param name="request">SealCode bat buoc</param>
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

    private static string ExtractGuid(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var parts = input.Split(new[] { ':', '|' });
        return parts[0].Trim();
    }

    [HttpGet("trips/{id}/export-pdf")]
    public async Task<IActionResult> GetTripExportPdf(Guid id)
    {
        try
        {
            var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GenerateDispatchPdfQuery(id));
            return File(pdfBytes, "application/pdf", $"PhieuXuatKho_{id.ToString().Substring(0, 8)}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}
