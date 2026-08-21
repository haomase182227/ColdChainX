using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ColdChainX.Application.Features.Delivery.Commands;
using ColdChainX.Application.Features.Delivery.Queries;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Exceptions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class DeliveryCommandHandlerTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly IFileService _fileService;
        private readonly IConfiguration _configuration;
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _driverId = Guid.NewGuid();
        private readonly Guid _tripId = Guid.NewGuid();
        private readonly Guid _lpnId = Guid.NewGuid();
        private readonly Guid _orderId = Guid.NewGuid();

        public DeliveryCommandHandlerTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _fileService = new FakeFileService();

            var settings = new System.Collections.Generic.Dictionary<string, string>
            {
                { "PaymentSettings:BankId", "vietinbank" },
                { "PaymentSettings:BankAccount", "1111111111" },
                { "PaymentSettings:BankAccountName", "NGUYEN VAN A" }
            };
            _configuration = new FakeConfiguration(settings);

            var driverRole = new Role
            {
                RoleId = Guid.NewGuid(),
                RoleName = "Driver",
                Description = "Driver role"
            };
            _db.Roles.Add(driverRole);

            _db.Users.Add(new User
            {
                UserId = _userId,
                Username = "driver_test",
                PasswordHash = "hashed",
                FullName = "Driver Test User",
                Status = "ACTIVE",
                RoleId = driverRole.RoleId,
                Role = driverRole
            });

            _db.Drivers.Add(new Driver
            {
                DriverId = _driverId,
                UserId = _userId,
                FullName = "Test Driver",
                IdentityNumber = "DRV001",
                PhoneNumber = "0900000001",
                DateOfBirth = new DateOnly(1990, 1, 1),
                JoinDate = new DateOnly(2024, 1, 1),
                Status = "AVAILABLE"
            });

            _db.MasterTrips.Add(new MasterTrip
            {
                TripId = _tripId,
                OriginLocationId = Guid.NewGuid(),
                DestinationLocationId = Guid.NewGuid(),
                TargetTemperature = 4.5m,
                PlannedStartTime = DateTime.UtcNow,
                PlannedEndTime = DateTime.UtcNow.AddHours(4),
                Status = "DISPATCHED"
            });

            _db.TripDrivers.Add(new TripDriver
            {
                TripId = _tripId,
                DriverId = _driverId,
                DriverRole = "PRIMARY"
            });

            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = _orderId,
                TrackingCode = "TRK-001",
                ItemName = "Salmon",
                Category = "SEAFOOD",
                Quantity = 10,
                PackingType = "BOX",
                TempCondition = "FROZEN",
                OrderDimension = new ColdChainX.Core.Entities.OrderDimension { ExpectedWeightKg = 100,
                ActualWeightKg = 100,
                ExpectedCbm = 2 },

                Status = "SHIPPING"
            });

            _db.Lpns.Add(new Lpn
            {
                LpnId = _lpnId,
                LpnCode = "LPN-001",
                OrderId = _orderId,
                TripId = _tripId,
                State = LpnState.SHIPPING
            });

            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Confirm_ValidRequest_ShouldSucceed()
        {
            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                ReceiverName = "Nguyen Van A",
                ReceiverPhone = "0901234567",
                EvidenceImage = image,
                UserId = _userId
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("DELIVERED", result.Data.OutcomeType);
            Assert.Equal("Nguyen Van A", result.Data.ReceiverName);

            var lpn = await _db.Lpns.FindAsync(_lpnId);
            Assert.NotNull(lpn);
            Assert.Equal(LpnState.DELIVERED, lpn.State);
            Assert.Equal("https://res.cloudinary.com/test/image.jpg", lpn.EvidenceImageUrl);

            var confirmation = await _db.LpnDeliveryConfirmations.FirstOrDefaultAsync(c => c.LpnId == _lpnId);
            Assert.NotNull(confirmation);
            Assert.Equal("DELIVERED", confirmation.OutcomeType);
            Assert.Equal("Nguyen Van A", confirmation.ReceiverName);
            Assert.Equal(_userId, confirmation.ConfirmedByDriverId);
        }

        [Fact]
        public async Task Confirm_BankTransferWithoutReceipt_ShouldThrowValidationException()
        {
            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                ReceiverName = "Nguyen Van A",
                ReceiverPhone = "0901234567",
                EvidenceImage = image,
                UserId = _userId,
                CodAmount = 500000,
                CodPaymentMethod = "BANK_TRANSFER",
                CodReceiptImage = null // Missing receipt image
            };

            var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
            Assert.Equal("Cod receipt image is required for BANK_TRANSFER payment method.", ex.Message);
        }

        [Fact]
        public async Task Confirm_BankTransferWithReceipt_ShouldSucceed()
        {
            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var receiptImage = new FakeFormFile(new byte[] { 5, 6, 7, 8 }, "image/png", "receipt.png");
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                ReceiverName = "Nguyen Van A",
                ReceiverPhone = "0901234567",
                EvidenceImage = image,
                UserId = _userId,
                CodAmount = 500000,
                CodPaymentMethod = "BANK_TRANSFER",
                CodReceiptImage = receiptImage
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("DELIVERED", result.Data.OutcomeType);
            Assert.Equal("BANK_TRANSFER", result.Data.CodPaymentMethod);
            Assert.Equal(500000, result.Data.CodAmount);
            Assert.Equal("https://res.cloudinary.com/test/image.jpg", result.Data.CodReceiptImageUrl);
            Assert.NotNull(result.Data.VietQrUrl);
            Assert.Contains("amount=500000", result.Data.VietQrUrl);
        }

        [Fact]
        public async Task Confirm_WithNewSeal_ShouldUpdateTripSeal()
        {
            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                ReceiverName = "Nguyen Van A",
                ReceiverPhone = "0901234567",
                EvidenceImage = image,
                UserId = _userId,
                NewSealNumber = "SEAL-NEW-1234"
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("SEAL-NEW-1234", result.Data.NewSealNumber);

            var trip = await _db.MasterTrips.Include(t => t.Seals).FirstOrDefaultAsync(t => t.TripId == _tripId);
            Assert.NotNull(trip);
            Assert.Equal("SEAL-NEW-1234", trip.SealNumber);
            Assert.Contains(trip.Seals, s => s.SealCode == "SEAL-NEW-1234" && s.Status == "APPLIED");
        }

        [Fact]
        public async Task Confirm_DriverNotAssigned_ShouldThrowForbidden()
        {
            var otherUserId = Guid.NewGuid();
            var otherDriverId = Guid.NewGuid();

            var driverRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Driver");
            _db.Users.Add(new User 
            { 
                UserId = otherUserId, 
                Username = "other", 
                PasswordHash = "hashed",
                FullName = "Other Driver User",
                Status = "ACTIVE",
                RoleId = driverRole?.RoleId,
                Role = driverRole
            });
            _db.Drivers.Add(new Driver
            {
                DriverId = otherDriverId,
                UserId = otherUserId,
                FullName = "Other Driver",
                IdentityNumber = "DRV002",
                PhoneNumber = "0900000002",
                DateOfBirth = new DateOnly(1992, 2, 2),
                JoinDate = new DateOnly(2024, 1, 1),
                Status = "AVAILABLE"
            });
            await _db.SaveChangesAsync();

            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                ReceiverName = "Nguyen Van A",
                EvidenceImage = image,
                UserId = otherUserId // Not assigned to trip
            };

            await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Confirm_LpnNotInShippingState_ShouldThrowInvalidOperation()
        {
            var lpn = await _db.Lpns.FindAsync(_lpnId);
            lpn!.State = LpnState.DELIVERED;
            await _db.SaveChangesAsync();

            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                ReceiverName = "Nguyen Van A",
                EvidenceImage = image,
                UserId = _userId
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Reject_ValidRequest_ShouldSucceed()
        {
            var handler = new RejectLpnDeliveryCommandHandler(_db, _fileService);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var command = new RejectLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                RejectReason = "DAMAGED",
                RejectNote = "Damaged during shipment",
                EvidenceImage = image,
                UserId = _userId
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("REJECTED", result.Data.OutcomeType);
            Assert.Equal("DAMAGED", result.Data.RejectReason);

            var lpn = await _db.Lpns.FindAsync(_lpnId);
            Assert.NotNull(lpn);
            Assert.Equal(LpnState.DELIVERY_RETURNED, lpn.State);

            var confirmation = await _db.LpnDeliveryConfirmations.FirstOrDefaultAsync(c => c.LpnId == _lpnId);
            Assert.NotNull(confirmation);
            Assert.Equal("REJECTED", confirmation.OutcomeType);
            Assert.Equal("DAMAGED", confirmation.RejectReason);
            Assert.Equal("Damaged during shipment", confirmation.RejectNote);
        }

        [Fact]
        public async Task Reject_ReasonOtherWithoutNote_ShouldThrowValidationException()
        {
            var handler = new RejectLpnDeliveryCommandHandler(_db, _fileService);
            var image = new FakeFormFile(new byte[] { 1, 2, 3, 4 }, "image/jpeg", "evidence.jpg");
            var command = new RejectLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                RejectReason = "OTHER",
                RejectNote = null, // Missing note for OTHER
                EvidenceImage = image,
                UserId = _userId
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task VerifyCod_ValidRequest_ShouldSucceedAndSyncOrderStatus()
        {
            var lpn = await _db.Lpns.FindAsync(_lpnId);
            lpn!.State = LpnState.DELIVERED;

            var confirmation = new LpnDeliveryConfirmation
            {
                ConfirmationId = Guid.NewGuid(),
                LpnId = _lpnId,
                TripId = _tripId,
                OrderId = _orderId,
                OutcomeType = "DELIVERED",
                ReceiverName = "Nguyen Van A",
                EvidenceImageUrl = "https://res.cloudinary.com/test/image.jpg",
                ConfirmedByDriverId = _userId,
                ConfirmedAt = DateTime.UtcNow,
                CodAmount = 2000,
                CodPaymentMethod = "BANK_TRANSFER",
                IsCodVerified = false
            };
            _db.LpnDeliveryConfirmations.Add(confirmation);
            await _db.SaveChangesAsync();

            var handler = new VerifyCodPaymentCommandHandler(_db, _configuration);
            var command = new VerifyCodPaymentCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                UserId = _userId
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(result.Data.IsCodVerified);
            Assert.NotNull(result.Data.CodVerifiedAt);

            var order = await _db.TransportOrders.FindAsync(_orderId);
            Assert.Equal("DELIVERED", order!.Status);
        }

        [Fact]
        public async Task VerifyCod_AlreadyVerified_ShouldThrowConflict()
        {
            var lpnId2 = Guid.NewGuid();
            var lpn2 = new Lpn
            {
                LpnId = lpnId2,
                LpnCode = "LPN-002",
                OrderId = _orderId,
                TripId = _tripId,
                State = LpnState.DELIVERED
            };
            _db.Lpns.Add(lpn2);

            var confirmation = new LpnDeliveryConfirmation
            {
                ConfirmationId = Guid.NewGuid(),
                LpnId = lpnId2,
                TripId = _tripId,
                OrderId = _orderId,
                OutcomeType = "DELIVERED",
                ReceiverName = "Nguyen Van A",
                EvidenceImageUrl = "https://res.cloudinary.com/test/image.jpg",
                ConfirmedByDriverId = _userId,
                ConfirmedAt = DateTime.UtcNow,
                CodAmount = 2000,
                CodPaymentMethod = "BANK_TRANSFER",
                IsCodVerified = true, // already verified
                CodVerifiedAt = DateTime.UtcNow
            };
            _db.LpnDeliveryConfirmations.Add(confirmation);
            await _db.SaveChangesAsync();

            var handler = new VerifyCodPaymentCommandHandler(_db, _configuration);
            var command = new VerifyCodPaymentCommand
            {
                TripId = _tripId,
                LpnId = lpnId2,
                UserId = _userId
            };

            await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task VerifyCod_OutcomeNotDelivered_ShouldThrowInvalidOperation()
        {
            var lpnId3 = Guid.NewGuid();
            var lpn3 = new Lpn
            {
                LpnId = lpnId3,
                LpnCode = "LPN-003",
                OrderId = _orderId,
                TripId = _tripId,
                State = LpnState.DELIVERY_RETURNED
            };
            _db.Lpns.Add(lpn3);

            var confirmation = new LpnDeliveryConfirmation
            {
                ConfirmationId = Guid.NewGuid(),
                LpnId = lpnId3,
                TripId = _tripId,
                OrderId = _orderId,
                OutcomeType = "REJECTED", // not DELIVERED
                RejectReason = "DAMAGED",
                EvidenceImageUrl = "https://res.cloudinary.com/test/image.jpg",
                ConfirmedByDriverId = _userId,
                ConfirmedAt = DateTime.UtcNow,
                IsCodVerified = false
            };
            _db.LpnDeliveryConfirmations.Add(confirmation);
            await _db.SaveChangesAsync();

            var handler = new VerifyCodPaymentCommandHandler(_db, _configuration);
            var command = new VerifyCodPaymentCommand
            {
                TripId = _tripId,
                LpnId = lpnId3,
                UserId = _userId
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task ConfirmLpnDelivery_ShouldUse4_5TemperatureFallback_WhenNoTelemetryLogsExist()
        {
            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = _lpnId,
                ReceiverName = "Nguyen Van A",
                ReceiverPhone = "0901234567",
                EvidenceImage = new FakeFormFile(new byte[] { 1, 2, 3 }),
                UserId = _userId
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(4.5m, result.Data.RecordedTemperature);

            var confirmation = await _db.LpnDeliveryConfirmations.FirstOrDefaultAsync(c => c.LpnId == _lpnId);
            Assert.NotNull(confirmation);
            Assert.Equal(4.5m, confirmation.RecordedTemperature);

            var lpn = await _db.Lpns.FirstOrDefaultAsync(l => l.LpnId == _lpnId);
            Assert.NotNull(lpn);
            Assert.Equal(4.5m, lpn.RecordedTemperature);
        }

        [Fact]
        public async Task ConfirmLpnDelivery_ShouldUseLatestTelemetryLogTemperature_WhenTelemetryLogsExist()
        {
            var lpnIdForTelemetry = Guid.NewGuid();
            _db.Lpns.Add(new Lpn
            {
                LpnId = lpnIdForTelemetry,
                LpnCode = "LPN-TELEMETRY-TEST",
                OrderId = _orderId,
                ReceiptId = Guid.NewGuid(),
                TripId = _tripId,
                Quantity = 10,
                State = LpnState.SHIPPING
            });

            _db.TelemetryLogs.Add(new TelemetryLog
            {
                LogId = Guid.NewGuid(),
                TripId = _tripId,
                Temperature = 3.8m,
                Timestamp = DateTime.UtcNow.AddMinutes(-5)
            });
            _db.TelemetryLogs.Add(new TelemetryLog
            {
                LogId = Guid.NewGuid(),
                TripId = _tripId,
                Temperature = 2.5m, // This is the latest telemetry log
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = lpnIdForTelemetry,
                ReceiverName = "Nguyen Van B",
                ReceiverPhone = "0901234567",
                EvidenceImage = new FakeFormFile(new byte[] { 1, 2, 3 }),
                UserId = _userId
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(2.5m, result.Data.RecordedTemperature);

            var confirmation = await _db.LpnDeliveryConfirmations.FirstOrDefaultAsync(c => c.LpnId == lpnIdForTelemetry);
            Assert.NotNull(confirmation);
            Assert.Equal(2.5m, confirmation.RecordedTemperature);

            var lpn = await _db.Lpns.FirstOrDefaultAsync(l => l.LpnId == lpnIdForTelemetry);
            Assert.NotNull(lpn);
            Assert.Equal(2.5m, lpn.RecordedTemperature);
        }

        [Fact]
        public async Task Checkin_WithProofImage_ShouldSucceed()
        {
            var stopId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Test Destination",
                Latitude = 10.8465m,
                Longitude = 106.8042m
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 10,
                StopType = "DELIVERY",
                PlannedArrivalTime = DateTime.UtcNow,
                PlannedDepartureTime = DateTime.UtcNow.AddHours(1),
                Status = "PLANNED"
            });
            await _db.SaveChangesAsync();

            var handler = new CheckinDriverCommandHandler(_db, _configuration);
            var command = new CheckinDriverCommand
            {
                StopId = stopId,
                ProofImageUrl = "https://example.com/proofs/arrival.jpg",
                Latitude = 10.8466m,
                Longitude = 106.8043m,
                LocationTimestamp = DateTimeOffset.UtcNow,
                AccuracyMeters = 10,
                UserId = _userId
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(stopId, result.Data.StopId);
            Assert.Equal("https://example.com/proofs/arrival.jpg", result.Data.ProofImageUrl);

            var dbStop = await _db.TripStops.FindAsync(stopId);
            Assert.NotNull(dbStop);
            Assert.NotNull(dbStop.ActualArrivalTime);
            Assert.Equal("ARRIVED", dbStop.Status);
        }

        [Fact]
        public async Task Checkin_WithoutProofImage_ShouldThrowValidationException()
        {
            var stopId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Test Destination",
                Latitude = 10.8465m,
                Longitude = 106.8042m
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 11,
                StopType = "DELIVERY",
                PlannedArrivalTime = DateTime.UtcNow,
                PlannedDepartureTime = DateTime.UtcNow.AddHours(1),
                Status = "PLANNED"
            });
            await _db.SaveChangesAsync();

            var handler = new CheckinDriverCommandHandler(_db, _configuration);
            var command = new CheckinDriverCommand
            {
                StopId = stopId,
                ProofImageUrl = "", // Missing arrival proof image
                Latitude = 10.8465m,
                Longitude = 106.8042m,
                UserId = _userId
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Checkin_TooFar700m_ShouldThrowValidationException()
        {
            var stopId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Test Destination",
                Latitude = 10.8465m,
                Longitude = 106.8042m
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 12,
                StopType = "DELIVERY",
                PlannedArrivalTime = DateTime.UtcNow,
                PlannedDepartureTime = DateTime.UtcNow.AddHours(1),
                Status = "PLANNED"
            });
            await _db.SaveChangesAsync();

            var handler = new CheckinDriverCommandHandler(_db, _configuration);
            var command = new CheckinDriverCommand
            {
                StopId = stopId,
                ProofImageUrl = "https://example.com/proofs/arrival.jpg",
                Latitude = 11.5000m, // Far away (> 700m)
                Longitude = 107.5000m,
                LocationTimestamp = DateTimeOffset.UtcNow,
                AccuracyMeters = 10,
                UserId = _userId
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Checkin_WithStaleClientGps_ShouldThrowValidationException()
        {
            var stopId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Test Destination",
                Latitude = 10.8465m,
                Longitude = 106.8042m
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 13,
                StopType = "DELIVERY",
                PlannedArrivalTime = DateTime.UtcNow,
                PlannedDepartureTime = DateTime.UtcNow.AddHours(1),
                Status = "PLANNED"
            });
            await _db.SaveChangesAsync();

            var handler = new CheckinDriverCommandHandler(_db, _configuration);
            var command = new CheckinDriverCommand
            {
                StopId = stopId,
                ProofImageUrl = "https://example.com/proofs/arrival.jpg",
                Latitude = 10.8466m,
                Longitude = 106.8043m,
                LocationTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
                AccuracyMeters = 10,
                UserId = _userId
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Checkin_WithoutAnyGps_ShouldThrowValidationException()
        {
            var stopId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Test Destination",
                Latitude = 10.8465m,
                Longitude = 106.8042m
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 14,
                StopType = "DELIVERY",
                PlannedArrivalTime = DateTime.UtcNow,
                PlannedDepartureTime = DateTime.UtcNow.AddHours(1),
                Status = "PLANNED"
            });
            await _db.SaveChangesAsync();

            var handler = new CheckinDriverCommandHandler(_db, _configuration);
            var command = new CheckinDriverCommand
            {
                StopId = stopId,
                ProofImageUrl = "https://example.com/proofs/arrival.jpg",
                UserId = _userId
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Checkin_WhenAlreadyArrived_ShouldThrowConflictWithoutCreatingAnotherEvent()
        {
            var stopId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            var originalArrival = DateTime.UtcNow.AddMinutes(-5);
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Test Destination",
                Latitude = 10.8465m,
                Longitude = 106.8042m
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 15,
                StopType = "DELIVERY",
                PlannedArrivalTime = DateTime.UtcNow,
                PlannedDepartureTime = DateTime.UtcNow.AddHours(1),
                ActualArrivalTime = originalArrival,
                Status = "ARRIVED"
            });
            _db.TripStopEvents.Add(new TripStopEvent
            {
                EventId = Guid.NewGuid(),
                StopId = stopId,
                EventType = "DRIVER_CHECKIN",
                EventTime = originalArrival
            });
            await _db.SaveChangesAsync();

            var handler = new CheckinDriverCommandHandler(_db, _configuration);
            var command = new CheckinDriverCommand
            {
                StopId = stopId,
                ProofImageUrl = "https://example.com/proofs/arrival-again.jpg",
                Latitude = 10.8466m,
                Longitude = 106.8043m,
                LocationTimestamp = DateTimeOffset.UtcNow,
                AccuracyMeters = 10,
                UserId = _userId
            };

            await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
            Assert.Equal(1, await _db.TripStopEvents.CountAsync(e => e.StopId == stopId && e.EventType == "DRIVER_CHECKIN"));
            Assert.Equal(originalArrival, (await _db.TripStops.FindAsync(stopId))!.ActualArrivalTime);
        }

        [Fact]
        public async Task Confirm_WithoutCheckin_ShouldThrowValidationException()
        {
            var locationId = Guid.NewGuid();
            var lpnId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Test Delivery Address",
                Latitude = 10.8465m,
                Longitude = 106.8042m
            });
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-CHECKIN-TEST",
                ItemName = "Milk",
                Category = "DAIRY",
                PackingType = "BOX",
                TempCondition = "COLD",
                DestLocation = locationId,
                Status = "SHIPPING"
            });
            _db.Lpns.Add(new Lpn
            {
                LpnId = lpnId,
                LpnCode = "LPN-CHECKIN-TEST",
                OrderId = orderId,
                TripId = _tripId,
                State = LpnState.SHIPPING
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = Guid.NewGuid(),
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 12,
                StopType = "DELIVERY",
                Status = "PLANNED",
                ActualArrivalTime = null
            });
            await _db.SaveChangesAsync();

            var handler = new ConfirmLpnDeliveryCommandHandler(_db, _fileService, _configuration);
            var command = new ConfirmLpnDeliveryCommand
            {
                TripId = _tripId,
                LpnId = lpnId,
                ReceiverName = "Nguyen Van A",
                ReceiverPhone = "0901234567",
                EvidenceImage = new FakeFormFile(new byte[] { 1, 2, 3 }),
                UserId = _userId
            };

            var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
            Assert.Contains("must check in", ex.Message);
        }

        [Fact]
        public async Task ReportNoShow_WithEvidence_ShouldSucceedWithoutSlaWait()
        {
            var stopId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var secondOrderId = Guid.NewGuid();

            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Kho Lạnh Khách Vắng Mặt",
                Latitude = 10.8m,
                Longitude = 106.8m
            });

            var tripStop = new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 2,
                StopType = "DELIVERY",
                ActualArrivalTime = DateTime.UtcNow.AddMinutes(-1), // Mới đến 1 phút trước!
                Status = "ARRIVED",
                Note = "Đến trước cổng bãi."
            };
            _db.TripStops.Add(tripStop);

            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                MasterTripId = _tripId,
                TrackingCode = "TRK-NOSHOW",
                ItemName = "Seafood Box",
                Category = "SEAFOOD",
                Quantity = 5,
                PackingType = "BOX",
                TempCondition = "FROZEN",
                OrderDimension = new ColdChainX.Core.Entities.OrderDimension { ExpectedWeightKg = 50, ActualWeightKg = 50, ExpectedCbm = 1 },
                CustomerId = Guid.NewGuid(),
                DestLocation = locationId,
                Status = "IN_TRANSIT"
            });
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = secondOrderId,
                MasterTripId = _tripId,
                TrackingCode = "TRK-NOSHOW-002",
                ItemName = "Frozen Food Box",
                Category = "FROZEN_FOOD",
                Quantity = 3,
                PackingType = "BOX",
                TempCondition = "FROZEN",
                OrderDimension = new ColdChainX.Core.Entities.OrderDimension
                {
                    ExpectedWeightKg = 30,
                    ActualWeightKg = 30,
                    ExpectedCbm = 0.5m
                },
                CustomerId = Guid.NewGuid(),
                DestLocation = locationId,
                Status = "IN_TRANSIT"
            });
            _db.Lpns.AddRange(
                new Lpn
                {
                    LpnId = Guid.NewGuid(),
                    LpnCode = "LPN-NOSHOW-001",
                    OrderId = orderId,
                    TripId = _tripId,
                    State = LpnState.SHIPPING
                },
                new Lpn
                {
                    LpnId = Guid.NewGuid(),
                    LpnCode = "LPN-NOSHOW-002",
                    OrderId = secondOrderId,
                    TripId = _tripId,
                    State = LpnState.SHIPPING
                });
            await _db.SaveChangesAsync();

            var goongService = new FakeGoongService();
            var handler = new ReportNoShowCommandHandler(_db, goongService);
            var command = new ReportNoShowCommand
            {
                TripStopId = stopId,
                DriverId = _userId,
                EvidenceImageUrl = "https://example.com/proofs/no-show-evidence.jpg"
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.Success);

            var dbStop = await _db.TripStops.FindAsync(stopId);
            Assert.NotNull(dbStop);
            Assert.Equal("SKIPPED_NOSHOW", dbStop.Status);
            Assert.NotNull(dbStop.ActualDepartureTime);
            Assert.Contains("No-Show Evidence: https://example.com/proofs/no-show-evidence.jpg", dbStop.Note);

            var stopEvent = await _db.TripStopEvents.FirstOrDefaultAsync(e => e.StopId == stopId && e.EventType == "NO_SHOW_REPORT");
            Assert.NotNull(stopEvent);
            Assert.Contains("ProofImageUrl: https://example.com/proofs/no-show-evidence.jpg", stopEvent.MetaData);

            var docCount = await _db.TransportDocuments.CountAsync(d => d.OrderId == orderId && d.DocType == "NO_SHOW_EVIDENCE");
            Assert.Equal(0, docCount);

            var penaltyCount = await _db.PenaltyBills.CountAsync();
            Assert.Equal(0, penaltyCount);

            var returnedOrders = await _db.TransportOrders
                .Where(order => order.OrderId == orderId || order.OrderId == secondOrderId)
                .ToListAsync();
            Assert.All(returnedOrders, order => Assert.Equal("DELIVERY_FAILED_NOSHOW", order.Status));

            var returnedLpns = await _db.Lpns
                .Where(lpn => lpn.OrderId == orderId || lpn.OrderId == secondOrderId)
                .ToListAsync();
            Assert.All(returnedLpns, lpn => Assert.Equal(LpnState.RETURN_PENDING, lpn.State));

            var noShowEpods = await _db.DeliveryEpods
                .Where(epod => epod.OrderId == orderId || epod.OrderId == secondOrderId)
                .ToListAsync();
            Assert.Equal(2, noShowEpods.Count);
            Assert.All(noShowEpods, epod => Assert.Equal("NO_SHOW", epod.Status));
        }

        [Fact]
        public async Task ReportNoShow_DriverNotAssignedToTrip_ShouldBeForbidden()
        {
            var otherUserId = Guid.NewGuid();
            var driverRole = await _db.Roles.SingleAsync(role => role.RoleName == "Driver");
            _db.Users.Add(new User
            {
                UserId = otherUserId,
                Username = "unassigned_driver",
                FullName = "Unassigned Driver",
                Status = "ACTIVE",
                RoleId = driverRole.RoleId,
                Role = driverRole
            });
            _db.Drivers.Add(new Driver
            {
                DriverId = Guid.NewGuid(),
                UserId = otherUserId,
                FullName = "Unassigned Driver",
                IdentityNumber = "DRV-UNASSIGNED",
                PhoneNumber = "0900000099",
                DateOfBirth = new DateOnly(1990, 1, 1),
                JoinDate = new DateOnly(2024, 1, 1),
                Status = "ACTIVE"
            });

            var locationId = Guid.NewGuid();
            var stopId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                Address = "Unauthorized No-Show Stop",
                Latitude = 10.8m,
                Longitude = 106.8m
            });
            _db.TripStops.Add(new TripStop
            {
                StopId = stopId,
                TripId = _tripId,
                LocationId = locationId,
                StopSequence = 2,
                StopType = "DELIVERY",
                ActualArrivalTime = DateTime.UtcNow,
                Status = "ARRIVED"
            });
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                MasterTripId = _tripId,
                TrackingCode = "TRK-NOSHOW-FORBIDDEN",
                ItemName = "Frozen cargo",
                Category = "FROZEN",
                Quantity = 1,
                PackingType = "BOX",
                TempCondition = "FROZEN",
                DestLocation = locationId,
                Status = "IN_TRANSIT"
            });
            await _db.SaveChangesAsync();

            var handler = new ReportNoShowCommandHandler(_db, new FakeGoongService());
            var command = new ReportNoShowCommand
            {
                TripStopId = stopId,
                DriverId = otherUserId,
                EvidenceImageUrl = "https://example.com/proofs/unauthorized.jpg"
            };

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                handler.Handle(command, CancellationToken.None));

            Assert.Equal("ARRIVED", (await _db.TripStops.FindAsync(stopId))!.Status);
            Assert.Equal("IN_TRANSIT", (await _db.TransportOrders.FindAsync(orderId))!.Status);
            Assert.Empty(_db.DeliveryEpods.Where(epod => epod.OrderId == orderId));
        }

        [Fact]
        public async Task ReportNoShow_WithoutEvidence_ShouldThrowValidationException()
        {
            var stopId = Guid.NewGuid();
            var handler = new ReportNoShowCommandHandler(_db, new FakeGoongService());
            var command = new ReportNoShowCommand
            {
                TripStopId = stopId,
                DriverId = _userId,
                EvidenceImageUrl = "" // Thiếu ảnh bằng chứng
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task NearestReturnWarehouses_ReturnsAllActiveWarehousesSortedByDistance()
        {
            var vehicle = new Vehicle
            {
                VehicleId = Guid.NewGuid(),
                TruckPlate = "51C-RETURN",
                VehicleType = "REFRIGERATED_TRUCK",
                MaxWeight = 5000,
                MaxCbm = 30,
                MinTemp = -20,
                MaxTemp = 10,
                Status = "ON_TRIP"
            };
            _db.Vehicles.Add(vehicle);
            var trip = await _db.MasterTrips.FindAsync(_tripId);
            trip!.VehicleId = vehicle.VehicleId;

            var currentLocation = new Location
            {
                LocationId = Guid.NewGuid(),
                Address = "Current vehicle location",
                Latitude = 10.700000m,
                Longitude = 106.700000m
            };
            _db.Locations.Add(currentLocation);
            _db.TripStops.Add(new TripStop
            {
                StopId = Guid.NewGuid(),
                TripId = _tripId,
                LocationId = currentLocation.LocationId,
                StopSequence = 1,
                StopType = "DELIVERY",
                Status = "ARRIVED",
                ActualArrivalTime = DateTime.UtcNow
            });

            for (var index = 0; index < 7; index++)
            {
                _db.Warehouses.Add(new Warehouse
                {
                    WarehouseId = Guid.NewGuid(),
                    WarehouseCode = $"WAREHOUSE-{index}",
                    WarehouseName = $"Kho lạnh {index}",
                    WarehouseType = "COLD_STORAGE",
                    Address = FormattableString.Invariant($"{10.700000m + index * 0.01m},{106.700000m}"),
                    MaxPallets = 100,
                    Status = "ACTIVE"
                });
            }
            _db.Warehouses.Add(new Warehouse
            {
                WarehouseId = Guid.NewGuid(),
                WarehouseCode = "WAREHOUSE-INACTIVE",
                WarehouseName = "Kho ngưng hoạt động",
                WarehouseType = "COLD_STORAGE",
                Address = "10.700000,106.700000",
                MaxPallets = 100,
                Status = "INACTIVE"
            });
            await _db.SaveChangesAsync();

            var handler = new GetNearestReturnWarehousesQueryHandler(_db);
            var result = await handler.Handle(
                new GetNearestReturnWarehousesQuery { TripId = _tripId },
                CancellationToken.None);

            Assert.True(result.Success);
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
            Assert.Equal(7, payload.RootElement.GetProperty("TotalWarehouses").GetInt32());
            var warehouses = payload.RootElement.GetProperty("Warehouses");
            Assert.Equal(7, warehouses.GetArrayLength());
            Assert.Equal("WAREHOUSE-0", warehouses[0].GetProperty("WarehouseCode").GetString());
        }
    }

    internal class FakeGoongService : IGoongMapService
    {
        public Task<ColdChainX.Application.DTOs.Dispatch.GoongOptimizedRouteResult> GetOptimizedRouteAsync(
            string origin, string destination, string? waypoints, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ColdChainX.Application.DTOs.Dispatch.GoongOptimizedRouteResult());
        }
    }

    public class FakeFormFile : IFormFile
    {
        private readonly byte[] _content;
        public FakeFormFile(byte[] content, string contentType = "image/jpeg", string fileName = "test.jpg")
        {
            _content = content;
            ContentType = contentType;
            FileName = fileName;
            Length = content.Length;
        }
        public string ContentType { get; }
        public string ContentDisposition => "";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length { get; }
        public string Name => "file";
        public string FileName { get; }
        public Stream OpenReadStream() => new MemoryStream(_content);
        public void CopyTo(Stream target) => target.Write(_content, 0, _content.Length);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            return target.WriteAsync(_content, 0, _content.Length, cancellationToken);
        }
    }

    public class FakeFileService : IFileService
    {
        public Task<string> UploadFileAsync(IFormFile file) => Task.FromResult("https://res.cloudinary.com/test/image.jpg");
        public Task<string> UploadFileAsync(Stream stream, string fileName) => Task.FromResult("https://res.cloudinary.com/test/image.jpg");
        public Task<string> UploadFileAsync(byte[] fileBytes, string fileName) => Task.FromResult("https://res.cloudinary.com/test/image.jpg");
        public string GetSignedUrl(string publicId) => "https://res.cloudinary.com/test/image.jpg";
    }

    public class FakeConfiguration : IConfiguration
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _values = new();

        public FakeConfiguration(System.Collections.Generic.Dictionary<string, string> values)
        {
            _values = values;
        }

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var val) ? val : null;
            set => _values[key] = value!;
        }

        public System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren() => System.Linq.Enumerable.Empty<IConfigurationSection>();
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotImplementedException();
        public IConfigurationSection GetSection(string key) => null!;
    }
}
