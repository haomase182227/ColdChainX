using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.DTOs.Incident;
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
            new ConfirmTransloadRequest { ConfirmationNote = "Đã sang đủ toàn bộ LPN." },
            _dispatcherId);

        Assert.True(confirmation.Success, confirmation.Message);
        Assert.Equal("IN_TRANSIT", (await _db.MasterTrips.FindAsync(_tripId))!.Status);
        Assert.Equal("TRANSLOAD_COMPLETED", (await _db.IncidentReports.FindAsync(_incidentId))!.Status);
        Assert.Equal(new[] { "IOT-REPLACEMENT" }, _mqtt.StreamingDeviceCodes);
        Assert.Equal(_tripId, (await _db.Lpns.FindAsync(_lpnId))!.TripId);
        Assert.Equal(_tripId, (await _db.TransportOrders.FindAsync(_orderId))!.MasterTripId);
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
            IncidentType = "VEHICLE_BREAKDOWN",
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
