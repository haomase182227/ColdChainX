import re

with open('ColdChainX.Infrastructure/Services/DispatchService.cs', 'r') as f:
    content = f.read()

start_marker = "    //  API 1: AUTO-DISPATCH — Tự động ghép chuyến"
end_marker = "    // ═══════════════════════════════════════════════════════════════════════\n    //  API 2: WAREHOUSE ORDER — Lệnh bốc xếp cho kho"

start_idx = content.find(start_marker)
end_idx = content.find(end_marker)

new_method = """    //  API 1: MANUAL DISPATCH — Ghép chuyến thủ công
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<ManualDispatchResult> ManualDispatchAsync(ManualDispatchRequest request)
    {
        // 1. Validate kho xuất phát
        var originLocation = await _context.Locations.FindAsync(request.OriginWarehouseLocationId)
            ?? throw new InvalidOperationException("LocationId kho xuất phát không tồn tại.");

        // 2. Validate đơn hàng
        var orders = await _context.TransportOrders
            .Include(o => o.DestLocationNavigation)
            .Where(o => request.OrderIds.Contains(o.OrderId))
            .ToListAsync();

        if (orders.Count == 0)
            throw new InvalidOperationException("Không tìm thấy đơn hàng nào khớp với danh sách đã chọn.");

        if (orders.Any(o => o.Status != "IN_WAREHOUSE"))
            throw new InvalidOperationException("Chỉ được ghép chuyến các đơn hàng có trạng thái IN_WAREHOUSE.");

        var missingOrders = request.OrderIds.Except(orders.Select(o => o.OrderId)).ToList();
        if (missingOrders.Any())
            throw new InvalidOperationException($"Không tìm thấy các đơn hàng sau: {string.Join(", ", missingOrders)}");

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

        // Lấy danh sách TripId đang active để check xe bận
        var isBusy = await _context.MasterTrips
            .AnyAsync(t => t.VehicleId == request.VehicleId
                        && (t.Status == "PLANNED" || t.Status == "LOADING"
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

        // 9. Tạo Trip
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

        foreach (var order in orders)
        {
            order.Status       = "DISPATCHED_PENDING";
            order.MasterTripId = masterTrip.TripId;
        }

        var notifiedCount = await SendLoadingNotificationsAsync(masterTrip, orders, vehicle, loadPlan, null);
        await _context.SaveChangesAsync();

        // 10. Build response
        var routeStops = routeResult.StopSequence.Select(s => new RouteStop
        {
            Sequence = s.Sequence, LocationId = s.LocationId, Address = s.Address,
            Latitude = s.Latitude, Longitude = s.Longitude, DistanceFromPreviousKm = s.DistanceFromPreviousKm,
            OrdersToUnload = orders.Where(o => o.DestLocation == s.LocationId).Select(o => new OrderSummary
            { OrderId = o.OrderId, TrackingCode = o.TrackingCode, ItemName = o.ItemName, Quantity = o.Quantity, WeightKg = o.ExpectedWeightKg, Cbm = o.ExpectedCbm, TempCondition = o.TempCondition }).ToList()
        }).ToList();

        var dispatchInstructions = loadPlan.Select(li => new DispatchInstruction
        { OrderId = li.OrderId, TrackingCode = li.TrackingCode, ItemName = li.ItemName, Action = "LOAD", PreviousStatus = "IN_WAREHOUSE", TargetStatus = "DISPATCHED_PENDING", LoadOrder = li.LoadOrder, Zone = li.Zone }).OrderBy(d => d.LoadOrder).ToList();

        var daysToExpiry = activeLicense.ExpiryDate.DayNumber - today.DayNumber;
        var licenseStatus = daysToExpiry <= 30 ? "EXPIRING_SOON" : "VALID";

        return new ManualDispatchResult
        {
            TripId = masterTrip.TripId,
            Vehicle = new VehicleInfo { VehicleId = vehicle.VehicleId, TruckPlate = vehicle.TruckPlate, MaxWeightKg = vehicle.MaxWeight, MaxCbm = vehicle.MaxCbm, TotalOrderWeightKg = totalWeight, TotalOrderCbm = totalCbm, WeightUtilizationPct = Math.Round(totalWeight / vehicle.MaxWeight * 100, 1), CbmUtilizationPct = Math.Round(totalCbm / vehicle.MaxCbm * 100, 1) },
            Driver = new DriverInfo { DriverId = vehicle.Driver.DriverId, FullName = vehicle.Driver.FullName, PhoneNumber = vehicle.Driver.PhoneNumber, IdentityNumber = vehicle.Driver.IdentityNumber, LicenseClass = activeLicense.LicenseClass, LicenseExpiry = activeLicense.ExpiryDate, LicenseStatus = licenseStatus },
            SelectedOrders = orders.Select(o => new OrderSummary { OrderId = o.OrderId, TrackingCode = o.TrackingCode, ItemName = o.ItemName, Quantity = o.Quantity, WeightKg = o.ExpectedWeightKg, Cbm = o.ExpectedCbm, TempCondition = o.TempCondition }).ToList(),
            Route = new RouteInfo { TotalDistanceKm = routeResult.TotalDistanceKm, TotalStops = routeStops.Count, Stops = routeStops },
            Navigation = new NavigationInfo { TotalDistanceKm = directionsResult.TotalDistanceKm, TotalDurationMinutes = directionsResult.TotalDurationSeconds / 60, GoongRouteOverview = directionsResult.OverviewPolyline ?? "", Legs = directionsResult.Legs.Select((leg, idx) => new NavigationLeg { LegIndex = idx + 1, FromAddress = leg.StartAddress ?? "N/A", ToAddress = leg.EndAddress ?? "N/A", DistanceKm = leg.DistanceKm, DurationMinutes = leg.DurationSeconds / 60, Steps = leg.Steps.Select((step, sIdx) => new NavigationStep { StepIndex = sIdx + 1, Instruction = step.Instruction, DistanceKm = step.DistanceKm, DurationSeconds = step.DurationSeconds, Maneuver = step.Maneuver }).ToList() }).ToList() },
            LoadPlan = loadPlan,
            DispatchInstructions = dispatchInstructions,
            NotifiedCoordinators = notifiedCount
        };
    }

"""

new_content = content[:start_idx] + new_method + content[end_idx:]

with open('ColdChainX.Infrastructure/Services/DispatchService.cs', 'w') as f:
    f.write(new_content)
