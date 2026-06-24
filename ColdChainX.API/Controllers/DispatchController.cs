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
    private readonly IGoongMapService _goongMapService;
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
        IGoongMapService goongMapService,
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
        _goongMapService = goongMapService;
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
    /// Chuyển trip sang trạng thái PICKING — thông báo cho Loader bắt đầu lấy hàng.
    /// Trip phải đang ở trạng thái PLANNED.
    /// </summary>
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
            WeightKg = order.ActualWeightKg,
            Cbm = order.ActualCbm ?? order.ExpectedCbm,
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
