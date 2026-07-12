using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
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

            var orderNear = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "ITEM-NEAR", ItemName = "Near Expiry Item", PackingType = "BOX", Category = "FOOD", Quantity = 10, TempCondition = "COLD", Status = "ASSIGNED" };
            var orderFar = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "ITEM-FAR", ItemName = "Far Expiry Item", PackingType = "BOX", Category = "FOOD", Quantity = 10, TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.AddRange(orderNear, orderFar);

            var receiptNear = new WarehouseReceipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-NEAR", OrderId = orderNear.OrderId, WarehouseId = warehouseId, ReceiptType = "INBOUND", DelivererName = "DelNear", CreatedAt = DateTime.UtcNow };
            var receiptFar = new WarehouseReceipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-FAR", OrderId = orderFar.OrderId, WarehouseId = warehouseId, ReceiptType = "INBOUND", DelivererName = "DelFar", CreatedAt = DateTime.UtcNow };
            _db.WarehouseReceipts.AddRange(receiptNear, receiptFar);

            var lpnNear = new Lpn
            {
                LpnId = Guid.NewGuid(),
                LpnCode = "LPN-NEAR",
                OrderId = orderNear.OrderId,
                ReceiptId = receiptNear.ReceiptId,
                Quantity = 50,
                State = LpnState.IN_STOCK,
                SlaDeadline = DateTime.UtcNow.AddDays(10),
                StorageLocation = "LOC-EXP",
                CreatedAt = DateTime.UtcNow
            };

            var lpnFar = new Lpn
            {
                LpnId = Guid.NewGuid(),
                LpnCode = "LPN-FAR",
                OrderId = orderFar.OrderId,
                ReceiptId = receiptFar.ReceiptId,
                Quantity = 100,
                State = LpnState.IN_STOCK,
                SlaDeadline = DateTime.UtcNow.AddDays(60),
                StorageLocation = "LOC-EXP",
                CreatedAt = DateTime.UtcNow
            };

            _db.Lpns.AddRange(lpnNear, lpnFar);
            await _db.SaveChangesAsync();

            // Act
            var response = await _analysisService.GetExpiryAlertsAsync(warehouseId, warningDays: 30, pageNumber: 1, pageSize: 10);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Single(response.Data.Data);
            
            var item = response.Data.Data.First();
            Assert.Equal("ITEM-NEAR", item.ItemCode);
            Assert.Equal("N/A", item.BatchNumber);
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

            var orderOld = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "ITEM-OLD", ItemName = "Old Stock Item", PackingType = "BOX", Category = "FOOD", Quantity = 10, TempCondition = "COLD", Status = "ASSIGNED" };
            var orderNew = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "ITEM-NEW", ItemName = "New Stock Item", PackingType = "BOX", Category = "FOOD", Quantity = 10, TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.AddRange(orderOld, orderNew);

            var receiptOld = new WarehouseReceipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-OLD", OrderId = orderOld.OrderId, WarehouseId = warehouseId, ReceiptType = "INBOUND", DelivererName = "DelOld", CreatedAt = DateTime.UtcNow.AddDays(-45) };
            var receiptNew = new WarehouseReceipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-NEW", OrderId = orderNew.OrderId, WarehouseId = warehouseId, ReceiptType = "INBOUND", DelivererName = "DelNew", CreatedAt = DateTime.UtcNow.AddDays(-5) };
            _db.WarehouseReceipts.AddRange(receiptOld, receiptNew);

            var lpnOld = new Lpn
            {
                LpnId = Guid.NewGuid(),
                LpnCode = "LPN-OLD",
                OrderId = orderOld.OrderId,
                ReceiptId = receiptOld.ReceiptId,
                Quantity = 50,
                State = LpnState.IN_STOCK,
                InboundTime = DateTime.UtcNow.AddDays(-45),
                StorageLocation = "LOC-AGE",
                CreatedAt = DateTime.UtcNow.AddDays(-45)
            };

            var lpnNew = new Lpn
            {
                LpnId = Guid.NewGuid(),
                LpnCode = "LPN-NEW",
                OrderId = orderNew.OrderId,
                ReceiptId = receiptNew.ReceiptId,
                Quantity = 100,
                State = LpnState.IN_STOCK,
                InboundTime = DateTime.UtcNow.AddDays(-5),
                StorageLocation = "LOC-AGE",
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            };

            _db.Lpns.AddRange(lpnOld, lpnNew);
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
            var wh = new Warehouse { WarehouseId = warehouseId, WarehouseCode = "WH-TEMP", WarehouseName = "Temp WH", WarehouseType = "COLD", Address = "123 Cold St", Status = "ACTIVE", MaxPallets = 100, DefaultMinTemp = 10m, DefaultMaxTemp = 15m };
            _db.Warehouses.Add(wh);

            var freezerWh = new Warehouse { WarehouseId = Guid.NewGuid(), WarehouseCode = "WH-FREEZER", WarehouseName = "Freezer WH", WarehouseType = "COLD", Address = "124 Cold St", Status = "ACTIVE", MaxPallets = 100, DefaultMinTemp = -20m, DefaultMaxTemp = -15m };
            _db.Warehouses.Add(freezerWh);

            var order1 = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "ITEM-TOO-HOT", ItemName = "Chilled Item", PackingType = "BOX", Category = "FOOD", Quantity = 10, TempCondition = "COLD", Status = "ASSIGNED" };
            var order2 = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "ITEM-TOO-COLD", ItemName = "Chilled Item 2", PackingType = "BOX", Category = "FOOD", Quantity = 10, TempCondition = "COLD", Status = "ASSIGNED" };
            var order3 = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "ITEM-OK", ItemName = "Frozen Item", PackingType = "BOX", Category = "FOOD", Quantity = 10, TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.AddRange(order1, order2, order3);

            var receipt1 = new WarehouseReceipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-1", OrderId = order1.OrderId, WarehouseId = warehouseId, ReceiptType = "INBOUND", DelivererName = "Del1", CreatedAt = DateTime.UtcNow };
            var receipt2 = new WarehouseReceipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-2", OrderId = order2.OrderId, WarehouseId = warehouseId, ReceiptType = "INBOUND", DelivererName = "Del2", CreatedAt = DateTime.UtcNow };
            var receipt3 = new WarehouseReceipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-3", OrderId = order3.OrderId, WarehouseId = freezerWh.WarehouseId, ReceiptType = "INBOUND", DelivererName = "Del3", CreatedAt = DateTime.UtcNow };
            _db.WarehouseReceipts.AddRange(receipt1, receipt2, receipt3);

            // Lpn 1: Requires 5 (RequiredTemperature = 5), stored in LOC-HOT -> Should be flagged (Too hot)
            var lpnTooHot = new Lpn
            {
                LpnId = Guid.NewGuid(),
                LpnCode = "LPN-TOO-HOT",
                OrderId = order1.OrderId,
                ReceiptId = receipt1.ReceiptId,
                Quantity = 50,
                State = LpnState.IN_STOCK,
                RequiredTemperature = 5m,
                StorageLocation = "LOC-HOT",
                CreatedAt = DateTime.UtcNow
            };

            // Lpn 2: Requires 5, stored in LOC-FREEZER -> Should be flagged (Too cold)
            var lpnTooCold = new Lpn
            {
                LpnId = Guid.NewGuid(),
                LpnCode = "LPN-TOO-COLD",
                OrderId = order2.OrderId,
                ReceiptId = receipt2.ReceiptId,
                Quantity = 50,
                State = LpnState.IN_STOCK,
                RequiredTemperature = 5m,
                StorageLocation = "LOC-FREEZER",
                CreatedAt = DateTime.UtcNow
            };

            // Lpn 3: Requires -18, stored in LOC-FREEZER -> Compatible, should NOT be flagged
            var lpnCompatible = new Lpn
            {
                LpnId = Guid.NewGuid(),
                LpnCode = "LPN-OK",
                OrderId = order3.OrderId,
                ReceiptId = receipt3.ReceiptId,
                Quantity = 50,
                State = LpnState.IN_STOCK,
                RequiredTemperature = -18m,
                StorageLocation = "LOC-FREEZER",
                CreatedAt = DateTime.UtcNow
            };

            _db.Lpns.AddRange(lpnTooHot, lpnTooCold, lpnCompatible);
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
        public async Task GetWarehouseUtilizationAsync_ShouldReturnCorrectMetrics_WhenZonesExist()
        {
            // Arrange
            var warehouseId = Guid.NewGuid();
            var wh = new Warehouse { WarehouseId = warehouseId, WarehouseCode = "WH-UTIL", WarehouseName = "Util WH", WarehouseType = "COLD", Address = "123 Util St", Status = "ACTIVE", MaxPallets = 80, CurrentPallets = 25 };
            _db.Warehouses.Add(wh);
            await _db.SaveChangesAsync();

            // Act
            var response = await _analysisService.GetWarehouseUtilizationAsync(warehouseId);

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Equal(80, response.Data.MaxPallets);
            Assert.Equal(25, response.Data.CurrentPallets);
            Assert.Equal(0.3125, response.Data.WarehouseOccupancyRate);
            Assert.Empty(response.Data.ZoneOccupancyRates);
        }
    }
}
