using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Features.Delivery.Commands;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Text.Json;

namespace ColdChainX.UnitTests;

public sealed class IncidentRescueFlowTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly FakeMqttPublisher _mqtt = new();
    private readonly IncidentRescueService _service;

    private readonly Guid _dispatcherId = Guid.NewGuid();
    private readonly Guid _driverUserId = Guid.NewGuid();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly Guid _tripId = Guid.NewGuid();
    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _brokenVehicleId = Guid.NewGuid();
    private readonly Guid _replacementVehicleId = Guid.NewGuid();
    private readonly Guid _replacementDeviceId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _lpnId = Guid.NewGuid();

    public IncidentRescueFlowTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new IncidentRescueService(
            _db,
            new FakeGoongMapService(),
            new FakeNotificationHubContext(),
            _mqtt,
            NullLogger<IncidentRescueService>.Instance);
    }

    [Fact]
    public async Task RescueCandidates_ReturnOnlyActiveFullCapacityTemperatureCompatibleVehiclesWithIot()
    {
        await SeedRescueTripAsync(replacementOnline: false);

        _db.Vehicles.AddRange(
            BuildVehicle(Guid.NewGuid(), "NO-IOT", "ACTIVE", 5000m, 30m, -20m, 10m),
            BuildVehicle(Guid.NewGuid(), "TOO-SMALL", "ACTIVE", 500m, 1m, -20m, 10m,
                new IotDevice { DeviceId = Guid.NewGuid(), DeviceCode = "IOT-SMALL", IsOnline = true }),
            BuildVehicle(Guid.NewGuid(), "WRONG-TEMP", "ACTIVE", 5000m, 30m, 2m, 10m,
                new IotDevice { DeviceId = Guid.NewGuid(), DeviceCode = "IOT-WARM", IsOnline = true }),
            BuildVehicle(Guid.NewGuid(), "NOT-ACTIVE", "MAINTENANCE", 5000m, 30m, -20m, 10m,
                new IotDevice { DeviceId = Guid.NewGuid(), DeviceCode = "IOT-MAINT", IsOnline = true }));
        await _db.SaveChangesAsync();

        var result = await _service.GetRescueCandidatesAsync(_incidentId);

        Assert.True(result.Success);
        var candidate = Assert.Single(result.Data!);
        Assert.Equal(_replacementVehicleId, candidate.VehicleId);
        Assert.Equal(1, candidate.IotDeviceCount);
        Assert.False(candidate.HasOnlineIot);
    }

    [Fact]
    public async Task RescueCandidates_WhenNoWholeLoadVehicle_ReturnExactMessage()
    {
        await SeedRescueTripAsync(replacementOnline: false);
        var replacement = await _db.Vehicles.FindAsync(_replacementVehicleId);
        replacement!.MaxWeight = 100m;
        await _db.SaveChangesAsync();

        var result = await _service.GetRescueCandidatesAsync(_incidentId);

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
        Assert.Equal("Không có xe thay thế phù hợp", result.Message);
    }

    [Fact]
    public async Task RescueCandidates_SortVehiclesByWarehouseDistanceFromIncident()
    {
        await SeedRescueTripAsync(replacementOnline: true);

        var nearWarehouseId = Guid.NewGuid();
        var farWarehouseId = Guid.NewGuid();
        _db.Warehouses.AddRange(
            new Warehouse
            {
                WarehouseId = nearWarehouseId,
                WarehouseCode = "NEAR-HUB",
                WarehouseName = "Kho gần hiện trường",
                WarehouseType = "HUB",
                Address = "10.701,106.701",
                MaxPallets = 100,
                Status = "ACTIVE"
            },
            new Warehouse
            {
                WarehouseId = farWarehouseId,
                WarehouseCode = "FAR-HUB",
                WarehouseName = "Kho xa hiện trường",
                WarehouseType = "HUB",
                Address = "11.1,107.1",
                MaxPallets = 100,
                Status = "ACTIVE"
            });

        var existingReplacement = await _db.Vehicles.FindAsync(_replacementVehicleId);
        existingReplacement!.CurrentLocation = farWarehouseId.ToString();
        var nearVehicle = BuildVehicle(
            Guid.NewGuid(),
            "NEAR-TRUCK",
            "ACTIVE",
            3000m,
            20m,
            -20m,
            10m,
            new IotDevice
            {
                DeviceId = Guid.NewGuid(),
                DeviceCode = "IOT-NEAR",
                IsOnline = true
            });
        nearVehicle.CurrentLocation = nearWarehouseId.ToString();
        _db.Vehicles.Add(nearVehicle);
        await _db.SaveChangesAsync();

        var result = await _service.GetRescueCandidatesAsync(_incidentId);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("NEAR-TRUCK", result.Data[0].TruckPlate);
        Assert.Equal(nearWarehouseId, result.Data[0].WarehouseId);
        Assert.Equal("Kho gần hiện trường", result.Data[0].WarehouseName);
        Assert.NotNull(result.Data[0].DistanceKm);
        Assert.True(result.Data[0].DistanceKm < result.Data[1].DistanceKm);
    }

    [Fact]
    public async Task DispatchAndConfirmTransload_KeepTripAndCargoIdsAndRequireOnlineMqtt()
    {
        await SeedRescueTripAsync(replacementOnline: false);

        var dispatch = await _service.DispatchRescueAsync(
            _incidentId,
            new DispatchRescueRequest
            {
                ReplacementVehicleId = _replacementVehicleId,
                TransloadMinutes = 30,
                Note = "Điều xe đến hiện trường."
            },
            _dispatcherId);

        Assert.True(dispatch.Success, dispatch.Message);
        var tripAfterDispatch = await _db.MasterTrips.FindAsync(_tripId);
        var incidentAfterDispatch = await _db.IncidentReports.FindAsync(_incidentId);
        var brokenVehicle = await _db.Vehicles.FindAsync(_brokenVehicleId);
        var replacementVehicle = await _db.Vehicles.FindAsync(_replacementVehicleId);
        var lpn = await _db.Lpns.FindAsync(_lpnId);
        var order = await _db.TransportOrders.FindAsync(_orderId);
        var device = await _db.IotDevices.FindAsync(_replacementDeviceId);

        Assert.Equal(_replacementVehicleId, tripAfterDispatch!.VehicleId);
        Assert.Equal("DELAYED", tripAfterDispatch.Status);
        Assert.Equal("MAINTENANCE", brokenVehicle!.Status);
        Assert.Equal("ONTRIP", replacementVehicle!.Status);
        Assert.Equal("RESCUE_DISPATCHED", incidentAfterDispatch!.Status);
        Assert.Equal(_brokenVehicleId, incidentAfterDispatch.BrokenVehicleId);
        Assert.Equal(_replacementVehicleId, incidentAfterDispatch.ReplacementVehicleId);
        Assert.Equal(_tripId, lpn!.TripId);
        Assert.Equal(_tripId, order!.MasterTripId);
        Assert.Equal(_replacementVehicleId, device!.VehicleId);
        Assert.Single(await _db.MasterTrips.ToListAsync());

        var maintenance = Assert.Single(await _db.MaintenanceTickets.ToListAsync());
        Assert.Equal(_brokenVehicleId, maintenance.VehicleId);
        Assert.Equal("OPEN", maintenance.Status);

        var offlineConfirmation = await _service.ConfirmTransloadAsync(
            _incidentId,
            new ConfirmTransloadRequest { ConfirmationNote = "Đã sang đủ hàng." },
            _dispatcherId);
        Assert.False(offlineConfirmation.Success);
        Assert.Contains("chưa online", offlineConfirmation.Message);
        Assert.Equal("DELAYED", (await _db.MasterTrips.FindAsync(_tripId))!.Status);
        Assert.Empty(_mqtt.StreamingDeviceCodes);

        device.IsOnline = true;
        await _db.SaveChangesAsync();

        _mqtt.PublishSucceeds = false;
        var mqttFailure = await _service.ConfirmTransloadAsync(
            _incidentId,
            new ConfirmTransloadRequest { ConfirmationNote = "Đã sang đủ toàn bộ LPN." },
            _dispatcherId);
        Assert.False(mqttFailure.Success);
        Assert.Contains("Không thể bật MQTT streaming", mqttFailure.Message);
        Assert.Equal("DELAYED", (await _db.MasterTrips.FindAsync(_tripId))!.Status);

        _mqtt.PublishSucceeds = true;
        var confirmation = await _service.ConfirmTransloadAsync(
            _incidentId,
            new ConfirmTransloadRequest
            {
                ConfirmationNote = "Đã sang đủ toàn bộ LPN.",
                LpnIds = { _lpnId },
                SealNumber = "SEAL-RESCUE-001",
                TransferTemperature = -5m,
                Latitude = 10.7m,
                Longitude = 106.7m,
                EvidenceUrls = { "https://evidence.test/transload.jpg" }
            },
            _dispatcherId);

        Assert.True(confirmation.Success, confirmation.Message);
        Assert.Equal("IN_TRANSIT", (await _db.MasterTrips.FindAsync(_tripId))!.Status);
        Assert.Equal("TRANSLOAD_COMPLETED", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);
        Assert.Equal(new[] { "IOT-REPLACEMENT" }, _mqtt.StreamingDeviceCodes);
        Assert.Equal(_tripId, (await _db.Lpns.FindAsync(_lpnId))!.TripId);
        Assert.Equal(_tripId, (await _db.TransportOrders.FindAsync(_orderId))!.MasterTripId);
        var transloadJson = (await _db.IncidentReports.FindAsync(_incidentId))!.TransloadDetailsJson;
        var transload = JsonSerializer.Deserialize<TransloadRecord>(transloadJson!);
        Assert.Equal("SEAL-RESCUE-001", transload!.SealNumber);
        Assert.Equal(-5m, transload.TransferTemperature);
        Assert.Equal(new[] { _lpnId }, transload.LpnIds);
        Assert.Contains(await _db.IncidentEvidences.ToListAsync(), e => e.EvidenceType == "TRANSLOAD_EVIDENCE");
    }

    [Fact]
    public async Task ConfirmTransload_AfterIncidentClosedForDispatchedReplacement_KeepsIncidentResolved()
    {
        await SeedRescueTripAsync(replacementOnline: true);
        var dispatch = await _service.DispatchRescueAsync(
            _incidentId,
            new DispatchRescueRequest
            {
                ReplacementVehicleId = _replacementVehicleId,
                Note = "Đã điều xe thay thế."
            },
            _dispatcherId);
        Assert.True(dispatch.Success, dispatch.Message);

        var incident = (await _db.IncidentReports.FindAsync(_incidentId))!;
        incident.Status = "RESOLVED";
        incident.ResolvedAt = DateTime.UtcNow;
        incident.ResolvedBy = _dispatcherId;
        await _db.SaveChangesAsync();

        var confirmation = await _service.ConfirmTransloadAsync(
            _incidentId,
            new ConfirmTransloadRequest
            {
                ConfirmationNote = "Đã sang đủ hàng sau khi Incident được đóng.",
                LpnIds = { _lpnId }
            },
            _dispatcherId);

        Assert.True(confirmation.Success, confirmation.Message);
        Assert.Equal("IN_TRANSIT", (await _db.MasterTrips.FindAsync(_tripId))!.Status);
        Assert.Equal("RESOLVED", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);
        Assert.NotNull((await _db.IncidentReports.FindAsync(_incidentId))!.TransloadConfirmedAt);
    }

    [Fact]
    public async Task DispatchRescue_RejectsVehicleThatCannotArriveWithinRemainingSafeTime()
    {
        await SeedRescueTripAsync(replacementOnline: true);
        var warehouseId = Guid.NewGuid();
        _db.Warehouses.Add(new Warehouse
        {
            WarehouseId = warehouseId,
            WarehouseCode = "FAR-RESCUE",
            WarehouseName = "Far Rescue Base",
            WarehouseType = "HUB",
            Address = "11.7,107.7",
            MaxPallets = 100,
            Status = "ACTIVE"
        });
        (await _db.Vehicles.FindAsync(_replacementVehicleId))!.CurrentLocation = warehouseId.ToString();
        (await _db.IncidentReports.FindAsync(_incidentId))!.RemainingSafeTimeMinutes = 5;
        await _db.SaveChangesAsync();

        var result = await _service.DispatchRescueAsync(
            _incidentId,
            new DispatchRescueRequest { ReplacementVehicleId = _replacementVehicleId },
            _dispatcherId);

        Assert.False(result.Success);
        Assert.Contains("vượt thời gian an toàn còn lại", result.Message);
        Assert.Equal("REPORTED", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);
    }

    [Fact]
    public async Task BreachedIncident_AllowsWarehouseRescueDespiteZeroRemainingSafeTime()
    {
        await SeedRescueTripAsync(replacementOnline: true);
        var warehouseId = Guid.NewGuid();
        _db.Warehouses.Add(new Warehouse
        {
            WarehouseId = warehouseId,
            WarehouseCode = "BREACH-COLD",
            WarehouseName = "Breach Cold Storage",
            WarehouseType = "HUB",
            Address = "10.8,106.8",
            MaxPallets = 100,
            CurrentPallets = 0,
            Status = "ACTIVE",
            DefaultMinTemp = -20m,
            DefaultMaxTemp = 10m
        });
        (await _db.Vehicles.FindAsync(_replacementVehicleId))!.CurrentLocation = warehouseId.ToString();
        var incident = (await _db.IncidentReports.FindAsync(_incidentId))!;
        incident.DirectDeliveryLocked = true;
        incident.TemperatureThresholdBreached = true;
        incident.RemainingSafeTimeMinutes = 0;
        incident.Status = "RESCUE_PLANNING";
        incident.ContainmentConfirmedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var plan = await _service.GetRescuePlanAsync(_incidentId);

        Assert.True(plan.Success, plan.Message);
        Assert.Equal("WAREHOUSE_RESCUE", plan.Data!.RecommendedAction);
        Assert.True(plan.Data.DirectDeliveryLocked);
        Assert.Null(Assert.Single(plan.Data.Vehicles).CanArriveWithinSafeTime);

        var directDispatch = await _service.DispatchRescueAsync(
            _incidentId,
            new DispatchRescueRequest
            {
                ReplacementVehicleId = _replacementVehicleId,
                PlanType = IncidentRescuePlanType.DIRECT_RESCUE
            },
            _dispatcherId);

        Assert.False(directDispatch.Success);
        Assert.Contains("nhiệt độ đã vượt ngưỡng", directDispatch.Message);

        var warehouseDispatch = await _service.DispatchRescueAsync(
            _incidentId,
            new DispatchRescueRequest
            {
                ReplacementVehicleId = _replacementVehicleId,
                PlanType = IncidentRescuePlanType.WAREHOUSE_RESCUE,
                DestinationWarehouseId = warehouseId
            },
            _dispatcherId);

        Assert.True(warehouseDispatch.Success, warehouseDispatch.Message);
        Assert.Equal("RESCUE_DISPATCHED", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);
    }

    [Fact]
    public async Task NonBreakdownIncident_WithoutInternalOption_RecommendsManualEscalation()
    {
        await SeedRescueTripAsync(replacementOnline: true);
        var incident = (await _db.IncidentReports.FindAsync(_incidentId))!;
        incident.DirectDeliveryLocked = true;
        incident.TemperatureThresholdBreached = true;
        incident.RemainingSafeTimeMinutes = 0;
        incident.Status = "RESCUE_PLANNING";
        await _db.SaveChangesAsync();

        var result = await _service.GetRescuePlanAsync(_incidentId);

        Assert.True(result.Success, result.Message);
        Assert.Equal("MANUAL_ESCALATION", result.Data!.RecommendedAction);
        Assert.True(result.Data.RequiresManualEscalation);
    }

    [Fact]
    public async Task VehicleBreakdown_RejectsStorageFallbackAndRequiresExternalReeferToRouteWarehouse()
    {
        await SeedRescueTripAsync(replacementOnline: true);
        (await _db.IncidentReports.FindAsync(_incidentId))!.IncidentType = "VEHICLE_BREAKDOWN";
        await _db.SaveChangesAsync();

        var result = await _service.RecordFallbackAsync(
            _incidentId,
            new RecordRescueFallbackRequest
            {
                PlanType = IncidentRescuePlanType.INTERNAL_COLD_STORAGE,
                WarehouseId = Guid.NewGuid(),
                Note = "Attempt to use a storage fallback."
            },
            _dispatcherId);

        Assert.False(result.Success);
        Assert.Contains("external-reefer-dispatch", result.Message);
        Assert.Equal("REPORTED", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);
    }

    [Theory]
    [InlineData("VEHICLE_BREAKDOWN")]
    [InlineData("REEFER_BREAKDOWN")]
    public async Task VehicleOrReeferBreakdown_RentsExternalReefer_ThenInboundsBySealWithoutQc(
        string incidentType)
    {
        await SeedRescueTripAsync(replacementOnline: true);
        var routeId = Guid.NewGuid();
        var hanoiWarehouseId = Guid.NewGuid();
        _db.RouteMasters.Add(new RouteMaster
        {
            RouteId = routeId,
            RouteCode = "HCM-HN",
            OriginCity = "Hồ Chí Minh",
            DestCity = "Hà Nội",
            TransitTime = "36:00",
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow
        });
        _db.Warehouses.Add(new Warehouse
        {
            WarehouseId = hanoiWarehouseId,
            WarehouseCode = "HN-HUB",
            WarehouseName = "Kho lạnh Hà Nội",
            WarehouseType = "HUB",
            Address = "Hà Nội",
            MaxPallets = 100,
            CurrentPallets = 0,
            Status = "ACTIVE",
            DefaultMinTemp = -20m,
            DefaultMaxTemp = 10m
        });
        var trip = (await _db.MasterTrips.FindAsync(_tripId))!;
        trip.RouteId = routeId;
        (await _db.IncidentReports.FindAsync(_incidentId))!.IncidentType = incidentType;
        await _db.SaveChangesAsync();

        var options = await _service.GetRescuePlanAsync(_incidentId);

        Assert.True(options.Success, options.Message);
        Assert.Equal("EXTERNAL_REEFER_TO_ROUTE_WAREHOUSE", options.Data!.RecommendedAction);
        Assert.True(options.Data.RequiresExternalVehicleRental);
        Assert.Equal(hanoiWarehouseId, options.Data.RouteDestinationWarehouse!.WarehouseId);

        var forbiddenInternalDispatch = await _service.DispatchRescueAsync(
            _incidentId,
            new DispatchRescueRequest
            {
                ReplacementVehicleId = _replacementVehicleId,
                PlanType = IncidentRescuePlanType.WAREHOUSE_RESCUE,
                DestinationWarehouseId = hanoiWarehouseId
            },
            _dispatcherId);
        Assert.False(forbiddenInternalDispatch.Success);
        Assert.Contains("bắt buộc thuê xe lạnh ngoài", forbiddenInternalDispatch.Message);

        var externalDispatch = await _service.DispatchExternalReeferAsync(
            _incidentId,
            new DispatchExternalReeferRequest
            {
                RentalProvider = "Đối tác xe lạnh Bắc Nam",
                VehiclePlate = "51R-123.45",
                DriverName = "Nguyễn Văn Thuê",
                DriverPhone = "0909123456",
                DestinationWarehouseId = hanoiWarehouseId,
                AgreedTemperature = -5m,
                ExpectedWarehouseArrivalAt = DateTime.UtcNow.AddHours(36),
                SealNumber = "EXT-SEAL-001",
                LpnIds = { _lpnId },
                EvidenceUrls = { "https://evidence.test/external-handover.jpg" },
                Note = "Thuê xe lạnh ngoài chở thẳng về kho Hà Nội."
            },
            _dispatcherId);

        Assert.True(externalDispatch.Success, externalDispatch.Message);
        Assert.Equal("EXTERNAL_REEFER_IN_TRANSIT", externalDispatch.Data!.IncidentStatus);
        Assert.Equal("DELAYED", externalDispatch.Data.TripStatus);
        Assert.Equal("MAINTENANCE", (await _db.Vehicles.FindAsync(_brokenVehicleId))!.Status);

        var wrongSealArrival = await _service.InboundRouteWarehouseAsync(
            _incidentId,
            new InboundRouteWarehouseRequest
            {
                SealNumber = "WRONG-SEAL"
            },
            _dispatcherId);

        Assert.False(wrongSealArrival.Success);
        Assert.Contains("seal", wrongSealArrival.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("EXTERNAL_REEFER_IN_TRANSIT", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);

        var arrival = await _service.InboundRouteWarehouseAsync(
            _incidentId,
            new InboundRouteWarehouseRequest
            {
                SealNumber = "EXT-SEAL-001"
            },
            _dispatcherId);

        Assert.True(arrival.Success, arrival.Message);
        Assert.Equal("READY_FOR_REDISPATCH", arrival.Data!.IncidentStatus);
        var lpnAtWarehouse = (await _db.Lpns.FindAsync(_lpnId))!;
        Assert.Equal(hanoiWarehouseId, lpnAtWarehouse.WarehouseId);
        Assert.Equal(LpnState.IN_STOCK, lpnAtWarehouse.State);
        Assert.Null(lpnAtWarehouse.TripId);
        Assert.NotNull(lpnAtWarehouse.InboundTime);
        Assert.Equal("READY_FOR_ROUTING", (await _db.TransportOrders.FindAsync(_orderId))!.Status);
        Assert.Equal("RELAY_COMPLETED", (await _db.MasterTrips.FindAsync(_tripId))!.Status);
        var receipt = await _db.WarehouseReceipts.SingleAsync(r => r.ReceiptId == lpnAtWarehouse.ReceiptId);
        Assert.Equal("INCIDENT_RELAY", receipt.ReceiptType);
        Assert.True(receipt.ReceiptType.Length <= 20);
        Assert.Contains("EXT-SEAL-001", receipt.Note);
        Assert.Contains("bỏ qua QC", receipt.Note);
    }

    [Fact]
    public async Task VehicleBreakdown_ConfirmExternalVehicleOnly_ImmediatelyOpensWarehouseInboundBySeal()
    {
        await SeedRescueTripAsync(replacementOnline: true);
        var routeId = Guid.NewGuid();
        var hanoiWarehouseId = Guid.NewGuid();
        _db.RouteMasters.Add(new RouteMaster
        {
            RouteId = routeId,
            RouteCode = "HCM-HN-MINIMAL",
            OriginCity = "Hồ Chí Minh",
            DestCity = "Hà Nội",
            TransitTime = "36:00",
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow
        });
        _db.Warehouses.Add(new Warehouse
        {
            WarehouseId = hanoiWarehouseId,
            WarehouseCode = "HN-HUB-MINIMAL",
            WarehouseName = "Kho Hà Nội",
            WarehouseType = "HUB",
            Address = "Hà Nội",
            MaxPallets = 100,
            Status = "ACTIVE",
            DefaultMinTemp = -20m,
            DefaultMaxTemp = 10m
        });
        var trip = (await _db.MasterTrips.FindAsync(_tripId))!;
        trip.RouteId = routeId;
        (await _db.IncidentReports.FindAsync(_incidentId))!.IncidentType = "VEHICLE_BREAKDOWN";
        await _db.SaveChangesAsync();

        var confirmation = await _service.DispatchExternalReeferAsync(
            _incidentId,
            new DispatchExternalReeferRequest { ExternalVehicleConfirmed = true },
            _dispatcherId);

        Assert.True(confirmation.Success, confirmation.Message);
        Assert.Equal("EXTERNAL_REEFER_IN_TRANSIT", confirmation.Data!.IncidentStatus);
        Assert.True(confirmation.Data.WarehouseInboundReady);
        Assert.Equal("INBOUND_RESCUE_BY_SEAL", confirmation.Data.RequiredWarehouseAction);
        Assert.Equal(hanoiWarehouseId, confirmation.Data.DestinationWarehouseId);

        var inbound = await _service.InboundRouteWarehouseAsync(
            _incidentId,
            new InboundRouteWarehouseRequest { SealNumber = "WAREHOUSE-SEAL-001" },
            _dispatcherId);

        Assert.True(inbound.Success, inbound.Message);
        Assert.Equal("READY_FOR_REDISPATCH", inbound.Data!.IncidentStatus);
        Assert.False(inbound.Data.WarehouseInboundReady);
        Assert.Equal("CREATE_REDISPATCH_TRIP", inbound.Data.RequiredWarehouseAction);
        var incident = await _db.IncidentReports.FindAsync(_incidentId);
        Assert.Contains("WAREHOUSE-SEAL-001", incident!.RescuePlanDetails);
    }

    [Fact]
    public async Task NoShowReturn_SelectedWarehouse_ReusesIncidentInboundBySealWithoutQc()
    {
        var warehouseId = Guid.NewGuid();
        var otherWarehouseId = Guid.NewGuid();
        var warehouseWorkerId = Guid.NewGuid();
        var otherWarehouseWorkerId = Guid.NewGuid();
        var driverRole = new Role { RoleId = Guid.NewGuid(), RoleName = "Driver" };
        var warehouseRole = new Role { RoleId = Guid.NewGuid(), RoleName = "WarehouseWorker" };
        var vehicle = BuildVehicle(_brokenVehicleId, "51C-NOSHOW", "ON_TRIP", 5000m, 30m, -20m, 10m);
        var warehouse = new Warehouse
        {
            WarehouseId = warehouseId,
            WarehouseCode = "PMH-COLD",
            WarehouseName = "Kho lạnh Phú Mỹ Hưng",
            WarehouseType = "COLD_STORAGE",
            Address = "10.732537,106.714447",
            MaxPallets = 100,
            Status = "ACTIVE"
        };
        var otherWarehouse = new Warehouse
        {
            WarehouseId = otherWarehouseId,
            WarehouseCode = "OTHER-COLD",
            WarehouseName = "Kho lạnh khác",
            WarehouseType = "COLD_STORAGE",
            Address = "10.800000,106.700000",
            MaxPallets = 100,
            Status = "ACTIVE"
        };
        var driverUser = new User
        {
            UserId = _driverUserId,
            Username = "noshow_driver",
            FullName = "No Show Driver",
            Role = driverRole,
            RoleId = driverRole.RoleId,
            Status = "ACTIVE"
        };
        var warehouseWorker = new User
        {
            UserId = warehouseWorkerId,
            Username = "pmh_warehouse",
            FullName = "PMH Warehouse Worker",
            Role = warehouseRole,
            RoleId = warehouseRole.RoleId,
            WarehouseId = warehouseId,
            Status = "ACTIVE"
        };
        var otherWarehouseWorker = new User
        {
            UserId = otherWarehouseWorkerId,
            Username = "other_warehouse",
            FullName = "Other Warehouse Worker",
            Role = warehouseRole,
            RoleId = warehouseRole.RoleId,
            WarehouseId = otherWarehouseId,
            Status = "ACTIVE"
        };
        var driver = new Driver
        {
            DriverId = _driverId,
            UserId = _driverUserId,
            FullName = "No Show Driver",
            IdentityNumber = "DRV-NOSHOW",
            PhoneNumber = "0900000000",
            DateOfBirth = new DateOnly(1990, 1, 1),
            JoinDate = new DateOnly(2024, 1, 1),
            Status = "ON_TRIP"
        };
        var trip = new MasterTrip
        {
            TripId = _tripId,
            VehicleId = vehicle.VehicleId,
            OriginLocationId = Guid.NewGuid(),
            DestinationLocationId = Guid.NewGuid(),
            SealNumber = "RETURN-SEAL-001",
            TargetTemperature = -5m,
            PlannedStartTime = DateTime.UtcNow.AddHours(-4),
            PlannedEndTime = DateTime.UtcNow,
            Status = "IN_TRANSIT"
        };
        var order = new TransportOrder
        {
            OrderId = _orderId,
            TrackingCode = "CT-HCM-NOSHOW",
            ItemName = "Frozen cargo",
            Category = "FROZEN",
            Quantity = 10,
            PackingType = "PALLET",
            TempCondition = "-5",
            Status = "DELIVERY_FAILED_NOSHOW",
            MasterTripId = trip.TripId
        };
        var lpn = new Lpn
        {
            LpnId = _lpnId,
            LpnCode = "LPN-CT-HCM-NOSHOW",
            OrderId = order.OrderId,
            ReceiptId = Guid.NewGuid(),
            TripId = trip.TripId,
            RouteId = Guid.NewGuid(),
            Quantity = 10,
            State = LpnState.SHIPPING
        };

        _db.Roles.AddRange(driverRole, warehouseRole);
        _db.Users.AddRange(driverUser, warehouseWorker, otherWarehouseWorker);
        _db.Drivers.Add(driver);
        _db.Vehicles.Add(vehicle);
        _db.Warehouses.AddRange(warehouse, otherWarehouse);
        _db.MasterTrips.Add(trip);
        _db.TripDrivers.Add(new TripDriver
        {
            TripDriverId = Guid.NewGuid(),
            TripId = trip.TripId,
            DriverId = driver.DriverId,
            DriverRole = "PRIMARY"
        });
        _db.TransportOrders.Add(order);
        _db.Lpns.Add(lpn);
        _db.DeliveryEpods.Add(new DeliveryEpod
        {
            EpodId = Guid.NewGuid(),
            OrderId = order.OrderId,
            HandoverConfirmedAt = DateTime.UtcNow,
            Status = "NO_SHOW",
            PaymentStatus = "SKIPPED_NO_SHOW",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var closeShift = new CloseShiftCommandHandler(
            _db,
            new FakeDeliveryEventService(),
            new FakeDriverAvailabilityService());
        await Assert.ThrowsAsync<ColdChainX.Shared.Exceptions.ValidationException>(() =>
            closeShift.Handle(new CloseShiftCommand
            {
                TripId = trip.TripId,
                WarehouseId = warehouse.WarehouseId,
                UserId = driverUser.UserId
            }, CancellationToken.None));

        Assert.Empty(_db.IncidentReports.Where(item =>
            item.IncidentType == IncidentType.CUSTOMER_NO_SHOW_RETURN.ToString()));
        lpn.State = LpnState.RETURN_PENDING;
        await _db.SaveChangesAsync();

        var closeResult = await closeShift.Handle(new CloseShiftCommand
        {
            TripId = trip.TripId,
            WarehouseId = warehouse.WarehouseId,
            UserId = driverUser.UserId
        }, CancellationToken.None);

        Assert.True(closeResult.Success);
        var incident = await _db.IncidentReports.SingleAsync(item =>
            item.IncidentType == IncidentType.CUSTOMER_NO_SHOW_RETURN.ToString());
        Assert.Equal("EXTERNAL_REEFER_IN_TRANSIT", incident.Status);
        var plan = JsonSerializer.Deserialize<ExternalReeferPlanRecord>(incident.RescuePlanDetails!);
        Assert.NotNull(plan);
        Assert.Equal(warehouse.WarehouseId, plan.DestinationWarehouseId);
        Assert.Contains(lpn.LpnId, plan.LpnIds);

        var wrongWarehouseInbound = await _service.InboundRouteWarehouseAsync(
            incident.IncidentId,
            new InboundRouteWarehouseRequest { SealNumber = "RETURN-SEAL-001" },
            otherWarehouseWorker.UserId);

        Assert.False(wrongWarehouseInbound.Success);
        Assert.Equal(403, wrongWarehouseInbound.StatusCode);
        Assert.Equal(LpnState.RETURN_PENDING, lpn.State);
        Assert.Empty(_db.WarehouseReceipts);

        var wrongSealInbound = await _service.InboundRouteWarehouseAsync(
            incident.IncidentId,
            new InboundRouteWarehouseRequest { SealNumber = "WRONG-SEAL" },
            warehouseWorker.UserId);

        Assert.False(wrongSealInbound.Success);
        Assert.Equal(LpnState.RETURN_PENDING, lpn.State);
        Assert.Empty(_db.WarehouseReceipts);

        var inbound = await _service.InboundRouteWarehouseAsync(
            incident.IncidentId,
            new InboundRouteWarehouseRequest { SealNumber = "RETURN-SEAL-001" },
            warehouseWorker.UserId);

        Assert.True(inbound.Success, inbound.Message);
        Assert.Equal("READY_FOR_REDISPATCH", inbound.Data!.IncidentStatus);
        Assert.Equal("CREATE_REDISPATCH_TRIP", inbound.Data.RequiredWarehouseAction);
        Assert.Equal(LpnState.IN_STOCK, lpn.State);
        Assert.Equal(warehouse.WarehouseId, lpn.WarehouseId);
        Assert.Null(lpn.TripId);
        Assert.Null(lpn.RouteId);
        Assert.Equal("READY_FOR_ROUTING", order.Status);
        Assert.Null(order.MasterTripId);
        Assert.Equal("COMPLETED", trip.Status);

        var receipt = await _db.WarehouseReceipts.SingleAsync(item => item.OrderId == order.OrderId);
        Assert.Equal("NO_SHOW_RETURN", receipt.ReceiptType);
        Assert.True(receipt.ReceiptType.Length <= 20);
        Assert.Contains("bỏ qua QC", receipt.Note);
        Assert.Contains("RETURN-SEAL-001", receipt.Note);
        Assert.Empty(_db.InboundQcPackageLines);
    }

    [Fact]
    public async Task DispatchRescue_CustomerEtaNotification_ContainsDatesWithoutTimes()
    {
        await SeedRescueTripAsync(replacementOnline: false);

        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(),
            CompanyName = "Incident ETA Customer",
            TaxCode = $"INC-ETA-{Guid.NewGuid():N}",
            Email = "incident-eta@example.com",
            Status = "ACTIVE"
        };
        var customerUser = new User
        {
            UserId = Guid.NewGuid(),
            Username = "incident-eta-customer",
            Email = customer.Email,
            FullName = "Incident ETA Customer",
            Status = "ACTIVE"
        };
        var destination = new Location
        {
            LocationId = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Customer = customer,
            Address = "Incident delivery point",
            Latitude = 10.8m,
            Longitude = 106.8m,
            Status = "ACTIVE"
        };
        var trip = (await _db.MasterTrips.FindAsync(_tripId))!;
        var order = (await _db.TransportOrders.FindAsync(_orderId))!;
        var plannedArrival = DateTime.UtcNow.AddHours(2);
        trip.DestinationLocationId = destination.LocationId;
        order.CustomerId = customer.CustomerId;
        order.Customer = customer;
        order.DestLocation = destination.LocationId;
        order.DestLocationNavigation = destination;

        _db.AddRange(
            customer,
            customerUser,
            destination,
            new Messagetype
            {
                TypeId = Guid.NewGuid(),
                TypeName = "INCIDENT"
            },
            new TripStop
            {
                StopId = Guid.NewGuid(),
                TripId = _tripId,
                Trip = trip,
                LocationId = destination.LocationId,
                Location = destination,
                StopSequence = 1,
                StopType = "DELIVERY",
                PlannedArrivalTime = plannedArrival,
                PlannedDepartureTime = plannedArrival.AddMinutes(30),
                Status = "PLANNED",
                CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var result = await _service.DispatchRescueAsync(
            _incidentId,
            new DispatchRescueRequest
            {
                ReplacementVehicleId = _replacementVehicleId,
                TransloadMinutes = 30
            },
            _dispatcherId);

        Assert.True(result.Success, result.Message);
        var etaChange = Assert.Single(result.Data!.UpdatedStops);
        var notification = await _db.Notifications
            .SingleAsync(item => item.TemplateId == "INCIDENT_TRIP_DELAYED");
        using var payload = JsonDocument.Parse(notification.Params);
        var oldEta = payload.RootElement.GetProperty("old_eta").GetString();
        var newEta = payload.RootElement.GetProperty("new_eta").GetString();

        Assert.Equal(
            etaChange.OldEta.AddHours(7).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            oldEta);
        Assert.Equal(
            etaChange.NewEta.AddHours(7).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            newEta);
        Assert.DoesNotContain(":", oldEta);
        Assert.DoesNotContain(":", newEta);

        var template = await _db.NotificationTemplates
            .SingleAsync(item => item.TemplateId == "INCIDENT_TRIP_DELAYED");
        Assert.Contains("Ngày giao dự kiến mới", template.BodyTemplate);
    }

    [Fact]
    public async Task ContinueTrip_ForNoRescueIncident_KeepsOriginalVehicle()
    {
        await SeedNoRescueTripAsync();

        var result = await _service.ContinueTripAsync(
            _incidentId,
            new ContinueTripAfterIncidentRequest { HandlingNote = "Đã siết lại dây điện và kiểm tra nhiệt độ." },
            _driverUserId);

        Assert.True(result.Success, result.Message);
        var trip = await _db.MasterTrips.FindAsync(_tripId);
        var incident = await _db.IncidentReports.FindAsync(_incidentId);
        Assert.Equal("IN_TRANSIT", trip!.Status);
        Assert.Equal(_brokenVehicleId, trip.VehicleId);
        Assert.Equal("CONTINUED", incident!.Status);
        Assert.Equal(_driverUserId, incident.HandledBy);
    }

    [Fact]
    public async Task ContinueTrip_WithoutHandlingNote_UsesDefaultNote()
    {
        await SeedNoRescueTripAsync();

        var result = await _service.ContinueTripAsync(
            _incidentId,
            new ContinueTripAfterIncidentRequest(),
            _driverUserId);

        Assert.True(result.Success, result.Message);
        var incident = await _db.IncidentReports.FindAsync(_incidentId);
        Assert.Equal("CONTINUED", incident!.Status);
        Assert.Equal(
            "Tài xế xác nhận đã xử lý sự cố tại chỗ và tiếp tục hành trình.",
            incident.HandlingNote);
    }

    [Fact]
    public async Task ContinueTrip_RejectsDriverNotAssignedToTrip()
    {
        await SeedNoRescueTripAsync();
        var otherUserId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            UserId = otherUserId,
            Username = "other-driver",
            PasswordHash = "hash",
            FullName = "Other Driver"
        });
        _db.Drivers.Add(new Driver
        {
            DriverId = Guid.NewGuid(),
            UserId = otherUserId,
            FullName = "Other Driver",
            IdentityNumber = "109876543210",
            PhoneNumber = "0911111111",
            DateOfBirth = new DateOnly(1991, 1, 1),
            JoinDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = "ACTIVE"
        });
        await _db.SaveChangesAsync();

        var result = await _service.ContinueTripAsync(
            _incidentId,
            new ContinueTripAfterIncidentRequest { HandlingNote = "Đã xử lý xong." },
            otherUserId);

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("không phải tài xế được phân công", result.Message);
        Assert.Equal("DELAYED", (await _db.MasterTrips.FindAsync(_tripId))!.Status);
        Assert.Equal("REPORTED", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);
    }

    private async Task SeedRescueTripAsync(bool replacementOnline)
    {
        var brokenVehicle = BuildVehicle(
            _brokenVehicleId,
            "OLD-TRUCK",
            "ONTRIP",
            5000m,
            30m,
            -20m,
            10m,
            new IotDevice
            {
                DeviceId = Guid.NewGuid(),
                DeviceCode = "IOT-OLD",
                IsOnline = false
            });
        var replacement = BuildVehicle(
            _replacementVehicleId,
            "NEW-TRUCK",
            "ACTIVE",
            3000m,
            20m,
            -20m,
            10m,
            new IotDevice
            {
                DeviceId = _replacementDeviceId,
                DeviceCode = "IOT-REPLACEMENT",
                IsOnline = replacementOnline
            });

        _db.Users.Add(new User
        {
            UserId = _dispatcherId,
            Username = "dispatcher",
            PasswordHash = "hash",
            FullName = "Dispatcher"
        });
        _db.Vehicles.AddRange(brokenVehicle, replacement);
        _db.MasterTrips.Add(new MasterTrip
        {
            TripId = _tripId,
            VehicleId = _brokenVehicleId,
            OriginLocationId = Guid.NewGuid(),
            DestinationLocationId = Guid.NewGuid(),
            TargetTemperature = -5m,
            PlannedStartTime = DateTime.UtcNow.AddHours(-1),
            PlannedEndTime = DateTime.UtcNow.AddHours(3),
            Status = "IN_TRANSIT"
        });
        _db.IncidentReports.Add(new IncidentReport
        {
            IncidentId = _incidentId,
            TripId = _tripId,
            IncidentType = "ACCIDENT",
            Severity = "HIGH",
            Description = "Xe hỏng giữa đường.",
            RequiresRescue = true,
            DriverPaidAmount = 0m,
            ExpenseStatus = "NOT_REQUIRED",
            Status = "REPORTED",
            ReportedBy = _dispatcherId,
            ReportedAt = DateTime.UtcNow,
            CurrentLatitude = 10.7m,
            CurrentLongitude = 106.7m
        });
        _db.TransportOrders.Add(new TransportOrder
        {
            OrderId = _orderId,
            TrackingCode = "TRACK-INCIDENT",
            ItemName = "Frozen cargo",
            Category = "FROZEN",
            Quantity = 10,
            PackingType = "PALLET",
            TempCondition = "-5",
            Status = "SHIPPING",
            MasterTripId = _tripId
        });
        _db.Lpns.Add(new Lpn
        {
            LpnId = _lpnId,
            LpnCode = "LPN-INCIDENT",
            OrderId = _orderId,
            ReceiptId = Guid.NewGuid(),
            TripId = _tripId,
            Quantity = 10,
            ActualWeightKg = 1200m,
            ActualCbm = 8m,
            State = LpnState.SHIPPING,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedNoRescueTripAsync()
    {
        _db.Users.Add(new User
        {
            UserId = _driverUserId,
            Username = "driver",
            PasswordHash = "hash",
            FullName = "Driver"
        });
        _db.Drivers.Add(new Driver
        {
            DriverId = _driverId,
            UserId = _driverUserId,
            FullName = "Driver",
            IdentityNumber = "012345678901",
            PhoneNumber = "0900000000",
            DateOfBirth = new DateOnly(1990, 1, 1),
            JoinDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = "ACTIVE"
        });
        _db.Vehicles.Add(BuildVehicle(
            _brokenVehicleId,
            "ORIGINAL",
            "ONTRIP",
            3000m,
            20m,
            -20m,
            10m));
        _db.MasterTrips.Add(new MasterTrip
        {
            TripId = _tripId,
            VehicleId = _brokenVehicleId,
            OriginLocationId = Guid.NewGuid(),
            DestinationLocationId = Guid.NewGuid(),
            TargetTemperature = -5m,
            PlannedStartTime = DateTime.UtcNow.AddHours(-1),
            PlannedEndTime = DateTime.UtcNow.AddHours(2),
            Status = "DELAYED"
        });
        _db.TripDrivers.Add(new TripDriver
        {
            TripDriverId = Guid.NewGuid(),
            TripId = _tripId,
            DriverId = _driverId,
            DriverRole = "PRIMARY",
            AssignedDurationHours = 3m,
            CreatedAt = DateTime.UtcNow
        });
        _db.IncidentReports.Add(new IncidentReport
        {
            IncidentId = _incidentId,
            TripId = _tripId,
            IncidentType = "DELAY",
            Severity = "LOW",
            Description = "Lỗi điện nhẹ.",
            RequiresRescue = false,
            DriverPaidAmount = 0m,
            ExpenseStatus = "NOT_REQUIRED",
            Status = "REPORTED",
            ReportedBy = _driverUserId,
            ReportedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private static Vehicle BuildVehicle(
        Guid id,
        string plate,
        string status,
        decimal maxWeight,
        decimal maxCbm,
        decimal minTemp,
        decimal maxTemp,
        params IotDevice[] devices)
    {
        var vehicle = new Vehicle
        {
            VehicleId = id,
            TruckPlate = plate,
            VehicleType = "REEFER",
            Status = status,
            MaxWeight = maxWeight,
            MaxCbm = maxCbm,
            MinTemp = minTemp,
            MaxTemp = maxTemp
        };
        foreach (var device in devices)
        {
            device.VehicleId = id;
            vehicle.IotDevices.Add(device);
        }

        return vehicle;
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private sealed class FakeGoongMapService : IGoongMapService
    {
        public Task<GoongOptimizedRouteResult> GetOptimizedRouteAsync(
            string origin,
            string destination,
            string? waypoints,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GoongOptimizedRouteResult
            {
                TotalDistanceMeters = 1000,
                TotalDurationSeconds = 120
            });
        }
    }

    private sealed class FakeMqttPublisher : IMqttCommandPublisher
    {
        public List<string> StreamingDeviceCodes { get; } = new();
        public bool PublishSucceeds { get; set; } = true;

        public Task ActivateSirenAsync(
            string deviceCode,
            object reason,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> StartStreamingAsync(
            string deviceCode,
            CancellationToken cancellationToken)
        {
            if (PublishSucceeds)
                StreamingDeviceCodes.Add(deviceCode);
            return Task.FromResult(PublishSucceeds);
        }

        public Task<bool> StopStreamingAsync(
            string deviceCode,
            CancellationToken cancellationToken)
        {
            if (PublishSucceeds)
                StreamingDeviceCodes.Remove(deviceCode);
            return Task.FromResult(PublishSucceeds);
        }
    }

    private sealed class FakeDeliveryEventService : IDeliveryEventService
    {
        public Task NotifyHandoverPartialReturnAsync(
            Guid orderId,
            string trackingCode,
            Guid epodId,
            int rejectedLpnCount,
            int totalLpnCount,
            string orderStatus,
            string handoverPdfUrl,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyCodPaymentConfirmedAsync(
            Guid orderId,
            string trackingCode,
            Guid epodId,
            decimal amountPaid,
            string paymentMethod,
            string orderStatus,
            string epodPdfUrl,
            string? receiverName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyTripCompletedAsync(
            Guid tripId,
            string tripCode,
            DateTime completedAt,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeDriverAvailabilityService : IDriverAvailabilityService
    {
        public Task<DriverAvailability> CheckAsync(Guid driverId, decimal additionalHours, DateOnly day)
            => Task.FromResult(new DriverAvailability { DriverId = driverId, CanAssign = true });

        public Task RecordWorkAsync(Guid driverId, Guid tripId, decimal hours, DateOnly day)
            => Task.CompletedTask;

        public Task ReconcileStatusAsync(Driver driver, Guid? excludedTripId = null)
            => Task.CompletedTask;
    }

    private sealed class FakeNotificationHubContext : IHubContext<NotificationHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients();
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class FakeHubClients : IHubClients
    {
        public IClientProxy All => new FakeClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy();
        public IClientProxy Client(string connectionId) => new FakeClientProxy();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeClientProxy();
        public IClientProxy Group(string groupName) => new FakeClientProxy();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeClientProxy();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy();
        public IClientProxy User(string userId) => new FakeClientProxy();
        public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeClientProxy();
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
