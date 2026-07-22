using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdChainX.UnitTests;

public class ManualDispatchFlowTests
{
    [Fact]
    public async Task ManualDispatch_CreatesTripAndLinksSchedule_WhenRequestIsValid()
    {
        await using var fixture = await CreateFixtureAsync();

        var result = await fixture.Service.ManualDispatchAsync(fixture.Request);

        var trip = await fixture.Db.MasterTrips.SingleAsync(t => t.TripId == result.TripId);
        var lpn = await fixture.Db.Lpns.SingleAsync(l => l.LpnId == fixture.Lpn.LpnId);
        var order = await fixture.Db.TransportOrders.SingleAsync(o => o.OrderId == fixture.Order.OrderId);
        var vehicle = await fixture.Db.Vehicles.SingleAsync(v => v.VehicleId == fixture.Vehicle.VehicleId);

        Assert.Equal(fixture.Route.RouteId, trip.RouteId);
        Assert.Equal(fixture.Schedule.ScheduleId, trip.ScheduleId);
        Assert.Equal(fixture.Request.PlannedStartTime.Date, trip.DepartureDate);
        Assert.Equal("PLANNED", trip.Status);
        Assert.Equal(LpnState.ALLOCATED, lpn.State);
        Assert.Equal(trip.TripId, lpn.TripId);
        Assert.Equal(trip.TripId, order.MasterTripId);
        Assert.Equal("LOADING", order.Status);
        Assert.Equal("PLANNING", vehicle.Status);
    }

    [Fact]
    public async Task ManualDispatch_AllowsInactiveScheduleWithExistingLpn()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Schedule.Status = "INACTIVE";
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ManualDispatchAsync(fixture.Request);

