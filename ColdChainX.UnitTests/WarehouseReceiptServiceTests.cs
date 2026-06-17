using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class WarehouseReceiptServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly MockWarehouseReceiptRepository _receiptRepository;
        private readonly MockLocationService _locationService;
        private readonly MockPdfService _pdfService;
        private readonly MockWebHostEnvironment _environment;
        private readonly MockWarehouseAttachmentRepository _attachmentRepository;
        private readonly ComplianceRulesEngine _complianceEngine;
        private readonly WarehouseReceiptService _service;
        private readonly string _tempPath;

        public WarehouseReceiptServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _receiptRepository = new MockWarehouseReceiptRepository(_db);
            _locationService = new MockLocationService();
            _pdfService = new MockPdfService();

            _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path.Combine(_tempPath, "Templates"));
            File.WriteAllText(Path.Combine(_tempPath, "Templates", "WarehouseReceiptTemplate.html"), "<html><body>{{Receipt_Code}}</body></html>");

            _environment = new MockWebHostEnvironment { ContentRootPath = _tempPath };
            _attachmentRepository = new MockWarehouseAttachmentRepository();
            _complianceEngine = new ComplianceRulesEngine();

            _service = new WarehouseReceiptService(
                _db,
                _receiptRepository,
                _locationService,
                _pdfService,
                _environment,
                _attachmentRepository,
                _complianceEngine
            );
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
            }
            _db.Dispose();
        }

        private async Task<(Guid orderId, Guid warehouseId, Guid receiverId, Guid customerId, WarehouseReceipt receipt, WarehouseReceiptItem item)> SeedBaseDataAsync(ProductCategory category, string countryOfOrigin = "Vietnam")
        {
            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                CustomerId = customerId,
                CompanyName = "Test Customer Company",
                TaxCode = $"TAX-{Guid.NewGuid().ToString()[..6]}",
                Email = "customer@example.com",
                Status = "ACTIVE"
            };
            _db.Customers.Add(customer);

            var locationId = Guid.NewGuid();
            var location = new Location
            {
                LocationId = locationId,
                Address = "District 1, Ho Chi Minh City",
                Latitude = 10.762622m,
                Longitude = 106.660172m,
                Status = "ACTIVE"
            };
            _db.Locations.Add(location);

            var receiverId = Guid.NewGuid();
            var receiver = new User
            {
                UserId = receiverId,
                FullName = "Receiver Clerk",
                Email = "clerk@coldchain.com",
                Username = $"clerk-{Guid.NewGuid().ToString()[..6]}",
                PasswordHash = "hashed",
                Role = new Role { RoleId = Guid.NewGuid(), RoleName = "WarehouseOperator" }
            };
            _db.Users.Add(receiver);

            var warehouseId = Guid.NewGuid();
            var warehouse = new Warehouse
            {
                WarehouseId = warehouseId,
                WarehouseCode = "WH-HCM-01",
                WarehouseName = "HCM Central Warehouse",
                WarehouseType = "STORAGE",
                Address = "HCM City",
                Status = "ACTIVE"
            };
            _db.Warehouses.Add(warehouse);

            var orderId = Guid.NewGuid();
            var order = new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = $"TRK-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                CustomerId = customerId,
                ItemName = "Seafood Cargo",
                PackingType = "PALLET",
                DestLocation = locationId,
                DestLocationNavigation = location,
                Category = category.ToString(),
                Quantity = 10,
                TempCondition = "2 to 8",
                Status = "ASSIGNED"
            };
            _db.TransportOrders.Add(order);

            var receiptId = Guid.NewGuid();
            var receipt = new WarehouseReceipt
            {
                ReceiptId = receiptId,
                ReceiptCode = $"REC-{Guid.NewGuid().ToString()[..8].ToUpper()}",
                OrderId = orderId,
                WarehouseId = warehouseId,
                ReceiptType = "INBOUND",
                ReceiverId = receiverId,
                DelivererName = "Deliverer Courier",
                TotalExpectedQty = 10,
                ReferenceDocNo = "PENDING_COMPLETE"
            };
            _db.WarehouseReceipts.Add(receipt);

            var item = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ReceiptId = receiptId,
                ItemName = "Compliance Test Item",
                ItemCode = "ITEM-001",
                Unit = "BOX",
                ExpectedQty = 10,
                ActualQty = 10,
                ActualWeightKg = 5.0m,
                LengthCm = 20,
                WidthCm = 20,
                HeightCm = 20,
                ConditionStatus = "GOOD",
                ProductCategory = category,
                CountryOfOrigin = countryOfOrigin,
                BatchNumber = "BATCH-DEFAULT",
                ManufacturedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(50))
            };
            _db.WarehouseReceiptItems.Add(item);

            await _db.SaveChangesAsync();

            return (orderId, warehouseId, receiverId, customerId, receipt, item);
        }

        [Fact]
        public async Task CompleteInbound_MissingDocument_BlocksCompletion()
        {
            // Arrange
            var (orderId, _, _, _, receipt, _) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Inbound completion blocked due to compliance validation failure.", result.Message);
            Assert.Contains("Missing:", result.Message);
            Assert.Contains("QUARANTINE_CERTIFICATE", result.Message);
        }

        [Fact]
        public async Task CompleteInbound_PendingDocument_BlocksCompletion()
        {
            // Arrange
            var (orderId, _, _, _, receipt, _) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            var att = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                WarehouseReceiptId = receipt.ReceiptId,
                FileName = "quarantine.pdf",
                FilePath = "/uploads/quarantine.pdf",
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.QUARANTINE_CERTIFICATE,
                Status = DocumentStatus.PENDING
            };
            await _attachmentRepository.AddAttachmentAsync(att);

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Inbound completion blocked due to compliance validation failure.", result.Message);
            Assert.Contains("Pending:", result.Message);
            Assert.Contains("QUARANTINE_CERTIFICATE", result.Message);
        }

        [Fact]
        public async Task CompleteInbound_RejectedDocument_BlocksCompletion()
        {
            // Arrange
            var (orderId, _, _, _, receipt, _) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            var att = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                WarehouseReceiptId = receipt.ReceiptId,
                FileName = "quarantine.pdf",
                FilePath = "/uploads/quarantine.pdf",
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.QUARANTINE_CERTIFICATE,
                Status = DocumentStatus.REJECTED
            };
            await _attachmentRepository.AddAttachmentAsync(att);

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Inbound completion blocked due to compliance validation failure.", result.Message);
            Assert.Contains("Failed:", result.Message);
            Assert.Contains("QUARANTINE_CERTIFICATE", result.Message);
        }

        [Fact]
        public async Task CompleteInbound_SuccessfulCompliance_AllowsCompletion()
        {
            // Arrange
            var (orderId, _, _, _, receipt, _) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            var att = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                WarehouseReceiptId = receipt.ReceiptId,
                FileName = "quarantine.pdf",
                FilePath = "/uploads/quarantine.pdf",
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.QUARANTINE_CERTIFICATE,
                Status = DocumentStatus.VERIFIED
            };
            await _attachmentRepository.AddAttachmentAsync(att);

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.True(result.Success);
            
            // Verify status updated
            var updatedReceipt = await _db.WarehouseReceipts.FindAsync(receipt.ReceiptId);
            Assert.NotNull(updatedReceipt);
            Assert.Equal("COMPLETED", updatedReceipt.ReferenceDocNo);
        }

        [Fact]
        public async Task CompleteInbound_ComplianceFailure_NoInventoryBatchCreated()
        {
            // Arrange
            var (orderId, _, _, _, _, _) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.False(result.Success);
            var batches = await _db.InventoryBatches.ToListAsync();
            Assert.Empty(batches);
        }

        [Fact]
        public async Task CompleteInbound_ComplianceFailure_NoInventoryStockCreated()
        {
            // Arrange
            var (orderId, _, _, _, _, _) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.False(result.Success);
            var stocks = await _db.InventoryStocks.ToListAsync();
            Assert.Empty(stocks);
        }

        [Fact]
        public async Task CompleteInbound_ComplianceFailure_NoInventoryMovementCreated()
        {
            // Arrange
            var (orderId, _, _, _, _, _) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.False(result.Success);
            var movements = await _db.InventoryMovements.ToListAsync();
            Assert.Empty(movements);
        }

        [Fact]
        public async Task CompleteInbound_ComplianceFailure_ExistingStockRemainsUnchanged()
        {
            // Arrange
            var (orderId, warehouseId, _, customerId, receipt, item) = await SeedBaseDataAsync(ProductCategory.SEAFOOD);

            // Set up pre-existing receiving zone and location
            var receivingZone = new WarehouseZone
            {
                ZoneId = Guid.NewGuid(),
                WarehouseId = warehouseId,
                ZoneCode = "RECEIVING",
                ZoneName = "Receiving Stage Zone",
                ZoneType = "RECEIVING",
                StorageType = "FLOOR",
                MaxCapacityPallets = 1000,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseZones.Add(receivingZone);

            var receivingLocation = new WarehouseLocation
            {
                LocationId = Guid.NewGuid(),
                ZoneId = receivingZone.ZoneId,
                LocationCode = "RCV-STAGE-01",
                MaxCapacityPallets = 1000,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseLocations.Add(receivingLocation);

            // Create existing batch
            var batch = new InventoryBatch
            {
                BatchId = Guid.NewGuid(),
                ItemCode = item.ItemCode,
                BatchNumber = "TEST-BATCH-123",
                Status = "ACTIVE",
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
            };
            _db.InventoryBatches.Add(batch);

            // Set up pre-existing stock record
            var stock = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = receivingLocation.LocationId,
                CustomerId = customerId,
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                Unit = item.Unit,
                BatchId = batch.BatchId,
                QuantityOnHand = 100.0m, // Existing quantity
                QuantityAllocated = 0,
                InboundDate = DateTime.UtcNow.AddDays(-5),
                Status = "AVAILABLE",
                PalletCount = 2
            };
            _db.InventoryStocks.Add(stock);

            // Update item to match the existing batch
            item.BatchNumber = "TEST-BATCH-123";
            _db.WarehouseReceiptItems.Update(item);
            await _db.SaveChangesAsync();

            // Act
            var result = await _service.CompleteInboundAsync(orderId);

            // Assert
            Assert.False(result.Success);

            // Reload existing stock from db to verify it remains unchanged
            var reloadedStock = await _db.InventoryStocks.FindAsync(stock.StockId);
            Assert.NotNull(reloadedStock);
            Assert.Equal(100.0m, reloadedStock.QuantityOnHand); // Quantity must not change on compliance failure
        }

        [Fact]
        public async Task UpdateMeasurements_ValidRequest_PersistsMeasurementsCorrectly()
        {
            // Arrange
            var (orderId, warehouseId, receiverId, customerId, receipt, _) = await SeedBaseDataAsync(ProductCategory.FOOD);
            
            // Set the receipt state to pending measurement (as required by the service logic)
            receipt.ReferenceDocNo = "PENDING_MEASUREMENT";
            _db.WarehouseReceipts.Update(receipt);
            await _db.SaveChangesAsync();

            var request = new UpdateMeasurementsRequest
            {
                Items = new List<InboundItemMeasurement>
                {
                    new InboundItemMeasurement
                    {
                        ItemName = "Seafood Box A",
                        ItemCode = "ITEM-SF-001",
                        Unit = "BOX",
                        ActualQty = 15.0m,
                        LengthCm = 30,
                        WidthCm = 30,
                        HeightCm = 30,
                        WeightKg = 12.5m,
                        ConditionStatus = "GOOD",
                        Note = "Slightly wet",
                        BatchNumber = "BATCH-SF-999",
                        ManufacturedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
                        CountryOfOrigin = "Norway",
                        ProductCategory = ProductCategory.SEAFOOD
                    }
                }
            };

            // Act
            var result = await _service.UpdateMeasurementsAsync(orderId, request);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("PENDING_COMPLETE", result.Data.ReferenceDocNo);
            Assert.Equal(15.0m, result.Data.TotalActualQty);

            // Verify stored items in the DB
            var storedItems = await _db.WarehouseReceiptItems
                .Where(i => i.ReceiptId == receipt.ReceiptId)
                .ToListAsync();

            Assert.Single(storedItems);
            var storedItem = storedItems[0];
            Assert.Equal("Seafood Box A", storedItem.ItemName);
            Assert.Equal("Norway", storedItem.CountryOfOrigin);
            Assert.Equal(ProductCategory.SEAFOOD, storedItem.ProductCategory);
        }
    }

    #region Mock Classes

    public class MockWarehouseReceiptRepository : IWarehouseReceiptRepository
    {
        private readonly ApplicationDbContext _db;
        public MockWarehouseReceiptRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<WarehouseReceipt?> GetByIdAsync(Guid receiptId) 
            => await _db.WarehouseReceipts.FindAsync(receiptId);

        public async Task<WarehouseReceipt?> GetByOrderIdAsync(Guid orderId) 
            => await _db.WarehouseReceipts.FirstOrDefaultAsync(r => r.OrderId == orderId);

        public async Task<List<WarehouseReceipt>> GetActiveReceiptsByWarehouseIdAsync(Guid warehouseId) 
            => await _db.WarehouseReceipts.Where(r => r.WarehouseId == warehouseId).ToListAsync();

        public async Task<List<WarehouseReceiptItem>> GetReceiptItemsByItemCodesAsync(IEnumerable<string> itemCodes) 
            => await _db.WarehouseReceiptItems.Where(i => itemCodes.Contains(i.ItemCode)).ToListAsync();

        public async Task AddAsync(WarehouseReceipt receipt) 
            => await _db.WarehouseReceipts.AddAsync(receipt);

        public async Task AddItemAsync(WarehouseReceiptItem item) 
            => await _db.WarehouseReceiptItems.AddAsync(item);

        public async Task SaveChangesAsync() 
            => await _db.SaveChangesAsync();
    }

    public class MockLocationService : ILocationService
    {
        public Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText) => Task.FromResult((0.0m, 0.0m));
        public Task<decimal> GetDistanceKmAsync(decimal originLat, decimal originLon, decimal destinationLat, decimal destinationLon) => Task.FromResult(0.0m);
    }

    public class MockPdfService : IPdfService
    {
        public Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber) => Task.FromResult("http://test.com/contract.pdf");
        public Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber) => Task.FromResult("http://test.com/quote.pdf");
        public Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode) => Task.FromResult("http://test.com/receipt.pdf");
    }

    public class MockWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;//
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "ColdChainX";
        public string EnvironmentName { get; set; } = "Development";
    }

    public class MockWarehouseAttachmentRepository : IWarehouseAttachmentRepository
    {
        public List<WarehouseEvidenceAttachment> Attachments { get; } = new();

        public Task<WarehouseEvidenceAttachment?> GetByIdAsync(Guid attachmentId) 
            => Task.FromResult(Attachments.FirstOrDefault(a => a.AttachmentId == attachmentId));

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptIdAsync(Guid receiptId)
            => Task.FromResult(Attachments.Where(a => a.WarehouseReceiptId == receiptId).ToList());

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdAsync(Guid receiptItemId)
            => Task.FromResult(Attachments.Where(a => a.WarehouseReceiptItemId == receiptItemId).ToList());

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdsAsync(IEnumerable<Guid> receiptItemIds)
            => Task.FromResult(Attachments.Where(a => a.WarehouseReceiptItemId.HasValue && receiptItemIds.Contains(a.WarehouseReceiptItemId.Value)).ToList());

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByAdjustmentIdAsync(Guid adjustmentId)
            => Task.FromResult(new List<WarehouseEvidenceAttachment>());

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByOutboundOrderIdAsync(Guid outboundOrderId)
            => Task.FromResult(new List<WarehouseEvidenceAttachment>());

        public Task<OutboundOrder?> GetOutboundOrderWithItemsAsync(Guid outboundOrderId)
            => Task.FromResult<OutboundOrder?>(null);

        public Task AddAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            Attachments.Add(attachment);
            return Task.CompletedTask;
        }

        public Task UpdateAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            var idx = Attachments.FindIndex(a => a.AttachmentId == attachment.AttachmentId);
            if (idx >= 0) Attachments[idx] = attachment;
            else Attachments.Add(attachment);
            return Task.CompletedTask;
        }

        public Task DeleteAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            Attachments.Remove(attachment);
            return Task.CompletedTask;
        }

        public Task AddAuditHistoryAsync(AttachmentAuditHistory history) => Task.CompletedTask;

        public Task<List<AttachmentAuditHistory>> GetAuditHistoryByAttachmentIdAsync(Guid attachmentId)
            => Task.FromResult(new List<AttachmentAuditHistory>());

        public Task<List<ComplianceZoningRule>> GetComplianceZoningRulesAsync()
            => Task.FromResult(new List<ComplianceZoningRule>());

        public Task<List<ComplianceZoningRule>> GetComplianceZoningRulesByCategoryAsync(ProductCategory category)
            => Task.FromResult(new List<ComplianceZoningRule>());

        public Task AddComplianceZoningRuleAsync(ComplianceZoningRule rule) => Task.CompletedTask;
        public Task UpdateComplianceZoningRuleAsync(ComplianceZoningRule rule) => Task.CompletedTask;
        public Task SaveChangesAsync() => Task.CompletedTask;
    }

    #endregion
}
