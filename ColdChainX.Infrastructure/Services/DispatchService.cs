using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Integration;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using ColdChainX.Infrastructure.Hubs;

namespace ColdChainX.Infrastructure.Services;

public class DispatchService : IDispatchService
{
    private readonly ApplicationDbContext _context;
    private readonly GeminiLoadOptimizerClient _geminiClient;
    private readonly ILocationService _locationService;
    private readonly IPdfService _pdfService;
    private readonly IWebHostEnvironment _environment;
    private readonly IHubContext<NotificationHub> _hubContext;

    // Tên role điều phối viên
    private const string CoordinatorRoleName = "Dispatcher";

    // TemplateId thông báo lệnh điều động (phải tồn tại trong bảng notification_templates)
    private const string LoadingOrderTemplateId = "DISPATCH_LOADING_ORDER";

    public DispatchService(
        ApplicationDbContext context,
        GeminiLoadOptimizerClient geminiClient,
        ILocationService locationService,
        IPdfService pdfService,
        IWebHostEnvironment environment,
        IHubContext<NotificationHub> hubContext)
    {
        _context = context;
        _geminiClient = geminiClient;
        _locationService = locationService;
        _pdfService = pdfService;
        _environment = environment;
        _hubContext = hubContext;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MAIN: PlanLoadFromWarehouseAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<PlanLoadResult> PlanLoadFromWarehouseAsync(PlanLoadRequest request)
    {
        // ── STEP 1: Validate vehicle ─────────────────────────────────────────
        var vehicle = await _context.Vehicles.FindAsync(request.VehicleId)
            ?? throw new InvalidOperationException("Xe không tồn tại.");

        if (vehicle.Status != null &&
            vehicle.Status.Equals("MAINTENANCE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Xe {vehicle.TruckPlate} đang trong trạng thái bảo dưỡng.");

        // ── STEP 2: Lấy đơn hàng đang ở kho ────────────────────────────────
        var orders = await _context.TransportOrders
            .Include(o => o.DestLocationNavigation)
            .Where(o => request.OrderIds.Contains(o.OrderId)
                        && o.Status == "IN_WAREHOUSE"
                        && o.WarehouseReceipts.Any())
            .ToListAsync();

        if (orders.Count == 0)
            throw new InvalidOperationException(
                "Không tìm thấy đơn hàng nào ở trạng thái IN_WAREHOUSE với các OrderId đã cung cấp.");

        var warehouseIds = orders
            .SelectMany(o => o.WarehouseReceipts)
            .Select(w => w.WarehouseId)
            .Distinct()
            .ToList();

        if (warehouseIds.Count > 1)
            throw new InvalidOperationException("Tất cả các đơn hàng được chọn phải cùng thuộc một kho lưu trữ (WarehouseId).");

        var missingOrderIds = request.OrderIds
            .Except(orders.Select(o => o.OrderId))
            .ToList();
        if (missingOrderIds.Any())
            throw new InvalidOperationException(
                $"Các đơn hàng sau không tồn tại hoặc không ở trạng thái IN_WAREHOUSE: " +
                $"{string.Join(", ", missingOrderIds)}");

        // ── STEP 3: Kiểm tra tải trọng & thể tích ──────────────────────────
        var totalWeight = orders.Sum(o => o.ExpectedWeightKg);
        var totalCbm    = orders.Sum(o => o.ExpectedCbm);

        if (totalWeight > vehicle.MaxWeight)
            throw new InvalidOperationException(
                $"Quá tải: Tổng trọng lượng ({totalWeight:F1}kg) vượt tải trọng xe ({vehicle.MaxWeight}kg).");

        if (totalCbm > vehicle.MaxCbm)
            throw new InvalidOperationException(
                $"Quá thể tích: Tổng CBM ({totalCbm:F2}m³) vượt dung tích xe ({vehicle.MaxCbm}m³).");

        // ── STEP 4: Lấy Location xuất phát (kho) ───────────────────────────
        var originLocation = await _context.Locations.FindAsync(request.OriginWarehouseLocationId)
            ?? throw new InvalidOperationException("LocationId kho xuất phát không tồn tại.");

        // ── STEP 5: Tính lộ trình bằng Nearest Neighbor TSP + Goong API ────
        var routeResult = await BuildOptimalRouteAsync(
            originLocation, orders, vehicle);

        // ── STEP 6: Thuật toán LIFO container loading nội bộ ───────────────
        var loadPlan = BuildLIFOLoadPlan(orders, routeResult.StopSequence);

        // ── STEP 7: Tạo MasterTrip ──────────────────────────────────────────
        var lastDestId = routeResult.StopSequence.Last().LocationId;
        var masterTrip = new MasterTrip
        {
            TripId              = Guid.NewGuid(),
            VehicleId           = vehicle.VehicleId,
            OriginLocationId    = originLocation.LocationId,
            DestinationLocationId = lastDestId,
            TotalDistanceKm     = routeResult.TotalDistanceKm,
            TargetTemperature   = GetTargetTemperature(orders),
            PlannedStartTime    = request.PlannedStartTime,
            PlannedEndTime      = request.PlannedEndTime,
            Status              = "PLANNED",
            CreatedAt           = DateTime.UtcNow,
        };
        _context.MasterTrips.Add(masterTrip);

        // ── STEP 8: Tạo TripStops ───────────────────────────────────────────
        var stopGapHours = (request.PlannedEndTime - request.PlannedStartTime).TotalHours
                           / Math.Max(routeResult.StopSequence.Count, 1);

        foreach (var stop in routeResult.StopSequence)
        {
            var plannedArrival = request.PlannedStartTime
                .AddHours(stopGapHours * stop.Sequence);

            _context.TripStops.Add(new TripStop
            {
                StopId               = Guid.NewGuid(),
                TripId               = masterTrip.TripId,
                LocationId           = stop.LocationId,
                StopSequence         = stop.Sequence,
                StopType             = "DELIVERY",
                Status               = "PLANNED",
                PlannedArrivalTime   = plannedArrival,
                PlannedDepartureTime = plannedArrival.AddMinutes(30),
                CreatedAt            = DateTime.UtcNow
            });
        }

        // ── STEP 9: Cập nhật trạng thái đơn hàng → LOADING ─────────────────
        foreach (var order in orders)
        {
            order.Status       = "LOADING";
            order.MasterTripId = masterTrip.TripId;
        }

        // ── STEP 10: Gửi thông báo cho Điều phối viên (Dispatcher) ─────────
        var notifiedCount = await SendLoadingNotificationsAsync(
            masterTrip, orders, vehicle, loadPlan,
            request.DispatchCoordinatorId);

        await _context.SaveChangesAsync();

        // ── STEP 11: Build kết quả trả về ──────────────────────────────────
        var routeStops = routeResult.StopSequence.Select(s =>
        {
            var stopOrders = orders
                .Where(o => o.DestLocation == s.LocationId)
                .Select(o => new OrderSummary
                {
                    OrderId       = o.OrderId,
                    TrackingCode  = o.TrackingCode,
                    ItemName      = o.ItemName,
                    Quantity      = o.Quantity,
                    WeightKg      = o.ExpectedWeightKg,
                    Cbm           = o.ExpectedCbm,
                    TempCondition = o.TempCondition
                }).ToList();

            return new RouteStop
            {
                Sequence               = s.Sequence,
                LocationId             = s.LocationId,
                Address                = s.Address,
                Latitude               = s.Latitude,
                Longitude              = s.Longitude,
                DistanceFromPreviousKm = s.DistanceFromPreviousKm,
                OrdersToUnload         = stopOrders
            };
        }).ToList();

        var dispatchInstructions = loadPlan.Select(li => new DispatchInstruction
        {
            OrderId        = li.OrderId,
            TrackingCode   = li.TrackingCode,
            ItemName       = li.ItemName,
            Action         = "LOAD",
            PreviousStatus = "IN_WAREHOUSE",
            TargetStatus   = "LOADING",
            LoadOrder      = li.LoadOrder,
            Zone           = li.Zone
        }).OrderBy(d => d.LoadOrder).ToList();

        return new PlanLoadResult
        {
            TripId = masterTrip.TripId,
            Vehicle = new VehicleInfo
            {
                VehicleId            = vehicle.VehicleId,
                TruckPlate           = vehicle.TruckPlate,
                MaxWeightKg          = vehicle.MaxWeight,
                MaxCbm               = vehicle.MaxCbm,
                TotalOrderWeightKg   = totalWeight,
                TotalOrderCbm        = totalCbm,
                WeightUtilizationPct = Math.Round(totalWeight / vehicle.MaxWeight * 100, 1),
                CbmUtilizationPct    = Math.Round(totalCbm / vehicle.MaxCbm * 100, 1)
            },
            Route = new RouteInfo
            {
                TotalDistanceKm = routeResult.TotalDistanceKm,
                TotalStops      = routeStops.Count,
                Stops           = routeStops
            },
            LoadPlan             = loadPlan,
            DispatchInstructions = dispatchInstructions,
            NotifiedCoordinators = notifiedCount
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ALGORITHM 1: Nearest Neighbor TSP — tối ưu thứ tự điểm dừng
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<RouteCalculationResult> BuildOptimalRouteAsync(
        Location origin,
        List<TransportOrder> orders,
        Vehicle vehicle)
    {
        // Lấy danh sách điểm đến duy nhất (nhiều đơn có thể cùng điểm giao)
        var destinations = orders
            .Where(o => o.DestLocation.HasValue && o.DestLocationNavigation != null)
            .GroupBy(o => o.DestLocation!.Value)
            .Select(g => g.First().DestLocationNavigation!)
            .ToList();

        if (destinations.Count == 0)
            throw new InvalidOperationException(
                "Không có đơn hàng nào có tọa độ điểm giao. " +
                "Hãy đảm bảo DestLocation đã được gán cho tất cả đơn hàng.");

        // ── Nearest Neighbor TSP ─────────────────────────────────────────────
        // Xuất phát từ kho (origin), tại mỗi bước chọn điểm gần nhất chưa thăm.
        var visited     = new HashSet<Guid>();
        var orderedStops = new List<StopInfo>();
        var totalDistKm  = 0m;

        decimal currentLat = origin.Latitude;
        decimal currentLon = origin.Longitude;

        while (visited.Count < destinations.Count)
        {
            Location? nearest         = null;
            decimal   nearestDistKm   = decimal.MaxValue;

            foreach (var dest in destinations)
            {
                if (visited.Contains(dest.LocationId)) continue;

                decimal distKm;
                try
                {
                    distKm = await _locationService.GetDistanceKmAsync(
                        currentLat, currentLon,
                        dest.Latitude, dest.Longitude);
                }
                catch
                {
                    // Goong API lỗi → fallback Haversine
                    distKm = HaversineKm(currentLat, currentLon, dest.Latitude, dest.Longitude);
                }

                if (distKm < nearestDistKm)
                {
                    nearestDistKm = distKm;
                    nearest       = dest;
                }
            }

            if (nearest == null) break;

            visited.Add(nearest.LocationId);
            totalDistKm += nearestDistKm;
            currentLat   = nearest.Latitude;
            currentLon   = nearest.Longitude;

            orderedStops.Add(new StopInfo
            {
                Sequence               = orderedStops.Count + 1,
                LocationId             = nearest.LocationId,
                Address                = nearest.Address,
                Latitude               = nearest.Latitude,
                Longitude              = nearest.Longitude,
                DistanceFromPreviousKm = nearestDistKm
            });
        }

        return new RouteCalculationResult
        {
            TotalDistanceKm = Math.Round(totalDistKm, 2),
            StopSequence    = orderedStops
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ALGORITHM 2: LIFO Container Loading Plan
    // ═══════════════════════════════════════════════════════════════════════
    //
    //  Nguyên tắc:
    //  1. Điểm giao CUỐI cùng trong lộ trình → xếp VÀO TRƯỚC (nằm sâu trong xe, đuôi)
    //  2. Điểm giao ĐẦU tiên → xếp VÀO SAU (gần cửa xe, đầu)
    //  3. Trong cùng điểm dừng: hàng NẶNG hơn xếp DƯỚI (ưu tiên trước)
    //  4. Phân vùng nhiệt độ:
    //       FROZEN (< -10°C)  → REAR   (ngăn đông, sâu nhất)
    //       CHILLED (0–8°C)   → MID    (ngăn mát, giữa)
    //       AMBIENT (> 8°C)   → FRONT  (nhiệt độ thường, gần cửa)
    //
    //  Kết quả: LoadOrder=1 = xếp lên đầu tiên (đặt sâu vào đuôi xe)
    // ═══════════════════════════════════════════════════════════════════════

    private static List<LoadInstruction> BuildLIFOLoadPlan(
        List<TransportOrder> orders,
        List<StopInfo> stopSequence)
    {
        // Map locationId → stop sequence (1 = giao trước)
        var stopSeqMap = stopSequence.ToDictionary(s => s.LocationId, s => s.Sequence);

        var enriched = orders
            .Where(o => o.DestLocation.HasValue)
            .Select(o => new
            {
                Order       = o,
                StopSeq     = stopSeqMap.TryGetValue(o.DestLocation!.Value, out var seq) ? seq : 999,
                TempZone    = ClassifyTempZone(o.TempCondition),
                TempZoneOrd = TempZoneOrder(o.TempCondition)
            })
            .ToList();

        // Xếp theo LIFO:
        //  - Điểm giao sau (StopSeq DESC) → xếp vào trước (LoadOrder nhỏ)
        //  - Cùng điểm: hàng nặng hơn → xếp vào trước
        //  - Cùng điểm & trọng lượng: zone đông → xếp trước
        var sorted = enriched
            .OrderByDescending(x => x.StopSeq)          // điểm cuối vào xe trước
            .ThenByDescending(x => x.Order.ExpectedWeightKg)  // nặng dưới
            .ThenBy(x => x.TempZoneOrd)                  // frozen trước
            .ToList();

        var result = new List<LoadInstruction>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var item   = sorted[i];
            var order  = item.Order;
            var zone   = item.TempZone;

            // Nếu là FROZEN, luôn ở REAR bất kể stop sequence
            if (item.TempZoneOrd == 0) zone = "REAR";

            var reason = BuildLoadReason(item.StopSeq, stopSequence.Count,
                                         item.TempZoneOrd, order.ExpectedWeightKg);

            result.Add(new LoadInstruction
            {
                LoadOrder           = i + 1,
                OrderId             = order.OrderId,
                TrackingCode        = order.TrackingCode,
                ItemName            = order.ItemName,
                WeightKg            = order.ExpectedWeightKg,
                Cbm                 = order.ExpectedCbm,
                TempCondition       = order.TempCondition,
                Zone                = zone,
                DeliveryLocationId  = order.DestLocation!.Value,
                DeliveryStopSequence = item.StopSeq,
                Reason              = reason
            });
        }

        return result;
    }

    // ── Helpers cho LIFO ────────────────────────────────────────────────────

    private static string ClassifyTempZone(string tempCondition)
    {
        // Phân tích TempCondition dạng: "-18C", "2-8C", "15-25C", "AMBIENT", "FROZEN", "CHILLED"
        var t = (tempCondition ?? "").ToUpperInvariant().Trim();
        if (t.Contains("FROZEN") || t.StartsWith("-") || t.Contains("-18"))
            return "REAR";
        if (t.Contains("CHILLED") || t.Contains("2-8") || t.Contains("0-4"))
            return "MID";
        return "FRONT";
    }

    private static int TempZoneOrder(string tempCondition)
    {
        var zone = ClassifyTempZone(tempCondition);
        return zone switch
        {
            "REAR"  => 0,   // frozen → ưu tiên xếp vào trước
            "MID"   => 1,
            "FRONT" => 2,
            _       => 3
        };
    }

    private static string BuildLoadReason(int stopSeq, int totalStops, int tempZoneOrd, decimal weight)
    {
        var parts = new List<string>();

        if (stopSeq == totalStops)
            parts.Add("Điểm giao cuối lộ trình → xếp sâu vào đuôi xe (LIFO)");
        else if (stopSeq == 1)
            parts.Add("Điểm giao đầu tiên → xếp gần cửa xe");
        else
            parts.Add($"Giao tại điểm #{stopSeq}/{totalStops}");

        parts.Add(tempZoneOrd == 0
            ? "Hàng đông lạnh → ngăn REAR"
            : tempZoneOrd == 1
                ? "Hàng mát → ngăn MID"
                : "Hàng nhiệt độ thường → ngăn FRONT");

        if (weight > 500)
            parts.Add($"Hàng nặng ({weight:F0}kg) → xếp phía dưới");

        return string.Join("; ", parts);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Gửi Notification cho Dispatcher
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<int> SendLoadingNotificationsAsync(
        MasterTrip trip,
        List<TransportOrder> orders,
        Vehicle vehicle,
        List<LoadInstruction> loadPlan,
        Guid? specificCoordinatorId)
    {
        List<Guid> targetUserIds;

        if (specificCoordinatorId.HasValue)
        {
            targetUserIds = new List<Guid> { specificCoordinatorId.Value };
        }
        else
        {
            // Tìm tất cả users có role Dispatcher
            targetUserIds = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role != null
                         && u.Role.RoleName == CoordinatorRoleName
                         && (u.Status == null || u.Status == "ACTIVE"))
                .Select(u => u.UserId)
                .ToListAsync();
        }

        if (targetUserIds.Count == 0) return 0;

        // Kiểm tra template tồn tại
        var templateExists = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == LoadingOrderTemplateId
                        && (t.Status == null || t.Status == "ACTIVE"));

        var count = 0;
        foreach (var userId in targetUserIds)
        {
            // Params cho template render
            var notifParams = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "tripId",      trip.TripId.ToString() },
                { "vehicle",     vehicle.TruckPlate },
                { "orderCount",  orders.Count.ToString() },
                { "firstLoad",   loadPlan.FirstOrDefault()?.ItemName ?? "-" },
                { "totalWeight", orders.Sum(o => o.ExpectedWeightKg).ToString("F1") },
                { "startTime",   trip.PlannedStartTime.ToString("dd/MM/yyyy HH:mm") }
            });

            // Dùng template DISPATCH_LOADING_ORDER nếu có, fallback template đơn giản
            var actualTemplateId = templateExists
                ? LoadingOrderTemplateId
                : await GetFallbackTemplateIdAsync();

            if (actualTemplateId == null) continue;

            _context.Notifications.Add(new Notification
            {
                NotiId     = Guid.NewGuid(),
                UserId     = userId,
                SenderId   = null,
                TemplateId = actualTemplateId,
                Params     = notifParams,
                OrderId    = null,
                IsRead     = false,
                CreatedAt  = DateTime.UtcNow
            });
            count++;
        }

        return count;
    }

    private async Task<string?> GetFallbackTemplateIdAsync()
    {
        // Lấy bất kỳ template nào đang active để dùng fallback
        return await _context.NotificationTemplates
            .Where(t => t.Status == null || t.Status == "ACTIVE")
            .Select(t => t.TemplateId)
            .FirstOrDefaultAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Tính nhiệt độ mục tiêu chuyến: lấy min của tất cả đơn hàng (đảm bảo an toàn nhất).</summary>
    private static decimal GetTargetTemperature(List<TransportOrder> orders)
    {
        // Phân tích TempCondition để lấy nhiệt độ thấp nhất (an toàn nhất cho cả chuyến)
        var minTemp = orders
            .Select(o => ParseMinTemp(o.TempCondition))
            .DefaultIfEmpty(4m)
            .Min();

        return minTemp;
    }

    private static decimal ParseMinTemp(string tempCondition)
    {
        var t = (tempCondition ?? "").ToUpperInvariant().Trim();
        if (t.Contains("FROZEN") || t.Contains("-18")) return -18m;
        if (t.Contains("CHILLED") || t.Contains("2-8")) return 2m;
        if (t.Contains("0-4")) return 0m;
        if (t.Contains("AMBIENT")) return 15m;

        // Cố parse số đầu tiên
        var firstPart = t.Split(new[] { '-', '~', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                         .FirstOrDefault();
        if (decimal.TryParse(firstPart?.Replace("C", ""), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return 4m; // default
    }

    /// <summary>Haversine formula — fallback khi Goong API không khả dụng.</summary>
    private static decimal HaversineKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double R = 6371.0;
        var dLat = ToRad((double)(lat2 - lat1));
        var dLon = ToRad((double)(lon2 - lon1));
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad((double)lat1)) * Math.Cos(ToRad((double)lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return (decimal)Math.Round(R * c, 2);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    // ── Internal result types ────────────────────────────────────────────────

    private class RouteCalculationResult
    {
        public decimal TotalDistanceKm { get; set; }
        public List<StopInfo> StopSequence { get; set; } = new();
    }

    private class StopInfo
    {
        public int     Sequence               { get; set; }
        public Guid    LocationId             { get; set; }
        public string  Address                { get; set; } = null!;
        public decimal Latitude               { get; set; }
        public decimal Longitude              { get; set; }
        public decimal DistanceFromPreviousKm { get; set; }
    }
    // ═══════════════════════════════════════════════════════════════════════
    //  API 1: MANUAL DISPATCH — Ghép chuyến thủ công
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<ManualDispatchResult> ManualDispatchAsync(ManualDispatchRequest request)
    {
        // 1. Validate kho xuất phát
        var originLocation = await _context.Locations.FindAsync(request.OriginWarehouseLocationId)
            ?? throw new InvalidOperationException("LocationId kho xuất phát không tồn tại.");

        // 2. Validate đơn hàng
        var orders = await _context.TransportOrders
            .Include(o => o.DestLocationNavigation)
            .Include(o => o.WarehouseReceipts)
            .Where(o => request.OrderIds.Contains(o.OrderId))
            .ToListAsync();

        if (orders.Count == 0)
            throw new InvalidOperationException("Không tìm thấy đơn hàng nào khớp với danh sách đã chọn.");

        if (orders.Any(o => o.Status != "IN_WAREHOUSE" || !o.WarehouseReceipts.Any()))
            throw new InvalidOperationException("Chỉ được ghép chuyến các đơn hàng có trạng thái IN_WAREHOUSE và đã có phiếu nhập kho.");

        var warehouseIds = orders
            .SelectMany(o => o.WarehouseReceipts)
            .Select(w => w.WarehouseId)
            .Distinct()
            .ToList();

        if (warehouseIds.Count > 1)
            throw new InvalidOperationException("Tất cả các đơn hàng được chọn phải cùng thuộc một kho lưu trữ (WarehouseId).");

        var missingOrders = request.OrderIds.Except(orders.Select(o => o.OrderId)).ToList();
        if (missingOrders.Any())
            throw new InvalidOperationException($"Không tìm thấy các đơn hàng sau: {string.Join(", ", missingOrders)}");

        if (orders.Any(o => o.PickupLocation != request.OriginWarehouseLocationId))
            throw new InvalidOperationException("Tất cả các đơn hàng được chọn phải cùng thuộc một kho xuất phát.");

        // 3. Check nhiệt độ
        var firstTemp = NormalizeTempGroup(orders.First().TempCondition);
        if (orders.Any(o => NormalizeTempGroup(o.TempCondition) != firstTemp))
            throw new InvalidOperationException("Tất cả các đơn hàng phải có cùng yêu cầu nhiệt độ (TempCondition).");

        // 4. Validate xe + tài xế
        var vehicle = await _context.Vehicles
            .Include(v => v.Driver)
                .ThenInclude(d => d!.DriverLicenses)
            .FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId)
            ?? throw new InvalidOperationException("Không tìm thấy xe (Vehicle) đã chọn.");

        if (vehicle.Status != "ACTIVE")
            throw new InvalidOperationException($"Xe {vehicle.TruckPlate} không ở trạng thái ACTIVE.");

        if (vehicle.DriverId == null || vehicle.Driver == null)
            throw new InvalidOperationException($"Xe {vehicle.TruckPlate} chưa được gán tài xế.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeLicense = vehicle.Driver.DriverLicenses
            .Where(l => l.ExpiryDate >= today && (l.Status == null || l.Status == "ACTIVE"))
            .OrderByDescending(l => l.ExpiryDate)
            .FirstOrDefault();

        if (activeLicense == null)
            throw new InvalidOperationException($"Tài xế {vehicle.Driver.FullName} không có bằng lái còn hạn.");

        // Lấy danh sách TripId đang active để check xe bận (không coi trạng thái PLANNED là bận)
        var isBusy = await _context.MasterTrips
            .AnyAsync(t => t.VehicleId == request.VehicleId
                        && (t.Status == "LOADING"
                         || t.Status == "SEALED" || t.Status == "DISPATCHED"
                         || t.Status == "PENDING_WH_APPROVAL"));

        if (isBusy)
            throw new InvalidOperationException($"Xe {vehicle.TruckPlate} hiện đang bận một chuyến khác.");

        // 5. Kiểm tra tải trọng
        var totalWeight = orders.Sum(o => o.ExpectedWeightKg);
        var totalCbm = orders.Sum(o => o.ExpectedCbm);
        var requiredMinTemp = GetTargetTemperature(orders);

        if (totalWeight > vehicle.MaxWeight || totalCbm > vehicle.MaxCbm)
            throw new InvalidOperationException(
                $"Quá tải: Tổng hàng hóa (Weight: {totalWeight:F1}kg, CBM: {totalCbm:F1}m³) vượt quá sức chứa của xe (MaxWeight: {vehicle.MaxWeight}kg, MaxCbm: {vehicle.MaxCbm}m³).");

        // 6. Tính lộ trình (TSP + Goong)
        var routeResult = await BuildOptimalRouteAsync(originLocation, orders, vehicle);

        // 7. LIFO load plan
        var loadPlan = BuildLIFOLoadPlan(orders, routeResult.StopSequence);

        // 8. Navigation (Goong)
        var navigationWaypoints = new List<(decimal Lat, decimal Lon, string Address)>
        {
            (originLocation.Latitude, originLocation.Longitude, originLocation.Address)
        };
        foreach (var stop in routeResult.StopSequence)
        {
            navigationWaypoints.Add((stop.Latitude, stop.Longitude, stop.Address));
        }

        GoongDirectionsResult directionsResult;
        try
        {
            directionsResult = await _locationService.GetDirectionsAsync(navigationWaypoints);
        }
        catch
        {
            directionsResult = new GoongDirectionsResult
            {
                TotalDistanceKm = routeResult.TotalDistanceKm,
                TotalDurationSeconds = (int)(routeResult.TotalDistanceKm / 40m * 3600m),
                Legs = new List<GoongLeg>()
            };
        }

        // 9. Tạo Trip và lưu DB
        var masterTrip = new MasterTrip
        {
            TripId              = Guid.NewGuid(),
            VehicleId           = vehicle.VehicleId,
            DriverId            = vehicle.DriverId,
            OriginLocationId    = originLocation.LocationId,
            DestinationLocationId = routeResult.StopSequence.Last().LocationId,
            TotalDistanceKm     = routeResult.TotalDistanceKm,
            TargetTemperature   = requiredMinTemp,
            PlannedStartTime    = request.PlannedStartTime,
            PlannedEndTime      = request.PlannedEndTime,
            Status              = "PLANNED",
            CreatedAt           = DateTime.UtcNow,
        };
        _context.MasterTrips.Add(masterTrip);

        // TripStops
        var stopGapHours = (request.PlannedEndTime - request.PlannedStartTime).TotalHours
                           / Math.Max(routeResult.StopSequence.Count, 1);
        foreach (var stop in routeResult.StopSequence)
        {
            var plannedArrival = request.PlannedStartTime.AddHours(stopGapHours * stop.Sequence);
            _context.TripStops.Add(new TripStop
            {
                StopId               = Guid.NewGuid(),
                TripId               = masterTrip.TripId,
                LocationId           = stop.LocationId,
                StopSequence         = stop.Sequence,
                StopType             = "DELIVERY",
                Status               = "PLANNED",
                PlannedArrivalTime   = plannedArrival,
                PlannedDepartureTime = plannedArrival.AddMinutes(30),
                CreatedAt            = DateTime.UtcNow
            });
        }

        // Liên kết đơn hàng với chuyến đi nhưng giữ nguyên trạng thái IN_WAREHOUSE
        foreach (var order in orders)
        {
            order.MasterTripId = masterTrip.TripId;
        }

        var notifiedCount = 0;
        await _context.SaveChangesAsync();

        // 10. Build response
        var routeStops = routeResult.StopSequence.Select(s => new RouteStop
        {
            Sequence = s.Sequence, LocationId = s.LocationId, Address = s.Address,
            Latitude = s.Latitude, Longitude = s.Longitude, DistanceFromPreviousKm = s.DistanceFromPreviousKm,
            OrdersToUnload = orders.Where(o => o.DestLocation == s.LocationId).Select(o => new OrderSummary
            { OrderId = o.OrderId, TrackingCode = o.TrackingCode, ItemName = o.ItemName, Quantity = o.Quantity, WeightKg = o.ExpectedWeightKg, Cbm = o.ExpectedCbm, TempCondition = o.TempCondition }).ToList()
        }).ToList();

        var routeInfo = new RouteInfo 
        { 
            TotalDistanceKm = routeResult.TotalDistanceKm, 
            TotalStops = routeStops.Count, 
            OriginLat = originLocation.Latitude,
            OriginLng = originLocation.Longitude,
            Stops = routeStops 
        };

        var dispatchInstructions = loadPlan.Select(li => new DispatchInstruction
        { OrderId = li.OrderId, TrackingCode = li.TrackingCode, ItemName = li.ItemName, Action = "LOAD", PreviousStatus = "IN_WAREHOUSE", TargetStatus = "IN_WAREHOUSE", LoadOrder = li.LoadOrder, Zone = li.Zone }).OrderBy(d => d.LoadOrder).ToList();

        var daysToExpiry = activeLicense.ExpiryDate.DayNumber - today.DayNumber;
        var licenseStatus = daysToExpiry <= 30 ? "EXPIRING_SOON" : "VALID";

        return new ManualDispatchResult
        {
            TripId = masterTrip.TripId,
            Vehicle = new VehicleInfo { VehicleId = vehicle.VehicleId, TruckPlate = vehicle.TruckPlate, MaxWeightKg = vehicle.MaxWeight, MaxCbm = vehicle.MaxCbm, TotalOrderWeightKg = totalWeight, TotalOrderCbm = totalCbm, WeightUtilizationPct = Math.Round(totalWeight / vehicle.MaxWeight * 100, 1), CbmUtilizationPct = Math.Round(totalCbm / vehicle.MaxCbm * 100, 1) },
            Driver = new DriverInfo { DriverId = vehicle.Driver.DriverId, FullName = vehicle.Driver.FullName, PhoneNumber = vehicle.Driver.PhoneNumber, IdentityNumber = vehicle.Driver.IdentityNumber, LicenseClass = activeLicense.LicenseClass, LicenseExpiry = activeLicense.ExpiryDate, LicenseStatus = licenseStatus },
            SelectedOrders = orders.Select(o => new OrderSummary { OrderId = o.OrderId, TrackingCode = o.TrackingCode, ItemName = o.ItemName, Quantity = o.Quantity, WeightKg = o.ExpectedWeightKg, Cbm = o.ExpectedCbm, TempCondition = o.TempCondition }).ToList(),
            Route = routeInfo,
            Navigation = new NavigationInfo { TotalDistanceKm = directionsResult.TotalDistanceKm, TotalDurationMinutes = directionsResult.TotalDurationSeconds / 60, GoongRouteOverview = directionsResult.OverviewPolyline ?? "", Legs = directionsResult.Legs.Select((leg, idx) => new NavigationLeg { LegIndex = idx + 1, FromAddress = leg.StartAddress ?? "N/A", ToAddress = leg.EndAddress ?? "N/A", DistanceKm = leg.DistanceKm, DurationMinutes = leg.DurationSeconds / 60, Steps = leg.Steps.Select((step, sIdx) => new NavigationStep { StepIndex = sIdx + 1, Instruction = step.Instruction, DistanceKm = step.DistanceKm, DurationSeconds = step.DurationSeconds, Maneuver = step.Maneuver }).ToList() }).ToList() },
            LoadPlan = loadPlan,
            DispatchInstructions = dispatchInstructions,
            NotifiedCoordinators = notifiedCount
        };
    }

    private string NormalizeTempGroup(string tempCondition)
    {
        if (string.IsNullOrWhiteSpace(tempCondition)) return "AMBIENT";
        var t = tempCondition.ToUpperInvariant();
        if (t.Contains("FROZEN") || t.Contains("-20")) return "FROZEN";
        if (t.Contains("CHILLED") || t.Contains("2 TO 8")) return "CHILLED";
        return "AMBIENT";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 2: WAREHOUSE ORDER — Lệnh bốc xếp cho kho
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<WarehouseOrderResult> CreateWarehouseOrderAsync(Guid tripId, Guid createdBy)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến hàng.");

        if (trip.Status != "PLANNED" && trip.Status != "DISPATCHED_PENDING"
            && trip.Status != "SEALED" && trip.Status != "LOADING")
            throw new InvalidOperationException(
                $"Chuyến hàng đang ở trạng thái '{trip.Status}', " +
                $"chỉ có thể tạo lệnh kho khi trạng thái là PLANNED hoặc DISPATCHED_PENDING.");

        // Chuyển trip sang PENDING_WH_APPROVAL
        trip.Status = "PENDING_WH_APPROVAL";

        // Lấy load plan để gửi kèm
        var stops = await _context.TripStops
            .Where(ts => ts.TripId == tripId)
            .OrderBy(ts => ts.StopSequence)
            .ToListAsync();
        var stopInfos = stops.Select(s => new StopInfo
        {
            LocationId = s.LocationId ?? Guid.Empty,
            Sequence = s.StopSequence
        }).ToList();
        var loadPlan = BuildLIFOLoadPlan(trip.TransportOrders.ToList(), stopInfos);

        // Gửi notification cho WarehouseMonitor (fallback Admin)
        var whMonitorIds = await _context.Users
            .Include(u => u.Role)
            .Where(u => u.Role != null
                     && (u.Role.RoleName == "WarehouseMonitor" || u.Role.RoleName == "Admin" || u.Role.RoleName == "ADMIN")
                     && (u.Status == null || u.Status == "ACTIVE"))
            .Select(u => u.UserId)
            .ToListAsync();

        var notifiedCount = 0;
        var actualTemplateId = await GetOrCreateTemplateAsync("DISPATCH_WH_ORDER",
            "Lệnh bốc xếp chờ duyệt — Xe {vehicle}",
            "Chuyến hàng {tripId} cần duyệt bốc xếp {orderCount} đơn hàng, tổng {totalWeight}kg.");

        foreach (var userId in whMonitorIds)
        {
            var notifParams = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "tripId", tripId.ToString() },
                { "vehicle", trip.Vehicle?.TruckPlate ?? "N/A" },
                { "orderCount", trip.TransportOrders.Count.ToString() },
                { "totalWeight", trip.TransportOrders.Sum(o => o.ExpectedWeightKg).ToString("F1") },
                { "action", "Duyệt hoặc từ chối lệnh bốc xếp" }
            });

            _context.Notifications.Add(new Notification
            {
                NotiId = Guid.NewGuid(),
                UserId = userId,
                SenderId = createdBy,
                TemplateId = actualTemplateId,
                Params = notifParams,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            notifiedCount++;
        }

        await _context.SaveChangesAsync();

        try
        {
            await _hubContext.Clients.Groups("Group_WarehouseMonitor", "Group_Admin")
                .SendAsync("WarehouseOrderCreated", new
                {
                    TripId = tripId,
                    Status = "PENDING_WH_APPROVAL",
                    Vehicle = trip.Vehicle?.TruckPlate ?? "N/A",
                    OrderCount = trip.TransportOrders.Count,
                    TotalWeight = trip.TransportOrders.Sum(o => o.ExpectedWeightKg)
                });
        }
        catch (Exception)
        {
            // Fail-safe to avoid blocking API if SignalR fails
        }

        return new WarehouseOrderResult
        {
            TripId = tripId,
            Status = "PENDING_WH_APPROVAL",
            Vehicle = trip.Vehicle == null ? null : new VehicleInfo
            {
                VehicleId = trip.Vehicle.VehicleId,
                TruckPlate = trip.Vehicle.TruckPlate,
                MaxWeightKg = trip.Vehicle.MaxWeight,
                MaxCbm = trip.Vehicle.MaxCbm,
                TotalOrderWeightKg = trip.TransportOrders.Sum(o => o.ExpectedWeightKg),
                TotalOrderCbm = trip.TransportOrders.Sum(o => o.ExpectedCbm)
            },
            Orders = trip.TransportOrders.Select(o => new OrderSummary
            {
                OrderId = o.OrderId,
                TrackingCode = o.TrackingCode,
                ItemName = o.ItemName,
                Quantity = o.Quantity,
                WeightKg = o.ExpectedWeightKg,
                Cbm = o.ExpectedCbm,
                TempCondition = o.TempCondition
            }).ToList(),
            LoadPlan = loadPlan,
            NotifiedUsers = notifiedCount
        };
    }

    public async Task<WarehouseOrderResult> ApproveWarehouseOrderAsync(Guid tripId, Guid approvedBy)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến hàng.");

        if (trip.Status != "PENDING_WH_APPROVAL")
            throw new InvalidOperationException(
                $"Không thể duyệt — chuyến đang ở trạng thái '{trip.Status}'. " +
                $"Chỉ duyệt được khi trạng thái là PENDING_WH_APPROVAL.");

        // Approve: chuyển trip → LOADING, orders → LOADING
        trip.Status = "LOADING";
        foreach (var order in trip.TransportOrders)
        {
            order.Status = "LOADING";
        }

        // Gửi notification cho Loader
        await NotifyLoadersAsync(tripId);

        await _context.SaveChangesAsync();

        try
        {
            await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin")
                .SendAsync("WarehouseOrderApproved", new
                {
                    TripId = tripId,
                    Status = "LOADING",
                    ApprovedBy = approvedBy
                });
        }
        catch (Exception)
        {
            // Fail-safe to avoid blocking API if SignalR fails
        }

        return new WarehouseOrderResult
        {
            TripId = tripId,
            Status = "APPROVED",
            ApprovedBy = approvedBy,
            ApprovedAt = DateTime.UtcNow,
            Orders = trip.TransportOrders.Select(o => new OrderSummary
            {
                OrderId = o.OrderId,
                TrackingCode = o.TrackingCode,
                ItemName = o.ItemName,
                Quantity = o.Quantity,
                WeightKg = o.ExpectedWeightKg,
                Cbm = o.ExpectedCbm,
                TempCondition = o.TempCondition
            }).ToList(),
            NotifiedUsers = 1
        };
    }

    public async Task<WarehouseOrderResult> RejectWarehouseOrderAsync(
        Guid tripId, Guid rejectedBy, string reason)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến hàng.");

        if (trip.Status != "PENDING_WH_APPROVAL")
            throw new InvalidOperationException(
                $"Không thể từ chối — chuyến đang ở trạng thái '{trip.Status}'.");

        // Reject: chuyển trip → WH_REJECTED, orders → IN_WAREHOUSE
        trip.Status = "WH_REJECTED";
        foreach (var order in trip.TransportOrders)
        {
            order.Status = "IN_WAREHOUSE";
            order.MasterTripId = null;
        }

        await _context.SaveChangesAsync();

        try
        {
            await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin")
                .SendAsync("WarehouseOrderRejected", new
                {
                    TripId = tripId,
                    Status = "WH_REJECTED",
                    RejectionReason = reason
                });
        }
        catch (Exception)
        {
            // Fail-safe to avoid blocking API if SignalR fails
        }

        return new WarehouseOrderResult
        {
            TripId = tripId,
            Status = "WH_REJECTED",
            RejectionReason = reason,
            Orders = trip.TransportOrders.Select(o => new OrderSummary
            {
                OrderId = o.OrderId,
                TrackingCode = o.TrackingCode,
                ItemName = o.ItemName,
                Quantity = o.Quantity,
                WeightKg = o.ExpectedWeightKg,
                Cbm = o.ExpectedCbm,
                TempCondition = o.TempCondition
            }).ToList()
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 3: IOT CHECK — Kiểm tra tín hiệu IoT xe
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<VehicleIoTStatus> CheckVehicleIoTAsync(Guid vehicleId)
    {
        var vehicle = await _context.Vehicles.FindAsync(vehicleId)
            ?? throw new KeyNotFoundException("Không tìm thấy xe.");

        var devices = await _context.IotDevices
            .Where(d => d.VehicleId == vehicleId)
            .ToListAsync();

        if (devices.Count == 0)
        {
            return new VehicleIoTStatus
            {
                VehicleId = vehicleId,
                TruckPlate = vehicle.TruckPlate,
                HasIoTDevices = false,
                OverallStatus = "NO_DEVICE",
                Devices = new List<IoTDeviceStatus>()
            };
        }

        var now = DateTime.UtcNow;
        var deviceStatuses = new List<IoTDeviceStatus>();

        foreach (var device in devices)
        {
            // Lấy telemetry gần nhất
            var latestTelemetry = await _context.TelemetryLogs
                .Where(t => t.DeviceId == device.DeviceId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync();

            var isOnline = device.LastPingTime.HasValue
                        && (now - device.LastPingTime.Value).TotalMinutes < 10;

            deviceStatuses.Add(new IoTDeviceStatus
            {
                DeviceId = device.DeviceId,
                BatteryLevel = device.BatteryLevel,
                LastPingTime = device.LastPingTime,
                Status = device.Status,
                IsOnline = isOnline,
                LatestTelemetry = latestTelemetry == null ? null : new LatestTelemetry
                {
                    Temperature = latestTelemetry.Temperature,
                    Latitude = latestTelemetry.Latitude,
                    Longitude = latestTelemetry.Longitude,
                    Timestamp = latestTelemetry.Timestamp
                }
            });
        }

        var onlineCount = deviceStatuses.Count(d => d.IsOnline);
        var overallStatus = onlineCount == devices.Count ? "ONLINE"
                          : onlineCount > 0 ? "PARTIAL"
                          : "OFFLINE";

        return new VehicleIoTStatus
        {
            VehicleId = vehicleId,
            TruckPlate = vehicle.TruckPlate,
            HasIoTDevices = true,
            OverallStatus = overallStatus,
            Devices = deviceStatuses
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 4: SEAL & DISPATCH — Kẹp chì + kiểm tra chất hàng
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<SealAndDispatchResult> SealAndDispatchAsync(
        Guid tripId, string sealCode, Guid sealedBy)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.TransportOrders)
            .Include(t => t.Seals)
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến hàng.");

        // Kiểm tra trạng thái: phải là LOADING (đã được WH approve)
        if (trip.Status != "LOADING")
            throw new InvalidOperationException(
                $"Không thể kẹp chì — chuyến đang ở trạng thái '{trip.Status}'. " +
                $"Chỉ kẹp chì được khi trạng thái là LOADING (đã được kho duyệt).");

        // Kiểm tra đã kẹp chì chưa
        if (trip.Seals.Any(s => s.Status == "APPLIED"))
            throw new InvalidOperationException("Chuyến hàng đã được kẹp chì trước đó.");

        // Kiểm tra tất cả đơn hàng đã được chất hết chưa
        var totalOrders = trip.TransportOrders.Count;
        var loadedOrders = trip.TransportOrders
            .Count(o => o.Status == "LOADING" || o.Status == "LOADED");
        var allLoaded = loadedOrders == totalOrders && totalOrders > 0;

        if (!allLoaded)
        {
            var notLoadedOrders = trip.TransportOrders
                .Where(o => o.Status != "LOADING" && o.Status != "LOADED")
                .Select(o => o.TrackingCode)
                .ToList();
            throw new InvalidOperationException(
                $"Chưa chất hết hàng! Còn {totalOrders - loadedOrders}/{totalOrders} đơn chưa xếp: " +
                $"{string.Join(", ", notLoadedOrders)}. " +
                $"Tất cả đơn phải ở trạng thái LOADING hoặc LOADED trước khi kẹp chì.");
        }

        // Tạo Seal record
        _context.Seals.Add(new Seal
        {
            SealId    = Guid.NewGuid(),
            TripId    = tripId,
            SealCode  = sealCode,
            AppliedAt = DateTime.UtcNow,
            Status    = "APPLIED",
            CreatedAt = DateTime.UtcNow
        });

        // Chuyển trip → SEALED
        trip.Status = "SEALED";

        // Cập nhật orders → SEALED
        foreach (var order in trip.TransportOrders)
        {
            order.Status = "SEALED";

            // Tạo OutboundOrder và OutboundOrderItem
            var outboundOrder = new OutboundOrder
            {
                OutboundOrderId = Guid.NewGuid(),
                OrderCode = $"OUT-{DateTime.UtcNow:yyyyMMddHHmmss}-{order.TrackingCode}",
                CustomerId = order.CustomerId ?? Guid.Empty,
                ReceiverName = "Default Receiver",
                ReceiverPhone = "0000000000",
                DestinationAddress = order.DestLocationNavigation?.Address ?? "Unknown Address",
                Status = ColdChainX.Core.Enums.OutboundOrderStatus.SHIPPED,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = sealedBy
            };

            var outboundItem = new OutboundOrderItem
            {
                OutboundOrderItemId = Guid.NewGuid(),
                OutboundOrderId = outboundOrder.OutboundOrderId,
                ItemCode = order.TrackingCode,
                ItemName = order.ItemName,
                Unit = order.PackingType,
                Quantity = order.Quantity
            };

            _context.OutboundOrders.Add(outboundOrder);
            _context.OutboundOrderItems.Add(outboundItem);
        }

        // Issue E-Waybill
        string? waybillUrl = null;
        try
        {
            waybillUrl = await GenerateWaybillPdfAsync(trip);
            var documentUploader = sealedBy;
            if (trip.DriverId.HasValue && await _context.Users.AnyAsync(u => u.UserId == trip.DriverId.Value))
            {
                documentUploader = trip.DriverId.Value;
            }
            _context.TransportDocuments.Add(new TransportDocument
            {
                DocId = Guid.NewGuid(),
                DocType = "E-WAYBILL",
                ImageUrl = waybillUrl,
                Status = "ISSUED",
                CreatedAt = DateTime.UtcNow,
                UploadedBy = documentUploader
            });
            trip.Status = "DISPATCHED";
        }
        catch
        {
            // Waybill generation failed, keep status as SEALED
        }

        await _context.SaveChangesAsync();

        return new SealAndDispatchResult
        {
            TripId = tripId,
            SealCode = sealCode,
            AllOrdersLoaded = allLoaded,
            TotalOrders = totalOrders,
            LoadedOrders = loadedOrders,
            SealedAt = DateTime.UtcNow,
            SealedBy = sealedBy,
            TripStatus = trip.Status ?? "SEALED",
            WaybillUrl = waybillUrl
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BACKLOG — Xử lý hàng tồn
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<BacklogDispatchResult> ProcessBacklogOrdersAsync(
        Guid originLocationId, DateTime plannedStart, DateTime plannedEnd, int backlogDays = 1)
    {
        var originLocation = await _context.Locations.FindAsync(originLocationId)
            ?? throw new InvalidOperationException("LocationId kho xuất phát không tồn tại.");

        var cutoffDate = DateTime.UtcNow.AddDays(-backlogDays);

        // Quét đơn hàng tồn kho lâu
        var backlogOrders = await _context.TransportOrders
            .Include(o => o.DestLocationNavigation)
            .Where(o => o.Status == "IN_WAREHOUSE"
                     && o.WarehouseReceipts.Any()
                     && o.CreatedAt.HasValue
                     && o.CreatedAt.Value < cutoffDate)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        if (backlogOrders.Count == 0)
            throw new InvalidOperationException(
                $"Không có đơn hàng tồn kho quá {backlogDays} ngày.");

        var result = new BacklogDispatchResult();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Nhóm theo nhiệt độ
        var tempGroups = backlogOrders
            .Where(o => o.DestLocation.HasValue && o.DestLocationNavigation != null)
            .GroupBy(o => NormalizeTempGroup(o.TempCondition))
            .ToList();

        // Lấy danh sách xe bận
        var busyVehicleIds = await _context.MasterTrips
            .Where(t => t.Status == "PLANNED" || t.Status == "LOADING"
                     || t.Status == "SEALED" || t.Status == "DISPATCHED"
                     || t.Status == "PENDING_WH_APPROVAL")
            .Where(t => t.VehicleId.HasValue)
            .Select(t => t.VehicleId!.Value)
            .Distinct()
            .ToListAsync();

        foreach (var group in tempGroups)
        {
            var batchOrders = group.ToList();
            var batchWeight = batchOrders.Sum(o => o.ExpectedWeightKg);
            var batchCbm = batchOrders.Sum(o => o.ExpectedCbm);
            var batchMinTemp = GetTargetTemperature(batchOrders);

            // Tìm xe nhỏ (≤ 2000kg) phù hợp
            var smallVehicle = await _context.Vehicles
                .Include(v => v.Driver)
                    .ThenInclude(d => d!.DriverLicenses)
                .Where(v => v.Status == "ACTIVE"
                         && v.DriverId != null
                         && v.MaxWeight <= 2000m
                         && v.MaxWeight >= batchWeight
                         && v.MaxCbm >= batchCbm
                         && v.MinTemp <= batchMinTemp)
                .ToListAsync();

            var eligibleSmallVehicle = smallVehicle
                .Where(v => !busyVehicleIds.Contains(v.VehicleId))
                .Where(v => v.Driver != null
                         && v.Driver.DriverLicenses.Any(l =>
                             l.ExpiryDate >= today
                             && (l.Status == null || l.Status == "ACTIVE")))
                .OrderBy(v => v.MaxWeight)
                .FirstOrDefault();

            if (eligibleSmallVehicle == null)
            {
                // Không có xe nhỏ phù hợp → skip
                result.SkippedOrders.AddRange(batchOrders.Select(o => new OrderSummary
                {
                    OrderId = o.OrderId,
                    TrackingCode = o.TrackingCode,
                    ItemName = o.ItemName,
                    Quantity = o.Quantity,
                    WeightKg = o.ExpectedWeightKg,
                    Cbm = o.ExpectedCbm,
                    TempCondition = o.TempCondition
                }));
                continue;
            }

            var driver = eligibleSmallVehicle.Driver!;

            // Tính lộ trình
            var routeResult = await BuildOptimalRouteAsync(
                originLocation, batchOrders, eligibleSmallVehicle);

            // Tạo MasterTrip
            var lastDestId = routeResult.StopSequence.Last().LocationId;
            var trip = new MasterTrip
            {
                TripId = Guid.NewGuid(),
                VehicleId = eligibleSmallVehicle.VehicleId,
                DriverId = driver.DriverId,
                OriginLocationId = originLocation.LocationId,
                DestinationLocationId = lastDestId,
                TotalDistanceKm = routeResult.TotalDistanceKm,
                TargetTemperature = batchMinTemp,
                PlannedStartTime = plannedStart,
                PlannedEndTime = plannedEnd,
                Status = "PLANNED",
                CreatedAt = DateTime.UtcNow,
            };
            _context.MasterTrips.Add(trip);

            // Tạo TripStops
            foreach (var stop in routeResult.StopSequence)
            {
                _context.TripStops.Add(new TripStop
                {
                    StopId = Guid.NewGuid(),
                    TripId = trip.TripId,
                    LocationId = stop.LocationId,
                    StopSequence = stop.Sequence,
                    StopType = "DELIVERY",
                    Status = "PLANNED",
                    PlannedArrivalTime = plannedStart.AddHours(stop.Sequence),
                    PlannedDepartureTime = plannedStart.AddHours(stop.Sequence).AddMinutes(20),
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Cập nhật orders
            foreach (var order in batchOrders)
            {
                order.Status = "DISPATCHED_PENDING";
                order.MasterTripId = trip.TripId;
            }

            busyVehicleIds.Add(eligibleSmallVehicle.VehicleId);

            result.DispatchedTrips.Add(new BacklogTripSummary
            {
                TripId = trip.TripId,
                TruckPlate = eligibleSmallVehicle.TruckPlate,
                DriverName = driver.FullName,
                OrderCount = batchOrders.Count,
                TotalWeightKg = batchWeight,
                TempCondition = group.Key
            });
            result.TotalProcessed += batchOrders.Count;
        }

        result.TotalSkipped = result.SkippedOrders.Count;

        await _context.SaveChangesAsync();

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Helper: Tạo/lấy notification template
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<string> GetOrCreateTemplateAsync(
        string templateId, string titleTemplate, string bodyTemplate)
    {
        var exists = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == templateId);

        if (!exists)
        {
            var msgType = await _context.Messagetypes.FirstOrDefaultAsync();
            if (msgType != null)
            {
                _context.NotificationTemplates.Add(new NotificationTemplate
                {
                    TemplateId = templateId,
                    TypeId = msgType.TypeId,
                    TitleTemplate = titleTemplate,
                    BodyTemplate = bodyTemplate,
                    Channel = "IN_APP",
                    Status = "ACTIVE"
                });
                await _context.SaveChangesAsync();
                return templateId;
            }
            // Fallback
            return await GetFallbackTemplateIdAsync() ?? templateId;
        }

        return templateId;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LEGACY methods (kept for backward compatibility)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<string> SuggestLoadPlanAsync(List<Guid> orderIds, Guid vehicleId)
    {
        var vehicle = await _context.Vehicles.FindAsync(vehicleId)
            ?? throw new Exception("Vehicle not found.");

        var orders = await _context.TransportOrders
            .Where(o => orderIds.Contains(o.OrderId))
            .ToListAsync();

        if (orders.Count == 0) throw new Exception("No orders found.");

        decimal totalWeight = orders.Sum(o => o.ExpectedWeightKg);

        if (totalWeight > vehicle.MaxWeight)
            throw new InvalidOperationException(
                $"Overload Error: Total weight ({totalWeight}kg) exceeds vehicle capacity ({vehicle.MaxWeight}kg).");

        var loadPlanJson = await _geminiClient.OptimizeLoadPlanAsync(vehicle, orders, new List<Guid>());
        return loadPlanJson;
    }

    public async Task CalculateRouteAndLIFOAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new Exception("Trip not found.");

        if (!trip.TransportOrders.Any()) throw new Exception("Trip has no orders.");

        var destLocationIds = trip.TransportOrders
            .Where(o => o.DestLocation.HasValue)
            .Select(o => o.DestLocation!.Value)
            .Distinct()
            .ToList();

        int seq = 1;
        var existingStops = await _context.TripStops.Where(ts => ts.TripId == tripId).ToListAsync();
        _context.TripStops.RemoveRange(existingStops);

        foreach (var locId in destLocationIds)
        {
            _context.TripStops.Add(new TripStop
            {
                StopId               = Guid.NewGuid(),
                TripId               = tripId,
                LocationId           = locId,
                StopSequence         = seq++,
                StopType             = "DELIVERY",
                Status               = "PLANNED",
                PlannedArrivalTime   = trip.PlannedStartTime.AddHours(seq),
                PlannedDepartureTime = trip.PlannedStartTime.AddHours(seq).AddMinutes(30),
                CreatedAt            = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task SealTruckAsync(Guid tripId, string sealCode, Guid warehouseKeeperId)
    {
        var trip = await _context.MasterTrips.FindAsync(tripId)
            ?? throw new Exception("Trip not found.");

        _context.Seals.Add(new Seal
        {
            SealId    = Guid.NewGuid(),
            TripId    = tripId,
            SealCode  = sealCode,
            AppliedAt = DateTime.UtcNow,
            Status    = "APPLIED",
            CreatedAt = DateTime.UtcNow
        });

        trip.Status = "SEALED";
        await _context.SaveChangesAsync();
    }

    public async Task IssueDispatchDocumentsAsync(Guid tripId, Guid? issuerId = null)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.TransportOrders)
                .ThenInclude(o => o.Customer)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new Exception("Trip not found.");

        var pdfUrl = await GenerateWaybillPdfAsync(trip);

        var documentUploader = issuerId ?? Guid.Empty;
        if (trip.DriverId.HasValue && await _context.Users.AnyAsync(u => u.UserId == trip.DriverId.Value))
        {
            documentUploader = trip.DriverId.Value;
        }
        else if (documentUploader == Guid.Empty || !await _context.Users.AnyAsync(u => u.UserId == documentUploader))
        {
            var fallbackUser = await _context.Users.FirstOrDefaultAsync(u => u.DeletedAt == null);
            if (fallbackUser != null)
            {
                documentUploader = fallbackUser.UserId;
            }
        }

        _context.TransportDocuments.Add(new TransportDocument
        {
            DocId     = Guid.NewGuid(),
            DocType   = "E-WAYBILL",
            ImageUrl  = pdfUrl,
            Status    = "ISSUED",
            CreatedAt = DateTime.UtcNow,
            UploadedBy = documentUploader
        });

        trip.Status = "DISPATCHED";
        await _context.SaveChangesAsync();
    }

    private async Task<string> GenerateWaybillPdfAsync(MasterTrip trip)
    {
        var templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "WaybillTemplate.html");
        if (!File.Exists(templatePath))
            throw new InvalidOperationException("WaybillTemplate.html template was not found");

        var html = await File.ReadAllTextAsync(templatePath);

        var ordersRows = "";
        int no = 1;
        foreach (var order in trip.TransportOrders)
        {
            ordersRows += $@"
            <tr>
                <td>{no}</td>
                <td>{order.TrackingCode}</td>
                <td>{order.Customer?.CompanyName ?? "Khách hàng vãng lai"}</td>
                <td>{order.ItemName}</td>
                <td>{order.Quantity}</td>
                <td>{order.ExpectedWeightKg:0.##} kg</td>
                <td>{order.TempCondition}</td>
            </tr>";
            no++;
        }

        var replacements = new Dictionary<string, string?>
        {
            ["Trip_Id"] = trip.TripId.ToString(),
            ["Issue_Date"] = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["Truck_Plate"] = trip.Vehicle?.TruckPlate ?? "N/A",
            ["Vehicle_Type"] = trip.Vehicle?.VehicleType ?? "N/A",
            ["Driver_Name"] = trip.Driver?.FullName ?? "N/A",
            ["Driver_Phone"] = trip.Driver?.PhoneNumber ?? "N/A",
            ["Driver_Identity"] = trip.Driver?.IdentityNumber ?? "N/A",
            ["Origin_Address"] = trip.OriginLocation?.Address ?? "N/A",
            ["Dest_Address"] = trip.DestinationLocation?.Address ?? "N/A",
            ["Total_Distance"] = trip.TotalDistanceKm?.ToString("F1", CultureInfo.InvariantCulture) ?? "0",
            ["Target_Temp"] = trip.TargetTemperature.ToString("F1", CultureInfo.InvariantCulture),
            ["Planned_Start"] = trip.PlannedStartTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["Planned_End"] = trip.PlannedEndTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["Orders_Table_Rows"] = ordersRows
        };

        foreach (var replacement in replacements)
            html = html.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

        return await _pdfService.SaveWaybillPdfAsync(html, trip.TripId.ToString());
    }

    public async Task<List<LoadInstruction>> GetLoadPlanAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        var stops = await _context.TripStops
            .Where(ts => ts.TripId == tripId)
            .OrderBy(ts => ts.StopSequence)
            .ToListAsync();

        var stopInfos = stops.Select(s => new StopInfo
        {
            LocationId = s.LocationId ?? Guid.Empty,
            Sequence = s.StopSequence
        }).ToList();

        var loadPlan = BuildLIFOLoadPlan(trip.TransportOrders.ToList(), stopInfos);
        return loadPlan;
    }

    public async Task<string> GenerateLoadPlanPdfAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        var stops = await _context.TripStops
            .Where(ts => ts.TripId == tripId)
            .OrderBy(ts => ts.StopSequence)
            .Include(ts => ts.Location)
            .ToListAsync();

        var stopInfos = stops.Select(s => new StopInfo
        {
            LocationId = s.LocationId ?? Guid.Empty,
            Sequence = s.StopSequence
        }).ToList();

        var loadPlan = BuildLIFOLoadPlan(trip.TransportOrders.ToList(), stopInfos);

        // Build stop address mapping
        var stopAddresses = stops.ToDictionary(s => s.LocationId ?? Guid.Empty, s => s.Location?.Address ?? "N/A");

        var html = GenerateLoadPlanHtml(trip, loadPlan, stopAddresses);
        return await _pdfService.SaveLoadPlanPdfAsync(html, tripId.ToString());
    }

    private static string GenerateLoadPlanHtml(MasterTrip trip, List<LoadInstruction> loadPlan, Dictionary<Guid, string> stopAddresses)
    {
        static string TempColor(string? zone) => zone switch
        {
            "REAR"  => "#1e40af",   // xanh đậm – đông lạnh
            "MID"   => "#0891b2",   // xanh biển – mát
            "FRONT" => "#16a34a",   // xanh lá – nhiệt độ thường
            _       => "#6b7280"
        };

        static string TempBg(string? zone) => zone switch
        {
            "REAR"  => "#dbeafe",
            "MID"   => "#cffafe",
            "FRONT" => "#dcfce7",
            _       => "#f3f4f6"
        };

        static string ZoneLabel(string? zone) => zone switch
        {
            "REAR"  => "🔵 Ngăn ĐÔNG (Đuôi xe)",
            "MID"   => "🩵 Ngăn MÁT (Giữa xe)",
            "FRONT" => "🟢 Ngăn THƯỜNG (Đầu xe)",
            _       => zone ?? "—"
        };

        var totalWeight = loadPlan.Sum(l => l.WeightKg);
        var totalCbm    = loadPlan.Sum(l => l.Cbm);
        var issueDate   = DateTime.UtcNow.AddHours(7).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        // Build container visual (3 rows: REAR | MID | FRONT)
        var grouped = loadPlan.GroupBy(l => l.Zone ?? "FRONT").ToDictionary(g => g.Key, g => g.ToList());
        var zones = new[] { "REAR", "MID", "FRONT" };

        var containerRows = "";
        foreach (var zone in zones)
        {
            if (!grouped.TryGetValue(zone, out var zoneItems) || zoneItems.Count == 0) continue;
            var color = TempColor(zone);
            var bg    = TempBg(zone);
            var label = ZoneLabel(zone);
            var cells = "";
            foreach (var item in zoneItems)
            {
                cells += $@"<div style='background:{bg};border:2px solid {color};border-radius:8px;padding:10px 8px;margin:4px;min-width:130px;text-align:center;'>
                    <div style='font-size:11px;font-weight:700;color:{color};'>#{item.LoadOrder} XẾP VÀO</div>
                    <div style='font-size:12px;font-weight:600;margin:4px 0;color:#1e293b;'>{System.Net.WebUtility.HtmlEncode(item.ItemName)}</div>
                    <div style='font-size:10px;color:#475569;'>{item.TrackingCode}</div>
                    <div style='font-size:10px;color:#475569;'>{item.WeightKg:0.##} kg / {item.Cbm:0.##} m³</div>
                </div>";
            }
            containerRows += $@"<tr>
                <td style='padding:8px 12px;font-size:12px;font-weight:700;color:{color};background:{bg};border:1px solid #e2e8f0;white-space:nowrap;vertical-align:middle;'>{label}</td>
                <td style='padding:8px;border:1px solid #e2e8f0;'>
                    <div style='display:flex;flex-wrap:wrap;align-items:center;'>{cells}</div>
                </td>
            </tr>";
        }

        // Door indicator
        var doorRow = @"<tr>
            <td colspan='2' style='background:#fef3c7;border:2px dashed #f59e0b;padding:10px;text-align:center;font-size:13px;font-weight:700;color:#92400e;border-radius:0 0 8px 8px;'>
                🚪 CỬA XE — HÀNG XẾP SAU CÙNG SẼ ĐƯỢC DỠ TRƯỚC TIÊN
            </td>
        </tr>";

        // Build instruction rows
        var instructionRows = "";
        foreach (var item in loadPlan)
        {
            var color  = TempColor(item.Zone);
            var bg     = TempBg(item.Zone);
            var stopAddr = item.DeliveryLocationId != Guid.Empty && stopAddresses.TryGetValue(item.DeliveryLocationId, out var addr) ? addr : "—";
            instructionRows += $@"<tr>
                <td style='text-align:center;font-weight:700;font-size:14px;color:{color};padding:8px;border:1px solid #e2e8f0;'>{item.LoadOrder}</td>
                <td style='padding:8px;border:1px solid #e2e8f0;font-size:11px;color:#6b7280;'>{item.TrackingCode}</td>
                <td style='padding:8px;border:1px solid #e2e8f0;font-size:12px;font-weight:600;'>{System.Net.WebUtility.HtmlEncode(item.ItemName)}</td>
                <td style='padding:8px;border:1px solid #e2e8f0;text-align:center;'><span style='background:{bg};color:{color};padding:3px 8px;border-radius:12px;font-size:11px;font-weight:700;'>{item.Zone}</span></td>
                <td style='padding:8px;border:1px solid #e2e8f0;text-align:center;font-size:11px;'>{item.TempCondition}</td>
                <td style='padding:8px;border:1px solid #e2e8f0;text-align:right;font-size:12px;'>{item.WeightKg:0.##} kg</td>
                <td style='padding:8px;border:1px solid #e2e8f0;text-align:right;font-size:12px;'>{item.Cbm:0.##} m³</td>
                <td style='padding:8px;border:1px solid #e2e8f0;font-size:11px;color:#475569;'>Stop #{item.DeliveryStopSequence}: {System.Net.WebUtility.HtmlEncode(stopAddr)}</td>
                <td style='padding:8px;border:1px solid #e2e8f0;font-size:10px;color:#64748b;'>{System.Net.WebUtility.HtmlEncode(item.Reason ?? "")}</td>
            </tr>";
        }

        return $@"<!DOCTYPE html>
<html lang='vi'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>Sơ Đồ Xếp Hàng LIFO — {trip.TripId}</title>
<style>
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{ font-family:'Segoe UI',Arial,sans-serif; background:#f8fafc; color:#1e293b; padding:20px; }}
  .header {{ background:linear-gradient(135deg,#1e3a5f,#2563eb); color:#fff; border-radius:12px; padding:24px 30px; margin-bottom:20px; }}
  .header h1 {{ font-size:22px; font-weight:700; margin-bottom:6px; }}
  .header .sub {{ font-size:13px; opacity:.85; }}
  .info-grid {{ display:grid; grid-template-columns:repeat(3,1fr); gap:12px; margin-bottom:20px; }}
  .info-card {{ background:#fff; border-radius:10px; padding:14px 18px; border:1px solid #e2e8f0; box-shadow:0 1px 3px rgba(0,0,0,.06); }}
  .info-card .label {{ font-size:10px; text-transform:uppercase; letter-spacing:.05em; color:#94a3b8; margin-bottom:4px; }}
  .info-card .value {{ font-size:14px; font-weight:600; color:#1e293b; }}
  .section-title {{ font-size:15px; font-weight:700; color:#1e293b; margin-bottom:10px; padding-bottom:6px; border-bottom:2px solid #2563eb; }}
  .container-diagram {{ background:#fff; border-radius:12px; padding:20px; margin-bottom:20px; border:1px solid #e2e8f0; box-shadow:0 1px 3px rgba(0,0,0,.06); }}
  .truck-header {{ background:#1e3a5f; color:#fff; padding:10px 16px; border-radius:8px 8px 0 0; text-align:center; font-weight:700; font-size:13px; margin-bottom:0; }}
  table.diagram-table {{ width:100%; border-collapse:collapse; }}
  table.diagram-table td {{ vertical-align:middle; }}
  .instruction-table {{ width:100%; border-collapse:collapse; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 1px 3px rgba(0,0,0,.06); }}
  .instruction-table th {{ background:#1e3a5f; color:#fff; padding:10px 8px; font-size:11px; text-align:left; border:1px solid #334155; }}
  .instruction-table tr:nth-child(even) {{ background:#f8fafc; }}
  .legend {{ display:flex; gap:12px; flex-wrap:wrap; margin-bottom:16px; }}
  .legend-item {{ display:flex; align-items:center; gap:6px; font-size:11px; }}
  .legend-dot {{ width:14px; height:14px; border-radius:4px; }}
  .footer {{ margin-top:20px; text-align:center; font-size:10px; color:#94a3b8; }}
  @media print {{ body {{ padding:10px; }} }}
</style>
</head>
<body>

<div class='header'>
  <h1>📦 SƠ ĐỒ XẾP HÀNG LIFO — LỆNH BỐC XẾP KHO</h1>
  <div class='sub'>Chuyến #{trip.TripId} &nbsp;|&nbsp; Ngày lập: {issueDate} (GMT+7) &nbsp;|&nbsp; Xe: {System.Net.WebUtility.HtmlEncode(trip.Vehicle?.TruckPlate ?? "N/A")}</div>
</div>

<div class='info-grid'>
  <div class='info-card'><div class='label'>🚛 Phương tiện</div><div class='value'>{System.Net.WebUtility.HtmlEncode(trip.Vehicle?.TruckPlate ?? "N/A")} — {System.Net.WebUtility.HtmlEncode(trip.Vehicle?.VehicleType ?? "N/A")}</div></div>
  <div class='info-card'><div class='label'>👤 Tài xế</div><div class='value'>{System.Net.WebUtility.HtmlEncode(trip.Driver?.FullName ?? "N/A")}</div></div>
  <div class='info-card'><div class='label'>📅 Xuất phát dự kiến</div><div class='value'>{trip.PlannedStartTime.AddHours(7).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}</div></div>
  <div class='info-card'><div class='label'>📍 Kho xuất phát</div><div class='value'>{System.Net.WebUtility.HtmlEncode(trip.OriginLocation?.Address ?? "N/A")}</div></div>
  <div class='info-card'><div class='label'>⚖️ Tổng trọng lượng</div><div class='value'>{totalWeight:0.##} kg</div></div>
  <div class='info-card'><div class='label'>📐 Tổng thể tích</div><div class='value'>{totalCbm:0.##} m³</div></div>
</div>

<div class='legend'>
  <div class='legend-item'><div class='legend-dot' style='background:#dbeafe;border:2px solid #1e40af;'></div>Ngăn ĐÔNG (REAR) — Đuôi xe</div>
  <div class='legend-item'><div class='legend-dot' style='background:#cffafe;border:2px solid #0891b2;'></div>Ngăn MÁT (MID) — Giữa xe</div>
  <div class='legend-item'><div class='legend-dot' style='background:#dcfce7;border:2px solid #16a34a;'></div>Ngăn THƯỜNG (FRONT) — Đầu xe</div>
</div>

<div class='container-diagram'>
  <p class='section-title'>🏗️ Sơ đồ Container — Nhìn từ trên xuống (Đầu xe → Đuôi xe)</p>
  <div class='truck-header'>⬆️ ĐẦU XE (CAB)</div>
  <table class='diagram-table'>
    {containerRows}
    {doorRow}
  </table>
</div>

<p class='section-title'>📋 Bảng Lệnh Xếp Hàng (thứ tự từ xếp VÀO đến xếp SAU)</p>
<table class='instruction-table'>
  <thead>
    <tr>
      <th style='width:40px;text-align:center;'>Thứ tự XẾP VÀO</th>
      <th>Mã đơn</th>
      <th>Tên hàng</th>
      <th>Ngăn</th>
      <th>Nhiệt độ</th>
      <th>Trọng lượng</th>
      <th>Thể tích</th>
      <th>Điểm giao</th>
      <th>Lý do</th>
    </tr>
  </thead>
  <tbody>
    {instructionRows}
  </tbody>
</table>

<div class='footer'>
  ColdChainX — Tài liệu nội bộ — In lúc {issueDate} (GMT+7) — Trip ID: {trip.TripId}
</div>

</body>
</html>";
    }

    public async Task<List<TransportDocument>> GetIssuedDocumentsAsync(Guid tripId)
    {
        var tripExists = await _context.MasterTrips.AnyAsync(t => t.TripId == tripId);
        if (!tripExists)
            throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        var documents = await _context.TransportDocuments
            .Where(d => d.ImageUrl.Contains(tripId.ToString()))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return documents;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Loader Notifications — Gửi thông báo cho Loader khi LIFO sẵn sàng
    // ═══════════════════════════════════════════════════════════════════════

    private const string LoaderRoleName = "Loader";
    private const string LoaderNotificationTemplateId = "DISPATCH_LOADER_READY";

    public async Task NotifyLoadersAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        // Tìm tất cả users có role Loader
        var loaderUserIds = await _context.Users
            .Include(u => u.Role)
            .Where(u => u.Role != null
                     && u.Role.RoleName == LoaderRoleName
                     && (u.Status == null || u.Status == "ACTIVE"))
            .Select(u => u.UserId)
            .ToListAsync();

        if (loaderUserIds.Count == 0) return;

        // Tạo template nếu chưa có
        var templateExists = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == LoaderNotificationTemplateId);

        if (!templateExists)
        {
            // Tìm MessageType bất kỳ để gắn vào template
            var msgType = await _context.Messagetypes.FirstOrDefaultAsync();
            if (msgType != null)
            {
                _context.NotificationTemplates.Add(new NotificationTemplate
                {
                    TemplateId = LoaderNotificationTemplateId,
                    TypeId = msgType.TypeId,
                    TitleTemplate = "Sơ đồ LIFO sẵn sàng — Xe {vehicle}",
                    BodyTemplate = "Chuyến hàng {tripId} đã có sơ đồ xếp hàng LIFO. " +
                                   "Vui lòng xếp {orderCount} đơn hàng lên xe theo thứ tự LIFO, " +
                                   "tổng trọng lượng {totalWeight}kg. Sau khi xếp xong, thực hiện kẹp chì.",
                    Channel = "IN_APP",
                    Status = "ACTIVE"
                });
                await _context.SaveChangesAsync();
            }
        }

        // Kiểm tra template tồn tại
        var actualTemplateId = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == LoaderNotificationTemplateId
                        && (t.Status == null || t.Status == "ACTIVE"))
            ? LoaderNotificationTemplateId
            : await GetFallbackTemplateIdAsync();

        if (actualTemplateId == null) return;

        foreach (var userId in loaderUserIds)
        {
            var notifParams = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "tripId",      trip.TripId.ToString() },
                { "vehicle",     trip.Vehicle?.TruckPlate ?? "N/A" },
                { "orderCount",  trip.TransportOrders.Count.ToString() },
                { "totalWeight", trip.TransportOrders.Sum(o => o.ExpectedWeightKg).ToString("F1") },
                { "action",      "Xem sơ đồ LIFO và xếp hàng lên container, sau đó kẹp chì" }
            });

            _context.Notifications.Add(new Notification
            {
                NotiId     = Guid.NewGuid(),
                UserId     = userId,
                SenderId   = null,
                TemplateId = actualTemplateId,
                Params     = notifParams,
                OrderId    = null,
                IsRead     = false,
                CreatedAt  = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        try
        {
            await _hubContext.Clients.Groups("Group_Loader", "Group_Admin")
                .SendAsync("WarehouseOrderApproved", new
                {
                    TripId = tripId,
                    Status = "LOADING",
                    Vehicle = trip.Vehicle?.TruckPlate ?? "N/A",
                    OrderCount = trip.TransportOrders.Count,
                    TotalWeight = trip.TransportOrders.Sum(o => o.ExpectedWeightKg)
                });
        }
        catch (Exception)
        {
            // Fail-safe to avoid blocking API if SignalR fails
        }
    }
}
