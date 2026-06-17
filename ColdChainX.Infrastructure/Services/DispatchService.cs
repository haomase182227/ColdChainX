using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Integration;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

public class DispatchService : IDispatchService
{
    private readonly ApplicationDbContext _context;
    private readonly GeminiLoadOptimizerClient _geminiClient;
    private readonly ILocationService _locationService;

    // Tên role điều phối viên
    private const string CoordinatorRoleName = "Dispatcher";

    // TemplateId thông báo lệnh điều động (phải tồn tại trong bảng notification_templates)
    private const string LoadingOrderTemplateId = "DISPATCH_LOADING_ORDER";

    public DispatchService(
        ApplicationDbContext context,
        GeminiLoadOptimizerClient geminiClient,
        ILocationService locationService)
    {
        _context = context;
        _geminiClient = geminiClient;
        _locationService = locationService;
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
                        && o.Status == "IN_WAREHOUSE")
            .ToListAsync();

        if (orders.Count == 0)
            throw new InvalidOperationException(
                "Không tìm thấy đơn hàng nào ở trạng thái IN_WAREHOUSE với các OrderId đã cung cấp.");

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
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new Exception("Trip not found.");

        _context.TransportDocuments.Add(new TransportDocument
        {
            DocId     = Guid.NewGuid(),
            DocType   = "E-WAYBILL",
            ImageUrl  = $"https://coldchainx.com/docs/ewaybill/{tripId}.pdf",
            Status    = "ISSUED",
            CreatedAt = DateTime.UtcNow,
            UploadedBy = trip.DriverId ?? issuerId ?? Guid.Empty
        });

        trip.Status = "DISPATCHED";
        await _context.SaveChangesAsync();
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
            LocationId = s.LocationId,
            Sequence = s.StopSequence
        }).ToList();

        var loadPlan = BuildLIFOLoadPlan(trip.TransportOrders.ToList(), stopInfos);
        return loadPlan;
    }
}
