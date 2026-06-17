using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
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

    public DispatchController(
        IDispatchService dispatchService,
        IVehicleService vehicleService,
        IOrderService orderService,
        ApplicationDbContext db)
    {
        _dispatchService = dispatchService;
        _vehicleService = vehicleService;
        _orderService = orderService;
        _db = db;
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

    /// <summary>
    /// Lập kế hoạch lấy hàng từ kho và ghép chuyến.
    ///
    /// Workflow:
    /// 1. Validate hàng IN_WAREHOUSE + kiểm tra tải trọng/CBM xe
    /// 2. Tính lộ trình tối ưu qua các điểm giao (Goong API + Nearest Neighbor TSP)
    /// 3. Gợi ý xếp hàng theo thuật toán LIFO nội bộ (điểm giao cuối → xếp trước)
    /// 4. Tạo MasterTrip + TripStops
    /// 5. Cập nhật trạng thái đơn hàng → LOADING (sinh lệnh điều động)
    /// 6. Gửi thông báo cho Điều phối viên (Dispatcher)
    ///
    /// Dùng GET /api/dispatch/lookup/vehicles, /lookup/locations, /lookup/orders-ready
    /// để lấy danh sách ID hợp lệ trước khi gọi endpoint này.
    /// </summary>
    [HttpPost("plan-load")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PlanLoadResult), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(typeof(object), 500)]
    public async Task<IActionResult> PlanLoad([FromForm] PlanLoadFormRequest form)
    {
        // Chuyển đổi từ form sang PlanLoadRequest
        var rawVehicleId = ExtractGuid(form.VehicleId);
        if (!Guid.TryParse(rawVehicleId, out var vehicleId))
            return BadRequest(new { Success = false, Error = "VehicleId không hợp lệ." });

        var rawOriginWarehouseLocationId = ExtractGuid(form.OriginWarehouseLocationId);
        if (!Guid.TryParse(rawOriginWarehouseLocationId, out var originLocId))
            return BadRequest(new { Success = false, Error = "OriginWarehouseLocationId không hợp lệ." });

        if (form.OrderIds == null || form.OrderIds.Length == 0)
            return BadRequest(new { Success = false, Error = "Phải chọn ít nhất 1 đơn hàng." });

        var orderIds = new List<Guid>();
        foreach (var raw in form.OrderIds)
        {
            var rawOrderId = ExtractGuid(raw);
            if (!Guid.TryParse(rawOrderId, out var oid))
                return BadRequest(new { Success = false, Error = $"OrderId không hợp lệ: {raw}" });
            orderIds.Add(oid);
        }

        Guid? coordinatorId = null;
        var dispatcherIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(dispatcherIdClaim) && Guid.TryParse(dispatcherIdClaim, out var parsedId))
        {
            coordinatorId = parsedId;
        }

        var request = new PlanLoadRequest
        {
            OrderIds                  = orderIds,
            VehicleId                 = vehicleId,
            OriginWarehouseLocationId = originLocId,
            PlannedStartTime          = form.PlannedStartTime,
            PlannedEndTime            = form.PlannedEndTime,
            DispatchCoordinatorId     = coordinatorId
        };

        try
        {
            var result = await _dispatchService.PlanLoadFromWarehouseAsync(request);
            return Ok(new
            {
                Success = true,
                Message = $"Đã lập kế hoạch chuyến hàng thành công. " +
                          $"Trip: {result.TripId}, " +
                          $"Lộ trình: {result.Route.TotalStops} điểm dừng / {result.Route.TotalDistanceKm}km, " +
                          $"Đã thông báo {result.NotifiedCoordinators} điều phối viên.",
                Data = result
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống.", Details = ex.Message });
        }
    }

    // ── Legacy endpoints ───────────────────────────────────────────────────

    /// <summary>[Legacy] Gợi ý xếp hàng bằng Gemini AI (không dùng Goong, không tạo Trip).</summary>
    [HttpPost("suggest-load")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(object), 400)]
    public async Task<IActionResult> SuggestLoad([FromBody] SuggestLoadRequest request)
    {
        try
        {
            var rawVehicleId = ExtractGuid(request.VehicleId);
            if (!Guid.TryParse(rawVehicleId, out var vehicleId))
                return BadRequest(new { Success = false, Error = "VehicleId không hợp lệ." });

            var orderIds = new List<Guid>();
            foreach (var raw in request.OrderIds)
            {
                var rawOrderId = ExtractGuid(raw);
                if (!Guid.TryParse(rawOrderId, out var oid))
                    return BadRequest(new { Success = false, Error = $"OrderId không hợp lệ: {raw}" });
                orderIds.Add(oid);
            }

            var plan = await _dispatchService.SuggestLoadPlanAsync(orderIds, vehicleId);
            return Ok(new { Success = true, Plan = plan });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Internal server error.", Details = ex.Message });
        }
    }

    /// <summary>[Legacy] Tính route và LIFO cho Trip đã tạo sẵn.</summary>
    [HttpPost("route-lifo/{tripId}")]
    public async Task<IActionResult> CalculateRouteAndLIFO(string tripId)
    {
        try
        {
            var rawTripId = ExtractGuid(tripId);
            if (!Guid.TryParse(rawTripId, out var parsedTripId))
                return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

            await _dispatchService.CalculateRouteAndLIFOAsync(parsedTripId);
            return Ok(new { Success = true, Message = "Route calculated and LIFO stops planned." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>Đóng hàng và kẹp chì niêm phong.</summary>
    [HttpPost("seal/{tripId}")]
    public async Task<IActionResult> SealTruck(string tripId, [FromBody] SealRequest request)
    {
        try
        {
            var rawTripId = ExtractGuid(tripId);
            if (!Guid.TryParse(rawTripId, out var parsedTripId))
                return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

            // Lấy userId từ JWT claim
            var keeperIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var keeperId = keeperIdClaim != null ? Guid.Parse(keeperIdClaim) : Guid.NewGuid();

            await _dispatchService.SealTruckAsync(parsedTripId, request.SealCode, keeperId);
            return Ok(new { Success = true, Message = "Truck sealed successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>Cấp giấy đi đường / E-Waybill cho chuyến.</summary>
    [HttpPost("issue-documents/{tripId}")]
    public async Task<IActionResult> IssueDocuments(string tripId)
    {
        try
        {
            var rawTripId = ExtractGuid(tripId);
            if (!Guid.TryParse(rawTripId, out var parsedTripId))
                return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userId = userIdClaim != null ? Guid.Parse(userIdClaim) : Guid.NewGuid();

            await _dispatchService.IssueDispatchDocumentsAsync(parsedTripId, userId);
            return Ok(new { Success = true, Message = "Dispatch documents issued." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>Lấy sơ đồ và thứ tự bốc xếp LIFO của chuyến đi cho nhân viên kho.</summary>
    [HttpGet("load-plan/{tripId}")]
    [ProducesResponseType(typeof(List<LoadInstruction>), 200)]
    [ProducesResponseType(typeof(object), 400)]
    public async Task<IActionResult> GetLoadPlan(string tripId)
    {
        try
        {
            var rawTripId = ExtractGuid(tripId);
            if (!Guid.TryParse(rawTripId, out var parsedTripId))
                return BadRequest(new { Success = false, Error = "TripId không hợp lệ." });

            var loadPlan = await _dispatchService.GetLoadPlanAsync(parsedTripId);
            return Ok(new { Success = true, Data = loadPlan });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>[Test Only] Test kết nối Gemini API.</summary>
    [AllowAnonymous]
    [HttpGet("test-gemini")]
    public async Task<IActionResult> TestGemini(
        [FromServices] ColdChainX.Infrastructure.Integration.GeminiLoadOptimizerClient geminiClient)
    {
        try
        {
            var mockVehicle = new ColdChainX.Core.Entities.Vehicle
            {
                VehicleId = Guid.NewGuid(),
                MaxCbm    = 10.26m,
                MaxWeight = 5000m
            };

            var mockOrders = new List<ColdChainX.Core.Entities.TransportOrder>
            {
                new()
                {
                    OrderId          = Guid.NewGuid(),
                    ItemName         = "Frozen Fish",
                    Quantity         = 50,
                    ExpectedCbm      = 2.5m,
                    ExpectedWeightKg = 1000m,
                    DestLocation     = Guid.NewGuid()
                },
                new()
                {
                    OrderId          = Guid.NewGuid(),
                    ItemName         = "Ice Cream",
                    Quantity         = 100,
                    ExpectedCbm      = 1.0m,
                    ExpectedWeightKg = 500m,
                    DestLocation     = Guid.NewGuid()
                }
            };

            var routeSequence = new List<Guid>
            {
                mockOrders[1].DestLocation!.Value,
                mockOrders[0].DestLocation!.Value
            };

            var plan = await geminiClient.OptimizeLoadPlanAsync(mockVehicle, mockOrders, routeSequence);
            return Ok(new { Message = "Gemini API Test Success", Plan = plan });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Giả lập nạp dữ liệu test và chạy toàn bộ luồng Dispatch.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("seed-and-test-dispatch")]
    public async Task<IActionResult> SeedAndTestDispatch(
        [FromServices] ColdChainX.Infrastructure.Persistence.ApplicationDbContext dbContext)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        try
        {
            // 1. Tạo MessageType và NotificationTemplate nếu chưa có
            var msgType = await dbContext.Messagetypes.FirstOrDefaultAsync();
            if (msgType == null)
            {
                msgType = new Messagetype
                {
                    TypeId = Guid.NewGuid(),
                    TypeName = "System Alert",
                    Description = "System generated alert notifications"
                };
                dbContext.Messagetypes.Add(msgType);
                await dbContext.SaveChangesAsync();
            }

            var template = await dbContext.NotificationTemplates.FindAsync("DISPATCH_LOADING_ORDER");
            if (template == null)
            {
                template = new NotificationTemplate
                {
                    TemplateId = "DISPATCH_LOADING_ORDER",
                    TypeId = msgType.TypeId,
                    TitleTemplate = "Lệnh điều động bốc xếp xe {vehicle}",
                    BodyTemplate = "Chuyến hàng {tripId} cần xếp {orderCount} đơn hàng, tổng trọng lượng {totalWeight}kg. Dự kiến khởi hành: {startTime}.",
                    Channel = "IN_APP",
                    Status = "ACTIVE"
                };
                dbContext.NotificationTemplates.Add(template);
                await dbContext.SaveChangesAsync();
            }

            // 2. Tạo hoặc lấy Role Dispatcher
            var dispatcherRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.RoleName == "Dispatcher");
            if (dispatcherRole == null)
            {
                dispatcherRole = new Role
                {
                    RoleId = Guid.NewGuid(),
                    RoleName = "Dispatcher",
                    Description = "Container dispatcher role"
                };
                dbContext.Roles.Add(dispatcherRole);
                await dbContext.SaveChangesAsync();
            }

            // 3. Tạo hoặc lấy User Dispatcher
            var dispatcherUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == "testdispatcher");
            if (dispatcherUser == null)
            {
                dispatcherUser = new User
                {
                    UserId = Guid.NewGuid(),
                    Username = "testdispatcher",
                    PasswordHash = "hashedpassword",
                    Email = "dispatcher@coldchainx.com",
                    FullName = "Test Dispatcher Coordinator",
                    RoleId = dispatcherRole.RoleId,
                    Status = "ACTIVE",
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Users.Add(dispatcherUser);
                await dbContext.SaveChangesAsync();
            }

            // 3.1. Tạo hoặc lấy Role Driver
            var driverRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.RoleName == "Driver");
            if (driverRole == null)
            {
                driverRole = new Role
                {
                    RoleId = Guid.NewGuid(),
                    RoleName = "Driver",
                    Description = "Driver role"
                };
                dbContext.Roles.Add(driverRole);
                await dbContext.SaveChangesAsync();
            }

            // 3.2. Tạo hoặc lấy User Driver
            var testDriverUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == "testdriver");
            Guid driverUserId = testDriverUser?.UserId ?? Guid.NewGuid();
            if (testDriverUser == null)
            {
                testDriverUser = new User
                {
                    UserId = driverUserId,
                    Username = "testdriver",
                    PasswordHash = "hashedpassword",
                    Email = "driver@coldchainx.com",
                    FullName = "Test Driver",
                    RoleId = driverRole.RoleId,
                    Status = "ACTIVE",
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Users.Add(testDriverUser);
                await dbContext.SaveChangesAsync();
            }

            // 3.3. Tạo hoặc lấy Driver Entity liên kết
            var testDriver = await dbContext.Drivers.FirstOrDefaultAsync(d => d.UserId == driverUserId);
            if (testDriver == null)
            {
                testDriver = new Driver
                {
                    DriverId = driverUserId, // DriverId trùng với UserId để tránh lỗi khóa ngoại UploadedBy khi tạo E-Waybill
                    UserId = driverUserId,
                    Status = "AVAILABLE",
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Drivers.Add(testDriver);
                await dbContext.SaveChangesAsync();
            }

            // 4. Tạo các Locations test (Hà Nội)
            var originLocation = new Location
            {
                LocationId = Guid.NewGuid(),
                Address = "Kho Tổng ColdChainX Hà Nội (Cầu Giấy)",
                Latitude = 21.028511m,
                Longitude = 105.804817m,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            
            var destLocation1 = new Location
            {
                LocationId = Guid.NewGuid(),
                Address = "Đại siêu thị BigC Thăng Long",
                Latitude = 21.009088m,
                Longitude = 105.798687m,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };

            var destLocation2 = new Location
            {
                LocationId = Guid.NewGuid(),
                Address = "Cửa hàng tiện lợi WinMart+ Hoàn Kiếm",
                Latitude = 21.028092m,
                Longitude = 105.852332m,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };

            dbContext.Locations.AddRange(originLocation, destLocation1, destLocation2);
            await dbContext.SaveChangesAsync();

            // 5. Tạo Vehicle test
            var randSuffix = new Random().Next(1000, 9999);
            var vehicle = new Vehicle
            {
                VehicleId = Guid.NewGuid(),
                TruckPlate = $"29C-{randSuffix}",
                Brand = "Hino",
                ManufactureYear = 2024,
                VehicleType = "Reefer Truck 5 Tons",
                MaxWeight = 5000m,
                MaxCbm = 25m,
                MinTemp = -20m,
                MaxTemp = 20m,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Vehicles.Add(vehicle);
            await dbContext.SaveChangesAsync();

            // 6. Tạo TransportOrders ở trạng thái IN_WAREHOUSE
            var order1 = new TransportOrder
            {
                OrderId = Guid.NewGuid(),
                TrackingCode = $"ORD-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                ItemName = "Hải sản đông lạnh xuất khẩu (Cá hồi)",
                Category = "Frozen Food",
                Quantity = 150,
                PackingType = "Thùng Carton",
                TempCondition = "FROZEN (-18C)",
                ExpectedWeightKg = 1200m,
                ActualWeightKg = 1200m,
                ExpectedCbm = 4.5m,
                ActualCbm = 4.5m,
                Status = "IN_WAREHOUSE",
                PickupLocation = originLocation.LocationId,
                DestLocation = destLocation1.LocationId,
                CargoValue = 150000000m,
                CreatedAt = DateTime.UtcNow
            };

            var order2 = new TransportOrder
            {
                OrderId = Guid.NewGuid(),
                TrackingCode = $"ORD-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                ItemName = "Sữa chua và phô mai Đà Lạt",
                Category = "Chilled Dairy",
                Quantity = 300,
                PackingType = "Khay nhựa",
                TempCondition = "CHILLED (2-8C)",
                ExpectedWeightKg = 900m,
                ActualWeightKg = 900m,
                ExpectedCbm = 3.0m,
                ActualCbm = 3.0m,
                Status = "IN_WAREHOUSE",
                PickupLocation = originLocation.LocationId,
                DestLocation = destLocation2.LocationId,
                CargoValue = 60000000m,
                CreatedAt = DateTime.UtcNow
            };

            var order3 = new TransportOrder
            {
                OrderId = Guid.NewGuid(),
                TrackingCode = $"ORD-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                ItemName = "Trái cây tươi (Táo Mỹ)",
                Category = "Fresh Produce",
                Quantity = 200,
                PackingType = "Thùng gỗ",
                TempCondition = "AMBIENT (15-20C)",
                ExpectedWeightKg = 1500m,
                ActualWeightKg = 1500m,
                ExpectedCbm = 5.2m,
                ActualCbm = 5.2m,
                Status = "IN_WAREHOUSE",
                PickupLocation = originLocation.LocationId,
                DestLocation = destLocation1.LocationId,
                CargoValue = 90000000m,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.TransportOrders.AddRange(order1, order2, order3);
            await dbContext.SaveChangesAsync();

            // 7. Thực hiện luồng chính: Lập kế hoạch bốc xếp ghép chuyến (PlanLoad)
            var planRequest = new PlanLoadRequest
            {
                OrderIds = new List<Guid> { order1.OrderId, order2.OrderId, order3.OrderId },
                VehicleId = vehicle.VehicleId,
                OriginWarehouseLocationId = originLocation.LocationId,
                PlannedStartTime = DateTime.UtcNow.AddHours(2),
                PlannedEndTime = DateTime.UtcNow.AddHours(6),
                DispatchCoordinatorId = dispatcherUser.UserId
            };

            var planResult = await _dispatchService.PlanLoadFromWarehouseAsync(planRequest);
            var tripId = planResult.TripId;

            // Gán Driver cho chuyến đi vừa tạo (tránh lỗi khóa ngoại khi phát hành tài liệu)
            var tripEntity = await dbContext.MasterTrips.FindAsync(tripId);
            if (tripEntity != null)
            {
                tripEntity.DriverId = driverUserId;
                await dbContext.SaveChangesAsync();
            }

            // 8. Thực hiện kẹp chì niêm phong xe (Seal Truck)
            var sealCode = $"SEAL-{Guid.NewGuid().ToString()[..6].ToUpper()}";
            await _dispatchService.SealTruckAsync(tripId, sealCode, dispatcherUser.UserId);

            // 9. Thực hiện cấp giấy đi đường / E-Waybill
            await _dispatchService.IssueDispatchDocumentsAsync(tripId);

            // 10. Lấy dữ liệu thực tế đã lưu xuống cơ sở dữ liệu để kiểm chứng
            var savedTrip = await dbContext.MasterTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            var savedStops = await dbContext.TripStops
                .AsNoTracking()
                .Where(ts => ts.TripId == tripId)
                .OrderBy(ts => ts.StopSequence)
                .ToListAsync();

            var savedOrders = await dbContext.TransportOrders
                .AsNoTracking()
                .Where(o => o.MasterTripId == tripId)
                .ToListAsync();

            var savedSeal = await dbContext.Seals
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TripId == tripId);

            var savedDocs = await dbContext.TransportDocuments
                .AsNoTracking()
                .Where(d => d.ImageUrl.Contains(tripId.ToString()))
                .ToListAsync();

            var savedNotifications = await dbContext.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == dispatcherUser.UserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            return Ok(new
            {
                Success = true,
                Message = "Simulated Dispatch workflow successfully completed.",
                SimulationData = new
                {
                    WarehouseLocation = new { originLocation.LocationId, originLocation.Address, originLocation.Latitude, originLocation.Longitude },
                    DeliveryLocations = new[]
                    {
                        new { destLocation1.LocationId, destLocation1.Address, destLocation1.Latitude, destLocation1.Longitude },
                        new { destLocation2.LocationId, destLocation2.Address, destLocation2.Latitude, destLocation2.Longitude }
                    },
                    Vehicle = new { vehicle.VehicleId, vehicle.TruckPlate, vehicle.Brand, vehicle.MaxWeight, vehicle.MaxCbm, vehicle.Status },
                    InitialOrders = new[]
                    {
                        new { order1.OrderId, order1.TrackingCode, order1.ItemName, order1.ExpectedWeightKg, order1.ExpectedCbm, order1.Status, order1.TempCondition },
                        new { order2.OrderId, order2.TrackingCode, order2.ItemName, order2.ExpectedWeightKg, order2.ExpectedCbm, order2.Status, order2.TempCondition },
                        new { order3.OrderId, order3.TrackingCode, order3.ItemName, order3.ExpectedWeightKg, order3.ExpectedCbm, order3.Status, order3.TempCondition }
                    }
                },
                PlanResult = planResult,
                DatabaseState = new
                {
                    MasterTrip = savedTrip == null ? null : new
                    {
                        savedTrip.TripId,
                        savedTrip.VehicleId,
                        savedTrip.OriginLocationId,
                        savedTrip.DestinationLocationId,
                        savedTrip.TotalDistanceKm,
                        savedTrip.TargetTemperature,
                        savedTrip.PlannedStartTime,
                        savedTrip.PlannedEndTime,
                        savedTrip.Status,
                        savedTrip.CreatedAt
                    },
                    TripStops = savedStops.Select(ts => new
                    {
                        ts.StopId,
                        ts.TripId,
                        ts.LocationId,
                        ts.StopSequence,
                        ts.StopType,
                        ts.Status,
                        ts.PlannedArrivalTime,
                        ts.PlannedDepartureTime
                    }).ToList(),
                    TransportOrders = savedOrders.Select(o => new
                    {
                        o.OrderId,
                        o.TrackingCode,
                        o.ItemName,
                        o.Status,
                        o.MasterTripId,
                        o.ExpectedWeightKg,
                        o.ExpectedCbm
                    }).ToList(),
                    Seal = savedSeal == null ? null : new
                    {
                        savedSeal.SealId,
                        savedSeal.TripId,
                        savedSeal.SealCode,
                        savedSeal.AppliedAt,
                        savedSeal.Status
                    },
                    TransportDocuments = savedDocs.Select(d => new
                    {
                        d.DocId,
                        d.DocType,
                        d.ImageUrl,
                        d.Status,
                        d.CreatedAt
                    }).ToList(),
                    Notifications = savedNotifications.Select(n => new
                    {
                        n.NotiId,
                        n.UserId,
                        n.TemplateId,
                        n.Params,
                        n.IsRead,
                        n.CreatedAt
                    }).ToList()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = ex.Message, Details = ex.InnerException?.Message ?? ex.StackTrace });
        }
    }

    private static string ExtractGuid(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var parts = input.Split(':');
        return parts[0].Trim();
    }
}

// ── Request/Response models ──────────────────────────────────────────────────

/// <summary>Request cho endpoint legacy suggest-load (chỉ dùng Gemini).</summary>
public class SuggestLoadRequest
{
    public List<string> OrderIds { get; set; } = new();
    public string VehicleId { get; set; } = null!;
}

public class SealRequest
{
    public string SealCode { get; set; } = null!;
}

/// <summary>
/// Form request cho POST /api/dispatch/plan-load (multipart/form-data).
/// Dùng string thay vì Guid vì HTML form / Swagger chỉ gửi được text.
/// OrderIds được gửi nhiều lần cùng tên field để chọn nhiều đơn hàng.
/// </summary>
public class PlanLoadFormRequest
{
    /// <summary>
    /// Danh sách OrderId (IN_WAREHOUSE). Gửi nhiều lần cùng tên field.
    /// Dùng GET /api/dispatch/lookup/orders-ready để lấy danh sách.
    /// </summary>
    public string[] OrderIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// VehicleId xe tải được chỉ định.
    /// Dùng GET /api/dispatch/lookup/vehicles để lấy danh sách.
    /// </summary>
    public string VehicleId { get; set; } = null!;

    /// <summary>
    /// LocationId kho xuất phát (điểm đầu lộ trình).
    /// Dùng GET /api/dispatch/lookup/locations để lấy danh sách.
    /// </summary>
    public string OriginWarehouseLocationId { get; set; } = null!;

    /// <summary>Thời gian dự kiến xuất phát (ISO 8601, VD: 2026-06-18T06:00:00).</summary>
    public DateTime PlannedStartTime { get; set; }

    /// <summary>Thời gian dự kiến hoàn thành chuyến (ISO 8601, VD: 2026-06-18T18:00:00).</summary>
    public DateTime PlannedEndTime { get; set; }
}
