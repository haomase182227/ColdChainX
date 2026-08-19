using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Application.Features.Delivery.Commands;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ColdChainX.UnitTests;

public class ConfirmHandoverRecipientTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly Guid _tripId = Guid.NewGuid();
    private readonly Guid _stopId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();

    public ConfirmHandoverRecipientTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new ApplicationDbContext(options);
        SeedHandoverData();
    }

    [Fact]
    public async Task ConfirmHandover_WithoutReceiverConfirmation_ShouldReject()
    {
        var handler = CreateHandler();
        var command = CreateCommand(isReceiverConfirmed: false);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => handler.Handle(command, CancellationToken.None));

        Assert.Contains("displayed name and phone", exception.Message);
        Assert.Empty(_db.DeliveryEpods);
    }

    [Fact]
    public async Task ConfirmHandover_WithoutHandoverPhoto_ShouldReject()
    {
        var handler = CreateHandler();
        var command = CreateCommand(includeHandoverPhoto: false);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => handler.Handle(command, CancellationToken.None));

        Assert.Contains("Handover photo is required", exception.Message);
        Assert.Empty(_db.DeliveryEpods);
    }

    [Fact]
    public async Task ConfirmHandover_WithSignatureAndPhoto_ShouldCreateEpod()
    {
        var handler = CreateHandler();
        var command = CreateCommand(includeHandoverPhoto: true);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Success);
        var epod = await _db.DeliveryEpods.SingleAsync();
        Assert.Equal("Nguyen Van A", epod.ReceiverName);
        Assert.Equal("0901234567", epod.ReceiverPhone);
        Assert.True(epod.ReceiverConfirmed);
        Assert.Equal("DELIVERED", (await _db.TransportOrders.FindAsync(_orderId))!.Status);
        Assert.All(await _db.Lpns.Where(l => l.OrderId == _orderId).ToListAsync(),
            lpn => Assert.Equal(LpnState.DELIVERED, lpn.State));
    }

    private ConfirmHandoverCommandHandler CreateHandler()
    {
        return new ConfirmHandoverCommandHandler(
            _db,
            new FakeFileService(),
            new FakePdfGeneratorService(),
            null!);
    }

    private ConfirmHandoverCommand CreateCommand(
        bool isReceiverConfirmed = true,
        bool includeHandoverPhoto = true)
    {
        return new ConfirmHandoverCommand
        {
            StopId = _stopId,
            UserId = _userId,
            Request = new HandoverConfirmRequest
            {
                TripId = _tripId,
                CustomerId = _customerId,
                IsReceiverConfirmed = isReceiverConfirmed,
                SignatureFile = new FakeFormFile([1, 2, 3], "image/png", "signature.png"),
                HandoverPhotoFile = includeHandoverPhoto
                    ? new FakeFormFile([4, 5, 6], "image/jpeg", "handover.jpg")
                    : null!
            }
        };
    }

    private void SeedHandoverData()
    {
        _db.Locations.Add(new Location
        {
            LocationId = _locationId,
            Address = "Test delivery location",
            Latitude = 10.8m,
            Longitude = 106.7m,
            Status = "ACTIVE"
        });

        _db.Customers.Add(new Customer
        {
            CustomerId = _customerId,
            CompanyName = "Test Customer",
            TaxCode = "TEST-001",
            Status = "ACTIVE"
        });

        _db.MasterTrips.Add(new MasterTrip
        {
            TripId = _tripId,
            OriginLocationId = _locationId,
            DestinationLocationId = _locationId,
            TargetTemperature = 4.5m,
            PlannedStartTime = DateTime.UtcNow.AddHours(-1),
            PlannedEndTime = DateTime.UtcNow.AddHours(1),
            Status = "IN_PROGRESS"
        });

        _db.TripStops.Add(new TripStop
        {
            StopId = _stopId,
            TripId = _tripId,
            LocationId = _locationId,
            StopSequence = 1,
            StopType = "DELIVERY",
            PlannedArrivalTime = DateTime.UtcNow.AddMinutes(-10),
            PlannedDepartureTime = DateTime.UtcNow.AddMinutes(20),
            ActualArrivalTime = DateTime.UtcNow.AddMinutes(-5),
            Status = "ARRIVED"
        });

        _db.Drivers.Add(new Driver
        {
            DriverId = _driverId,
            UserId = _userId,
            FullName = "Test Driver",
            IdentityNumber = "DRV-001",
            PhoneNumber = "0900000000",
            DateOfBirth = new DateOnly(1990, 1, 1),
            JoinDate = new DateOnly(2024, 1, 1),
            Status = "ACTIVE"
        });

        _db.TripDrivers.Add(new TripDriver
        {
            TripDriverId = Guid.NewGuid(),
            TripId = _tripId,
            DriverId = _driverId,
            DriverRole = "PRIMARY"
        });

        _db.TransportOrders.Add(new TransportOrder
        {
            OrderId = _orderId,
            TrackingCode = "TRK-RECIPIENT-001",
            CustomerId = _customerId,
            ItemName = "Frozen seafood",
            Category = "SEAFOOD",
            Quantity = 10,
            PackingType = "BOX",
            TempCondition = "FROZEN",
            DestLocation = _locationId,
            MasterTripId = _tripId,
            ReceiverName = "Nguyen Van A",
            ReceiverPhone = "0901234567",
            Status = "SHIPPING"
        });

        _db.Lpns.Add(new Lpn
        {
            LpnId = Guid.NewGuid(),
            LpnCode = "LPN-RECIPIENT-001",
            OrderId = _orderId,
            ReceiptId = Guid.NewGuid(),
            TripId = _tripId,
            Quantity = 10,
            ActualWeightKg = 100,
            ActualCbm = 1,
            State = LpnState.SHIPPING
        });

        _db.Quotations.Add(new Quotation
        {
            QuoteId = Guid.NewGuid(),
            OrderId = _orderId,
            BaseFreight = 1_000_000,
            VatAmount = 0,
            FinalAmount = 1_000_000,
            PricingSource = "TEST",
            Status = "ACCEPTED",
            CreatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakePdfGeneratorService : IPdfGeneratorService
    {
        public Task<byte[]> GeneratePdfAsync<T>(string templateName, T data)
        {
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }
}
