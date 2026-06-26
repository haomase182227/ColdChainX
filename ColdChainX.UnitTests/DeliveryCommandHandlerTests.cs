using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ColdChainX.Application.Features.Delivery.Commands;
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

            // Seed Role & User
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
                ExpectedWeightKg = 100,
                ActualWeightKg = 100,
                ExpectedCbm = 2,
                CargoValue = 1000,
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
            // Arrange
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

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("DELIVERED", result.Data.OutcomeType);
            Assert.Equal("Nguyen Van A", result.Data.ReceiverName);

            // Verify LPN state updated to DELIVERED
            var lpn = await _db.Lpns.FindAsync(_lpnId);
            Assert.NotNull(lpn);
            Assert.Equal(LpnState.DELIVERED, lpn.State);
            Assert.Equal("https://res.cloudinary.com/test/image.jpg", lpn.EvidenceImageUrl);

            // Verify confirmation record created
            var confirmation = await _db.LpnDeliveryConfirmations.FirstOrDefaultAsync(c => c.LpnId == _lpnId);
            Assert.NotNull(confirmation);
            Assert.Equal("DELIVERED", confirmation.OutcomeType);
            Assert.Equal("Nguyen Van A", confirmation.ReceiverName);
            Assert.Equal(_userId, confirmation.ConfirmedByDriverId);
        }

        [Fact]
        public async Task Confirm_BankTransferWithoutReceipt_ShouldThrowValidationException()
        {
            // Arrange
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

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
            Assert.Equal("Cod receipt image is required for BANK_TRANSFER payment method.", ex.Message);
        }

        [Fact]
        public async Task Confirm_BankTransferWithReceipt_ShouldSucceed()
        {
            // Arrange
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

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
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
            // Arrange
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

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("SEAL-NEW-1234", result.Data.NewSealNumber);

            // Verify Trip seal code updated
            var trip = await _db.MasterTrips.Include(t => t.Seals).FirstOrDefaultAsync(t => t.TripId == _tripId);
            Assert.NotNull(trip);
            Assert.Equal("SEAL-NEW-1234", trip.SealNumber);
            Assert.Contains(trip.Seals, s => s.SealCode == "SEAL-NEW-1234" && s.Status == "APPLIED");
        }

        [Fact]
        public async Task Confirm_DriverNotAssigned_ShouldThrowForbidden()
        {
            // Arrange
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

            // Act & Assert
            await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Confirm_LpnNotInShippingState_ShouldThrowInvalidOperation()
        {
            // Arrange
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

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Reject_ValidRequest_ShouldSucceed()
        {
            // Arrange
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

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("REJECTED", result.Data.OutcomeType);
            Assert.Equal("DAMAGED", result.Data.RejectReason);

            // Verify LPN state updated to DELIVERY_RETURNED
            var lpn = await _db.Lpns.FindAsync(_lpnId);
            Assert.NotNull(lpn);
            Assert.Equal(LpnState.DELIVERY_RETURNED, lpn.State);

            // Verify confirmation record created
            var confirmation = await _db.LpnDeliveryConfirmations.FirstOrDefaultAsync(c => c.LpnId == _lpnId);
            Assert.NotNull(confirmation);
            Assert.Equal("REJECTED", confirmation.OutcomeType);
            Assert.Equal("DAMAGED", confirmation.RejectReason);
            Assert.Equal("Damaged during shipment", confirmation.RejectNote);
        }

        [Fact]
        public async Task Reject_ReasonOtherWithoutNote_ShouldThrowValidationException()
        {
            // Arrange
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

            // Act & Assert
            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task VerifyCod_ValidRequest_ShouldSucceedAndSyncOrderStatus()
        {
            // Arrange: We need the LPN to be DELIVERED, and have a delivery confirmation with CodAmount > 0 and IsCodVerified = false
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

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.True(result.Data.IsCodVerified);
            Assert.NotNull(result.Data.CodVerifiedAt);

            // Check Order status is synced to DELIVERED now because all LPNs are DELIVERED and COD is verified
            var order = await _db.TransportOrders.FindAsync(_orderId);
            Assert.Equal("DELIVERED", order!.Status);
        }

        [Fact]
        public async Task VerifyCod_AlreadyVerified_ShouldThrowConflict()
        {
            // Arrange: Create a verified confirmation
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

            // Act & Assert
            await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task VerifyCod_OutcomeNotDelivered_ShouldThrowInvalidOperation()
        {
            // Arrange: Create a REJECTED confirmation
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

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));
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
