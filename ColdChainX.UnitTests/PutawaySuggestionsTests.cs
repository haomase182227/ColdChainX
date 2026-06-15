using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class PutawaySuggestionsTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly InventoryService _inventoryService;

        public PutawaySuggestionsTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _inventoryService = new InventoryService(_db, NullLogger<InventoryService>.Instance);
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task GetPutawaySuggestions_ExcludesIncompatibleTemperatureAndCapacityExceeded()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var wh = new Warehouse { WarehouseId = warehouseId, WarehouseCode = "WH-01", WarehouseName = "Main WH", WarehouseType = "COLD", Address = "Add", Status = "ACTIVE" };
            _db.Warehouses.Add(wh);

            // Staging zone and location
            var stageZone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "STAGE", ZoneName = "Stage Zone", ZoneType = "RECEIVING", StorageType = "FLOOR", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };
            var stageLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = stageZone.ZoneId, LocationCode = "RCV-STAGE-01", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };
            _db.WarehouseZones.Add(stageZone);
            _db.WarehouseLocations.Add(stageLoc);

            // 1. Incompatible temperature zone (Too hot: min 10 max 15, stock requires min 2 max 8)
            var hotZone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "HOT", ZoneName = "Hot Zone", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", TemperatureMin = 10m, TemperatureMax = 15m, MaxCapacityPallets = 10, CurrentPallets = 0 };
            var hotLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = hotZone.ZoneId, LocationCode = "LOC-HOT", Status = "ACTIVE", MaxCapacityPallets = 5, CurrentPallets = 0 };
            _db.WarehouseZones.Add(hotZone);
            _db.WarehouseLocations.Add(hotLoc);

            // 2. Compatible temperature zone (min 2 max 8) but location capacity exceeded
            var coldZone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "COLD", ZoneName = "Cold Zone", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", TemperatureMin = 2m, TemperatureMax = 8m, MaxCapacityPallets = 10, CurrentPallets = 1 };
            var fullLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = coldZone.ZoneId, LocationCode = "LOC-FULL", Status = "ACTIVE", MaxCapacityPallets = 2, CurrentPallets = 2 }; // Already full
            _db.WarehouseZones.Add(coldZone);
            _db.WarehouseLocations.Add(fullLoc);

            // 3. Compatible zone and location with capacity
            var validLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = coldZone.ZoneId, LocationCode = "LOC-VALID", Status = "ACTIVE", MaxCapacityPallets = 5, CurrentPallets = 1 };
            _db.WarehouseLocations.Add(validLoc);

            // Seed Batch
            var batch = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-A", BatchNumber = "B-001", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            _db.InventoryBatches.Add(batch);

            // Seed Stock in receiving/staging
            var stockId = Guid.NewGuid();
            var stock = new InventoryStock
            {
                StockId = stockId,
                LocationId = stageLoc.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-A",
                ItemName = "Item A",
                Unit = "PCS",
                BatchId = batch.BatchId,
                QuantityOnHand = 100m,
                PalletCount = 1,
                RequiredTempMin = 2m,
                RequiredTempMax = 8m,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _db.InventoryStocks.Add(stock);
            await _db.SaveChangesAsync();

            // Act
            var response = await _inventoryService.GetPutawaySuggestionsAsync(stockId);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            
            // Should exclude hotLoc (temp mismatch) and fullLoc (capacity limit). Only validLoc should remain.
            Assert.Single(response.Data);
            Assert.Equal(validLoc.LocationId, response.Data[0].LocationId);
        }

        [Fact]
        public async Task GetPutawaySuggestions_RanksSuggestionsByPriority()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var wh = new Warehouse { WarehouseId = warehouseId, WarehouseCode = "WH-01", WarehouseName = "Main WH", WarehouseType = "COLD", Address = "Add", Status = "ACTIVE" };
            _db.Warehouses.Add(wh);

            var coldZone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "COLD", ZoneName = "Cold Zone", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", TemperatureMin = 2m, TemperatureMax = 8m, MaxCapacityPallets = 100, CurrentPallets = 10 };
            _db.WarehouseZones.Add(coldZone);

            var stageZone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "STAGE", ZoneName = "Stage Zone", ZoneType = "RECEIVING", StorageType = "FLOOR", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };
            var stageLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = stageZone.ZoneId, LocationCode = "RCV-STAGE-01", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };
            _db.WarehouseZones.Add(stageZone);
            _db.WarehouseLocations.Add(stageLoc);

            // Locations to test priority ranking
            // Rank 1: Same batch consolidation (100)
            var sameBatchLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = coldZone.ZoneId, LocationCode = "LOC-SAME-BATCH", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 1 };
            // Rank 2: Same item consolidation (80)
            var sameItemLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = coldZone.ZoneId, LocationCode = "LOC-SAME-ITEM", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 1 };
            // Rank 3: Empty location (50)
            var emptyLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = coldZone.ZoneId, LocationCode = "LOC-EMPTY", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 0 };
            // Rank 4: Compatible location with other items (20)
            var otherLoc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = coldZone.ZoneId, LocationCode = "LOC-OTHER", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };

            _db.WarehouseLocations.AddRange(sameBatchLoc, sameItemLoc, emptyLoc, otherLoc);

            // Seed Batches
            var batchA = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-A", BatchNumber = "B-001", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            var batchB = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-A", BatchNumber = "B-002", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            var batchC = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-OTHER", BatchNumber = "B-999", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            _db.InventoryBatches.AddRange(batchA, batchB, batchC);

            // Seed Stocks on destination locations
            var stockOnSameBatch = new InventoryStock { StockId = Guid.NewGuid(), LocationId = sameBatchLoc.LocationId, CustomerId = customerId, ItemCode = "ITEM-A", ItemName = "Item A", Unit = "PCS", BatchId = batchA.BatchId, QuantityOnHand = 10m, PalletCount = 1, Status = "AVAILABLE", InboundDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            var stockOnSameItem = new InventoryStock { StockId = Guid.NewGuid(), LocationId = sameItemLoc.LocationId, CustomerId = customerId, ItemCode = "ITEM-A", ItemName = "Item A", Unit = "PCS", BatchId = batchB.BatchId, QuantityOnHand = 10m, PalletCount = 1, Status = "AVAILABLE", InboundDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            var stockOnOther = new InventoryStock { StockId = Guid.NewGuid(), LocationId = otherLoc.LocationId, CustomerId = customerId, ItemCode = "ITEM-OTHER", ItemName = "Other Item", Unit = "PCS", BatchId = batchC.BatchId, QuantityOnHand = 10m, PalletCount = 2, Status = "AVAILABLE", InboundDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            _db.InventoryStocks.AddRange(stockOnSameBatch, stockOnSameItem, stockOnOther);

            // Seed Stock in receiving/staging to suggest putaway for (requires cold temperature range 2 to 8)
            var targetStockId = Guid.NewGuid();
            var targetStock = new InventoryStock
            {
                StockId = targetStockId,
                LocationId = stageLoc.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-A",
                ItemName = "Item A",
                Unit = "PCS",
                BatchId = batchA.BatchId, // Batch A
                QuantityOnHand = 50m,
                PalletCount = 1,
                RequiredTempMin = 2m,
                RequiredTempMax = 8m,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _db.InventoryStocks.Add(targetStock);
            await _db.SaveChangesAsync();

            // Act
            var response = await _inventoryService.GetPutawaySuggestionsAsync(targetStockId);

            // Assert
            Assert.True(response.Success);
            var items = response.Data;
            Assert.Equal(4, items.Count);

            // Rank 1: SAME_BATCH (Score 100)
            Assert.Equal("SAME_BATCH", items[0].MatchType);
            Assert.Equal(100, items[0].SuitabilityScore);
            Assert.Equal(sameBatchLoc.LocationId, items[0].LocationId);

            // Rank 2: SAME_ITEM (Score 80)
            Assert.Equal("SAME_ITEM", items[1].MatchType);
            Assert.Equal(80, items[1].SuitabilityScore);
            Assert.Equal(sameItemLoc.LocationId, items[1].LocationId);

            // Rank 3: EMPTY (Score 50)
            Assert.Equal("EMPTY", items[2].MatchType);
            Assert.Equal(50, items[2].SuitabilityScore);
            Assert.Equal(emptyLoc.LocationId, items[2].LocationId);

            // Rank 4: COMPATIBLE (Score 20)
            Assert.Equal("COMPATIBLE", items[3].MatchType);
            Assert.Equal(20, items[3].SuitabilityScore);
            Assert.Equal(otherLoc.LocationId, items[3].LocationId);
        }
    }
}
