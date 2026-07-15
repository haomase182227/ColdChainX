using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using System.Text;
using System.Text.Json;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Shared.Responses;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
    private readonly IGoongMapService _goongMapService;
    private readonly IWebHostEnvironment _env;
    private readonly IFileService _fileService;
    private readonly IMediator _mediator;
    private readonly IDriverAvailabilityService _driverAvailability;

    public DispatchController(
        IDispatchService dispatchService,
        IVehicleService vehicleService,
        IOrderService orderService,
        ApplicationDbContext db,
        IPdfService pdfService,
        ILocationService locationService,
        IGoongMapService goongMapService,
        IWebHostEnvironment env,
        IFileService fileService,
        IMediator mediator,
        IDriverAvailabilityService driverAvailability)
    {
        _dispatchService = dispatchService;
        _vehicleService = vehicleService;
        _orderService = orderService;
        _db = db;
        _pdfService = pdfService;
        _locationService = locationService;
        _goongMapService = goongMapService;
        _env = env;
        _fileService = fileService;
        _mediator = mediator;
        _driverAvailability = driverAvailability;
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
                v.InnerLengthCm,
                v.InnerWidthCm,
                v.InnerHeightCm,
                UsableCbm = v.InnerLengthCm.HasValue && v.InnerWidthCm.HasValue && v.InnerHeightCm.HasValue
                    && v.InnerLengthCm.Value > 0 && v.InnerWidthCm.Value > 0 && v.InnerHeightCm.Value > 0
                        ? Math.Min(
                            v.MaxCbm,
                            v.InnerLengthCm.Value * v.InnerWidthCm.Value * v.InnerHeightCm.Value / 1_000_000m) * 0.8m
                        : (decimal?)null,
                v.MinTemp,
                v.MaxTemp
            })
            .ToList();
        return Ok(ApiResponse<object>.SuccessResponse(items, "Lấy danh sách xe tải thành công."));
    }

    /// <summary>
    /// [Lookup] Danh sách tài xế khả dụng — dùng để chọn 1–2 tài xế cho manual-dispatch.
    /// </summary>
    /// <remarks>
    /// Tự động gỡ trạng thái RELAX nếu đã qua ngày/tuần giới hạn, sau đó chỉ trả về
    /// các tài xế có thể gán chuyến (không RELAX/Offline/Inactive và còn bằng lái hạn).
    /// </remarks>
    [HttpGet("lookup/drivers")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupDrivers()
    {
        var candidates = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Where(d => d.Status != "Offline" && d.Status != "Inactive" && d.Status != "DELETED")
            .ToListAsync();

        // Tự động gỡ RELAX đã hết hạn (cập nhật Status tại chỗ)
        foreach (var d in candidates)
            await _driverAvailability.ReconcileStatusAsync(d);
        await _db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = candidates
            .Where(d => d.Status != "RELAX")
            .Select(d =>
            {
                var lic = d.DriverLicenses
                    .Where(l => l.ExpiryDate >= today && (l.Status == null || l.Status == "ACTIVE"))
                    .OrderByDescending(l => l.ExpiryDate)
                    .FirstOrDefault();
                return new
                {
                    d.DriverId,
                    d.FullName,
                    d.PhoneNumber,
                    d.Status,
                    LicenseClass = lic?.LicenseClass,
                    LicenseExpiry = lic?.ExpiryDate,
                    HasValidLicense = lic != null,
                    Label = $"{d.FullName} — {d.PhoneNumber}" + (lic != null ? $" | Bằng {lic.LicenseClass}" : " | ⚠ Hết hạn bằng lái")
                };
            })
            .Where(x => x.HasValidLicense)
            .OrderBy(x => x.FullName)
            .ToList();

        return Ok(ApiResponse<object>.SuccessResponse(items, "Lấy danh sách tài xế thành công."));
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

        return Ok(ApiResponse<object>.SuccessResponse(locations, "Lấy danh sách kho/điểm đến thành công."));
    }

    /// <summary>
    /// [Lookup] Danh sách kho hiện có — dùng để chọn kho trước khi ghép chuyến (manual-dispatch).
    /// </summary>
    /// <summary>
    /// [Lookup] Danh sach lich chay dang ACTIVE tu bang route_schedules.
    /// </summary>
    [HttpGet("lookup/Schedule")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupSchedules()
    {
        var items = await _db.RouteSchedules
            .AsNoTracking()
            .Where(s => s.Status == "ACTIVE" && s.Route.Status == "ACTIVE")
            .OrderBy(s => s.Route.RouteCode)
            .ThenBy(s => s.DepartureDate)
            .ThenBy(s => s.DepartureTime)
            .Select(s => new
            {
                s.ScheduleId,
                s.RouteId,
                s.Route.RouteCode,
                RouteName = s.Route.OriginCity + " -> " + s.Route.DestCity,
                s.ScheduleName,
                DayOfWeek = (int)s.DepartureDate.DayOfWeek,
                s.DepartureTime,
                s.CutOffTime,
                s.Status,
                Label = s.ScheduleName + " - " + s.Route.RouteCode
                    + " | " + s.Route.OriginCity + " -> " + s.Route.DestCity
                    + " | " + s.DepartureTime
            })
            .ToListAsync();

        return Ok(new { Success = true, Count = items.Count, Data = items });
    }

    [HttpGet("lookup/warehouses")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupWarehouses()
    {
        var items = await _db.Warehouses
            .OrderBy(w => w.WarehouseName)
            .Select(w => new
            {
                w.WarehouseId,
                w.WarehouseCode,
                w.WarehouseName,
                w.Address,
                Label = $"{w.WarehouseName} ({w.WarehouseCode})"
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(items, "Lấy danh sách kho xuất phát thành công."));
    }

    /// <summary>
    /// [Lookup] Danh sách LPN đang ở trạng thái IN_STOCK — dùng để chọn LPN cho manual-dispatch.
    /// </summary>
    /// <remarks>
    /// Truyền <paramref name="warehouseId"/> để chỉ lấy các LPN thuộc kho đã chọn
    /// (Lpn.WarehouseId == warehouseId). Đây là bước bắt buộc của luồng manual-dispatch:
    /// người dùng chọn kho trước, sau đó chỉ thấy LPN của kho đó.
    ///
    /// Mỗi phần tử Data trả về: lpnId, lpnCode, orderId, trackingCode, itemName, customerName,
    /// warehouseId, warehouseName, destinationAddress, routeName, plannedDispatchDate, quantity,
    /// actualWeightKg, actualCbm, tempCondition.
    ///
    /// Có phân trang qua <paramref name="pageNumber"/> / <paramref name="pageSize"/> (mặc định 1/10).
    /// </remarks>
    [HttpGet("lookup/lpns-ready")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupLpnsReady(
        [FromQuery] Guid? warehouseId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10)
    {
        var safePageNumber = pageNumber < 1 ? 1 : pageNumber;
        var safePageSize = pageSize < 1 ? 10 : pageSize;

        var query = from l in _db.Lpns
                    join o in _db.TransportOrders on l.OrderId equals o.OrderId
                    join w in _db.Warehouses on l.WarehouseId equals w.WarehouseId
                    join c in _db.Customers on o.CustomerId equals c.CustomerId into cg
                    from cust in cg.DefaultIfEmpty()
                    join dl in _db.Locations on o.DestLocation equals dl.LocationId into dlg
                    from destLoc in dlg.DefaultIfEmpty()
                    join s in _db.RouteSchedules on o.ScheduleId equals s.ScheduleId into sg
                    from schedule in sg.DefaultIfEmpty()
                    join r in _db.RouteMasters on schedule.RouteId equals r.RouteId into rg
                    from route in rg.DefaultIfEmpty()
                    where l.State == LpnState.IN_STOCK
                       && (warehouseId == null || l.WarehouseId == warehouseId)
                    select new
                    {
                        l.LpnId,
                        l.LpnCode,
                        l.Quantity,
                        l.ActualWeightKg,
                        l.ActualCbm,
                        l.SlaDeadline,
                        o.OrderId,
                        o.TrackingCode,
                        o.ItemName,
                        o.TempCondition,
                        CustomerName = cust != null ? cust.CompanyName : "N/A",
                        WarehouseId = w.WarehouseId,
                        WarehouseName = w.WarehouseName,
                        DestinationAddress = destLoc != null ? destLoc.Address : null,
                        RouteOriginCity = route != null ? route.OriginCity : null,
                        RouteDestCity = route != null ? route.DestCity : null,
                        l.CreatedAt
                    };

        var totalRecords = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalRecords / (double)safePageSize);

        var rawLpns = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync();

        var items = rawLpns.Select(x => new
        {
            x.LpnId,
            Label = $"{x.LpnCode} ({x.TrackingCode}) — {x.ItemName} | Qty: {x.Quantity} | {x.ActualWeightKg}kg / {x.ActualCbm}m³ ({x.TempCondition}) | Khách: {x.CustomerName} | Kho: {x.WarehouseName}",
            x.LpnCode,
            x.OrderId,
            x.TrackingCode,
            x.ItemName,
            x.CustomerName,
            x.WarehouseId,
            x.WarehouseName,
            x.DestinationAddress,
            RouteName = x.RouteOriginCity != null ? $"{x.RouteOriginCity} → {x.RouteDestCity}" : null,
            PlannedDispatchDate = x.SlaDeadline,
            x.Quantity,
            x.ActualWeightKg,
            x.ActualCbm,
            x.TempCondition,
            x.CreatedAt
        }).ToList();

        var pagedResult = PagedResult<object>.Create(items, totalRecords, safePageNumber, safePageSize);

        return Ok(ApiResponse<PagedResult<object>>.SuccessResponse(pagedResult, "Lấy danh sách LPN thành công."));
    }

    [HttpPost("preview-load-plan")]
    public async Task<IActionResult> PreviewLoadPlan([FromBody] PreviewLoadPlanRequest request)
    {
        var vehicle = await _db.Vehicles.FindAsync(request.VehicleId);
        if (vehicle == null) return NotFound(ApiResponse<object>.Failure("Vehicle not found"));

        decimal vLength = vehicle.InnerLengthCm ?? (vehicle.VehicleType == "TRUCK_1T" ? 300m : 200m);
        decimal vWidth = vehicle.InnerWidthCm ?? (vehicle.VehicleType == "TRUCK_1T" ? 180m : 140m);
        decimal vHeight = vehicle.InnerHeightCm ?? (vehicle.VehicleType == "TRUCK_1T" ? 190m : 140m);

        var lpns = await _db.Lpns
            .Include(l => l.Order)
            .Where(l => request.LpnIds.Contains(l.LpnId))
            .ToListAsync();

        var engineItems = new List<ColdChainX.Application.Services.LpnDims>();
        
        // LIFO logic: rank by SlaDeadline. Latest deadline = delivered last = packed first (highest sequence)
        var orderedLpns = lpns.OrderByDescending(l => l.SlaDeadline).ToList();
        for (int i = 0; i < orderedLpns.Count; i++)
        {
            var lpn = orderedLpns[i];
            engineItems.Add(new ColdChainX.Application.Services.LpnDims
            {
                LpnId = lpn.LpnId,
                Length = lpn.LengthCm ?? 120m,
                Width = lpn.WidthCm ?? 100m,
                Height = lpn.HeightCm ?? 150m,
                RouteStopSequence = orderedLpns.Count - i
            });
        }

        var engine = new ColdChainX.Application.Services.CargoPackingEngine();
        var packingResult = engine.Pack(
            new ColdChainX.Application.Services.ContainerDims { Length = vLength, Width = vWidth, Height = vHeight }, 
            engineItems);

        var colors = new[] { "#ff9999", "#99ff99", "#9999ff", "#ffff99", "#ff99ff", "#99ffff" };

        var response = new PreviewLoadPlanResponse
        {
            VehicleType = vehicle.VehicleType,
            ContainerLength = vLength,
            ContainerWidth = vWidth,
            ContainerHeight = vHeight,
            Utilisation = packingResult.Utilisation,
            UnplacedLpnIds = packingResult.UnplacedLpnIds,
            PlacedItems = packingResult.PlacedItems.Select((pi, idx) => {
                var lpn = lpns.First(l => l.LpnId == pi.LpnId);
                return new PreviewPlacedItem
                {
                    LpnId = pi.LpnId,
                    LpnCode = lpn.LpnCode,
                    X = pi.X,
                    Y = pi.Y,
                    Z = pi.Z,
                    W = pi.W,
                    H = pi.H,
                    D = pi.D,
                    Color = colors[idx % colors.Length]
                };
            }).ToList()
        };

        return Ok(ApiResponse<PreviewLoadPlanResponse>.SuccessResponse(response, "Preview load plan successful."));
    }

    /// <summary>
    /// [BUOC 1/5] Ghep chuyen thu cong — chon xe, tai xe va danh sach LPN.
    /// </summary>
    /// <remarks>
    /// TRANG THAI LPN: IN_STOCK → ALLOCATED
    ///
    /// Dieu kien:
    ///   - Phai chon Kho (WarehouseId) truoc; chi cac LPN thuoc kho do moi duoc ghep chuyen
    ///   - Tat ca LPN phai cung mot kho — khong duoc tron LPN tu nhieu kho khac nhau
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
            return BadRequest(ApiResponse<object>.Failure("Vui lòng chọn ít nhất một LPN."));

        if (form.PlannedStartTime >= form.PlannedEndTime)
            return BadRequest(ApiResponse<object>.Failure("PlannedStartTime phải nhỏ hơn PlannedEndTime."));

        // Bắt buộc chọn kho trước — chỉ các LPN thuộc kho này mới được ghép chuyến.
        if (!Guid.TryParse(ExtractGuid(form.ScheduleId), out var selectedScheduleId) || selectedScheduleId == Guid.Empty)
            return BadRequest(new { Success = false, Error = "ScheduleId is required and must be a valid GUID." });

        if (!Guid.TryParse(ExtractGuid(form.WarehouseId), out var selectedWarehouseId) || selectedWarehouseId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Failure("Vui lòng chọn kho (WarehouseId) trước khi ghép chuyến."));

        // Tài xế: 1–2 người, gán theo chuyến qua TripDriver
        var driverIds = (form.DriverIds ?? new List<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => ExtractGuid(d))
            .Where(d => Guid.TryParse(d, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

        if (driverIds.Count < 1 || driverIds.Count > 2)
            return BadRequest(ApiResponse<object>.Failure("Vui lòng chọn 1 hoặc 2 tài xế cho chuyến."));

        var parsedLpnIds = lpnIds.Select(id => Guid.Parse(ExtractGuid(id))).ToList();

        // Tự động tìm kho xuất phát từ LPN đầu tiên
        var firstLpnId = parsedLpnIds.First();
        var firstLpn = await _db.Lpns.Include(l => l.Order).FirstOrDefaultAsync(l => l.LpnId == firstLpnId);
        if (firstLpn == null)
            return BadRequest(ApiResponse<object>.Failure($"Không tìm thấy LPN {firstLpnId}."));

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
                return BadRequest(ApiResponse<object>.Failure("Không tìm thấy vị trí kho xuất phát nào trong hệ thống."));
            originLocId = fallbackLocId;
        }

        var request = new ManualDispatchRequest
        {
            ScheduleId = selectedScheduleId,
            WarehouseId = selectedWarehouseId,
            LpnIds = parsedLpnIds,
            VehicleId = Guid.Parse(ExtractGuid(form.VehicleId)),
            DriverIds = driverIds,
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

            return Ok(ApiResponse<ManualDispatchResult>.SuccessResponse(result, "Ghép chuyến thành công!"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Failure(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<object>.Failure($"Lỗi hệ thống khi manual-dispatch: {ex.Message}"));
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
    //  API 1.3: LẤY LẠI LINK GIẤY ĐI ĐƯỜNG (E-WAYBILL) THEO TRIP ID
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet("trip/{tripId}/waybill-url")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(object), 404)]
    public IActionResult GetWaybillUrl(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var id))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        // Giấy đi đường lưu với prefix "waybill_" (tách biệt hoàn toàn với sơ đồ LIFO "lifo_")
        var url = _fileService.GetSignedUrl($"coldchainx/waybill_{id}");
        return Ok(new { Success = true, WaybillPdfUrl = url });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 1.2: LẤY LẠI BẢN ĐỒ DẪN ĐƯỜNG (GOONG) THEO TRIP ID
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet("trip/{tripId}/route")]
    [ProducesResponseType(typeof(TripRouteResponse), 200)]
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

        var deliveryStops = trip.TripStops
            .Where(stop => stop.Location != null)
            .Where(stop => !IsSameLocation(stop.Location!, trip.OriginLocation)
                && !IsSameLocation(stop.Location!, trip.DestinationLocation))
            .OrderBy(stop => stop.StopSequence)
            .ToList();

        var origin = GoongMapService.FormatCoordinate(
            trip.OriginLocation.Latitude,
            trip.OriginLocation.Longitude);
        var destination = GoongMapService.FormatCoordinate(
            trip.DestinationLocation.Latitude,
            trip.DestinationLocation.Longitude);
        var waypoints = string.Join("|", deliveryStops.Select(stop =>
            GoongMapService.FormatCoordinate(stop.Location!.Latitude, stop.Location.Longitude)));

        try
        {
            var optimizedRoute = await _goongMapService.GetOptimizedRouteAsync(
                origin,
                destination,
                string.IsNullOrWhiteSpace(waypoints) ? null : waypoints,
                HttpContext.RequestAborted);
            var response = await BuildTripRouteResponseAsync(trip, deliveryStops, optimizedRoute);
            return Ok(new { Success = true, Data = response });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi khi gọi Goong API tối ưu lộ trình.", Detail = ex.Message });
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
    [ProducesResponseType(typeof(PagedResult<ColdChainX.Application.DTOs.Dispatch.TripDispatchDto>), 200)]
    public async Task<IActionResult> GetTripsCanStartPicking([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var query = _db.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
            .Where(t => t.Status == "PLANNED")
            .OrderByDescending(t => t.CreatedAt);

        var totalRecords = await query.CountAsync();

        var trips = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ColdChainX.Application.DTOs.Dispatch.TripDispatchDto
            {
                TripId = t.TripId,
                Status = t.Status,
                Vehicle = t.Vehicle != null ? t.Vehicle.TruckPlate : "N/A",
                Driver  = t.TripDrivers.Count > 0 ? string.Join(", ", t.TripDrivers.Select(td => td.Driver.FullName)) : "N/A",
                PlannedStartTime = t.PlannedStartTime,
                PlannedEndTime = t.PlannedEndTime,
                EstimatedDurationHours = t.EstimatedDurationHours,
                TotalLpns     = _db.Lpns.Count(l => l.TripId == t.TripId),
                AllocatedLpns = _db.Lpns.Count(l => l.TripId == t.TripId && l.State == LpnState.ALLOCATED),
                Label = $"{t.TripId} | Xe {(t.Vehicle != null ? t.Vehicle.TruckPlate : "N/A")} | {_db.Lpns.Count(l => l.TripId == t.TripId)} LPN"
            })
            .ToListAsync();

        var pagedResult = PagedResult<ColdChainX.Application.DTOs.Dispatch.TripDispatchDto>.Create(trips, totalRecords, pageNumber, pageSize);
        return Ok(pagedResult);
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
    //  API 2.1: CANCEL TRIP — Hủy chuyến đã ghép, reset toàn bộ về free
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hủy một chuyến đã ghép (planning) — kể cả đã bốc hàng / xếp hàng / kẹp chì.
    /// </summary>
    /// <remarks>
    /// ĐIỀU KIỆN: không được có LPN nào ở trạng thái SHIPPING (hàng đã xuất phát).
    ///
    /// Sau khi hủy, toàn bộ trở về như trước khi gọi manual-dispatch:
    ///   - LPN.State → IN_STOCK (hàng trở lại kho), gỡ TripId
    ///   - Đơn hàng → IN_STOCK, gỡ MasterTripId
    ///   - Seal → CANCELLED, gỡ SealNumber
    ///   - E-Waybill (TransportDocument) → CANCELLED
    ///   - Vehicle.Status / Driver.Status → ACTIVE (giải phóng)
    ///   - Trip.Status → CANCELLED
    ///
    /// Xe sau khi hủy có thể được ghép chuyến mới qua POST /api/Dispatch/manual-dispatch.
    /// </remarks>
    /// <param name="tripId">ID chuyến cần hủy</param>
    [HttpPost("trip/{tripId}/cancel")]
    [ProducesResponseType(typeof(CancelTripResult), 200)]
    public async Task<IActionResult> CancelTrip(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        try
        {
            var result = await _dispatchService.CancelTripAsync(parsedTripId);
            return Ok(new { Success = true, Data = result });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { Success = false, Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống khi hủy chuyến.", Detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 3: IOT CHECK — Kiểm tra tín hiệu IoT xe
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kiểm tra trạng thái kết nối, GPS, nhiệt độ, pin của các thiết bị IoT gắn trên xe.
    /// </summary>
    [HttpPost("vehicle-iot-check/{tripId}/{vehicleId}")]
    [ProducesResponseType(typeof(VehicleIoTStatus), 200)]
    public async Task<IActionResult> CheckVehicleIoT(string tripId, string vehicleId)
    {
        var rawVehicleId = ExtractGuid(vehicleId);
        if (!Guid.TryParse(rawVehicleId, out var parsedVehicleId))
            return BadRequest(new { Success = false, Error = "VehicleId không hợp lệ." });

        var rawTripId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawTripId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

        try
        {
            var result = await _dispatchService.CheckVehicleIoTAsync(parsedVehicleId, parsedTripId);
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
    [ProducesResponseType(typeof(PagedResult<ColdChainX.Application.DTOs.Dispatch.TripDispatchDto>), 200)]
    public async Task<IActionResult> GetTripsReadyToSeal([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var query = _db.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
            .Where(t => t.Status == "LOADING_COMPLETED" 
                     && string.IsNullOrEmpty(t.SealNumber)
                     && _db.Lpns.Count(l => l.TripId == t.TripId) > 0
                     && _db.Lpns.Count(l => l.TripId == t.TripId && l.State == LpnState.RELEASED) == _db.Lpns.Count(l => l.TripId == t.TripId))
            .OrderByDescending(t => t.CreatedAt);

        var totalRecords = await query.CountAsync();

        var trips = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ColdChainX.Application.DTOs.Dispatch.TripDispatchDto
            {
                TripId = t.TripId,
                Status = t.Status,
                Vehicle = t.Vehicle != null ? t.Vehicle.TruckPlate : "N/A",
                Driver  = t.TripDrivers.Count > 0 ? string.Join(", ", t.TripDrivers.Select(td => td.Driver.FullName)) : "N/A",
                PlannedStartTime = t.PlannedStartTime,
                PlannedEndTime = t.PlannedEndTime,
                EstimatedDurationHours = t.EstimatedDurationHours,
                TotalLpns    = _db.Lpns.Count(l => l.TripId == t.TripId),
                AllocatedLpns = _db.Lpns.Count(l => l.TripId == t.TripId && l.State == LpnState.RELEASED),
                Label = $"{t.TripId} | Xe {(t.Vehicle != null ? t.Vehicle.TruckPlate : "N/A")} | {_db.Lpns.Count(l => l.TripId == t.TripId)} LPN"
            })
            .ToListAsync();

        var pagedResult = PagedResult<ColdChainX.Application.DTOs.Dispatch.TripDispatchDto>.Create(trips, totalRecords, pageNumber, pageSize);
        return Ok(pagedResult);
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

    private async Task<TripRouteResponse> BuildTripRouteResponseAsync(
        MasterTrip trip,
        IReadOnlyList<TripStop> deliveryStops,
        GoongOptimizedRouteResult optimizedRoute)
    {
        var sortedStops = ApplyWaypointOrder(deliveryStops, optimizedRoute.WaypointOrder);
        var destinationLocationIds = sortedStops
            .Where(stop => stop.LocationId.HasValue)
            .Select(stop => stop.LocationId!.Value)
            .Distinct()
            .ToList();

        var orders = await _db.TransportOrders
            .Where(order => order.MasterTripId == trip.TripId
                && order.DestLocation.HasValue
                && destinationLocationIds.Contains(order.DestLocation.Value))
            .ToListAsync();

        var lpns = await _db.Lpns
            .Include(lpn => lpn.Order)
            .Where(lpn => lpn.TripId == trip.TripId
                && lpn.Order.DestLocation.HasValue
                && destinationLocationIds.Contains(lpn.Order.DestLocation.Value))
            .ToListAsync();

        return new TripRouteResponse
        {
            TripId = trip.TripId,
            OverviewPolyline = optimizedRoute.OverviewPolyline,
            TotalDistanceMeters = optimizedRoute.TotalDistanceMeters,
            TotalDurationSeconds = optimizedRoute.TotalDurationSeconds,
            WaypointOrder = optimizedRoute.WaypointOrder,
            Origin = new TripRoutePointDto
            {
                LocationId = trip.OriginLocation.LocationId,
                Address = trip.OriginLocation.Address,
                Lat = trip.OriginLocation.Latitude,
                Lon = trip.OriginLocation.Longitude
            },
            Destination = new TripRoutePointDto
            {
                LocationId = trip.DestinationLocation.LocationId,
                Address = trip.DestinationLocation.Address,
                Lat = trip.DestinationLocation.Latitude,
                Lon = trip.DestinationLocation.Longitude
            },
            OptimizedStops = sortedStops.Select((stop, index) =>
            {
                var locationId = stop.LocationId!.Value;
                return new OptimizedTripStopDto
                {
                    StopId = stop.StopId,
                    LocationId = locationId,
                    OriginalStopSequence = stop.StopSequence,
                    OptimizedSequence = index + 1,
                    StopType = stop.StopType,
                    Address = stop.Location!.Address,
                    Lat = stop.Location.Latitude,
                    Lon = stop.Location.Longitude,
                    Orders = orders
                        .Where(order => order.DestLocation == locationId)
                        .Select(ToTripRouteOrderDto)
                        .ToList(),
                    Lpns = lpns
                        .Where(lpn => lpn.Order.DestLocation == locationId)
                        .Select(ToLpnSummary)
                        .ToList()
                };
            }).ToList()
        };
    }

    private static List<TripStop> ApplyWaypointOrder(
        IReadOnlyList<TripStop> originalStops,
        IReadOnlyList<int> waypointOrder)
    {
        if (originalStops.Count == 0)
        {
            return new List<TripStop>();
        }

        var ordered = new List<TripStop>(originalStops.Count);
        var used = new HashSet<int>();

        foreach (var waypointIndex in waypointOrder)
        {
            if (waypointIndex < 0 || waypointIndex >= originalStops.Count || !used.Add(waypointIndex))
            {
                continue;
            }

            ordered.Add(originalStops[waypointIndex]);
        }

        for (var i = 0; i < originalStops.Count; i++)
        {
            if (used.Add(i))
            {
                ordered.Add(originalStops[i]);
            }
        }

        return ordered;
    }

    private static bool IsSameLocation(Location left, Location right)
    {
        return left.LocationId == right.LocationId
            || (left.Latitude == right.Latitude && left.Longitude == right.Longitude);
    }

    private static TripRouteOrderDto ToTripRouteOrderDto(TransportOrder order)
    {
        return new TripRouteOrderDto
        {
            OrderId = order.OrderId,
            TrackingCode = order.TrackingCode,
            ItemName = order.ItemName,
            Category = order.Category,
            Quantity = order.Quantity,
            WeightKg = (order.OrderDimension?.ActualWeightKg ?? 0m),
            Cbm = ((order.OrderDimension?.ActualCbm ?? 0m) > 0 ? (order.OrderDimension?.ActualCbm ?? 0m) : (order.OrderDimension?.ExpectedCbm ?? 0m)),
            TempCondition = order.TempCondition
        };
    }

    private static LpnSummary ToLpnSummary(Lpn lpn)
    {
        return new LpnSummary
        {
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            OrderId = lpn.OrderId,
            OrderTrackingCode = lpn.Order.TrackingCode,
            ItemName = lpn.Order.ItemName,
            Quantity = lpn.Quantity,
            WeightKg = lpn.ActualWeightKg,
            Cbm = lpn.ActualCbm,
            TempCondition = lpn.Order.TempCondition
        };
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


