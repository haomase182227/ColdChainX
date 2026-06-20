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
        // 1. Seed Customer
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.TaxCode == "0102030405");
        if (customer == null)
        {
            customer = new Customer
            {
                CustomerId = Guid.NewGuid(),
                CompanyName = "CleanFood Vietnam Ltd",
                TaxCode = "0102030405",
                Address = "KCN Tân Bình, Tân Phú, TP. HCM",
                Email = "contact@cleanfood.vn",
                PaymentTerm = 30,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.Customers.Add(customer);
        }

        // 2. Seed Warehouse Location
        var originWarehouse = await _db.Locations.FirstOrDefaultAsync(l => l.Address.Contains("Kho Trung Chuyển Sóng Thần"));
        if (originWarehouse == null)
        {
            originWarehouse = new Location
            {
                LocationId = Guid.NewGuid(),
                Address = "Kho Trung Chuyển Sóng Thần - Dĩ An, Bình Dương",
                Latitude = 10.9012m,
                Longitude = 106.7589m,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.Locations.Add(originWarehouse);
        }

        // 3. Seed Destination Locations
        var destA = await _db.Locations.FirstOrDefaultAsync(l => l.Address.Contains("Lotte Mart Nam Sài Gòn"));
        if (destA == null)
        {
            destA = new Location
            {
                LocationId = Guid.NewGuid(),
                Address = "Lotte Mart Nam Sài Gòn, Quận 7, TP. HCM",
                Latitude = 10.7324m,
                Longitude = 106.7021m,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.Locations.Add(destA);
        }

        var destB = await _db.Locations.FirstOrDefaultAsync(l => l.Address.Contains("Aeon Mall Tân Phú"));
        if (destB == null)
        {
            destB = new Location
            {
                LocationId = Guid.NewGuid(),
                Address = "Aeon Mall Tân Phú, Tân Phú, TP. HCM",
                Latitude = 10.7915m,
                Longitude = 106.6124m,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.Locations.Add(destB);
        }

        // 4. Seed Driver
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.IdentityNumber == "079090123456");
        if (driver == null)
        {
            driver = new Driver
            {
                DriverId = Guid.NewGuid(),
                FullName = "Lê Hoàng Long",
                IdentityNumber = "079090123456",
                PhoneNumber = "0987654321",
                DateOfBirth = new DateOnly(1990, 5, 20),
                JoinDate = new DateOnly(2024, 1, 1),
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.Drivers.Add(driver);

            // Seed DriverLicense
            var license = new DriverLicense
            {
                LicenseId = Guid.NewGuid(),
                DriverId = driver.DriverId,
                LicenseNumber = "GPLX-12345",
                LicenseClass = "FC",
                IssueDate = new DateOnly(2023, 1, 1),
                ExpiryDate = new DateOnly(2028, 1, 1),
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.DriverLicenses.Add(license);
        }

        // 5. Seed Vehicle (assigned to driver)
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.TruckPlate == "51D-888.88");
        if (vehicle == null)
        {
            vehicle = new Vehicle
            {
                VehicleId = Guid.NewGuid(),
                DriverId = driver.DriverId,
                TruckPlate = "51D-888.88",
                Brand = "Hino",
                ManufactureYear = 2023,
                VehicleType = "Reefer Truck 5 Tons",
                MaxWeight = 5000,
                MaxCbm = 20,
                MinTemp = -20,
                MaxTemp = 20,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.Vehicles.Add(vehicle);
        }

        await _db.SaveChangesAsync(); // Lưu trước các IDs để tạo orders & contracts

        // Giải phóng xe 51D-888.88 khỏi các chuyến đi đang hoạt động để sẵn sàng test
        var activeTrips = await _db.MasterTrips
            .Where(t => t.VehicleId == vehicle.VehicleId && t.Status != "COMPLETED" && t.Status != "CANCELLED")
            .ToListAsync();
        foreach (var trip in activeTrips)
        {
            trip.Status = "CANCELLED";
        }
        await _db.SaveChangesAsync();

        // Clear old test data first if it exists
        var testOrders = await _db.TransportOrders.Where(o => o.TrackingCode.StartsWith("ORD-TEST-")).ToListAsync();
        if (testOrders.Any())
        {
            var testOrderIds = testOrders.Select(o => o.OrderId).ToList();
            var testContracts = await _db.CustomerContracts.Where(c => testOrderIds.Contains(c.OrderId ?? Guid.Empty)).ToListAsync();
            _db.CustomerContracts.RemoveRange(testContracts);
            _db.TransportOrders.RemoveRange(testOrders);
            await _db.SaveChangesAsync();
        }

        // 6. Seed TransportOrders
        var ordersToSeed = new List<(string Code, string Name, string Temp, decimal Weight, decimal Cbm, Guid Dest, int Qty, string Pack)>
        {
            ("ORD-TEST-001", "Thịt bò Kobe nhập khẩu", "FROZEN", 1500, 6, destA.LocationId, 15, "Thùng carton"),
            ("ORD-TEST-002", "Sữa chua Vinamilk", "2 to 8", 2000, 8, destA.LocationId, 100, "Thùng carton"),
            ("ORD-TEST-003", "Trái cây tươi nhập khẩu", "2 to 8", 1200, 5, destB.LocationId, 50, "Sọt nhựa")
        };

        var createdOrdersCount = 0;
        var createdContractsCount = 0;

        foreach (var info in ordersToSeed)
        {
            var existingOrder = await _db.TransportOrders.FirstOrDefaultAsync(o => o.TrackingCode == info.Code);
            if (existingOrder == null)
            {
                var newOrder = new TransportOrder
                {
                    OrderId = Guid.NewGuid(),
                    TrackingCode = info.Code,
                    ItemName = info.Name,
                    Category = "Thực phẩm",
                    TempCondition = info.Temp,
                    ExpectedWeightKg = info.Weight,
                    ExpectedCbm = info.Cbm,
                    PickupLocation = originWarehouse.LocationId,
                    DestLocation = info.Dest,
                    CustomerId = customer.CustomerId,
                    Quantity = info.Qty,
                    PackingType = info.Pack,
                    Status = "IN_WAREHOUSE",
                    CargoValue = info.Weight * 200000,
                    CreatedAt = DateTime.UtcNow
                };
                _db.TransportOrders.Add(newOrder);
                createdOrdersCount++;

                // Seed Contract
                var contract = new CustomerContract
                {
                    ContractId = Guid.NewGuid(),
                    CustomerId = customer.CustomerId,
                    OrderId = newOrder.OrderId,
                    ContractNumber = $"HD-2026-{info.Code.Replace("ORD-TEST-", "")}",
                    SignedDate = new DateOnly(2026, 6, 1),
                    ExpiredDate = new DateOnly(2027, 12, 31),
                    FileUrl = $"https://res.cloudinary.com/dbt5zpage/image/upload/coldchainx/contract_{info.Code}.pdf",
                    SignedFileUrl = $"https://res.cloudinary.com/dbt5zpage/image/upload/coldchainx/contract_{info.Code}.pdf",
                    SentAt = DateTime.UtcNow.AddDays(-2),
                    UploadedSignedAt = DateTime.UtcNow.AddDays(-1),
                    VerifiedAt = DateTime.UtcNow.AddMinutes(-30),
                    Status = "SIGNED",
                    CreatedAt = DateTime.UtcNow
                };
                _db.CustomerContracts.Add(contract);
                createdContractsCount++;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            Success = true,
            Message = "Đã nạp dữ liệu chuẩn thành công.",
            Data = new
            {
                Customer = customer.CompanyName,
                Warehouse = originWarehouse.Address,
                Destinations = new[] { destA.Address, destB.Address },
                Driver = driver.FullName,
                Vehicle = $"{vehicle.TruckPlate} (Tải: {vehicle.MaxWeight}kg, Temp: {vehicle.MinTemp}°C đến {vehicle.MaxTemp}°C)",
                NewOrdersCreated = createdOrdersCount,
                NewContractsCreated = createdContractsCount
            }
        });
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
