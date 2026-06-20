using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class InventoryAnalysisServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly InventoryAnalysisService _analysisService;

        public InventoryAnalysisServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _analysisService = new InventoryAnalysisService(_db, NullLogger<InventoryAnalysisService>.Instance);
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task GetExpiryAlerts_FiltersAndReturnsUpcomingExpiryStock()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var wh = new Warehouse { WarehouseId = warehouseId, WarehouseCode = "WH-EXP", WarehouseName = "Expiry WH", WarehouseType = "COLD", Address = "123 Cold St", Status = "ACTIVE", MaxPallets = 100 };
            _db.Warehouses.Add(wh);

            var zone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "Z-EXP", ZoneName = "Zone Expiry", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", MaxCapacityPallets = 50, CurrentPallets = 10 };
            _db.WarehouseZones.Add(zone);

            var loc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = zone.ZoneId, LocationCode = "LOC-EXP", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };
            _db.WarehouseLocations.Add(loc);

            // Batches:
            // 1. Expired/Near expiry (10 days from now) -> Should be flagged
            var batchNear = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-NEAR", BatchNumber = "B-NEAR", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            // 2. Far expiry (60 days from now) -> Should NOT be flagged when warningDays is 30
            var batchFar = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-FAR", BatchNumber = "B-FAR", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            _db.InventoryBatches.AddRange(batchNear, batchFar);

            var stockNear = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = loc.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-NEAR",
                ItemName = "Near Expiry Item",
                Unit = "PCS",
                BatchId = batchNear.BatchId,
                QuantityOnHand = 50m,
                PalletCount = 1,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            var stockFar = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = loc.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-FAR",
                ItemName = "Far Expiry Item",
                Unit = "PCS",
                BatchId = batchFar.BatchId,
                QuantityOnHand = 100m,
                PalletCount = 2,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _db.InventoryStocks.AddRange(stockNear, stockFar);
            await _db.SaveChangesAsync();

            // Act
            var response = await _analysisService.GetExpiryAlertsAsync(warehouseId, warningDays: 30, pageNumber: 1, pageSize: 10);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Single(response.Data.Data);
            
            var item = response.Data.Data.First();
            Assert.Equal("ITEM-NEAR", item.ItemCode);
            Assert.Equal("B-NEAR", item.BatchNumber);
            Assert.Equal(10, item.RemainingDays);
            Assert.Equal("Expiry WH", item.WarehouseName);
            Assert.Equal("LOC-EXP", item.LocationCode);
        }

        [Fact]
        public async Task GetAgingInventory_CalculatesCorrectStorageDays()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var wh = new Warehouse { WarehouseId = warehouseId, WarehouseCode = "WH-AGE", WarehouseName = "Aging WH", WarehouseType = "COLD", Address = "123 Cold St", Status = "ACTIVE", MaxPallets = 100 };
            _db.Warehouses.Add(wh);

            var zone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "Z-AGE", ZoneName = "Zone Aging", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", MaxCapacityPallets = 50, CurrentPallets = 10 };
            _db.WarehouseZones.Add(zone);

            var loc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = zone.ZoneId, LocationCode = "LOC-AGE", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };
            _db.WarehouseLocations.Add(loc);

            var batch = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-AGE", BatchNumber = "B-AGE", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(100)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            _db.InventoryBatches.Add(batch);

            // Stock 1: Inbound 45 days ago -> Should be flagged if threshold is 30 days
            var stockOld = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = loc.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-OLD",
                ItemName = "Old Stock Item",
                Unit = "PCS",
                BatchId = batch.BatchId,
                QuantityOnHand = 50m,
                PalletCount = 1,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow.AddDays(-45),
                CreatedAt = DateTime.UtcNow.AddDays(-45)
            };

            // Stock 2: Inbound 5 days ago -> Should NOT be flagged if threshold is 30 days
            var stockNew = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = loc.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-NEW",
                ItemName = "New Stock Item",
                Unit = "PCS",
                BatchId = batch.BatchId,
                QuantityOnHand = 100m,
                PalletCount = 2,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow.AddDays(-5),
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            };

            _db.InventoryStocks.AddRange(stockOld, stockNew);
            await _db.SaveChangesAsync();

            // Act
            var response = await _analysisService.GetAgingInventoryAsync(warehouseId, thresholdDays: 30, pageNumber: 1, pageSize: 10);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Single(response.Data.Data);

            var item = response.Data.Data.First();
            Assert.Equal("ITEM-OLD", item.ItemCode);
            Assert.True(item.StorageDays >= 45);
            Assert.Equal("Aging WH", item.WarehouseName);
            Assert.Equal("LOC-AGE", item.LocationCode);
        }

        [Fact]
        public async Task GetTemperatureAudits_FlagsIncompatibleStocks()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var wh = new Warehouse { WarehouseId = warehouseId, WarehouseCode = "WH-TEMP", WarehouseName = "Temp WH", WarehouseType = "COLD", Address = "123 Cold St", Status = "ACTIVE", MaxPallets = 100 };
            _db.Warehouses.Add(wh);

            // Zone: operating temp is min 10 max 15
            var hotZone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "Z-HOT", ZoneName = "Hot Zone", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", TemperatureMin = 10m, TemperatureMax = 15m, MaxCapacityPallets = 50, CurrentPallets = 10 };
            var locHot = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = hotZone.ZoneId, LocationCode = "LOC-HOT", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };
            
            // Zone: operating temp is min -20 max -15 (Freezer)
            var freezerZone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = warehouseId, ZoneCode = "Z-FREEZER", ZoneName = "Freezer Zone", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", TemperatureMin = -20m, TemperatureMax = -15m, MaxCapacityPallets = 50, CurrentPallets = 10 };
            var locFreezer = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = freezerZone.ZoneId, LocationCode = "LOC-FREEZER", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 2 };

            _db.WarehouseZones.AddRange(hotZone, freezerZone);
            _db.WarehouseLocations.AddRange(locHot, locFreezer);

            var batch = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-TEMP", BatchNumber = "B-TEMP", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(100)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            _db.InventoryBatches.Add(batch);

            // Stock 1: Requires min 2 max 8, stored in Z-HOT (10 to 15) -> Should be flagged (Too hot)
            var stockTooHot = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = locHot.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-TOO-HOT",
                ItemName = "Chilled Item",
                Unit = "PCS",
                BatchId = batch.BatchId,
                QuantityOnHand = 50m,
                PalletCount = 1,
                RequiredTempMin = 2m,
                RequiredTempMax = 8m,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            // Stock 2: Requires min 2 max 8, stored in Z-FREEZER (-20 to -15) -> Should be flagged (Too cold)
            var stockTooCold = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = locFreezer.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-TOO-COLD",
                ItemName = "Chilled Item 2",
                Unit = "PCS",
                BatchId = batch.BatchId,
                QuantityOnHand = 50m,
                PalletCount = 1,
                RequiredTempMin = 2m,
                RequiredTempMax = 8m,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            // Stock 3: Requires min -25 max -10, stored in Z-FREEZER (-20 to -15) -> Compatible, should NOT be flagged
            var stockCompatible = new InventoryStock
            {
                StockId = Guid.NewGuid(),
                LocationId = locFreezer.LocationId,
                CustomerId = customerId,
                ItemCode = "ITEM-OK",
                ItemName = "Frozen Item",
                Unit = "PCS",
                BatchId = batch.BatchId,
                QuantityOnHand = 50m,
                PalletCount = 1,
                RequiredTempMin = -25m,
                RequiredTempMax = -10m,
                Status = "AVAILABLE",
                InboundDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _db.InventoryStocks.AddRange(stockTooHot, stockTooCold, stockCompatible);
            await _db.SaveChangesAsync();

            // Act
            var response = await _analysisService.GetTemperatureAuditsAsync(warehouseId, pageNumber: 1, pageSize: 10);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Equal(2, response.Data.Data.Count);

            var list = response.Data.Data.ToList();
            Assert.Contains(list, s => s.ItemCode == "ITEM-TOO-HOT");
            Assert.Contains(list, s => s.ItemCode == "ITEM-TOO-COLD");
            Assert.DoesNotContain(list, s => s.ItemCode == "ITEM-OK");
        }

        [Fact]
        public async Task GetWarehouseUtilization_ComputesCorrectRates()
        {
            // Arrange
            var warehouseId = Guid.NewGuid();
            var wh = new Warehouse 
            { 
                WarehouseId = warehouseId, 
                WarehouseCode = "WH-UTIL", 
                WarehouseName = "Utilization WH", 
                WarehouseType = "COLD", 
                Address = "123 Cold St", 
                Status = "ACTIVE", 
                MaxPallets = 100,
                CurrentPallets = 0 
            };
            _db.Warehouses.Add(wh);

            // Zone 1: Max 50, Current 10 -> Occupancy 20% (0.2)
            var zone1 = new WarehouseZone 
            { 
                ZoneId = Guid.NewGuid(), 
                WarehouseId = warehouseId, 
                ZoneCode = "Z-1", 
                ZoneName = "Zone 1", 
                ZoneType = "STORAGE", 
                StorageType = "RACK", 
                Status = "ACTIVE", 
                MaxCapacityPallets = 50, 
                CurrentPallets = 10 
            };
            
            // Zone 2: Max 30, Current 15 -> Occupancy 50% (0.5)
            var zone2 = new WarehouseZone 
            { 
                ZoneId = Guid.NewGuid(), 
                WarehouseId = warehouseId, 
                ZoneCode = "Z-2", 
                ZoneName = "Zone 2", 
                ZoneType = "STORAGE", 
                StorageType = "RACK", 
                Status = "ACTIVE", 
                MaxCapacityPallets = 30, 
                CurrentPallets = 15 
            };

            _db.WarehouseZones.AddRange(zone1, zone2);
            await _db.SaveChangesAsync();

            // Act
            var response = await _analysisService.GetWarehouseUtilizationAsync(warehouseId);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Equal(warehouseId, response.Data.WarehouseId);
            Assert.Equal(100, response.Data.MaxPallets);
            Assert.Equal(25, response.Data.CurrentPallets); // 10 + 15
            Assert.Equal(0.25, response.Data.WarehouseOccupancyRate); // 25 / 100

            Assert.Equal(2, response.Data.ZoneOccupancyRates.Count);
            
            var z1Detail = response.Data.ZoneOccupancyRates.First(z => z.ZoneId == zone1.ZoneId);
            Assert.Equal(50, z1Detail.MaxCapacityPallets);
            Assert.Equal(10, z1Detail.CurrentPallets);
            Assert.Equal(0.2, z1Detail.ZoneOccupancyRate);

            var z2Detail = response.Data.ZoneOccupancyRates.First(z => z.ZoneId == zone2.ZoneId);
            Assert.Equal(30, z2Detail.MaxCapacityPallets);
            Assert.Equal(15, z2Detail.CurrentPallets);
            Assert.Equal(0.5, z2Detail.ZoneOccupancyRate);
        }
    }
}
