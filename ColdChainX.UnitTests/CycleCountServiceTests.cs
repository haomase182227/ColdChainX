using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.CycleCount;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Repositories;
using ColdChainX.Application.Services;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class CycleCountServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly InventoryService _inventoryService;
        private readonly CycleCountRepository _cycleCountRepository;
        private readonly CycleCountService _service;

        private Guid _adminRoleId;
        private Guid _managerRoleId;
        private Guid _pickerRoleId;

        private Guid _managerUserId;
        private Guid _pickerUserId;

        public CycleCountServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _inventoryService = new InventoryService(_db, NullLogger<InventoryService>.Instance);
            _cycleCountRepository = new CycleCountRepository(_db);
            _service = new CycleCountService(
                _cycleCountRepository,
                _db,
                _inventoryService,
                NullLogger<CycleCountService>.Instance
            );

            SeedRolesAndUsers();
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        private void SeedRolesAndUsers()
        {
            _adminRoleId = Guid.NewGuid();
            _managerRoleId = Guid.NewGuid();
            _pickerRoleId = Guid.NewGuid();

            var adminRole = new Role { RoleId = _adminRoleId, RoleName = "Admin" };
            var managerRole = new Role { RoleId = _managerRoleId, RoleName = "Manager" };
            var pickerRole = new Role { RoleId = _pickerRoleId, RoleName = "Picker" };

            _db.Roles.AddRange(adminRole, managerRole, pickerRole);

            _managerUserId = Guid.NewGuid();
            _pickerUserId = Guid.NewGuid();

            var manager = new User
            {
                UserId = _managerUserId,
                Username = "mngr01",
                PasswordHash = "hashed",
                FullName = "Operations Manager",
                RoleId = _managerRoleId,
                Status = "ACTIVE",
                Role = managerRole
            };

            var picker = new User
            {
                UserId = _pickerUserId,
                Username = "pckr01",
                PasswordHash = "hashed",
                FullName = "Picker One",
                RoleId = _pickerRoleId,
                Status = "ACTIVE",
                Role = pickerRole
            };

            _db.Users.AddRange(manager, picker);
            _db.SaveChanges();
        }

        private async Task<(Guid warehouseId, Guid zoneId, Guid locId1, Guid locId2, Guid stockId, Guid batchId)> SeedInventoryAsync()
        {
            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                CustomerId = customerId,
                CompanyName = "Seafood Supplier",
                TaxCode = "TAX-998",
                Email = "customer@seafood.com",
                Status = "ACTIVE"
            };
            _db.Customers.Add(customer);

            var warehouseId = Guid.NewGuid();
            var warehouse = new Warehouse
            {
                WarehouseId = warehouseId,
                WarehouseCode = "WH-CYCLE-01",
                WarehouseName = "Cycle Count Warehouse",
                WarehouseType = "STORAGE",
                Address = "District 7, HCM",
                Status = "ACTIVE"
            };
            _db.Warehouses.Add(warehouse);

            var zoneId = Guid.NewGuid();
            var zone = new WarehouseZone
            {
                ZoneId = zoneId,
                WarehouseId = warehouseId,
                ZoneCode = "Z-CC-01",
                ZoneName = "Active Storage Zone",
                ZoneType = "STORAGE",
                StorageType = "RACK",
                MaxCapacityPallets = 100,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseZones.Add(zone);

            // Location 1 (Has stock)
            var locId1 = Guid.NewGuid();
            var location1 = new WarehouseLocation
            {
                LocationId = locId1,
                ZoneId = zoneId,
                LocationCode = "LOC-CC-01",
                MaxCapacityPallets = 10,
                CurrentPallets = 4,
                Status = "ACTIVE"
            };

            // Location 2 (Empty)
            var locId2 = Guid.NewGuid();
            var location2 = new WarehouseLocation
            {
                LocationId = locId2,
                ZoneId = zoneId,
                LocationCode = "LOC-CC-02",
                MaxCapacityPallets = 10,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseLocations.AddRange(location1, location2);

            var batchId = Guid.NewGuid();
            var batch = new InventoryBatch
            {
                BatchId = batchId,
                ItemCode = "ITEM-SEAFOOD-01",
                BatchNumber = "B-SF-100",
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)),
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.InventoryBatches.Add(batch);

            var stockId = Guid.NewGuid();
            var stock = new InventoryStock
            {
                StockId = stockId,
                LocationId = locId1,
                CustomerId = customerId,
                ItemCode = "ITEM-SEAFOOD-01",
                ItemName = "Premium Salmon",
                Unit = "KG",
                BatchId = batchId,
                QuantityOnHand = 500.0m,
                QuantityAllocated = 0,
                InboundDate = DateTime.UtcNow,
                Status = "AVAILABLE",
                PalletCount = 4,
                Location = location1,
                Batch = batch,
                Customer = customer
            };
            _db.InventoryStocks.Add(stock);
            await _db.SaveChangesAsync();

            return (warehouseId, zoneId, locId1, locId2, stockId, batchId);
        }

        [Fact]
        public async Task CreatePlan_SnapshotsStockAndHandlesEmptyLocations()
        {
            // Arrange
            var (whId, zoneId, locId1, locId2, stockId, batchId) = await SeedInventoryAsync();
            var dto = new CreateCycleCountPlanDto
            {
                WarehouseId = whId,
                AssignedToUserId = _pickerUserId,
                Notes = "Monthly Count Plan",
                ZoneIds = new List<Guid> { zoneId }
            };

            // Act
            var result = await _service.CreatePlanAsync(dto, _managerUserId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(CycleCountPlanStatus.ASSIGNED, result.Data.Status);
            
            // Check entries (two locations targeted: LOC-CC-01 should have stock entry, LOC-CC-02 should have EMPTY entry)
            var plan = await _db.CycleCountPlans.Include(p => p.Entries).FirstOrDefaultAsync(p => p.PlanId == result.Data.PlanId);
            Assert.NotNull(plan);
            Assert.Equal(2, plan.Entries.Count);

            var stockEntry = plan.Entries.FirstOrDefault(e => e.LocationId == locId1);
            Assert.NotNull(stockEntry);
            Assert.Equal("ITEM-SEAFOOD-01", stockEntry.ItemCode);
            Assert.Equal(500.0m, stockEntry.SystemQuantity);
            Assert.Equal(4, stockEntry.SystemPallets);
            Assert.Equal(stockId, stockEntry.StockId);

            var emptyEntry = plan.Entries.FirstOrDefault(e => e.LocationId == locId2);
            Assert.NotNull(emptyEntry);
            Assert.Equal("EMPTY", emptyEntry.ItemCode);
            Assert.Equal(0.0m, emptyEntry.SystemQuantity);
            Assert.Equal(0, emptyEntry.SystemPallets);
            Assert.Null(emptyEntry.StockId);
        }

        [Fact]
        public async Task GetPlanDetails_Picker_EnforcesBlindCount()
        {
            // Arrange
            var (whId, zoneId, _, _, _, _) = await SeedInventoryAsync();
            var dto = new CreateCycleCountPlanDto
            {
                WarehouseId = whId,
                AssignedToUserId = _pickerUserId,
                ZoneIds = new List<Guid> { zoneId }
            };
            var createResult = await _service.CreatePlanAsync(dto, _managerUserId);
            Assert.True(createResult.Success);

            // Act: Fetch as picker
            var result = await _service.GetPlanDetailsAsync(createResult.Data.PlanId, _pickerUserId);

            // Assert
            Assert.True(result.Success);
            Assert.All(result.Data.Entries, e =>
            {
                Assert.Null(e.SystemQuantity);
                Assert.Null(e.SystemPallets);
                Assert.Null(e.VarianceQuantity);
                Assert.Null(e.VariancePallets);
            });
        }

        [Fact]
        public async Task GetPlanDetails_Manager_ShowsExpectedSystemMetrics()
        {
            // Arrange
            var (whId, zoneId, _, _, _, _) = await SeedInventoryAsync();
            var dto = new CreateCycleCountPlanDto
            {
                WarehouseId = whId,
                AssignedToUserId = _pickerUserId,
                ZoneIds = new List<Guid> { zoneId }
            };
            var createResult = await _service.CreatePlanAsync(dto, _managerUserId);
            Assert.True(createResult.Success);

            // Act: Fetch as Manager
            var result = await _service.GetPlanDetailsAsync(createResult.Data.PlanId, _managerUserId);

            // Assert
            Assert.True(result.Success);
            
            var stockEntry = result.Data.Entries.FirstOrDefault(e => e.ItemCode == "ITEM-SEAFOOD-01");
            Assert.NotNull(stockEntry);
            Assert.Equal(500.0m, stockEntry.SystemQuantity);
            Assert.Equal(4, stockEntry.SystemPallets);
        }

        [Fact]
        public async Task SubmitCounts_CalculatesVarianceAndAutoApprovesMatches()
        {
            // Arrange
            var (whId, zoneId, locId1, locId2, _, _) = await SeedInventoryAsync();
            var dto = new CreateCycleCountPlanDto
            {
                WarehouseId = whId,
                AssignedToUserId = _pickerUserId,
                ZoneIds = new List<Guid> { zoneId }
            };
            var createResult = await _service.CreatePlanAsync(dto, _managerUserId);
            Assert.True(createResult.Success);
            var planId = createResult.Data.PlanId;

            // Start Counting
            var startResult = await _service.StartCountingAsync(planId, _pickerUserId);
            Assert.True(startResult.Success);

            // Query entries
            var planDetails = await _db.CycleCountPlans.Include(p => p.Entries).FirstOrDefaultAsync(p => p.PlanId == planId);
            var entry1 = planDetails.Entries.First(e => e.LocationId == locId1); // System: 500 qty, 4 pallets
            var entry2 = planDetails.Entries.First(e => e.LocationId == locId2); // System: 0 qty, 0 pallets

            var submitDto = new SubmitCycleCountsDto
            {
                Counts = new List<SubmitEntryCountDto>
                {
                    // Matching count -> Auto-approved
                    new SubmitEntryCountDto { EntryId = entry1.EntryId, CountedQuantity = 500.0m, CountedPallets = 4 },
                    // Discrepancy -> Awaiting review
                    new SubmitEntryCountDto { EntryId = entry2.EntryId, CountedQuantity = 100.0m, CountedPallets = 1, FoundItemCode = "ITEM-SEAFOOD-01" }
                }
            };

            // Act
            var submitResult = await _service.SubmitCountsAsync(planId, submitDto, _pickerUserId);

            // Assert
            Assert.True(submitResult.Success);

            var planAfter = await _db.CycleCountPlans.Include(p => p.Entries).FirstOrDefaultAsync(p => p.PlanId == planId);
            Assert.Equal(CycleCountPlanStatus.AWAITING_APPROVAL, planAfter.Status);

            var e1After = planAfter.Entries.First(e => e.EntryId == entry1.EntryId);
            Assert.Equal(CycleCountEntryStatus.APPROVED, e1After.Status);
            Assert.Equal(0m, e1After.VarianceQuantity);
            Assert.Equal(0, e1After.VariancePallets);

            var e2After = planAfter.Entries.First(e => e.EntryId == entry2.EntryId);
            Assert.Equal(CycleCountEntryStatus.COUNTED, e2After.Status);
            Assert.Equal(100.0m, e2After.VarianceQuantity);
            Assert.Equal(1, e2After.VariancePallets);
            Assert.Equal("ITEM-SEAFOOD-01", e2After.ItemCode);
        }

        [Fact]
        public async Task ReviewVariance_Approve_UpdatesExistingStock()
        {
            // Arrange
            var (whId, zoneId, locId1, _, stockId, _) = await SeedInventoryAsync();
            var dto = new CreateCycleCountPlanDto
            {
                WarehouseId = whId,
                AssignedToUserId = _pickerUserId,
                ZoneIds = new List<Guid> { zoneId }
            };
            var createResult = await _service.CreatePlanAsync(dto, _managerUserId);
            var planId = createResult.Data.PlanId;

            await _service.StartCountingAsync(planId, _pickerUserId);

            var entry = await _db.CycleCountEntries.FirstAsync(e => e.LocationId == locId1);
            var submitDto = new SubmitCycleCountsDto
            {
                Counts = new List<SubmitEntryCountDto>
                {
                    // Report 480 (variance -20 qty, -1 pallet)
                    new SubmitEntryCountDto { EntryId = entry.EntryId, CountedQuantity = 480.0m, CountedPallets = 3 }
                }
            };
            await _service.SubmitCountsAsync(planId, submitDto, _pickerUserId);

            var reviewDto = new ReviewVarianceDto
            {
                Approve = true,
                ManagerNotes = "Approve stock adjustment."
            };

            // Act
            var reviewResult = await _service.ReviewVarianceAsync(entry.EntryId, reviewDto, _managerUserId);

            // Assert
            Assert.True(reviewResult.Success);

            // Verify stock is adjusted
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.NotNull(stock);
            Assert.Equal(480.0m, stock.QuantityOnHand);
            Assert.Equal(3, stock.PalletCount);

            var entryAfter = await _db.CycleCountEntries.FindAsync(entry.EntryId);
            Assert.Equal(CycleCountEntryStatus.APPROVED, entryAfter.Status);
            Assert.NotNull(entryAfter.AdjustmentId);

            var adj = await _db.InventoryAdjustments.FindAsync(entryAfter.AdjustmentId.Value);
            Assert.NotNull(adj);
            Assert.Equal(-20.0m, adj.QuantityChanged);
            Assert.Equal(480.0m, adj.QuantityAfter);
        }

        [Fact]
        public async Task ReviewVariance_Approve_CreatesNewStockForUnexpectedFind()
        {
            // Arrange
            var (whId, zoneId, _, locId2, _, batchId) = await SeedInventoryAsync();
            var dto = new CreateCycleCountPlanDto
            {
                WarehouseId = whId,
                AssignedToUserId = _pickerUserId,
                ZoneIds = new List<Guid> { zoneId }
            };
            var createResult = await _service.CreatePlanAsync(dto, _managerUserId);
            var planId = createResult.Data.PlanId;

            await _service.StartCountingAsync(planId, _pickerUserId);

            var entry = await _db.CycleCountEntries.FirstAsync(e => e.LocationId == locId2);
            var submitDto = new SubmitCycleCountsDto
            {
                Counts = new List<SubmitEntryCountDto>
                {
                    // Picker found 150 qty of ITEM-SEAFOOD-01 in LOC-CC-02
                    new SubmitEntryCountDto 
                    { 
                        EntryId = entry.EntryId, 
                        CountedQuantity = 150.0m, 
                        CountedPallets = 2,
                        FoundItemCode = "ITEM-SEAFOOD-01",
                        FoundBatchId = batchId
                    }
                }
            };
            await _service.SubmitCountsAsync(planId, submitDto, _pickerUserId);

            var reviewDto = new ReviewVarianceDto
            {
                Approve = true,
                ManagerNotes = "Approve new stock find."
            };

            // Act
            var reviewResult = await _service.ReviewVarianceAsync(entry.EntryId, reviewDto, _managerUserId);

            // Assert
            Assert.True(reviewResult.Success);

            // Verify a new stock is created in LOC-CC-02
            var newStock = await _db.InventoryStocks
                .FirstOrDefaultAsync(s => s.LocationId == locId2 && s.ItemCode == "ITEM-SEAFOOD-01");
            
            Assert.NotNull(newStock);
            Assert.Equal(150.0m, newStock.QuantityOnHand);
            Assert.Equal(2, newStock.PalletCount);
            Assert.Equal("AVAILABLE", newStock.Status);

            var entryAfter = await _db.CycleCountEntries.FindAsync(entry.EntryId);
            Assert.Equal(CycleCountEntryStatus.APPROVED, entryAfter.Status);
            Assert.Equal(newStock.StockId, entryAfter.StockId);
            Assert.NotNull(entryAfter.AdjustmentId);

            var adj = await _db.InventoryAdjustments.FindAsync(entryAfter.AdjustmentId.Value);
            Assert.NotNull(adj);
            Assert.Equal(150.0m, adj.QuantityChanged);
            Assert.Equal(150.0m, adj.QuantityAfter);
        }

        [Fact]
        public async Task ReviewVariance_Reject_KeepsInventoryIntact()
        {
            // Arrange
            var (whId, zoneId, locId1, _, stockId, _) = await SeedInventoryAsync();
            var dto = new CreateCycleCountPlanDto
            {
                WarehouseId = whId,
                AssignedToUserId = _pickerUserId,
                ZoneIds = new List<Guid> { zoneId }
            };
            var createResult = await _service.CreatePlanAsync(dto, _managerUserId);
            var planId = createResult.Data.PlanId;

            await _service.StartCountingAsync(planId, _pickerUserId);

            var entry = await _db.CycleCountEntries.FirstAsync(e => e.LocationId == locId1);
            var submitDto = new SubmitCycleCountsDto
            {
                Counts = new List<SubmitEntryCountDto>
                {
                    new SubmitEntryCountDto { EntryId = entry.EntryId, CountedQuantity = 400.0m, CountedPallets = 3 }
                }
            };
            await _service.SubmitCountsAsync(planId, submitDto, _pickerUserId);

            var reviewDto = new ReviewVarianceDto
            {
                Approve = false,
                ManagerNotes = "Count seems incorrect. Recount required."
            };

            // Act
            var reviewResult = await _service.ReviewVarianceAsync(entry.EntryId, reviewDto, _managerUserId);

            // Assert
            Assert.True(reviewResult.Success);

            // Verify stock is NOT adjusted
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.NotNull(stock);
            Assert.Equal(500.0m, stock.QuantityOnHand);
            Assert.Equal(4, stock.PalletCount);

            var entryAfter = await _db.CycleCountEntries.FindAsync(entry.EntryId);
            Assert.Equal(CycleCountEntryStatus.REJECTED, entryAfter.Status);
            Assert.Null(entryAfter.AdjustmentId);
        }
    }
}
