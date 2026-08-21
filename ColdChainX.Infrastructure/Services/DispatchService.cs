using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Integration;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
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
    private readonly IDriverAvailabilityService _driverAvailability;
    private readonly IMqttCommandPublisher _mqttPublisher;
    private readonly ILogger<DispatchService> _logger;
    private readonly ICargoCompatibilityService _cargoCompatibilityService;
    private readonly INotificationService? _notificationService;
    private readonly IIncidentWorkflowNotificationService? _workflowNotificationService;

    private const string CoordinatorRoleName = "Dispatcher";

    private const string LoadingOrderTemplateId = "DISPATCH_LOADING_ORDER";

    private const decimal MaxColdAirflowVolumeUtilization = 0.80m;

    private static readonly string[] BusyTripStatuses =
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

    public DispatchService(
        ApplicationDbContext context,
        GeminiLoadOptimizerClient geminiClient,
        ILocationService locationService,
        IPdfService pdfService,
        IWebHostEnvironment environment,
        IHubContext<NotificationHub> hubContext,
        IDriverAvailabilityService driverAvailability,
        IMqttCommandPublisher mqttPublisher,
        ILogger<DispatchService> logger,
        ICargoCompatibilityService? cargoCompatibilityService = null,
        INotificationService? notificationService = null,
        IIncidentWorkflowNotificationService? workflowNotificationService = null)
    {
        _context = context;
        _geminiClient = geminiClient;
        _locationService = locationService;
        _pdfService = pdfService;
        _environment = environment;
        _hubContext = hubContext;
        _driverAvailability = driverAvailability;
        _mqttPublisher = mqttPublisher;
        _logger = logger;
        _cargoCompatibilityService = cargoCompatibilityService ?? new CargoCompatibilityService();
        _notificationService = notificationService;
        _workflowNotificationService = workflowNotificationService;
    }


    public async Task<PlanLoadResult> PlanLoadFromWarehouseAsync(PlanLoadRequest request)
    {
        var vehicle = await _context.Vehicles.FindAsync(request.VehicleId)
            ?? throw new InvalidOperationException("Xe không tồn tại.");

        if (vehicle.Status != null &&
            vehicle.Status.Equals("MAINTENANCE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Xe {vehicle.TruckPlate} đang trong trạng thái bảo dưỡng.");

        var lpns = await _context.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.DestLocationNavigation)
            .Include(l => l.Receipt)
            .Include(l => l.InboundQcPackageLines)
            .Where(l => request.LpnIds.Contains(l.LpnId)
                        && l.State == LpnState.IN_STOCK)
            .ToListAsync();

        if (lpns.Count == 0)
            throw new InvalidOperationException(
                "Không tìm thấy LPN nào ở trạng thái IN_STOCK với các LpnId đã cung cấp.");

        var warehouseIds = lpns
            .Select(l => l.Receipt.WarehouseId)
            .Distinct()
            .ToList();

        if (warehouseIds.Count > 1)
            throw new InvalidOperationException("Tất cả các LPN được chọn phải cùng thuộc một kho lưu trữ (WarehouseId).");

        var missingLpnIds = request.LpnIds
            .Except(lpns.Select(l => l.LpnId))
            .ToList();
        if (missingLpnIds.Any())
            throw new InvalidOperationException(
                $"Các LPN sau không tồn tại hoặc không ở trạng thái IN_STOCK: " +
                $"{string.Join(", ", missingLpnIds)}");

        var orders = lpns
            .GroupBy(l => l.OrderId)
            .Select(g => g.First().Order)
            .ToList();

        var totalWeight = lpns.Sum(l => l.ActualWeightKg);
        var totalCbm    = lpns.Sum(l => l.ActualCbm);

        if (totalWeight > vehicle.MaxWeight)
            throw new InvalidOperationException(
                $"Quá tải: Tổng trọng lượng ({totalWeight:F1}kg) vượt tải trọng xe ({vehicle.MaxWeight}kg).");

        if (totalCbm > vehicle.MaxCbm)
            throw new InvalidOperationException(
                $"Quá thể tích: Tổng CBM ({totalCbm:F2}m³) vượt dung tích xe ({vehicle.MaxCbm}m³).");

        var originLocation = await _context.Locations.FindAsync(request.OriginWarehouseLocationId)
            ?? throw new InvalidOperationException("LocationId kho xuất phát không tồn tại.");

        var routeResult = await BuildOptimalRouteAsync(
            originLocation, orders, vehicle);

        var loadPlan = BuildLpnLIFOLoadPlan(lpns, routeResult.StopSequence);

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

        foreach (var lpn in lpns)
        {
            lpn.TripId = masterTrip.TripId;
            lpn.State = LpnState.ALLOCATED;
            if (lpn.Order != null)
            {
                lpn.Order.Status = "LOADING";
                lpn.Order.MasterTripId = masterTrip.TripId;
            }
        }

        var notifiedCount = await SendLoadingNotificationsAsync(
            masterTrip, orders, vehicle, loadPlan,
            request.DispatchCoordinatorId);
        var customerNotifiedCount = await SendCustomerNotificationsAsync(masterTrip, orders);

        await _context.SaveChangesAsync();

        var routeStops = routeResult.StopSequence.Select(s =>
        {
            var stopLpns = lpns
                .Where(l => l.Order?.DestLocation == s.LocationId)
                .Select(l => new LpnSummary
                {
                    LpnId = l.LpnId,
                    LpnCode = l.LpnCode,
                    OrderId = l.OrderId,
                    OrderTrackingCode = l.Order?.TrackingCode ?? string.Empty,
                    ItemName = l.Order?.ItemName ?? string.Empty,
                    Quantity = l.Quantity,
                    WeightKg = l.ActualWeightKg,
                    Cbm = l.ActualCbm,
                    TempCondition = l.Order?.TempCondition ?? "AMBIENT"
                }).ToList();

            return new StopDto
            {
                Sequence               = s.Sequence,
                LocationId             = s.LocationId,
                Address                = s.Address,
                Latitude               = s.Latitude,
                Longitude              = s.Longitude,
                DistanceFromPreviousKm = s.DistanceFromPreviousKm,
                LpnsToUnload           = stopLpns
            };
        }).ToList();

        var dispatchInstructions = loadPlan.Select(li => new DispatchInstruction
        {
            LpnId          = li.LpnId,
            LpnCode        = li.LpnCode,
            OrderId        = li.OrderId,
            TrackingCode   = li.TrackingCode,
            ItemName       = li.ItemName,
            Action         = "LOAD",
            PreviousStatus = "IN_STOCK",
            TargetStatus   = "ALLOCATED",
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
            RouteDetails = new RouteDetailsDto
            {
                TotalDistanceKm = (double)routeResult.TotalDistanceKm,
                TotalDurationMinutes = 0, // Auto-dispatch doesn't fetch full directions here
                OverviewPolyline = "", 
                OriginLat = originLocation.Latitude,
                OriginLng = originLocation.Longitude,
                OriginAddress = originLocation.Address,
                DestinationLat = routeStops.LastOrDefault()?.Latitude ?? originLocation.Latitude,
                DestinationLng = routeStops.LastOrDefault()?.Longitude ?? originLocation.Longitude,
                DestinationAddress = routeStops.LastOrDefault()?.Address ?? originLocation.Address,
                Stops = routeStops,
                Steps = new List<StepDto>()
            },
            LoadPlan             = loadPlan,
            DispatchInstructions = dispatchInstructions,
            NotifiedCoordinators = notifiedCount
        };
    }


    private async Task<RouteCalculationResult> BuildOptimalRouteAsync(
        Location origin,
        List<TransportOrder> orders,
        Vehicle vehicle)
    {
        var destinations = orders
            .Where(o => o.DestLocation.HasValue && o.DestLocationNavigation != null)
            .GroupBy(o => o.DestLocation!.Value)
            .Select(g => g.First().DestLocationNavigation!)
            .ToList();

        if (destinations.Count == 0)
            throw new InvalidOperationException(
                "Không có đơn hàng nào có tọa độ điểm giao. " +
                "Hãy đảm bảo DestLocation đã được gán cho tất cả đơn hàng.");

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


    private static List<LoadInstruction> BuildLIFOLoadPlan(
        List<TransportOrder> orders,
        List<StopInfo> stopSequence)
    {
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

        var sorted = enriched
            .OrderByDescending(x => x.StopSeq)          // điểm cuối vào xe trước
            .ThenByDescending(x => (x.Order.OrderDimension?.ExpectedWeightKg ?? 0m))  // nặng dưới
            .ThenBy(x => x.TempZoneOrd)                  // frozen trước
            .ToList();

        var result = new List<LoadInstruction>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var item   = sorted[i];
            var order  = item.Order;
            var zone   = item.TempZone;

            if (item.TempZoneOrd == 0) zone = "REAR";

            var reason = BuildLoadReason(item.StopSeq, stopSequence.Count,
                                         item.TempZoneOrd, (order.OrderDimension?.ExpectedWeightKg ?? 0m));

            result.Add(new LoadInstruction
            {
                LoadOrder           = i + 1,
                OrderId             = order.OrderId,
                TrackingCode        = order.TrackingCode,
                ItemName            = order.ItemName,
                WeightKg            = (order.OrderDimension?.ExpectedWeightKg ?? 0m),
                Cbm                 = (order.OrderDimension?.ExpectedCbm ?? 0m),
                TempCondition       = order.TempCondition,
                Zone                = zone,
                DeliveryLocationId  = order.DestLocation!.Value,
                DeliveryStopSequence = item.StopSeq,
                Reason              = reason
            });
        }

        return result;
    }

    private static List<LoadInstruction> BuildLpnLIFOLoadPlan(
        List<Lpn> lpns,
        List<StopInfo> stopSequence)
    {
        var stopSeqMap = stopSequence.ToDictionary(s => s.LocationId, s => s.Sequence);

        var enriched = lpns
            .Where(l => l.Order != null && l.Order.DestLocation.HasValue)
            .Select(l => new
            {
                Lpn         = l,
                StopSeq     = stopSeqMap.TryGetValue(l.Order.DestLocation!.Value, out var seq) ? seq : 999,
                TempZone    = ClassifyTempZone(l.Order.TempCondition),
                TempZoneOrd = TempZoneOrder(l.Order.TempCondition)
            })
            .ToList();

        var sorted = enriched
            .OrderByDescending(x => x.StopSeq)          // điểm cuối vào xe trước
            .ThenByDescending(x => x.Lpn.ActualWeightKg)  // nặng dưới
            .ThenBy(x => x.TempZoneOrd)                  // frozen trước
            .ToList();

        var result = new List<LoadInstruction>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var item   = sorted[i];
            var lpn    = item.Lpn;
            var order  = lpn.Order;
            var zone   = item.TempZone;

            if (item.TempZoneOrd == 0) zone = "REAR";

            var reason = BuildLoadReason(item.StopSeq, stopSequence.Count,
                                         item.TempZoneOrd, lpn.ActualWeightKg);

            result.Add(new LoadInstruction
            {
                LoadOrder           = i + 1,
                LpnId               = lpn.LpnId,
                LpnCode             = lpn.LpnCode,
                OrderId             = order.OrderId,
                TrackingCode        = order.TrackingCode,
                ItemName            = order.ItemName,
                WeightKg            = lpn.ActualWeightKg,
                Cbm                 = lpn.ActualCbm,
                TempCondition       = order.TempCondition,
                Zone                = zone,
                DeliveryLocationId  = order.DestLocation!.Value,
                DeliveryStopSequence = item.StopSeq,
                Reason              = reason
            });
        }

        return result;
    }


    private static string ClassifyTempZone(string tempCondition)
    {
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



    private const string CustomerEtaTemplateId = "DISPATCH_CUSTOMER_ETA";

    private async Task<int> SendCustomerNotificationsAsync(MasterTrip trip, List<TransportOrder> orders)
    {
        const string titleTemplate = "Đơn hàng {orderCode} đã được xếp lên xe";
        const string bodyTemplate =
            "Đơn hàng của bạn đã được xếp lên xe {vehicle}. Dự kiến giao hàng ngày {eta} tại {address}.";

        var customerEtaTemplate = await _context.NotificationTemplates
            .FirstOrDefaultAsync(t => t.TemplateId == CustomerEtaTemplateId);
        if (customerEtaTemplate == null)
        {
            var msgType = await _context.Messagetypes.FirstOrDefaultAsync();
            if (msgType != null)
            {
                customerEtaTemplate = new NotificationTemplate
                {
                    TemplateId = CustomerEtaTemplateId,
                    TypeId = msgType.TypeId,
                    TitleTemplate = titleTemplate,
                    BodyTemplate = bodyTemplate,
                    Channel = "IN_APP",
                    Status = "ACTIVE"
                };
                _context.NotificationTemplates.Add(customerEtaTemplate);
                await _context.SaveChangesAsync();
            }
        }
        else
        {
            customerEtaTemplate.TitleTemplate = titleTemplate;
            customerEtaTemplate.BodyTemplate = bodyTemplate;
            customerEtaTemplate.Channel = "IN_APP";
            customerEtaTemplate.Status = "ACTIVE";
        }

        var actualTemplateId = customerEtaTemplate != null
            ? CustomerEtaTemplateId
            : await GetFallbackTemplateIdAsync();

        if (actualTemplateId == null) return 0;

        int notifiedCount = 0;
        
        var ordersByCustomer = orders.Where(o => o.CustomerId != null).GroupBy(o => o.CustomerId.Value);

        foreach (var customerGroup in ordersByCustomer)
        {
            var customerId = customerGroup.Key;
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId);
            if (customer == null || string.IsNullOrWhiteSpace(customer.Email)) continue;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == customer.Email);
            if (user == null) continue;

            var destLocationId = customerGroup.First().DestLocation;
            var stop = _context.TripStops.Local
                .FirstOrDefault(s => s.TripId == trip.TripId && s.LocationId == destLocationId)
                ?? await _context.TripStops
                    .FirstOrDefaultAsync(s => s.TripId == trip.TripId && s.LocationId == destLocationId);
            var eta = stop != null
                ? stop.PlannedArrivalTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "N/A";
            var address = customerGroup.First().DestLocationNavigation?.Address ?? "N/A";

            var orderCodes = string.Join(", ", customerGroup.Select(o => o.TrackingCode));

            var notifParams = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "orderCode", orderCodes },
                { "vehicle",   trip.Vehicle?.TruckPlate ?? "N/A" },
                { "eta",       eta },
                { "address",   address }
            });

            _context.Notifications.Add(new Notification
            {
                NotiId     = Guid.NewGuid(),
                UserId     = user.UserId,
                SenderId   = null,
                TemplateId = actualTemplateId,
                Params     = notifParams,
                OrderId    = customerGroup.First().OrderId,
                IsRead     = false,
                CreatedAt  = DateTime.UtcNow
            });
            notifiedCount++;
        }

        await _context.SaveChangesAsync();
        return notifiedCount;
    }

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
            targetUserIds = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role != null
                         && u.Role.RoleName == CoordinatorRoleName
                         && (u.Status == null || u.Status == "ACTIVE"))
                .Select(u => u.UserId)
                .ToListAsync();
        }

        if (targetUserIds.Count == 0) return 0;

        var templateExists = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == LoadingOrderTemplateId
                        && (t.Status == null || t.Status == "ACTIVE"));

        var count = 0;
        foreach (var userId in targetUserIds)
        {
            var notifParams = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "tripId",      trip.TripId.ToString() },
                { "vehicle",     vehicle.TruckPlate },
                { "orderCount",  orders.Count.ToString() },
                { "firstLoad",   loadPlan.FirstOrDefault()?.ItemName ?? "-" },
                { "totalWeight", orders.Sum(o => (o.OrderDimension?.ExpectedWeightKg ?? 0m)).ToString("F1") },
                { "startTime",   trip.PlannedStartTime.ToString("dd/MM/yyyy HH:mm") }
            });

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
        return await _context.NotificationTemplates
            .Where(t => t.Status == null || t.Status == "ACTIVE")
            .Select(t => t.TemplateId)
            .FirstOrDefaultAsync();
    }


    private static decimal GetTargetTemperature(List<TransportOrder> orders)
    {
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

        var firstPart = t.Split(new[] { '-', '~', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                         .FirstOrDefault();
        if (decimal.TryParse(firstPart?.Replace("C", ""), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return 4m; // default
    }

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

    private static void ValidateColdStorageCapacity(
        IReadOnlyCollection<Lpn> lpns,
        Vehicle vehicle,
        decimal recordedTotalCbm)
    {
        if (!HasPositiveDimensions(vehicle.InnerLengthCm, vehicle.InnerWidthCm, vehicle.InnerHeightCm))
            throw new InvalidOperationException(
                $"Xe {vehicle.TruckPlate} chua co du kich thuoc long thung de kiem tra xep hang.");

        var lpnsWithoutDimensions = lpns
            .Where(l => !HasActualPackageLineDimensions(l)
                && !HasPositiveDimensions(l.LengthCm, l.WidthCm, l.HeightCm))
            .Select(l => l.LpnCode)
            .ToList();
        if (lpnsWithoutDimensions.Count > 0)
            throw new InvalidOperationException(
                $"Cac LPN sau chua co actual package lines hoac kich thuoc LPN fallback: {string.Join(", ", lpnsWithoutDimensions)}.");

        var vehicleDimensions = new[]
        {
            vehicle.InnerLengthCm!.Value,
            vehicle.InnerWidthCm!.Value,
            vehicle.InnerHeightCm!.Value
        };
        Array.Sort(vehicleDimensions);

        var oversizedLpn = lpns.FirstOrDefault(l => GetLoadItemDimensions(l).Any(dimensions =>
        {
            var itemDimensions = new[] { dimensions.length, dimensions.width, dimensions.height };
            Array.Sort(itemDimensions);

            return itemDimensions[0] > vehicleDimensions[0]
                || itemDimensions[1] > vehicleDimensions[1]
                || itemDimensions[2] > vehicleDimensions[2];
        }));

        if (oversizedLpn != null)
            throw new InvalidOperationException(
                $"LPN {oversizedLpn.LpnCode} co kien khong lot thung xe {vehicle.TruckPlate} ({vehicle.InnerLengthCm:F2} x {vehicle.InnerWidthCm:F2} x {vehicle.InnerHeightCm:F2} cm), ke ca khi xoay kien.");
    }

    private static bool HasPositiveDimensions(decimal? lengthCm, decimal? widthCm, decimal? heightCm)
        => lengthCm > 0 && widthCm > 0 && heightCm > 0;

    private static bool HasActualPackageLineDimensions(Lpn lpn)
        => lpn.InboundQcPackageLines.Any(line =>
            line.Quantity > 0
            && line.LengthCm > 0
            && line.WidthCm > 0
            && line.HeightCm > 0);

    private static IReadOnlyCollection<(Guid? packageLineId, string? packageLabel, int quantity, decimal length, decimal width, decimal height, decimal totalWeight)> GetLoadItemDimensions(Lpn lpn)
    {
        var packageLines = lpn.InboundQcPackageLines
            .Where(line => line.Quantity > 0 && line.LengthCm > 0 && line.WidthCm > 0 && line.HeightCm > 0)
            .Select(line => (
                packageLineId: (Guid?)line.InboundQcPackageLineId,
                packageLabel: (string?)line.Label,
                quantity: line.Quantity,
                length: line.LengthCm,
                width: line.WidthCm,
                height: line.HeightCm,
                totalWeight: line.ActualWeightKg))
            .ToList();

        if (packageLines.Count > 0)
            return packageLines;

        if (HasPositiveDimensions(lpn.LengthCm, lpn.WidthCm, lpn.HeightCm))
        {
            return new[]
            {
                (
                    packageLineId: (Guid?)null,
                    packageLabel: (string?)lpn.LpnCode,
                    quantity: Math.Max(1, lpn.Quantity),
                    length: lpn.LengthCm!.Value,
                    width: lpn.WidthCm!.Value,
                    height: lpn.HeightCm!.Value,
                    totalWeight: lpn.ActualWeightKg)
            };
        }

        return Array.Empty<(Guid? packageLineId, string? packageLabel, int quantity, decimal length, decimal width, decimal height, decimal totalWeight)>();
    }
    private static double ToRad(double deg) => deg * Math.PI / 180.0;




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

    public async Task<ManualDispatchResult> ManualDispatchAsync(ManualDispatchRequest request)
    {
        var selectedScheduleId = request.ScheduleId.HasValue && request.ScheduleId.Value != Guid.Empty
            ? request.ScheduleId.Value
            : Guid.Empty;
        RouteSchedule? schedule = null;
        if (selectedScheduleId != Guid.Empty)
        {
            schedule = await _context.RouteSchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScheduleId == selectedScheduleId)
                ?? throw new InvalidOperationException($"ScheduleId '{selectedScheduleId}' does not exist.");
        }

        var originLocation = await _context.Locations.FindAsync(request.OriginWarehouseLocationId)
            ?? throw new InvalidOperationException("LocationId kho xuất phát không tồn tại.");

        var lpns = await _context.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.DestLocationNavigation)
            .Include(l => l.Receipt)
            .Where(l => request.LpnIds.Contains(l.LpnId))
            .ToListAsync();

        if (lpns.Count == 0)
            throw new InvalidOperationException("Không tìm thấy LPN nào khớp với danh sách đã chọn.");

        var missingLpns = request.LpnIds.Except(lpns.Select(l => l.LpnId)).ToList();
        if (missingLpns.Any())
            throw new InvalidOperationException($"Không tìm thấy các LPN sau: {string.Join(", ", missingLpns)}");

        IncidentReport? relayIncident = null;
        ExternalReeferPlanRecord? relayPlan = null;
        if (request.IncidentId.HasValue)
        {
            relayIncident = await _context.IncidentReports
                .FirstOrDefaultAsync(i => i.IncidentId == request.IncidentId.Value)
                ?? throw new InvalidOperationException("Không tìm thấy Incident cần ghép chuyến lại.");
            if (relayIncident.Status != "READY_FOR_REDISPATCH")
                throw new InvalidOperationException("Incident chỉ được ghép chuyến lại sau khi Warehouse Worker inbound bằng seal.");

            try
            {
                relayPlan = JsonSerializer.Deserialize<ExternalReeferPlanRecord>(relayIncident.RescuePlanDetails ?? string.Empty);
            }
            catch (JsonException)
            {
                relayPlan = null;
            }
            if (relayPlan == null || relayPlan.ArrivedAt == null)
                throw new InvalidOperationException("Incident thiếu dữ liệu inbound tại kho tuyến.");
            if (!request.LpnIds.Distinct().ToHashSet().SetEquals(relayPlan.LpnIds.Distinct()))
                throw new InvalidOperationException("Phải ghép lại đúng toàn bộ LPN đã inbound từ xe lạnh thuê ngoài.");
            if (lpns.Any(l => l.WarehouseId != relayPlan.DestinationWarehouseId))
                throw new InvalidOperationException("Tất cả LPN phải nằm tại đúng kho đích của tuyến.");
        }

        var selectedSetValidation = _cargoCompatibilityService.ValidateSelectedSet(
            lpns,
            selectedScheduleId,
            request.LpnIds);
        if (!selectedSetValidation.IsValid)
        {
            var messages = selectedSetValidation.Conflicts
                .Select(c => $"{c.ReasonCode}: {c.Message}")
                .Distinct();
            throw new InvalidOperationException($"Selected LPNs are not valid for dispatch. {string.Join("; ", messages)}");
        }

        if (lpns.Any(l => l.State != LpnState.IN_STOCK))
            throw new InvalidOperationException("Chỉ được ghép chuyến các LPN có trạng thái IN_STOCK.");



        var distinctWarehouses = lpns.Select(l => l.WarehouseId).Distinct().ToList();
        if (distinctWarehouses.Count > 1)
        {
            throw new InvalidOperationException("Chỉ được phép ghép các kiện hàng (LPN) nằm trong cùng một kho.");
        }

        var orders = lpns
            .GroupBy(l => l.OrderId)
            .Select(g => g.First().Order)
            .ToList();


        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId)
            ?? throw new InvalidOperationException("Không tìm thấy xe (Vehicle) đã chọn.");

        if (vehicle.Status != "ACTIVE")
            throw new InvalidOperationException(
                $"Xe {vehicle.TruckPlate} không thể ghép chuyến — trạng thái hiện tại: '{vehicle.Status}'. " +
                $"Chỉ xe ACTIVE mới có thể được ghép chuyến.");

        var vehicleTemperatureConflicts = _cargoCompatibilityService.ValidateVehicleTemperature(vehicle, lpns);
        if (vehicleTemperatureConflicts.Any())
        {
            var messages = vehicleTemperatureConflicts
                .Select(c => $"{c.ReasonCode}: {c.Message}")
                .Distinct();
            throw new InvalidOperationException($"Vehicle temperature is not compatible with selected LPNs. {string.Join("; ", messages)}");
        }

        var driverIds = request.DriverIds.Distinct().ToList();
        if (driverIds.Count < 1 || driverIds.Count > 2)
            throw new InvalidOperationException("Phải chọn 1 hoặc 2 tài xế cho chuyến (mỗi chuyến tối đa 2 tài xế).");

        var drivers = await _context.Drivers
            .Include(d => d.DriverLicenses)
            .Where(d => driverIds.Contains(d.DriverId))
            .ToListAsync();

        var missingDrivers = driverIds.Except(drivers.Select(d => d.DriverId)).ToList();
        if (missingDrivers.Any())
            throw new InvalidOperationException($"Không tìm thấy tài xế: {string.Join(", ", missingDrivers)}");
        drivers = driverIds.Select(id => drivers.First(d => d.DriverId == id)).ToList();

        var lpnWarehouseIdStr = lpns.FirstOrDefault(l => l.WarehouseId.HasValue)?.WarehouseId?.ToString();

        bool IsLocationOk(string? currentLocation) {
            if (string.IsNullOrWhiteSpace(currentLocation)) return false;
            if (lpnWarehouseIdStr != null && currentLocation.Equals(lpnWarehouseIdStr, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        bool IsDriverLocationOk(string? currentLocation) {
            if (string.IsNullOrWhiteSpace(currentLocation)) return true;
            return IsLocationOk(currentLocation);
        }

        if (!IsLocationOk(vehicle.CurrentLocation))
        {
            throw new InvalidOperationException($"Xe {vehicle.TruckPlate} không nằm tại kho xuất phát này (Vị trí hiện tại: {vehicle.CurrentLocation}).");
        }

        foreach (var d in drivers)
        {
            if (!IsDriverLocationOk(d.CurrentLocation))
            {
                throw new InvalidOperationException($"Tài xế {d.FullName} không nằm tại kho xuất phát này.");
            }
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var driverLicenses = new Dictionary<Guid, DriverLicense>();
        foreach (var driver in drivers)
        {
            await _driverAvailability.ReconcileStatusAsync(driver);

            if (driver.Status != "ACTIVE" && driver.Status?.ToUpperInvariant() != "AVAILABLE")
                throw new InvalidOperationException(
                    $"Tài xế {driver.FullName} không thể ghép chuyến — trạng thái hiện tại: '{driver.Status}'.");

            var activeLicense = driver.DriverLicenses
                .Where(l => l.ExpiryDate >= today && (l.Status == null || l.Status == "ACTIVE"))
                .OrderByDescending(l => l.ExpiryDate)
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"Tài xế {driver.FullName} không có bằng lái còn hạn.");
            driverLicenses[driver.DriverId] = activeLicense;

            var driverBusy = await _context.TripDrivers
                .AnyAsync(td => td.DriverId == driver.DriverId
                    && td.Trip.Status != null
                    && BusyTripStatuses.Contains(td.Trip.Status));
            if (driverBusy)
                throw new InvalidOperationException($"Tài xế {driver.FullName} hiện đang bận một chuyến khác.");
        }

        var lateLpns = lpns.Where(l => l.SlaDeadline.HasValue && l.SlaDeadline.Value < DateTime.UtcNow).ToList();

        var isBusy = await _context.MasterTrips
            .AnyAsync(t => t.VehicleId == request.VehicleId
                        && t.Status != null
                        && BusyTripStatuses.Contains(t.Status));

        if (isBusy)
            throw new InvalidOperationException($"Xe {vehicle.TruckPlate} hiện đang bận một chuyến khác.");

        var totalWeight = lpns.Sum(l => l.ActualWeightKg);
        var totalCbm = lpns.Sum(l => l.ActualCbm);
        var requiredMinTemp = GetTargetTemperature(orders);

        ValidateColdStorageCapacity(lpns, vehicle, totalCbm);

        if (totalWeight > vehicle.MaxWeight)
            throw new InvalidOperationException(
                $"Quá tải: Tổng khối lượng {totalWeight:F1}kg vượt tải trọng tối đa {vehicle.MaxWeight:F1}kg của xe {vehicle.TruckPlate}.");

        var routeResult = await BuildOptimalRouteAsync(originLocation, orders, vehicle);

        decimal vLength = vehicle.InnerLengthCm ?? (vehicle.VehicleType == "TRUCK_1T" ? 300m : 200m);
        decimal vWidth = vehicle.InnerWidthCm ?? (vehicle.VehicleType == "TRUCK_1T" ? 180m : 140m);
        decimal vHeight = vehicle.InnerHeightCm ?? (vehicle.VehicleType == "TRUCK_1T" ? 190m : 140m);

        var engineItems = new List<ColdChainX.Application.Services.LpnDims>();
        foreach (var lpn in lpns)
        {
            var stop = routeResult.StopSequence.FirstOrDefault(s => s.LocationId == lpn.Order?.DestLocation);
            int seq = stop?.Sequence ?? 1;

            foreach (var dimensions in GetLoadItemDimensions(lpn))
            {
                var itemWeight = Math.Round(dimensions.totalWeight / dimensions.quantity, 2);
                for (int i = 0; i < dimensions.quantity; i++)
                {
                    engineItems.Add(new ColdChainX.Application.Services.LpnDims
                    {
                        LpnId = lpn.LpnId,
                        PackageLineId = dimensions.packageLineId,
                        PackageLabel = dimensions.packageLabel,
                        Length = dimensions.length,
                        Width = dimensions.width,
                        Height = dimensions.height,
                        RouteStopSequence = seq,
                        WeightKg = itemWeight,
                        RequiredTemperature = _cargoCompatibilityService.ResolveRequiredTemperature(lpn) ?? requiredMinTemp,
                        IsStackable = lpn.Order?.IsStackable ?? true
                    });
                }
            }
        }

        var engine = new ColdChainX.Application.Services.CargoPackingEngine();
        var packingResult = engine.Pack(
            new ColdChainX.Application.Services.ContainerDims { Length = vLength, Width = vWidth, Height = vHeight }, 
            engineItems);

                if (packingResult.UnplacedLpnIds.Any())
        {
            var unplacedCodes = lpns.Where(l => packingResult.UnplacedLpnIds.Contains(l.LpnId)).Select(l => l.LpnCode);
            if (packingResult.Utilisation < 30.0m)
            {
                throw new InvalidOperationException($"Lỗi xếp xe (3D Packing): Thể tích xe mới sử dụng {packingResult.Utilisation:F1}% (< 30%) nhưng đã có kiện rớt. Không thể ghép chuyến, vui lòng đổi xe. Kiện rớt: {string.Join(", ", unplacedCodes)}");
            }
            else
            {
                throw new InvalidOperationException($"Lỗi xếp xe (3D Packing): Xe đã đạt {packingResult.Utilisation:F1}% nhưng vẫn rớt hàng. Vui lòng bỏ bớt các kiện sau để tạo chuyến: {string.Join(", ", unplacedCodes)}");
            }
        }

        var loadPlan = BuildLpnLIFOLoadPlan(lpns, routeResult.StopSequence);

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

        var estimatedDurationHours = directionsResult.TotalDurationSeconds > 0
            ? Math.Round((decimal)directionsResult.TotalDurationSeconds / 3600m, 2)
            : Math.Round(routeResult.TotalDistanceKm / 40m, 2);

        var masterTrip = new MasterTrip
        {
            TripId              = Guid.NewGuid(),
            VehicleId           = vehicle.VehicleId,
            OriginLocationId    = originLocation.LocationId,
            DestinationLocationId = routeResult.StopSequence.Last().LocationId,
            TotalDistanceKm     = routeResult.TotalDistanceKm,
            EstimatedDurationHours = estimatedDurationHours,
            RouteId             = schedule?.RouteId,
            ScheduleId          = schedule?.ScheduleId,
            DepartureDate       = request.PlannedStartTime.Date,
            TargetTemperature   = requiredMinTemp,
            PlannedStartTime    = request.PlannedStartTime,
            PlannedEndTime      = request.PlannedEndTime,
            Status              = "PLANNED",
            CreatedAt           = DateTime.UtcNow,
        };
        _context.MasterTrips.Add(masterTrip);

        if (relayIncident != null && relayPlan != null)
        {
            var redispatchPlannedAt = DateTime.UtcNow;
            relayPlan.RedispatchTripId = masterTrip.TripId;
            relayPlan.RedispatchPlannedAt = redispatchPlannedAt;
            relayIncident.TripId = masterTrip.TripId;
            relayIncident.ReplacementVehicleId = vehicle.VehicleId;
            relayIncident.Status = "REDISPATCH_PLANNED";
            relayIncident.HandledBy = request.DispatcherId;
            relayIncident.HandledAt = redispatchPlannedAt;
            relayIncident.RedispatchPlan = $"Đã ghép chuyến {masterTrip.TripId} từ kho {relayPlan.DestinationWarehouseName}; chờ picking, loading và seal-and-dispatch.";
            relayIncident.RescuePlanDetails = JsonSerializer.Serialize(relayPlan);
        }

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

        foreach (var lpn in lpns)
        {
            lpn.TripId = masterTrip.TripId;
            lpn.State = LpnState.ALLOCATED;
            if (lpn.Order != null)
            {
                lpn.Order.MasterTripId = masterTrip.TripId;
                lpn.Order.Status = "LOADING";
            }
        }

        var perDriverHours = Math.Round(estimatedDurationHours / driverIds.Count, 2);
        var startDay = DateOnly.FromDateTime(request.PlannedStartTime);

        foreach (var driver in drivers)
        {
            var availability = await _driverAvailability.CheckAsync(driver.DriverId, perDriverHours, startDay);
            if (!availability.CanAssign)
            {
                driver.Status = "RELAX";
                await _context.SaveChangesAsync();
                throw new InvalidOperationException(
                    $"Không thể gán tài xế {driver.FullName}: {availability.Reason} " +
                    $"Tài xế được chuyển sang trạng thái RELAX (nghỉ bắt buộc).");
            }
        }

        var assignedDrivers = new List<(Driver Driver, string Role)>();
        for (int i = 0; i < drivers.Count; i++)
        {
            var driver = drivers[i];
            var role = i == 0 ? "PRIMARY" : "SECONDARY";

            _context.TripDrivers.Add(new TripDriver
            {
                TripDriverId          = Guid.NewGuid(),
                TripId                = masterTrip.TripId,
                DriverId              = driver.DriverId,
                DriverRole            = role,
                AssignedDurationHours = perDriverHours,
                CreatedAt             = DateTime.UtcNow
            });

            await _driverAvailability.RecordWorkAsync(driver.DriverId, masterTrip.TripId, perDriverHours, startDay);
            driver.Status = "PLANNING";
            assignedDrivers.Add((driver, role));
        }

        vehicle.Status = "PLANNING";
        await _context.SaveChangesAsync();

        if (relayIncident != null && _workflowNotificationService != null)
        {
            await NotifyManualRedispatchAudiencesAsync(
                relayIncident,
                relayPlan!,
                masterTrip,
                vehicle,
                drivers,
                lpns,
                request.DispatcherId);
        }

        var notifiedCount = 0;
        var driverNotifiedCount = await SendDriverNotificationsAsync(masterTrip, vehicle, drivers);

        await _context.SaveChangesAsync();

        await _context.SaveChangesAsync();
        var routeStops = routeResult.StopSequence.Select(s => new StopDto
        {
            Sequence = s.Sequence, LocationId = s.LocationId, Address = s.Address,
            Latitude = s.Latitude, Longitude = s.Longitude, DistanceFromPreviousKm = s.DistanceFromPreviousKm,
            LpnsToUnload = lpns.Where(l => l.Order?.DestLocation == s.LocationId).Select(l => new LpnSummary
            {
                LpnId = l.LpnId,
                LpnCode = l.LpnCode,
                OrderId = l.OrderId,
                OrderTrackingCode = l.Order?.TrackingCode ?? string.Empty,
                ItemName = l.Order?.ItemName ?? string.Empty,
                Quantity = l.Quantity,
                WeightKg = l.ActualWeightKg,
                Cbm = l.ActualCbm,
                TempCondition = l.Order?.TempCondition ?? "AMBIENT"
            }).ToList()
        }).ToList();

        var flatSteps = new List<StepDto>();
        foreach (var leg in directionsResult.Legs)
        {
            foreach (var st in leg.Steps)
            {
                flatSteps.Add(new StepDto
                {
                    Instruction     = st.Instruction,
                    DistanceKm      = st.DistanceKm,
                    DurationSeconds = st.DurationSeconds,
                    Maneuver        = st.Maneuver
                });
            }
        }

        var lastStop = routeResult.StopSequence.LastOrDefault();
        
        var routeDetails = new RouteDetailsDto
        {
            TotalDistanceKm    = (double)directionsResult.TotalDistanceKm,
            TotalDurationMinutes = directionsResult.TotalDurationSeconds / 60,
            OverviewPolyline   = directionsResult.OverviewPolyline ?? "",
            OriginLat          = originLocation.Latitude,
            OriginLng          = originLocation.Longitude,
            OriginAddress      = originLocation.Address,
            DestinationLat     = lastStop?.Latitude ?? originLocation.Latitude,
            DestinationLng     = lastStop?.Longitude ?? originLocation.Longitude,
            DestinationAddress = lastStop?.Address ?? originLocation.Address,
            Stops              = routeStops,
            Steps              = flatSteps
        };

        var dispatchInstructions = loadPlan.Select(li => new DispatchInstruction
        {
            LpnId = li.LpnId,
            LpnCode = li.LpnCode,
            OrderId = li.OrderId,
            TrackingCode = li.TrackingCode,
            ItemName = li.ItemName,
            Action = "LOAD",
            PreviousStatus = "IN_STOCK",
            TargetStatus = "ALLOCATED",
            LoadOrder = li.LoadOrder,
            Zone = li.Zone
        }).OrderBy(d => d.LoadOrder).ToList();


        var driverInfos = assignedDrivers.Select(ad =>
        {
            var lic = driverLicenses[ad.Driver.DriverId];
            var daysToExpiry = lic.ExpiryDate.DayNumber - today.DayNumber;
            return new DriverInfo
            {
                DriverId              = ad.Driver.DriverId,
                FullName              = ad.Driver.FullName,
                PhoneNumber           = ad.Driver.PhoneNumber,
                IdentityNumber        = ad.Driver.IdentityNumber,
                LicenseClass          = lic.LicenseClass,
                LicenseExpiry         = lic.ExpiryDate,
                LicenseStatus         = daysToExpiry <= 30 ? "EXPIRING_SOON" : "VALID",
                DriverRole            = ad.Role,
                AssignedDurationHours = perDriverHours
            };
        }).ToList();

        return new ManualDispatchResult
        {
            TripId = masterTrip.TripId,
            Vehicle = new VehicleInfo { VehicleId = vehicle.VehicleId, TruckPlate = vehicle.TruckPlate, MaxWeightKg = vehicle.MaxWeight, MaxCbm = vehicle.MaxCbm, TotalOrderWeightKg = totalWeight, TotalOrderCbm = totalCbm, WeightUtilizationPct = Math.Round(totalWeight / vehicle.MaxWeight * 100, 1), CbmUtilizationPct = Math.Round(totalCbm / vehicle.MaxCbm * 100, 1) },
            Drivers = driverInfos,
            EstimatedDurationHours = estimatedDurationHours,
            SelectedLpns = lpns.Select(l => new LpnSummary { LpnId = l.LpnId, LpnCode = l.LpnCode, OrderId = l.OrderId, OrderTrackingCode = l.Order?.TrackingCode ?? string.Empty, ItemName = l.Order?.ItemName ?? string.Empty, Quantity = l.Quantity, WeightKg = l.ActualWeightKg, Cbm = l.ActualCbm, TempCondition = l.Order?.TempCondition ?? "AMBIENT" }).ToList(),
            RouteDetails = routeDetails,
            LoadPlan = loadPlan,
            DispatchInstructions = dispatchInstructions,
            NotifiedCoordinators = notifiedCount,
            LateLpnCount = lateLpns.Count,
            SlaWarning = lateLpns.Any()
                ? $"{lateLpns.Count} LPN đã quá SLA deadline. Khuyến nghị dùng xe tải trọng ≤ 2000 kg."
                : null,
            SuggestedMaxPayloadKg = lateLpns.Any() ? 2000 : null
        };
    }

    public async Task<ManualDispatchResult> CreateTripFromWarehouseAsync(WarehouseRedispatchRequest request)
    {
        var requestedLpnIds = request.LpnIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (requestedLpnIds.Count == 0)
            throw new InvalidOperationException("Phải chọn ít nhất một LPN để tạo chuyến từ kho.");

        var lpns = await _context.Lpns
            .AsNoTracking()
            .Include(l => l.Warehouse)
            .Where(l => requestedLpnIds.Contains(l.LpnId))
            .ToListAsync();

        var missingLpnIds = requestedLpnIds.Except(lpns.Select(l => l.LpnId)).ToList();
        if (missingLpnIds.Count > 0)
            throw new InvalidOperationException($"Không tìm thấy các LPN sau: {string.Join(", ", missingLpnIds)}");

        if (lpns.Any(l => l.State != LpnState.IN_STOCK || l.TripId.HasValue))
            throw new InvalidOperationException("Chỉ được tạo chuyến từ kho cho LPN đang IN_STOCK và chưa thuộc chuyến nào.");

        if (lpns.Any(l => !l.WarehouseId.HasValue))
            throw new InvalidOperationException("Tất cả LPN phải được nhập kho trước khi tạo chuyến lại.");

        var warehouseIds = lpns
            .Select(l => l.WarehouseId!.Value)
            .Distinct()
            .ToList();
        if (warehouseIds.Count != 1)
            throw new InvalidOperationException("Tất cả LPN phải nằm trong cùng một kho để tạo chuyến lại.");

        var noShowIncidents = await _context.IncidentReports
            .AsNoTracking()
            .Where(incident => incident.Status == "READY_FOR_REDISPATCH"
                && incident.IncidentType == IncidentType.CUSTOMER_NO_SHOW_RETURN.ToString()
                && incident.RescuePlanDetails != null)
            .OrderByDescending(incident => incident.ReportedAt)
            .ToListAsync();

        var requestedLpnSet = requestedLpnIds.ToHashSet();
        var matchingIncidentIds = new List<Guid>();
        foreach (var incident in noShowIncidents)
        {
            ExternalReeferPlanRecord? plan;
            try
            {
                plan = JsonSerializer.Deserialize<ExternalReeferPlanRecord>(incident.RescuePlanDetails!);
            }
            catch (JsonException)
            {
                continue;
            }

            if (plan?.ArrivedAt != null
                && plan.DestinationWarehouseId == warehouseIds[0]
                && requestedLpnSet.SetEquals(plan.LpnIds))
            {
                matchingIncidentIds.Add(incident.IncidentId);
            }
        }

        if (matchingIncidentIds.Count == 0)
        {
            throw new InvalidOperationException(
                "Không tìm thấy hồ sơ khách vắng mặt READY_FOR_REDISPATCH khớp với các LPN đã chọn.");
        }
        if (matchingIncidentIds.Count > 1)
        {
            throw new InvalidOperationException(
                "Có nhiều hồ sơ khách vắng mặt cùng khớp với các LPN đã chọn. Vui lòng kiểm tra dữ liệu Incident.");
        }

        var incidentId = matchingIncidentIds[0];

        var warehouse = lpns.First().Warehouse;
        if (warehouse == null)
            throw new InvalidOperationException("Không tìm thấy kho đang giữ LPN.");
        if (!string.IsNullOrWhiteSpace(warehouse.Status)
            && !warehouse.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)
            && !warehouse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Kho {warehouse.WarehouseName} hiện không hoạt động.");
        }

        var warehouseAddress = warehouse.Address?.Trim();
        if (string.IsNullOrWhiteSpace(warehouseAddress))
            throw new InvalidOperationException("Kho đang giữ LPN chưa có địa chỉ để tạo điểm xuất phát.");

        var originLocationId = await _context.Locations
            .AsNoTracking()
            .Where(l => l.Status == "ACTIVE" && l.Address == warehouseAddress)
            .Select(l => l.LocationId)
            .FirstOrDefaultAsync();
        if (originLocationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Không tìm thấy Location ACTIVE trùng địa chỉ kho đang giữ LPN. Cần cấu hình Location cho kho trước khi tạo chuyến.");
        }

        return await ManualDispatchAsync(new ManualDispatchRequest
        {
            IncidentId = incidentId,
            DispatcherId = request.DispatcherId,
            ScheduleId = null,
            LpnIds = requestedLpnIds,
            VehicleId = request.VehicleId,
            DriverIds = request.DriverIds,
            OriginWarehouseLocationId = originLocationId,
            PlannedStartTime = request.PlannedStartTime,
            PlannedEndTime = request.PlannedEndTime,
            ScreenshotBase64 = request.ScreenshotBase64
        });
    }

    private async Task NotifyManualRedispatchAudiencesAsync(
        IncidentReport incident,
        ExternalReeferPlanRecord relayPlan,
        MasterTrip trip,
        Vehicle vehicle,
        IReadOnlyCollection<Driver> drivers,
        IReadOnlyCollection<Lpn> lpns,
        Guid? dispatcherId)
    {
        if (_workflowNotificationService == null)
            return;

        var lpnCodes = lpns.Select(lpn => lpn.LpnCode).Distinct().ToList();
        var lpnSummary = FormatIncidentLpnCodes(lpnCodes);
        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "REDISPATCH_PLANNED",
            Title = "Đã tạo lại chuyến giao hàng khẩn cấp",
            Body = $"Trip {trip.TripId} đã được tạo cho {lpnCodes.Count} LPN: {lpnSummary}.",
            RecipientRoles = new[] { "ADMIN", "DISPATCHER" },
            AdditionalUserIds = dispatcherId.HasValue ? new[] { dispatcherId.Value } : Array.Empty<Guid>(),
            IncludeReporter = false,
            IncludeTripDrivers = false,
            RealtimeGroups = new[] { "Group_Admin", "Group_Dispatcher" },
            RealtimeEventName = "IncidentRedispatchPlanned",
            Payload = new
            {
                incident.IncidentId,
                TripId = trip.TripId,
                VehicleId = vehicle.VehicleId,
                vehicle.TruckPlate,
                LpnCodes = lpnCodes,
                Status = incident.Status,
                Priority = "URGENT"
            }
        });

        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "URGENT_PICKING_REQUIRED",
            Title = "Cần picking chuyến sự cố gấp",
            Body = $"Ưu tiên lấy và xếp {lpnCodes.Count} LPN cho xe {vehicle.TruckPlate}: {lpnSummary}.",
            RecipientRoles = new[] { "WAREHOUSEWORKER" },
            RecipientWarehouseId = relayPlan.DestinationWarehouseId,
            IncludeReporter = false,
            IncludeTripDrivers = false,
            RealtimeEventName = "WarehouseUrgentRedispatchReadyForPicking",
            Payload = new
            {
                incident.IncidentId,
                TripId = trip.TripId,
                WarehouseId = relayPlan.DestinationWarehouseId,
                LpnCodes = lpnCodes,
                vehicle.TruckPlate,
                Priority = "URGENT"
            }
        });

        var driverUserIds = drivers
            .Where(driver => driver.UserId.HasValue)
            .Select(driver => driver.UserId!.Value)
            .Distinct()
            .ToList();
        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "URGENT_REDISPATCH_ASSIGNED",
            Title = "Bạn được phân công chuyến giao lại khẩn cấp",
            Body = $"Nhận xe {vehicle.TruckPlate} tại {relayPlan.DestinationWarehouseName} để tiếp tục giao {lpnCodes.Count} LPN cho khách.",
            AdditionalUserIds = driverUserIds,
            IncludeReporter = false,
            IncludeTripDrivers = false,
            RealtimeEventName = "DriverUrgentRedispatchAssigned",
            Payload = new
            {
                incident.IncidentId,
                TripId = trip.TripId,
                vehicle.TruckPlate,
                WarehouseName = relayPlan.DestinationWarehouseName,
                LpnCodes = lpnCodes,
                PlannedStartTime = trip.PlannedStartTime
            }
        });

        var customerUserCache = new Dictionary<Guid, Guid?>();
        foreach (var order in lpns
                     .Select(lpn => lpn.Order)
                     .Where(order => order != null)
                     .DistinctBy(order => order.OrderId))
        {
            if (!order.CustomerId.HasValue)
                continue;
            if (!customerUserCache.TryGetValue(order.CustomerId.Value, out var customerUserId))
            {
                var customerEmail = await _context.Customers
                    .Where(customer => customer.CustomerId == order.CustomerId.Value)
                    .Select(customer => customer.Email)
                    .FirstOrDefaultAsync();
                customerUserId = string.IsNullOrWhiteSpace(customerEmail)
                    ? null
                    : await _context.Users
                        .Where(user => user.Email != null && user.Email.ToLower() == customerEmail.ToLower())
                        .Select(user => (Guid?)user.UserId)
                        .FirstOrDefaultAsync();
                customerUserCache[order.CustomerId.Value] = customerUserId;
            }
            if (!customerUserId.HasValue)
                continue;

            await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
            {
                IncidentId = incident.IncidentId,
                TripId = trip.TripId,
                Action = "CUSTOMER_REPLACEMENT_VEHICLE_ASSIGNED",
                Title = $"Đã bố trí xe mới cho đơn {order.TrackingCode}",
                Body = $"ColdChainX đã bố trí xe lạnh {vehicle.TruckPlate} để tiếp tục giao đơn. Lịch giao có thể trễ hơn kế hoạch ban đầu.",
                AdditionalUserIds = new[] { customerUserId.Value },
                IncludeReporter = false,
                IncludeTripDrivers = false,
                RealtimeEventName = "CustomerReplacementVehicleAssigned",
                NotificationType = "ORDER_DELAYED",
                ReferenceId = order.OrderId.ToString(),
                Screen = "ORDER_DETAIL",
                AdditionalData = new Dictionary<string, string>
                {
                    ["orderId"] = order.OrderId.ToString(),
                    ["trackingCode"] = order.TrackingCode
                },
                Payload = new
                {
                    incident.IncidentId,
                    order.OrderId,
                    order.TrackingCode,
                    TripId = trip.TripId,
                    vehicle.TruckPlate
                }
            });
        }
    }

    private static string FormatIncidentLpnCodes(IReadOnlyCollection<string> lpnCodes)
    {
        var visible = string.Join(", ", lpnCodes.Take(10));
        return lpnCodes.Count > 10 ? $"{visible} và {lpnCodes.Count - 10} LPN khác" : visible;
    }

    private string NormalizeTempGroup(string tempCondition)
    {
        if (string.IsNullOrWhiteSpace(tempCondition)) return "AMBIENT";
        var t = tempCondition.ToUpperInvariant();
        if (t.Contains("FROZEN") || t.Contains("-20")) return "FROZEN";
        if (t.Contains("CHILLED") || t.Contains("2 TO 8")) return "CHILLED";
        return "AMBIENT";
    }


    public async Task<StartPickingResult> StartPickingAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến hàng.");

        if (trip.Status != "PLANNED")
            throw new InvalidOperationException(
                $"Không thể bắt đầu picking — chuyến đang ở trạng thái '{trip.Status}'. " +
                "Chỉ có thể bắt đầu picking khi trạng thái là PLANNED.");

        trip.Status = "PICKING";

        var allocatedLpns = await _context.Lpns
            .Where(l => l.TripId == tripId && l.State == LpnState.ALLOCATED)
            .ToListAsync();

        foreach (var lpn in allocatedLpns)
            lpn.State = LpnState.LOADING;

        var lpnCount = await _context.Lpns.CountAsync(l => l.TripId == tripId);

        await _context.SaveChangesAsync();

        var linkedIncident = await _context.IncidentReports
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TripId == tripId && i.Status == "REDISPATCH_PLANNED");
        if (linkedIncident != null && _workflowNotificationService != null)
        {
            await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
            {
                IncidentId = linkedIncident.IncidentId,
                TripId = tripId,
                Action = "REDISPATCH_PICKING_STARTED",
                Title = "Bắt đầu lấy hàng cho chuyến giao lại",
                Body = $"Kho đã bắt đầu picking {lpnCount} LPN cho chuyến {tripId}.",
                RecipientRoles = new[] { "ADMIN", "DISPATCHER", "WAREHOUSEWORKER" },
                IncludeReporter = false,
                IncludeTripDrivers = false,
                RealtimeGroups = new[] { "Group_Admin", "Group_Dispatcher", "Group_WarehouseWorker" },
                RealtimeEventName = "IncidentRedispatchPickingStarted",
                Payload = new { linkedIncident.IncidentId, TripId = tripId, LpnCount = lpnCount, Status = "PICKING" }
            });
        }

        try
        {
            await _hubContext.Clients.Group("Group_WarehouseWorker")
                .SendAsync("PickingStarted", new
                {
                    TripId = tripId,
                    Status = "PICKING",
                    LpnCount = lpnCount
                });
        }
        catch (Exception)
        {
        }

        return new StartPickingResult(tripId, "PICKING", lpnCount);
    }


    public async Task<CancelTripResult> CancelTripAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
                    .ThenInclude(d => d.DriverLicenses)
            .Include(t => t.TransportOrders)
            .Include(t => t.Seals)
            .Include(t => t.TripStops)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến hàng.");

        if (trip.Status == "CANCELLED")
            throw new InvalidOperationException("Chuyến hàng đã bị hủy trước đó.");

        var lpns = await _context.Lpns
            .Include(l => l.Order)
            .Where(l => l.TripId == tripId)
            .ToListAsync();

        var shippingLpns = lpns.Where(l => l.State == LpnState.SHIPPING).ToList();
        if (shippingLpns.Any())
            throw new InvalidOperationException(
                $"Không thể hủy chuyến — có {shippingLpns.Count} LPN đã ở trạng thái SHIPPING (hàng đã xuất phát): " +
                $"{string.Join(", ", shippingLpns.Select(l => l.LpnCode))}. " +
                "Chỉ hủy được khi chưa có LPN nào SHIPPING.");

        var previousStatus = trip.Status ?? "UNKNOWN";
        var now = DateTime.UtcNow;

        foreach (var lpn in lpns)
        {
            lpn.State = LpnState.IN_STOCK;
            lpn.TripId = null;
            lpn.UpdatedAt = now;
        }

        var resetOrderCount = 0;
        var orders = lpns.Where(l => l.Order != null).Select(l => l.Order!).Distinct().ToList();
        foreach (var order in orders)
        {
            order.Status = "IN_STOCK";
            order.MasterTripId = null;
            resetOrderCount++;
        }
        foreach (var order in trip.TransportOrders)
        {
            order.Status = "IN_STOCK";
            order.MasterTripId = null;
        }

        var cancelledSealCount = 0;
        foreach (var seal in trip.Seals.Where(s => s.Status != "CANCELLED"))
        {
            seal.Status = "CANCELLED";
            seal.RemovedAt = now;
            cancelledSealCount++;
        }
        trip.SealNumber = null;


        var lpnCodes = lpns.Select(l => l.LpnCode).ToList();
        if (lpnCodes.Count > 0)
        {
            var relatedOrderIds = await _context.OutboundOrderItems
                .Where(i => lpnCodes.Contains(i.ItemCode))
                .Select(i => i.OutboundOrderId)
                .Distinct()
                .ToListAsync();
            if (relatedOrderIds.Count > 0)
            {
                var outboundOrders = await _context.OutboundOrders
                    .Where(o => relatedOrderIds.Contains(o.OutboundOrderId)
                             && o.Status != OutboundOrderStatus.CANCELLED
                             && o.Status != OutboundOrderStatus.SHIPPED)
                    .ToListAsync();
                foreach (var oo in outboundOrders)
                {
                    oo.Status = OutboundOrderStatus.CANCELLED;
                    oo.UpdatedAt = now;
                }
            }
        }

        foreach (var stop in trip.TripStops.Where(s => s.Status != "CANCELLED"))
            stop.Status = "CANCELLED";

        if (trip.Vehicle != null)
            trip.Vehicle.Status = "ACTIVE";

        var workLogs = await _context.DriverWorkLogs
            .Where(w => w.TripId == tripId)
            .ToListAsync();
        if (workLogs.Count > 0)
            _context.DriverWorkLogs.RemoveRange(workLogs);

        foreach (var td in trip.TripDrivers)
        {
            if (td.Driver != null)
                await ReleaseDriverAsync(td.Driver, tripId);
        }

        trip.Status = "CANCELLED";

        await _context.SaveChangesAsync();

        try
        {
            await _hubContext.Clients.Groups("Group_WarehouseWorker", "Group_Admin")
                .SendAsync("TripCancelled", new
                {
                    TripId = tripId,
                    PreviousStatus = previousStatus,
                    Status = "CANCELLED",
                    ResetLpnCount = lpns.Count
                });
        }
        catch (Exception)
        {
        }

        return new CancelTripResult
        {
            TripId              = tripId,
            PreviousStatus      = previousStatus,
            NewStatus           = "CANCELLED",
            ResetLpnCount       = lpns.Count,
            ResetOrderCount     = resetOrderCount,
            CancelledSealCount  = cancelledSealCount,
            VoidedDocumentCount = 0,
            VehiclePlate        = trip.Vehicle?.TruckPlate,
            DriverName          = trip.TripDrivers.Count > 0
                                    ? string.Join(", ", trip.TripDrivers.Select(td => td.Driver?.FullName).Where(n => n != null))
                                    : null,
            CancelledAt         = now,
            Message             = $"Đã hủy chuyến {tripId}. {lpns.Count} LPN đã trở về kho (IN_STOCK), " +
                                  $"xe và tài xế đã được giải phóng."
        };
    }


    public async Task<VehicleIoTStatus> CheckVehicleIoTAsync(Guid vehicleId, Guid tripId)
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

        var deviceStatuses = new List<IoTDeviceStatus>();
        bool hasOffline = false;

        foreach (var device in devices)
        {
            var latestTelemetry = await _context.TelemetryLogs
                .Where(t => t.DeviceId == device.DeviceId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync();

            if (!device.IsOnline)
            {
                hasOffline = true;
            }

            deviceStatuses.Add(new IoTDeviceStatus
            {
                DeviceId = device.DeviceId,
                BatteryLevel = device.BatteryLevel,
                LastPingTime = device.LastPingTime,
                Status = device.Status,
                IsOnline = device.IsOnline,
                LatestTelemetry = latestTelemetry == null ? null : new LatestTelemetry
                {
                    Temperature = latestTelemetry.Temperature,
                    Latitude = latestTelemetry.Latitude,
                    Longitude = latestTelemetry.Longitude,
                    Timestamp = latestTelemetry.Timestamp
                }
            });
        }

        var overallStatus = hasOffline ? "OFFLINE" : "ONLINE";

        if (hasOffline)
        {
            _logger.LogWarning("Vehicle {VehicleId} has offline IoT devices.", vehicleId);
            try
            {
                await _hubContext.Clients.Group($"Vehicle_{vehicleId}").SendAsync("IotWarning", "Một số thiết bị IoT đang mất kết nối. Vui lòng kiểm tra lại nguồn điện.");
            }
            catch { }
        }
        else
        {
            var trip = await _context.MasterTrips
                .Include(t => t.TripDrivers)
                    .ThenInclude(td => td.Driver)
                        .ThenInclude(d => d.DriverLicenses)
                .FirstOrDefaultAsync(t => t.TripId == tripId)
                ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

            if (trip.VehicleId != vehicleId)
                throw new InvalidOperationException("Xe được kiểm tra không thuộc chuyến đi này.");

            EnsureDriversCanDepart(trip);

            foreach (var device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device.DeviceCode))
                {
                    var published = await _mqttPublisher.StartStreamingAsync(device.DeviceCode, CancellationToken.None);
                    if (!published)
                    {
                        throw new InvalidOperationException(
                            $"Không thể bật MQTT streaming cho thiết bị {device.DeviceCode}. Chuyến vẫn chưa được tiếp tục.");
                    }
                }
            }

            if (trip.Status != "IN_TRANSIT" && trip.Status != "COMPLETED")
            {
                trip.Status = "IN_TRANSIT";
                trip.StartedAt ??= DateTime.UtcNow;
                vehicle.Status = "ONTRIP";
                foreach (var td in trip.TripDrivers)
                {
                    if (td.Driver != null)
                        td.Driver.Status = "ONTRIP";
                }
                await _context.SaveChangesAsync();
            }
        }

        return new VehicleIoTStatus
        {
            VehicleId = vehicleId,
            TruckPlate = vehicle.TruckPlate,
            HasIoTDevices = true,
            OverallStatus = overallStatus,
            Devices = deviceStatuses
        };
    }


    public async Task<SealAndDispatchResult> SealAndDispatchAsync(
        Guid tripId, string sealCode, Guid sealedBy)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
                    .ThenInclude(d => d.User)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
                    .ThenInclude(d => d.DriverLicenses)
            .Include(t => t.TransportOrders)
            .Include(t => t.Seals)
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến hàng.");

        if (trip.Status != "LOADING_COMPLETED")
            throw new InvalidOperationException(
                $"Không thể kẹp chì — chuyến đang ở trạng thái '{trip.Status}'. " +
                $"Chỉ kẹp chì được khi trạng thái là LOADING_COMPLETED (kho đã xếp xong).");

        if (trip.Seals.Any(s => s.Status == "APPLIED") || !string.IsNullOrEmpty(trip.SealNumber))
            throw new InvalidOperationException("Chuyến hàng đã được kẹp chì trước đó.");

        EnsureDriversCanDepart(trip);

        var lpns = await _context.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.DestLocationNavigation)
            .Include(l => l.Customer)
            .Where(l => l.TripId == tripId)
            .ToListAsync();

        if (lpns.Count == 0)
            throw new InvalidOperationException("Chuyến đi không có LPN nào.");

        var totalLpns = lpns.Count;
        var loadedLpns = lpns.Count(l => l.State == LpnState.RELEASED);
        var allLoaded = loadedLpns == totalLpns;

        if (!allLoaded)
        {
            var notLoadedLpns = lpns
                .Where(l => l.State != LpnState.RELEASED)
                .Select(l => l.LpnCode)
                .ToList();
            throw new InvalidOperationException(
                $"Chưa xác nhận xuất kho hết LPN! Còn {totalLpns - loadedLpns}/{totalLpns} LPN chưa RELEASED: " +
                $"{string.Join(", ", notLoadedLpns)}. " +
                $"Tất cả LPN phải ở trạng thái RELEASED trước khi kẹp chì.");
        }

        _context.Seals.Add(new Seal
        {
            SealId    = Guid.NewGuid(),
            TripId    = tripId,
            SealCode  = sealCode,
            AppliedAt = DateTime.UtcNow,
            Status    = "APPLIED",
            CreatedAt = DateTime.UtcNow
        });

        foreach (var lpn in lpns)
            lpn.State = LpnState.SHIPPING;

        trip.SealNumber = sealCode;

        trip.Status = "SEALED";

        foreach (var order in trip.TransportOrders)
            order.Status = "SEALED";

        var now = DateTime.UtcNow;
        var lpnsByCustomer = lpns.GroupBy(l => l.CustomerId ?? Guid.Empty);

        foreach (var customerGroup in lpnsByCustomer)
        {
            var firstLpn = customerGroup.First();
            var customer  = firstLpn.Customer;

            var outboundOrder = new OutboundOrder
            {
                OutboundOrderId    = Guid.NewGuid(),
                OrderCode          = $"OUT-{now:yyyyMMddHHmmss}-{customerGroup.Key.ToString("N")[..8]}",
                CustomerId         = customerGroup.Key,
                ReceiverName       = customer?.CompanyName ?? customerGroup.Key.ToString(),
                ReceiverPhone      = customer?.Email ?? string.Empty,
                DestinationAddress = firstLpn.Order?.DestLocationNavigation?.Address ?? string.Empty,
                Status             = ColdChainX.Core.Enums.OutboundOrderStatus.SHIPPED,
                CreatedAt          = now,
                CreatedBy          = sealedBy
            };
            _context.OutboundOrders.Add(outboundOrder);

            foreach (var lpn in customerGroup)
            {
                _context.OutboundOrderItems.Add(new OutboundOrderItem
                {
                    OutboundOrderItemId = Guid.NewGuid(),
                    OutboundOrderId     = outboundOrder.OutboundOrderId,
                    ItemCode            = lpn.LpnCode,
                    ItemName            = lpn.Order?.ItemName ?? string.Empty,
                    Unit                = lpn.Order?.PackingType ?? string.Empty,
                    Quantity            = lpn.Quantity
                });
            }
        }

        string? waybillUrl = null;
        try
        {
            waybillUrl = await GenerateWaybillPdfAsync(trip);
            var documentUploader = sealedBy;
            var primaryDriverUserId = trip.TripDrivers
                .OrderBy(td => td.DriverRole == "PRIMARY" ? 0 : 1)
                .Select(td => td.Driver?.User?.UserId)
                .FirstOrDefault(uid => uid.HasValue);
            if (primaryDriverUserId.HasValue
                && await _context.Users.AnyAsync(u => u.UserId == primaryDriverUserId.Value))
            {
                documentUploader = primaryDriverUserId.Value;
            }
            _context.TransportDocuments.Add(new TransportDocument
            {
                DocId = Guid.NewGuid(),
                DocType = "E-WAYBILL",
                ImageUrl = waybillUrl,
                CreatedAt = DateTime.UtcNow,
                UploadedBy = documentUploader
            });
            trip.Status = "IN_TRANSIT";
            trip.StartedAt ??= DateTime.UtcNow;
            if (trip.Vehicle != null)
                trip.Vehicle.Status = "ONTRIP";
            foreach (var td in trip.TripDrivers)
            {
                if (td.Driver != null)
                    td.Driver.Status = "ONTRIP";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Waybill generation failed for trip {TripId}. Trip remains SEALED.", tripId);
        }

        var linkedIncidents = new List<IncidentReport>();
        if (trip.Status == "IN_TRANSIT")
        {
            linkedIncidents = await _context.IncidentReports
                .Where(i => i.TripId == tripId && i.Status == "REDISPATCH_PLANNED")
                .ToListAsync();
            foreach (var incident in linkedIncidents)
            {
                incident.Status = "REDISPATCHED_TO_CUSTOMER";
                incident.RescueDispatchedAt = now;
                incident.HandledAt = now;
                incident.RedispatchPlan = $"Chuyến {tripId} đã kẹp seal {sealCode} và xuất phát giao khách.";
            }
        }

        await _context.SaveChangesAsync();

        if (_workflowNotificationService != null)
        {
            var incidentsToNotify = linkedIncidents.Count > 0
                ? linkedIncidents
                : await _context.IncidentReports
                    .AsNoTracking()
                    .Where(i => i.TripId == tripId && i.Status == "REDISPATCH_PLANNED")
                    .ToListAsync();
            foreach (var incident in incidentsToNotify)
            {
                var departed = trip.Status == "IN_TRANSIT";
                await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
                {
                    IncidentId = incident.IncidentId,
                    TripId = tripId,
                    Action = departed ? "REDISPATCHED_TO_CUSTOMER" : "REDISPATCH_SEALED",
                    Title = departed ? "Chuyến giao lại đã rời kho" : "Chuyến giao lại đã kẹp seal",
                    Body = departed
                        ? $"Chuyến {tripId} đã kẹp seal {sealCode} và đang giao hàng cho khách."
                        : $"Chuyến {tripId} đã kẹp seal {sealCode}; đang chờ hoàn tất chứng từ để rời kho.",
                    RecipientRoles = new[] { "ADMIN", "DISPATCHER", "WAREHOUSEWORKER" },
                    IncludeReporter = false,
                    IncludeTripDrivers = false,
                    AdditionalUserIds = trip.TripDrivers
                        .Where(td => td.Driver?.UserId.HasValue == true)
                        .Select(td => td.Driver!.UserId!.Value)
                        .Append(sealedBy)
                        .ToList(),
                    RealtimeGroups = new[] { "Group_Admin", "Group_Dispatcher", "Group_WarehouseWorker" },
                    RealtimeEventName = departed
                        ? "IncidentRedispatchedToCustomer"
                        : "IncidentRedispatchSealed",
                    Payload = new
                    {
                        incident.IncidentId,
                        TripId = tripId,
                        SealCode = sealCode,
                        TripStatus = trip.Status,
                        WaybillUrl = waybillUrl
                    }
                });
            }
        }

        return new SealAndDispatchResult
        {
            TripId = tripId,
            SealCode = sealCode,
            AllOrdersLoaded = allLoaded,
            TotalOrders = totalLpns,
            LoadedOrders = loadedLpns,
            SealedAt = DateTime.UtcNow,
            SealedBy = sealedBy,
            TripStatus = trip.Status ?? "SEALED",
            WaybillUrl = waybillUrl
        };
    }


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
            return await GetFallbackTemplateIdAsync() ?? templateId;
        }

        return templateId;
    }


    public async Task<string> SuggestLoadPlanAsync(List<Guid> orderIds, Guid vehicleId)
    {
        var vehicle = await _context.Vehicles.FindAsync(vehicleId)
            ?? throw new Exception("Vehicle not found.");

        var orders = await _context.TransportOrders
            .Where(o => orderIds.Contains(o.OrderId))
            .ToListAsync();

        if (orders.Count == 0) throw new Exception("No orders found.");

        decimal totalWeight = orders.Sum(o => (o.OrderDimension?.ExpectedWeightKg ?? 0m));

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

        if (destLocationIds.Any())
        {
            trip.DestinationLocationId = destLocationIds.Last();
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
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
                    .ThenInclude(d => d.User)
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.TransportOrders)
                .ThenInclude(o => o.Customer)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new Exception("Trip not found.");

        var pdfUrl = await GenerateWaybillPdfAsync(trip);

        var documentUploader = issuerId ?? Guid.Empty;
        var primaryDriverUserId = trip.TripDrivers
            .OrderBy(td => td.DriverRole == "PRIMARY" ? 0 : 1)
            .Select(td => td.Driver?.User?.UserId)
            .FirstOrDefault(uid => uid.HasValue);
        if (primaryDriverUserId.HasValue && await _context.Users.AnyAsync(u => u.UserId == primaryDriverUserId.Value))
        {
            documentUploader = primaryDriverUserId.Value;
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

        var lpns = await _context.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.Customer)
            .Where(l => l.TripId == trip.TripId)
            .ToListAsync();

        var ordersRows = "";
        int no = 1;
        foreach (var lpn in lpns)
        {
            ordersRows += $@"
            <tr>
                <td>{no}</td>
                <td>{lpn.LpnCode} / {lpn.Order?.TrackingCode ?? "N/A"}</td>
                <td>{lpn.Order?.Customer?.CompanyName ?? "Khách hàng vãng lai"}</td>
                <td>{lpn.Order?.ItemName ?? "N/A"}</td>
                <td>{lpn.Quantity}</td>
                <td>{lpn.ActualWeightKg:0.##} kg</td>
                <td>{lpn.Order?.TempCondition ?? "AMBIENT"}</td>
            </tr>";
            no++;
        }

        var tripDriversOrdered = trip.TripDrivers
            .OrderBy(td => td.DriverRole == "PRIMARY" ? 0 : 1)
            .ToList();
        var primaryDriver = tripDriversOrdered.Select(td => td.Driver).FirstOrDefault(d => d != null);
        var driverNames = tripDriversOrdered.Count > 0
            ? string.Join(", ", tripDriversOrdered.Select(td => td.Driver?.FullName).Where(n => !string.IsNullOrEmpty(n)))
            : null;

        var replacements = new Dictionary<string, string?>
        {
            ["Trip_Id"] = trip.TripId.ToString(),
            ["Issue_Date"] = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["Truck_Plate"] = trip.Vehicle?.TruckPlate ?? "N/A",
            ["Vehicle_Type"] = trip.Vehicle?.VehicleType ?? "N/A",
            ["Driver_Name"] = string.IsNullOrEmpty(driverNames) ? "N/A" : driverNames,
            ["Driver_Phone"] = primaryDriver?.PhoneNumber ?? "N/A",
            ["Driver_Identity"] = primaryDriver?.IdentityNumber ?? "N/A",
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
        var tripExists = await _context.MasterTrips.AnyAsync(t => t.TripId == tripId);
        if (!tripExists)
            throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        var stops = await _context.TripStops
            .Where(ts => ts.TripId == tripId)
            .OrderBy(ts => ts.StopSequence)
            .ToListAsync();

        var stopInfos = stops.Select(s => new StopInfo
        {
            LocationId = s.LocationId ?? Guid.Empty,
            Sequence = s.StopSequence
        }).ToList();

        var lpns = await _context.Lpns
            .Include(l => l.Order)
            .Where(l => l.TripId == tripId)
            .ToListAsync();

        var loadPlan = BuildLpnLIFOLoadPlan(lpns, stopInfos);
        return loadPlan;
    }

    public async Task<string> GenerateLoadPlanPdfAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
            .Include(t => t.OriginLocation)
            .Include(t => t.DestinationLocation)
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

        var lpns = await _context.Lpns
            .Include(l => l.Order)
            .Where(l => l.TripId == tripId)
            .ToListAsync();

        var loadPlan = BuildLpnLIFOLoadPlan(lpns, stopInfos);

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

        var doorRow = @"<tr>
            <td colspan='2' style='background:#fef3c7;border:2px dashed #f59e0b;padding:10px;text-align:center;font-size:13px;font-weight:700;color:#92400e;border-radius:0 0 8px 8px;'>
                🚪 CỬA XE — HÀNG XẾP SAU CÙNG SẼ ĐƯỢC DỠ TRƯỚC TIÊN
            </td>
        </tr>";

        var instructionRows = "";
        foreach (var item in loadPlan)
        {
            var color  = TempColor(item.Zone);
            var bg     = TempBg(item.Zone);
            var stopAddr = item.DeliveryLocationId != Guid.Empty && stopAddresses.TryGetValue(item.DeliveryLocationId, out var addr) ? addr : "—";
            instructionRows += $@"<tr>
                <td style='text-align:center;font-weight:700;font-size:14px;color:{color};padding:8px;border:1px solid #e2e8f0;'>{item.LoadOrder}</td>
                <td style='padding:8px;border:1px solid #e2e8f0;font-size:11px;color:#6b7280;'>{item.LpnCode} / {item.TrackingCode}</td>
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
  <div class='info-card'><div class='label'>👤 Tài xế</div><div class='value'>{System.Net.WebUtility.HtmlEncode(trip.TripDrivers.Count > 0 ? string.Join(", ", trip.TripDrivers.Select(td => td.Driver?.FullName).Where(n => !string.IsNullOrEmpty(n))) : "N/A")}</div></div>
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
      <th>Mã LPN / Mã đơn</th>
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


    private const string WarehouseWorkerRoleName = "WarehouseWorker";
    private const string WarehouseWorkerNotificationTemplateId = "DISPATCH_WAREHOUSE_WORKER_READY";

    public async Task NotifyLoadersAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        var loaderUserIds = await _context.Users
            .Include(u => u.Role)
            .Where(u => u.Role != null
                     && u.Role.RoleName == WarehouseWorkerRoleName
                     && (u.Status == null || u.Status == "ACTIVE"))
            .Select(u => u.UserId)
            .ToListAsync();

        if (loaderUserIds.Count == 0) return;

        var templateExists = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == WarehouseWorkerNotificationTemplateId);

        if (!templateExists)
        {
            var msgType = await _context.Messagetypes.FirstOrDefaultAsync();
            if (msgType != null)
            {
                _context.NotificationTemplates.Add(new NotificationTemplate
                {
                    TemplateId = WarehouseWorkerNotificationTemplateId,
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

        var actualTemplateId = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == WarehouseWorkerNotificationTemplateId
                        && (t.Status == null || t.Status == "ACTIVE"))
            ? WarehouseWorkerNotificationTemplateId
            : await GetFallbackTemplateIdAsync();

        if (actualTemplateId == null) return;

        foreach (var userId in loaderUserIds)
        {
            var notifParams = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "tripId",      trip.TripId.ToString() },
                { "vehicle",     trip.Vehicle?.TruckPlate ?? "N/A" },
                { "orderCount",  trip.TransportOrders.Count.ToString() },
                { "totalWeight", trip.TransportOrders.Sum(o => (o.OrderDimension?.ExpectedWeightKg ?? 0m)).ToString("F1") },
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
            await _hubContext.Clients.Groups("Group_WarehouseWorker", "Group_Admin")
                .SendAsync("WarehouseOrderApproved", new
                {
                    TripId = tripId,
                    Status = "LOADING",
                    Vehicle = trip.Vehicle?.TruckPlate ?? "N/A",
                    OrderCount = trip.TransportOrders.Count,
                    TotalWeight = trip.TransportOrders.Sum(o => (o.OrderDimension?.ExpectedWeightKg ?? 0m))
                });
        }
        catch (Exception)
        {
        }
    }

    private static bool HasValidDriverLicense(Driver driver)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return driver.DriverLicenses.Any(l =>
            l.ExpiryDate >= today
            && (string.IsNullOrWhiteSpace(l.Status)
                || l.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)));
    }

    private static void EnsureDriversCanDepart(MasterTrip trip)
    {
        var assignedDrivers = trip.TripDrivers
            .Select(td => td.Driver)
            .Where(driver => driver != null)
            .Cast<Driver>()
            .ToList();

        if (assignedDrivers.Count == 0)
            throw new InvalidOperationException("Chuyến đi chưa được gán tài xế.");

        foreach (var driver in assignedDrivers)
        {
            var status = driver.Status?.Trim().ToUpperInvariant();
            if (status is "INACTIVE" or "RELAX" or "SUSPENDED_DOCS" or "DELETED")
            {
                throw new InvalidOperationException(
                    $"Tài xế {driver.FullName} không thể xuất phát — trạng thái hiện tại: '{driver.Status}'.");
            }

            if (!HasValidDriverLicense(driver))
            {
                throw new InvalidOperationException(
                    $"Tài xế {driver.FullName} không thể xuất phát vì GPLX đang thiếu hoặc đã hết hạn.");
            }
        }
    }

    private async Task ReleaseDriverAsync(Driver driver, Guid? excludedTripId = null)
    {
        if (!HasValidDriverLicense(driver))
        {
            driver.Status = "SUSPENDED_DOCS";
            return;
        }

        driver.Status = "ACTIVE";
        await _driverAvailability.ReconcileStatusAsync(driver, excludedTripId);
    }

    private const string DriverTripAssignedTemplateId = "DISPATCH_DRIVER_ASSIGNED";

    private async Task<int> SendDriverNotificationsAsync(MasterTrip trip, Vehicle vehicle, List<Driver> drivers)
    {
        if (_notificationService != null)
        {
            try
            {
                var recipientIds = drivers
                    .Where(driver => driver.UserId.HasValue)
                    .Select(driver => driver.UserId!.Value)
                    .Distinct()
                    .ToList();

                if (recipientIds.Count == 0)
                    return 0;

                var result = await _notificationService.SendToUsersAsync(
                    recipientIds,
                    "Bạn có chuyến mới",
                    "Bạn vừa được phân công một chuyến vận chuyển mới.",
                    "TRIP_ASSIGNED",
                    trip.TripId.ToString(),
                    new Dictionary<string, string>
                    {
                        ["tripId"] = trip.TripId.ToString(),
                        ["vehiclePlate"] = vehicle.TruckPlate,
                        ["screen"] = "trip-detail"
                    });

                if (result.FailedSends > 0)
                {
                    _logger.LogWarning(
                        "FCM trip assignment notification was only partially delivered. TripId: {TripId}, Successful: {Successful}, Failed: {Failed}.",
                        trip.TripId,
                        result.SuccessfulSends,
                        result.FailedSends);
                }

                return result.NotificationIds.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "FCM trip assignment notification failed after the trip assignment was saved. TripId: {TripId}.",
                    trip.TripId);
                return 0;
            }
        }

        var templateExists = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == DriverTripAssignedTemplateId
                        && (t.Status == null || t.Status == "ACTIVE"));

        if (!templateExists)
        {
            var msgType = await _context.Messagetypes.FirstOrDefaultAsync();
            if (msgType != null)
            {
                _context.NotificationTemplates.Add(new NotificationTemplate
                {
                    TemplateId = DriverTripAssignedTemplateId,
                    TypeId = msgType.TypeId,
                    TitleTemplate = "Bạn được gán chuyến mới {tripId}",
                    BodyTemplate = "Bạn đã được gán vào chuyến xe {vehiclePlate} dự kiến khởi hành lúc {startTime}.",
                    Channel = "IN_APP",
                    Status = "ACTIVE"
                });
                await _context.SaveChangesAsync();
            }
        }

        var actualTemplateId = await _context.NotificationTemplates
            .AnyAsync(t => t.TemplateId == DriverTripAssignedTemplateId
                        && (t.Status == null || t.Status == "ACTIVE"))
            ? DriverTripAssignedTemplateId
            : await GetFallbackTemplateIdAsync();

        if (actualTemplateId == null) return 0;

        int notifiedCount = 0;
        var notifParams = System.Text.Json.JsonSerializer.Serialize(new
        {
            tripId = trip.TripId,
            vehiclePlate = vehicle.TruckPlate,
            startTime = trip.PlannedStartTime.ToString("dd/MM/yyyy HH:mm")
        });

        foreach (var driver in drivers)
        {
            if (driver.UserId.HasValue)
            {
                _context.Notifications.Add(new Notification
                {
                    NotiId     = Guid.NewGuid(),
                    UserId     = driver.UserId.Value,
                    SenderId   = null,
                    TemplateId = actualTemplateId,
                    Params     = notifParams,
                    OrderId    = null,
                    IsRead     = false,
                    CreatedAt  = DateTime.UtcNow
                });
                notifiedCount++;
            }
        }

        return notifiedCount;
    }
}
