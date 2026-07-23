using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Claim;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class IncidentAndClaimServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly IncidentReportService _incidentService;
        private readonly ClaimService _claimService;
        private readonly FakePdfGeneratorService _pdfGeneratorService;
        private readonly FakeFileService _fileService;

        public IncidentAndClaimServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _pdfGeneratorService = new FakePdfGeneratorService();
            _fileService = new FakeFileService();
            _incidentService = new IncidentReportService(
                _db,
                _pdfGeneratorService,
                _fileService,
                NullLogger<IncidentReportService>.Instance);
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
                IncidentType = IncidentType.DAMAGE_CARGO,
                Severity = IncidentSeverity.HIGH,
                Description = "Pallet tipped over during sharp turn",
                CurrentLatitude = 10.7m,
                CurrentLongitude = 106.7m,
                DriverPaidAmount = 1_250_000m
            };

            // Act
            var response = await _incidentService.ReportIncidentAsync(request, userId);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Equal("DAMAGE_CARGO", response.Data.IncidentType);
            Assert.Equal("HIGH", response.Data.Severity);
            Assert.Equal("REPORTED", response.Data.Status);
            Assert.Equal(1_250_000m, response.Data.DriverPaidAmount);
            Assert.Null(response.Data.ReimbursedAmount);
            Assert.Equal(tripId.ToString(), response.Data.TripCode);
            Assert.Equal("driver_john", response.Data.ReportedByUsername);

            var dbIncident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == response.Data.IncidentId);
            Assert.NotNull(dbIncident);
            Assert.Equal("Pallet tipped over during sharp turn", dbIncident.Description);
            Assert.Equal(1_250_000m, dbIncident.DriverPaidAmount);
        }

        [Fact]
        public async Task ResolveIncident_UpdatesStatusToResolved()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var incidentId = Guid.NewGuid();
            _db.Users.Add(new User
            {
                UserId = userId,
                Username = "incident_driver",
                PasswordHash = "hash",
                RoleId = Guid.NewGuid(),
                Email = "incident.driver@test.com",
                FullName = "Incident Driver"
            });
            var incident = new IncidentReport
            {
                IncidentId = incidentId,
                IncidentType = "ACCIDENT",
                Severity = "MEDIUM",
                Description = "Minor scratch",
                DriverPaidAmount = 800_000m,
                ApprovedAmount = 750_000m,
                ReimbursedAmount = 750_000m,
                ExpenseStatus = "REIMBURSED",
                Status = "REPORTED",
                ReportedBy = userId,
                ReportedAt = DateTime.UtcNow
            };
            _db.IncidentReports.Add(incident);
            await _db.SaveChangesAsync();

            // Act
            var response = await _incidentService.ResolveIncidentAsync(
                incidentId,
                new ResolveIncidentRequest
                {
                    ResolutionNote = "Vehicle inspected and cleared to proceed."
                },
                userId);

            // Assert
            Assert.True(response.Success);
            
            var dbIncident = await _db.IncidentReports
                .Include(i => i.IncidentEvidences)
                .FirstAsync(i => i.IncidentId == incidentId);
            Assert.Equal("RESOLVED", dbIncident!.Status);
            Assert.Equal("Vehicle inspected and cleared to proceed.", dbIncident.ResolutionNote);
            Assert.NotNull(dbIncident.ResolvedAt);
            Assert.Equal(750_000m, dbIncident.ReimbursedAmount);
            var evidence = Assert.Single(dbIncident.IncidentEvidences);
            Assert.Equal("RESOLUTION_PDF", evidence.EvidenceType);
            Assert.Equal(FakeFileService.UploadedUrl, evidence.FileUrl);
            Assert.Equal(1, _pdfGeneratorService.CallCount);
            Assert.EndsWith($"{incidentId:N}.pdf", _fileService.LastFileName);

            var getResponse = await _incidentService.GetIncidentByIdAsync(incidentId);
            Assert.True(getResponse.Success);
            Assert.NotNull(getResponse.Data);
            Assert.Equal(800_000m, getResponse.Data.DriverPaidAmount);
            Assert.Equal(750_000m, getResponse.Data.ReimbursedAmount);
            Assert.Equal(FakeFileService.UploadedUrl, Assert.Single(getResponse.Data.Evidences).FileUrl);
        }

        [Fact]
        public async Task GetPagedIncidents_ReturnsEvidenceFileUrl()
        {
            var incidentId = Guid.NewGuid();
            var reporterId = Guid.NewGuid();
            _db.Users.Add(new User
            {
                UserId = reporterId,
                Username = "paged_driver",
                PasswordHash = "hash",
                RoleId = Guid.NewGuid(),
                Email = "paged.driver@test.com",
                FullName = "Paged Driver"
            });
            var incident = new IncidentReport
            {
                IncidentId = incidentId,
                IncidentType = "VEHICLE_BREAKDOWN",
                Severity = "HIGH",
                Description = "Cooling unit stopped",
                DriverPaidAmount = 2_000_000m,
                ReimbursedAmount = 2_000_000m,
                Status = "RESOLVED",
                ReportedBy = reporterId,
                ReportedAt = DateTime.UtcNow,
                IncidentEvidences = new List<IncidentEvidence>
                {
                    new()
                    {
                        EvidenceId = Guid.NewGuid(),
                        IncidentId = incidentId,
                        EvidenceType = "RESOLUTION_PDF",
                        FileUrl = FakeFileService.UploadedUrl
                    }
                }
            };
            _db.IncidentReports.Add(incident);
            await _db.SaveChangesAsync();

            var response = await _incidentService.GetPagedIncidentsAsync(null, 1, 10);

            Assert.True(response.Success);
            var item = Assert.Single(response.Data!.Data);
            Assert.Equal(FakeFileService.UploadedUrl, Assert.Single(item.Evidences).FileUrl);
        }

        [Fact]
        public async Task ResolveIncident_WhenUploadFails_DoesNotResolveOrSaveEvidence()
        {
            var incidentId = Guid.NewGuid();
            var reporterId = Guid.NewGuid();
            _db.Users.Add(new User
            {
                UserId = reporterId,
                Username = "upload_driver",
                PasswordHash = "hash",
                RoleId = Guid.NewGuid(),
                Email = "upload.driver@test.com",
                FullName = "Upload Driver"
            });
            _db.IncidentReports.Add(new IncidentReport
            {
                IncidentId = incidentId,
                IncidentType = "ACCIDENT",
                Severity = "MEDIUM",
                Description = "Minor accident",
                DriverPaidAmount = 300_000m,
                ApprovedAmount = 300_000m,
                ReimbursedAmount = 300_000m,
                ExpenseStatus = "REIMBURSED",
                Status = "REPORTED",
                ReportedBy = reporterId,
                ReportedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            _fileService.ThrowOnUpload = true;

            var response = await _incidentService.ResolveIncidentAsync(
                incidentId,
                new ResolveIncidentRequest
                {
                    ResolutionNote = "Repair completed."
                },
                reporterId);

            Assert.False(response.Success);
            var incident = await _db.IncidentReports.FindAsync(incidentId);
            Assert.Equal("REPORTED", incident!.Status);
            Assert.Null(incident.ResolvedAt);
            Assert.Equal(300_000m, incident.ReimbursedAmount);
            Assert.Empty(await _db.IncidentEvidences.ToListAsync());
        }

        [Fact]
        public async Task IncidentFullFlow_ReportResolveAndRetrieveEvidence()
        {
            var userId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            _db.Users.Add(new User
            {
                UserId = userId,
                Username = "full_flow_driver",
                PasswordHash = "hash",
                RoleId = Guid.NewGuid(),
                Email = "full.flow.driver@test.com",
                FullName = "Full Flow Driver"
            });
            _db.MasterTrips.Add(new MasterTrip
            {
                TripId = tripId,
                Status = "DEPARTED",
                VehicleId = Guid.NewGuid(),
                PlannedStartTime = DateTime.UtcNow,
                PlannedEndTime = DateTime.UtcNow.AddHours(4),
                OriginLocationId = Guid.NewGuid(),
                DestinationLocationId = Guid.NewGuid()
            });
            await _db.SaveChangesAsync();

            var reportResult = await _incidentService.ReportIncidentAsync(
                new CreateIncidentRequest
                {
                    TripId = tripId,
                    IncidentType = IncidentType.VEHICLE_BREAKDOWN,
                    Severity = IncidentSeverity.HIGH,
                    Description = "Cooling system failed while in transit.",
                    CurrentLatitude = 10.762622m,
                    CurrentLongitude = 106.660172m,
                    DriverPaidAmount = 1_500_000m
                },
                userId);

            Assert.True(reportResult.Success);
            var incidentId = reportResult.Data!.IncidentId;
            Assert.Equal("REPORTED", reportResult.Data.Status);
            Assert.Equal(1_500_000m, reportResult.Data.DriverPaidAmount);
            Assert.Equal("PENDING_APPROVAL", reportResult.Data.ExpenseStatus);
            Assert.Empty(reportResult.Data.Evidences);

            var persistedIncident = await _db.IncidentReports.FindAsync(incidentId);
            persistedIncident!.Status = "CONTINUED";
            await _db.SaveChangesAsync();

            var approveResult = await _incidentService.ApproveExpenseAsync(
                incidentId,
                new ApproveIncidentExpenseRequest
                {
                    ApprovedAmount = 1_500_000m,
                    ApprovalNote = "Approved after receipt review."
                },
                userId);
            Assert.True(approveResult.Success);
            Assert.Equal("APPROVED", approveResult.Data!.ExpenseStatus);

            var receiptBytes = new MemoryStream(new byte[] { 1, 2, 3 });
            var receipt = new FormFile(receiptBytes, 0, receiptBytes.Length, "ReceiptFile", "receipt.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/jpeg"
            };
            var reimburseResult = await _incidentService.ReimburseExpenseAsync(
                incidentId,
                new ReimburseIncidentExpenseRequest
                {
                    ReimbursedAmount = 1_500_000m,
                    ReceiptFile = receipt,
                    Note = "Transferred to driver."
                },
                userId);
            Assert.True(reimburseResult.Success);
            Assert.Equal("REIMBURSED", reimburseResult.Data!.ExpenseStatus);

            var resolveResult = await _incidentService.ResolveIncidentAsync(
                incidentId,
                new ResolveIncidentRequest
                {
                    ResolutionNote = "Cooling unit repaired and trip cleared to continue."
                },
                userId);

            Assert.True(resolveResult.Success);
            Assert.Equal(1, _pdfGeneratorService.CallCount);
            Assert.Equal($"incident_resolution_{incidentId:N}.pdf", _fileService.LastFileName);

            var detailResult = await _incidentService.GetIncidentByIdAsync(incidentId);
            Assert.True(detailResult.Success);
            var detail = detailResult.Data!;
            Assert.Equal("RESOLVED", detail.Status);
            Assert.Equal("VEHICLE_BREAKDOWN", detail.IncidentType);
            Assert.Equal(1_500_000m, detail.DriverPaidAmount);
            Assert.Equal(1_500_000m, detail.ReimbursedAmount);
            Assert.Equal("Cooling unit repaired and trip cleared to continue.", detail.ResolutionNote);
            Assert.Contains(detail.Evidences, e => e.EvidenceType == "REIMBURSEMENT_RECEIPT");
            Assert.Contains(detail.Evidences, e => e.EvidenceType == "RESOLUTION_PDF");

            var listResult = await _incidentService.GetPagedIncidentsAsync(tripId, 1, 10);
            Assert.True(listResult.Success);
            var listItem = Assert.Single(listResult.Data!.Data);
            Assert.Equal(incidentId, listItem.IncidentId);
            Assert.Equal(2, listItem.Evidences.Count);

            var savedEvidences = await _db.IncidentEvidences.ToListAsync();
            Assert.Equal(2, savedEvidences.Count);
            Assert.All(savedEvidences, evidence => Assert.Equal(incidentId, evidence.IncidentId));
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
                OrderDimension = new ColdChainX.Core.Entities.OrderDimension { ExpectedWeightKg = 100, 
                ActualWeightKg = 100, 
                ExpectedCbm = 2.5m }, 

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

        private sealed class FakePdfGeneratorService : IPdfGeneratorService
        {
            public int CallCount { get; private set; }

            public Task<byte[]> GeneratePdfAsync<T>(string templateName, T data)
            {
                Assert.Equal("IncidentResolution", templateName);
                CallCount++;
                return Task.FromResult(new byte[] { 0x25, 0x50, 0x44, 0x46 });
            }
        }

        private sealed class FakeFileService : IFileService
        {
            public const string UploadedUrl = "https://res.cloudinary.com/coldchainx/incident-resolution.pdf";

            public bool ThrowOnUpload { get; set; }
            public string LastFileName { get; private set; } = string.Empty;

            public Task<string> UploadFileAsync(IFormFile file)
            {
                if (ThrowOnUpload)
                    throw new InvalidOperationException("Cloudinary upload failed.");

                LastFileName = file.FileName;
                return Task.FromResult(UploadedUrl);
            }

            public Task<string> UploadFileAsync(Stream stream, string fileName)
                => throw new NotSupportedException();

            public Task<string> UploadFileAsync(byte[] fileBytes, string fileName)
            {
                if (ThrowOnUpload)
                    throw new InvalidOperationException("Cloudinary upload failed.");

                LastFileName = fileName;
                return Task.FromResult(UploadedUrl);
            }

            public string GetSignedUrl(string publicId) => UploadedUrl;
        }
    }
}


