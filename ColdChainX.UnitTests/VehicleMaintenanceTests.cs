using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Infrastructure.Hubs;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class VehicleMaintenanceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly FleetManagementService _service;
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _vehicleId = Guid.NewGuid();

        public VehicleMaintenanceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _service = new FleetManagementService(
                _db,
                new FakeHubContext(),
                new FakePasswordHasher(),
                new FakeFileService()
            );

            // Seed reference entities
            _db.Users.Add(new User
            {
                UserId = _userId,
                Username = "testmanager",
                PasswordHash = "hashed",
                Email = "manager@coldchainx.com",
                FullName = "Test Manager"
            });

            _db.Vehicles.Add(new Vehicle
            {
                VehicleId = _vehicleId,
                TruckPlate = "29C-12345",
                Brand = "Hino",
                VehicleType = "REEFER_3T",
                MaxWeight = 3000m,
                MaxCbm = 12m,
                CurrentOdometer = 5000.0,
                NextMaintenanceOdometer = 10000.0,
                NextMaintenanceDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30),
                WarningDaysBeforeDue = 15,
                WarningKmBeforeDue = 500.0,
                Status = "ACTIVE"
            });

            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task CreateMaintenanceTicket_LocksVehicle_WithoutOdometerShift()
        {
            // Arrange
            var request = new CreateMaintenanceTicketRequest
            {
                MaintenanceType = "Routine inspection",
                GarageName = "Logistics Center Garage",
                Description = "Engine oil change and cooling unit PTI"
            };

            // Act
            var result = await _service.CreateMaintenanceTicketAsync(_vehicleId, request, _userId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("OPEN", result.Data.Status);

            var vehicle = await _db.Vehicles.FindAsync(_vehicleId);
            Assert.Equal("MAINTENANCE", vehicle!.Status);
            Assert.Equal(10000.0, vehicle.NextMaintenanceOdometer); // should NOT have shifted
        }

        [Fact]
        public async Task CompleteMaintenanceTicket_ShiftsOdometerAndDate_RestoresActive()
        {
            // Arrange
            _db.SystemConfigs.AddRange(
                new SystemConfig { Id = Guid.NewGuid(), Key = "MaintenanceIntervalOdometer", Value = "12000.0" },
                new SystemConfig { Id = Guid.NewGuid(), Key = "MaintenanceIntervalDays", Value = "180" }
            );
            await _db.SaveChangesAsync();

            var request = new CreateMaintenanceTicketRequest
            {
                MaintenanceType = "Routine",
                GarageName = "Main Shop",
                Description = "Periodic service"
            };
            var ticketResult = await _service.CreateMaintenanceTicketAsync(_vehicleId, request, _userId);
            var ticketId = ticketResult.Data!.TicketId;

            var completeRequest = new CompleteMaintenanceTicketRequest
            {
                Cost = 350.00m,
                CompletionDate = DateOnly.FromDateTime(DateTime.Today)
            };

            // Act
            var result = await _service.CompleteMaintenanceTicketAsync(ticketId, completeRequest);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("RESOLVED", result.Data!.Status);
            Assert.Equal(350.00m, result.Data.Cost);

            var vehicle = await _db.Vehicles.FindAsync(_vehicleId);
            Assert.Equal("ACTIVE", vehicle!.Status);
            Assert.Equal(5000.0 + 12000.0, vehicle.NextMaintenanceOdometer); // shifted by 12,000 km
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today).AddDays(180), vehicle.NextMaintenanceDate); // shifted by 180 days
        }

        [Fact]
        public async Task CompleteMaintenanceTicket_KeepsStatusMaintenance_IfOtherActiveTicketsExist()
        {
            // Arrange
            var createReq1 = new CreateMaintenanceTicketRequest { MaintenanceType = "Engine", GarageName = "Shop", Description = "Repair" };
            var createReq2 = new CreateMaintenanceTicketRequest { MaintenanceType = "Reefer", GarageName = "Shop", Description = "Reefer Repair" };

            var t1 = await _service.CreateMaintenanceTicketAsync(_vehicleId, createReq1, _userId);
            var t2 = await _service.CreateMaintenanceTicketAsync(_vehicleId, createReq2, _userId);

            var completeReq = new CompleteMaintenanceTicketRequest
            {
                Cost = 150m,
                CompletionDate = DateOnly.FromDateTime(DateTime.Today)
            };

            // Act - Complete only the first ticket
            var result = await _service.CompleteMaintenanceTicketAsync(t1.Data!.TicketId, completeReq);

            // Assert
            Assert.True(result.Success);
            var vehicle = await _db.Vehicles.FindAsync(_vehicleId);
            Assert.Equal("MAINTENANCE", vehicle!.Status); // Must NOT return to ACTIVE since t2 is still OPEN
        }

        [Fact]
        public async Task UpdateTicketStatus_ToCancelled_RestoresActiveVehicleStatus()
        {
            // Arrange
            var createReq = new CreateMaintenanceTicketRequest { MaintenanceType = "Engine", GarageName = "Shop", Description = "Repair" };
            var t = await _service.CreateMaintenanceTicketAsync(_vehicleId, createReq, _userId);
            var ticketId = t.Data!.TicketId;

            // Act
            var result = await _service.UpdateMaintenanceTicketStatusAsync(ticketId, "CANCELLED");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("CANCELLED", result.Data!.Status);

            var vehicle = await _db.Vehicles.FindAsync(_vehicleId);
            Assert.Equal("ACTIVE", vehicle!.Status); // safely restored to ACTIVE
        }

        [Fact]
        public async Task GetVehicleMaintenanceForecast_CalculatesOverdueAndOverruns()
        {
            // Arrange
            var tripId = Guid.NewGuid();
            _db.MasterTrips.Add(new MasterTrip
            {
                TripId = tripId,
                VehicleId = _vehicleId,
                OriginLocationId = Guid.NewGuid(),
                DestinationLocationId = Guid.NewGuid(),
                TotalDistanceKm = 6000m // trip is 6,000 km
            });
            await _db.SaveChangesAsync();

            // Act - Headroom is 10000 - 5000 = 5000 km. Trip (6000 km) exceeds headroom.
            var result = await _service.GetVehicleMaintenanceForecastAsync(_vehicleId, tripId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.IsOverrunForecast);
            Assert.Equal("OVERDUE", result.Data.ForecastStatus);
            Assert.Contains("exceeds available odometer headroom", result.Data.Message);
        }

        [Fact]
        public async Task SyncOdometer_CreatesVehicleOdometerLog()
        {
            // Arrange
            var request = new SyncOdometerRequest
            {
                Odometer = 6500.5,
                LocationText = "Test Location",
                Reason = "Manual correction check"
            };
            var updaterId = Guid.NewGuid();

            // Act
            var result = await _service.SyncOdometerAsync("29C-12345", request, updaterId);

            // Assert
            Assert.True(result.Success);
            
            // Check vehicle was updated
            var vehicle = await _db.Vehicles.FindAsync(_vehicleId);
            Assert.Equal(6500.5, vehicle!.CurrentOdometer);
            Assert.Equal("Test Location", vehicle.CurrentLocation);

            // Check log was created
            var log = await _db.VehicleOdometerLogs.FirstOrDefaultAsync(l => l.VehicleId == _vehicleId);
            Assert.NotNull(log);
            Assert.Equal(6500.5, log.OdometerValue);
            Assert.Equal("Test Location", log.LocationText);
            Assert.Equal("Manual correction check", log.Reason);
            Assert.Equal(updaterId, log.UpdatedBy);
            Assert.True((DateTime.Now - log.CreatedAt).TotalSeconds < 5);
        }
    }

    // Hand-made mock/fake dependencies to keep unit tests fast and dependency-free

    public class FakeHubContext : IHubContext<NotificationHub>
    {
        public IHubClients Clients => new FakeHubClients();
        public IGroupManager Groups => throw new NotImplementedException();
    }

    public class FakeHubClients : IHubClients
    {
        public IClientProxy All => new FakeClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy();
        public IClientProxy Client(string connectionId) => new FakeClientProxy();
        public IClientProxy Group(string groupName) => new FakeClientProxy();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeClientProxy();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeClientProxy();
        public IClientProxy User(string userId) => new FakeClientProxy();
        public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeClientProxy();
    }

    public class FakeClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public class FakePasswordHasher : IPasswordHasher<User>
    {
        public string HashPassword(User user, string password) => password;
        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
            => PasswordVerificationResult.Success;
    }
}
