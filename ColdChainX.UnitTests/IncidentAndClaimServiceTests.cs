using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.DTOs.Claim;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class IncidentAndClaimServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly IncidentReportService _incidentService;
        private readonly ClaimService _claimService;

        public IncidentAndClaimServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _incidentService = new IncidentReportService(_db, NullLogger<IncidentReportService>.Instance);
            _claimService = new ClaimService(_db, NullLogger<ClaimService>.Instance);
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task ReportIncident_SavesIncidentSuccessfully()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var user = new User { UserId = userId, Username = "driver_john", PasswordHash = "hash", RoleId = Guid.NewGuid(), Email = "john@test.com", FullName = "John Doe" };
            _db.Users.Add(user);

            var tripId = Guid.NewGuid();
            var trip = new MasterTrip
            {
                TripId = tripId,
                Status = "DEPARTED",
                VehicleId = Guid.NewGuid(),
                DriverId = Guid.NewGuid(),
                PlannedStartTime = DateTime.UtcNow,
                PlannedEndTime = DateTime.UtcNow.AddHours(2),
                OriginLocationId = Guid.NewGuid(),
                DestinationLocationId = Guid.NewGuid()
            };
            _db.MasterTrips.Add(trip);
            await _db.SaveChangesAsync();

            var request = new CreateIncidentRequest
            {
                TripId = tripId,
                IncidentType = "Cargo_Damage",
                Severity = "High",
                Description = "Pallet tipped over during sharp turn",
                CurrentLatitude = 10.7m,
                CurrentLongitude = 106.7m
            };

            // Act
            var response = await _incidentService.ReportIncidentAsync(request, userId);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Equal("CARGO_DAMAGE", response.Data.IncidentType);
            Assert.Equal("HIGH", response.Data.Severity);
            Assert.Equal("REPORTED", response.Data.Status);
            Assert.Equal(tripId.ToString(), response.Data.TripCode);
            Assert.Equal("driver_john", response.Data.ReportedByUsername);

            var dbIncident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == response.Data.IncidentId);
            Assert.NotNull(dbIncident);
            Assert.Equal("Pallet tipped over during sharp turn", dbIncident.Description);
        }

        [Fact]
        public async Task ResolveIncident_UpdatesStatusToResolved()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var incidentId = Guid.NewGuid();
            var incident = new IncidentReport
            {
                IncidentId = incidentId,
                IncidentType = "ACCIDENT",
                Severity = "MEDIUM",
                Description = "Minor scratch",
                Status = "REPORTED",
                ReportedBy = userId,
                ReportedAt = DateTime.UtcNow
            };
            _db.IncidentReports.Add(incident);
            await _db.SaveChangesAsync();

            // Act
            var response = await _incidentService.ResolveIncidentAsync(incidentId, "Vehicle inspected and cleared to proceed.", userId);

            // Assert
            Assert.True(response.Success);
            
            var dbIncident = await _db.IncidentReports.FindAsync(incidentId);
            Assert.Equal("RESOLVED", dbIncident!.Status);
            Assert.Contains("Vehicle inspected and cleared to proceed.", dbIncident.Description);
            Assert.NotNull(dbIncident.ResolvedAt);
        }

        [Fact]
        public async Task CreateClaim_SavesClaimAndEvidences()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var user = new User { UserId = userId, Username = "cust_jane", PasswordHash = "hash", RoleId = Guid.NewGuid(), Email = "jane@test.com", FullName = "Jane Doe" };
            _db.Users.Add(user);

            var orderId = Guid.NewGuid();
            var order = new TransportOrder 
            { 
                OrderId = orderId, 
                TrackingCode = "TRK-999", 
                CustomerId = userId, 
                Quantity = 10, 
                Status = "DELIVERED", 
                ItemName = "Meat", 
                Category = "Food", 
                PackingType = "Pallet", 
                TempCondition = "Frozen", 
                ExpectedWeightKg = 100, 
                ActualWeightKg = 100, 
                ExpectedCbm = 2.5m, 
                CargoValue = 1000, 
                CreatedAt = DateTime.UtcNow 
            };
            _db.TransportOrders.Add(order);
            await _db.SaveChangesAsync();

            var request = new CreateClaimRequest
            {
                OrderId = orderId,
                ClaimType = "Damage",
                Description = "Frozen meat thawed during transport",
                EvidenceImages = new List<string> { "http://cloud.com/img1.jpg", "http://cloud.com/img2.jpg" }
            };

            // Act
            var response = await _claimService.CreateClaimAsync(request, userId);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Equal("DAMAGE", response.Data.ClaimType);
            Assert.Equal("OPEN", response.Data.Status);
            Assert.Equal("TRK-999", response.Data.OrderTrackingCode);
            Assert.Equal(2, response.Data.Evidences.Count);
            Assert.Equal("http://cloud.com/img1.jpg", response.Data.Evidences[0].ImageUrl);
            Assert.Equal("cust_jane", response.Data.Evidences[0].UploadedByUsername);

            var dbClaim = await _db.Claims.Include(c => c.ClaimEvidences).FirstOrDefaultAsync(c => c.ClaimId == response.Data.ClaimId);
            Assert.NotNull(dbClaim);
            Assert.Equal(2, dbClaim.ClaimEvidences.Count);
        }

        [Fact]
        public async Task ResolveClaim_UpdatesStatusAndFaultOwner()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var claimId = Guid.NewGuid();
            var claim = new Claim
            {
                ClaimId = claimId,
                ClaimCode = "CLM-1234",
                ClaimType = "DAMAGE",
                Description = "Cargo spoiled",
                Status = "OPEN",
                CreatedAt = DateTime.UtcNow
            };
            _db.Claims.Add(claim);
            await _db.SaveChangesAsync();

            var request = new ResolveClaimRequest
            {
                Status = "Resolved",
                FaultOwner = "Driver",
                ResolutionNote = "Compensated customer $200"
            };

            // Act
            var response = await _claimService.ResolveClaimAsync(claimId, request, userId);

            // Assert
            Assert.True(response.Success);

            var dbClaim = await _db.Claims.FindAsync(claimId);
            Assert.Equal("RESOLVED", dbClaim!.Status);
            Assert.Equal("DRIVER", dbClaim.FaultOwner);
            Assert.Equal("Compensated customer $200", dbClaim.ResolutionNote);
            Assert.NotNull(dbClaim.ResolvedAt);
        }
    }
}
