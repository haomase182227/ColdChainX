using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Repositories;
using ColdChainX.Application.Services;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class InventoryHoldServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly InventoryService _inventoryService;
        private readonly InventoryHoldRepository _holdRepository;
        private readonly InventoryHoldService _service;

        public InventoryHoldServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _inventoryService = new InventoryService(_db, NullLogger<InventoryService>.Instance);
            _holdRepository = new InventoryHoldRepository(_db);
            _service = new InventoryHoldService(
                _holdRepository,
                _db,
                _inventoryService,
                NullLogger<InventoryHoldService>.Instance
            );
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        private async Task<(Guid stockId, Guid locationId, Guid customerId, Guid batchId, InventoryStock stock)> SeedStockAsync(
            string itemCode, decimal qty, string locationCode = "LOC-01", string zoneType = "STORAGE")
        {
            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                CustomerId = customerId,
                CompanyName = "Hold Test Customer",
                TaxCode = $"TAX-{Guid.NewGuid().ToString()[..6]}",
                Email = "customer@example.com",
                Status = "ACTIVE"
            };
            _db.Customers.Add(customer);

            var warehouseId = Guid.NewGuid();
            var warehouse = new Warehouse
            {
                WarehouseId = warehouseId,
                WarehouseCode = "WH-HOLD-01",
                WarehouseName = "Hold Warehouse",
                WarehouseType = "STORAGE",
                Address = "HCM City",
                Status = "ACTIVE"
            };
            _db.Warehouses.Add(warehouse);

            var zoneId = Guid.NewGuid();
            var zone = new WarehouseZone
            {
                ZoneId = zoneId,
                WarehouseId = warehouseId,
                ZoneCode = $"ZONE-{locationCode}",
                ZoneName = "Test Zone",
                ZoneType = zoneType,
                StorageType = "RACK",
                MaxCapacityPallets = 100,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseZones.Add(zone);

            var locationId = Guid.NewGuid();
            var location = new WarehouseLocation
            {
                LocationId = locationId,
                ZoneId = zoneId,
                LocationCode = locationCode,
                MaxCapacityPallets = 10,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseLocations.Add(location);

            var batchId = Guid.NewGuid();
            var batch = new InventoryBatch
            {
                BatchId = batchId,
                ItemCode = itemCode,
                BatchNumber = "BATCH-HOLD-01",
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.InventoryBatches.Add(batch);

            var stockId = Guid.NewGuid();
            var stock = new InventoryStock
            {
                StockId = stockId,
                LocationId = locationId,
                CustomerId = customerId,
                ItemCode = itemCode,
                ItemName = "Hold Test Item",
                Unit = "BOX",
                BatchId = batchId,
                QuantityOnHand = qty,
                QuantityAllocated = 0,
                InboundDate = DateTime.UtcNow,
                Status = "AVAILABLE",
                PalletCount = 1,
                Location = location,
                Batch = batch,
                Customer = customer
            };
            _db.InventoryStocks.Add(stock);

            await _db.SaveChangesAsync();

            return (stockId, locationId, customerId, batchId, stock);
        }

        [Fact]
        public async Task CreateHold_FullQuantity_UpdatesStatusToHold()
        {
            // Arrange
            var (stockId, _, _, _, _) = await SeedStockAsync("ITEM-HOLD-1", 100.0m);
            var dto = new CreateInventoryHoldDto
            {
                StockId = stockId,
                Quantity = 100.0m,
                ReasonCode = "TEMP_EXCURSION",
                Notes = "Temp spike logged."
            };

            // Act
            var result = await _service.CreateHoldAsync(dto, Guid.NewGuid());

            // Assert
            Assert.True(result.Success);
            Assert.Equal("HOLD", result.Data.Status);
            
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.NotNull(stock);
            Assert.Equal("HOLD", stock.Status);

            var holds = await _db.InventoryHolds.ToListAsync();
            Assert.Single(holds);
            Assert.Equal(100.0m, holds[0].HoldQuantity);
            Assert.Equal("TEMP_EXCURSION", holds[0].ReasonCode);
        }

        [Fact]
        public async Task CreateHold_PartialQuantity_QuarantinesCorrectAmount()
        {
            // Arrange
            var (stockId, _, _, _, _) = await SeedStockAsync("ITEM-HOLD-2", 100.0m);
            
            // Create Quarantine Location
            var quarantineZone = new WarehouseZone
            {
                ZoneId = Guid.NewGuid(),
                WarehouseId = Guid.NewGuid(), // dummy, but needed
                ZoneCode = "QUARANTINE",
                ZoneName = "Quarantine Zone",
                ZoneType = "QUARANTINE",
                StorageType = "FLOOR",
                MaxCapacityPallets = 100,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseZones.Add(quarantineZone);

            var quarantineLocId = Guid.NewGuid();
            var quarantineLoc = new WarehouseLocation
            {
                LocationId = quarantineLocId,
                ZoneId = quarantineZone.ZoneId,
                LocationCode = "QL-STAGE-01",
                MaxCapacityPallets = 10,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseLocations.Add(quarantineLoc);
            await _db.SaveChangesAsync();

            var dto = new CreateInventoryHoldDto
            {
                StockId = stockId,
                Quantity = 40.0m,
                ReasonCode = "DAMAGED",
                Notes = "Damaged during putaway.",
                TargetQuarantineLocationId = quarantineLocId
            };

            // Act
            var result = await _service.CreateHoldAsync(dto, Guid.NewGuid());

            // Assert
            Assert.True(result.Success);

            // Source stock should be reduced to 60.0m
            var sourceStock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.NotNull(sourceStock);
            Assert.Equal(60.0m, sourceStock.QuantityOnHand);
            Assert.Equal("AVAILABLE", sourceStock.Status);

            // New quarantined stock should be created in QL-STAGE-01 with 40.0m and HOLD status
            var quarantinedStock = await _db.InventoryStocks
                .FirstOrDefaultAsync(s => s.LocationId == quarantineLocId && s.ItemCode == "ITEM-HOLD-2");
            Assert.NotNull(quarantinedStock);
            Assert.Equal(40.0m, quarantinedStock.QuantityOnHand);
            Assert.Equal("HOLD", quarantinedStock.Status);

            var holds = await _db.InventoryHolds.ToListAsync();
            Assert.Single(holds);
            Assert.Equal(quarantinedStock.StockId, holds[0].StockId);
            Assert.Equal(40.0m, holds[0].HoldQuantity);
        }

        [Fact]
        public async Task CreateHold_InsufficientAvailableQty_Fails()
        {
            // Arrange
            var (stockId, _, _, _, _) = await SeedStockAsync("ITEM-HOLD-3", 100.0m);
            
            // Allocate part of it
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            stock.QuantityAllocated = 80.0m;
            await _db.SaveChangesAsync();

            var dto = new CreateInventoryHoldDto
            {
                StockId = stockId,
                Quantity = 30.0m, // Only 20 available
                ReasonCode = "EXPIRED",
                Notes = "Testing over hold."
            };

            // Act
            var result = await _service.CreateHoldAsync(dto, Guid.NewGuid());

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Insufficient available quantity", result.Message);
        }

        [Fact]
        public async Task ReleaseHold_RestoresStatusToAvailable()
        {
            // Arrange
            var (stockId, _, _, _, _) = await SeedStockAsync("ITEM-HOLD-4", 100.0m);
            var dtoHold = new CreateInventoryHoldDto
            {
                StockId = stockId,
                Quantity = 100.0m,
                ReasonCode = "QA_QUARANTINE",
                Notes = "QA holding batch."
            };
            var resultHold = await _service.CreateHoldAsync(dtoHold, Guid.NewGuid());
            Assert.True(resultHold.Success);

            var releaseDto = new ReleaseInventoryHoldDto
            {
                ReleaseNotes = "Passed QA inspection."
            };

            // Act
            var resultRelease = await _service.ReleaseHoldAsync(resultHold.Data.HoldId, releaseDto, Guid.NewGuid());

            // Assert
            Assert.True(resultRelease.Success);

            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.NotNull(stock);
            Assert.Equal("AVAILABLE", stock.Status);

            var hold = await _db.InventoryHolds.FindAsync(resultHold.Data.HoldId);
            Assert.NotNull(hold);
            Assert.Equal("RELEASED", hold.Status);
            Assert.Equal("Passed QA inspection.", hold.ReleaseNotes);
        }

        [Fact]
        public async Task FEFO_AllocationEngine_ExcludesHeldStock()
        {
            // Arrange
            var (stockId, _, _, _, _) = await SeedStockAsync("ITEM-HOLD-5", 100.0m);

            // Put it on hold
            var dtoHold = new CreateInventoryHoldDto
            {
                StockId = stockId,
                Quantity = 100.0m,
                ReasonCode = "TEMP_EXCURSION",
                Notes = "Spike logged."
            };
            var resultHold = await _service.CreateHoldAsync(dtoHold, Guid.NewGuid());
            Assert.True(resultHold.Success);

            // Try to allocate
            var allocateRequest = new AllocateInventoryRequest
            {
                ReferenceDocumentId = Guid.NewGuid(),
                Items = new List<AllocateInventoryItemRequest>
                {
                    new AllocateInventoryItemRequest { ItemCode = "ITEM-HOLD-5", Quantity = 50.0m }
                }
            };

            // Act
            var allocateResult = await _inventoryService.AllocateStockAsync(allocateRequest, Guid.NewGuid());

            // Assert
            Assert.False(allocateResult.Success);
            Assert.Contains("Insufficient inventory", allocateResult.Message);
        }

        [Fact]
        public async Task AdjustOutHold_ResolvesHoldAndPostVariance()
        {
            // Arrange
            var (stockId, _, _, _, _) = await SeedStockAsync("ITEM-HOLD-6", 100.0m);
            var dtoHold = new CreateInventoryHoldDto
            {
                StockId = stockId,
                Quantity = 100.0m,
                ReasonCode = "DAMAGED",
                Notes = "Crushed pallets."
            };
            var resultHold = await _service.CreateHoldAsync(dtoHold, Guid.NewGuid());
            Assert.True(resultHold.Success);

            // Act
            var resultAdjust = await _service.AdjustOutHoldAsync(resultHold.Data.HoldId, "Scrapped completely", Guid.NewGuid());

            // Assert
            Assert.True(resultAdjust.Success);

            // Stock should be inactive with 0 quantity
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.NotNull(stock);
            Assert.Equal(0.0m, stock.QuantityOnHand);
            Assert.Equal("INACTIVE", stock.Status);

            var hold = await _db.InventoryHolds.FindAsync(resultHold.Data.HoldId);
            Assert.NotNull(hold);
            Assert.Equal("ADJUSTED", hold.Status);
            Assert.NotNull(hold.AdjustmentId);

            var adj = await _db.InventoryAdjustments.FindAsync(hold.AdjustmentId.Value);
            Assert.NotNull(adj);
            Assert.Equal(-100.0m, adj.QuantityChanged);
            Assert.Equal(0.0m, adj.QuantityAfter);
        }

        [Fact]
        public async Task CreateHold_PartialQuantity_CalculatesProportionalPallets()
        {
            // Arrange: Seed stock with 100 qty and 4 pallets
            var (stockId1, _, _, _, _) = await SeedStockAsync("ITEM-PALLET-CALC-1", 100.0m);
            
            var stock1 = await _db.InventoryStocks.FindAsync(stockId1);
            stock1.PalletCount = 4;
            await _db.SaveChangesAsync();

            // Create Quarantine Location
            var quarantineZone = new WarehouseZone
            {
                ZoneId = Guid.NewGuid(),
                WarehouseId = Guid.NewGuid(),
                ZoneCode = "QUAR-PALLETS",
                ZoneName = "Quarantine Zone Pallets",
                ZoneType = "QUARANTINE",
                StorageType = "FLOOR",
                MaxCapacityPallets = 100,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseZones.Add(quarantineZone);

            var quarantineLocId = Guid.NewGuid();
            var quarantineLoc = new WarehouseLocation
            {
                LocationId = quarantineLocId,
                ZoneId = quarantineZone.ZoneId,
                LocationCode = "QL-PAL-01",
                MaxCapacityPallets = 10,
                CurrentPallets = 0,
                Status = "ACTIVE"
            };
            _db.WarehouseLocations.Add(quarantineLoc);
            await _db.SaveChangesAsync();

            // Hold 25 qty -> should calculate (25 * 4 / 100) = 1 pallet
            var dto1 = new CreateInventoryHoldDto
            {
                StockId = stockId1,
                Quantity = 25.0m,
                ReasonCode = "DAMAGED",
                TargetQuarantineLocationId = quarantineLocId
            };
            var result1 = await _service.CreateHoldAsync(dto1, Guid.NewGuid());
            Assert.True(result1.Success);

            var qStock1 = await _db.InventoryStocks
                .FirstOrDefaultAsync(s => s.LocationId == quarantineLocId && s.ItemCode == "ITEM-PALLET-CALC-1");
            Assert.NotNull(qStock1);
            Assert.Equal(1, qStock1.PalletCount);

            // Arrange: Seed stock with 100 qty and 0 pallets
            var (stockId2, _, _, _, _) = await SeedStockAsync("ITEM-PALLET-CALC-2", 100.0m);
            
            var stock2 = await _db.InventoryStocks.FindAsync(stockId2);
            stock2.PalletCount = 0; // Test PalletCount = 0 edgecase
            await _db.SaveChangesAsync();

            var dto2 = new CreateInventoryHoldDto
            {
                StockId = stockId2,
                Quantity = 50.0m,
                ReasonCode = "DAMAGED",
                TargetQuarantineLocationId = quarantineLocId
            };
            var result2 = await _service.CreateHoldAsync(dto2, Guid.NewGuid());
            Assert.True(result2.Success);

            var qStock2 = await _db.InventoryStocks
                .FirstOrDefaultAsync(s => s.LocationId == quarantineLocId && s.ItemCode == "ITEM-PALLET-CALC-2");
            Assert.NotNull(qStock2);
            Assert.Equal(0, qStock2.PalletCount); // Proportional pallets should be 0 because source PalletCount was 0
        }

        [Fact]
        public async Task GetHoldById_ExistingHoldId_ReturnsHoldResponseDto()
        {
            // Arrange
            var (stockId, _, _, _, _) = await SeedStockAsync("ITEM-GET-BY-ID-1", 100.0m, "LOC-GET-BY-ID", "STORAGE");
            var dto = new CreateInventoryHoldDto
            {
                StockId = stockId,
                Quantity = 100.0m,
                ReasonCode = "TEMP_EXCURSION",
                Notes = "Testing retrieval"
            };
            var createResult = await _service.CreateHoldAsync(dto, Guid.NewGuid());
            Assert.True(createResult.Success);
            var holdId = createResult.Data.HoldId;

            // Act
            var result = await _service.GetHoldByIdAsync(holdId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(holdId, result.Data.HoldId);
            Assert.Equal(stockId, result.Data.StockId);
            Assert.Equal("ITEM-GET-BY-ID-1", result.Data.ItemCode);
            Assert.Equal("LOC-GET-BY-ID", result.Data.LocationCode);
            Assert.Equal("HOLD", result.Data.Status);
        }

        [Fact]
        public async Task GetHoldById_NonExistingHoldId_ReturnsFailure()
        {
            // Act
            var result = await _service.GetHoldByIdAsync(Guid.NewGuid());

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Hold record not found.", result.Message);
        }
    }
}
