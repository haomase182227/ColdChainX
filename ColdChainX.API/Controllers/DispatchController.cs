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
using ColdChainX.Application.DTOs.WarehouseFlow;
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
    private static readonly string[] ActiveTripStatuses =
    {
        "PLANNED",
        "PICKING",
        "LOADING",
        "LOADED",
        "LOADING_COMPLETED",
        "SEALED",
        "DISPATCHED",
        "IN_TRANSIT",
        "DELAYED"
    };

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
    private readonly ICargoCompatibilityService _cargoCompatibilityService;

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
        IDriverAvailabilityService driverAvailability,
        ICargoCompatibilityService cargoCompatibilityService)
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
        _cargoCompatibilityService = cargoCompatibilityService;
    }

    private Guid GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idClaim, out var userId) ? userId : Guid.Empty;
    }

    // ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Lookup endpoints (dÃƒÆ’Ã‚Â¹ng Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã†â€™ populate dropdown trong form) ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬

    private string? FormatDateWithWeekday(DateTime? date)
    {
        if (!date.HasValue) return null;
        var d = date.Value;
        var dayOfWeek = d.DayOfWeek switch
        {
            DayOfWeek.Sunday => "CN",
            DayOfWeek.Monday => "T2",
            DayOfWeek.Tuesday => "T3",
            DayOfWeek.Wednesday => "T4",
            DayOfWeek.Thursday => "T5",
            DayOfWeek.Friday => "T6",
            DayOfWeek.Saturday => "T7",
            _ => ""
        };
        return $"{d:yyyy-MM-dd} ({dayOfWeek})";
    }


    /// <summary>
    /// Lấy danh sách các LPN khả dụng (chưa lên xe) trong một kho cụ thể. (Dùng để chọn Pivot LPN)
    /// </summary>
    [HttpGet("available-lpns")]
    [ProducesResponseType(typeof(PagedResponse<List<FilterLpnResponse>>), 200)]
    public async Task<IActionResult> GetAvailableLpns([FromQuery] Guid warehouseId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.Schedule)
                    .ThenInclude(s => s.Route)
            .Include(l => l.Order)
                .ThenInclude(o => o.Customer)
            .Where(l => l.WarehouseId == warehouseId && l.State == LpnState.IN_STOCK && l.TripId == null);

        var totalCount = await query.CountAsync();

        var lpns = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var res = lpns.Select(l => new FilterLpnResponse
        {
            LpnId = l.LpnId,
            LpnCode = l.LpnCode,
            Quantity = l.Quantity,
            ActualWeightKg = l.ActualWeightKg,
            ActualCbm = l.ActualCbm,
            Category = l.Order?.Category ?? "UNKNOWN",
            RequiredTemperature = l.RequiredTemperature,
            HasStrongOdor = l.Order?.HasStrongOdor ?? false,
            SlaDeadline = FormatDateWithWeekday(l.SlaDeadline),
            DepartureTime = l.Order?.Schedule?.DepartureTime.ToString(@"hh\:mm"),
            OrderId = l.Order?.OrderId,
            TrackingCode = l.Order?.TrackingCode,
            ItemName = l.Order?.ItemName,
            CustomerName = l.Order?.Customer?.CompanyName,
            RouteName = l.Order?.Schedule?.Route != null ? $"{l.Order.Schedule.Route.OriginCity} - {l.Order.Schedule.Route.DestCity}" : null,
            State = l.State.ToString()
        }).ToList();

        return Ok(PagedResponse<List<FilterLpnResponse>>.SuccessPagedResponse(res, pageIndex, pageSize, totalCount));
    }

    /// <summary>
    /// [Lookup] Lấy danh sách 4 LPN tương thích với LPN gốc (Pivot).
    /// </summary>
    [HttpGet("filter-lpns")]
    [ProducesResponseType(typeof(PagedResponse<List<FilterLpnResponse>>), 200)]
    public async Task<IActionResult> FilterLpns([FromQuery] Guid pivotLpnId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        var pivotLpn = await _db.Lpns
            .Include(l => l.Order)
            .FirstOrDefaultAsync(l => l.LpnId == pivotLpnId);

        if (pivotLpn == null) return NotFound(PagedResponse<List<FilterLpnResponse>>.Failure("Pivot LPN not found."));
        if (pivotLpn.Order == null) return BadRequest(PagedResponse<List<FilterLpnResponse>>.Failure("Pivot LPN does not have an Order."));

        var query = _db.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.Schedule)
                    .ThenInclude(s => s.Route)
            .Include(l => l.Order)
                .ThenInclude(o => o.Customer)
            .Where(l => l.State == LpnState.IN_STOCK && l.TripId == null && l.LpnId != pivotLpnId && l.WarehouseId == pivotLpn.WarehouseId);

        var allLpns = await query.ToListAsync();

        var validLpns = new List<Lpn>();

        foreach (var lpn in allLpns)
        {
            if (lpn.Order == null) continue;

            // Ãƒâ€žÃ‚ÂÃƒÆ’Ã‚Â£ BÃƒÂ¡Ã‚Â»Ã…Â½ QUA LÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºp 0 (RÃƒÆ’Ã‚Â ng buÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢c LÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch trÃƒÆ’Ã‚Â¬nh), lÃƒÂ¡Ã‚ÂºÃ‚Â¥y TÃƒÂ¡Ã‚ÂºÃ‚Â¤T CÃƒÂ¡Ã‚ÂºÃ‚Â¢ hÃƒÆ’Ã‚Â ng hÃƒÆ’Ã‚Â³a trong kho.

            // Lá»›p 1: RÃ ng buá»™c Danh má»¥c
            var pCat = pivotLpn.Order.Category.ToUpper();
            var tCat = lpn.Order.Category.ToUpper();

            if (pCat == "PHARMACEUTICALS" && tCat != "PHARMACEUTICALS") continue;
            if (pCat == "MEAT_SEAFOOD" && tCat != "MEAT_SEAFOOD") continue;
            if (pCat == "RAW_MATERIALS_OTHERS" && tCat != "RAW_MATERIALS_OTHERS") continue;
            if ((pCat == "FROZEN_FRUITS_VEGGIES" || pCat == "ICE_CREAM_BEVERAGES") &&
                (tCat != "FROZEN_FRUITS_VEGGIES" && tCat != "ICE_CREAM_BEVERAGES")) continue;

            // LÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºp 2: Dung sai NhiÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ Ãƒâ€žÃ‚ÂÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ng
            decimal pTemp = pivotLpn.RequiredTemperature ?? 0;
            decimal tTemp = lpn.RequiredTemperature ?? 0;
            decimal tolerance = 2m; // Default
            if (pCat == "PHARMACEUTICALS") tolerance = 0m;
            else if (pCat == "ICE_CREAM_BEVERAGES") tolerance = 1m;

            if (Math.Abs(pTemp - tTemp) > tolerance) continue;

            // Lá»›p 3: RÃ ng buá»™c Ma tráº­n MÃ¹i
            if (pivotLpn.Order.HasStrongOdor && tCat == "ICE_CREAM_BEVERAGES") continue;
            if (pCat == "ICE_CREAM_BEVERAGES" && lpn.Order.HasStrongOdor) continue;

            validLpns.Add(lpn);
        }

        var totalCount = validLpns.Count;

        var pagedLpns = validLpns
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var res = pagedLpns.Select(l => new FilterLpnResponse
        {
            LpnId = l.LpnId,
            LpnCode = l.LpnCode,
            Quantity = l.Quantity,
            ActualWeightKg = l.ActualWeightKg,
            ActualCbm = l.ActualCbm,
            Category = l.Order?.Category ?? "UNKNOWN",
            RequiredTemperature = l.RequiredTemperature,
            HasStrongOdor = l.Order?.HasStrongOdor ?? false,
            SlaDeadline = FormatDateWithWeekday(l.SlaDeadline),
            DepartureTime = l.Order?.Schedule?.DepartureTime.ToString(@"hh\:mm"),
            OrderId = l.Order?.OrderId,
            TrackingCode = l.Order?.TrackingCode,
            ItemName = l.Order?.ItemName,
            CustomerName = l.Order?.Customer?.CompanyName,
            RouteName = l.Order?.Schedule?.Route != null ? $"{l.Order.Schedule.Route.OriginCity} - {l.Order.Schedule.Route.DestCity}" : null,
            State = l.State.ToString(),
            IsCompatible = true
        }).ToList();

        return Ok(PagedResponse<List<FilterLpnResponse>>.SuccessPagedResponse(res, pageIndex, pageSize, totalCount, "Filtered LPNs successfully."));
    }

    /// <summary>
    /// [Lookup] Danh sách xe tải ACTIVE và chưa được gán chuyến đang hoạt động.
    /// </summary>

    [HttpGet("lookup/vehicles/by-warehouse/{warehouseId}")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupVehiclesByWarehouse(Guid warehouseId)
    {
        var warehouseName = await _db.Warehouses
            .Where(w => w.WarehouseId == warehouseId)
            .Select(w => w.WarehouseName)
            .FirstOrDefaultAsync() ?? "Kho hiá»‡n táº¡i";

        var wIdStr = warehouseId.ToString();
        var busyVehicleIds = _db.MasterTrips
            .AsNoTracking()
            .Where(t => ActiveTripStatuses.Contains(t.Status))
            .Select(t => t.VehicleId);

        var maintenanceVehicleIds = _db.MaintenanceTickets
            .AsNoTracking()
            .Where(t => t.Status == "OPEN" || t.Status == "IN_PROGRESS")
            .Select(t => t.VehicleId);

        var vehicles = await _db.Vehicles
            .AsNoTracking()
            .Where(v => v.Status == "ACTIVE"
                && (v.CurrentLocation == wIdStr || v.CurrentLocation == null)
                && !busyVehicleIds.Contains(v.VehicleId)
                && !maintenanceVehicleIds.Contains(v.VehicleId))
            .ToListAsync();

        var items = vehicles.Select(v => new
        {
            v.VehicleId,
            Label = $"{v.TruckPlate} - {v.VehicleType} | tải {v.MaxWeight}kg / {v.MaxCbm}m3",
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
                    : v.MaxCbm * 0.8m,
            CurrentLocation = v.CurrentLocation == wIdStr ? warehouseName : "ChÆ°a xÃ¡c Ä‘á»‹nh"
        }).ToList();

        return Ok(items);
    }

    [HttpGet("lookup/drivers/by-warehouse/{warehouseId}")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupDriversByWarehouse(Guid warehouseId)
    {
        var warehouseName = await _db.Warehouses
            .Where(w => w.WarehouseId == warehouseId)
            .Select(w => w.WarehouseName)
            .FirstOrDefaultAsync() ?? "Kho hiá»‡n táº¡i";

        var wIdStr = warehouseId.ToString();
        var busyDriverIds = _db.TripDrivers
            .AsNoTracking()
            .Where(td => td.Trip.Status != null
                && ActiveTripStatuses.Contains(td.Trip.Status))
            .Select(td => td.DriverId);

        var candidates = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Include(d => d.User)
            .Where(d => (d.Status == "ACTIVE" || d.Status == "RELAX")
                && d.CurrentLocation == wIdStr
                && !busyDriverIds.Contains(d.DriverId))
            .ToListAsync();

        foreach (var d in candidates)
            await _driverAvailability.ReconcileStatusAsync(d);
        await _db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = candidates
            .Where(d => d.Status == "ACTIVE")
            .Select(d =>
            {
                var lic = d.DriverLicenses
                    .Where(l => l.ExpiryDate >= today && (l.Status == null || l.Status == "ACTIVE"))
                    .OrderByDescending(l => l.ExpiryDate)
                    .FirstOrDefault();

                var isLicValid = lic != null;
                var tag = !isLicValid ? "[HẾT HẠN BẰNG]" : d.Status == "ACTIVE" ? "" : $"[{d.Status}]";
                return new
                {
                    d.DriverId,
                    d.FullName,
                    Phone = d.PhoneNumber,
                    d.Status,
                    LicenseClass = lic?.LicenseClass ?? "N/A",
                    LicenseExpiryDate = lic?.ExpiryDate,
                    IsLicenseValid = isLicValid,
                    Label = $"{d.FullName} ({d.PhoneNumber}) {tag} - Háº¡ng {lic?.LicenseClass ?? "N/A"}",
                    CurrentLocation = d.CurrentLocation == wIdStr ? warehouseName : "ChÆ°a xÃ¡c Ä‘á»‹nh"
                };
            })
            .ToList();

        return Ok(items);
    }

    [HttpGet("lookup/vehicles")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupVehicles()
    {
        var busyVehicleIds = await _db.MasterTrips
            .AsNoTracking()
            .Where(t => ActiveTripStatuses.Contains(t.Status))
            .Select(t => t.VehicleId)
            .Distinct()
            .ToListAsync();

        var result = await _vehicleService.GetAllAsync();
        var items = result.Data?
            .Where(v => v.Status == "ACTIVE" && !busyVehicleIds.Contains(v.VehicleId))
            .Select(v => new
            {
                v.VehicleId,
                Label      = $"{v.TruckPlate} ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â  {v.VehicleType} | tÃƒÂ¡Ã‚ÂºÃ‚Â£i {v.MaxWeight}kg / {v.MaxCbm}mÃƒâ€šÃ‚Â³",
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
        return Ok(ApiResponse<object>.SuccessResponse(items, "Láº¥y danh sÃ¡ch xe táº£i thÃ nh cÃ´ng."));
    }

    /// <summary>
    /// [Lookup] Danh sách tài xế khả dụng – dùng để chọn 1-2 tài xế cho manual-dispatch.
    /// </summary>
    /// <remarks>
    /// TÃƒÂ¡Ã‚Â»Ã‚Â± Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ng gÃƒÂ¡Ã‚Â»Ã‚Â¡ trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i RELAX nÃƒÂ¡Ã‚ÂºÃ‚Â¿u Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ qua ngÃƒÆ’Ã‚Â y/tuÃƒÂ¡Ã‚ÂºÃ‚Â§n giÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºi hÃƒÂ¡Ã‚ÂºÃ‚Â¡n, sau Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â³ chÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° trÃƒÂ¡Ã‚ÂºÃ‚Â£ vÃƒÂ¡Ã‚Â»Ã‚Â
    /// cÃ¡c tÃ i xáº¿ cÃ³ thá»ƒ gÃ¡n chuyáº¿n (khÃ´ng RELAX/Offline/Inactive vÃ  cÃ²n báº±ng lÃ¡i háº¡n).
    /// </remarks>
    [HttpGet("lookup/drivers")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupDrivers()
    {
        var candidates = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Where(d => d.Status != "Offline" && d.Status != "Inactive" && d.Status != "DELETED")
            .ToListAsync();

        // TÃƒÂ¡Ã‚Â»Ã‚Â± Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ng gÃƒÂ¡Ã‚Â»Ã‚Â¡ RELAX Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ hÃƒÂ¡Ã‚ÂºÃ‚Â¿t hÃƒÂ¡Ã‚ÂºÃ‚Â¡n (cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t Status tÃƒÂ¡Ã‚ÂºÃ‚Â¡i chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€)
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
                    Label = $"{d.FullName} ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â {d.PhoneNumber}" + (lic != null ? $" | BÃƒÂ¡Ã‚ÂºÃ‚Â±ng {lic.LicenseClass}" : " | ÃƒÂ¢Ã…Â¡Ã‚Â  HÃƒÂ¡Ã‚ÂºÃ‚Â¿t hÃƒÂ¡Ã‚ÂºÃ‚Â¡n bÃƒÂ¡Ã‚ÂºÃ‚Â±ng lÃƒÆ’Ã‚Â¡i")
                };
            })
            .Where(x => x.HasValidLicense)
            .OrderBy(x => x.FullName)
            .ToList();

        return Ok(ApiResponse<object>.SuccessResponse(items, "Láº¥y danh sÃ¡ch tÃ i xáº¿ thÃ nh cÃ´ng."));
    }

    /// <summary>
    /// [Lookup] Danh sách Location đang ACTIVE – dùng để chọn kho xuất phát.
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

        return Ok(ApiResponse<object>.SuccessResponse(locations, "Láº¥y danh sÃ¡ch kho/Ä‘iá»ƒm Ä‘áº¿n thÃ nh cÃ´ng."));
    }

    /// <summary>
    /// [Lookup] Danh sách kho hiện có – dùng để chọn kho trạm dừng khi ghép chuyến (manual-dispatch).
    /// </summary>
    /// <summary>
    /// [Lookup] Danh sach lich chay dang ACTIVE tu bang route_schedules.
    /// </summary>
    [HttpGet("lookup/Schedule")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupSchedules()
    {
        var rawItems = await _db.RouteSchedules
            .AsNoTracking()
            .Where(s => s.Route.Status.ToUpper() == "ACTIVE"
                && (s.Status.ToUpper() == "ACTIVE"
                    || _db.Lpns.Any(l => l.Order.ScheduleId == s.ScheduleId
                        && l.State == LpnState.IN_STOCK
                        && l.TripId == null)))
            .OrderBy(s => s.DepartureDate)
            .ThenBy(s => s.DepartureTime)
            .ThenBy(s => s.Route.RouteCode)
            .Select(s => new
            {
                s.ScheduleId,
                s.RouteId,
                s.Route.RouteCode,
                s.Route.OriginCity,
                s.Route.DestCity,
                s.ScheduleName,
                s.DepartureDate,
                s.DepartureTime,
                s.CutOffTime,
                s.Status
            })
            .ToListAsync();

        var items = rawItems.Select(s => new
        {
            s.ScheduleId,
            s.RouteId,
            s.RouteCode,
            RouteName = $"{s.OriginCity} -> {s.DestCity}",
            s.ScheduleName,
            s.DepartureDate,
            DayOfWeek = (int)s.DepartureDate.DayOfWeek,
            s.DepartureTime,
            s.CutOffTime,
            s.Status,
            Label = $"{s.ScheduleName} - {s.RouteCode} | {s.OriginCity} -> {s.DestCity} | {s.DepartureDate:dd/MM/yyyy} {s.DepartureTime:hh\\:mm}"
        }).ToList();

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

        return Ok(ApiResponse<object>.SuccessResponse(items, "Láº¥y danh sÃ¡ch kho xuáº¥t phÃ¡t thÃ nh cÃ´ng."));
    }

    /// <summary>
    /// [Lookup] Danh sách tài xế khả dụng – dùng để chọn 1-2 tài xế cho manual-dispatch.
    /// </summary>
    /// <remarks>
    /// TruyÃƒÂ¡Ã‚Â»Ã‚Ân <paramref name="warehouseId"/> Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã†â€™ chÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° lÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÆ’Ã‚Â¡c LPN thuÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢c kho Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ chÃƒÂ¡Ã‚Â»Ã‚Ân
    /// (Lpn.WarehouseId == warehouseId). Ãƒâ€žÃ‚ÂÃƒÆ’Ã‚Â¢y lÃƒÆ’Ã‚Â  bÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc bÃƒÂ¡Ã‚ÂºÃ‚Â¯t buÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢c cÃƒÂ¡Ã‚Â»Ã‚Â§a luÃƒÂ¡Ã‚Â»Ã¢â‚¬Å“ng manual-dispatch:
    /// ngÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Âi dÃƒÆ’Ã‚Â¹ng chÃƒÂ¡Ã‚Â»Ã‚Ân kho trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc, sau Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â³ chÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° thÃƒÂ¡Ã‚ÂºÃ‚Â¥y LPN cÃƒÂ¡Ã‚Â»Ã‚Â§a kho Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â³.
    ///
    /// MÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i phÃƒÂ¡Ã‚ÂºÃ‚Â§n tÃƒÂ¡Ã‚Â»Ã‚Â­ Data trÃƒÂ¡Ã‚ÂºÃ‚Â£ vÃƒÂ¡Ã‚Â»Ã‚Â: lpnId, lpnCode, orderId, trackingCode, itemName, customerName,
    /// warehouseId, warehouseName, destinationAddress, routeName, plannedDispatchDate, quantity,
    /// actualWeightKg, actualCbm, tempCondition.
    ///
    /// CÃ³ phÃ¢n trang qua <paramref name="pageNumber"/> / <paramref name="pageSize"/> (máº·c Ä‘á»‹nh 1/10).
    /// </remarks>
    [HttpGet("lookup/lpns-ready")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupLpnsReady(
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? scheduleId,
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
                       && l.TripId == null
                       && (warehouseId == null || l.WarehouseId == warehouseId)
                       && (scheduleId == null || o.ScheduleId == scheduleId)
                    select new
                    {
                        l.LpnId,
                        l.LpnCode,
                        l.Quantity,
                        l.ActualWeightKg,
                        l.ActualCbm,
                        l.RequiredTemperature,
                        l.SlaDeadline,
                        o.OrderId,
                        o.TrackingCode,
                        o.ItemName,
                        o.Category,
                        o.TempCondition,
                        o.HasStrongOdor,
                        o.IsStackable,
                        CustomerName = cust != null ? cust.CompanyName : "N/A",
                        ScheduleId = schedule != null ? schedule.ScheduleId : (Guid?)null,
                        ScheduleName = schedule != null ? schedule.ScheduleName : null,
                        DepartureDate = schedule != null ? schedule.DepartureDate : (DateTime?)null,
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

        var items = rawLpns.Select(x => new DispatchLpnReadyDto
        {
            LpnId = x.LpnId,
            Label = $"{x.LpnCode} ({x.TrackingCode}) ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â {x.ItemName} | Qty: {x.Quantity} | {x.ActualWeightKg}kg / {x.ActualCbm}mÃƒâ€šÃ‚Â³ ({x.TempCondition}) | KhÃƒÆ’Ã‚Â¡ch: {x.CustomerName} | Kho: {x.WarehouseName}",
            LpnCode = x.LpnCode,
            OrderId = x.OrderId,
            TrackingCode = x.TrackingCode,
            ItemName = x.ItemName,
            CustomerName = x.CustomerName,
            ScheduleId = x.ScheduleId,
            ScheduleName = x.ScheduleName,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.WarehouseName,
            DestinationAddress = x.DestinationAddress,
            RouteName = x.RouteOriginCity != null ? $"{x.RouteOriginCity} â†’ {x.RouteDestCity}" : null,
            PlannedDispatchDate = x.DepartureDate,
            Quantity = x.Quantity,
            ActualWeightKg = x.ActualWeightKg,
            ActualCbm = x.ActualCbm,
            TempCondition = x.TempCondition,
            Category = x.Category,
            RequiredTemperature = x.RequiredTemperature,
            HasStrongOdor = x.HasStrongOdor,
            IsStackable = x.IsStackable,
            CreatedAt = x.CreatedAt
        }).ToList();

        var pagedResult = PagedResult<DispatchLpnReadyDto>.Create(items, totalRecords, safePageNumber, safePageSize);

        return Ok(ApiResponse<PagedResult<DispatchLpnReadyDto>>.SuccessResponse(pagedResult, "Láº¥y danh sÃ¡ch LPN thÃ nh cÃ´ng."));
    }

    [HttpPost("compatible-lpns/search")]
    [ProducesResponseType(typeof(ApiResponse<CompatibleLpnsSearchResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> SearchCompatibleLpns(
        [FromBody] CompatibleLpnsSearchRequest? request,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        if (request == null || request.ScheduleId == Guid.Empty)
        {
            return BadRequest(ApiResponse<object>.Failure("ScheduleId is required."));
        }

        var scheduleExists = await _db.RouteSchedules
            .AsNoTracking()
            .AnyAsync(s => s.ScheduleId == request.ScheduleId);

        if (!scheduleExists)
        {
            return BadRequest(ApiResponse<object>.Failure("Schedule does not exist."));
        }

        var safePageNumber = pageNumber < 1 ? 1 : pageNumber;
        var safePageSize = pageSize < 1 ? 20 : pageSize;
        var selectedIds = (request.SelectedLpnIds ?? new List<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var selectedLpns = selectedIds.Count == 0
            ? new List<Lpn>()
            : await _db.Lpns
                .Include(l => l.Order)
                    .ThenInclude(o => o.Schedule)
                .Where(l => selectedIds.Contains(l.LpnId))
                .ToListAsync();

        var selectedValidation = _cargoCompatibilityService.ValidateSelectedSet(
            selectedLpns,
            request.ScheduleId,
            selectedIds);

        var compatibleItems = new List<CompatibleLpnItemDto>();
        if (selectedValidation.IsValid)
        {
            var selectedWarehouseId = selectedLpns.Count > 0 ? selectedLpns[0].WarehouseId : null;

            var candidates = await _db.Lpns
                .Include(l => l.Order)
                    .ThenInclude(o => o.Customer)
                .Include(l => l.Order)
                    .ThenInclude(o => o.Schedule)
                        .ThenInclude(s => s.Route)
                .Include(l => l.Order)
                    .ThenInclude(o => o.DestLocationNavigation)
                .Include(l => l.Warehouse)
                .Where(l => l.State == LpnState.IN_STOCK
                    && l.TripId == null
                    && l.Order.ScheduleId == request.ScheduleId
                    && !selectedIds.Contains(l.LpnId)
                    && (!selectedWarehouseId.HasValue || l.WarehouseId == selectedWarehouseId))
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            compatibleItems = candidates
                .Where(l => !_cargoCompatibilityService
                    .ValidateCandidate(l, selectedLpns, request.ScheduleId, selectedWarehouseId)
                    .Any())
                .Select(ToCompatibleLpnItemDto)
                .ToList();
        }

        var totalRecords = compatibleItems.Count;
        var totalPages = (int)Math.Ceiling(totalRecords / (double)safePageSize);
        var pageItems = compatibleItems
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        var response = new CompatibleLpnsSearchResponse
        {
            SelectedSetValid = selectedValidation.IsValid,
            Conflicts = selectedValidation.Conflicts,
            TotalRecords = totalRecords,
            TotalPages = totalPages,
            CurrentPage = safePageNumber,
            PageSize = safePageSize,
            Items = pageItems
        };

        return Ok(ApiResponse<CompatibleLpnsSearchResponse>.SuccessResponse(response, "Compatible LPNs retrieved successfully."));
    }

    [AllowAnonymous]
    [HttpPost("simulate-packing")]
    public async Task<IActionResult> SimulatePacking([FromBody] SimulatePackingRequest request, [FromQuery] bool for3d = false)
    {
        if (request.ScheduleId == Guid.Empty)
        {
            return BadRequest(ApiResponse<object>.Failure("ScheduleId is required."));
        }

        if (request.LpnIds == null || !request.LpnIds.Any())
        {
            return BadRequest(ApiResponse<object>.Failure("At least one LPN is required."));
        }

        var schedule = await _db.RouteSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ScheduleId == request.ScheduleId);

        var vehicle = await _db.Vehicles.FindAsync(request.VehicleId);
        if (vehicle == null) return NotFound(ApiResponse<object>.Failure("Vehicle not found"));

        if (!vehicle.InnerLengthCm.HasValue || vehicle.InnerLengthCm.Value <= 0
            || !vehicle.InnerWidthCm.HasValue || vehicle.InnerWidthCm.Value <= 0
            || !vehicle.InnerHeightCm.HasValue || vehicle.InnerHeightCm.Value <= 0)
        {
            return BadRequest(ApiResponse<object>.Failure(
                $"Xe {vehicle.TruckPlate} chưa có đủ kích thước lòng thùng (dài, rộng, cao) để mô phỏng xếp hàng."));
        }

        decimal vLength = vehicle.InnerLengthCm.Value;
        decimal vWidth = vehicle.InnerWidthCm.Value;
        decimal vHeight = vehicle.InnerHeightCm.Value;

        var selectedIds = request.LpnIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var lpns = await _db.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.Schedule)
            .Where(l => request.LpnIds.Contains(l.LpnId))
            .ToListAsync();

        var lpnsWithoutDimensions = lpns
            .Where(l => !l.LengthCm.HasValue || l.LengthCm.Value <= 0
                || !l.WidthCm.HasValue || l.WidthCm.Value <= 0
                || !l.HeightCm.HasValue || l.HeightCm.Value <= 0)
            .Select(l => l.LpnCode)
            .ToList();
        if (lpnsWithoutDimensions.Count > 0)
        {
            return BadRequest(ApiResponse<object>.Failure(
                $"Các LPN sau chưa có đủ kích thước dài, rộng, cao để mô phỏng xếp hàng: {string.Join(", ", lpnsWithoutDimensions)}."));
        }

        var selectedValidation = _cargoCompatibilityService.ValidateSelectedSet(
            lpns,
            request.ScheduleId,
            selectedIds);
        var vehicleTempConflicts = _cargoCompatibilityService.ValidateVehicleTemperature(vehicle, lpns);
        var blockingReasons = selectedValidation.Conflicts
            .Concat(vehicleTempConflicts)
            .Select(c => $"{c.ReasonCode}: {c.Message}")
            .Distinct()
            .ToList();

        if (schedule == null)
        {
            blockingReasons.Add("DIFFERENT_SCHEDULE: Schedule does not exist.");
        }

        if (!string.Equals(vehicle.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            blockingReasons.Add($"INVALID_VEHICLE_STATE: Vehicle {vehicle.TruckPlate} is not ACTIVE.");
        }

        var vehicleBusy = await _db.MasterTrips
            .AnyAsync(t => t.VehicleId == request.VehicleId
                && ActiveTripStatuses.Contains(t.Status));

        if (vehicleBusy)
        {
            blockingReasons.Add($"INVALID_VEHICLE_STATE: Vehicle {vehicle.TruckPlate} is already assigned to an active trip.");
        }

        var totalWeight = lpns.Sum(l => l.ActualWeightKg);
        var totalCbm = lpns.Sum(l => l.ActualCbm);
        var isOverweight = totalWeight > vehicle.MaxWeight;
        var isOverCbm = totalCbm > vehicle.MaxCbm;
        if (isOverweight)
        {
            blockingReasons.Add($"OVERWEIGHT: Total weight {totalWeight:0.##}kg exceeds vehicle max weight {vehicle.MaxWeight:0.##}kg.");
        }

        if (isOverCbm)
        {
            blockingReasons.Add($"OVERCAPACITY: Total CBM {totalCbm:0.##} exceeds vehicle max CBM {vehicle.MaxCbm:0.##}.");
        }

        var engineItems = new List<ColdChainX.Application.Services.LpnDims>();
        
        // LIFO logic: rank by SlaDeadline. Latest deadline = delivered last = packed first (highest sequence)
        var orderedLpns = lpns.OrderByDescending(l => l.SlaDeadline).ToList();
        for (int i = 0; i < orderedLpns.Count; i++)
        {
            var lpn = orderedLpns[i];
            int qty = Math.Max(1, lpn.Quantity);
            for (int j = 0; j < qty; j++)
            {
                engineItems.Add(new ColdChainX.Application.Services.LpnDims
                {
                    LpnId = lpn.LpnId,
                    Length = lpn.LengthCm!.Value,
                    Width = lpn.WidthCm!.Value,
                    Height = lpn.HeightCm!.Value,
                    RouteStopSequence = orderedLpns.Count - i,
                    WeightKg = lpn.ActualWeightKg,
                    RequiredTemperature = _cargoCompatibilityService.ResolveRequiredTemperature(lpn) ?? 5m,
                    IsStackable = lpn.Order?.IsStackable ?? true
                });
            }
        }

        var engine = new ColdChainX.Application.Services.CargoPackingEngine();
        var packingResult = engine.Pack(
            new ColdChainX.Application.Services.ContainerDims { Length = vLength, Width = vWidth, Height = vHeight }, 
            engineItems);

        if (packingResult.UnplacedLpnIds.Any())
        {
            blockingReasons.Add($"PACKING_FAILED: Unplaced LPNs: {string.Join(", ", packingResult.UnplacedLpnIds)}.");
        }

        var colors = new[] { 
            "#ef4444", "#3b82f6", "#10b981", "#f59e0b", 
            "#8b5cf6", "#ec4899", "#06b6d4", "#f97316", 
            "#84cc16", "#64748b"
        };

        var distinctLpnIds = lpns.Select(l => l.LpnId).Distinct().ToList();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var shareableLink = $"{baseUrl}/3d-viewer.html?scheduleId={request.ScheduleId}&vehicleId={request.VehicleId}&lpnIds={string.Join(",", request.LpnIds)}";

        var response = new SimulatePackingResponse
        {
            SelectedSetValid = selectedValidation.IsValid,
            CanCreateTrip = !blockingReasons.Any(),
            BlockingReasons = blockingReasons,
            Vehicle = new SimulatePackingVehicleDto
            {
                VehicleId = vehicle.VehicleId,
                TruckPlate = vehicle.TruckPlate,
                VehicleType = vehicle.VehicleType,
                Status = vehicle.Status,
                MaxWeight = vehicle.MaxWeight,
                MaxCbm = vehicle.MaxCbm,
                MinTemp = vehicle.MinTemp,
                MaxTemp = vehicle.MaxTemp
            },
            TotalWeight = totalWeight,
            MaxWeight = vehicle.MaxWeight,
            WeightUtilization = vehicle.MaxWeight > 0 ? Math.Round(totalWeight / vehicle.MaxWeight * 100, 1) : 0,
            IsOverweight = isOverweight,
            TotalCbm = totalCbm,
            MaxCbm = vehicle.MaxCbm,
            IsOverCbm = isOverCbm,
            VehicleType = vehicle.VehicleType,
            ContainerLength = vLength,
            ContainerWidth = vWidth,
            ContainerHeight = vHeight,
            Utilisation = packingResult.Utilisation,
            ShareableLink = shareableLink,
            UnplacedLpnIds = packingResult.UnplacedLpnIds,
            PlacedItems = packingResult.PlacedItems.Select(pi => {
                var lpn = lpns.First(l => l.LpnId == pi.LpnId);
                int colorIdx = distinctLpnIds.IndexOf(pi.LpnId);
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
                    Color = colors[colorIdx % colors.Length],
                    ItemName = lpn.Order?.ItemName ?? "Unknown Item",
                    Quantity = lpn.Quantity,
                    Location = lpn.StorageLocation ?? "N/A"
                };
            }).ToList()
        };

        return Ok(ApiResponse<SimulatePackingResponse>.SuccessResponse(response, "Preview load plan successful."));
    }

    /// <summary>
    /// API thông minh: Tìm ra Lịch trình (Schedule) từ một ID bất kỳ (có thể là LpnId, OrderId, TripId hoặc chính ScheduleId).
    /// </summary>
    [HttpGet("resolve-schedule/{entityId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> ResolveScheduleFromId(Guid entityId)
    {
        // 1. Thử xem ID này có phải là LPN ID không?
        var lpn = await _db.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.Schedule)
            .FirstOrDefaultAsync(l => l.LpnId == entityId);
        if (lpn?.Order?.Schedule != null)
        {
            return Ok(ApiResponse<object>.SuccessResponse(new { EntityType = "LPN", Schedule = lpn.Order.Schedule }, "Success"));
        }

        // 2. Thử xem ID này có phải là Order ID không?
        var order = await _db.TransportOrders
            .Include(o => o.Schedule)
            .FirstOrDefaultAsync(o => o.OrderId == entityId);
        if (order?.Schedule != null)
        {
            return Ok(ApiResponse<object>.SuccessResponse(new { EntityType = "ORDER", Schedule = order.Schedule }, "Success"));
        }

        // 3. Thử xem ID này có phải là Trip ID không?
        var trip = await _db.MasterTrips.FirstOrDefaultAsync(t => t.TripId == entityId);
        if (trip != null)
        {
            var sched = await _db.RouteSchedules.FirstOrDefaultAsync(s => s.ScheduleId == trip.ScheduleId);
            if (sched != null)
            {
                return Ok(ApiResponse<object>.SuccessResponse(new { EntityType = "TRIP", Schedule = sched }, "Success"));
            }
        }

        // 4. Thử xem ID này có phải là chính Schedule ID không?
        var schedule = await _db.RouteSchedules.FirstOrDefaultAsync(s => s.ScheduleId == entityId);
        if (schedule != null)
        {
            return Ok(ApiResponse<object>.SuccessResponse(new { EntityType = "SCHEDULE", Schedule = schedule }, "Success"));
        }

        return NotFound(ApiResponse<object>.Failure($"Không thể truy xuất được Lịch trình (Schedule) nào từ ID: {entityId}"));
    }

    /// <summary>
    /// [BUOC 1/5] Ghép chuyến thủ công để chọn xe, tài xế và danh sách LPN.
    /// </summary>
    /// <remarks>
    /// TRANG THAI LPN: IN_STOCK â†’ ALLOCATED
    ///
    /// Dieu kien:
    ///   - Phai chon Kho (WarehouseId) truoc; chi cac LPN thuoc kho do moi duoc ghep chuyen
    ///   - Tat ca LPN phai cung mot kho ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â khong duoc tron LPN tu nhieu kho khac nhau
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
            return BadRequest(ApiResponse<object>.Failure("Vui lÃƒÆ’Ã‚Â²ng chÃƒÂ¡Ã‚Â»Ã‚Ân ÃƒÆ’Ã‚Â­t nhÃƒÂ¡Ã‚ÂºÃ‚Â¥t mÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t LPN."));

        if (form.PlannedStartTime >= form.PlannedEndTime)
            return BadRequest(ApiResponse<object>.Failure("PlannedStartTime phÃƒÂ¡Ã‚ÂºÃ‚Â£i nhÃƒÂ¡Ã‚Â»Ã‚Â hÃƒâ€ Ã‚Â¡n PlannedEndTime."));

        if (string.IsNullOrWhiteSpace(form.ScheduleId)
            || !Guid.TryParse(ExtractGuid(form.ScheduleId), out var selectedScheduleId)
            || selectedScheduleId == Guid.Empty)
        {
            return BadRequest(ApiResponse<object>.Failure("ScheduleId is required."));
        }

        // TÃƒÆ’Ã‚Â i xÃƒÂ¡Ã‚ÂºÃ‚Â¿: 1ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“2 ngÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Âi, gÃƒÆ’Ã‚Â¡n theo chuyÃƒÂ¡Ã‚ÂºÃ‚Â¿n qua TripDriver
        var driverIds = (form.DriverIds ?? new List<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => ExtractGuid(d))
            .Where(d => Guid.TryParse(d, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

        if (driverIds.Count < 1 || driverIds.Count > 2)
            return BadRequest(ApiResponse<object>.Failure("Vui lÃƒÂ²ng chÃ¡Â»Â n 1 hoÃ¡ÂºÂ·c 2 tÃƒÂ i xÃ¡ÂºÂ¿ cho chuyÃ¡ÂºÂ¿n."));

        var parsedLpnIds = new List<Guid>();
        foreach (var lpnId in lpnIds)
        {
            if (!Guid.TryParse(ExtractGuid(lpnId), out var parsedLpnId) || parsedLpnId == Guid.Empty)
            {
                return BadRequest(ApiResponse<object>.Failure($"LpnId '{lpnId}' khÃ´ng há»£p lá»‡."));
            }

            parsedLpnIds.Add(parsedLpnId);
        }

        parsedLpnIds = parsedLpnIds.Distinct().ToList();

        var existingLpnIds = await _db.Lpns
            .Where(l => parsedLpnIds.Contains(l.LpnId))
            .Select(l => l.LpnId)
            .ToListAsync();

        var wrongScheduleLpnIds = await _db.Lpns
            .Include(l => l.Order)
            .Where(l => parsedLpnIds.Contains(l.LpnId)
                && (l.Order == null || l.Order.ScheduleId != selectedScheduleId))
            .Select(l => l.LpnId)
            .ToListAsync();

        var invalidLpnIds = parsedLpnIds
            .Except(existingLpnIds)
            .Concat(wrongScheduleLpnIds)
            .Distinct()
            .ToList();

        if (invalidLpnIds.Any())
        {
            return BadRequest(ApiResponse<object>.Failure(
                "Some LPNs do not belong to the selected schedule.",
                errors: new { invalidLpnIds }));
        }

        // Tá»± Ä‘á»™ng tÃ¬m kho xuáº¥t phÃ¡t tá»« LPN Ä‘áº§u tiÃªn
        var firstLpnId = parsedLpnIds.First();
        var firstLpn = await _db.Lpns.Include(l => l.Order).FirstOrDefaultAsync(l => l.LpnId == firstLpnId);
        if (firstLpn == null)
            return BadRequest(ApiResponse<object>.Failure($"KhÃ´ng tÃ¬m tháº¥y LPN {firstLpnId}."));

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
                return BadRequest(ApiResponse<object>.Failure("KhÃ´ng tÃ¬m tháº¥y vá»‹ trÃ­ kho xuáº¥t phÃ¡t nÃ o trong há»‡ thá»‘ng."));
            originLocId = fallbackLocId;
        }

        var parsedVehicleId = Guid.Parse(ExtractGuid(form.VehicleId));

        // Kiá»ƒm tra xe Ä‘Ã£ Ä‘Æ°á»£c gÃ¡n thiáº¿t bá»‹ IoT chÆ°a
        var hasIot = await _db.IotDevices.AnyAsync(d => d.VehicleId == parsedVehicleId);
        if (!hasIot)
            return BadRequest(ApiResponse<object>.Failure("Xe chÆ°a Ä‘Æ°á»£c gÃ¡n thiáº¿t bá»‹ IoT. Vui lÃ²ng gÃ¡n thiáº¿t bá»‹ IoT cho xe nÃ y trÆ°á»›c khi ghÃ©p chuyáº¿n."));

        var request = new ManualDispatchRequest
        {
            ScheduleId = selectedScheduleId,
            
            LpnIds = parsedLpnIds,
            VehicleId = parsedVehicleId,
            DriverIds = driverIds,
            OriginWarehouseLocationId = originLocId,
            PlannedStartTime          = form.PlannedStartTime,
            PlannedEndTime            = form.PlannedEndTime,
            ScreenshotBase64          = form.ScreenshotBase64
        };

        try
        {
            var result = await _dispatchService.ManualDispatchAsync(request);

            // Sinh file PDF LÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡nh Ãƒâ€žÃ¢â‚¬ËœiÃƒÂ¡Ã‚Â»Ã‚Âu Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ng + Load Plan
            var goongKey = Environment.GetEnvironmentVariable("key") ?? "xV6YBygCVRIQYybUrDAfaqYuuVfO9qvQBqQSA7uK";
            var html = ManifestTemplateBuilder.BuildHtml(result, goongKey);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var lpnQuery = string.Join(",", request.LpnIds);
            var lifoReportUrl = $"{baseUrl}/lifo-report.html?vehicleId={request.VehicleId}&lpnIds={lpnQuery}";
            var pdfUrl = await _pdfService.SavePdfFromUrlAsync(lifoReportUrl, result.TripId.ToString(), "lifo");
            result.LifoPdfUrl = pdfUrl;
            if (!string.IsNullOrEmpty(pdfUrl))
            {
                var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdStr, out var currentUserId))
                {
                    var adminUser = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Role != null && (u.Role.RoleName.ToUpper() == "ADMIN" || u.Role.RoleName.ToUpper() == "WAREHOUSEWORKER"));
                    currentUserId = adminUser?.UserId ?? Guid.Empty;
                }

                _db.TransportDocuments.Add(new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    DocType = "LIFO-PLAN",
                    ImageUrl = pdfUrl,
                    CreatedAt = DateTime.UtcNow,
                    UploadedBy = currentUserId
                });
                await _db.SaveChangesAsync();
            }
            return Ok(ApiResponse<ManualDispatchResult>.SuccessResponse(result, "GhÃ©p chuyáº¿n thÃ nh cÃ´ng!"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Failure(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<object>.Failure($"LÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i hÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡ thÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœng khi manual-dispatch: {ex.Message}"));
        }
    }

    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â
    //  API 1.1: LÃƒÂ¡Ã‚ÂºÃ‚Â¤Y LÃƒÂ¡Ã‚ÂºÃ‚Â I LINK SÃƒâ€ Ã‚Â  Ãƒâ€žÃ‚ÂÃƒÂ¡Ã‚Â»Ã¢â‚¬â„¢ LIFO PDF BÃƒÂ¡Ã‚ÂºÃ‚Â°NG TRIP ID
    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â

    /// <summary>
    /// API test tạo báo cáo LIFO PDF (Frontend Rendering)
    /// </summary>
    [HttpPost("test-lifo-pdf")]
    public IActionResult TestLifoPdf([FromBody] TestLifoPdfRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.VehicleId) || request.LpnIds == null || !request.LpnIds.Any())
            return BadRequest(new { Success = false, Error = "Vui long cung cap vehicleId va danh sach lpnIds." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var lpnQuery = string.Join(",", request.LpnIds);
        
        return Ok(new 
        {
            Success = true,
            ThreeDLink = $"{baseUrl}/3d-viewer.html?vehicleId={request.VehicleId}&lpnIds={lpnQuery}",
            PdfLink = $"{baseUrl}/lifo-report.html?vehicleId={request.VehicleId}&lpnIds={lpnQuery}"
        });
    }

    public class TestLifoPdfRequest
    {
        public string VehicleId { get; set; }
        public List<string> LpnIds { get; set; }
    }

    [HttpGet("trip/{tripId}/lifo-url")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetLifoUrl(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var id))
            return BadRequest(new { Success = false, Error = "TripId khÃ´ng há»£p lá»‡." });

        // TrÃƒÂ¡Ã‚ÂºÃ‚Â£ vÃƒÂ¡Ã‚Â»Ã‚Â trÃƒÂ¡Ã‚Â»Ã‚Â±c tiÃƒÂ¡Ã‚ÂºÃ‚Â¿p link Cloudinary cÃƒÆ’Ã‚Â³ chÃƒÂ¡Ã‚Â»Ã‚Â¯ kÃƒÆ’Ã‚Â½ (Signed URL) Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã†â€™ bypass lÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i 401
        var url = _fileService.GetSignedUrl($"coldchainx/lifo_{id}");
        return Ok(new { Success = true, LifoPdfUrl = url });
    }

    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â
    //  API 1.3: LÃƒÂ¡Ã‚ÂºÃ‚Â¤Y LÃƒÂ¡Ã‚ÂºÃ‚Â I LINK GIÃƒÂ¡Ã‚ÂºÃ‚Â¤Y Ãƒâ€žÃ‚ÂI Ãƒâ€žÃ‚ÂÃƒâ€ Ã‚Â¯ÃƒÂ¡Ã‚Â»Ã…â€œNG (E-WAYBILL) THEO TRIP ID
    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â

    [HttpGet("trip/{tripId}/waybill-url")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(object), 404)]
    public IActionResult GetWaybillUrl(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var id))
            return BadRequest(new { Success = false, Error = "TripId khÃ´ng há»£p lá»‡." });

        // GiÃƒÂ¡Ã‚ÂºÃ‚Â¥y Ãƒâ€žÃ¢â‚¬Ëœi Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Âng lÃƒâ€ Ã‚Â°u vÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºi prefix "waybill_" (tÃƒÆ’Ã‚Â¡ch biÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t hoÃƒÆ’Ã‚Â n toÃƒÆ’Ã‚Â n vÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºi sÃƒâ€ Ã‚Â¡ Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â‚¬Å“ LIFO "lifo_")
        var url = _fileService.GetSignedUrl($"coldchainx/waybill_{id}");
        return Ok(new { Success = true, WaybillPdfUrl = url });
    }

    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â
    //  API 1.2: LÃƒÂ¡Ã‚ÂºÃ‚Â¤Y LÃƒÂ¡Ã‚ÂºÃ‚Â I BÃƒÂ¡Ã‚ÂºÃ‚Â¢N Ãƒâ€žÃ‚ÂÃƒÂ¡Ã‚Â»Ã¢â‚¬â„¢ DÃƒÂ¡Ã‚ÂºÃ‚ÂªN Ãƒâ€žÃ‚ÂÃƒâ€ Ã‚Â¯ÃƒÂ¡Ã‚Â»Ã…â€œNG (GOONG) THEO TRIP ID
    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â

    [HttpGet("trip/{tripId}/route")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TripRouteResponse), 200)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetTripRoute(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var id))
            return BadRequest(new { Success = false, Error = "TripId khÃ´ng há»£p lá»‡." });

        var trip = await _db.MasterTrips
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Location)
            .FirstOrDefaultAsync(t => t.TripId == id);

        if (trip == null)
            return NotFound(new { Success = false, Error = "KhÃ´ng tÃ¬m tháº¥y chuyáº¿n Ä‘i." });

        var actualDestinationLocation = trip.DestinationLocation;
        if (trip.TripStops != null && trip.TripStops.Any())
        {
            var lastStop = trip.TripStops.OrderBy(s => s.StopSequence).LastOrDefault(s => s.Location != null);
            if (lastStop != null && lastStop.Location != null)
            {
                actualDestinationLocation = lastStop.Location;
            }
        }

        var deliveryStops = trip.TripStops
            .Where(stop => stop.Location != null)
            .Where(stop => !IsSameLocation(stop.Location!, trip.OriginLocation)
                && !IsSameLocation(stop.Location!, actualDestinationLocation))
            .OrderBy(stop => stop.StopSequence)
            .ToList();

        var origin = GoongMapService.FormatCoordinate(
            trip.OriginLocation.Latitude,
            trip.OriginLocation.Longitude);
        var destination = GoongMapService.FormatCoordinate(
            actualDestinationLocation.Latitude,
            actualDestinationLocation.Longitude);
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
            return StatusCode(500, new { Success = false, Error = "LÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i khi gÃƒÂ¡Ã‚Â»Ã‚Âi Goong API tÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœi Ãƒâ€ Ã‚Â°u lÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ trÃƒÆ’Ã‚Â¬nh.", Detail = ex.Message });
        }
    }

    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â
    //  API 2: START PICKING ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â BÃƒÂ¡Ã‚ÂºÃ‚Â¯t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§u lÃƒÂ¡Ã‚ÂºÃ‚Â¥y hÃƒÆ’Ã‚Â ng tÃƒÂ¡Ã‚Â»Ã‚Â« kho
    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â

    /// <summary>
    /// Lấy danh sách chuyến đi cùng đầy đủ xe, tài xế, tuyến, điểm dừng, LPN,
    /// đơn hàng, khách hàng, kho, chứng từ, ePOD, seal và sự cố.
    /// Orders và LPNs được sắp theo thứ tự xếp hàng LIFO.
    /// </summary>
    /// <remarks>
    /// Có thể lọc theo status và tìm theo trip ID, biển số xe, mã tuyến,
    /// mã đơn hàng hoặc mã LPN. pageSize tối đa là 100 vì mỗi chuyến chứa
    /// toàn bộ dữ liệu vận hành liên quan.
    /// </remarks>
    [HttpGet("trips")]
    [Authorize(Roles = "Admin,ADMIN,WarehouseWorker,WAREHOUSEWORKER,Dispatcher,DISPATCHER")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TripDetailsDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllTrips(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var safePageNumber = Math.Max(pageNumber, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.MasterTrips.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            query = query.Where(trip => trip.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            var hasTripId = Guid.TryParse(keyword, out var searchedTripId);

            query = query.Where(trip =>
                (hasTripId && trip.TripId == searchedTripId)
                || (trip.Vehicle != null && trip.Vehicle.TruckPlate.Contains(keyword))
                || (trip.Route != null && trip.Route.RouteCode.Contains(keyword))
                || trip.TransportOrders.Any(order => order.TrackingCode.Contains(keyword))
                || _db.Lpns.Any(lpn => lpn.TripId == trip.TripId && lpn.LpnCode.Contains(keyword)));
        }

        var totalRecords = await query.CountAsync(cancellationToken);
        var tripIds = await query
            .OrderByDescending(trip => trip.CreatedAt)
            .ThenByDescending(trip => trip.PlannedStartTime)
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .Select(trip => trip.TripId)
            .ToListAsync(cancellationToken);

        var trips = await BuildTripDetailsAsync(tripIds, cancellationToken);
        var pagedResult = PagedResult<TripDetailsDto>.Create(
            trips,
            totalRecords,
            safePageNumber,
            safePageSize);

        return Ok(ApiResponse<PagedResult<TripDetailsDto>>.SuccessResponse(
            pagedResult,
            "Lấy danh sách chuyến đi thành công."));
    }

    /// <summary>
    /// Lấy đầy đủ thông tin một chuyến đi theo ID, bao gồm toàn bộ LPN và đơn hàng
    /// đã được sắp theo thứ tự xếp hàng LIFO.
    /// </summary>
    [HttpGet("trips/{id:guid}")]
    [Authorize(Roles = "Admin,ADMIN,WarehouseWorker,WAREHOUSEWORKER,Dispatcher,DISPATCHER")]
    [ProducesResponseType(typeof(ApiResponse<TripDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TripDetailsDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTripById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var trips = await BuildTripDetailsAsync(new[] { id }, cancellationToken);
        var trip = trips.SingleOrDefault();

        if (trip == null)
        {
            return NotFound(ApiResponse<TripDetailsDto>.Failure(
                $"Không tìm thấy chuyến đi với ID '{id}'.",
                StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse<TripDetailsDto>.SuccessResponse(
            trip,
            "Lấy chi tiết chuyến đi thành công."));
    }

    /// <summary>
    /// [STEP 2/5 - LOOKUP] Danh sách chuyến ĐÃ ĐƯỢC GHÉP và sẵn sàng bốc hàng (Status = PLANNED).
    /// </summary>
    /// <remarks>
    /// DÃ¹ng Ä‘á»ƒ FE biáº¿t nÃªn nháº­p tripId nÃ o vÃ o POST /api/Dispatch/trip/{tripId}/start-picking.
    /// ChÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° trÃƒÂ¡Ã‚ÂºÃ‚Â£ vÃƒÂ¡Ã‚Â»Ã‚Â cÃƒÆ’Ã‚Â¡c chuyÃƒÂ¡Ã‚ÂºÃ‚Â¿n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ ghÃƒÆ’Ã‚Â©p (PLANNED) ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â tÃƒÂ¡Ã‚Â»Ã‚Â©c Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ qua bÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc manual-dispatch.
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
    /// [STEP 2/5] Bắt đầu lệnh bốc hàng để chuyển LPN từ ALLOCATED sang LOADING.
    /// </summary>
    /// <remarks>
    /// LPN state: ALLOCATED â†’ LOADING
    /// Trip status: PLANNED â†’ PICKING
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
            return BadRequest(new { Success = false, Error = "TripId khÃ´ng há»£p lá»‡." });

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
            return StatusCode(500, new { Success = false, Error = "LÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i hÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡ thÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœng khi bÃƒÂ¡Ã‚ÂºÃ‚Â¯t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§u picking.", Detail = ex.Message });
        }
    }

    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â
    //  API 2.1: CANCEL TRIP ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â HÃƒÂ¡Ã‚Â»Ã‚Â§y chuyÃƒÂ¡Ã‚ÂºÃ‚Â¿n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ ghÃƒÆ’Ã‚Â©p, reset toÃƒÆ’Ã‚Â n bÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ vÃƒÂ¡Ã‚Â»Ã‚Â free
    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â

    /// <summary>
    /// Hủy một chuyến đang ghép (planning) để kết thúc việc bốc hàng / xếp hàng / kẹp chì.
    /// </summary>
    /// <remarks>
    /// Ãƒâ€žÃ‚ÂIÃƒÂ¡Ã‚Â»Ã¢â€šÂ¬U KIÃƒÂ¡Ã‚Â»Ã¢â‚¬Â N: khÃƒÆ’Ã‚Â´ng Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c cÃƒÆ’Ã‚Â³ LPN nÃƒÆ’Ã‚Â o ÃƒÂ¡Ã‚Â»Ã…Â¸ trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i SHIPPING (hÃƒÆ’Ã‚Â ng Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ xuÃƒÂ¡Ã‚ÂºÃ‚Â¥t phÃƒÆ’Ã‚Â¡t).
    ///
    /// Sau khi hÃƒÂ¡Ã‚Â»Ã‚Â§y, toÃƒÆ’Ã‚Â n bÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ trÃƒÂ¡Ã‚Â»Ã…Â¸ vÃƒÂ¡Ã‚Â»Ã‚Â nhÃƒâ€ Ã‚Â° trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc khi gÃƒÂ¡Ã‚Â»Ã‚Âi manual-dispatch:
    ///   - LPN.State â†’ IN_STOCK (hÃ ng trá»Ÿ láº¡i kho), gá»¡ TripId
    ///   - Ãƒâ€žÃ‚ÂÃƒâ€ Ã‚Â¡n hÃƒÆ’Ã‚Â ng ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ IN_STOCK, gÃƒÂ¡Ã‚Â»Ã‚Â¡ MasterTripId
    ///   - Seal â†’ CANCELLED, gá»¡ SealNumber
    ///   - E-Waybill (TransportDocument) â†’ CANCELLED
    ///   - Vehicle.Status / Driver.Status â†’ ACTIVE (giáº£i phÃ³ng)
    ///   - Trip.Status â†’ CANCELLED
    ///
    /// Xe sau khi há»§y cÃ³ thá»ƒ Ä‘Æ°á»£c ghÃ©p chuyáº¿n má»›i qua POST /api/Dispatch/manual-dispatch.
    /// </remarks>
    /// <param name="tripId">ID chuyáº¿n cáº§n há»§y</param>
    [HttpPost("trip/{tripId}/cancel")]
    [ProducesResponseType(typeof(CancelTripResult), 200)]
    public async Task<IActionResult> CancelTrip(string tripId)
    {
        var rawId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId khÃ´ng há»£p lá»‡." });

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
            return StatusCode(500, new { Success = false, Error = "LÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i hÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡ thÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœng khi hÃƒÂ¡Ã‚Â»Ã‚Â§y chuyÃƒÂ¡Ã‚ÂºÃ‚Â¿n.", Detail = ex.Message });
        }
    }

    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â
    //  API 3: IOT CHECK ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â KiÃƒÂ¡Ã‚Â»Ã†â€™m tra tÃƒÆ’Ã‚Â­n hiÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡u IoT xe
    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â

    /// <summary>
    /// Kiểm tra trạng thái kết nối, GPS, nhiệt độ, pin của các thiết bị IoT gắn trên xe.
    /// </summary>
    [HttpPost("vehicle-iot-check/{tripId}")]
    [ProducesResponseType(typeof(VehicleIoTStatus), 200)]
    public async Task<IActionResult> CheckVehicleIoT(string tripId)
    {
        var rawTripId = ExtractGuid(tripId);
        if (!Guid.TryParse(rawTripId, out var parsedTripId))
            return BadRequest(new { Success = false, Error = "TripId khÃ´ng há»£p lá»‡." });

        var trip = await _db.MasterTrips.FirstOrDefaultAsync(t => t.TripId == parsedTripId);
        if (trip == null)
            return BadRequest(new { Success = false, Error = "KhÃ´ng tÃ¬m tháº¥y chuyáº¿n Ä‘i." });

        if (trip.VehicleId == null)
            return BadRequest(new { Success = false, Error = "Chuyáº¿n Ä‘i nÃ y chÆ°a Ä‘Æ°á»£c gÃ¡n xe." });
            
        var parsedVehicleId = trip.VehicleId.Value;

        var hasIot = await _db.IotDevices.AnyAsync(d => d.VehicleId == parsedVehicleId);
        if (!hasIot)
            return BadRequest(new { Success = false, Error = "Xe cháº¡y chuyáº¿n nÃ y chÆ°a Ä‘Æ°á»£c gÃ¡n thiáº¿t bá»‹ IoT." });

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

    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â
    //  API 4: SEAL & DISPATCH ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â KÃƒÂ¡Ã‚ÂºÃ‚Â¹p chÃƒÆ’Ã‚Â¬ + kiÃƒÂ¡Ã‚Â»Ã†â€™m tra chÃƒÂ¡Ã‚ÂºÃ‚Â¥t hÃƒÆ’Ã‚Â ng
    // ÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚ÂÃƒÂ¢Ã¢â‚¬Â¢Ã‚Â

    /// <summary>
    /// [STEP 5/5 - LOOKUP] Danh sách chuyến đã xếp xong và ĐỦ ĐIỀU KIỆN kẹp chì.
    /// </summary>
    /// <remarks>
    /// TrÃƒÂ¡Ã‚ÂºÃ‚Â£ vÃƒÂ¡Ã‚Â»Ã‚Â cÃƒÆ’Ã‚Â¡c chuyÃƒÂ¡Ã‚ÂºÃ‚Â¿n cÃƒÆ’Ã‚Â³ Status = LOADING_COMPLETED vÃƒÆ’Ã‚Â  TÃƒÂ¡Ã‚ÂºÃ‚Â¤T CÃƒÂ¡Ã‚ÂºÃ‚Â¢ LPN Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ ÃƒÂ¡Ã‚Â»Ã…Â¸ trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i RELEASED.
    /// DÃ¹ng Ä‘á»ƒ FE biáº¿t nÃªn nháº­p tripId nÃ o vÃ o POST /api/Dispatch/seal-and-dispatch/{tripId}.
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
    /// LPN state: RELEASED â†’ SHIPPING
    /// Trip status: LOADING_COMPLETED â†’ SEALED â†’ DISPATCHED
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
            return BadRequest(new { Success = false, Error = "TripId khÃ´ng há»£p lá»‡." });

        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SealCode))
            return BadRequest(new { Success = false, Error = "SealCode lÃ  báº¯t buá»™c." });

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
            return StatusCode(500, new { Success = false, Error = "LÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i hÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡ thÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœng khi kÃƒÂ¡Ã‚ÂºÃ‚Â¹p chÃƒÆ’Ã‚Â¬.", Detail = ex.Message });
        }
    }

    private async Task<List<TripDetailsDto>> BuildTripDetailsAsync(
        IReadOnlyCollection<Guid> requestedTripIds,
        CancellationToken cancellationToken)
    {
        if (requestedTripIds.Count == 0)
        {
            return new List<TripDetailsDto>();
        }

        var tripIds = requestedTripIds.Distinct().ToList();
        var trips = await _db.MasterTrips
            .AsNoTracking()
            .Where(trip => tripIds.Contains(trip.TripId))
            .Include(trip => trip.Route)
            .Include(trip => trip.Schedule)
            .Include(trip => trip.OriginLocation)
            .Include(trip => trip.DestinationLocation)
            .Include(trip => trip.Vehicle)
                .ThenInclude(vehicle => vehicle!.IotDevices)
            .Include(trip => trip.Vehicle)
                .ThenInclude(vehicle => vehicle!.VehicleDocuments)
            .Include(trip => trip.TripDrivers)
                .ThenInclude(tripDriver => tripDriver.Driver)
                    .ThenInclude(driver => driver.DriverLicenses)
            .Include(trip => trip.TripStops)
                .ThenInclude(stop => stop.Location)
            .Include(trip => trip.Seals)
            .Include(trip => trip.IncidentReports)
                .ThenInclude(incident => incident.IncidentEvidences)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        if (trips.Count == 0)
        {
            return new List<TripDetailsDto>();
        }

        var lpns = await _db.Lpns
            .AsNoTracking()
            .Where(lpn => lpn.TripId.HasValue && tripIds.Contains(lpn.TripId.Value))
            .Include(lpn => lpn.Order)
                .ThenInclude(order => order.Customer)
            .Include(lpn => lpn.Customer)
            .Include(lpn => lpn.Warehouse)
            .Include(lpn => lpn.Receipt)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var orders = await _db.TransportOrders
            .AsNoTracking()
            .Where(order =>
                (order.MasterTripId.HasValue && tripIds.Contains(order.MasterTripId.Value))
                || _db.Lpns.Any(lpn =>
                    lpn.OrderId == order.OrderId
                    && lpn.TripId.HasValue
                    && tripIds.Contains(lpn.TripId.Value)))
            .Include(order => order.Customer)
            .Include(order => order.PickupLocationNavigation)
            .Include(order => order.DestLocationNavigation)
            .Include(order => order.Schedule)
                .ThenInclude(schedule => schedule!.Route)
            .Include(order => order.OrderDimension)
            .Include(order => order.TransportDocuments)
            .Include(order => order.DeliveryEpods)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var confirmations = await _db.LpnDeliveryConfirmations
            .AsNoTracking()
            .Where(confirmation => tripIds.Contains(confirmation.TripId))
            .ToListAsync(cancellationToken);

        var confirmationsByLpn = confirmations
            .GroupBy(confirmation => confirmation.LpnId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.ConfirmedAt).First());
        var lpnsByTrip = lpns
            .Where(lpn => lpn.TripId.HasValue)
            .ToLookup(lpn => lpn.TripId!.Value);
        var tripById = trips.ToDictionary(trip => trip.TripId);
        var result = new List<TripDetailsDto>(trips.Count);

        foreach (var requestedTripId in requestedTripIds)
        {
            if (!tripById.TryGetValue(requestedTripId, out var trip))
            {
                continue;
            }

            var tripLpns = lpnsByTrip[trip.TripId].ToList();
            var tripStops = trip.TripStops.ToList();
            var lpnOrderIds = tripLpns
                .Select(lpn => lpn.OrderId)
                .ToHashSet();
            var tripOrders = orders
                .Where(order => order.MasterTripId == trip.TripId || lpnOrderIds.Contains(order.OrderId))
                .GroupBy(order => order.OrderId)
                .Select(group => group.First())
                .ToList();
            var orderIds = tripOrders
                .Select(order => order.OrderId)
                .ToHashSet();

            var loadPlan = BuildTripLifoLoadPlan(tripLpns, tripStops, tripOrders);
            var loadPlanByLpn = loadPlan.ToDictionary(item => item.LpnId);
            var orderedTripLpns = tripLpns
                .OrderBy(lpn => loadPlanByLpn.TryGetValue(lpn.LpnId, out var item)
                    ? item.LoadOrder
                    : int.MaxValue)
                .ThenBy(lpn => lpn.LpnCode)
                .ToList();

            var orderDtos = tripOrders
                .Select(order => ToTripOrderDetailsDto(
                    order,
                    orderedTripLpns.Where(lpn => lpn.OrderId == order.OrderId).ToList(),
                    loadPlan.Where(item => item.OrderId == order.OrderId).ToList(),
                    tripStops))
                .OrderBy(order => order.FirstLifoLoadOrder ?? int.MaxValue)
                .ThenBy(order => order.TrackingCode)
                .ToList();
            var lpnDtos = orderedTripLpns
                .Select(lpn => ToTripLpnDetailsDto(
                    lpn,
                    confirmationsByLpn.GetValueOrDefault(lpn.LpnId),
                    loadPlanByLpn.GetValueOrDefault(lpn.LpnId)))
                .ToList();

            var stopDtos = tripStops
                .OrderBy(stop => stop.StopSequence)
                .Select(stop =>
                {
                    var stopOrders = tripOrders
                        .Where(order =>
                            order.DropoffStopId == stop.StopId
                            || (!order.DropoffStopId.HasValue
                                && stop.LocationId.HasValue
                                && order.DestLocation == stop.LocationId))
                        .OrderBy(order => loadPlan
                            .Where(item => item.OrderId == order.OrderId)
                            .Select(item => (int?)item.LoadOrder)
                            .Min() ?? int.MaxValue)
                        .ThenBy(order => order.TrackingCode)
                        .ToList();
                    var stopOrderIds = stopOrders
                        .Select(order => order.OrderId)
                        .ToHashSet();
                    var stopLpns = orderedTripLpns
                        .Where(lpn => stopOrderIds.Contains(lpn.OrderId))
                        .ToList();

                    return new TripStopDetailsDto
                    {
                        StopId = stop.StopId,
                        LocationId = stop.LocationId,
                        StopSequence = stop.StopSequence,
                        StopType = stop.StopType,
                        PlannedArrivalTime = stop.PlannedArrivalTime,
                        PlannedDepartureTime = stop.PlannedDepartureTime,
                        ActualArrivalTime = stop.ActualArrivalTime,
                        ActualDepartureTime = stop.ActualDepartureTime,
                        Status = stop.Status,
                        Note = stop.Note,
                        Location = stop.Location == null ? null : ToTripLocationDetailsDto(stop.Location),
                        OrderIds = stopOrders.Select(order => order.OrderId).ToList(),
                        OrderTrackingCodes = stopOrders.Select(order => order.TrackingCode).ToList(),
                        LpnIds = stopLpns.Select(lpn => lpn.LpnId).ToList(),
                        LpnCodes = stopLpns.Select(lpn => lpn.LpnCode).ToList()
                    };
                })
                .ToList();

            result.Add(new TripDetailsDto
            {
                TripId = trip.TripId,
                RouteId = trip.RouteId,
                ScheduleId = trip.ScheduleId,
                DepartureDate = trip.DepartureDate,
                VehicleId = trip.VehicleId,
                OriginLocationId = trip.OriginLocationId,
                DestinationLocationId = trip.DestinationLocationId,
                SealNumber = trip.SealNumber,
                TotalDistanceKm = trip.TotalDistanceKm,
                EstimatedDurationHours = trip.EstimatedDurationHours,
                TargetTemperature = trip.TargetTemperature,
                RequiresInspection = trip.RequiresInspection,
                PlannedStartTime = trip.PlannedStartTime,
                PlannedEndTime = trip.PlannedEndTime,
                StartedAt = trip.StartedAt,
                CompletedAt = trip.CompletedAt,
                Status = trip.Status,
                CreatedAt = trip.CreatedAt,
                Summary = new TripCargoSummaryDto
                {
                    TotalOrders = orderIds.Count,
                    TotalLpns = tripLpns.Count,
                    TotalQuantity = tripLpns.Sum(lpn => lpn.Quantity),
                    TotalWeightKg = tripLpns.Sum(lpn => lpn.ActualWeightKg),
                    TotalCbm = tripLpns.Sum(lpn => lpn.ActualCbm),
                    DeliveredLpns = tripLpns.Count(lpn => lpn.State == LpnState.DELIVERED),
                    ReturnedLpns = tripLpns.Count(lpn => lpn.State == LpnState.DELIVERY_RETURNED),
                    LpnStateCounts = tripLpns
                        .GroupBy(lpn => lpn.State.ToString())
                        .ToDictionary(group => group.Key, group => group.Count())
                },
                Route = trip.Route == null ? null : ToTripRouteDetailsDto(trip.Route),
                Schedule = trip.Schedule == null ? null : ToTripScheduleDetailsDto(trip.Schedule),
                Origin = ToTripLocationDetailsDto(trip.OriginLocation),
                Destination = ToTripLocationDetailsDto(trip.DestinationLocation),
                Vehicle = trip.Vehicle == null ? null : ToTripVehicleDetailsDto(trip.Vehicle),
                Drivers = trip.TripDrivers
                    .OrderBy(tripDriver => tripDriver.DriverRole == "PRIMARY" ? 0 : 1)
                    .ThenBy(tripDriver => tripDriver.CreatedAt)
                    .Select(ToTripDriverDetailsDto)
                    .ToList(),
                Stops = stopDtos,
                LoadPlan = loadPlan,
                Orders = orderDtos,
                Lpns = lpnDtos,
                Seals = trip.Seals
                    .OrderBy(seal => seal.AppliedAt ?? seal.CreatedAt)
                    .Select(ToTripSealDetailsDto)
                    .ToList(),
                Incidents = trip.IncidentReports
                    .OrderByDescending(incident => incident.ReportedAt)
                    .Select(ToTripIncidentDetailsDto)
                    .ToList()
            });
        }

        return result;
    }

    private static List<TripLifoLoadItemDto> BuildTripLifoLoadPlan(
        IReadOnlyCollection<Lpn> lpns,
        IReadOnlyCollection<TripStop> stops,
        IReadOnlyCollection<TransportOrder> orders)
    {
        var stopSequenceByLocation = stops
            .Where(stop => stop.LocationId.HasValue)
            .GroupBy(stop => stop.LocationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Min(stop => stop.StopSequence));
        var stopAddressByLocation = stops
            .Where(stop => stop.LocationId.HasValue && stop.Location != null)
            .GroupBy(stop => stop.LocationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.First().Location!.Address);
        var orderById = orders.ToDictionary(order => order.OrderId);

        var sortedLpns = lpns
            .Where(lpn => lpn.Order?.DestLocation.HasValue == true)
            .Select(lpn =>
            {
                var deliveryLocationId = lpn.Order.DestLocation!.Value;
                var stopSequence = stopSequenceByLocation.GetValueOrDefault(deliveryLocationId, 999);
                var zone = ClassifyLifoZone(lpn.Order.TempCondition);

                return new
                {
                    Lpn = lpn,
                    DeliveryLocationId = deliveryLocationId,
                    StopSequence = stopSequence,
                    Zone = zone,
                    ZoneOrder = GetLifoZoneOrder(zone)
                };
            })
            .OrderByDescending(item => item.StopSequence)
            .ThenByDescending(item => item.Lpn.ActualWeightKg)
            .ThenBy(item => item.ZoneOrder)
            .ThenBy(item => item.Lpn.LpnCode)
            .ToList();

        var result = new List<TripLifoLoadItemDto>(sortedLpns.Count);
        for (var index = 0; index < sortedLpns.Count; index++)
        {
            var item = sortedLpns[index];
            var lpn = item.Lpn;
            var order = lpn.Order;
            orderById.TryGetValue(order.OrderId, out var fullOrder);

            var deliveryAddress = stopAddressByLocation.GetValueOrDefault(item.DeliveryLocationId)
                ?? fullOrder?.DestLocationNavigation?.Address
                ?? string.Empty;

            result.Add(new TripLifoLoadItemDto
            {
                LoadOrder = index + 1,
                LpnId = lpn.LpnId,
                LpnCode = lpn.LpnCode,
                OrderId = order.OrderId,
                TrackingCode = order.TrackingCode,
                ItemName = order.ItemName,
                WeightKg = lpn.ActualWeightKg,
                Cbm = lpn.ActualCbm,
                TempCondition = order.TempCondition,
                Zone = item.Zone,
                DeliveryLocationId = item.DeliveryLocationId,
                DeliveryAddress = deliveryAddress,
                DeliveryStopSequence = item.StopSequence,
                Reason = BuildLifoReason(
                    item.StopSequence,
                    stops.Count,
                    item.Zone,
                    lpn.ActualWeightKg)
            });
        }

        return result;
    }

    private static string ClassifyLifoZone(string? temperatureCondition)
    {
        var condition = (temperatureCondition ?? string.Empty).ToUpperInvariant().Trim();
        if (condition.Contains("FROZEN") || condition.StartsWith("-") || condition.Contains("-18"))
        {
            return "REAR";
        }

        if (condition.Contains("CHILLED") || condition.Contains("2-8") || condition.Contains("0-4"))
        {
            return "MID";
        }

        return "FRONT";
    }

    private static int GetLifoZoneOrder(string zone)
    {
        return zone switch
        {
            "REAR" => 0,
            "MID" => 1,
            "FRONT" => 2,
            _ => 3
        };
    }

    private static string BuildLifoReason(
        int stopSequence,
        int totalStops,
        string zone,
        decimal weightKg)
    {
        var parts = new List<string>();

        if (stopSequence == totalStops)
        {
            parts.Add("Điểm giao cuối lộ trình → xếp sâu vào đuôi xe (LIFO)");
        }
        else if (stopSequence == 1)
        {
            parts.Add("Điểm giao đầu tiên → xếp gần cửa xe");
        }
        else
        {
            parts.Add($"Giao tại điểm #{stopSequence}/{totalStops}");
        }

        parts.Add(zone switch
        {
            "REAR" => "Hàng đông lạnh → ngăn REAR",
            "MID" => "Hàng mát → ngăn MID",
            _ => "Hàng nhiệt độ thường → ngăn FRONT"
        });
        parts.Add($"Khối lượng {weightKg:F1}kg → hàng nặng xếp dưới");

        return string.Join("; ", parts);
    }

    private static TripLocationDetailsDto ToTripLocationDetailsDto(Location location)
    {
        return new TripLocationDetailsDto
        {
            LocationId = location.LocationId,
            CustomerId = location.CustomerId,
            Address = location.Address,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Status = location.Status
        };
    }

    private static TripRouteDetailsDto ToTripRouteDetailsDto(RouteMaster route)
    {
        return new TripRouteDetailsDto
        {
            RouteId = route.RouteId,
            RouteCode = route.RouteCode,
            OriginCity = route.OriginCity,
            DestinationCity = route.DestCity,
            TransitTime = route.TransitTime,
            Status = route.Status
        };
    }

    private static TripScheduleDetailsDto ToTripScheduleDetailsDto(RouteSchedule schedule)
    {
        return new TripScheduleDetailsDto
        {
            ScheduleId = schedule.ScheduleId,
            RouteId = schedule.RouteId,
            ScheduleName = schedule.ScheduleName,
            DepartureDate = schedule.DepartureDate,
            DepartureTime = schedule.DepartureTime,
            CutOffTime = schedule.CutOffTime,
            Status = schedule.Status
        };
    }

    private static TripVehicleDetailsDto ToTripVehicleDetailsDto(Vehicle vehicle)
    {
        return new TripVehicleDetailsDto
        {
            VehicleId = vehicle.VehicleId,
            TruckPlate = vehicle.TruckPlate,
            Brand = vehicle.Brand,
            ManufactureYear = vehicle.ManufactureYear,
            ChassisNumber = vehicle.ChassisNumber,
            EngineNumber = vehicle.EngineNumber,
            StandardFuelLiters = vehicle.StandardFuelLiters,
            VehicleType = vehicle.VehicleType,
            MaxWeight = vehicle.MaxWeight,
            MaxCbm = vehicle.MaxCbm,
            InnerLengthCm = vehicle.InnerLengthCm,
            InnerWidthCm = vehicle.InnerWidthCm,
            InnerHeightCm = vehicle.InnerHeightCm,
            MinTemp = vehicle.MinTemp,
            MaxTemp = vehicle.MaxTemp,
            CurrentLocation = vehicle.CurrentLocation,
            CurrentOdometer = vehicle.CurrentOdometer,
            NextMaintenanceOdometer = vehicle.NextMaintenanceOdometer,
            NextMaintenanceDate = vehicle.NextMaintenanceDate,
            Status = vehicle.Status,
            IotDevices = vehicle.IotDevices
                .OrderBy(device => device.DeviceCode)
                .Select(device => new TripIotDeviceDto
                {
                    DeviceId = device.DeviceId,
                    DeviceCode = device.DeviceCode,
                    BatteryLevel = device.BatteryLevel,
                    LastPingTime = device.LastPingTime,
                    IsOnline = device.IsOnline,
                    Status = device.Status
                })
                .ToList(),
            Documents = vehicle.VehicleDocuments
                .OrderBy(document => document.DocumentType)
                .Select(document => new TripVehicleDocumentDto
                {
                    DocumentId = document.DocId,
                    DocumentType = document.DocumentType,
                    DocumentNumber = document.DocumentNumber,
                    Issuer = document.Issuer,
                    IssueDate = document.IssueDate,
                    ExpireDate = document.ExpireDate,
                    Status = document.Status
                })
                .ToList()
        };
    }

    private static TripDriverDetailsDto ToTripDriverDetailsDto(TripDriver tripDriver)
    {
        var driver = tripDriver.Driver;
        return new TripDriverDetailsDto
        {
            TripDriverId = tripDriver.TripDriverId,
            DriverId = tripDriver.DriverId,
            UserId = driver.UserId,
            DriverRole = tripDriver.DriverRole,
            AssignedDurationHours = tripDriver.AssignedDurationHours,
            FullName = driver.FullName,
            IdentityNumber = driver.IdentityNumber,
            PhoneNumber = driver.PhoneNumber,
            DateOfBirth = driver.DateOfBirth,
            JoinDate = driver.JoinDate,
            CurrentLocation = driver.CurrentLocation,
            Status = driver.Status,
            Licenses = driver.DriverLicenses
                .OrderByDescending(license => license.ExpiryDate)
                .Select(license => new TripDriverLicenseDto
                {
                    LicenseId = license.LicenseId,
                    LicenseNumber = license.LicenseNumber,
                    LicenseClass = license.LicenseClass,
                    IssueDate = license.IssueDate,
                    ExpiryDate = license.ExpiryDate,
                    Status = license.Status
                })
                .ToList()
        };
    }

    private static TripOrderDetailsDto ToTripOrderDetailsDto(
        TransportOrder order,
        IReadOnlyCollection<Lpn> orderLpns,
        IReadOnlyCollection<TripLifoLoadItemDto> orderLoadItems,
        IReadOnlyCollection<TripStop> tripStops)
    {
        var orderedLoadItems = orderLoadItems
            .OrderBy(item => item.LoadOrder)
            .ToList();
        var deliveryStopSequence = orderedLoadItems
            .Select(item => (int?)item.DeliveryStopSequence)
            .FirstOrDefault();

        if (!deliveryStopSequence.HasValue && order.DestLocation.HasValue)
        {
            deliveryStopSequence = tripStops
                .Where(stop => stop.LocationId == order.DestLocation)
                .Select(stop => (int?)stop.StopSequence)
                .Min();
        }

        return new TripOrderDetailsDto
        {
            OrderId = order.OrderId,
            TrackingCode = order.TrackingCode,
            CustomerId = order.CustomerId,
            ItemName = order.ItemName,
            Category = order.Category,
            Quantity = order.Quantity,
            PackingType = order.PackingType,
            TempCondition = order.TempCondition,
            HasStrongOdor = order.HasStrongOdor,
            IsStackable = order.IsStackable,
            PickupLocationId = order.PickupLocation,
            DestinationLocationId = order.DestLocation,
            MasterTripId = order.MasterTripId,
            ScheduleId = order.ScheduleId,
            DropoffStopId = order.DropoffStopId,
            PickupAddress = order.PickupLocationNavigation?.Address,
            DestinationAddress = order.DestLocationNavigation?.Address
                ?? tripStops
                    .FirstOrDefault(stop => stop.LocationId == order.DestLocation)
                    ?.Location
                    ?.Address,
            DeliveryStopSequence = deliveryStopSequence,
            FirstLifoLoadOrder = orderedLoadItems
                .Select(item => (int?)item.LoadOrder)
                .FirstOrDefault(),
            LifoLoadOrders = orderedLoadItems
                .Select(item => item.LoadOrder)
                .ToList(),
            LifoZone = orderedLoadItems
                .Select(item => item.Zone)
                .FirstOrDefault(),
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            Customer = order.Customer == null ? null : ToTripCustomerDetailsDto(order.Customer),
            PickupLocation = order.PickupLocationNavigation == null
                ? null
                : ToTripLocationDetailsDto(order.PickupLocationNavigation),
            DestinationLocation = order.DestLocationNavigation == null
                ? null
                : ToTripLocationDetailsDto(order.DestLocationNavigation),
            Schedule = order.Schedule == null ? null : ToTripScheduleDetailsDto(order.Schedule),
            Dimension = order.OrderDimension == null
                ? null
                : new TripOrderDimensionDto
                {
                    ExpectedWeightKg = order.OrderDimension.ExpectedWeightKg,
                    ActualWeightKg = order.OrderDimension.ActualWeightKg,
                    ExpectedCbm = order.OrderDimension.ExpectedCbm,
                    ActualCbm = order.OrderDimension.ActualCbm,
                    LengthCm = order.OrderDimension.LengthCm,
                    WidthCm = order.OrderDimension.WidthCm,
                    HeightCm = order.OrderDimension.HeightCm
                },
            LpnIds = orderLpns.Select(lpn => lpn.LpnId).ToList(),
            LpnCodes = orderLpns.Select(lpn => lpn.LpnCode).ToList(),
            Documents = order.TransportDocuments
                .OrderByDescending(document => document.CreatedAt)
                .Select(document => new TripTransportDocumentDto
                {
                    DocumentId = document.DocId,
                    DocumentType = document.DocType,
                    ImageUrl = document.ImageUrl,
                    VerifiedBy = document.VerifiedBy,
                    RejectReason = document.RejectReason,
                    UploadedBy = document.UploadedBy,
                    CreatedAt = document.CreatedAt
                })
                .ToList(),
            DeliveryEpods = order.DeliveryEpods
                .OrderByDescending(epod => epod.CreatedAt)
                .Select(epod => new TripDeliveryEpodDto
                {
                    EpodId = epod.EpodId,
                    CheckinTime = epod.CheckinTime,
                    SignedAt = epod.SignedAt,
                    ReceiverName = epod.ReceiverName,
                    ReceiverPhone = epod.ReceiverPhone,
                    SignImageUrl = epod.SignImageUrl,
                    SignLatitude = epod.SignLatitude,
                    SignLongitude = epod.SignLongitude,
                    DeliveryRating = epod.DeliveryRating,
                    Note = epod.Note,
                    PdfUrl = epod.PdfUrl,
                    Status = epod.Status,
                    CodAmount = epod.CodAmount,
                    CodAmountPaid = epod.CodAmountPaid,
                    PaymentMethod = epod.PaymentMethod,
                    PaymentStatus = epod.PaymentStatus,
                    PaymentEvidenceImageUrl = epod.PaymentEvidenceImageUrl,
                    HandoverConfirmedAt = epod.HandoverConfirmedAt,
                    HandoverPdfUrl = epod.HandoverPdfUrl,
                    PaymentConfirmedAt = epod.PaymentConfirmedAt
                })
                .ToList()
        };
    }

    private static TripLpnDetailsDto ToTripLpnDetailsDto(
        Lpn lpn,
        LpnDeliveryConfirmation? confirmation,
        TripLifoLoadItemDto? loadItem)
    {
        var customer = lpn.Customer ?? lpn.Order?.Customer;
        return new TripLpnDetailsDto
        {
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            OrderId = lpn.OrderId,
            CustomerId = lpn.CustomerId,
            ReceiptId = lpn.ReceiptId,
            WarehouseId = lpn.WarehouseId,
            RouteId = lpn.RouteId,
            TripId = lpn.TripId,
            Quantity = lpn.Quantity,
            ActualWeightKg = lpn.ActualWeightKg,
            ActualCbm = lpn.ActualCbm,
            LengthCm = lpn.LengthCm,
            WidthCm = lpn.WidthCm,
            HeightCm = lpn.HeightCm,
            RequiredTemperature = lpn.RequiredTemperature,
            RecordedTemperature = lpn.RecordedTemperature,
            StorageLocation = lpn.StorageLocation,
            State = lpn.State.ToString(),
            DiscrepancyReason = lpn.DiscrepancyReason,
            EvidenceImageUrl = lpn.EvidenceImageUrl,
            IsFastTrack = lpn.IsFastTrack,
            InboundTime = lpn.InboundTime,
            SlaDeadline = lpn.SlaDeadline,
            CreatedAt = lpn.CreatedAt,
            UpdatedAt = lpn.UpdatedAt,
            OrderTrackingCode = lpn.Order?.TrackingCode ?? string.Empty,
            OrderItemName = lpn.Order?.ItemName ?? string.Empty,
            OrderCategory = lpn.Order?.Category ?? string.Empty,
            OrderStatus = lpn.Order?.Status ?? string.Empty,
            LifoLoadOrder = loadItem?.LoadOrder,
            DeliveryStopSequence = loadItem?.DeliveryStopSequence,
            LifoZone = loadItem?.Zone,
            LifoReason = loadItem?.Reason,
            Customer = customer == null ? null : ToTripCustomerDetailsDto(customer),
            Warehouse = lpn.Warehouse == null
                ? null
                : new TripWarehouseDetailsDto
                {
                    WarehouseId = lpn.Warehouse.WarehouseId,
                    WarehouseName = lpn.Warehouse.WarehouseName,
                    WarehouseCode = lpn.Warehouse.WarehouseCode,
                    Address = lpn.Warehouse.Address,
                    WarehouseType = lpn.Warehouse.WarehouseType,
                    DefaultMinTemp = lpn.Warehouse.DefaultMinTemp,
                    DefaultMaxTemp = lpn.Warehouse.DefaultMaxTemp,
                    Status = lpn.Warehouse.Status
                },
            Receipt = lpn.Receipt == null
                ? null
                : new TripWarehouseReceiptDto
                {
                    ReceiptId = lpn.Receipt.ReceiptId,
                    ReceiptCode = lpn.Receipt.ReceiptCode,
                    ReferenceDocNo = lpn.Receipt.ReferenceDocNo,
                    ReceiptType = lpn.Receipt.ReceiptType,
                    Reason = lpn.Receipt.Reason,
                    TotalExpectedQuantity = lpn.Receipt.TotalExpectedQty,
                    TotalActualQuantity = lpn.Receipt.TotalActualQty,
                    RecordedTemperature = lpn.Receipt.RecordedTemperature,
                    DelivererName = lpn.Receipt.DelivererName,
                    Note = lpn.Receipt.Note,
                    PdfUrl = lpn.Receipt.PdfUrl,
                    CreatedAt = lpn.Receipt.CreatedAt
                },
            DeliveryConfirmation = confirmation == null
                ? null
                : new TripLpnDeliveryConfirmationDto
                {
                    ConfirmationId = confirmation.ConfirmationId,
                    OutcomeType = confirmation.OutcomeType,
                    ReceiverName = confirmation.ReceiverName,
                    ReceiverPhone = confirmation.ReceiverPhone,
                    RejectReason = confirmation.RejectReason,
                    RejectNote = confirmation.RejectNote,
                    EvidenceImageUrl = confirmation.EvidenceImageUrl,
                    ConfirmedByDriverId = confirmation.ConfirmedByDriverId,
                    ConfirmedAt = confirmation.ConfirmedAt,
                    CheckinAt = confirmation.CheckinAt,
                    SignatureImageUrl = confirmation.SignatureImageUrl,
                    CodAmount = confirmation.CodAmount,
                    CodPaymentMethod = confirmation.CodPaymentMethod,
                    CodReceiptImageUrl = confirmation.CodReceiptImageUrl,
                    NewSealNumber = confirmation.NewSealNumber,
                    RecordedTemperature = confirmation.RecordedTemperature,
                    IsCodVerified = confirmation.IsCodVerified,
                    CodVerifiedAt = confirmation.CodVerifiedAt,
                    CodVerifiedByUserId = confirmation.CodVerifiedByUserId
                }
        };
    }

    private static TripCustomerDetailsDto ToTripCustomerDetailsDto(Customer customer)
    {
        return new TripCustomerDetailsDto
        {
            CustomerId = customer.CustomerId,
            CompanyName = customer.CompanyName,
            TaxCode = customer.TaxCode,
            Address = customer.Address,
            Email = customer.Email,
            PaymentTerm = customer.PaymentTerm,
            Status = customer.Status
        };
    }

    private static TripSealDetailsDto ToTripSealDetailsDto(Seal seal)
    {
        return new TripSealDetailsDto
        {
            SealId = seal.SealId,
            StopId = seal.StopId,
            SealCode = seal.SealCode,
            AppliedAt = seal.AppliedAt,
            AppliedImageUrl = seal.AppliedImageUrl,
            RemovedAt = seal.RemovedAt,
            RemovedImageUrl = seal.RemovedImageUrl,
            Status = seal.Status,
            Note = seal.Note,
            CreatedAt = seal.CreatedAt
        };
    }

    private static TripIncidentDetailsDto ToTripIncidentDetailsDto(IncidentReport incident)
    {
        return new TripIncidentDetailsDto
        {
            IncidentId = incident.IncidentId,
            IncidentType = incident.IncidentType,
            Severity = incident.Severity,
            Description = incident.Description,
            CurrentLatitude = incident.CurrentLatitude,
            CurrentLongitude = incident.CurrentLongitude,
            DriverPaidAmount = incident.DriverPaidAmount,
            ReimbursedAmount = incident.ReimbursedAmount,
            RequiresRescue = incident.RequiresRescue,
            Status = incident.Status,
            ReportedBy = incident.ReportedBy,
            ReportedAt = incident.ReportedAt,
            HandledBy = incident.HandledBy,
            HandledAt = incident.HandledAt,
            HandlingNote = incident.HandlingNote,
            BrokenVehicleId = incident.BrokenVehicleId,
            ReplacementVehicleId = incident.ReplacementVehicleId,
            MaintenanceTicketId = incident.MaintenanceTicketId,
            RescueDispatchedAt = incident.RescueDispatchedAt,
            TransloadConfirmedAt = incident.TransloadConfirmedAt,
            TransloadNote = incident.TransloadNote,
            ApprovedAmount = incident.ApprovedAmount,
            ExpenseStatus = incident.ExpenseStatus,
            ResolvedAt = incident.ResolvedAt,
            ResolutionNote = incident.ResolutionNote,
            Evidence = incident.IncidentEvidences
                .Select(evidence => new TripIncidentEvidenceDto
                {
                    EvidenceId = evidence.EvidenceId,
                    EvidenceType = evidence.EvidenceType,
                    FileUrl = evidence.FileUrl
                })
                .ToList()
        };
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

    private CompatibleLpnItemDto ToCompatibleLpnItemDto(Lpn lpn)
    {
        var order = lpn.Order;
        var schedule = order?.Schedule;
        var route = schedule?.Route;
        var warehouse = lpn.Warehouse;
        var customerName = order?.Customer?.CompanyName ?? "N/A";
        var tempCondition = order?.TempCondition ?? string.Empty;

        return new CompatibleLpnItemDto
        {
            LpnId = lpn.LpnId,
            Label = $"{lpn.LpnCode} ({order?.TrackingCode}) - {order?.ItemName} | Qty: {lpn.Quantity} | {lpn.ActualWeightKg}kg / {lpn.ActualCbm}m3 ({tempCondition}) | Khach: {customerName} | Kho: {warehouse?.WarehouseName}",
            LpnCode = lpn.LpnCode,
            OrderId = lpn.OrderId,
            TrackingCode = order?.TrackingCode ?? string.Empty,
            ItemName = order?.ItemName ?? string.Empty,
            CustomerName = customerName,
            ScheduleId = schedule?.ScheduleId,
            ScheduleName = schedule?.ScheduleName,
            WarehouseId = lpn.WarehouseId,
            WarehouseName = warehouse?.WarehouseName,
            DestinationAddress = order?.DestLocationNavigation?.Address,
            RouteName = route != null ? $"{route.OriginCity} -> {route.DestCity}" : null,
            PlannedDispatchDate = schedule?.DepartureDate,
            Quantity = lpn.Quantity,
            ActualWeightKg = lpn.ActualWeightKg,
            ActualCbm = lpn.ActualCbm,
            TempCondition = tempCondition,
            Category = order?.Category,
            RequiredTemperature = _cargoCompatibilityService.ResolveRequiredTemperature(lpn),
            HasStrongOdor = order?.HasStrongOdor ?? false,
            IsStackable = order?.IsStackable ?? true,
            CreatedAt = lpn.CreatedAt,
            IsCompatible = true
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