        var trip = await fixture.Db.MasterTrips.SingleAsync(t => t.TripId == result.TripId);
        Assert.Equal(fixture.Schedule.ScheduleId, trip.ScheduleId);
    }

    [Fact]
    public async Task ManualDispatch_RejectsLpnFromAnotherSchedule()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Order.ScheduleId = Guid.NewGuid();
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ManualDispatchAsync(fixture.Request));

        Assert.Contains("must belong to orders", error.Message);
        Assert.Empty(fixture.Db.MasterTrips);
    }

    [Fact]
    public async Task ManualDispatch_RejectsLpnThatCannotFitInsideVehicle()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Lpn.LengthCm = 1_000m;
        fixture.Lpn.WidthCm = 100m;
        fixture.Lpn.HeightCm = 100m;
        fixture.Lpn.ActualCbm = 10m;
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ManualDispatchAsync(fixture.Request));

        Assert.Contains("không lọt thùng xe", error.Message);
        Assert.Empty(fixture.Db.MasterTrips);
    }

    [Fact]
    public async Task ManualDispatch_RejectsLoadAboveEightyPercentCapacity()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Vehicle.MaxCbm = 1m;
        fixture.Vehicle.InnerLengthCm = 100m;
        fixture.Vehicle.InnerWidthCm = 100m;
        fixture.Vehicle.InnerHeightCm = 100m;
        fixture.Lpn.LengthCm = 100m;
        fixture.Lpn.WidthCm = 100m;
        fixture.Lpn.HeightCm = 90m;
        fixture.Lpn.ActualCbm = 0.90m;
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ManualDispatchAsync(fixture.Request));

        Assert.Contains("80%", error.Message);
        Assert.Empty(fixture.Db.MasterTrips);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"manual-dispatch-{Guid.NewGuid()}")
            .Options;
        var db = new ApplicationDbContext(options);

        var route = new RouteMaster
        {
            RouteId = Guid.NewGuid(),
            RouteCode = "HCM-HN",
            OriginCity = "HCM",
            DestCity = "Ha Noi",
            TransitTime = "24h",
            Status = "ACTIVE"
        };
        var schedule = new RouteSchedule
        {
            ScheduleId = Guid.NewGuid(),
            RouteId = route.RouteId,
            Route = route,
            ScheduleName = "Morning schedule",
            DepartureDate = DateTime.Today.AddDays(1),
            DepartureTime = new TimeSpan(8, 0, 0),
            CutOffTime = new TimeSpan(17, 0, 0),
            Status = "ACTIVE"
        };
        var origin = new Location
        {
            LocationId = Guid.NewGuid(),
            Address = "HCM warehouse",
            Latitude = 10.80m,
            Longitude = 106.70m,
            Status = "ACTIVE"
        };
        var destination = new Location
        {
            LocationId = Guid.NewGuid(),
            Address = "Ha Noi customer",
            Latitude = 21.03m,
            Longitude = 105.85m,
            Status = "ACTIVE"
        };
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(),
            TrackingCode = "TEST-MANUAL-001",
            ItemName = "Frozen cargo",
            Category = "FROZEN_FOOD",
            Quantity = 1,
            PackingType = "CARTON",
            TempCondition = "FROZEN -18C",
            Status = "IN_STOCK",
            ScheduleId = schedule.ScheduleId,
            Schedule = schedule,
            PickupLocation = origin.LocationId,
            DestLocation = destination.LocationId,
            DestLocationNavigation = destination
        };
        var warehouseId = Guid.NewGuid();
        var receipt = new WarehouseReceipt
        {
            ReceiptId = Guid.NewGuid(),
            ReceiptCode = "REC-TEST-001",
            OrderId = order.OrderId,
            Order = order,
            WarehouseId = warehouseId,
            ReceiptType = "INBOUND",
            DelivererName = "Test",
            ReceiverId = Guid.NewGuid()
        };
        var lpn = new Lpn
        {
            LpnId = Guid.NewGuid(),
            LpnCode = "LPN-TEST-001",
            OrderId = order.OrderId,
            Order = order,
            ReceiptId = receipt.ReceiptId,
            Receipt = receipt,
            WarehouseId = warehouseId,
            Quantity = 1,
            ActualWeightKg = 100m,
            ActualCbm = 1m,
            LengthCm = 100m,
            WidthCm = 100m,
            HeightCm = 100m,
            State = LpnState.IN_STOCK,
            CreatedAt = DateTime.UtcNow
        };
        var vehicle = new Vehicle
        {
            VehicleId = Guid.NewGuid(),
            TruckPlate = "51C-TEST",
            VehicleType = "REEFER_TRUCK",
            MaxWeight = 5_000m,
            MaxCbm = 30m,
            InnerLengthCm = 950m,
            InnerWidthCm = 250m,
            InnerHeightCm = 240m,
            MinTemp = -25m,
            MaxTemp = 10m,
            Status = "ACTIVE"
        };
        var driver = new Driver
        {
            DriverId = Guid.NewGuid(),
            FullName = "Test Driver",
            IdentityNumber = "TEST-ID-001",
            PhoneNumber = "0900000000",
            DateOfBirth = new DateOnly(1990, 1, 1),
            JoinDate = new DateOnly(2025, 1, 1),
            Status = "ACTIVE"
        };
        driver.DriverLicenses.Add(new DriverLicense
        {
            LicenseId = Guid.NewGuid(),
            DriverId = driver.DriverId,
            Driver = driver,
            LicenseNumber = "TEST-LICENSE-001",
            LicenseClass = "C",
            IssueDate = new DateOnly(2025, 1, 1),
            ExpiryDate = new DateOnly(2035, 1, 1),
            Status = "ACTIVE"
        });

        db.AddRange(route, schedule, origin, destination, order, receipt, lpn, vehicle, driver);
        await db.SaveChangesAsync();

        var availability = new DriverAvailabilityService(db);
        var service = new DispatchService(
            db,
            null!,
            new FakeLocationService(),
            null!,
            null!,
            null!,
            availability,
            null!,
            NullLogger<DispatchService>.Instance);

        var start = DateTime.UtcNow.AddDays(1);
        var request = new ManualDispatchRequest
        {
            ScheduleId = schedule.ScheduleId,
            LpnIds = new List<Guid> { lpn.LpnId },
            VehicleId = vehicle.VehicleId,
            DriverIds = new List<Guid> { driver.DriverId },
            OriginWarehouseLocationId = origin.LocationId,
            PlannedStartTime = start,
            PlannedEndTime = start.AddHours(5)
        };

        return new Fixture(db, service, request, route, schedule, order, lpn, vehicle);
    }

    private sealed record Fixture(
        ApplicationDbContext Db,
        DispatchService Service,
        ManualDispatchRequest Request,
        RouteMaster Route,
        RouteSchedule Schedule,
        TransportOrder Order,
        Lpn Lpn,
        Vehicle Vehicle) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeLocationService : ILocationService
    {
        public Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText)
            => Task.FromResult((10.80m, 106.70m));

        public Task<decimal> GetDistanceKmAsync(
            decimal originLat,
            decimal originLon,
            decimal destinationLat,
            decimal destinationLon)
            => Task.FromResult(10m);

        public Task<GoongDirectionsResult> GetDirectionsAsync(
            List<(decimal Lat, decimal Lon, string Address)> waypoints)
            => Task.FromResult(new GoongDirectionsResult
            {
                TotalDistanceKm = 10m,
                TotalDurationSeconds = 3_600,
                OverviewPolyline = "test",
                Legs = new List<GoongLeg>()
            });
    }
}
